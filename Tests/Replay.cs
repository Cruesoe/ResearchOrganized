using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using ResearchOrganized.Layout;

namespace ResearchOrganized.Tests
{
    /// <summary>
    /// Rebuilds a tab from an in-game layout log (the "Write Layout Log" setting) and lays it out
    /// again with the current code, so a change can be judged against the real tree.
    ///
    ///   ResearchOrganized.Tests.exe replay &lt;Player.log&gt; &lt;tabName&gt; [maxNodesPerColumn]
    /// </summary>
    internal static class Replay
    {
        private static readonly Regex HeaderPattern = new Regex(@"Layout log for tab '([^']+)' \(\d+ projects;.* max (\d+) per column");
        private static readonly Regex AnchorsPattern = new Regex(@"^-- epoch (\d+): .*anchors in order: (.*)$");
        private static readonly Regex PositionPattern = new Regex(@"^\s*(\d+)\s+(\d+) \| ([^|]+) \| [^|]* \| ([^|]*) \| ([^|]*) \| (.*?)(?: \| (\d+))?\s*$");
        private static readonly Regex PrerequisitePattern = new Regex(@"([^,\s(]+)\(([^)]*)\)");

        private sealed class Project
        {
            public string Name;
            public int Column, Row, Cost, Epoch;
            public string Hub;
            public List<string> Prereqs = new List<string>();
        }

        public static int Run(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("usage: replay <Player.log> <tabName> [maxNodesPerColumn]");
                return 2;
            }

            string tab = args[2];
            int wantedCap = args.Length > 3 ? int.Parse(args[3]) : 0;
            var projects = new List<Project>();
            var anchorOrder = new Dictionary<string, int>();
            int cap = 0;
            if (!Load(args[1], tab, wantedCap, projects, anchorOrder, ref cap))
            {
                Console.WriteLine("no layout log for tab " + tab + (wantedCap > 0 ? " at " + wantedCap + " per column" : ""));
                return 1;
            }

            FillMissingCosts(projects);

            var index = new Dictionary<string, int>();
            for (int i = 0; i < projects.Count; i++) index[projects[i].Name] = i;
            var graph = new LayoutGraph(projects.Count);
            foreach (var project in projects)
                foreach (string prereq in project.Prereqs)
                    if (index.TryGetValue(prereq, out int parent)) graph.AddEdge(parent, index[project.Name]);

            int n = projects.Count;
            var options = new LayoutOptions
            {
                maxNodesPerColumn = cap,
                epoch = new int[n],
                isAnchor = new bool[n],
                isMajorAnchor = new bool[n],
                isFinale = new bool[n],
                anchorOrder = new int[n],
                isCapstone = new bool[n],
                tieRank = new int[n],
                nodeNames = new string[n]
            };
            for (int i = 0; i < n; i++)
            {
                var p = projects[i];
                options.epoch[i] = p.Epoch;
                options.isAnchor[i] = p.Hub == "minor" || p.Hub == "major" || p.Hub == "finale";
                options.isMajorAnchor[i] = p.Hub == "major";
                options.isFinale[i] = p.Hub == "finale";
                options.isCapstone[i] = p.Hub == "capstone";
                anchorOrder.TryGetValue(p.Name, out options.anchorOrder[i]);
                options.nodeNames[i] = p.Name;
            }
            var byCost = new List<int>();
            for (int i = 0; i < n; i++) byCost.Add(i);
            byCost.Sort((a, b) => projects[a].Cost != projects[b].Cost ? projects[a].Cost.CompareTo(projects[b].Cost) : string.CompareOrdinal(projects[a].Name, projects[b].Name));
            for (int position = 0; position < n; position++) options.tieRank[byCost[position]] = position;

            var oldColumn = new int[n];
            var oldRow = new int[n];
            for (int i = 0; i < n; i++) { oldColumn[i] = projects[i].Column; oldRow[i] = projects[i].Row; }

            var result = TabLayout.Compute(graph, options);
            var newColumn = new int[n];
            var newRow = new int[n];
            for (int i = 0; i < n; i++) { newColumn[i] = result.Layer[i]; newRow[i] = (int)Math.Round(result.Y[i] / options.yStep); }

            Console.WriteLine("== in game (from the log)");
            Report(projects, graph, options, oldColumn, oldRow);
            Console.WriteLine();
            Console.WriteLine("== current code");
            Render(projects, newColumn, newRow);
            Report(projects, graph, options, newColumn, newRow);
            return 0;
        }

        /// <summary>Reads the last layout log for <paramref name="tab"/> (at <paramref name="wantedCap"/> per column when given).</summary>
        private static bool Load(string path, string tab, int wantedCap, List<Project> projects, Dictionary<string, int> anchorOrder, ref int cap)
        {
            string[] lines = File.ReadAllLines(path);
            int start = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                var header = HeaderPattern.Match(lines[i]);
                if (!header.Success || header.Groups[1].Value != tab) continue;
                int logCap = int.Parse(header.Groups[2].Value);
                if (wantedCap > 0 && logCap != wantedCap) continue;
                start = i;
                cap = logCap;
            }
            if (start < 0) return false;

            int order = 1;
            for (int i = start + 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.TrimStart().StartsWith("at ") || HeaderPattern.IsMatch(line)) break;

                var anchors = AnchorsPattern.Match(line);
                if (anchors.Success)
                {
                    foreach (string entry in anchors.Groups[2].Value.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string name = entry.Split(' ')[0];
                        if (name != "none") anchorOrder[name] = order++;
                    }
                    continue;
                }

                var position = PositionPattern.Match(line);
                if (!position.Success) continue;
                var project = new Project
                {
                    Column = int.Parse(position.Groups[1].Value),
                    Row = int.Parse(position.Groups[2].Value),
                    Name = position.Groups[3].Value.Trim(),
                    Hub = position.Groups[5].Value.Trim(),
                    Epoch = TechLevelOrder(position.Groups[4].Value.Trim()),
                    Cost = position.Groups[7].Success ? int.Parse(position.Groups[7].Value) : -1
                };
                foreach (Match prereq in PrerequisitePattern.Matches(position.Groups[6].Value)) project.Prereqs.Add(prereq.Groups[1].Value);
                projects.Add(project);
            }
            return projects.Count > 0;
        }

        /// <summary>Older logs carry no costs: take them from the research dump, or 0 when it has none.</summary>
        private static void FillMissingCosts(List<Project> projects)
        {
            var costs = new Dictionary<string, int>();
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "research-graph.txt");
            if (File.Exists(path))
            {
                foreach (string line in File.ReadAllLines(path))
                {
                    var fields = line.Split('|');
                    if (fields.Length >= 3 && int.TryParse(fields[2], out int cost)) costs[fields[0]] = cost;
                }
            }
            foreach (var project in projects)
                if (project.Cost < 0) project.Cost = costs.TryGetValue(project.Name, out int cost) ? cost : 0;
        }

        private static int TechLevelOrder(string techLevel)
        {
            string[] levels = { "Undefined", "Animal", "Neolithic", "Medieval", "Industrial", "Spacer", "Ultra", "Archotech" };
            int index = Array.IndexOf(levels, techLevel);
            return index < 0 ? 0 : index;
        }

        private static void Render(List<Project> projects, int[] column, int[] row)
        {
            int columns = 0, rows = 0;
            var cells = new Dictionary<long, int>();
            for (int i = 0; i < projects.Count; i++)
            {
                cells[((long)column[i] << 32) ^ (uint)row[i]] = i;
                columns = Math.Max(columns, column[i] + 1);
                rows = Math.Max(rows, row[i] + 1);
            }

            const int width = 15;
            for (int r = 0; r < rows; r++)
            {
                Console.Write(r.ToString().PadLeft(3) + "  ");
                for (int c = 0; c < columns; c++)
                {
                    string name = cells.TryGetValue(((long)c << 32) ^ (uint)r, out int node) ? projects[node].Name.Replace("Ferny_", "") : "";
                    if (name.Length > width - 1) name = name.Substring(0, width - 1);
                    Console.Write(name.PadRight(width));
                }
                Console.WriteLine();
            }
        }

        private static void Report(List<Project> projects, LayoutGraph graph, LayoutOptions options, int[] column, int[] row)
        {
            int n = projects.Count, columns = 0, links = 0, adjacent = 0, long7 = 0, totalSpan = 0;
            for (int i = 0; i < n; i++) columns = Math.Max(columns, column[i] + 1);
            foreach (var edge in graph.AllEdges())
            {
                int span = column[edge.Child] - column[edge.Parent];
                links++;
                totalSpan += span;
                if (span == 1) adjacent++;
                if (span >= 7) long7++;
            }

            // A project right of a hub it does not need breaks left-to-right progression.
            var ancestors = new HashSet<int>[n];
            for (int i = 0; i < n; i++) ancestors[i] = Ancestors(graph, i);
            int misplaced = 0;
            for (int hub = 0; hub < n; hub++)
            {
                if (!options.isAnchor[hub]) continue;
                for (int i = 0; i < n; i++)
                    if (i != hub && !options.isCapstone[i] && column[i] > column[hub] && !ancestors[i].Contains(hub)) misplaced++;
            }

            var drawnRows = new int[n];
            for (int i = 0; i < n; i++) drawnRows[i] = row[i];
            int crossings = CrossingCounter.Count(graph.DrawnOnly(), column, drawnRows, options.xStep, options.yStep);

            Console.WriteLine(string.Format("{0} columns, {1} links: {2} to the next column ({3:0}%), {4} spanning 7+, average span {5:0.0}, {6} crossings",
                columns, links, adjacent, 100.0 * adjacent / Math.Max(1, links), long7, (double)totalSpan / Math.Max(1, links), crossings));
            Console.WriteLine(string.Format("project/hub pairs where the project sits right of a hub it does not need: {0}", misplaced));
            var hubs = new List<string>();
            for (int i = 0; i < n; i++) if (options.isAnchor[i]) hubs.Add(projects[i].Name + " " + column[i]);
            Console.WriteLine("hub columns, in hub order: " + string.Join(", ", hubs.ToArray()));
        }

        private static HashSet<int> Ancestors(LayoutGraph graph, int node)
        {
            var seen = new HashSet<int>();
            var stack = new Stack<int>();
            stack.Push(node);
            while (stack.Count > 0)
            {
                var parents = graph.ParentsOf(stack.Pop());
                for (int i = 0; i < parents.Count; i++) if (seen.Add(parents[i])) stack.Push(parents[i]);
            }
            return seen;
        }
    }
}
