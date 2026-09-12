using System;
using System.Collections.Generic;
using System.IO;
using ResearchOrganized.Layout;

namespace ResearchOrganized.Tests
{
    internal static class RepresentativeFixtures
    {
        /// <summary>The complete VFE Tribals 1.6 Basics dependency graph (13 projects).</summary>
        public static LayoutGraph VfeTribalsBasics()
        {
            var graph = new LayoutGraph(13);
            graph.AddEdge(0, 1);  // Fire -> Agriculture
            graph.AddEdge(1, 2);  // Agriculture -> Cultivation
            graph.AddEdge(2, 3);  // Cultivation -> Medicine
            graph.AddEdge(0, 4);  // Fire -> Animal handling
            graph.AddEdge(0, 5);  // Fire -> Mining
            graph.AddEdge(5, 6);  // Mining -> Construction
            graph.AddEdge(6, 7);  // Construction -> Furniture
            graph.AddEdge(0, 8);  // Fire -> Tribalwear
            graph.AddEdge(0, 9);  // Fire -> Hunting
            graph.AddEdge(9, 10); // Hunting -> Weapons
            graph.AddEdge(10, 11);// Weapons -> Bow
            graph.AddEdge(3, 12); // Medicine -> Culture
            graph.AddEdge(8, 12); // Tribalwear -> Culture
            graph.AddEdge(7, 12); // Furniture -> Culture
            graph.AddEdge(4, 12); // Animal handling -> Culture
            graph.AddEdge(11, 12);// Bow -> Culture
            return graph;
        }

        /// <summary>Loads one tech-level slice from the checked-in active-modlist research dump.</summary>
        public static LayoutGraph LoadTechLevel(string techLevel, out int projectCount)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "research-graph.txt");
            string[] lines = File.ReadAllLines(path);
            var selected = new List<string>();
            var prerequisites = new Dictionary<string, string[]>();

            foreach (string line in lines)
            {
                string[] fields = line.Split('|');
                if (fields.Length < 4 || !string.Equals(fields[1], techLevel, StringComparison.OrdinalIgnoreCase)) continue;
                selected.Add(fields[0]);
                prerequisites[fields[0]] = fields[3].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            }

            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < selected.Count; i++) index[selected[i]] = i;
            var graph = new LayoutGraph(selected.Count);
            for (int child = 0; child < selected.Count; child++)
            {
                foreach (string prerequisite in prerequisites[selected[child]])
                    if (index.TryGetValue(prerequisite, out int parent)) graph.AddEdge(parent, child);
            }

            projectCount = selected.Count;
            return graph;
        }
    }
}
