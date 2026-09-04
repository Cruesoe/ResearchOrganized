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
        ///
        /// <paramref name="anchors"/> and <paramref name="anchorOrder"/> come from
        /// <see cref="ResearchOrganizedMain.OrganizeTabsAndLayout"/>, which finds them once
        /// across every project so a hub is recognised the same way regardless of which tab
        /// it ends up on.
        /// </summary>
        public static void ApplyLayout(List<ResearchProjectDef> tabNodes, string tabName,
            HashSet<ResearchProjectDef> anchors, Dictionary<ResearchProjectDef, int> anchorOrder)
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
            options.epoch = new int[tabNodes.Count];
            options.isAnchor = new bool[tabNodes.Count];
            options.anchorOrder = new int[tabNodes.Count];
            options.tieRank = new int[tabNodes.Count];

            for (int i = 0; i < tabNodes.Count; i++)
            {
                options.epoch[i] = (int)tabNodes[i].techLevel;
                options.isAnchor[i] = anchors.Contains(tabNodes[i]);
                anchorOrder.TryGetValue(tabNodes[i], out options.anchorOrder[i]);
            }
            options.tieRank = BuildTieRank(tabNodes);

            var result = TabLayout.Compute(graph, options);

            for (int i = 0; i < tabNodes.Count; i++)
            {
                tabNodes[i].researchViewX = result.X[i];
                tabNodes[i].researchViewY = result.Y[i];
            }

            ReportCycles(tabNodes, tabName, result);
        }

        /// <summary>
        /// Tie-break for otherwise-equal placement choices, lowest first: cheapest project
        /// first, since that is roughly the order a colony researches in and keeps the early
        /// projects to the left where the eye starts.
        /// </summary>
        private static int[] BuildTieRank(List<ResearchProjectDef> tabNodes)
        {
            var order = new List<int>(tabNodes.Count);
            for (int i = 0; i < tabNodes.Count; i++) order.Add(i);

            order.Sort(delegate (int a, int b)
            {
                int costCompare = tabNodes[a].baseCost.CompareTo(tabNodes[b].baseCost);
                if (costCompare != 0) return costCompare;
                return string.Compare(tabNodes[a].defName, tabNodes[b].defName, System.StringComparison.Ordinal);
            });

            var rank = new int[tabNodes.Count];
            for (int position = 0; position < order.Count; position++) rank[order[position]] = position;
            return rank;
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

        /// <summary>Every project this one depends on, directly or transitively, for layout purposes.</summary>
        public static HashSet<ResearchProjectDef> GetAllAncestors(ResearchProjectDef node)
        {
            var ancestors = new HashSet<ResearchProjectDef>();
            var stack = new Stack<ResearchProjectDef>();
            stack.Push(node);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                foreach (var pre in GetDirectPrereqs(current))
                {
                    if (ancestors.Add(pre)) stack.Push(pre);
                }
            }
            return ancestors;
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
