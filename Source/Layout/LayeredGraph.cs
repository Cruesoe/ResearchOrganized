using System.Collections.Generic;

namespace ResearchOrganized.Layout
{
    /// <summary>
    /// A graph split into layers, with dummy nodes inserted so that every edge connects
    /// two adjacent layers. Long edges become chains of dummies, which is what lets the
    /// ordering stage reason about crossings uniformly and the coordinate stage draw long
    /// dependency chains as straight lines rather than steep diagonals.
    /// </summary>
    public sealed class LayeredGraph
    {
        public int RealNodeCount;
        public int TotalNodeCount;

        /// <summary>Layer index per node (real and dummy).</summary>
        public int[] Layer;

        /// <summary>Layer index -> nodes in that layer, in current left-to-right order.</summary>
        public List<int>[] Layers;

        /// <summary>Neighbours one layer further along (children side).</summary>
        public List<int>[] Down;

        /// <summary>Neighbours one layer back (parents side).</summary>
        public List<int>[] Up;

        public bool IsDummy(int node)
        {
            return node >= RealNodeCount;
        }

        public int LayerCount
        {
            get { return Layers.Length; }
        }

        public static LayeredGraph Build(LayoutGraph graph, int[] layerOf)
        {
            int maxLayer = 0;
            for (int i = 0; i < layerOf.Length; i++) if (layerOf[i] > maxLayer) maxLayer = layerOf[i];

            var result = new LayeredGraph();
            result.RealNodeCount = graph.NodeCount;

            var layerList = new List<int>(layerOf);
            var down = new List<List<int>>();
            var up = new List<List<int>>();
            for (int i = 0; i < graph.NodeCount; i++)
            {
                down.Add(new List<int>());
                up.Add(new List<int>());
            }

            foreach (var edge in graph.AllEdges())
            {
                int from = edge.Parent;
                int to = edge.Child;
                int span = layerOf[to] - layerOf[from];

                if (span == 1)
                {
                    down[from].Add(to);
                    up[to].Add(from);
                    continue;
                }

                // Walk the gap, creating one dummy per intermediate layer.
                int previous = from;
                for (int layer = layerOf[from] + 1; layer < layerOf[to]; layer++)
                {
                    int dummy = layerList.Count;
                    layerList.Add(layer);
                    down.Add(new List<int>());
                    up.Add(new List<int>());

                    down[previous].Add(dummy);
                    up[dummy].Add(previous);
                    previous = dummy;
                }
                down[previous].Add(to);
                up[to].Add(previous);
            }

            result.TotalNodeCount = layerList.Count;
            result.Layer = layerList.ToArray();
            result.Down = down.ToArray();
            result.Up = up.ToArray();

            result.Layers = new List<int>[maxLayer + 1];
            for (int i = 0; i <= maxLayer; i++) result.Layers[i] = new List<int>();
            for (int node = 0; node < result.TotalNodeCount; node++) result.Layers[result.Layer[node]].Add(node);

            return result;
        }

        /// <summary>Index of each node within its own layer.</summary>
        public int[] BuildPositions()
        {
            var pos = new int[TotalNodeCount];
            for (int l = 0; l < Layers.Length; l++)
            {
                var layer = Layers[l];
                for (int i = 0; i < layer.Count; i++) pos[layer[i]] = i;
            }
            return pos;
        }

        /// <summary>
        /// Edge crossings between layer <paramref name="upper"/> and the layer after it.
        /// Two edges cross when their endpoints are ordered oppositely on the two layers,
        /// so this is an inversion count over the lower endpoints once the edges are read
        /// left to right along the upper layer.
        ///
        /// Counted with a Fenwick tree in O(E log n). The naive pairwise version is O(E^2),
        /// which is fine for a toy graph and disastrous for a real modlist - it is called
        /// from inside the ordering loop.
        /// </summary>
        public int CountCrossingsBetween(int upper, int[] pos)
        {
            if (upper < 0 || upper + 1 >= Layers.Length) return 0;

            var layer = Layers[upper];
            int lowerSize = Layers[upper + 1].Count;
            if (lowerSize == 0) return 0;

            // Edges in left-to-right order of their upper endpoint; ties by lower endpoint.
            var lowerEndpoints = new List<int>();
            for (int i = 0; i < layer.Count; i++)
            {
                int node = layer[i];
                var children = Down[node];
                if (children.Count == 0) continue;

                var group = new List<int>(children.Count);
                for (int c = 0; c < children.Count; c++) group.Add(pos[children[c]]);
                group.Sort();
                lowerEndpoints.AddRange(group);
            }

            // Inversions: for each endpoint, how many already-seen endpoints sit to its right.
            var tree = new int[lowerSize + 1];
            int crossings = 0;
            int seen = 0;

            for (int i = 0; i < lowerEndpoints.Count; i++)
            {
                int value = lowerEndpoints[i] + 1;

                int atOrBefore = 0;
                for (int j = value; j > 0; j -= j & (-j)) atOrBefore += tree[j];

                crossings += seen - atOrBefore;
                seen++;

                for (int j = value; j <= lowerSize; j += j & (-j)) tree[j]++;
            }

            return crossings;
        }

        public int CountCrossings()
        {
            var pos = BuildPositions();
            int total = 0;
            for (int l = 0; l + 1 < Layers.Length; l++) total += CountCrossingsBetween(l, pos);
            return total;
        }
    }
}
