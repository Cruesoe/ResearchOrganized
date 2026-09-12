using System.Collections.Generic;

namespace ResearchOrganized.Layout
{
    /// <summary>
    /// Finds every cyclic strongly connected component and gives each one a deterministic
    /// low-feedback ordering. Edges opposing that order are reversed, yielding a DAG while
    /// disturbing fewer authored relationships than traversal-order back-edge selection.
    /// </summary>
    public static class CycleBreaker
    {
        public sealed class Result
        {
            public LayoutGraph Acyclic;
            public List<LayoutGraph.Edge> ReversedEdges = new List<LayoutGraph.Edge>();
            public HashSet<int> NodesInCycles = new HashSet<int>();
        }

        public static Result Break(LayoutGraph graph, int[] tieRank = null)
        {
            var result = new Result();
            List<List<int>> components = FindStronglyConnectedComponents(graph);
            var reversed = new HashSet<LayoutGraph.Edge>();

            foreach (List<int> component in components)
            {
                if (component.Count <= 1) continue;
                for (int i = 0; i < component.Count; i++) result.NodesInCycles.Add(component[i]);

                List<int> order = LowFeedbackOrder(graph, component, tieRank);
                var position = new Dictionary<int, int>();
                for (int i = 0; i < order.Count; i++) position[order[i]] = i;

                var members = new HashSet<int>(component);
                foreach (LayoutGraph.Edge edge in graph.AllEdges())
                {
                    if (members.Contains(edge.Parent) && members.Contains(edge.Child)
                        && position[edge.Parent] > position[edge.Child]) reversed.Add(edge);
                }
            }

            foreach (LayoutGraph.Edge edge in graph.AllEdges())
                if (reversed.Contains(edge)) result.ReversedEdges.Add(edge);
            result.Acyclic = graph.WithReversedEdges(reversed);
            return result;
        }

        /// <summary>
        /// Eades-style source/sink peeling. When no source or sink exists, choose the node with
        /// the largest outgoing-minus-incoming degree; stable caller rank breaks ties.
        /// </summary>
        private static List<int> LowFeedbackOrder(LayoutGraph graph, List<int> component, int[] tieRank)
        {
            var remaining = new HashSet<int>(component);
            var left = new List<int>();
            var right = new List<int>();

            while (remaining.Count > 0)
            {
                int source = SelectSourceOrSink(graph, remaining, tieRank, wantSource: true);
                if (source >= 0)
                {
                    left.Add(source);
                    remaining.Remove(source);
                    continue;
                }

                int sink = SelectSourceOrSink(graph, remaining, tieRank, wantSource: false);
                if (sink >= 0)
                {
                    right.Add(sink);
                    remaining.Remove(sink);
                    continue;
                }

                int best = -1;
                int bestScore = int.MinValue;
                foreach (int node in remaining)
                {
                    int score = DegreeWithin(graph.ChildrenOf(node), remaining) - DegreeWithin(graph.ParentsOf(node), remaining);
                    if (best < 0 || score > bestScore || (score == bestScore && Rank(node, tieRank) < Rank(best, tieRank)))
                    {
                        best = node;
                        bestScore = score;
                    }
                }
                left.Add(best);
                remaining.Remove(best);
            }

            right.Reverse();
            left.AddRange(right);
            return left;
        }

        private static int SelectSourceOrSink(LayoutGraph graph, HashSet<int> remaining, int[] tieRank, bool wantSource)
        {
            int selected = -1;
            foreach (int node in remaining)
            {
                IReadOnlyList<int> relevant = wantSource ? graph.ParentsOf(node) : graph.ChildrenOf(node);
                if (DegreeWithin(relevant, remaining) != 0) continue;
                if (selected < 0 || Rank(node, tieRank) < Rank(selected, tieRank)) selected = node;
            }
            return selected;
        }

        private static int DegreeWithin(IReadOnlyList<int> neighbours, HashSet<int> remaining)
        {
            int count = 0;
            for (int i = 0; i < neighbours.Count; i++) if (remaining.Contains(neighbours[i])) count++;
            return count;
        }

        private static int Rank(int node, int[] tieRank)
        {
            return tieRank != null && tieRank.Length > node ? tieRank[node] : node;
        }

        /// <summary>Kosaraju's algorithm, iterative in both passes for deep modded trees.</summary>
        private static List<List<int>> FindStronglyConnectedComponents(LayoutGraph graph)
        {
            int count = graph.NodeCount;
            var visited = new bool[count];
            var finishOrder = new List<int>(count);
            var frames = new List<KeyValuePair<int, int>>();

            for (int root = 0; root < count; root++)
            {
                if (visited[root]) continue;
                visited[root] = true;
                frames.Add(new KeyValuePair<int, int>(root, 0));
                while (frames.Count > 0)
                {
                    KeyValuePair<int, int> frame = frames[frames.Count - 1];
                    IReadOnlyList<int> children = graph.ChildrenOf(frame.Key);
                    if (frame.Value >= children.Count)
                    {
                        finishOrder.Add(frame.Key);
                        frames.RemoveAt(frames.Count - 1);
                        continue;
                    }
                    frames[frames.Count - 1] = new KeyValuePair<int, int>(frame.Key, frame.Value + 1);
                    int child = children[frame.Value];
                    if (!visited[child])
                    {
                        visited[child] = true;
                        frames.Add(new KeyValuePair<int, int>(child, 0));
                    }
                }
            }

            var components = new List<List<int>>();
            visited = new bool[count];
            var stack = new List<int>();
            for (int orderIndex = finishOrder.Count - 1; orderIndex >= 0; orderIndex--)
            {
                int root = finishOrder[orderIndex];
                if (visited[root]) continue;
                var component = new List<int>();
                visited[root] = true;
                stack.Add(root);
                while (stack.Count > 0)
                {
                    int node = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);
                    component.Add(node);
                    IReadOnlyList<int> parents = graph.ParentsOf(node);
                    for (int i = 0; i < parents.Count; i++)
                    {
                        int parent = parents[i];
                        if (visited[parent]) continue;
                        visited[parent] = true;
                        stack.Add(parent);
                    }
                }
                components.Add(component);
            }
            return components;
        }
    }
}
