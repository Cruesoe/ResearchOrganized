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

        /// <summary>
        /// Preferred row for each node, normally derived from its authored Y coordinate.
        /// Used only as a tie-break after connector crossings, never as a hard constraint.
        /// </summary>
        public int[] preferredRow;

        /// <summary>
        /// A node that always goes last in its epoch, after everything else is placed,
        /// regardless of what prerequisites it does or does not have - an era's "advance to
        /// the next tech level" node, from mods like Node Research.
        /// </summary>
        public bool[] isCapstone;

        /// <summary>
        /// Allows linked capstones to sit immediately after their furthest prerequisite
        /// instead of after every unrelated project in the epoch. Unlinked capstones still
        /// go last. Used for compact compatibility layouts such as Node Research's VFE
        /// Tribals emergence node.
        /// </summary>
        public bool compactLinkedCapstones;
    }

    public sealed class LayoutResult
    {
        public float[] X;
        public float[] Y;

        /// <summary>Column index per node.</summary>
        public int[] Layer;

        public HashSet<int> NodesInCycles = new HashSet<int>();
        public List<LayoutGraph.Edge> ReversedEdges = new List<LayoutGraph.Edge>();

        public int InitialCrossings;
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

            var tieRank = options.tieRank ?? DefaultRank(graph.NodeCount);
            var broken = CycleBreaker.Break(graph, tieRank);
            result.ReversedEdges = broken.ReversedEdges;
            result.NodesInCycles = broken.NodesInCycles;

            var column = new int[graph.NodeCount];
            var row = new int[graph.NodeCount];

            var epoch = options.epoch ?? new int[graph.NodeCount];
            var isAnchor = options.isAnchor ?? new bool[graph.NodeCount];
            var anchorOrder = options.anchorOrder ?? new int[graph.NodeCount];
            var isCapstone = options.isCapstone ?? new bool[graph.NodeCount];

            EpochLayout.Compute(broken.Acyclic, options, epoch, isAnchor, anchorOrder, tieRank, isCapstone, column, row);
            result.InitialCrossings = CrossingCounter.Count(broken.Acyclic, column, row, options.xStep, options.yStep);
            RowOptimizer.Improve(broken.Acyclic, options, isAnchor, column, row);

            for (int node = 0; node < graph.NodeCount; node++)
            {
                result.Layer[node] = column[node];
                result.X[node] = column[node] * options.xStep;
                result.Y[node] = row[node] * options.yStep;
            }

            result.Crossings = CrossingCounter.Count(broken.Acyclic, column, row, options.xStep, options.yStep);
            return result;
        }

        /// <summary>
        /// Lays out each bucket as if it were a tab of its own, then sets the buckets side by
        /// side in ascending bucket order with <paramref name="gapColumns"/> blank columns
        /// between them, so a later bucket never starts before an earlier one ends. Edges
        /// between buckets are still drawn but never steer placement, the same as edges
        /// between tabs. Crossing counts are summed per bucket.
        /// </summary>
        public static LayoutResult ComputeBuckets(LayoutGraph graph, LayoutOptions options, int[] bucket, int gapColumns)
        {
            if (options == null) options = new LayoutOptions();

            int n = graph.NodeCount;
            var result = new LayoutResult
            {
                X = new float[n],
                Y = new float[n],
                Layer = new int[n]
            };
            if (n == 0) return result;

            var bucketValues = new List<int>(new HashSet<int>(bucket));
            bucketValues.Sort();

            int columnOffset = 0;
            foreach (int value in bucketValues)
            {
                var members = new List<int>();
                for (int node = 0; node < n; node++) if (bucket[node] == value) members.Add(node);

                var localIndex = new Dictionary<int, int>(members.Count);
                for (int i = 0; i < members.Count; i++) localIndex[members[i]] = i;

                var sub = new LayoutGraph(members.Count);
                for (int i = 0; i < members.Count; i++)
                {
                    var children = graph.ChildrenOf(members[i]);
                    for (int c = 0; c < children.Count; c++)
                    {
                        if (localIndex.TryGetValue(children[c], out int child)) sub.AddEdge(i, child);
                    }
                }

                var subOptions = new LayoutOptions
                {
                    xStep = options.xStep,
                    yStep = options.yStep,
                    maxNodesPerColumn = options.maxNodesPerColumn,
                    compactLinkedCapstones = options.compactLinkedCapstones,
                    epoch = Subset(options.epoch, members),
                    isAnchor = Subset(options.isAnchor, members),
                    anchorOrder = Subset(options.anchorOrder, members),
                    tieRank = Subset(options.tieRank, members),
                    preferredRow = Subset(options.preferredRow, members),
                    isCapstone = Subset(options.isCapstone, members)
                };

                var part = Compute(sub, subOptions);

                int width = 0;
                for (int i = 0; i < members.Count; i++)
                {
                    int column = part.Layer[i] + columnOffset;
                    result.Layer[members[i]] = column;
                    result.X[members[i]] = column * options.xStep;
                    result.Y[members[i]] = part.Y[i];
                    if (part.Layer[i] + 1 > width) width = part.Layer[i] + 1;
                }

                foreach (var edge in part.ReversedEdges)
                    result.ReversedEdges.Add(new LayoutGraph.Edge(members[edge.Parent], members[edge.Child]));
                foreach (int node in part.NodesInCycles) result.NodesInCycles.Add(members[node]);
                result.InitialCrossings += part.InitialCrossings;
                result.Crossings += part.Crossings;

                columnOffset += width + gapColumns;
            }

            return result;
        }

        private static T[] Subset<T>(T[] values, List<int> members)
        {
            if (values == null) return null;
            var subset = new T[members.Count];
            for (int i = 0; i < members.Count; i++) subset[i] = values[members[i]];
            return subset;
        }

        private static int[] DefaultRank(int count)
        {
            var rank = new int[count];
            for (int i = 0; i < count; i++) rank[i] = i;
            return rank;
        }
    }
}
