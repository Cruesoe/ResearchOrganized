using System.Collections.Generic;

namespace ResearchOrganized.Layout
{
    /// <summary>
    /// Makes a graph acyclic by reversing a small set of edges (a feedback arc set).
    ///
    /// The old engine handled cycles by force-placing whichever node looked least blocked
    /// and drawing it in red. That leaves the cycle in the graph, so every later stage has
    /// to cope with it. Reversing the back edges instead means layering, ordering and
    /// coordinates all operate on a clean DAG; the reversed edges are reported so the
    /// caller can still flag them to the user.
    /// </summary>
    public static class CycleBreaker
    {
        public sealed class Result
        {
            public LayoutGraph Acyclic;
            public List<LayoutGraph.Edge> ReversedEdges = new List<LayoutGraph.Edge>();

            /// <summary>Every node belonging to a cyclic strongly connected component.</summary>
            public HashSet<int> NodesInCycles = new HashSet<int>();
        }

        private enum Mark { Unvisited, OnStack, Done }

        /// <summary>
        /// Depth-first search; any edge pointing back at a node still on the recursion
        /// stack closes a cycle, so it gets reversed. Iterative to avoid blowing the stack
        /// on deep modded research chains.
        /// </summary>
        public static Result Break(LayoutGraph graph)
        {
            var result = new Result();
            MarkCyclicComponents(graph, result.NodesInCycles);
            var marks = new Mark[graph.NodeCount];

            // (node, index of next child to examine)
            var stack = new List<KeyValuePair<int, int>>();

            for (int root = 0; root < graph.NodeCount; root++)
            {
                if (marks[root] != Mark.Unvisited) continue;

                marks[root] = Mark.OnStack;
                stack.Add(new KeyValuePair<int, int>(root, 0));

                while (stack.Count > 0)
                {
                    var frame = stack[stack.Count - 1];
                    int node = frame.Key;
                    int childIndex = frame.Value;
                    var children = graph.ChildrenOf(node);

                    if (childIndex >= children.Count)
                    {
                        marks[node] = Mark.Done;
                        stack.RemoveAt(stack.Count - 1);
                        continue;
                    }

                    stack[stack.Count - 1] = new KeyValuePair<int, int>(node, childIndex + 1);
                    int child = children[childIndex];

                    if (marks[child] == Mark.OnStack)
                    {
                        // Back edge: reverse it.
                        var edge = new LayoutGraph.Edge(node, child);
                        result.ReversedEdges.Add(edge);
                        result.NodesInCycles.Add(node);
                        result.NodesInCycles.Add(child);
                    }
                    else if (marks[child] == Mark.Unvisited)
                    {
                        marks[child] = Mark.OnStack;
                        stack.Add(new KeyValuePair<int, int>(child, 0));
                    }
                }
            }

            var reversedSet = new HashSet<LayoutGraph.Edge>(result.ReversedEdges);
            result.Acyclic = graph.WithReversedEdges(reversedSet);
            return result;
        }

        /// <summary>
        /// Kosaraju's algorithm. Both passes are iterative so very deep modded trees cannot
        /// exhaust the runtime stack. Components with more than one node are cycles because
        /// LayoutGraph rejects self-edges.
        /// </summary>
        private static void MarkCyclicComponents(LayoutGraph graph, HashSet<int> cyclicNodes)
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

                if (component.Count > 1)
                    for (int i = 0; i < component.Count; i++) cyclicNodes.Add(component[i]);
            }
        }
    }
}
