using System;
using System.Collections.Generic;

namespace ResearchOrganized.Layout
{
    public sealed class LayoutOptions
    {
        public float xStep = 1f;
        public float yStep = 0.63f;

        /// <summary>
        /// Cap on nodes per column. Also the height budget used when packing separate
        /// components into a tab. 0 or less means unbounded.
        /// </summary>
        public int maxNodesPerColumn = 12;

        public int orderingSweeps = 8;
        public int coordinateSweeps = 4;
    }

    public sealed class LayoutResult
    {
        /// <summary>Coordinates indexed by the caller's node id.</summary>
        public float[] X;
        public float[] Y;

        /// <summary>Column index per node, derived from the final X.</summary>
        public int[] Layer;

        /// <summary>Nodes that sat on an edge which had to be reversed to break a cycle.</summary>
        public HashSet<int> NodesInCycles = new HashSet<int>();

        public List<LayoutGraph.Edge> ReversedEdges = new List<LayoutGraph.Edge>();

        /// <summary>Edge crossings remaining in the final ordering. Lower is better.</summary>
        public int Crossings;
    }

    /// <summary>
    /// Layered graph drawing, the standard four-stage pipeline:
    ///
    ///   1. break cycles by reversing a small set of edges
    ///   2. assign layers, respecting a maximum column width
    ///   3. order within layers to minimise edge crossings
    ///   4. assign coordinates, straightening long chains
    ///
    /// Run per connected component rather than over the whole tab at once, then the
    /// components are packed together. Sharing one column budget between unrelated
    /// fragments is what used to push a node many columns away from its own parent.
    /// </summary>
    public static class SugiyamaLayout
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

            var components = ComponentFinder.Find(graph);
            var laidOut = new List<ComponentLayout>(components.Count);

            foreach (var component in components)
            {
                laidOut.Add(ComputeComponent(component, options, result));
            }

            Pack(laidOut, options);

            foreach (var component in laidOut)
            {
                var map = component.Source.LocalToGlobal;
                for (int local = 0; local < map.Length; local++)
                {
                    int global = map[local];
                    result.X[global] = component.X[local] + component.OffsetX;
                    result.Y[global] = component.Y[local] + component.OffsetY;
                }
            }

            float xStep = options.xStep > 0f ? options.xStep : 1f;
            for (int node = 0; node < graph.NodeCount; node++)
            {
                result.Layer[node] = (int)Math.Round(result.X[node] / xStep);
            }

            return result;
        }

        private sealed class ComponentLayout
        {
            public Component Source;
            public float[] X;
            public float[] Y;
            public float Width;
            public float Height;
            public float OffsetX;
            public float OffsetY;
        }

        /// <summary>Runs the four stages over one component and records its extents.</summary>
        private static ComponentLayout ComputeComponent(Component component, LayoutOptions options, LayoutResult aggregate)
        {
            var sub = component.SubGraph;
            var map = component.LocalToGlobal;

            var broken = CycleBreaker.Break(sub);
            foreach (var edge in broken.ReversedEdges)
            {
                aggregate.ReversedEdges.Add(new LayoutGraph.Edge(map[edge.Parent], map[edge.Child]));
            }
            foreach (int local in broken.NodesInCycles) aggregate.NodesInCycles.Add(map[local]);

            int[] layerOf = Layering.Assign(broken.Acyclic, options.maxNodesPerColumn);

            var layered = LayeredGraph.Build(broken.Acyclic, layerOf);
            Ordering.Optimize(layered, options.orderingSweeps);

            float[] y;
            CoordinateAssigner.Assign(layered, options.yStep, options.coordinateSweeps, out y);

            aggregate.Crossings += layered.CountCrossings();

            var layout = new ComponentLayout
            {
                Source = component,
                X = new float[sub.NodeCount],
                Y = new float[sub.NodeCount]
            };

            float maxX = 0f, minY = float.MaxValue, maxY = float.MinValue;
            for (int local = 0; local < sub.NodeCount; local++)
            {
                layout.X[local] = layerOf[local] * options.xStep;
                layout.Y[local] = y[local];

                if (layout.X[local] > maxX) maxX = layout.X[local];
                if (y[local] < minY) minY = y[local];
                if (y[local] > maxY) maxY = y[local];
            }

            // Normalise each component to its own origin so packing controls placement.
            for (int local = 0; local < sub.NodeCount; local++) layout.Y[local] -= minY;

            layout.Width = maxX;
            layout.Height = maxY - minY;
            return layout;
        }

        /// <summary>
        /// Shelf packing, left aligned. Components are stacked down a shelf until the next
        /// one would exceed the column height budget, then a new shelf starts to the right.
        /// Nothing is centred and no shelf is padded, so a tab of loose nodes comes out as a
        /// tight block starting at the origin rather than a sparse spread.
        /// </summary>
        private static void Pack(List<ComponentLayout> components, LayoutOptions options)
        {
            if (components.Count == 0) return;

            float yStep = options.yStep > 0f ? options.yStep : 0.63f;
            float xStep = options.xStep > 0f ? options.xStep : 1f;

            float budget = options.maxNodesPerColumn > 0
                ? options.maxNodesPerColumn * yStep
                : float.MaxValue;

            // Tallest first packs shelves more fully; ties keep the deterministic order
            // ComponentFinder produced.
            var order = new List<int>(components.Count);
            for (int i = 0; i < components.Count; i++) order.Add(i);
            order.Sort(delegate (int a, int b)
            {
                int compared = components[b].Height.CompareTo(components[a].Height);
                if (compared != 0) return compared;
                return a.CompareTo(b);
            });

            float shelfX = 0f;
            float cursorY = 0f;
            float shelfWidth = 0f;

            for (int i = 0; i < order.Count; i++)
            {
                var component = components[order[i]];

                // Height including the row the component actually occupies.
                float occupied = component.Height + yStep;

                // Tolerance matters: cursorY accumulates one addition per component, so a
                // shelf that should hold exactly maxNodesPerColumn rows would otherwise
                // spill its last row into a new shelf on floating point error alone.
                float tolerance = yStep * 0.01f;

                if (cursorY > 0f && cursorY + occupied > budget + tolerance)
                {
                    shelfX += shelfWidth + xStep;
                    cursorY = 0f;
                    shelfWidth = 0f;
                }

                component.OffsetX = shelfX;
                component.OffsetY = cursorY;

                cursorY += occupied;
                if (component.Width > shelfWidth) shelfWidth = component.Width;
            }
        }
    }
}
