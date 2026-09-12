using System;
using System.Collections.Generic;

namespace ResearchOrganized.Layout
{
    /// <summary>
    /// Counts unique pairs of connector lines that cross. Long edges are split into virtual
    /// segments at column boundaries so crossings through intermediate columns are included.
    /// </summary>
    public static class CrossingCounter
    {
        private const double Epsilon = 0.000001;

        private struct Segment
        {
            public int EdgeIndex;
            public int Parent;
            public int Child;
            public double StartRow;
            public double EndRow;
        }

        public static int Count(LayoutGraph graph, int[] column, int[] row)
        {
            var buckets = new Dictionary<int, List<Segment>>();
            List<LayoutGraph.Edge> edges = graph.AllEdges();

            for (int edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
            {
                LayoutGraph.Edge edge = edges[edgeIndex];
                int startColumn = column[edge.Parent];
                int endColumn = column[edge.Child];
                double startRow = row[edge.Parent];
                double endRow = row[edge.Child];

                if (startColumn == endColumn) continue;
                if (startColumn > endColumn)
                {
                    int columnSwap = startColumn;
                    startColumn = endColumn;
                    endColumn = columnSwap;
                    double rowSwap = startRow;
                    startRow = endRow;
                    endRow = rowSwap;
                }

                int span = endColumn - startColumn;
                for (int boundary = startColumn; boundary < endColumn; boundary++)
                {
                    double startT = (double)(boundary - startColumn) / span;
                    double endT = (double)(boundary + 1 - startColumn) / span;
                    var segment = new Segment
                    {
                        EdgeIndex = edgeIndex,
                        Parent = edge.Parent,
                        Child = edge.Child,
                        StartRow = startRow + (endRow - startRow) * startT,
                        EndRow = startRow + (endRow - startRow) * endT
                    };

                    if (!buckets.TryGetValue(boundary, out List<Segment> bucket))
                    {
                        bucket = new List<Segment>();
                        buckets[boundary] = bucket;
                    }
                    bucket.Add(segment);
                }
            }

            var crossingPairs = new HashSet<long>();
            foreach (List<Segment> bucket in buckets.Values)
            {
                for (int i = 0; i < bucket.Count; i++)
                {
                    for (int j = i + 1; j < bucket.Count; j++)
                    {
                        Segment first = bucket[i];
                        Segment second = bucket[j];
                        if (first.Parent == second.Parent || first.Parent == second.Child
                            || first.Child == second.Parent || first.Child == second.Child) continue;
                        if (!SegmentsCross(first, second)) continue;

                        int low = Math.Min(first.EdgeIndex, second.EdgeIndex);
                        int high = Math.Max(first.EdgeIndex, second.EdgeIndex);
                        crossingPairs.Add(((long)low << 32) ^ (uint)high);
                    }
                }
            }

            return crossingPairs.Count;
        }

        private static bool SegmentsCross(Segment first, Segment second)
        {
            double startDifference = first.StartRow - second.StartRow;
            double endDifference = first.EndRow - second.EndRow;
            if (Math.Abs(startDifference) <= Epsilon && Math.Abs(endDifference) <= Epsilon) return false;
            if (startDifference * endDifference < -Epsilon) return true;
            return Math.Abs(startDifference) <= Epsilon || Math.Abs(endDifference) <= Epsilon;
        }
    }
}
