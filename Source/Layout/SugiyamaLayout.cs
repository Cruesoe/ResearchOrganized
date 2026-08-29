using System;
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

        /// <summary>
        /// Optional placement priority per node, lower placed first. Nodes competing for a
        /// full column are kept in this order, so the important ones hold their depth column
        /// and the rest spill sideways.
        /// </summary>
        public int[] rank;

        /// <summary>Optional. Nodes forced into the final columns regardless of depth.</summary>
        public bool[] pinLast;
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
    ///   2. assign columns by prerequisite depth, respecting a maximum column height
    ///   3. order within columns to minimise edge crossings
    ///   4. assign coordinates, straightening long chains
    ///
    /// Stage 2 runs over the whole tab at once rather than per fragment, because a column
    /// index is meaningful to the reader: everything at column 0 is available immediately,
    /// and anything further right depends on something to its left.
    /// </summary>
    public static class SugiyamaLayout
    {
        /// <summary>Width of a column that contains only edge routing, as a fraction of xStep.</summary>
        private const float DummyColumnFraction = 0.3f;

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

            int[] columnOf = Layering.Assign(broken.Acyclic, options.maxNodesPerColumn, options.rank, options.pinLast);

            var layered = LayeredGraph.Build(broken.Acyclic, columnOf);
            Ordering.Optimize(layered, options.orderingSweeps);

            float[] y;
            CoordinateAssigner.Assign(layered, options.yStep, options.maxNodesPerColumn, options.coordinateSweeps, out y);

            result.Crossings = layered.CountCrossings();

            // A column holding nothing but routed edges draws no cards, so it does not need
            // a full column of width. Columns with cards stay at least xStep apart.
            var hasRealNode = new bool[layered.LayerCount];
            for (int l = 0; l < layered.LayerCount; l++)
            {
                var contents = layered.Layers[l];
                for (int i = 0; i < contents.Count; i++)
                {
                    if (!layered.IsDummy(contents[i])) { hasRealNode[l] = true; break; }
                }
            }

            var layerX = new float[layered.LayerCount];
            for (int l = 1; l < layered.LayerCount; l++)
            {
                layerX[l] = layerX[l - 1] + (hasRealNode[l] ? options.xStep : options.xStep * DummyColumnFraction);
            }

            for (int node = 0; node < graph.NodeCount; node++)
            {
                result.Layer[node] = columnOf[node];
                result.X[node] = layerX[columnOf[node]];
                result.Y[node] = y[node];
            }

            return result;
        }
    }
}
