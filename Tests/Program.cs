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
            Run("scale benchmark", ScaleBenchmark);

            Console.WriteLine();
            Console.WriteLine(failures == 0
                ? string.Format("PASS - {0} checks across 14 tests", checks)
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

        // ---- helpers --------------------------------------------------------------

        /// <summary>Layout with the ordering stage skipped, for a fair before/after crossing count.</summary>
        private static int CrossingsWithoutOrdering(LayoutGraph graph, LayoutOptions options)
        {
            var broken = CycleBreaker.Break(graph);
            int[] layerOf = Layering.Assign(broken.Acyclic, options.maxNodesPerColumn);
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
