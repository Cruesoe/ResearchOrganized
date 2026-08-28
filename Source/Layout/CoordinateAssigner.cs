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
        public static void Assign(LayeredGraph graph, float yStep, int sweeps, out float[] y)
        {
            var positions = new float[graph.TotalNodeCount];
            for (int l = 0; l < graph.LayerCount; l++)
            {
                var layer = graph.Layers[l];
                for (int i = 0; i < layer.Count; i++) positions[layer[i]] = i * yStep;
            }

            if (sweeps < 1) sweeps = 1;

            for (int iteration = 0; iteration < sweeps; iteration++)
            {
                bool downward = (iteration % 2) == 0;

                if (downward)
                {
                    for (int l = 1; l < graph.LayerCount; l++) RefineLayer(graph, l, positions, graph.Up, yStep);
                }
                else
                {
                    for (int l = graph.LayerCount - 2; l >= 0; l--) RefineLayer(graph, l, positions, graph.Down, yStep);
                }
            }

            Normalize(positions);
            y = positions;
        }

        private static void RefineLayer(LayeredGraph graph, int layerIndex, float[] y, List<int>[] neighbours, float yStep)
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
                MoveTo(layer, y, settled, index, sum / linked.Count, yStep);
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
        private static void MoveTo(List<int> layer, float[] y, bool[] settled, int index, float desired, float gap)
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
                        limit = y[layer[j]] - gap * (j - index);
                        break;
                    }
                }

                float target = desired < limit ? desired : limit;
                if (target <= current) return;

                y[node] = target;
                for (int j = index + 1; j < layer.Count && !settled[j]; j++)
                {
                    float minimum = y[layer[j - 1]] + gap;
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
                        limit = y[layer[j]] + gap * (index - j);
                        break;
                    }
                }

                float target = desired > limit ? desired : limit;
                if (target >= current) return;

                y[node] = target;
                for (int j = index - 1; j >= 0 && !settled[j]; j--)
                {
                    float maximum = y[layer[j + 1]] - gap;
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
