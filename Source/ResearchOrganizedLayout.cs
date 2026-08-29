using System.Collections.Generic;
using System.Linq;
using RimWorld;
using ResearchOrganized.Layout;
using Verse;

namespace ResearchOrganized
{
    /// <summary>
    /// Adapter between RimWorld's research defs and the pure layout core in
    /// <see cref="ResearchOrganized.Layout"/>.
    ///
    /// Everything game-specific lives here: reading prerequisites, deciding which projects
    /// matter most on a tab, and writing coordinates back onto the defs. The layout
    /// decisions are made by <see cref="SugiyamaLayout"/>, which knows nothing about
    /// RimWorld and is covered by the test project.
    /// </summary>
    public static class ResearchOrganizedLayout
    {
        private static readonly Dictionary<ResearchProjectDef, List<ResearchProjectDef>> cachedPrereqs =
            new Dictionary<ResearchProjectDef, List<ResearchProjectDef>>();


        /// <summary>Projects sitting on a dependency cycle. Drawn with a red border.</summary>
        public static HashSet<ResearchProjectDef> cyclicNodes = new HashSet<ResearchProjectDef>();

        public static void ClearCaches()
        {
            cachedPrereqs.Clear();
            cyclicNodes.Clear();
        }

        /// <summary>
        /// Lays out one tab. Only prerequisites between two projects on this same tab become
        /// edges; a prerequisite living on another tab cannot constrain a position here.
        /// </summary>
        public static void ApplyLayout(List<ResearchProjectDef> tabNodes, string tabName)
        {
            if (tabNodes == null || tabNodes.Count == 0) return;

            var indexOf = new Dictionary<ResearchProjectDef, int>(tabNodes.Count);
            for (int i = 0; i < tabNodes.Count; i++) indexOf[tabNodes[i]] = i;

            var graph = new LayoutGraph(tabNodes.Count);
            for (int i = 0; i < tabNodes.Count; i++)
            {
                foreach (var prereq in GetDirectPrereqs(tabNodes[i]))
                {
                    int parentIndex;
                    if (indexOf.TryGetValue(prereq, out parentIndex)) graph.AddEdge(parentIndex, i);
                }
            }

            var options = BuildOptions(tabName);
            options.rank = BuildRank(tabNodes, graph);
            // No gateway pinning. The rule that "something on a later tab depends on this"
            // matches almost every project that leads into the next era - recurve bow, for
            // one, which then sat isolated in a far column of the neolithic tab. Worse, it
            // could not match microelectronics, the case it was written for, because that has
            // followers on its own tab. Marking a tab's concluding project needs a rule based
            // on what it unlocks, not on who depends on it.

            var result = TabLayout.Compute(graph, options);

            for (int i = 0; i < tabNodes.Count; i++)
            {
                tabNodes[i].researchViewX = result.X[i];
                tabNodes[i].researchViewY = result.Y[i];
            }

            ReportCycles(tabNodes, tabName, result);
        }

        /// <summary>
        /// Placement priority within a column, lowest first. Two ideas, in order:
        ///
        /// 1. Anchors - projects with many direct children on this tab - come first. They
        ///    are the hubs the rest of the tab hangs off, so they must hold their own depth
        ///    column and never be the ones pushed aside when a column fills up.
        /// 2. Then cheapest first, because that is roughly the order a colony researches in,
        ///    and it keeps the early projects to the left where the eye starts.
        ///
        /// Counting only direct in-tab children, as the original did, is what makes this
        /// work for modded trees too: it needs no list of known project names.
        /// </summary>
        private static int[] BuildRank(List<ResearchProjectDef> tabNodes, LayoutGraph graph)
        {
            int minorThreshold = ResearchOrganizedMod.settings.minorAnchorChildThreshold;
            int majorThreshold = ResearchOrganizedMod.settings.majorAnchorChildThreshold;

            var order = new List<int>(tabNodes.Count);
            for (int i = 0; i < tabNodes.Count; i++) order.Add(i);

            order.Sort(delegate (int a, int b)
            {
                int tierA = AnchorTier(graph.ChildrenOf(a).Count, minorThreshold, majorThreshold);
                int tierB = AnchorTier(graph.ChildrenOf(b).Count, minorThreshold, majorThreshold);
                if (tierA != tierB) return tierA.CompareTo(tierB);

                int childCompare = graph.ChildrenOf(b).Count.CompareTo(graph.ChildrenOf(a).Count);
                if (childCompare != 0) return childCompare;

                int costCompare = tabNodes[a].baseCost.CompareTo(tabNodes[b].baseCost);
                if (costCompare != 0) return costCompare;

                return string.Compare(tabNodes[a].defName, tabNodes[b].defName, System.StringComparison.Ordinal);
            });

            var rank = new int[tabNodes.Count];
            for (int position = 0; position < order.Count; position++) rank[order[position]] = position;
            return rank;
        }

        private static int AnchorTier(int directChildren, int minorThreshold, int majorThreshold)
        {
            if (majorThreshold > 0 && directChildren >= majorThreshold) return 0;
            if (minorThreshold > 0 && directChildren >= minorThreshold) return 1;
            return 2;
        }

        private static LayoutOptions BuildOptions(string tabName)
        {
            var options = new LayoutOptions
            {
                xStep = ResearchOrganizedMain.GlobalXStep,
                yStep = ResearchOrganizedMain.GlobalYStep,
                maxNodesPerColumn = ResearchOrganizedMain.GlobalMaxNodesPerColumn
            };

            LayoutConfig perTab;
            if (tabName != null && ResearchOrganizedMain.TabLayouts.TryGetValue(tabName, out perTab))
            {
                options.xStep = perTab.xStep;
                options.yStep = perTab.yStep;
                options.maxNodesPerColumn = perTab.maxNodesPerColumn;
            }

            return options;
        }

        private static void ReportCycles(List<ResearchProjectDef> tabNodes, string tabName, LayoutResult result)
        {
            if (result.ReversedEdges.Count == 0) return;

            foreach (int index in result.NodesInCycles)
            {
                if (index >= 0 && index < tabNodes.Count) cyclicNodes.Add(tabNodes[index]);
            }

            var described = result.ReversedEdges
                .Select(e => tabNodes[e.Parent].defName + " -> " + tabNodes[e.Child].defName);

            Log.Warning($"[Research: Organized] Circular research dependencies on tab '{tabName}'. " +
                        $"Reversed for layout purposes: [{string.Join(", ", described)}]. " +
                        $"Affected projects are outlined in red. This usually means a mod conflict or malformed XML.");
        }

        /// <summary>
        /// A project's prerequisites for layout purposes: its real ones, its hidden ones, and
        /// any virtual links from config. Virtual links steer positioning only - they are not
        /// added to the def, so they never affect what you actually have to research.
        /// </summary>
        public static List<ResearchProjectDef> GetDirectPrereqs(ResearchProjectDef def)
        {
            List<ResearchProjectDef> cached;
            if (cachedPrereqs.TryGetValue(def, out cached)) return cached;

            var combined = new HashSet<ResearchProjectDef>(def.prerequisites ?? new List<ResearchProjectDef>());
            if (def.hiddenPrerequisites != null)
            {
                foreach (var prereq in def.hiddenPrerequisites) combined.Add(prereq);
            }

            List<ResearchProjectDef> virtualPrereqs;
            if (ResearchOrganizedMain.VirtualPrereqsCache.TryGetValue(def, out virtualPrereqs))
            {
                foreach (var prereq in virtualPrereqs) combined.Add(prereq);
            }

            combined.Remove(def);
            return cachedPrereqs[def] = combined.ToList();
        }
    }
}
