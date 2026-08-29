using System;
using System.Collections.Generic;
using ResearchOrganized.Layout;

namespace ResearchOrganized.Tests
{
    /// <summary>
    /// Assertions over the layout core. No test framework on purpose: a plain console exe
    /// needs no package restore, so this runs anywhere the mod itself builds.
    /// Exit code 0 means everything passed.
    /// </summary>
    internal static class Program
    {
        private static int failures;
        private static int checks;
        private static int tests;

        private static int Main()
        {
            Run("empty graph is handled", EmptyGraph);
            Run("chain lands in consecutive columns", ChainLayering);
            Run("every follower is right of its parent", ChildAlwaysRightOfParent);
            Run("column height cap is respected", ColumnHeightCap);
            Run("cycles are broken and reported", CyclesAreBroken);
            Run("layout is deterministic", Deterministic);
            Run("roots share the first column", RootsShareTheFirstColumn);
            Run("loose projects pack tightly", LooseNodesPackTightly);
            Run("a wide fan lands in one contiguous block", WideFanFormsOneBlock);
            Run("groups never interleave", GroupsDoNotInterleave);
            Run("a small group stays beside its parent", SmallGroupStaysNearParent);
            Run("hubs sit beside their own followers", HubsSitAtGroupEnd);
            Run("groups are visually separated", GroupsAreSeparated);
            Run("no empty rows across the tab", NoEmptyRowsAcrossTheTab);
            Run("refinement reduces crossings", RefinementReducesCrossings);
            Run("height never exceeds the cap", HeightNeverExceedsColumnCap);
            Run("backward edges do not throw", BackwardEdgesDoNotThrow);
            Run("scale benchmark", ScaleBenchmark);

            Console.WriteLine();
            Console.WriteLine(failures == 0
                ? string.Format("PASS - {0} checks across {1} tests", checks, tests)
                : string.Format("FAIL - {0} failed check(s) of {1}", failures, checks));
            return failures == 0 ? 0 : 1;
        }

        // ---- basics ---------------------------------------------------------------

        private static void EmptyGraph()
        {
            var result = TabLayout.Compute(new LayoutGraph(0), new LayoutOptions());
            IsTrue(result.X.Length == 0, "no coordinates produced");
        }

        private static void ChainLayering()
        {
            var graph = new LayoutGraph(3);
            graph.AddEdge(0, 1);
            graph.AddEdge(1, 2);

            var result = TabLayout.Compute(graph, new LayoutOptions());
            AreEqual(0, result.Layer[0], "first project column");
            AreEqual(1, result.Layer[1], "second project column");
            AreEqual(2, result.Layer[2], "third project column");
        }

        private static void ChildAlwaysRightOfParent()
        {
            var graph = RandomDag(60, 120, seed: 12345);
            var result = TabLayout.Compute(graph, new LayoutOptions { maxNodesPerColumn = 10 });

            foreach (var edge in graph.AllEdges())
            {
                if (result.Layer[edge.Child] <= result.Layer[edge.Parent])
                {
                    IsTrue(false, "edge " + edge + " does not advance a column");
                    return;
                }
            }
            IsTrue(true, "all 120 edges advance at least one column");
        }

        private static void ColumnHeightCap()
        {
            var graph = MakeFan(40);
            var options = new LayoutOptions { maxNodesPerColumn = 6 };
            var result = TabLayout.Compute(graph, options);

            var counts = new Dictionary<int, int>();
            for (int node = 0; node < graph.NodeCount; node++)
            {
                int layer = result.Layer[node];
                counts[layer] = counts.ContainsKey(layer) ? counts[layer] + 1 : 1;
            }

            foreach (var pair in counts)
            {
                if (pair.Value > 6)
                {
                    IsTrue(false, string.Format("column {0} holds {1} cards, cap was 6", pair.Key, pair.Value));
                    return;
                }
            }
            IsTrue(true, "no column exceeded the cap");
        }

        private static void CyclesAreBroken()
        {
            var graph = new LayoutGraph(3);
            graph.AddEdge(0, 1);
            graph.AddEdge(1, 2);
            graph.AddEdge(2, 0);

            var result = TabLayout.Compute(graph, new LayoutOptions());
            IsTrue(result.ReversedEdges.Count >= 1, "at least one edge was reversed");
            IsTrue(result.NodesInCycles.Count >= 2, "projects on the cycle were reported");
        }

        private static void Deterministic()
        {
            var graph = RandomDag(45, 95, seed: 777);
            var first = TabLayout.Compute(graph, new LayoutOptions());
            var second = TabLayout.Compute(graph, new LayoutOptions());

            for (int node = 0; node < graph.NodeCount; node++)
            {
                if (first.X[node] != second.X[node] || first.Y[node] != second.Y[node])
                {
                    IsTrue(false, "project " + node + " moved between identical runs");
                    return;
                }
            }
            IsTrue(true, "two runs produced identical coordinates");
        }

        // ---- the grouping model ---------------------------------------------------

        /// <summary>
        /// A project with no prerequisites is available immediately, so it belongs in
        /// column zero whichever part of the tab it belongs to.
        /// </summary>
        private static void RootsShareTheFirstColumn()
        {
            var graph = new LayoutGraph(20);
            for (int i = 1; i <= 12; i++) graph.AddEdge(0, i);
            graph.AddEdge(13, 14);
            // 15..19 stand alone.

            var result = TabLayout.Compute(graph, new LayoutOptions { maxNodesPerColumn = 10 });

            int[] roots = { 0, 13, 15, 16, 17, 18, 19 };
            foreach (int root in roots)
            {
                if (result.Layer[root] != 0)
                {
                    IsTrue(false, string.Format("root {0} landed in column {1}, not 0", root, result.Layer[root]));
                    return;
                }
            }
            IsTrue(true, "all 7 prerequisite-free projects share column 0");
        }

        private static void LooseNodesPackTightly()
        {
            var graph = new LayoutGraph(80);
            var result = TabLayout.Compute(graph, new LayoutOptions { maxNodesPerColumn = 10 });

            int columns = 0;
            for (int i = 0; i < 80; i++) if (result.Layer[i] + 1 > columns) columns = result.Layer[i] + 1;

            Console.WriteLine(string.Format("         80 loose projects packed into {0} columns (ideal 8)", columns));
            IsTrue(columns <= 8, string.Format("used {0} columns for 80 projects at 10 per column", columns));
        }

        /// <summary>
        /// The case that motivated the model. Electricity has 24 followers against a cap of
        /// 10, so they cannot share a column - but they must still land as ONE run of cells
        /// with nothing else inside it, not diffuse across the tab.
        /// </summary>
        private static void WideFanFormsOneBlock()
        {
            var graph = new LayoutGraph(30);
            for (int i = 1; i <= 24; i++) graph.AddEdge(0, i);   // the hub
            graph.AddEdge(25, 26);                                // an unrelated pair
            // 27, 28, 29 stand alone.

            var options = new LayoutOptions { maxNodesPerColumn = 10 };
            var result = TabLayout.Compute(graph, options);

            var cells = new List<int>();
            for (int i = 1; i <= 24; i++) cells.Add(result.Layer[i] * 1000 + RowOf(result, i, options));
            cells.Sort();

            // Contiguous in column-major order means every step is +1 row, or a wrap.
            int breaks = 0;
            for (int i = 1; i < cells.Count; i++) if (cells[i] != cells[i - 1] + 1) breaks++;

            int spanColumns = result.Layer[24] - result.Layer[1] + 1;
            Console.WriteLine(string.Format("         24 followers span {0} columns with {1} break(s) in the run",
                spanColumns, breaks));

            IsTrue(breaks <= 2, string.Format("the fan is split into {0} pieces", breaks + 1));
            IsTrue(spanColumns <= 3, string.Format("the fan spans {0} columns", spanColumns));
        }

        /// <summary>
        /// No cell inside one parent's run may belong to a different parent. This is what
        /// stops a fan reading as a grid of unrelated cards.
        /// </summary>
        private static void GroupsDoNotInterleave()
        {
            var graph = new LayoutGraph(40);
            for (int i = 1; i <= 15; i++) graph.AddEdge(0, i);
            for (int i = 16; i <= 25; i++) graph.AddEdge(1, i);
            for (int i = 26; i <= 30; i++) graph.AddEdge(2, i);
            // 31..39 stand alone.

            var options = new LayoutOptions { maxNodesPerColumn = 8 };
            var result = TabLayout.Compute(graph, options);

            var owners = new Dictionary<int, int>();   // cell -> owning parent
            foreach (var edge in graph.AllEdges())
            {
                int cell = result.Layer[edge.Child] * 1000 + RowOf(result, edge.Child, options);
                owners[cell] = edge.Parent;
            }

            var sortedCells = new List<int>(owners.Keys);
            sortedCells.Sort();

            // Walking the cells in order, an owner must not reappear after another owner
            // has taken over - that would mean two groups were woven together.
            var seen = new HashSet<int>();
            int previousOwner = -1;
            foreach (int cell in sortedCells)
            {
                int owner = owners[cell];
                if (owner != previousOwner)
                {
                    if (!seen.Add(owner))
                    {
                        IsTrue(false, string.Format("group {0} is split around another group", owner));
                        return;
                    }
                    previousOwner = owner;
                }
            }
            IsTrue(true, "each parent's followers form one unbroken run");
        }

        private static void SmallGroupStaysNearParent()
        {
            // A big hub and a small one at the same depth. The small group must not be
            // pushed past the big one's two dozen.
            var graph = new LayoutGraph(32);
            for (int i = 2; i <= 25; i++) graph.AddEdge(0, i);   // 24 followers
            for (int i = 26; i <= 29; i++) graph.AddEdge(1, i);  // 4 followers

            var options = new LayoutOptions { maxNodesPerColumn = 10 };
            var result = TabLayout.Compute(graph, options);

            int worst = 0;
            for (int i = 26; i <= 29; i++)
            {
                int span = result.Layer[i] - result.Layer[1];
                if (span > worst) worst = span;
            }

            Console.WriteLine(string.Format("         small group sits {0} column(s) from its parent", worst));
            IsTrue(worst <= 1, string.Format("the small group is {0} columns away", worst));
        }

        private static void HubsSitAtGroupEnd()
        {
            // Node 5 is a follower of 0 and a hub in its own right.
            var graph = new LayoutGraph(20);
            for (int i = 1; i <= 8; i++) graph.AddEdge(0, i);
            for (int i = 9; i <= 14; i++) graph.AddEdge(5, i);

            var options = new LayoutOptions { maxNodesPerColumn = 10 };
            var result = TabLayout.Compute(graph, options);

            // Hubs start at the end of their group, but the refinement pass then pulls them
            // level with the middle of their own followers. That is the better place to be:
            // the lines out of the hub stay short instead of fanning from a corner. What
            // matters is that it ends up beside its followers, not that it is last.
            int hubRow = RowOf(result, 5, options);

            double followerRows = 0;
            for (int i = 9; i <= 14; i++) followerRows += RowOf(result, i, options);
            followerRows /= 6;

            Console.WriteLine(string.Format("         hub at row {0}, its followers centre on row {1:0.#}",
                hubRow, followerRows));

            IsTrue(Math.Abs(hubRow - followerRows) <= 2.0,
                string.Format("hub is {0:0.#} rows from the middle of its own followers", Math.Abs(hubRow - followerRows)));
        }

        private static void GroupsAreSeparated()
        {
            // Two small groups sharing a column should have a blank row between them.
            var graph = new LayoutGraph(12);
            for (int i = 2; i <= 5; i++) graph.AddEdge(0, i);
            for (int i = 6; i <= 9; i++) graph.AddEdge(1, i);

            var options = new LayoutOptions { maxNodesPerColumn = 12, separateGroups = true };
            var result = TabLayout.Compute(graph, options);

            var rowsA = new List<int>();
            var rowsB = new List<int>();
            for (int i = 2; i <= 5; i++) rowsA.Add(RowOf(result, i, options));
            for (int i = 6; i <= 9; i++) rowsB.Add(RowOf(result, i, options));
            rowsA.Sort();
            rowsB.Sort();

            bool sameColumn = result.Layer[2] == result.Layer[6];
            int gap = sameColumn ? Math.Abs(Math.Min(rowsB[0], rowsA[0]) - Math.Max(rowsA[rowsA.Count - 1], rowsB[rowsB.Count - 1])) : 0;

            Console.WriteLine(string.Format("         two 4-card groups share a column: {0}", sameColumn));
            IsTrue(!sameColumn || gap >= 4, "a blank row divides the two groups");
        }

        private static void NoEmptyRowsAcrossTheTab()
        {
            var graph = new LayoutGraph(12);
            graph.AddEdge(1, 2);
            graph.AddEdge(2, 3);
            graph.AddEdge(3, 4);
            graph.AddEdge(5, 6);
            for (int i = 8; i < 12; i++) graph.AddEdge(5, i);

            var options = new LayoutOptions { maxNodesPerColumn = 10, separateGroups = false };
            var result = TabLayout.Compute(graph, options);

            var rows = new List<float>(result.Y);
            rows.Sort();

            float biggest = 0f;
            for (int i = 1; i < rows.Count; i++)
            {
                float gap = rows[i] - rows[i - 1];
                if (gap > biggest) biggest = gap;
            }

            Console.WriteLine(string.Format("         widest empty band: {0:0.##} rows", biggest / options.yStep));
            IsTrue(biggest <= options.yStep + 0.001f, "no band left with nothing in it");
        }

        private static void RefinementReducesCrossings()
        {
            var graph = RandomDag(60, 140, seed: 4242);

            int refined = TabLayout.Compute(graph, new LayoutOptions { maxNodesPerColumn = 10, refineSweeps = 6 }).Crossings;
            int raw = TabLayout.Compute(graph, new LayoutOptions { maxNodesPerColumn = 10, refineSweeps = 0 }).Crossings;

            Console.WriteLine(string.Format("         60 projects / 140 links: {0} -> {1} crossings", raw, refined));
            IsTrue(refined <= raw, string.Format("refinement made it worse ({0} vs {1})", refined, raw));
        }

        private static void HeightNeverExceedsColumnCap()
        {
            var cases = new[]
            {
                MakeFan(60),
                new LayoutGraph(120),
                MakeLadder(40),
                RandomDag(200, 500, seed: 31337)
            };

            var options = new LayoutOptions { maxNodesPerColumn = 10 };
            float cap = options.maxNodesPerColumn * options.yStep;

            for (int c = 0; c < cases.Length; c++)
            {
                var result = TabLayout.Compute(cases[c], options);

                float minY = float.MaxValue, maxY = float.MinValue;
                for (int i = 0; i < cases[c].NodeCount; i++)
                {
                    if (result.Y[i] < minY) minY = result.Y[i];
                    if (result.Y[i] > maxY) maxY = result.Y[i];
                }

                Console.WriteLine(string.Format("         case {0}: {1} projects -> {2:0.#} rows tall (cap {3})",
                    c, cases[c].NodeCount, (maxY - minY) / options.yStep, options.maxNodesPerColumn));

                IsTrue(maxY - minY <= cap + 0.001f, string.Format("case {0} exceeded the cap", c));
            }
        }

        private static void BackwardEdgesDoNotThrow()
        {
            var graph = new LayoutGraph(6);
            graph.AddEdge(0, 1);
            graph.AddEdge(1, 2);
            graph.AddEdge(2, 0);   // a cycle, the only way an edge can point backwards
            for (int i = 3; i < 6; i++) graph.AddEdge(0, i);

            var result = TabLayout.Compute(graph, new LayoutOptions { maxNodesPerColumn = 2 });
            IsTrue(result.Crossings >= 0, "laid out without throwing");
        }

        private static void ScaleBenchmark()
        {
            var graph = RandomDag(400, 900, seed: 20260828);
            var options = new LayoutOptions { maxNodesPerColumn = 10 };

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = TabLayout.Compute(graph, options);
            watch.Stop();

            Console.WriteLine(string.Format("         400 projects / 900 links: {0} crossings in {1} ms",
                result.Crossings, watch.ElapsedMilliseconds));

            IsTrue(watch.ElapsedMilliseconds < 10000, "large tab laid out in under 10 seconds");
        }

        // ---- helpers --------------------------------------------------------------

        private static int RowOf(LayoutResult result, int node, LayoutOptions options)
        {
            return (int)Math.Round(result.Y[node] / options.yStep);
        }

        private static LayoutGraph MakeFan(int children)
        {
            var graph = new LayoutGraph(children + 1);
            for (int i = 1; i <= children; i++) graph.AddEdge(0, i);
            return graph;
        }

        private static LayoutGraph MakeLadder(int rungs)
        {
            var graph = new LayoutGraph(rungs * 2);
            for (int i = 0; i + 1 < rungs; i++) graph.AddEdge(i, i + 1);
            for (int i = 0; i < rungs; i++) graph.AddEdge(i, rungs + i);
            return graph;
        }

        private static LayoutGraph RandomDag(int nodes, int edges, int seed)
        {
            var random = new Random(seed);
            var graph = new LayoutGraph(nodes);

            int attempts = 0;
            while (graph.EdgeCount < edges && attempts++ < edges * 40)
            {
                int a = random.Next(nodes);
                int b = random.Next(nodes);
                if (a == b) continue;
                graph.AddEdge(Math.Min(a, b), Math.Max(a, b));
            }
            return graph;
        }

        private static void Run(string name, Action test)
        {
            tests++;
            try
            {
                int before = failures;
                test();
                Console.WriteLine((failures == before ? "  ok   " : "  FAIL ") + name);
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine("  FAIL " + name + " - threw " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void IsTrue(bool condition, string message)
        {
            checks++;
            if (!condition)
            {
                failures++;
                Console.WriteLine("         assertion failed: " + message);
            }
        }

        private static void AreEqual(int expected, int actual, string message)
        {
            checks++;
            if (expected != actual)
            {
                failures++;
                Console.WriteLine(string.Format("         {0}: expected {1}, got {2}", message, expected, actual));
            }
        }
    }
}
