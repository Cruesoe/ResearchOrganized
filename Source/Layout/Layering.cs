using System.Collections.Generic;

namespace ResearchOrganized.Layout
{
    /// <summary>
    /// Assigns each node to a layer (a column, in RimWorld terms) such that every node sits
    /// strictly to the right of all its parents, while keeping any single layer from
    /// exceeding maxWidth nodes.
    ///
    /// Uses Coffman-Graham labelling to decide the order nodes are considered, then a
    /// width-bounded greedy placement. The labelling is what keeps siblings together:
    /// nodes whose parents were placed early get labelled early, so related work lands in
    /// neighbouring layers instead of being scattered by an arbitrary topological order.
    /// </summary>
    public static class Layering
    {
        /// <summary>
        /// Returns layer[node]. Requires an acyclic graph - run <see cref="CycleBreaker"/> first.
        /// maxWidth &lt;= 0 means unbounded.
        /// </summary>
        public static int[] Assign(LayoutGraph graph, int maxWidth)
        {
            int[] order = ComputeLabelOrder(graph);
            var layer = new int[graph.NodeCount];
            var layerCounts = new Dictionary<int, int>();

            for (int i = 0; i < order.Length; i++)
            {
                int node = order[i];

                int earliest = 0;
                var parents = graph.ParentsOf(node);
                for (int p = 0; p < parents.Count; p++)
                {
                    int candidate = layer[parents[p]] + 1;
                    if (candidate > earliest) earliest = candidate;
                }

                if (maxWidth > 0)
                {
                    int count;
                    while (layerCounts.TryGetValue(earliest, out count) && count >= maxWidth) earliest++;
                }

                layer[node] = earliest;
                int existing;
                layerCounts[earliest] = layerCounts.TryGetValue(earliest, out existing) ? existing + 1 : 1;
            }

            return layer;
        }

        /// <summary>
        /// Coffman-Graham labelling. Repeatedly takes an unlabelled node whose parents are
        /// all labelled, preferring the one whose parent labels are lexicographically
        /// smallest (comparing largest label first). Returns nodes in labelling order,
        /// which is always a valid topological order.
        /// </summary>
        private static int[] ComputeLabelOrder(LayoutGraph graph)
        {
            int n = graph.NodeCount;
            var labelled = new bool[n];
            var label = new int[n];
            var order = new int[n];

            var remainingParents = new int[n];
            for (int i = 0; i < n; i++) remainingParents[i] = graph.ParentsOf(i).Count;

            var ready = new List<int>();
            for (int i = 0; i < n; i++) if (remainingParents[i] == 0) ready.Add(i);

            for (int step = 0; step < n; step++)
            {
                if (ready.Count == 0)
                {
                    // Should not happen on a DAG, but never spin: take any unlabelled node.
                    for (int i = 0; i < n; i++) if (!labelled[i]) { ready.Add(i); break; }
                    if (ready.Count == 0) break;
                }

                int bestIndex = 0;
                var bestKey = ParentLabelKey(graph, ready[0], label, labelled);
                for (int i = 1; i < ready.Count; i++)
                {
                    var key = ParentLabelKey(graph, ready[i], label, labelled);
                    if (CompareDescending(key, bestKey) < 0)
                    {
                        bestKey = key;
                        bestIndex = i;
                    }
                }

                int chosen = ready[bestIndex];
                ready.RemoveAt(bestIndex);

                labelled[chosen] = true;
                label[chosen] = step;
                order[step] = chosen;

                var children = graph.ChildrenOf(chosen);
                for (int c = 0; c < children.Count; c++)
                {
                    int child = children[c];
                    if (--remainingParents[child] == 0 && !labelled[child]) ready.Add(child);
                }
            }

            return order;
        }

        private static List<int> ParentLabelKey(LayoutGraph graph, int node, int[] label, bool[] labelled)
        {
            var key = new List<int>();
            var parents = graph.ParentsOf(node);
            for (int i = 0; i < parents.Count; i++) if (labelled[parents[i]]) key.Add(label[parents[i]]);
            key.Sort();
            key.Reverse();
            return key;
        }

        /// <summary>Lexicographic compare of descending label lists; shorter wins on a prefix tie.</summary>
        private static int CompareDescending(List<int> a, List<int> b)
        {
            int shared = a.Count < b.Count ? a.Count : b.Count;
            for (int i = 0; i < shared; i++)
            {
                if (a[i] != b[i]) return a[i] < b[i] ? -1 : 1;
            }
            return a.Count.CompareTo(b.Count);
        }
    }
}
