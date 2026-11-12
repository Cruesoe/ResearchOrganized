using System.Collections.Generic;

namespace ResearchOrganized.Layout
{
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

            var isMajorAnchor = options.isMajorAnchor ?? new bool[graph.NodeCount];
            var isFinale = options.isFinale ?? new bool[graph.NodeCount];
            EpochLayout.Compute(broken.Acyclic, options, epoch, isAnchor, isMajorAnchor, isFinale, anchorOrder, tieRank, isCapstone, column, row);

            // Columns come from every link; rows are tidied against the ones with a connector on
            // screen. A hidden prerequisite still puts a card to the right of what it needs, but
            // never drags one up or down to untangle a line nobody can see.
            var drawn = broken.Acyclic.DrawnOnly();
            result.InitialCrossings = CrossingCounter.Count(drawn, column, row, options.xStep, options.yStep);
            RowOptimizer.Improve(drawn, options, isAnchor, column, row);

            for (int node = 0; node < graph.NodeCount; node++)
            {
                result.Layer[node] = column[node];
                result.X[node] = column[node] * options.xStep;
                result.Y[node] = row[node] * options.yStep;
            }

            result.Crossings = CrossingCounter.Count(drawn, column, row, options.xStep, options.yStep);
            return result;
        }

        /// <summary>
        /// Lays out each bucket as if it were a tab of its own, then sets the buckets side by
        /// side in ascending bucket order with a <paramref name="gapColumns"/>-column gap (fractions allowed)
        /// between them, so a later bucket never starts before an earlier one ends. The gap is in X only:
        /// <see cref="LayoutResult.Layer"/> numbers columns across buckets without it. An edge
        /// between two buckets never steers placement, the same as an edge between two tabs.
        /// Crossing counts are summed per bucket.
        /// </summary>
        public static LayoutResult ComputeBuckets(LayoutGraph graph, LayoutOptions options, int[] bucket, float gapColumns)
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
            float gapOffset = 0f;
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
                        if (localIndex.TryGetValue(children[c], out int child))
                            sub.AddEdge(i, child, graph.IsDrawn(members[i], children[c]));
                    }
                }

                var subOptions = new LayoutOptions
                {
                    xStep = options.xStep,
                    yStep = options.yStep,
                    maxNodesPerColumn = options.maxNodesPerColumn,
                    epoch = Subset(options.epoch, members),
                    isAnchor = Subset(options.isAnchor, members),
                    isMajorAnchor = Subset(options.isMajorAnchor, members),
                    isFinale = Subset(options.isFinale, members),
                    anchorOrder = Subset(options.anchorOrder, members),
                    tieRank = Subset(options.tieRank, members),
                    isCapstone = Subset(options.isCapstone, members),
                    trace = options.trace,
                    nodeNames = Subset(options.nodeNames, members)
                };
                if (options.trace != null)
                    options.trace.Add(string.Format("== bucket {0}: {1} projects, starts at column {2} (columns below are local to the bucket)", value, members.Count, columnOffset));

                var part = Compute(sub, subOptions);

                int width = 0;
                for (int i = 0; i < members.Count; i++)
                {
                    int column = part.Layer[i] + columnOffset;
                    result.Layer[members[i]] = column;
                    result.X[members[i]] = (column + gapOffset) * options.xStep;
                    result.Y[members[i]] = part.Y[i];
                    if (part.Layer[i] + 1 > width) width = part.Layer[i] + 1;
                }

                foreach (var edge in part.ReversedEdges)
                    result.ReversedEdges.Add(new LayoutGraph.Edge(members[edge.Parent], members[edge.Child]));
                foreach (int node in part.NodesInCycles) result.NodesInCycles.Add(members[node]);
                result.InitialCrossings += part.InitialCrossings;
                result.Crossings += part.Crossings;

                columnOffset += width;
                gapOffset += gapColumns;
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
