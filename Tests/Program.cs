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

        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "dump") return Dump.Run(args);
            return RunTests();
        }

        private static int RunTests()
        {
            Run("empty graph is handled", EmptyGraph);
            Run("chain lands in consecutive columns", ChainLayering);
            Run("every follower is right of its parent", ChildAlwaysRightOfParent);
            Run("column height cap is respected", ColumnHeightCap);
            Run("cycles are broken and reported", CyclesAreBroken);
            Run("layout is deterministic", Deterministic);
            Run("loose projects pack tightly", LooseNodesPackTightly);
            Run("siblings placed together land in consecutive rows", SiblingsLandConsecutively);
            Run("backward edges do not throw", BackwardEdgesDoNotThrow);
            Run("height never exceeds the cap", HeightNeverExceedsColumnCap);
            Run("scale benchmark", ScaleBenchmark);

            Run("an anchor holds its own column", AnchorHoldsItsOwnColumn);
            Run("starter projects are placed before the anchor", StartersComeBeforeTheAnchor);
            Run("an anchor's own followers sit right after it", AnchorFollowersSitAdjacent);
            Run("an anchor centres on its followers", AnchorCentresOnFollowers);
            Run("a later, more specific anchor claims its own descendants", DeeperAnchorClaimsItsOwnBranch);
            Run("an oversized anchor batch is not exceeded", AnchorBatchRespectsHeightCap);
            Run("tech levels are laid out in order, left to right", EpochsAdvanceLeftToRight);
            Run("a capstone lands after everything else in its era", CapstoneLandsLast);
            Run("a capstone with no prerequisites still lands last", CapstoneWithNoPrerequisitesStillLandsLast);

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
        /// The greedy placer fills a column top-down from whatever row the parent it followed
        /// suggests. Two siblings placed back-to-back should not have a gap forced between
        /// them just because of iteration order.
        /// </summary>
        private static void SiblingsLandConsecutively()
        {
            var graph = new LayoutGraph(9);
            for (int i = 1; i <= 8; i++) graph.AddEdge(0, i);

            var options = new LayoutOptions { maxNodesPerColumn = 10 };
            var result = TabLayout.Compute(graph, options);

            var rows = new List<int>();
            for (int i = 1; i <= 8; i++) rows.Add(RowOf(result, i, options));
            rows.Sort();

            for (int i = 1; i < rows.Count; i++)
            {
                if (rows[i] - rows[i - 1] > 1)
                {
                    IsTrue(false, string.Format("gap between sibling rows {0} and {1}", rows[i - 1], rows[i]));
                    return;
                }
            }
            IsTrue(true, "eight siblings landed in eight consecutive rows");
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

        // ---- the anchor / epoch model -----------------------------------------------

        /// <summary>
        /// The case that motivated restoring this engine. A hub marked as an anchor must hold
        /// a column of its own instead of being placed inline with its siblings at the same
        /// depth - that is what let Electricity sit among a hundred other projects instead of
        /// getting its own column with its two dozen followers after it.
        /// </summary>
        private static void AnchorHoldsItsOwnColumn()
        {
            var graph = new LayoutGraph(30);
            for (int i = 1; i <= 24; i++) graph.AddEdge(0, i);
            graph.AddEdge(25, 26); // an unrelated small chain sharing node 0's depth

            var options = new LayoutOptions
            {
                maxNodesPerColumn = 10,
                epoch = new int[30],
                isAnchor = MarkAnchors(30, 0),
                anchorOrder = new int[30],
                tieRank = Identity(30)
            };

            var result = TabLayout.Compute(graph, options);

            for (int i = 1; i <= 24; i++)
            {
                if (result.Layer[i] == result.Layer[0])
                {
                    IsTrue(false, "a follower shares the anchor's own column");
                    return;
                }
            }
            IsTrue(true, "the anchor's column holds nothing but the anchor");
        }

        /// <summary>
        /// Drug production, Heavy bridges and Piano do not need Electricity and are cheaper -
        /// they should be placed ahead of it, not scattered behind its follower block.
        /// </summary>
        private static void StartersComeBeforeTheAnchor()
        {
            var graph = new LayoutGraph(10);
            for (int i = 5; i <= 9; i++) graph.AddEdge(0, i); // the anchor's followers
            // 1..4 are cheap, independent starters.

            var options = new LayoutOptions
            {
                maxNodesPerColumn = 10,
                epoch = new int[10],
                isAnchor = MarkAnchors(10, 0),
                anchorOrder = new int[10],
                tieRank = Identity(10)
            };

            var result = TabLayout.Compute(graph, options);

            for (int i = 1; i <= 4; i++)
            {
                if (result.Layer[i] >= result.Layer[0])
                {
                    IsTrue(false, string.Format("starter {0} did not come before the anchor", i));
                    return;
                }
            }
            IsTrue(true, "every starter sits left of the anchor");
        }

        private static void AnchorFollowersSitAdjacent()
        {
            var graph = new LayoutGraph(30);
            for (int i = 1; i <= 24; i++) graph.AddEdge(0, i);

            var options = new LayoutOptions
            {
                maxNodesPerColumn = 10,
                epoch = new int[30],
                isAnchor = MarkAnchors(30, 0),
                anchorOrder = new int[30],
                tieRank = Identity(30)
            };

            var result = TabLayout.Compute(graph, options);

            int earliest = int.MaxValue;
            for (int i = 1; i <= 24; i++) if (result.Layer[i] < earliest) earliest = result.Layer[i];

            AreEqual(1, earliest - result.Layer[0], "followers start in the very next column after the anchor");
        }

        /// <summary>
        /// The anchor should read level with the middle of its own fan, not pinned to row
        /// zero regardless of where its followers land.
        /// </summary>
        private static void AnchorCentresOnFollowers()
        {
            var graph = new LayoutGraph(10);
            for (int i = 1; i <= 8; i++) graph.AddEdge(0, i);

            var options = new LayoutOptions
            {
                maxNodesPerColumn = 10,
                epoch = new int[10],
                isAnchor = MarkAnchors(10, 0),
                anchorOrder = new int[10],
                tieRank = Identity(10)
            };

            var result = TabLayout.Compute(graph, options);

            int minRow = int.MaxValue, maxRow = int.MinValue;
            for (int i = 1; i <= 8; i++)
            {
                int r = RowOf(result, i, options);
                if (r < minRow) minRow = r;
                if (r > maxRow) maxRow = r;
            }

            int anchorRow = RowOf(result, 0, options);
            Console.WriteLine(string.Format("         followers span rows {0}-{1}, anchor sits at row {2}", minRow, maxRow, anchorRow));
            IsTrue(anchorRow >= minRow && anchorRow <= maxRow, "the anchor sits within its own followers' span");
        }

        /// <summary>
        /// A project needing both a broad early anchor and a more specific later one (say,
        /// Electricity and Machining) should read as belonging to the closer, more specific
        /// hub - not get pulled back to sit under the earlier, broader one.
        /// </summary>
        private static void DeeperAnchorClaimsItsOwnBranch()
        {
            var graph = new LayoutGraph(12);
            for (int i = 1; i <= 6; i++) graph.AddEdge(0, i); // electricity's plain followers
            graph.AddEdge(0, 1);                              // machining also needs electricity (node 1)
            for (int i = 7; i <= 11; i++) graph.AddEdge(1, i); // machining's own followers

            var options = new LayoutOptions
            {
                maxNodesPerColumn = 20,
                epoch = new int[12],
                isAnchor = MarkAnchors(12, 0, 1),
                anchorOrder = new int[12] { 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, // node 0 before node 1
                tieRank = Identity(12)
            };

            var result = TabLayout.Compute(graph, options);

            int earliest = int.MaxValue;
            for (int i = 7; i <= 11; i++) if (result.Layer[i] < earliest) earliest = result.Layer[i];

            AreEqual(1, earliest - result.Layer[1], "machining's own followers sit right after machining, not after electricity");
        }

        private static void AnchorBatchRespectsHeightCap()
        {
            var graph = new LayoutGraph(40);
            for (int i = 1; i <= 30; i++) graph.AddEdge(0, i); // 30 followers, cap of 10

            var options = new LayoutOptions
            {
                maxNodesPerColumn = 10,
                epoch = new int[40],
                isAnchor = MarkAnchors(40, 0),
                anchorOrder = new int[40],
                tieRank = Identity(40)
            };

            var result = TabLayout.Compute(graph, options);

            var counts = new Dictionary<int, int>();
            for (int i = 0; i < 40; i++)
            {
                int layer = result.Layer[i];
                counts[layer] = counts.ContainsKey(layer) ? counts[layer] + 1 : 1;
            }

            foreach (var pair in counts)
            {
                IsTrue(pair.Value <= 10, string.Format("column {0} holds {1}, cap was 10", pair.Key, pair.Value));
            }
        }

        private static void EpochsAdvanceLeftToRight()
        {
            var graph = new LayoutGraph(6);
            // No edges between epochs - a combined tab mixing tech levels, as
            // "use a single main tab" produces.
            var options = new LayoutOptions
            {
                maxNodesPerColumn = 10,
                epoch = new[] { 0, 0, 1, 1, 2, 2 },
                isAnchor = new bool[6],
                anchorOrder = new int[6],
                tieRank = Identity(6)
            };

            var result = TabLayout.Compute(graph, options);

            IsTrue(result.Layer[2] > result.Layer[0] && result.Layer[3] > result.Layer[1],
                "the second tech level starts after the first");
            IsTrue(result.Layer[4] > result.Layer[2] && result.Layer[5] > result.Layer[3],
                "the third tech level starts after the second");
        }

        /// <summary>
        /// An era's "advance to the next tech level" node (Node Research's Emergence nodes,
        /// for one) must read as the last thing in its era, even when it does carry real
        /// prerequisites into a hub's fan.
        /// </summary>
        private static void CapstoneLandsLast()
        {
            var graph = new LayoutGraph(11);
            for (int i = 1; i <= 8; i++) graph.AddEdge(0, i); // an anchor and its followers
            graph.AddEdge(0, 9);                              // the capstone also needs the anchor
            // 10 stands alone.

            var options = new LayoutOptions
            {
                maxNodesPerColumn = 10,
                epoch = new int[11],
                isAnchor = MarkAnchors(11, 0),
                anchorOrder = new int[11],
                tieRank = Identity(11),
                isCapstone = MarkAnchors(11, 9)
            };

            var result = TabLayout.Compute(graph, options);

            for (int i = 0; i < 11; i++)
            {
                if (i == 9) continue;
                IsTrue(result.Layer[9] > result.Layer[i], string.Format("capstone did not land after project {0}", i));
            }
        }

        private static void CapstoneWithNoPrerequisitesStillLandsLast()
        {
            var graph = new LayoutGraph(6);
            for (int i = 1; i <= 4; i++) graph.AddEdge(0, i);
            // node 5 is the capstone, with no edges at all - as Node Research creates one
            // before its own prerequisite-wiring pass runs.

            var options = new LayoutOptions
            {
                maxNodesPerColumn = 10,
                epoch = new int[6],
                isAnchor = MarkAnchors(6, 0),
                anchorOrder = new int[6],
                tieRank = Identity(6),
                isCapstone = MarkAnchors(6, 5)
            };

            var result = TabLayout.Compute(graph, options);

            for (int i = 0; i < 5; i++)
            {
                IsTrue(result.Layer[5] > result.Layer[i], string.Format("unlinked capstone did not land after project {0}", i));
            }
        }

        // ---- helpers --------------------------------------------------------------

        private static int RowOf(LayoutResult result, int node, LayoutOptions options)
        {
            return (int)Math.Round(result.Y[node] / options.yStep);
        }

        private static bool[] MarkAnchors(int count, params int[] anchors)
        {
            var flags = new bool[count];
            foreach (int a in anchors) flags[a] = true;
            return flags;
        }

        private static int[] Identity(int count)
        {
            var rank = new int[count];
            for (int i = 0; i < count; i++) rank[i] = i;
            return rank;
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
