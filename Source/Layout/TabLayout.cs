using System;
using System.Collections.Generic;

namespace ResearchOrganized.Layout
{
    public sealed class LayoutOptions
    {
        public float xStep = 1f;
        public float yStep = 0.63f;

        /// <summary>Hard cap on cards per column. 0 or less means unbounded.</summary>
        public int maxNodesPerColumn = 12;

        /// <summary>Leave a blank row between sibling groups when the column has room.</summary>
        public bool separateGroups = true;

        /// <summary>Passes of reordering within groups to reduce crossing lines.</summary>
        public int refineSweeps = 4;

        /// <summary>
        /// Optional placement priority, lower first. Used to order members inside a group -
        /// the caller puts cheap projects first - and to break ties between groups.
        /// </summary>
        public int[] rank;
    }

    public sealed class LayoutResult
    {
        public float[] X;
        public float[] Y;

        /// <summary>Column index per node.</summary>
        public int[] Layer;

        public HashSet<int> NodesInCycles = new HashSet<int>();
        public List<LayoutGraph.Edge> ReversedEdges = new List<LayoutGraph.Edge>();

        /// <summary>Edge crossings in the final arrangement. Lower is better.</summary>
        public int Crossings;
    }

    /// <summary>
    /// Lays out one research tab.
    ///
    /// The organising idea is that a reader follows GROUPS, not individual cards. A parent's
    /// followers are allocated one contiguous run of cells - filling a column top to bottom
    /// and wrapping into the next - and no other group is placed inside that run. So a
    /// project with two dozen followers shows a fan landing in one solid block, instead of
    /// two dozen lines diffusing across a uniform grid of unrelated cards.
    ///
    /// Everything else follows from that:
    ///
    ///   - Each depth occupies a contiguous band of columns. A depth wider than one column
    ///     simply spans several, which is unavoidable once a hub has more followers than a
    ///     column can hold, and honest about what is being shown.
    ///   - Small groups are allocated first, so a parent with three followers keeps them
    ///     beside it rather than being pushed past a neighbour's twenty.
    ///   - Hubs sit at the end of their own group, next to where their followers begin.
    ///   - Column 0 is every project with no prerequisites on this tab: available now.
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

            var acyclic = broken.Acyclic;
            int[] depth = ComputeDepth(acyclic);
            int[] primaryParent = ChoosePrimaryParents(acyclic, depth, options.rank);

            var groups = BuildGroups(acyclic, depth, primaryParent, options.rank);
            Allocate(groups, depth, options);
            Refine(acyclic, groups, options.refineSweeps);

            var column = new int[graph.NodeCount];
            var row = new int[graph.NodeCount];
            Project(groups, column, row);

            // Pulling a card next to its followers leaves a hole where it used to be, and
            // drops it into whatever row was free - which splits the block it landed in.
            // Repacking closes both, and moving cards changes what "beside" means, so the
            // two alternate until they settle.
            for (int pass = 0; pass < 3; pass++)
            {
                PullParentsBesideFollowers(acyclic, depth, column, row, options);
                RepackColumns(column, row, primaryParent, options);
            }
            CompactRows(column, row);

            for (int node = 0; node < graph.NodeCount; node++)
            {
                result.Layer[node] = column[node];
                result.X[node] = column[node] * options.xStep;
                result.Y[node] = row[node] * options.yStep;
            }

            result.Crossings = CrossingCounter.Count(acyclic, column, row);
            return result;
        }

        private struct Cell
        {
            public int Column;
            public int Row;

            public Cell(int column, int row)
            {
                Column = column;
                Row = row;
            }
        }

        private sealed class Group
        {
            public int Depth;
            public int Parent;               // -1 for the roots
            public List<int> Members = new List<int>();
            public List<Cell> Cells = new List<Cell>();
        }

        /// <summary>
        /// Every node hangs off exactly one parent for grouping purposes: the deepest one,
        /// so the group sits as far right as the prerequisites actually require. Remaining
        /// prerequisites still draw their lines, they just do not decide the grouping.
        /// </summary>
        private static int[] ChoosePrimaryParents(LayoutGraph graph, int[] depth, int[] rank)
        {
            var primary = new int[graph.NodeCount];
            for (int node = 0; node < graph.NodeCount; node++)
            {
                var parents = graph.ParentsOf(node);
                int best = -1;
                for (int i = 0; i < parents.Count; i++)
                {
                    int candidate = parents[i];
                    if (best < 0
                        || depth[candidate] > depth[best]
                        || (depth[candidate] == depth[best] && RankOf(rank, candidate) < RankOf(rank, best)))
                    {
                        best = candidate;
                    }
                }
                primary[node] = best;
            }
            return primary;
        }

        private static List<Group> BuildGroups(LayoutGraph graph, int[] depth, int[] primaryParent, int[] rank)
        {
            var byKey = new Dictionary<long, Group>();
            var ordered = new List<Group>();

            for (int node = 0; node < graph.NodeCount; node++)
            {
                long key = ((long)depth[node] << 32) ^ (uint)(primaryParent[node] + 1);

                Group group;
                if (!byKey.TryGetValue(key, out group))
                {
                    group = new Group { Depth = depth[node], Parent = primaryParent[node] };
                    byKey[key] = group;
                    ordered.Add(group);
                }
                group.Members.Add(node);
            }

            foreach (var group in ordered)
            {
                // Hubs last, so a hub sits beside where its own followers start. Otherwise
                // by rank, which the caller orders cheapest first.
                group.Members.Sort(delegate (int a, int b)
                {
                    bool hubA = graph.ChildrenOf(a).Count > 0;
                    bool hubB = graph.ChildrenOf(b).Count > 0;
                    if (hubA != hubB) return hubA ? 1 : -1;
                    int byRank = RankOf(rank, a).CompareTo(RankOf(rank, b));
                    if (byRank != 0) return byRank;
                    return a.CompareTo(b);
                });
            }

            return ordered;
        }

        /// <summary>
        /// Hands every group a contiguous run of cells. Depths are laid out in order and each
        /// occupies its own band of columns, so a follower is always right of its parent.
        /// </summary>
        private static void Allocate(List<Group> groups, int[] depth, LayoutOptions options)
        {
            int maxRows = options.maxNodesPerColumn > 0 ? options.maxNodesPerColumn : int.MaxValue;

            var byDepth = new Dictionary<int, List<Group>>();
            int deepest = 0;
            foreach (var group in groups)
            {
                List<Group> list;
                if (!byDepth.TryGetValue(group.Depth, out list))
                {
                    list = new List<Group>();
                    byDepth[group.Depth] = list;
                }
                list.Add(group);
                if (group.Depth > deepest) deepest = group.Depth;
            }

            int bandStart = 0;

            for (int level = 0; level <= deepest; level++)
            {
                List<Group> atLevel;
                if (!byDepth.TryGetValue(level, out atLevel)) continue;

                // Smallest groups first: a parent with a handful of followers keeps them
                // close, rather than being displaced by a neighbour's two dozen.
                atLevel.Sort(delegate (Group a, Group b)
                {
                    int bySize = a.Members.Count.CompareTo(b.Members.Count);
                    if (bySize != 0) return bySize;
                    return a.Parent.CompareTo(b.Parent);
                });

                int column = bandStart;
                int row = 0;
                bool firstGroup = true;

                foreach (var group in atLevel)
                {
                    if (!firstGroup && options.separateGroups && row > 0)
                    {
                        row++;
                        if (row >= maxRows) { column++; row = 0; }
                    }

                    // A big group begins a fresh column rather than starting halfway down
                    // one another group already filled. Otherwise its parent has no single
                    // place to sit beside it, and the connectors leave at every angle.
                    if (!firstGroup && row > 0 && group.Members.Count * 2 > maxRows)
                    {
                        column++;
                        row = 0;
                    }
                    firstGroup = false;

                    foreach (int unused in group.Members)
                    {
                        group.Cells.Add(new Cell(column, row));
                        row++;
                        if (row >= maxRows) { column++; row = 0; }
                    }
                }

                bandStart = (row > 0) ? column + 1 : Math.Max(column, bandStart);
                if (bandStart <= column) bandStart = column + 1;
            }
        }

        /// <summary>
        /// Shuffles members within their own group - never across groups, so the blocks stay
        /// intact - to pull each card near the average position of what it connects to.
        /// </summary>
        private static void Refine(LayoutGraph graph, List<Group> groups, int sweeps)
        {
            if (sweeps < 1) return;

            var row = new int[graph.NodeCount];
            var column = new int[graph.NodeCount];
            Project(groups, column, row);

            for (int sweep = 0; sweep < sweeps; sweep++)
            {
                bool downward = (sweep % 2) == 0;

                foreach (var group in groups)
                {
                    if (group.Members.Count < 2) continue;

                    var keys = new Dictionary<int, double>();
                    for (int i = 0; i < group.Members.Count; i++)
                    {
                        int node = group.Members[i];
                        var neighbours = downward ? graph.ParentsOf(node) : graph.ChildrenOf(node);
                        keys[node] = neighbours.Count == 0 ? row[node] : Average(neighbours, row);
                    }

                    group.Members.Sort(delegate (int a, int b)
                    {
                        int byKey = keys[a].CompareTo(keys[b]);
                        if (byKey != 0) return byKey;
                        return a.CompareTo(b);
                    });
                }

                Project(groups, column, row);
            }
        }

        /// <summary>
        /// Slides each project rightwards until it sits directly beside its own followers,
        /// level with the middle of them.
        ///
        /// Depth alone puts a project as far left as its prerequisites allow, which is the
        /// wrong answer to look at: electricity has nothing before it, so depth pins it to
        /// column 0 while its two dozen followers begin a column or more away, and the
        /// connectors sweep across everything in between. Nothing is gained by that
        /// leftmost position - what a reader wants is the parent next to its block.
        ///
        /// Deepest first, so by the time a project is considered its own followers have
        /// already settled. A project never moves left, and never past its own
        /// prerequisites, so the reading order still holds.
        /// </summary>
        private static void PullParentsBesideFollowers(LayoutGraph graph, int[] depth, int[] column, int[] row, LayoutOptions options)
        {
            int maxRows = options.maxNodesPerColumn > 0 ? options.maxNodesPerColumn : int.MaxValue;

            var taken = new HashSet<long>();
            for (int node = 0; node < graph.NodeCount; node++) taken.Add(CellKey(column[node], row[node]));

            var order = new List<int>();
            for (int node = 0; node < graph.NodeCount; node++) order.Add(node);
            order.Sort(delegate (int a, int b)
            {
                if (depth[a] != depth[b]) return depth[b].CompareTo(depth[a]);
                return a.CompareTo(b);
            });

            foreach (int node in order)
            {
                var children = graph.ChildrenOf(node);
                if (children.Count == 0) continue;

                int earliestChild = int.MaxValue;
                for (int i = 0; i < children.Count; i++)
                {
                    if (column[children[i]] < earliestChild) earliestChild = column[children[i]];
                }

                int target = earliestChild - 1;
                if (target <= column[node]) continue;

                var parents = graph.ParentsOf(node);
                for (int i = 0; i < parents.Count; i++)
                {
                    if (column[parents[i]] + 1 > target) target = column[parents[i]] + 1;
                }
                if (target >= earliestChild || target <= column[node]) continue;

                var childRows = new List<int>(children.Count);
                for (int i = 0; i < children.Count; i++) childRows.Add(row[children[i]]);
                childRows.Sort();
                int desired = childRows[childRows.Count / 2];

                int chosen = NearestFreeRow(taken, target, desired, maxRows);
                if (chosen < 0) continue;

                taken.Remove(CellKey(column[node], row[node]));
                column[node] = target;
                row[node] = chosen;
                taken.Add(CellKey(target, chosen));
            }
        }

        private static int NearestFreeRow(HashSet<long> taken, int column, int desired, int maxRows)
        {
            for (int offset = 0; offset < maxRows; offset++)
            {
                int below = desired + offset;
                if (below < maxRows && !taken.Contains(CellKey(column, below))) return below;

                int above = desired - offset;
                if (above >= 0 && !taken.Contains(CellKey(column, above))) return above;
            }
            return -1;
        }

        /// <summary>
        /// Rebuilds each column so it reads cleanly: no holes, and every card that follows
        /// the same parent sitting together.
        ///
        /// Both are damage from the pull. A card that moves out to join its own followers
        /// leaves a hole behind it, and lands in whichever row happened to be free, which
        /// drops it into the middle of someone else's block. Sibling runs are restored in
        /// place - a group keeps roughly the height it already had, so nothing jumps across
        /// the tab - and a blank row goes between runs when the column has room for it.
        /// </summary>
        private static void RepackColumns(int[] column, int[] row, int[] primaryParent, LayoutOptions options)
        {
            int maxRows = options.maxNodesPerColumn > 0 ? options.maxNodesPerColumn : int.MaxValue;

            var byColumn = new Dictionary<int, List<int>>();
            for (int node = 0; node < column.Length; node++)
            {
                List<int> members;
                if (!byColumn.TryGetValue(column[node], out members))
                {
                    members = new List<int>();
                    byColumn[column[node]] = members;
                }
                members.Add(node);
            }

            foreach (var pair in byColumn)
            {
                var members = pair.Value;

                var runs = new Dictionary<int, List<int>>();
                var runOrder = new List<int>();
                foreach (int node in members)
                {
                    int key = primaryParent[node];
                    List<int> run;
                    if (!runs.TryGetValue(key, out run))
                    {
                        run = new List<int>();
                        runs[key] = run;
                        runOrder.Add(key);
                    }
                    run.Add(node);
                }

                foreach (int key in runOrder)
                {
                    runs[key].Sort(delegate (int a, int b)
                    {
                        if (row[a] != row[b]) return row[a].CompareTo(row[b]);
                        return a.CompareTo(b);
                    });
                }

                // Runs keep their existing vertical order, so repacking tidies a column
                // rather than rearranging it.
                runOrder.Sort(delegate (int a, int b)
                {
                    double meanA = MeanRow(runs[a], row);
                    double meanB = MeanRow(runs[b], row);
                    int byMean = meanA.CompareTo(meanB);
                    if (byMean != 0) return byMean;
                    return a.CompareTo(b);
                });

                bool roomForGaps = options.separateGroups
                                && members.Count + runOrder.Count - 1 <= maxRows;

                int next = 0;
                bool first = true;
                foreach (int key in runOrder)
                {
                    if (!first && roomForGaps) next++;
                    first = false;

                    foreach (int node in runs[key])
                    {
                        row[node] = next;
                        next++;
                    }
                }
            }
        }

        private static double MeanRow(List<int> nodes, int[] row)
        {
            double total = 0;
            for (int i = 0; i < nodes.Count; i++) total += row[nodes[i]];
            return total / nodes.Count;
        }

        /// <summary>Removes rows that ended up with nothing in them anywhere on the tab.</summary>
        private static void CompactRows(int[] column, int[] row)
        {
            var used = new List<int>();
            var seen = new HashSet<int>();
            for (int node = 0; node < row.Length; node++) if (seen.Add(row[node])) used.Add(row[node]);
            used.Sort();

            var moved = new Dictionary<int, int>();
            for (int i = 0; i < used.Count; i++) moved[used[i]] = i;

            for (int node = 0; node < row.Length; node++) row[node] = moved[row[node]];
        }

        private static long CellKey(int column, int row)
        {
            return ((long)column << 32) ^ (uint)row;
        }

        private static void Project(List<Group> groups, int[] column, int[] row)
        {
            foreach (var group in groups)
            {
                for (int i = 0; i < group.Members.Count && i < group.Cells.Count; i++)
                {
                    column[group.Members[i]] = group.Cells[i].Column;
                    row[group.Members[i]] = group.Cells[i].Row;
                }
            }
        }

        private static double Average(IReadOnlyList<int> nodes, int[] row)
        {
            double total = 0;
            for (int i = 0; i < nodes.Count; i++) total += row[nodes[i]];
            return total / nodes.Count;
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

            while (ready.Count > 0)
            {
                int pick = 0;
                for (int i = 1; i < ready.Count; i++) if (ready[i] < ready[pick]) pick = i;

                int node = ready[pick];
                ready.RemoveAt(pick);

                var children = graph.ChildrenOf(node);
                for (int c = 0; c < children.Count; c++)
                {
                    int child = children[c];
                    if (depth[node] + 1 > depth[child]) depth[child] = depth[node] + 1;
                    if (--remaining[child] == 0) ready.Add(child);
                }
            }

            return depth;
        }

        private static int RankOf(int[] rank, int node)
        {
            return rank != null ? rank[node] : node;
        }
    }
}
