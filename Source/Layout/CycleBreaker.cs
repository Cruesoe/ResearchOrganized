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

            /// <summary>Nodes that sat on at least one reversed edge.</summary>
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
    }
}
