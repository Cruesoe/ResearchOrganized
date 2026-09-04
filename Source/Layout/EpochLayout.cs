using System;
using System.Collections.Generic;

namespace ResearchOrganized.Layout
{
    /// <summary>
    /// Lays out one research tab the way the original mod did, before it was rewritten from
    /// scratch: group by tech level, place the cheap non-hub projects first, then work through
    /// the hubs ("anchors") one capacity-limited batch at a time, placing each batch's own
    /// followers right after it and centering the hub on them.
    ///
    /// This is a port, not a redesign - the earlier rewrite kept re-discovering, one bug
    /// report at a time, problems this approach had already solved: a project with two dozen
    /// followers holding its own column instead of being buried among them, a cheap early
    /// project never getting shoved behind a big hub it does not need, and a hard cap on
    /// column height instead of growing until the window can no longer show it.
    ///
    /// It runs on an already-acyclic graph - <see cref="TabLayout"/> breaks cycles first - so
    /// unlike the original there is no "stuck node" case to special-case here: a topological
    /// sort guarantees every node's same-tab prerequisites are resolvable before it is asked
    /// to place.
    /// </summary>
    public static class EpochLayout
    {
        public static void Compute(
            LayoutGraph graph,
            LayoutOptions options,
            int[] epoch,
            bool[] isAnchor,
            int[] anchorOrder,
            int[] tieRank,
            bool[] isCapstone,
            int[] column,
            int[] row)
        {
            int n = graph.NodeCount;
            int maxNodes = options.maxNodesPerColumn > 0 ? options.maxNodesPerColumn : int.MaxValue;

            var placed = new bool[n];
            var occupied = new HashSet<long>();
            int currentColumn = 0;

            var ancestorCache = new HashSet<int>[n];

            foreach (int epochValue in DistinctSorted(epoch))
            {
                // A capstone (an era's "advance to the next tech level" node, from mods like
                // Node Research) is not part of the normal hub/starter placement at all - it
                // is placed once everything else in the era is down, so it always reads as
                // the last thing in it regardless of what prerequisites it does or does not
                // have.
                var members = new List<int>();
                var capstones = new List<int>();
                for (int node = 0; node < n; node++)
                {
                    if (epoch[node] != epochValue) continue;
                    if (isCapstone[node]) capstones.Add(node); else members.Add(node);
                }

                var epochAnchors = new List<int>();
                foreach (int node in members) if (isAnchor[node]) epochAnchors.Add(node);
                epochAnchors.Sort(delegate (int a, int b) { return anchorOrder[a].CompareTo(anchorOrder[b]); });

                // Anything that is not an anchor and does not descend from one of this
                // epoch's anchors goes first - the cheap, early projects a colony researches
                // before it ever reaches for the big hub, shown before that hub rather than
                // scattered behind it.
                var starters = new List<int>();
                foreach (int node in members)
                {
                    if (isAnchor[node]) continue;
                    if (!DescendsFromAny(graph, node, epochAnchors, ancestorCache)) starters.Add(node);
                }

                if (starters.Count > 0)
                {
                    PlaceNodesDAG(starters, ref currentColumn, placed, column, row, occupied, graph, maxNodes, tieRank);
                }

                var remainingAnchors = new List<int>();
                foreach (int a in epochAnchors) if (!placed[a]) remainingAnchors.Add(a);

                while (remainingAnchors.Count > 0)
                {
                    var ready = new List<int>();
                    foreach (int a in remainingAnchors)
                    {
                        if (AllResolved(graph.ParentsOf(a), epoch, epochValue, placed)) ready.Add(a);
                    }

                    if (ready.Count == 0)
                    {
                        // Every remaining anchor in this epoch depends on another one still
                        // waiting - place whichever is closest to ready first.
                        int fallback = remainingAnchors[0];
                        int fewest = CountUnplacedSameEpoch(graph.ParentsOf(fallback), epoch, epochValue, placed);
                        for (int i = 1; i < remainingAnchors.Count; i++)
                        {
                            int count = CountUnplacedSameEpoch(graph.ParentsOf(remainingAnchors[i]), epoch, epochValue, placed);
                            if (count < fewest) { fewest = count; fallback = remainingAnchors[i]; }
                        }
                        ready.Add(fallback);
                    }

                    // A node belongs to the anchor deepest in the order among its epoch-anchor
                    // ancestors - so a project needing both Electricity and Machining reads as
                    // Machining's, not as noise scattered under the earlier, broader hub.
                    var claimedBy = new Dictionary<int, int>();
                    foreach (int node in members)
                    {
                        if (placed[node] || isAnchor[node]) continue;
                        int owner = DeepestAnchorAncestor(graph, node, epochAnchors, anchorOrder, ancestorCache);
                        if (owner >= 0) claimedBy[node] = owner;
                    }

                    var batch = new List<int>();
                    var dependentsByAnchor = new Dictionary<int, List<int>>();
                    int batchCapacity = 0;

                    foreach (int anchor in ready)
                    {
                        var dependents = new List<int>();
                        foreach (var pair in claimedBy) if (pair.Value == anchor) dependents.Add(pair.Key);

                        int directChildren = 0;
                        var children = graph.ChildrenOf(anchor);
                        for (int i = 0; i < children.Count; i++) if (claimedBy.TryGetValue(children[i], out int owner) && owner == anchor) directChildren++;

                        int required = Math.Max(directChildren, 1);
                        if (batch.Count == 0 || batchCapacity + required <= maxNodes)
                        {
                            batch.Add(anchor);
                            dependentsByAnchor[anchor] = dependents;
                            batchCapacity += required;
                        }
                    }

                    foreach (int a in batch) remainingAnchors.Remove(a);

                    int batchColumn = currentColumn;
                    foreach (int anchor in batch)
                    {
                        var parents = graph.ParentsOf(anchor);
                        for (int i = 0; i < parents.Count; i++)
                        {
                            if (placed[parents[i]]) batchColumn = Math.Max(batchColumn, column[parents[i]] + 1);
                        }
                    }
                    currentColumn = batchColumn;

                    int cursor = 0;
                    foreach (int anchor in batch)
                    {
                        column[anchor] = batchColumn;
                        row[anchor] = cursor;
                        occupied.Add(Key(batchColumn, cursor));
                        placed[anchor] = true;

                        var children = graph.ChildrenOf(anchor);
                        int directChildren = 0;
                        for (int i = 0; i < children.Count; i++) if (claimedBy.TryGetValue(children[i], out int owner) && owner == anchor) directChildren++;
                        cursor += Math.Max(directChildren, 1);
                    }

                    currentColumn = batchColumn + 1;

                    var allDependents = new List<int>();
                    var seenDependent = new HashSet<int>();
                    foreach (int anchor in batch)
                    {
                        foreach (int node in dependentsByAnchor[anchor]) if (seenDependent.Add(node)) allDependents.Add(node);
                    }

                    if (allDependents.Count > 0)
                    {
                        PlaceNodesDAG(allDependents, ref currentColumn, placed, column, row, occupied, graph, maxNodes, tieRank);

                        foreach (int anchor in batch)
                        {
                            int minRow = int.MaxValue, maxRow = int.MinValue;
                            foreach (int node in dependentsByAnchor[anchor])
                            {
                                var children = graph.ChildrenOf(anchor);
                                bool isDirectChild = false;
                                for (int i = 0; i < children.Count; i++) if (children[i] == node) { isDirectChild = true; break; }
                                if (!isDirectChild) continue;
                                if (row[node] < minRow) minRow = row[node];
                                if (row[node] > maxRow) maxRow = row[node];
                            }
                            if (minRow != int.MaxValue)
                            {
                                occupied.Remove(Key(column[anchor], row[anchor]));
                                row[anchor] = (minRow + maxRow) / 2;
                                occupied.Add(Key(column[anchor], row[anchor]));
                            }
                        }
                    }

                    // Centering can pull two anchors in the same batch to the same row -
                    // keep them at least one row apart in the order they now fall.
                    batch.Sort(delegate (int a, int b) { return row[a].CompareTo(row[b]); });
                    for (int i = 1; i < batch.Count; i++)
                    {
                        if (row[batch[i]] - row[batch[i - 1]] < 1)
                        {
                            occupied.Remove(Key(column[batch[i]], row[batch[i]]));
                            row[batch[i]] = row[batch[i - 1]] + 1;
                            occupied.Add(Key(column[batch[i]], row[batch[i]]));
                        }
                    }
                }

                var orphaned = new List<int>();
                foreach (int node in members) if (!placed[node]) orphaned.Add(node);
                if (orphaned.Count > 0)
                {
                    PlaceNodesDAG(orphaned, ref currentColumn, placed, column, row, occupied, graph, maxNodes, tieRank);
                }

                if (capstones.Count > 0)
                {
                    PlaceNodesDAG(capstones, ref currentColumn, placed, column, row, occupied, graph, maxNodes, tieRank);
                }
            }
        }

        /// <summary>
        /// Places a batch of same-tab projects generation by generation: whichever one is
        /// most tied to what is already down goes down next, one column past its furthest
        /// placed prerequisite and level with it, sliding down (or up, then down) to the
        /// nearest free row once the cap on a column is reached.
        /// </summary>
        private static void PlaceNodesDAG(List<int> nodesToPlace, ref int currentColumn, bool[] placed,
            int[] column, int[] row, HashSet<long> occupied, LayoutGraph graph, int maxNodes, int[] tieRank)
        {
            int baseColumn = currentColumn;
            var unplaced = new HashSet<int>(nodesToPlace);

            while (unplaced.Count > 0)
            {
                // Only a node whose prerequisites are no longer waiting in this same batch
                // may be considered - one still-unplaced parent means the column it would
                // need is not known yet, and picking it early could leave it left of a
                // parent that has not been placed.
                var ready = new List<int>();
                foreach (int candidate in unplaced)
                {
                    if (AllParentsResolved(graph.ParentsOf(candidate), unplaced)) ready.Add(candidate);
                }
                if (ready.Count == 0) ready.Add(FirstOf(unplaced));

                int node = PickNext(ready, graph, placed, column, row, tieRank);
                unplaced.Remove(node);

                var placedParents = new List<int>();
                var parents = graph.ParentsOf(node);
                for (int i = 0; i < parents.Count; i++) if (placed[parents[i]]) placedParents.Add(parents[i]);

                int minColumn = baseColumn;
                int desiredRow = 0;
                if (placedParents.Count > 0)
                {
                    int maxParentColumn = 0;
                    for (int i = 0; i < placedParents.Count; i++) if (column[placedParents[i]] > maxParentColumn) maxParentColumn = column[placedParents[i]];
                    minColumn = Math.Max(baseColumn, maxParentColumn + 1);
                    desiredRow = NearestColumnAverageRow(placedParents, maxParentColumn, column, row);
                }

                int chosenColumn = minColumn;
                int chosenRow = desiredRow;

                while (true)
                {
                    int countInColumn = 0;
                    foreach (long key in occupied) if (ColumnOf(key) == chosenColumn) countInColumn++;
                    if (countInColumn >= maxNodes) { chosenColumn++; chosenRow = desiredRow; continue; }

                    bool found = false;
                    for (int offset = 0; offset < maxNodes; offset++)
                    {
                        int testRow = chosenRow + offset;
                        if (testRow >= 0 && testRow < maxNodes && !occupied.Contains(Key(chosenColumn, testRow))) { chosenRow = testRow; found = true; break; }
                        testRow = chosenRow - offset;
                        if (offset > 0 && testRow >= 0 && testRow < maxNodes && !occupied.Contains(Key(chosenColumn, testRow))) { chosenRow = testRow; found = true; break; }
                    }
                    if (found) break;
                    chosenColumn++;
                }

                column[node] = chosenColumn;
                row[node] = chosenRow;
                occupied.Add(Key(chosenColumn, chosenRow));
                placed[node] = true;
            }

            int rightmost = baseColumn;
            foreach (long key in occupied) if (ColumnOf(key) >= baseColumn && ColumnOf(key) > rightmost) rightmost = ColumnOf(key);
            currentColumn = rightmost + 1;
        }

        /// <summary>
        /// Of what is left, the one most tied to what is already down goes next: most placed
        /// parents first, then level with the average row of its nearest-column ones, then a
        /// stable tie-break so the result does not depend on set iteration order.
        /// </summary>
        private static int PickNext(List<int> candidates, LayoutGraph graph, bool[] placed, int[] column, int[] row, int[] tieRank)
        {
            int best = -1;
            int bestPlacedCount = -1;
            double bestAvgRow = 0;

            foreach (int node in candidates)
            {
                var parents = graph.ParentsOf(node);
                var placedParents = new List<int>();
                for (int i = 0; i < parents.Count; i++) if (placed[parents[i]]) placedParents.Add(parents[i]);

                int placedCount = placedParents.Count;
                double avgRow = 0;
                if (placedCount > 0)
                {
                    int maxColumn = 0;
                    for (int i = 0; i < placedParents.Count; i++) if (column[placedParents[i]] > maxColumn) maxColumn = column[placedParents[i]];
                    avgRow = NearestColumnAverageRow(placedParents, maxColumn, column, row);
                }

                if (best < 0 || placedCount > bestPlacedCount
                    || (placedCount == bestPlacedCount && avgRow < bestAvgRow)
                    || (placedCount == bestPlacedCount && avgRow == bestAvgRow && tieRank[node] < tieRank[best]))
                {
                    best = node;
                    bestPlacedCount = placedCount;
                    bestAvgRow = avgRow;
                }
            }

            return best;
        }

        private static int NearestColumnAverageRow(List<int> placedParents, int nearestColumn, int[] column, int[] row)
        {
            int sum = 0, count = 0;
            for (int i = 0; i < placedParents.Count; i++)
            {
                if (column[placedParents[i]] != nearestColumn) continue;
                sum += row[placedParents[i]];
                count++;
            }
            return count == 0 ? 0 : (int)Math.Round((double)sum / count);
        }

        private static bool AllParentsResolved(IReadOnlyList<int> parents, HashSet<int> unplaced)
        {
            for (int i = 0; i < parents.Count; i++) if (unplaced.Contains(parents[i])) return false;
            return true;
        }

        private static int FirstOf(HashSet<int> set)
        {
            foreach (int node in set) return node;
            return -1;
        }

        private static bool AllResolved(IReadOnlyList<int> parents, int[] epoch, int epochValue, bool[] placed)
        {
            for (int i = 0; i < parents.Count; i++)
            {
                if (epoch[parents[i]] == epochValue && !placed[parents[i]]) return false;
            }
            return true;
        }

        private static int CountUnplacedSameEpoch(IReadOnlyList<int> parents, int[] epoch, int epochValue, bool[] placed)
        {
            int count = 0;
            for (int i = 0; i < parents.Count; i++) if (epoch[parents[i]] == epochValue && !placed[parents[i]]) count++;
            return count;
        }

        private static bool DescendsFromAny(LayoutGraph graph, int node, List<int> anchors, HashSet<int>[] cache)
        {
            if (anchors.Count == 0) return false;
            var ancestors = Ancestors(graph, node, cache);
            for (int i = 0; i < anchors.Count; i++) if (ancestors.Contains(anchors[i])) return true;
            return false;
        }

        private static int DeepestAnchorAncestor(LayoutGraph graph, int node, List<int> epochAnchors, int[] anchorOrder, HashSet<int>[] cache)
        {
            var ancestors = Ancestors(graph, node, cache);
            int best = -1;
            for (int i = 0; i < epochAnchors.Count; i++)
            {
                int anchor = epochAnchors[i];
                if (!ancestors.Contains(anchor)) continue;
                if (best < 0 || anchorOrder[anchor] > anchorOrder[best]) best = anchor;
            }
            return best;
        }

        private static HashSet<int> Ancestors(LayoutGraph graph, int node, HashSet<int>[] cache)
        {
            if (cache[node] != null) return cache[node];

            var ancestors = new HashSet<int>();
            var stack = new List<int>();
            stack.Add(node);

            while (stack.Count > 0)
            {
                int current = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);

                var parents = graph.ParentsOf(current);
                for (int i = 0; i < parents.Count; i++)
                {
                    if (ancestors.Add(parents[i])) stack.Add(parents[i]);
                }
            }

            return cache[node] = ancestors;
        }

        private static List<int> DistinctSorted(int[] values)
        {
            var seen = new HashSet<int>(values);
            var list = new List<int>(seen);
            list.Sort();
            return list;
        }

        private static long Key(int col, int row) { return ((long)col << 32) ^ (uint)row; }
        private static int ColumnOf(long key) { return (int)(key >> 32); }
    }
}
