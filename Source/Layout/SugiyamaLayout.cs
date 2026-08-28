using System.Collections.Generic;

namespace ResearchOrganized.Layout
{
    public sealed class LayoutOptions
    {
        public float xStep = 1f;
        public float yStep = 0.63f;

        /// <summary>Cap on nodes per column. 0 or less means unbounded.</summary>
        public int maxNodesPerColumn = 12;

        public int orderingSweeps = 8;
        public int coordinateSweeps = 4;
    }

    public sealed class LayoutResult
    {
        /// <summary>Coordinates indexed by the caller's node id.</summary>
        public float[] X;
        public float[] Y;

        /// <summary>Column index per node.</summary>
        public int[] Layer;

        /// <summary>Nodes that sat on an edge which had to be reversed to break a cycle.</summary>
        public HashSet<int> NodesInCycles = new HashSet<int>();

        public List<LayoutGraph.Edge> ReversedEdges = new List<LayoutGraph.Edge>();

        /// <summary>Edge crossings remaining in the final ordering. Lower is better.</summary>
        public int Crossings;
    }

    /// <summary>
    /// Layered graph drawing, the standard four-stage pipeline:
    ///
    ///   1. break cycles by reversing a small set of edges
    ///   2. assign layers, respecting a maximum column width
    ///   3. order within layers to minimise edge crossings
    ///   4. assign coordinates, straightening long chains
    ///
    /// Stage 3 is the one that matters most for readability and is what the previous
    /// greedy engine lacked entirely.
    /// </summary>
    public static class SugiyamaLayout
    {
        public static LayoutResult Compute(LayoutGraph graph, LayoutOptions options)
        {
            if (options == null) options = new LayoutOptions();

            var result = new LayoutResult
            {
                X = new float[graph.NodeCount],
                Y = new float[graph.NodeCount],
                Layer = new int[graph.NodeCount]
            };

            if (graph.NodeCount == 0) return result;

            var broken = CycleBreaker.Break(graph);
            result.ReversedEdges = broken.ReversedEdges;
            result.NodesInCycles = broken.NodesInCycles;

            int[] layerOf = Layering.Assign(broken.Acyclic, options.maxNodesPerColumn);

            var layered = LayeredGraph.Build(broken.Acyclic, layerOf);
            Ordering.Optimize(layered, options.orderingSweeps);

            float[] y;
            CoordinateAssigner.Assign(layered, options.yStep, options.coordinateSweeps, out y);

            for (int node = 0; node < graph.NodeCount; node++)
            {
                result.Layer[node] = layerOf[node];
                result.X[node] = layerOf[node] * options.xStep;
                result.Y[node] = y[node];
            }

            result.Crossings = layered.CountCrossings();
            return result;
        }
    }
}
