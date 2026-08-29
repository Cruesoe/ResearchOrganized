using System;
using System.Collections.Generic;

namespace ResearchOrganized.Layout
{
    /// <summary>
    /// Decides which column each node sits in.
    ///
    /// A column means something to the reader: it is how deep a project sits in the
    /// prerequisite chain, so column 0 is "you can research this right now". That has to
    /// hold across the whole tab, not per fragment - a project with no prerequisites must
    /// never appear to the right of one that has them, because the eye reads that as
    /// "comes after".
    ///
    /// Columns 0..maxDepth are reserved for nodes of exactly that depth. When a column is
    /// full, the lowest-priority nodes spill into columns past maxDepth rather than into the
    /// next depth's column, so a project that fits always stays beside its parent and only
    /// the least important leaves get pushed out to the side.
    /// </summary>
    public static class Layering
    {
        /// <summary>
        /// Returns column[node]. Requires an acyclic graph - run <see cref="CycleBreaker"/>
        /// first. maxWidth &lt;= 0 means unbounded.
        /// </summary>
        /// <param name="rank">
        /// Optional placement priority, lower first. Nodes competing for a full column are
        /// kept in rank order, so whatever the caller considers important stays put and the
        /// rest spills. Null means node index order.
        /// </param>
        /// <param name="pinLast">
        /// Optional. Nodes forced into the final columns regardless of depth - for a project
        /// that concludes a tab, such as one unlocking the bench the next tab needs.
        /// </param>
        public static int[] Assign(LayoutGraph graph, int maxWidth, int[] rank, bool[] pinLast)
        {
            int count = graph.NodeCount;
            var column = new int[count];
            if (count == 0) return column;

            int[] depth = ComputeDepth(graph);

            int maxDepth = 0;
            for (int i = 0; i < count; i++) if (depth[i] > maxDepth) maxDepth = depth[i];

            var order = new List<int>(count);
            for (int i = 0; i < count; i++) order.Add(i);
            order.Sort(delegate (int a, int b)
            {
                if (depth[a] != depth[b]) return depth[a].CompareTo(depth[b]);
                if (rank != null && rank[a] != rank[b]) return rank[a].CompareTo(rank[b]);
                return a.CompareTo(b);
            });

            // A node with children cannot be pinned: it would land to the right of its own
            // followers, and an edge that runs backwards has no valid routing through the
            // columns. Enforced here rather than trusting the caller, because the failure
            // mode is an exception deep in the crossing counter.
            var effectivePin = new bool[count];
            if (pinLast != null)
            {
                for (int i = 0; i < count; i++) effectivePin[i] = pinLast[i] && graph.ChildrenOf(i).Count == 0;
            }

            var occupancy = new Dictionary<int, int>();
            var placed = new bool[count];

            for (int i = 0; i < order.Count; i++)
            {
                int node = order[i];
                if (effectivePin[node]) continue;

                column[node] = PlaceNode(graph, node, depth[node], maxDepth, maxWidth, column, occupancy);
                placed[node] = true;
            }

            {
                int finalColumn = maxDepth;
                foreach (var pair in occupancy) if (pair.Key > finalColumn) finalColumn = pair.Key;
                finalColumn++;

                for (int i = 0; i < order.Count; i++)
                {
                    int node = order[i];
                    if (!effectivePin[node] || placed[node]) continue;

                    column[node] = PlaceNode(graph, node, finalColumn, int.MaxValue, maxWidth, column, occupancy);
                    placed[node] = true;
                }
            }

            return column;
        }

        private static int PlaceNode(LayoutGraph graph, int node, int idealColumn, int reservedThrough,
                                     int maxWidth, int[] column, Dictionary<int, int> occupancy)
        {
            int earliest = idealColumn;
            var parents = graph.ParentsOf(node);
            for (int p = 0; p < parents.Count; p++)
            {
                int after = column[parents[p]] + 1;
                if (after > earliest) earliest = after;
            }

            int chosen = earliest;
            if (maxWidth > 0)
            {
                int held;
                if (chosen <= reservedThrough && occupancy.TryGetValue(chosen, out held) && held >= maxWidth)
                {
                    // This depth's column is full. Spill sideways instead of stealing the
                    // next depth's column, which would drag this node away from its parent
                    // and push a whole subtree along with it.
                    chosen = reservedThrough + 1;
                    if (chosen < earliest) chosen = earliest;
                }
                while (occupancy.TryGetValue(chosen, out held) && held >= maxWidth) chosen++;
            }

            int existing;
            occupancy[chosen] = occupancy.TryGetValue(chosen, out existing) ? existing + 1 : 1;
            return chosen;
        }

        /// <summary>Longest path from any root, which is the depth a reader perceives.</summary>
        private static int[] ComputeDepth(LayoutGraph graph)
        {
            int count = graph.NodeCount;
            var depth = new int[count];
            var remaining = new int[count];

            var ready = new List<int>();
            for (int i = 0; i < count; i++)
            {
                remaining[i] = graph.ParentsOf(i).Count;
                if (remaining[i] == 0) ready.Add(i);
            }

            int processed = 0;
            while (ready.Count > 0)
            {
                // Smallest index first purely so the result is reproducible.
                int pick = 0;
                for (int i = 1; i < ready.Count; i++) if (ready[i] < ready[pick]) pick = i;

                int node = ready[pick];
                ready.RemoveAt(pick);
                processed++;

                var children = graph.ChildrenOf(node);
                for (int c = 0; c < children.Count; c++)
                {
                    int child = children[c];
                    if (depth[node] + 1 > depth[child]) depth[child] = depth[node] + 1;
                    if (--remaining[child] == 0) ready.Add(child);
                }
            }

            // A well-formed acyclic graph always drains; if something is left, it keeps
            // depth 0 rather than being dropped from the layout.
            if (processed < count) { }

            return depth;
        }
    }
}
