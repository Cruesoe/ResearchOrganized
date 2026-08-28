using System.Collections.Generic;

namespace ResearchOrganized.Layout
{
    /// <summary>
    /// Splits a graph into weakly connected components.
    ///
    /// This matters more than it sounds. A research tab holds one tech level, and
    /// prerequisites almost always cross tech levels, so most projects on a tab have no
    /// in-tab parent at all: a tab is typically dozens of loose nodes plus a handful of
    /// short chains. Laying that out as a single graph makes unrelated nodes compete for
    /// the same columns, and the width cap then flings a dependent node far to the right of
    /// its own parent. Laying out each component on its own and packing the results keeps
    /// every child beside its parent and leaves no gaps between fragments.
    /// </summary>
    public sealed class Component
    {
        /// <summary>Original node ids, ascending. Index into this is the local id.</summary>
        public int[] LocalToGlobal;

        /// <summary>The component as a standalone graph, numbered from zero.</summary>
        public LayoutGraph SubGraph;
    }

    public static class ComponentFinder
    {
        /// <summary>
        /// Components in a deterministic order: by the smallest node id each contains.
        /// Edges are treated as undirected for the purpose of grouping.
        /// </summary>
        public static List<Component> Find(LayoutGraph graph)
        {
            var componentOf = new int[graph.NodeCount];
            for (int i = 0; i < graph.NodeCount; i++) componentOf[i] = -1;

            var groups = new List<List<int>>();
            var stack = new List<int>();

            for (int root = 0; root < graph.NodeCount; root++)
            {
                if (componentOf[root] != -1) continue;

                int id = groups.Count;
                var members = new List<int>();
                groups.Add(members);

                componentOf[root] = id;
                stack.Add(root);

                while (stack.Count > 0)
                {
                    int node = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);
                    members.Add(node);

                    var children = graph.ChildrenOf(node);
                    for (int i = 0; i < children.Count; i++)
                    {
                        if (componentOf[children[i]] != -1) continue;
                        componentOf[children[i]] = id;
                        stack.Add(children[i]);
                    }

                    var parents = graph.ParentsOf(node);
                    for (int i = 0; i < parents.Count; i++)
                    {
                        if (componentOf[parents[i]] != -1) continue;
                        componentOf[parents[i]] = id;
                        stack.Add(parents[i]);
                    }
                }
            }

            var result = new List<Component>(groups.Count);
            foreach (var members in groups)
            {
                members.Sort();

                var localOf = new Dictionary<int, int>(members.Count);
                for (int i = 0; i < members.Count; i++) localOf[members[i]] = i;

                var sub = new LayoutGraph(members.Count);
                for (int i = 0; i < members.Count; i++)
                {
                    var children = graph.ChildrenOf(members[i]);
                    for (int c = 0; c < children.Count; c++) sub.AddEdge(i, localOf[children[c]]);
                }

                result.Add(new Component { LocalToGlobal = members.ToArray(), SubGraph = sub });
            }

            return result;
        }
    }
}
