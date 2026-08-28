using System.Collections.Generic;

namespace ResearchOrganized.Layout
{
    /// <summary>
    /// Decides the order of nodes within each layer so that as few edges cross as possible.
    ///
    /// This is the stage the previous engine had no equivalent of. Minimising crossings is
    /// what actually makes a dense modded tree readable; stacking siblings and spacing hubs
    /// only ever approximated it.
    ///
    /// Alternating median sweeps, each followed by a transpose pass that swaps adjacent
    /// pairs while that helps. The best ordering seen across all iterations is kept, so the
    /// result can never be worse than where it started.
    /// </summary>
    public static class Ordering
    {
        public static void Optimize(LayeredGraph graph, int sweeps)
        {
            if (graph.LayerCount < 2) return;
            if (sweeps < 1) sweeps = 1;

            var best = Snapshot(graph);
            int bestCrossings = graph.CountCrossings();

            for (int iteration = 0; iteration < sweeps; iteration++)
            {
                bool downward = (iteration % 2) == 0;
                MedianSweep(graph, downward);
                Transpose(graph);

                int crossings = graph.CountCrossings();
                if (crossings < bestCrossings)
                {
                    bestCrossings = crossings;
                    best = Snapshot(graph);
                    if (crossings == 0) break;
                }
            }

            Restore(graph, best);
        }

        /// <summary>
        /// Reorders each layer by the median position of each node's neighbours in the
        /// already-fixed adjacent layer. Nodes with no neighbours on that side keep their
        /// current slot rather than being swept to one end.
        /// </summary>
        private static void MedianSweep(LayeredGraph graph, bool downward)
        {
            var pos = graph.BuildPositions();

            if (downward)
            {
                for (int l = 1; l < graph.LayerCount; l++) SortLayerByMedian(graph, l, pos, graph.Up);
            }
            else
            {
                for (int l = graph.LayerCount - 2; l >= 0; l--) SortLayerByMedian(graph, l, pos, graph.Down);
            }
        }

        private static void SortLayerByMedian(LayeredGraph graph, int layerIndex, int[] pos, List<int>[] neighbours)
        {
            var layer = graph.Layers[layerIndex];
            var keys = new Dictionary<int, double>();

            for (int i = 0; i < layer.Count; i++)
            {
                int node = layer[i];
                keys[node] = Median(neighbours[node], pos, i);
            }

            layer.Sort(delegate (int a, int b)
            {
                int compared = keys[a].CompareTo(keys[b]);
                if (compared != 0) return compared;
                // Stable tie-break so layout is deterministic run to run.
                return a.CompareTo(b);
            });

            for (int i = 0; i < layer.Count; i++) pos[layer[i]] = i;
        }

        private static double Median(List<int> neighbours, int[] pos, int fallback)
        {
            if (neighbours.Count == 0) return fallback;

            var values = new List<int>(neighbours.Count);
            for (int i = 0; i < neighbours.Count; i++) values.Add(pos[neighbours[i]]);
            values.Sort();

            int middle = values.Count / 2;
            if (values.Count % 2 == 1) return values[middle];
            return (values[middle - 1] + values[middle]) / 2.0;
        }

        /// <summary>
        /// Swaps adjacent pairs within a layer whenever doing so reduces the crossings on
        /// the layer's two sides. Cleans up the local damage a median sweep leaves behind.
        /// </summary>
        private static void Transpose(LayeredGraph graph)
        {
            bool improved = true;
            int guard = 0;
            var pos = graph.BuildPositions();

            while (improved && guard++ < 8)
            {
                improved = false;

                for (int l = 0; l < graph.LayerCount; l++)
                {
                    var layer = graph.Layers[l];
                    for (int i = 0; i + 1 < layer.Count; i++)
                    {
                        int left = layer[i];
                        int right = layer[i + 1];

                        // Only edges touching these two nodes can change, and only their
                        // relative order matters - so compare locally instead of recounting
                        // the whole layer pair.
                        int before = PairCrossings(left, right, graph.Down, pos) + PairCrossings(left, right, graph.Up, pos);
                        int after = PairCrossings(right, left, graph.Down, pos) + PairCrossings(right, left, graph.Up, pos);

                        if (after < before)
                        {
                            layer[i] = right;
                            layer[i + 1] = left;
                            pos[left] = i + 1;
                            pos[right] = i;
                            improved = true;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Crossings contributed by edges of <paramref name="leftNode"/> and
        /// <paramref name="rightNode"/> on one side, assuming left sits before right.
        /// Positions of the adjacent layer are unaffected by swapping within this one.
        /// </summary>
        private static int PairCrossings(int leftNode, int rightNode, List<int>[] neighbours, int[] pos)
        {
            var leftEdges = neighbours[leftNode];
            var rightEdges = neighbours[rightNode];

            int crossings = 0;
            for (int a = 0; a < leftEdges.Count; a++)
            {
                int leftTarget = pos[leftEdges[a]];
                for (int b = 0; b < rightEdges.Count; b++)
                {
                    if (leftTarget > pos[rightEdges[b]]) crossings++;
                }
            }
            return crossings;
        }

        private static List<int>[] Snapshot(LayeredGraph graph)
        {
            var copy = new List<int>[graph.LayerCount];
            for (int i = 0; i < graph.LayerCount; i++) copy[i] = new List<int>(graph.Layers[i]);
            return copy;
        }

        private static void Restore(LayeredGraph graph, List<int>[] snapshot)
        {
            for (int i = 0; i < graph.LayerCount; i++)
            {
                graph.Layers[i].Clear();
                graph.Layers[i].AddRange(snapshot[i]);
            }
        }
    }
}
