using System.Collections.Generic;

namespace ResearchOrganized.Layout
{
    public sealed class LayoutOptions
    {
        public float xStep = 1f;
        public float yStep = 0.63f;

        /// <summary>Hard cap on cards per column. 0 or less means unbounded.</summary>
        public int maxNodesPerColumn = 12;

        /// <summary>Which tech level (or other grouping) each node belongs to. Processed in ascending order.</summary>
        public int[] epoch;

        /// <summary>Whether each node is a hub that should hold a column of its own.</summary>
        public bool[] isAnchor;

        /// <summary>
        /// Global placement order for anchors, lower first - normally shallowest in the
        /// research tree first, so a node depending on two anchors reads as belonging to the
        /// deeper, more specific one.
        /// </summary>
        public int[] anchorOrder;

        /// <summary>Tie-break for otherwise-equal placement choices, lower first - the caller puts cheap projects first.</summary>
        public int[] tieRank;
    }

    public sealed class LayoutResult
    {
        public float[] X;
        public float[] Y;

        /// <summary>Column index per node.</summary>
        public int[] Layer;

        public HashSet<int> NodesInCycles = new HashSet<int>();
        public List<LayoutGraph.Edge> ReversedEdges = new List<LayoutGraph.Edge>();

        public int Crossings;
    }

    /// <summary>
    /// Lays out one research tab. Cycles (a mod's malformed prerequisites) are broken first so
    /// the rest of the pipeline can assume a clean DAG; the actual placement is
    /// <see cref="EpochLayout"/>, which knows nothing about RimWorld and is covered by the
    /// test project.
    /// </summary>
    public static class TabLayout
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

            var column = new int[graph.NodeCount];
            var row = new int[graph.NodeCount];

            var epoch = options.epoch ?? new int[graph.NodeCount];
            var isAnchor = options.isAnchor ?? new bool[graph.NodeCount];
            var anchorOrder = options.anchorOrder ?? new int[graph.NodeCount];
            var tieRank = options.tieRank ?? DefaultRank(graph.NodeCount);

            EpochLayout.Compute(broken.Acyclic, options, epoch, isAnchor, anchorOrder, tieRank, column, row);

            for (int node = 0; node < graph.NodeCount; node++)
            {
                result.Layer[node] = column[node];
                result.X[node] = column[node] * options.xStep;
                result.Y[node] = row[node] * options.yStep;
            }

            result.Crossings = CrossingCounter.Count(broken.Acyclic, column, row);
            return result;
        }

        private static int[] DefaultRank(int count)
        {
            var rank = new int[count];
            for (int i = 0; i < count; i++) rank[i] = i;
            return rank;
        }
    }
}
