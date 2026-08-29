using System.Collections.Generic;

namespace ResearchOrganized.Layout
{
    /// <summary>
    /// Counts how many pairs of connector lines cross. Not used to drive the layout - it is
    /// a measurement, so a change can be shown to have improved or hurt readability rather
    /// than argued about.
    ///
    /// Two edges are counted as crossing when they start in the same column as each other,
    /// end in the same column as each other, and their endpoints are ordered oppositely.
    /// Edges between different column pairs are ignored: they are not comparable, and
    /// guessing at their geometry would make the number less trustworthy, not more.
    /// </summary>
    public static class CrossingCounter
    {
        public static int Count(LayoutGraph graph, int[] column, int[] row)
        {
            var buckets = new Dictionary<long, List<LayoutGraph.Edge>>();

            foreach (var edge in graph.AllEdges())
            {
                long key = ((long)column[edge.Parent] << 32) ^ (uint)column[edge.Child];

                List<LayoutGraph.Edge> bucket;
                if (!buckets.TryGetValue(key, out bucket))
                {
                    bucket = new List<LayoutGraph.Edge>();
                    buckets[key] = bucket;
                }
                bucket.Add(edge);
            }

            int crossings = 0;
            foreach (var bucket in buckets.Values)
            {
                for (int i = 0; i < bucket.Count; i++)
                {
                    for (int j = i + 1; j < bucket.Count; j++)
                    {
                        int aStart = row[bucket[i].Parent], aEnd = row[bucket[i].Child];
                        int bStart = row[bucket[j].Parent], bEnd = row[bucket[j].Child];

                        if ((aStart < bStart && aEnd > bEnd) || (aStart > bStart && aEnd < bEnd)) crossings++;
                    }
                }
            }
            return crossings;
        }
    }
}
