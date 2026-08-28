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
    /// Everything game-specific lives here: reading prerequisites, deciding which edges are
    /// in scope for a tab, and writing coordinates back onto the defs. The actual layout
    /// decisions are made by <see cref="SugiyamaLayout"/>, which knows nothing about RimWorld
    /// and is covered by the test project.
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
            var result = SugiyamaLayout.Compute(graph, options);

            for (int i = 0; i < tabNodes.Count; i++)
            {
                tabNodes[i].researchViewX = result.X[i];
                tabNodes[i].researchViewY = result.Y[i];
            }

            ReportCycles(tabNodes, tabName, result);
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
