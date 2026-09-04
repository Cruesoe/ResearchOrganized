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

            var tieRank = new int[tab.Count];
            for (int i = 0; i < tab.Count; i++) tieRank[i] = i; // already cheapest-first from the sort above

            const int minorAnchorThreshold = 3;
            const int majorAnchorThreshold = 7;
            bool[] isAnchor;
            int[] anchorOrder;
            FindAnchors(tab, graph, minorAnchorThreshold, majorAnchorThreshold, out isAnchor, out anchorOrder);

            var epoch = new int[tab.Count]; // one tech level per Dump run, so every project shares an epoch

            var options = new LayoutOptions
            {
                maxNodesPerColumn = cap,
                epoch = epoch,
                isAnchor = isAnchor,
                anchorOrder = anchorOrder,
                tieRank = tieRank
            };
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

            // What a reader actually follows: is a project sitting next to at least one of
            // the things it needs? A project with several prerequisites can only ever be
            // adjacent to one of them, so counting every link understates a good layout.
            int withParents = 0, besideOne = 0, worstNearest = 0;
            string worstOrphan = "";
            for (int i = 0; i < tab.Count; i++)
            {
                var parents = graph.ParentsOf(i);
                if (parents.Count == 0) continue;
                withParents++;

                int nearest = int.MaxValue;
                for (int p = 0; p < parents.Count; p++)
                {
                    int span = result.Layer[i] - result.Layer[parents[p]];
                    if (span < nearest) nearest = span;
                }
                if (nearest == 1) besideOne++;
                if (nearest > worstNearest) { worstNearest = nearest; worstOrphan = tab[i].Name; }
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
            Console.WriteLine(string.Format("projects beside a prerequisite: {0} of {1} ({2:0.#}%)",
                besideOne, withParents, withParents == 0 ? 0 : 100.0 * besideOne / withParents));
            Console.WriteLine(string.Format("furthest from its nearest prerequisite: {0} columns  {1}", worstNearest, worstOrphan));
            Console.WriteLine(string.Format("crossings: {0}", result.Crossings));

            Console.Write("column heights:");
            for (int c = 0; c < columns; c++)
            {
                perColumn.TryGetValue(c, out int held);
                Console.Write(" " + held);
            }
            Console.WriteLine("   (cap " + options.maxNodesPerColumn + ")");
        }

        /// <summary>
        /// Mirrors ResearchOrganizedMain.FindAnchors: bottom-up by ancestor depth, a project
        /// with enough non-anchor children becomes a hub, and its own children then stop
        /// counting toward whether something upstream of it also qualifies.
        /// </summary>
        private static void FindAnchors(List<Project> tab, LayoutGraph graph, int minorThreshold, int majorThreshold,
            out bool[] isAnchor, out int[] anchorOrder)
        {
            int count = tab.Count;
            var ancestorCounts = new int[count];
            for (int i = 0; i < count; i++) ancestorCounts[i] = CountAncestors(graph, i);

            var bottomUp = new List<int>(count);
            for (int i = 0; i < count; i++) bottomUp.Add(i);
            bottomUp.Sort((a, b) => ancestorCounts[b].CompareTo(ancestorCounts[a]));

            var majorAnchors = new HashSet<int>();
            var minorAnchors = new HashSet<int>();

            foreach (int node in bottomUp)
            {
                var children = graph.ChildrenOf(node);
                if (children.Count == 0) continue;

                int nonMajor = 0;
                for (int i = 0; i < children.Count; i++) if (!majorAnchors.Contains(children[i])) nonMajor++;
                if (majorThreshold > 0 && nonMajor >= majorThreshold) { majorAnchors.Add(node); continue; }

                int nonAnchor = 0;
                for (int i = 0; i < children.Count; i++) if (!majorAnchors.Contains(children[i]) && !minorAnchors.Contains(children[i])) nonAnchor++;
                if (minorThreshold > 0 && nonAnchor >= minorThreshold) minorAnchors.Add(node);
            }

            isAnchor = new bool[count];
            foreach (int a in majorAnchors) isAnchor[a] = true;
            foreach (int a in minorAnchors) isAnchor[a] = true;

            var anchorList = new List<int>();
            for (int i = 0; i < count; i++) if (isAnchor[i]) anchorList.Add(i);
            anchorList.Sort((a, b) =>
            {
                int byDepth = ancestorCounts[a].CompareTo(ancestorCounts[b]);
                if (byDepth != 0) return byDepth;
                int byTier = (majorAnchors.Contains(a) ? 0 : 1).CompareTo(majorAnchors.Contains(b) ? 0 : 1);
                if (byTier != 0) return byTier;
                return string.CompareOrdinal(tab[a].Name, tab[b].Name);
            });

            anchorOrder = new int[count];
            for (int position = 0; position < anchorList.Count; position++) anchorOrder[anchorList[position]] = position;
        }

        private static int CountAncestors(LayoutGraph graph, int node)
        {
            var ancestors = new HashSet<int>();
            var stack = new Stack<int>();
            stack.Push(node);
            while (stack.Count > 0)
            {
                int current = stack.Pop();
                var parents = graph.ParentsOf(current);
                for (int i = 0; i < parents.Count; i++) if (ancestors.Add(parents[i])) stack.Push(parents[i]);
            }
            return ancestors.Count;
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
