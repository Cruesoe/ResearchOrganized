using System;
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
    /// decisions are made by <see cref="TabLayout"/>, which knows nothing about
    /// RimWorld and is covered by the test project.
    /// </summary>
    public static class ResearchOrganizedLayout
    {
        /// <summary>
        /// Node Research (ferny.noderesearch) generates one of these per tech level - a node
        /// with no in-game function beyond "advance to the next era", meant to read as that
        /// era's last item regardless of what prerequisites it happens to carry. Matched by
        /// name rather than a reference to that mod's assembly, so this works whether or not
        /// it is installed.
        /// </summary>
        private const string EraCapstonePrefix = "BRM_Emergence_";

        private static readonly Dictionary<ResearchProjectDef, bool> capstoneCache =
            new Dictionary<ResearchProjectDef, bool>();

        /// <summary>Blank columns between two era blocks on the combined tab.</summary>
        private const int EraGapColumns = 1;

        /// <summary>
        /// A project's era block on the combined tab, lowest drawn first. Projects with no tech
        /// level would otherwise sort before Animal, so they go last, the way the Miscellaneous
        /// tab does.
        /// </summary>
        public static int EraBucket(ResearchProjectDef def)
        {
            return def.techLevel == TechLevel.Undefined ? int.MaxValue : (int)def.techLevel;
        }

        /// <summary>Prerequisite edges between projects in <paramref name="tabNodes"/> only.</summary>
        private static LayoutGraph BuildGraph(List<ResearchProjectDef> tabNodes)
        {
            var indexOf = new Dictionary<ResearchProjectDef, int>(tabNodes.Count);
            for (int i = 0; i < tabNodes.Count; i++) indexOf[tabNodes[i]] = i;

            var graph = new LayoutGraph(tabNodes.Count);
            for (int i = 0; i < tabNodes.Count; i++)
            {
                foreach (var prereq in GetDirectPrereqs(tabNodes[i]))
                {
                    if (indexOf.TryGetValue(prereq, out int parentIndex))
                        graph.AddEdge(parentIndex, i, HasDrawnConnector(tabNodes[i], prereq));
                }
            }
            return graph;
        }

        /// <summary>
        /// Whether the research window draws a line for this link. ListProjects walks
        /// <see cref="ResearchProjectDef.prerequisites"/> and nothing else, so a hidden
        /// prerequisite - or a virtual link, which is not on the def at all - orders the two
        /// cards with nothing drawn between them.
        /// </summary>
        private static bool HasDrawnConnector(ResearchProjectDef def, ResearchProjectDef prereq)
        {
            return def.prerequisites != null && def.prerequisites.Contains(prereq);
        }

        public static bool IsEraCapstone(ResearchProjectDef def)
        {
            if (def == null) return false;
            if (capstoneCache.TryGetValue(def, out bool cached)) return cached;

            bool isCapstone = def.defName != null && def.defName.StartsWith(EraCapstonePrefix, System.StringComparison.Ordinal);
            return capstoneCache[def] = isCapstone;
        }

        /// <summary>
        /// Node Research marks its foundation technologies with a "ResearchFoundationExtension"
        /// mod extension. Matched by type name rather than a reference to that mod's assembly,
        /// so this works whether or not it is installed.
        /// </summary>
        private const string FoundationExtensionName = "ResearchFoundationExtension";

        private static readonly Dictionary<ResearchProjectDef, bool> foundationCache =
            new Dictionary<ResearchProjectDef, bool>();

        public static bool IsFoundationTech(ResearchProjectDef def)
        {
            if (def == null) return false;
            if (foundationCache.TryGetValue(def, out bool cached)) return cached;

            bool isFoundation = false;
            List<DefModExtension> extensions = def.modExtensions;
            if (extensions != null)
            {
                for (int i = 0; i < extensions.Count; i++)
                {
                    if (extensions[i]?.GetType().Name == FoundationExtensionName)
                    {
                        isFoundation = true;
                        break;
                    }
                }
            }

            foundationCache[def] = isFoundation;
            return isFoundation;
        }

        private static readonly Dictionary<ResearchProjectDef, List<ResearchProjectDef>> cachedPrereqs =
            new Dictionary<ResearchProjectDef, List<ResearchProjectDef>>();

        /// <summary>Projects sitting on a dependency cycle. Drawn with a red border.</summary>
        public static HashSet<ResearchProjectDef> cyclicNodes = new HashSet<ResearchProjectDef>();

        public static void ClearCaches()
        {
            cachedPrereqs.Clear();
            cyclicNodes.Clear();
            foundationCache.Clear();
            capstoneCache.Clear();
        }

        /// <summary>
        /// Lays out one tab. Only prerequisites between two projects on this same tab become
        /// edges; a prerequisite living on another tab cannot constrain a position here.
        ///
        /// <paramref name="anchors"/> and <paramref name="anchorOrder"/> are found per tab, over
        /// that tab's own links only, so a hub is recognised from what follows it here and
        /// nothing on another tab can shift where it is placed.
        /// </summary>
        /// <param name="eraBuckets">Lays each tech level out as a block of its own, left to
        /// right, with a blank column between blocks - the single tab "Combine All Tabs" produces.</param>
        public static void ApplyLayout(List<ResearchProjectDef> tabNodes, string tabName,
            HashSet<ResearchProjectDef> anchors, Dictionary<ResearchProjectDef, int> anchorOrder,
            bool eraBuckets = false)
        {
            if (tabNodes == null || tabNodes.Count == 0) return;

            var graph = BuildGraph(tabNodes);

            var options = BuildOptions(tabName);
            options.epoch = new int[tabNodes.Count];
            options.isAnchor = new bool[tabNodes.Count];
            options.anchorOrder = new int[tabNodes.Count];
            options.isCapstone = new bool[tabNodes.Count];

            for (int i = 0; i < tabNodes.Count; i++)
            {
                options.epoch[i] = (int)tabNodes[i].techLevel;
                options.isAnchor[i] = anchors.Contains(tabNodes[i]);
                anchorOrder.TryGetValue(tabNodes[i], out options.anchorOrder[i]);
                options.isCapstone[i] = IsEraCapstone(tabNodes[i]);
            }
            options.tieRank = BuildTieRank(tabNodes);

            LayoutResult result;
            if (eraBuckets)
            {
                var bucket = new int[tabNodes.Count];
                for (int i = 0; i < tabNodes.Count; i++) bucket[i] = EraBucket(tabNodes[i]);
                result = TabLayout.ComputeBuckets(graph, options, bucket, EraGapColumns);
            }
            else
            {
                result = TabLayout.Compute(graph, options);
            }

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
        /// projects to the left where the eye starts. Ties settle on defName so a run does not
        /// depend on def load order.
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
