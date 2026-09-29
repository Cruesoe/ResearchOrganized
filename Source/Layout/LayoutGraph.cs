using System;
using System.Collections.Generic;

namespace ResearchOrganized.Layout
{
    /// <summary>
    /// A directed graph over nodes 0..NodeCount-1. Edges run parent -> child.
    ///
    /// An edge also carries whether the research window draws a connector for it. Both kinds
    /// order the cards; only a drawn one can be seen to cross or wander, so anything judged on
    /// the picture works from <see cref="DrawnOnly"/>.
    ///
    /// Deliberately free of RimWorld and Unity types: everything in the Layout namespace
    /// is pure so it can be exercised by the test harness without launching the game.
    /// </summary>
    public sealed class LayoutGraph
    {
        private readonly List<int>[] outEdges;
        private readonly List<int>[] inEdges;
        private readonly HashSet<Edge> undrawn = new HashSet<Edge>();

        public LayoutGraph(int nodeCount)
        {
            if (nodeCount < 0) throw new ArgumentOutOfRangeException(nameof(nodeCount));

            NodeCount = nodeCount;
            outEdges = new List<int>[nodeCount];
            inEdges = new List<int>[nodeCount];
            for (int i = 0; i < nodeCount; i++)
            {
                outEdges[i] = new List<int>();
                inEdges[i] = new List<int>();
            }
        }

        public int NodeCount { get; }

        public int EdgeCount { get; private set; }

        /// <summary>Adds a drawn parent -> child. Self loops and duplicates are ignored.</summary>
        public bool AddEdge(int parent, int child)
        {
            return AddEdge(parent, child, true);
        }

        /// <summary>
        /// Adds parent -> child, saying whether the research window draws a connector for it.
        /// A link added both ways - a project naming the same prerequisite twice, once visibly -
        /// counts as drawn, since the line is there either way.
        /// </summary>
        public bool AddEdge(int parent, int child, bool drawn)
        {
            if (parent == child) return false;
            if (outEdges[parent].Contains(child))
            {
                if (drawn) undrawn.Remove(new Edge(parent, child));
                return false;
            }

            outEdges[parent].Add(child);
            inEdges[child].Add(parent);
            if (!drawn) undrawn.Add(new Edge(parent, child));
            EdgeCount++;
            return true;
        }

        public bool HasEdge(int parent, int child)
        {
            return outEdges[parent].Contains(child);
        }

        /// <summary>Whether the research window draws a connector for this edge.</summary>
        public bool IsDrawn(int parent, int child)
        {
            return !undrawn.Contains(new Edge(parent, child));
        }

        /// <summary>
        /// A copy holding only the edges that have a connector on screen. A hidden prerequisite
        /// or a layout-only virtual link still belongs in the full graph - it decides which
        /// column a card lands in - but there is no line to untangle or shorten, so it is left
        /// out of everything scored on how the tab looks.
        /// </summary>
        public LayoutGraph DrawnOnly()
        {
            var copy = new LayoutGraph(NodeCount);
            foreach (var edge in AllEdges())
                if (IsDrawn(edge.Parent, edge.Child)) copy.AddEdge(edge.Parent, edge.Child);
            return copy;
        }

        public IReadOnlyList<int> ChildrenOf(int node)
        {
            return outEdges[node];
        }

        public IReadOnlyList<int> ParentsOf(int node)
        {
            return inEdges[node];
        }

        /// <summary>Every edge as a (parent, child) pair, in insertion order per parent.</summary>
        public List<Edge> AllEdges()
        {
            var result = new List<Edge>(EdgeCount);
            for (int parent = 0; parent < NodeCount; parent++)
            {
                var children = outEdges[parent];
                for (int i = 0; i < children.Count; i++) result.Add(new Edge(parent, children[i]));
            }
            return result;
        }

        /// <summary>A copy with the given edges flipped. Used to make the graph acyclic.</summary>
        public LayoutGraph WithReversedEdges(ICollection<Edge> reversed)
        {
            var copy = new LayoutGraph(NodeCount);
            foreach (var edge in AllEdges())
            {
                bool drawn = IsDrawn(edge.Parent, edge.Child);
                if (reversed.Contains(edge)) copy.AddEdge(edge.Child, edge.Parent, drawn);
                else copy.AddEdge(edge.Parent, edge.Child, drawn);
            }
            return copy;
        }

        public struct Edge : IEquatable<Edge>
        {
            public readonly int Parent;
            public readonly int Child;

            public Edge(int parent, int child)
            {
                Parent = parent;
                Child = child;
            }

            public bool Equals(Edge other)
            {
                return Parent == other.Parent && Child == other.Child;
            }

            public override bool Equals(object obj)
            {
                return obj is Edge && Equals((Edge)obj);
            }

            public override int GetHashCode()
            {
                unchecked { return (Parent * 397) ^ Child; }
            }

            public override string ToString()
            {
                return Parent + "->" + Child;
            }
        }
    }
}
