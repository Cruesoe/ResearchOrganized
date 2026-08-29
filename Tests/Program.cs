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

        private static int Main()
        {
            Run("empty graph is handled", EmptyGraph);
            Run("chain lands in consecutive columns", ChainLayering);
            Run("every child is right of its parents", ChildAlwaysRightOfParent);
            Run("column width cap is respected", ColumnWidthCap);
            Run("cycles are broken and reported", CyclesAreBroken);
            Run("a planar graph ends with zero crossings", PlanarGraphHasNoCrossings);
            Run("crossings are reduced on a tangled graph", CrossingsAreReduced);
            Run("nodes in a column keep minimum separation", MinimumSeparation);
            Run("layout is deterministic", Deterministic);
            Run("disconnected components all get placed", DisconnectedComponents);
            Run("dependents stay near their parents", DependentsStayNearParents);
            Run("loose nodes pack tightly", LooseNodesPackTightly);
            Run("routed edges do not inflate columns", RoutedEdgesDoNotInflateColumns);
            Run("roots share the first column", RootsShareTheFirstColumn);
            Run("anchors hold their column when it overflows", AnchorsHoldTheirColumnWhenItOverflows);
            Run("gateway projects go last", GatewayProjectsGoLast);
            Run("pinned project with followers does not corrupt layers", PinnedProjectWithFollowersDoesNotCorruptLayers);
            Run("backward edges do not throw", BackwardEdgesDoNotThrow);
            Run("height never exceeds the column cap", HeightNeverExceedsColumnCap);
            Run("scale benchmark", ScaleBenchmark);

            Console.WriteLine();
            Console.WriteLine(failures == 0
                ? string.Format("PASS - {0} checks across 20 tests", checks)
                : string.Format("FAIL - {0} failed check(s) of {1}", failures, checks));
            return failures == 0 ? 0 : 1;
        }

        // ---- tests ----------------------------------------------------------------

        private static void EmptyGraph()
        {
            var result = SugiyamaLayout.Compute(new LayoutGraph(0), new LayoutOptions());
            IsTrue(result.X.Length == 0, "no coordinates produced");
        }

        private static void ChainLayering()
        {
            var graph = new LayoutGraph(3);
            graph.AddEdge(0, 1);
            graph.AddEdge(1, 2);

            var result = SugiyamaLayout.Compute(graph, new LayoutOptions());
            AreEqual(0, result.Layer[0], "first node column");
            AreEqual(1, result.Layer[1], "second node column");
            AreEqual(2, result.Layer[2], "third node column");
            IsTrue(result.X[0] < result.X[1] && result.X[1] < result.X[2], "x increases along the chain");
        }

        private static void ChildAlwaysRightOfParent()
        {
            var graph = RandomDag(60, 120, seed: 12345);
            var result = SugiyamaLayout.Compute(graph, new LayoutOptions());

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

        private static void ColumnWidthCap()
        {
            // One root with 20 independent children; cap of 4 must split them across columns.
            var graph = new LayoutGraph(21);
            for (int i = 1; i <= 20; i++) graph.AddEdge(0, i);

            var options = new LayoutOptions { maxNodesPerColumn = 4 };
            var result = SugiyamaLayout.Compute(graph, options);

            var counts = new Dictionary<int, int>();
            for (int node = 0; node < graph.NodeCount; node++)
            {
                int layer = result.Layer[node];
                counts[layer] = counts.ContainsKey(layer) ? counts[layer] + 1 : 1;
            }

            foreach (var pair in counts)
            {
                if (pair.Value > 4)
                {
                    IsTrue(false, string.Format("column {0} holds {1} nodes, cap was 4", pair.Key, pair.Value));
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

            var result = SugiyamaLayout.Compute(graph, new LayoutOptions());
            IsTrue(result.ReversedEdges.Count >= 1, "at least one edge was reversed");
            IsTrue(result.NodesInCycles.Count >= 2, "nodes on the cycle were reported");

            // After breaking, the retained edges must still form a left-to-right order.
            int advancing = 0;
            foreach (var edge in graph.AllEdges())
            {
                if (result.Layer[edge.Child] > result.Layer[edge.Parent]) advancing++;
            }
            AreEqual(2, advancing, "two of the three cycle edges still advance");
        }

        private static void PlanarGraphHasNoCrossings()
        {
            // Three parents wired to three children in reverse order. Crossings only
            // vanish if the ordering stage flips one of the layers.
            var graph = new LayoutGraph(6);
            graph.AddEdge(0, 5);
            graph.AddEdge(1, 4);
            graph.AddEdge(2, 3);

            var result = SugiyamaLayout.Compute(graph, new LayoutOptions());
            AreEqual(0, result.Crossings, "crossings eliminated");
        }

        private static void CrossingsAreReduced()
        {
            var graph = RandomDag(40, 90, seed: 999);
            var options = new LayoutOptions();

            int optimized = SugiyamaLayout.Compute(graph, options).Crossings;
            int unoptimized = CrossingsWithoutOrdering(graph, options);

            IsTrue(optimized <= unoptimized,
                string.Format("ordering did not make things worse ({0} vs {1} unoptimised)", optimized, unoptimized));
            IsTrue(optimized < unoptimized,
                string.Format("ordering reduced crossings: {0} -> {1}", unoptimized, optimized));

            Console.WriteLine(string.Format("         40 nodes / 90 edges: {0} -> {1} crossings ({2:0.#}% fewer)",
                unoptimized, optimized, 100.0 * (unoptimized - optimized) / unoptimized));
        }

        /// <summary>
        /// Not an assertion so much as a guard rail: this runs during game startup, so if a
        /// tree the size of a heavy modlist ever takes seconds rather than milliseconds we
        /// want to see it here rather than in a load-time freeze.
        /// </summary>
        private static void ScaleBenchmark()
        {
            var graph = RandomDag(400, 900, seed: 20260828);
            var options = new LayoutOptions();

            int before = CrossingsWithoutOrdering(graph, options);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = SugiyamaLayout.Compute(graph, options);
            watch.Stop();

            Console.WriteLine(string.Format("         400 nodes / 900 edges: {0} -> {1} crossings in {2} ms",
                before, result.Crossings, watch.ElapsedMilliseconds));

            IsTrue(result.Crossings <= before, "large graph did not regress");
            IsTrue(watch.ElapsedMilliseconds < 10000, "large graph laid out in under 10 seconds");
        }

        private static void MinimumSeparation()
        {
            var graph = RandomDag(50, 100, seed: 4242);
            var options = new LayoutOptions();
            var result = SugiyamaLayout.Compute(graph, options);

            var byLayer = new Dictionary<int, List<float>>();
            for (int node = 0; node < graph.NodeCount; node++)
            {
                int layer = result.Layer[node];
                if (!byLayer.ContainsKey(layer)) byLayer[layer] = new List<float>();
                byLayer[layer].Add(result.Y[node]);
            }

            foreach (var pair in byLayer)
            {
                var values = pair.Value;
                values.Sort();
                for (int i = 1; i < values.Count; i++)
                {
                    float gap = values[i] - values[i - 1];
                    if (gap < options.yStep - 0.001f)
                    {
                        IsTrue(false, string.Format("column {0} has a {1:0.###} gap, expected >= {2}", pair.Key, gap, options.yStep));
                        return;
                    }
                }
            }
            IsTrue(true, "all columns kept their minimum spacing");
        }

        private static void Deterministic()
        {
            var graph = RandomDag(45, 95, seed: 777);
            var first = SugiyamaLayout.Compute(graph, new LayoutOptions());
            var second = SugiyamaLayout.Compute(graph, new LayoutOptions());

            for (int node = 0; node < graph.NodeCount; node++)
            {
                if (first.X[node] != second.X[node] || first.Y[node] != second.Y[node])
                {
                    IsTrue(false, "node " + node + " moved between identical runs");
                    return;
                }
            }
            IsTrue(true, "two runs produced identical coordinates");
        }

        private static void DisconnectedComponents()
        {
            var graph = new LayoutGraph(6);
            graph.AddEdge(0, 1);
            graph.AddEdge(2, 3);
            // 4 and 5 are isolated.

            var result = SugiyamaLayout.Compute(graph, new LayoutOptions());
            AreEqual(0, result.Layer[4], "isolated node sits in the first column");
            AreEqual(0, result.Layer[5], "second isolated node too");
            IsTrue(result.Layer[1] == 1 && result.Layer[3] == 1, "both components advance normally");
        }

        /// <summary>
        /// Models a real tab. Because tabs are split by tech level and prerequisites almost
        /// always cross tech levels, most nodes on a tab have no in-tab parent at all - a tab
        /// is mostly loose nodes plus a few short chains. A dependent node must still land
        /// beside its parent, not be flung to the far right because the cap filled the
        /// columns in between.
        /// </summary>
        private static void DependentsStayNearParents()
        {
            // 80 loose nodes, plus one 4-long chain hanging off node 0.
            var graph = new LayoutGraph(84);
            graph.AddEdge(0, 80);
            graph.AddEdge(80, 81);
            graph.AddEdge(81, 82);
            graph.AddEdge(82, 83);

            var options = new LayoutOptions { maxNodesPerColumn = 10 };
            var result = SugiyamaLayout.Compute(graph, options);

            int worst = 0;
            foreach (var edge in graph.AllEdges())
            {
                int span = result.Layer[edge.Child] - result.Layer[edge.Parent];
                if (span > worst) worst = span;
            }

            Console.WriteLine(string.Format("         worst parent->child column span: {0}", worst));
            IsTrue(worst <= 2, string.Format("a child sits {0} columns from its parent", worst));
        }

        /// <summary>
        /// A tab of entirely unconnected projects should pack into a tight block, not sprawl.
        /// </summary>
        private static void LooseNodesPackTightly()
        {
            var graph = new LayoutGraph(80);
            var options = new LayoutOptions { maxNodesPerColumn = 10 };
            var result = SugiyamaLayout.Compute(graph, options);

            int columns = 0;
            for (int i = 0; i < 80; i++) if (result.Layer[i] + 1 > columns) columns = result.Layer[i] + 1;

            Console.WriteLine(string.Format("         80 loose nodes packed into {0} columns (ideal 8)", columns));
            IsTrue(columns <= 8, string.Format("used {0} columns for 80 nodes at 10 per column", columns));
        }

        /// <summary>
        /// A long edge crossing a column becomes a dummy node in that column. A dummy is a
        /// line passing through, not a research card, so it must not push the real nodes in
        /// that column a whole row apart. This is what left large vertical gaps between
        /// cards that sat in the same column.
        /// </summary>
        private static void RoutedEdgesDoNotInflateColumns()
        {
            // A hub fanning to 8 nodes placed far to the right, so every intermediate
            // column carries 8 routed edges, plus 4 real cards sharing one of those columns.
            var graph = new LayoutGraph(14);
            for (int i = 1; i <= 8; i++) graph.AddEdge(0, i);
            for (int i = 1; i <= 8; i++) graph.AddEdge(i, 9);
            graph.AddEdge(9, 10);
            graph.AddEdge(10, 11);
            graph.AddEdge(11, 12);
            graph.AddEdge(12, 13);

            var options = new LayoutOptions { maxNodesPerColumn = 4 };
            var result = SugiyamaLayout.Compute(graph, options);

            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < graph.NodeCount; i++)
            {
                if (result.Y[i] < minY) minY = result.Y[i];
                if (result.Y[i] > maxY) maxY = result.Y[i];
            }

            float rows = (maxY - minY) / options.yStep;
            Console.WriteLine(string.Format("         14 nodes, cap 4: vertical extent {0:0.#} rows", rows));

            // 14 nodes at 4 per column needs about 4 rows. Allow generous slack for
            // straightening, but not the runaway growth a full-height dummy causes.
            IsTrue(rows <= 8f, string.Format("layout is {0:0.#} rows tall for a 4-row-deep graph", rows));
        }

        /// <summary>
        /// The research window does not scroll comfortably in Y, so a tab must never grow
        /// taller than maxNodesPerColumn rows - overflow goes sideways instead. The original
        /// engine guaranteed this by only ever placing nodes at row indices below the cap;
        /// the rewrite lost it and tabs ran off the bottom of the window.
        /// </summary>
        private static void HeightNeverExceedsColumnCap()
        {
            var cases = new[]
            {
                // one hub fanning very wide - the classic overflow case
                MakeFan(60),
                // a tab that is mostly loose nodes
                new LayoutGraph(120),
                // long chains that generate routed edges through many columns
                MakeLadder(40),
                // a dense random graph
                RandomDag(200, 500, seed: 31337)
            };

            var options = new LayoutOptions { maxNodesPerColumn = 10 };
            float cap = options.maxNodesPerColumn * options.yStep;

            for (int c = 0; c < cases.Length; c++)
            {
                var result = SugiyamaLayout.Compute(cases[c], options);

                float minY = float.MaxValue, maxY = float.MinValue;
                for (int i = 0; i < cases[c].NodeCount; i++)
                {
                    if (result.Y[i] < minY) minY = result.Y[i];
                    if (result.Y[i] > maxY) maxY = result.Y[i];
                }

                float rows = (maxY - minY) / options.yStep;
                Console.WriteLine(string.Format("         case {0}: {1} nodes -> {2:0.#} rows tall (cap {3})",
                    c, cases[c].NodeCount, rows, options.maxNodesPerColumn));

                IsTrue(maxY - minY <= cap + 0.001f,
                    string.Format("case {0} is {1:0.#} rows tall, cap is {2}", c, rows, options.maxNodesPerColumn));
            }
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

        /// <summary>
        /// A project with no prerequisites is available immediately, so it belongs in column
        /// zero no matter which part of the tab it belongs to. Previously independent
        /// fragments were packed to the right of the biggest one, which read as "you need
        /// electricity first" for projects that need nothing at all.
        /// </summary>
        private static void RootsShareTheFirstColumn()
        {
            // A big hub with a dozen followers, plus three unrelated standalone projects.
            var graph = new LayoutGraph(20);
            for (int i = 1; i <= 12; i++) graph.AddEdge(0, i);
            graph.AddEdge(13, 14);
            // 15, 16, 17, 18, 19 stand alone.

            var options = new LayoutOptions { maxNodesPerColumn = 10 };
            var result = SugiyamaLayout.Compute(graph, options);

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

        /// <summary>
        /// When a column overflows, the hubs keep their place and the leaves move aside.
        /// Losing this is what buried a key project like machining among its siblings.
        /// </summary>
        private static void AnchorsHoldTheirColumnWhenItOverflows()
        {
            // 14 children of a root, one of which (node 1) is itself a hub with 5 followers.
            var graph = new LayoutGraph(20);
            for (int i = 1; i <= 14; i++) graph.AddEdge(0, i);
            for (int i = 15; i <= 19; i++) graph.AddEdge(1, i);

            // Rank the hub first, the way the adapter ranks anchors ahead of leaves.
            var rank = new int[20];
            for (int i = 0; i < 20; i++) rank[i] = i == 1 ? 0 : i + 1;

            var options = new LayoutOptions { maxNodesPerColumn = 10, rank = rank };
            var result = SugiyamaLayout.Compute(graph, options);

            Console.WriteLine(string.Format("         anchor in column {0}, its followers in column {1}",
                result.Layer[1], result.Layer[15]));

            AreEqual(1, result.Layer[1], "anchor stays at its true depth");
            IsTrue(result.Layer[15] == result.Layer[1] + 1, "the anchor's followers sit directly beside it");
        }

        /// <summary>
        /// A project that another tab depends on concludes this one, so it belongs at the
        /// right-hand end rather than in the middle of a column of siblings.
        /// </summary>
        private static void GatewayProjectsGoLast()
        {
            var graph = new LayoutGraph(12);
            for (int i = 1; i <= 8; i++) graph.AddEdge(0, i);
            graph.AddEdge(8, 9);
            graph.AddEdge(9, 10);

            // Node 3 is the gateway: shallow, but it is what unlocks the next tab.
            var pinLast = new bool[12];
            pinLast[3] = true;

            var options = new LayoutOptions { maxNodesPerColumn = 10, pinLast = pinLast };
            var result = SugiyamaLayout.Compute(graph, options);

            int deepest = 0;
            for (int i = 0; i < 12; i++) if (i != 3 && result.Layer[i] > deepest) deepest = result.Layer[i];

            Console.WriteLine(string.Format("         gateway in column {0}, everything else ends at {1}",
                result.Layer[3], deepest));

            IsTrue(result.Layer[3] > deepest, "the gateway sits past every other project");
        }

        /// <summary>
        /// Pinning a project that other projects on the same tab depend on would push it to
        /// the right of its own followers, producing an edge that runs backwards. That
        /// corrupted the layer structure and threw out of the crossing counter, which took
        /// down the layout for every tab at once.
        /// </summary>
        private static void PinnedProjectWithFollowersDoesNotCorruptLayers()
        {
            var graph = new LayoutGraph(8);
            graph.AddEdge(0, 1);
            graph.AddEdge(1, 2);   // node 1 is pinned yet node 2 depends on it
            graph.AddEdge(2, 3);
            for (int i = 4; i < 8; i++) graph.AddEdge(0, i);

            var pinLast = new bool[8];
            pinLast[1] = true;

            var options = new LayoutOptions { maxNodesPerColumn = 4, pinLast = pinLast };
            var result = SugiyamaLayout.Compute(graph, options);

            foreach (var edge in graph.AllEdges())
            {
                if (result.Layer[edge.Child] <= result.Layer[edge.Parent])
                {
                    IsTrue(false, string.Format("edge {0} runs backwards: columns {1} -> {2}",
                        edge, result.Layer[edge.Parent], result.Layer[edge.Child]));
                    return;
                }
            }
            IsTrue(true, "every edge still advances a column");
        }

        /// <summary>
        /// Even if something upstream hands the layered graph an edge that cannot run left
        /// to right, it must degrade rather than throw - this runs at game startup, and one
        /// exception aborts the layout for every tab.
        /// </summary>
        private static void BackwardEdgesDoNotThrow()
        {
            var graph = new LayoutGraph(6);
            graph.AddEdge(0, 1);
            graph.AddEdge(1, 2);
            for (int i = 3; i < 6; i++) graph.AddEdge(0, i);

            // Force a layering that puts a child left of its parent.
            var broken = CycleBreaker.Break(graph);
            var layerOf = new int[6];
            layerOf[0] = 0; layerOf[1] = 5; layerOf[2] = 2;
            layerOf[3] = 1; layerOf[4] = 1; layerOf[5] = 1;

            var layered = LayeredGraph.Build(broken.Acyclic, layerOf);
            Ordering.Optimize(layered, 4);
            int crossings = layered.CountCrossings();

            IsTrue(crossings >= 0, "counted crossings without throwing");
        }

        // ---- helpers --------------------------------------------------------------

        /// <summary>Layout with the ordering stage skipped, for a fair before/after crossing count.</summary>
        private static int CrossingsWithoutOrdering(LayoutGraph graph, LayoutOptions options)
        {
            var broken = CycleBreaker.Break(graph);
            int[] layerOf = Layering.Assign(broken.Acyclic, options.maxNodesPerColumn, options.rank, options.pinLast);
            var layered = LayeredGraph.Build(broken.Acyclic, layerOf);
            return layered.CountCrossings();
        }

        /// <summary>Random DAG: edges only ever run from a lower to a higher index, so it cannot cycle.</summary>
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
