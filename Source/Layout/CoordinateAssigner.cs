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
    /// HARD RULE: a component is never taller than maxNodesPerColumn rows. The research
    /// window does not scroll comfortably in Y, so a tab that grows downward runs off the
    /// bottom. Overflow has to go sideways instead, which is what the column cap in
    /// <see cref="Layering"/> arranges and what <see cref="EnforceBounds"/> guarantees here.
    /// </summary>
    public static class CoordinateAssigner
    {
        /// <summary>
        /// Assigns Y positions. Real nodes stay at least yStep apart; the whole component
        /// stays within (maxNodesPerColumn - 1) * yStep.
        /// </summary>
        public static void Assign(LayeredGraph graph, float yStep, int maxNodesPerColumn, int sweeps, out float[] y)
        {
            var positions = new float[graph.TotalNodeCount];
            var gaps = new List<float[]>(graph.LayerCount);

            for (int l = 0; l < graph.LayerCount; l++)
            {
                var layer = graph.Layers[l];
                var layerGaps = ComputeGaps(graph, layer, yStep);

                float cursor = 0f;
                for (int i = 0; i < layer.Count; i++)
                {
                    cursor += layerGaps[i];
                    positions[layer[i]] = cursor;
                }
                gaps.Add(layerGaps);
            }

            // Layering caps real nodes per layer, so the required height is always
            // achievable; a value of 0 or less means the caller wants no cap at all.
            float cap = maxNodesPerColumn > 0 ? (maxNodesPerColumn - 1) * yStep : float.MaxValue;

            if (sweeps < 1) sweeps = 1;

            for (int iteration = 0; iteration < sweeps; iteration++)
            {
                bool downward = (iteration % 2) == 0;

                if (downward)
                {
                    for (int l = 1; l < graph.LayerCount; l++) RefineLayer(graph, l, positions, graph.Up, gaps[l], cap);
                }
                else
                {
                    for (int l = graph.LayerCount - 2; l >= 0; l--) RefineLayer(graph, l, positions, graph.Down, gaps[l], cap);
                }
            }

            for (int l = 0; l < graph.LayerCount; l++) EnforceBounds(graph.Layers[l], positions, gaps[l], cap);

            Normalize(positions);
            y = positions;
        }

        /// <summary>
        /// Minimum spacing between each node and the one above it in the same layer.
        ///
        /// Only real nodes reserve height: consecutive cards are always a full yStep apart,
        /// and any routed edges sitting between them share that one row rather than each
        /// claiming their own. So a column's height depends on how many cards it holds and
        /// not at all on how many edges pass through it.
        ///
        /// Splitting the row between the dummies matters - giving them zero spacing lets two
        /// cards with an edge routed between them land on the same coordinate and overlap.
        /// </summary>
        private static float[] ComputeGaps(LayeredGraph graph, List<int> layer, float yStep)
        {
            var gaps = new float[layer.Count];

            int previousReal = -1;
            for (int i = 0; i < layer.Count; i++)
            {
                if (graph.IsDummy(layer[i])) continue;

                if (previousReal >= 0)
                {
                    int steps = i - previousReal;
                    float share = yStep / steps;
                    for (int k = previousReal + 1; k <= i; k++) gaps[k] = share;
                }
                previousReal = i;
            }

            // Dummies before the first card or after the last one cost nothing at all.
            return gaps;
        }

        /// <summary>
        /// Restores the two invariants after the sweeps: every pair keeps its required gap,
        /// and nothing sits outside [0, cap]. Forward pass pushes down, backward pass pulls
        /// back up. Since the gaps in a layer sum to at most cap, both always succeed.
        /// </summary>
        private static void EnforceBounds(List<int> layer, float[] y, float[] gaps, float cap)
        {
            if (layer.Count == 0) return;

            if (y[layer[0]] < 0f) y[layer[0]] = 0f;
            for (int i = 1; i < layer.Count; i++)
            {
                float minimum = y[layer[i - 1]] + gaps[i];
                if (y[layer[i]] < minimum) y[layer[i]] = minimum;
            }

            if (cap == float.MaxValue) return;

            if (y[layer[layer.Count - 1]] > cap) y[layer[layer.Count - 1]] = cap;
            for (int i = layer.Count - 2; i >= 0; i--)
            {
                float maximum = y[layer[i + 1]] - gaps[i + 1];
                if (y[layer[i]] > maximum) y[layer[i]] = maximum;
            }

            if (y[layer[0]] < 0f) y[layer[0]] = 0f;
        }

        private static void RefineLayer(LayeredGraph graph, int layerIndex, float[] y, List<int>[] neighbours, float[] gaps, float cap)
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

                float desired = sum / linked.Count;
                if (desired < 0f) desired = 0f;
                if (desired > cap) desired = cap;

                MoveTo(layer, y, settled, index, desired, gaps);
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
