using System.Collections.Generic;

namespace ResearchOrganized.Layout
{
    /// <summary>
    /// Turns layer index + within-layer order into actual coordinates.
    ///
    /// Layer becomes X (progression runs left to right, as in vanilla). Within a layer,
    /// nodes are nudged toward the average position of their neighbours so chains line up
    /// straight, without ever reordering a layer - the ordering stage already decided that,
    /// and undoing it here would reintroduce crossings.
    ///
    /// This is the "priority method": dummy nodes pull hardest, so long edges come out
    /// straight, then real nodes in descending degree order.
    /// </summary>
    public static class CoordinateAssigner
    {
        /// <summary>
        /// Vertical space a dummy node occupies, as a fraction of a real node's row.
        ///
        /// A dummy stands for an edge passing through a column, so it needs about the width
        /// of a line, not the height of a research card. Giving dummies a full row makes
        /// every long edge inflate every column it crosses, which is what pushed real nodes
        /// apart and left large gaps down a column.
        /// </summary>
        private const float DummyRowFraction = 0.22f;

        public static void Assign(LayeredGraph graph, float yStep, int sweeps, out float[] y)
        {
            var positions = new float[graph.TotalNodeCount];
            var gaps = new List<float[]>(graph.LayerCount);

            for (int l = 0; l < graph.LayerCount; l++)
            {
                var layer = graph.Layers[l];
                var layerGaps = new float[layer.Count];

                float cursor = 0f;
                for (int i = 0; i < layer.Count; i++)
                {
                    if (i > 0)
                    {
                        layerGaps[i] = Separation(graph, layer[i - 1], layer[i], yStep);
                        cursor += layerGaps[i];
                    }
                    positions[layer[i]] = cursor;
                }
                gaps.Add(layerGaps);
            }

            if (sweeps < 1) sweeps = 1;

            for (int iteration = 0; iteration < sweeps; iteration++)
            {
                bool downward = (iteration % 2) == 0;

                if (downward)
                {
                    for (int l = 1; l < graph.LayerCount; l++) RefineLayer(graph, l, positions, graph.Up, gaps[l]);
                }
                else
                {
                    for (int l = graph.LayerCount - 2; l >= 0; l--) RefineLayer(graph, l, positions, graph.Down, gaps[l]);
                }
            }

            Normalize(positions);
            y = positions;
        }

        /// <summary>Required space between two vertically adjacent nodes in one layer.</summary>
        private static float Separation(LayeredGraph graph, int above, int below, float yStep)
        {
            return (Footprint(graph, above, yStep) + Footprint(graph, below, yStep)) * 0.5f;
        }

        private static float Footprint(LayeredGraph graph, int node, float yStep)
        {
            return graph.IsDummy(node) ? yStep * DummyRowFraction : yStep;
        }

        private static void RefineLayer(LayeredGraph graph, int layerIndex, float[] y, List<int>[] neighbours, float[] gaps)
        {
            var layer = graph.Layers[layerIndex];
            if (layer.Count == 0) return;

            var settled = new bool[layer.Count];

            var byPriority = new List<int>();
            for (int i = 0; i < layer.Count; i++) byPriority.Add(i);

            byPriority.Sort(delegate (int a, int b)
            {
                int pa = Priority(graph, layer[a], neighbours);
                int pb = Priority(graph, layer[b], neighbours);
                if (pa != pb) return pb.CompareTo(pa);
                return a.CompareTo(b);
            });

            for (int k = 0; k < byPriority.Count; k++)
            {
                int index = byPriority[k];
                int node = layer[index];
                var linked = neighbours[node];
                if (linked.Count == 0)
                {
                    settled[index] = true;
                    continue;
                }

                float sum = 0f;
                for (int i = 0; i < linked.Count; i++) sum += y[linked[i]];
                MoveTo(layer, y, settled, index, sum / linked.Count, gaps);
                settled[index] = true;
            }
        }

        private static int Priority(LayeredGraph graph, int node, List<int>[] neighbours)
        {
            // Dummies outrank everything so long edges stay straight.
            if (graph.IsDummy(node)) return int.MaxValue;
            return neighbours[node].Count;
        }

        /// <summary>
        /// Shifts one node toward <paramref name="desired"/>, stopping short of any already
        /// settled node and dragging unsettled neighbours along to preserve the minimum gap.
        /// Order within the layer is never changed.
        /// </summary>
        private static void MoveTo(List<int> layer, float[] y, bool[] settled, int index, float desired, float[] gaps)
        {
            int node = layer[index];
            float current = y[node];

            if (desired > current)
            {
                float limit = float.MaxValue;
                for (int j = index + 1; j < layer.Count; j++)
                {
                    if (settled[j])
                    {
                        float required = 0f;
                        for (int k = index + 1; k <= j; k++) required += gaps[k];
                        limit = y[layer[j]] - required;
                        break;
                    }
                }

                float target = desired < limit ? desired : limit;
                if (target <= current) return;

                y[node] = target;
                for (int j = index + 1; j < layer.Count && !settled[j]; j++)
                {
                    float minimum = y[layer[j - 1]] + gaps[j];
                    if (y[layer[j]] < minimum) y[layer[j]] = minimum;
                    else break;
                }
            }
            else if (desired < current)
            {
                float limit = float.MinValue;
                for (int j = index - 1; j >= 0; j--)
                {
                    if (settled[j])
                    {
                        float required = 0f;
                        for (int k = j + 1; k <= index; k++) required += gaps[k];
                        limit = y[layer[j]] + required;
                        break;
                    }
                }

                float target = desired > limit ? desired : limit;
                if (target >= current) return;

                y[node] = target;
                for (int j = index - 1; j >= 0 && !settled[j]; j--)
                {
                    float maximum = y[layer[j + 1]] - gaps[j + 1];
                    if (y[layer[j]] > maximum) y[layer[j]] = maximum;
                    else break;
                }
            }
        }

        private static void Normalize(float[] y)
        {
            if (y.Length == 0) return;

            float min = float.MaxValue;
            for (int i = 0; i < y.Length; i++) if (y[i] < min) min = y[i];
            if (min == 0f) return;
            for (int i = 0; i < y.Length; i++) y[i] -= min;
        }
    }
}
