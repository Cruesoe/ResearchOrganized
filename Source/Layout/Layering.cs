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

            var members = new Dictionary<int, List<int>>();

            for (int i = 0; i < order.Count; i++)
            {
                int node = order[i];
                if (effectivePin[node]) continue;
                Place(graph, node, depth, rank, maxWidth, column, members, 0);
            }

            {
                int finalColumn = maxDepth;
                foreach (var pair in members) if (pair.Key > finalColumn) finalColumn = pair.Key;
                finalColumn++;

                for (int i = 0; i < order.Count; i++)
                {
                    int node = order[i];
                    if (!effectivePin[node]) continue;
                    Place(graph, node, depth, rank, maxWidth, column, members, finalColumn);
                }
            }

            return column;
        }

        /// <summary>
        /// Places one node in the leftmost column at or after its depth that will take it.
        ///
        /// When the wanted column is full, the node does NOT get flung to the far right -
        /// that produced edges stretching the width of the tab. It takes the place of the
        /// least important node already sitting there, if it outranks one, and that node
        /// moves along to the next column instead. Only a node at the same depth can be
        /// displaced, so a parent is never pushed past a child that has already been placed.
        /// </summary>
        private static void Place(LayoutGraph graph, int node, int[] depth, int[] rank, int maxWidth,
                                  int[] column, Dictionary<int, List<int>> members, int minColumn)
        {
            int chosen = depth[node];
            if (minColumn > chosen) chosen = minColumn;

            var parents = graph.ParentsOf(node);
            for (int p = 0; p < parents.Count; p++)
            {
                int after = column[parents[p]] + 1;
                if (after > chosen) chosen = after;
            }

            while (true)
            {
                List<int> holding;
                if (!members.TryGetValue(chosen, out holding))
                {
                    holding = new List<int>();
                    members[chosen] = holding;
                }

                if (maxWidth <= 0 || holding.Count < maxWidth)
                {
                    holding.Add(node);
                    column[node] = chosen;
                    return;
                }

                int displaced = PickDisplaced(graph, holding, node, depth, rank);
                if (displaced >= 0)
                {
                    holding.Remove(displaced);
                    holding.Add(node);
                    column[node] = chosen;

                    // The displaced node must land further right, never back here.
                    Place(graph, displaced, depth, rank, maxWidth, column, members, chosen + 1);
                    return;
                }

                chosen++;
            }
        }

        /// <summary>
        /// Chooses which node in a full column should move along, or -1 to leave them all.
        ///
        /// First preference is a node that does not belong in this column at all: one that
        /// overflowed here from a shallower depth. A column should go to the nodes whose
        /// depth it represents, and yielding it costs the overflowed node nothing it had a
        /// claim to. Only childless ones though - shifting a node that already has followers
        /// placed could push it past them.
        ///
        /// Otherwise a peer at the same depth that ranks below the newcomer. Peers are safe
        /// to move regardless, since anything depending on them is deeper and not yet placed.
        /// </summary>
        private static int PickDisplaced(LayoutGraph graph, List<int> holding, int node, int[] depth, int[] rank)
        {
            int overflowed = -1;
            for (int i = 0; i < holding.Count; i++)
            {
                int other = holding[i];
                if (depth[other] >= depth[node]) continue;
                if (graph.ChildrenOf(other).Count > 0) continue;
                if (overflowed < 0 || RankOf(rank, other) > RankOf(rank, overflowed)) overflowed = other;
            }
            if (overflowed >= 0) return overflowed;

            int peer = -1;
            for (int i = 0; i < holding.Count; i++)
            {
                int other = holding[i];
                if (depth[other] != depth[node]) continue;
                if (RankOf(rank, other) <= RankOf(rank, node)) continue;
                if (peer < 0 || RankOf(rank, other) > RankOf(rank, peer)) peer = other;
            }
            return peer;
        }

        private static int RankOf(int[] rank, int node)
        {
            return rank != null ? rank[node] : node;
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
