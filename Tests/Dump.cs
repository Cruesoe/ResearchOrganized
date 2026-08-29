using System;
using System.Collections.Generic;
using System.IO;
using ResearchOrganized.Layout;

namespace ResearchOrganized.Tests
{
    /// <summary>
    /// Renders a real research tab as text so a layout can be judged without launching
    /// RimWorld. Reading a grid here takes seconds; the alternative has been asking someone
    /// to load a 200 mod save and send a screenshot.
    ///
    ///   ResearchOrganized.Tests.exe dump &lt;graph file&gt; &lt;techLevel&gt; [cap]
    /// </summary>
    internal static class Dump
    {
        private sealed class Project
        {
            public string Name;
            public string TechLevel;
            public int Cost;
            public List<string> Prereqs = new List<string>();
        }

        public static int Run(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("usage: dump <graph file> <techLevel> [maxNodesPerColumn]");
                return 2;
            }

            string path = args[1];
            string wanted = args[2];
            int cap = args.Length > 3 ? int.Parse(args[3]) : 10;

            var all = Load(path);

            var tab = new List<Project>();
            foreach (var project in all.Values)
            {
                if (string.Equals(project.TechLevel, wanted, StringComparison.OrdinalIgnoreCase)) tab.Add(project);
            }

            if (tab.Count == 0)
            {
                Console.WriteLine("no projects at tech level " + wanted);
                return 1;
            }

            // Cheapest first, matching how the mod ranks within a group.
            tab.Sort(delegate (Project a, Project b)
            {
                if (a.Cost != b.Cost) return a.Cost.CompareTo(b.Cost);
                return string.CompareOrdinal(a.Name, b.Name);
            });

            var indexOf = new Dictionary<string, int>();
            for (int i = 0; i < tab.Count; i++) indexOf[tab[i].Name] = i;

            var graph = new LayoutGraph(tab.Count);
            int inTabEdges = 0;
            for (int i = 0; i < tab.Count; i++)
            {
                foreach (string prereq in tab[i].Prereqs)
                {
                    int parent;
                    if (indexOf.TryGetValue(prereq, out parent) && graph.AddEdge(parent, i)) inTabEdges++;
                }
            }

            var rank = new int[tab.Count];
            for (int i = 0; i < tab.Count; i++) rank[i] = i;

            var options = new LayoutOptions { maxNodesPerColumn = cap, rank = rank };
            var result = TabLayout.Compute(graph, options);

            Render(tab, graph, result, options);
            Report(tab, graph, result, options, inTabEdges);
            return 0;
        }

        private static void Render(List<Project> tab, LayoutGraph graph, LayoutResult result, LayoutOptions options)
        {
            int columns = 0, rows = 0;
            var cells = new Dictionary<long, int>();
            for (int i = 0; i < tab.Count; i++)
            {
                int c = result.Layer[i];
                int r = (int)Math.Round(result.Y[i] / options.yStep);
                cells[((long)c << 32) ^ (uint)r] = i;
                if (c + 1 > columns) columns = c + 1;
                if (r + 1 > rows) rows = r + 1;
            }

            const int width = 19;
            Console.WriteLine();
            Console.Write("     ");
            for (int c = 0; c < columns; c++) Console.Write(("col " + c).PadRight(width));
            Console.WriteLine();

            for (int r = 0; r < rows; r++)
            {
                Console.Write(r.ToString().PadLeft(3) + "  ");
                for (int c = 0; c < columns; c++)
                {
                    int node;
                    if (cells.TryGetValue(((long)c << 32) ^ (uint)r, out node))
                    {
                        string name = tab[node].Name;
                        if (name.Length > width - 2) name = name.Substring(0, width - 2);
                        Console.Write(name.PadRight(width));
                    }
                    else Console.Write(new string(' ', width));
                }
                Console.WriteLine();
            }
        }

        private static void Report(List<Project> tab, LayoutGraph graph, LayoutResult result, LayoutOptions options, int inTabEdges)
        {
            int columns = 0, rows = 0;
            var perColumn = new Dictionary<int, int>();
            for (int i = 0; i < tab.Count; i++)
            {
                int c = result.Layer[i];
                int r = (int)Math.Round(result.Y[i] / options.yStep);
                perColumn.TryGetValue(c, out int held);
                perColumn[c] = held + 1;
                if (c + 1 > columns) columns = c + 1;
                if (r + 1 > rows) rows = r + 1;
            }

            int worstSpan = 0;
            string worstEdge = "";
            int adjacent = 0;
            foreach (var edge in graph.AllEdges())
            {
                int span = result.Layer[edge.Child] - result.Layer[edge.Parent];
                if (span == 1) adjacent++;
                if (span > worstSpan) { worstSpan = span; worstEdge = tab[edge.Parent].Name + " -> " + tab[edge.Child].Name; }
            }

            int occupied = 0;
            foreach (var pair in perColumn) occupied += pair.Value;
            double fill = columns == 0 ? 0 : (double)occupied / (columns * rows);

            Console.WriteLine();
            Console.WriteLine(string.Format("{0} projects, {1} in-tab links", tab.Count, inTabEdges));
            Console.WriteLine(string.Format("grid {0} columns x {1} rows, {2:0.#}% of cells filled", columns, rows, fill * 100));
            Console.WriteLine(string.Format("links spanning one column: {0} of {1} ({2:0.#}%)",
                adjacent, inTabEdges, inTabEdges == 0 ? 0 : 100.0 * adjacent / inTabEdges));
            Console.WriteLine(string.Format("widest link: {0} columns  {1}", worstSpan, worstEdge));
            Console.WriteLine(string.Format("crossings: {0}", result.Crossings));

            Console.Write("column heights:");
            for (int c = 0; c < columns; c++)
            {
                perColumn.TryGetValue(c, out int held);
                Console.Write(" " + held);
            }
            Console.WriteLine("   (cap " + options.maxNodesPerColumn + ")");
        }

        private static Dictionary<string, Project> Load(string path)
        {
            var all = new Dictionary<string, Project>();
            foreach (string line in File.ReadAllLines(path))
            {
                var parts = line.Split('|');
                if (parts.Length < 4 || parts[0].Length == 0) continue;

                var project = new Project { Name = parts[0], TechLevel = parts[1] };
                int.TryParse(parts[2], out project.Cost);
                if (parts[3].Length > 0) project.Prereqs.AddRange(parts[3].Split(','));
                all[project.Name] = project;
            }
            return all;
        }
    }
}
