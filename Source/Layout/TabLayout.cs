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

        /// <summary>Unused by this layout; kept so callers do not need changing.</summary>
        public bool separateGroups = true;
        public int refineSweeps = 4;

        /// <summary>
        /// Optional placement priority, lower first. Orders cards within a generation - the
        /// caller puts cheap projects first.
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

        public int Crossings;
    }

    /// <summary>
    /// Lays out one research tab as a set of trees, one per group of connected projects.
    ///
    /// A tree is drawn the way anybody draws a tree: the thing at the top of it sits in one
    /// column, everything that follows from it sits in the next column along, everything
    /// following those in the column after that. A project with two dozen followers gets a
    /// column to itself and the followers fill the columns to its right - so the fan reads
    /// as a fan, and nothing unrelated is threaded through it.
    ///
    /// Trees are then packed onto the tab: stacked down a shelf while they fit the height,
    /// then a new shelf to the right. Smallest first, so a tree of five does not have to
    /// wait behind a tree of thirty. Projects with nothing attached fill whatever is left.
    ///
    /// The previous layout arranged the whole tab by prerequisite depth instead, which put
    /// every root in column 0 regardless of what followed it. That is why electricity sat in
    /// the first column with its followers scattered through three more, cutting across
    /// another tree on the way.
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
            int cap = options.maxNodesPerColumn > 0 ? options.maxNodesPerColumn : int.MaxValue;

            var column = new int[graph.NodeCount];
            var row = new int[graph.NodeCount];

            var trees = new List<Tree>();
            var loose = new List<int>();
            foreach (var members in FindConnectedGroups(acyclic))
            {
                if (members.Count == 1) loose.Add(members[0]);
                else trees.Add(LayoutTree(acyclic, members, column, row, cap, options.rank));
            }

            PackTrees(trees, column, row, cap);
            PlaceLoose(loose, column, row, trees, cap);
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

        private sealed class Tree
        {
            public List<int> Members;
            public int Width;
            public int Height;
        }

        /// <summary>Groups of projects joined by prerequisites, ignoring direction.</summary>
        private static List<List<int>> FindConnectedGroups(LayoutGraph graph)
        {
            var seen = new bool[graph.NodeCount];
            var groups = new List<List<int>>();
            var stack = new List<int>();

            for (int start = 0; start < graph.NodeCount; start++)
            {
                if (seen[start]) continue;

                var members = new List<int>();
                seen[start] = true;
                stack.Add(start);

                while (stack.Count > 0)
                {
                    int node = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);
                    members.Add(node);

                    var children = graph.ChildrenOf(node);
                    for (int i = 0; i < children.Count; i++)
                    {
                        if (!seen[children[i]]) { seen[children[i]] = true; stack.Add(children[i]); }
                    }
                    var parents = graph.ParentsOf(node);
                    for (int i = 0; i < parents.Count; i++)
                    {
                        if (!seen[parents[i]]) { seen[parents[i]] = true; stack.Add(parents[i]); }
                    }
                }

                members.Sort();
                groups.Add(members);
            }
            return groups;
        }

        /// <summary>
        /// Places one tree in its own coordinate space, starting at column 0.
        ///
        /// Generation by generation: everything with nothing before it in this tree goes in
        /// the first column, everything following those in the next, and so on. A generation
        /// too tall for one column wraps into the next, and the following generation always
        /// starts a fresh column - so a follower is never left of the thing it follows, and a
        /// parent standing alone in its generation gets a column to itself.
        /// </summary>
        private static Tree LayoutTree(LayoutGraph graph, List<int> members, int[] column, int[] row, int cap, int[] rank)
        {
            var generation = Generations(graph, members);

            int deepest = 0;
            foreach (int node in members) if (generation[node] > deepest) deepest = generation[node];

            var byGeneration = new List<int>[deepest + 1];
            for (int g = 0; g <= deepest; g++) byGeneration[g] = new List<int>();
            foreach (int node in members) byGeneration[generation[node]].Add(node);

            int localColumn = 0;
            int tallest = 0;

            for (int g = 0; g <= deepest; g++)
            {
                var here = byGeneration[g];
                if (here.Count == 0) continue;

                // Hubs last, so the next generation begins beside the card it follows from.
                here.Sort(delegate (int a, int b)
                {
                    bool hubA = graph.ChildrenOf(a).Count > 0;
                    bool hubB = graph.ChildrenOf(b).Count > 0;
                    if (hubA != hubB) return hubA ? 1 : -1;
                    int byRank = RankOf(rank, a).CompareTo(RankOf(rank, b));
                    if (byRank != 0) return byRank;
                    return a.CompareTo(b);
                });

                int r = 0;
                foreach (int node in here)
                {
                    column[node] = localColumn;
                    row[node] = r;
                    r++;
                    if (r > tallest) tallest = r;
                    if (r >= cap) { localColumn++; r = 0; }
                }
                if (r > 0) localColumn++;
            }

            return new Tree { Members = members, Width = Math.Max(1, localColumn), Height = Math.Max(1, tallest) };
        }

        /// <summary>Longest path from the tree's own starting projects.</summary>
        private static int[] Generations(LayoutGraph graph, List<int> members)
        {
            var generation = new int[graph.NodeCount];
            var remaining = new Dictionary<int, int>(members.Count);
            var ready = new List<int>();

            foreach (int node in members)
            {
                int count = graph.ParentsOf(node).Count;
                remaining[node] = count;
                if (count == 0) ready.Add(node);
            }

            while (ready.Count > 0)
            {
                int pick = 0;
                for (int i = 1; i < ready.Count; i++) if (ready[i] < ready[pick]) pick = i;

                int node = ready[pick];
                ready.RemoveAt(pick);

                var children = graph.ChildrenOf(node);
                for (int i = 0; i < children.Count; i++)
                {
                    int child = children[i];
                    if (generation[node] + 1 > generation[child]) generation[child] = generation[node] + 1;
                    if (--remaining[child] == 0) ready.Add(child);
                }
            }
            return generation;
        }

        /// <summary>
        /// Drops the trees onto the tab: down a shelf while they fit the height, then a new
        /// shelf to the right. Smallest first, so a tree of five is not made to wait behind a
        /// tree of thirty - which is what put one tree's followers in the middle of another's.
        /// </summary>
        private static void PackTrees(List<Tree> trees, int[] column, int[] row, int cap)
        {
            trees.Sort(delegate (Tree a, Tree b)
            {
                int bySize = a.Members.Count.CompareTo(b.Members.Count);
                if (bySize != 0) return bySize;
                return a.Members[0].CompareTo(b.Members[0]);
            });

            int shelfColumn = 0;
            int shelfRow = 0;
            int shelfWidth = 0;

            foreach (var tree in trees)
            {
                if (shelfRow > 0 && shelfRow + tree.Height > cap)
                {
                    shelfColumn += shelfWidth;
                    shelfRow = 0;
                    shelfWidth = 0;
                }

                foreach (int node in tree.Members)
                {
                    column[node] += shelfColumn;
                    row[node] += shelfRow;
                }

                shelfRow += tree.Height;
                if (tree.Width > shelfWidth) shelfWidth = tree.Width;
            }
        }

        /// <summary>
        /// Projects with nothing attached go in last, filling whatever the trees left. They
        /// can never take a column a tree needed, and they keep the tab from being padded out
        /// with blank space.
        /// </summary>
        private static void PlaceLoose(List<int> loose, int[] column, int[] row, List<Tree> trees, int cap)
        {
            if (loose.Count == 0) return;

            var nextFree = new Dictionary<int, int>();
            foreach (var tree in trees)
            {
                foreach (int node in tree.Members)
                {
                    int after = row[node] + 1;
                    int held;
                    if (!nextFree.TryGetValue(column[node], out held) || after > held) nextFree[column[node]] = after;
                }
            }

            int target = 0;
            foreach (int node in loose)
            {
                while (true)
                {
                    int used;
                    if (!nextFree.TryGetValue(target, out used)) used = 0;
                    if (used < cap)
                    {
                        column[node] = target;
                        row[node] = used;
                        nextFree[target] = used + 1;
                        break;
                    }
                    target++;
                }
            }
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

        private static int RankOf(int[] rank, int node)
        {
            return rank != null ? rank[node] : node;
        }
    }
}
