using System;
using System.Collections.Generic;

namespace ResearchOrganized.Layout
{
    /// <summary>
    /// Counts unique pairs of rendered prerequisite connectors that intersect. RimWorld 1.6
    /// draws a straight line from the prerequisite card's right edge to the dependent card's
    /// left edge. Its research view uses 190 pixels per X unit, 100 pixels per Y unit, and a
    /// 140-pixel card width; using those values here makes the optimizer measure the geometry
    /// players actually see.
    /// </summary>
    public static class CrossingCounter
    {
        private const double PixelsPerX = 190.0;
        private const double PixelsPerY = 100.0;
        private const double CardWidth = 140.0;
        private const double CardCenterY = 25.0;
        private const double Epsilon = 0.000001;

        private struct Point
        {
            public double X;
            public double Y;
        }

        public static int Count(LayoutGraph graph, int[] column, int[] row, float xStep = 1f, float yStep = 0.63f)
        {
            return CountAffected(graph, graph.AllEdges(), column, row, xStep, yStep, null);
        }

        /// <summary>Counts intersecting pairs where at least one edge is in affectedEdges.</summary>
        internal static int CountAffected(LayoutGraph graph, List<LayoutGraph.Edge> edges, int[] column, int[] row,
            float xStep, float yStep, HashSet<int> affectedEdges)
        {
            if (affectedEdges != null)
            {
                int affectedCrossings = 0;
                foreach (int firstIndex in affectedEdges)
                {
                    LayoutGraph.Edge first = edges[firstIndex];
                    for (int secondIndex = 0; secondIndex < edges.Count; secondIndex++)
                    {
                        if (secondIndex == firstIndex) continue;
                        // A pair of affected edges is visited from both sides; retain one ordering.
                        if (affectedEdges.Contains(secondIndex) && secondIndex < firstIndex) continue;
                        if (EdgesIntersect(first, edges[secondIndex], column, row, xStep, yStep)) affectedCrossings++;
                    }
                }
                return affectedCrossings;
            }

            int crossings = 0;
            for (int firstIndex = 0; firstIndex < edges.Count; firstIndex++)
            {
                LayoutGraph.Edge first = edges[firstIndex];
                for (int secondIndex = firstIndex + 1; secondIndex < edges.Count; secondIndex++)
                {
                    if (EdgesIntersect(first, edges[secondIndex], column, row, xStep, yStep)) crossings++;
                }
            }
            return crossings;
        }

        private static bool EdgesIntersect(LayoutGraph.Edge first, LayoutGraph.Edge second,
            int[] column, int[] row, float xStep, float yStep)
        {
            // Lines that meet at their shared project are a branch, not a crossing.
            if (first.Parent == second.Parent || first.Parent == second.Child
                || first.Child == second.Parent || first.Child == second.Child) return false;

            return Intersects(RenderedStart(first, column, row, xStep, yStep), RenderedEnd(first, column, row, xStep, yStep),
                              RenderedStart(second, column, row, xStep, yStep), RenderedEnd(second, column, row, xStep, yStep));
        }

        private static Point RenderedStart(LayoutGraph.Edge edge, int[] column, int[] row, float xStep, float yStep)
        {
            return new Point
            {
                X = column[edge.Parent] * xStep * PixelsPerX + CardWidth,
                Y = row[edge.Parent] * yStep * PixelsPerY + CardCenterY
            };
        }

        private static Point RenderedEnd(LayoutGraph.Edge edge, int[] column, int[] row, float xStep, float yStep)
        {
            return new Point
            {
                X = column[edge.Child] * xStep * PixelsPerX,
                Y = row[edge.Child] * yStep * PixelsPerY + CardCenterY
            };
        }

        private static bool Intersects(Point firstStart, Point firstEnd, Point secondStart, Point secondEnd)
        {
            double a = Cross(firstStart, firstEnd, secondStart);
            double b = Cross(firstStart, firstEnd, secondEnd);
            double c = Cross(secondStart, secondEnd, firstStart);
            double d = Cross(secondStart, secondEnd, firstEnd);

            if (OppositeSigns(a, b) && OppositeSigns(c, d)) return true;
            if (Math.Abs(a) <= Epsilon && OnSegment(firstStart, firstEnd, secondStart)) return true;
            if (Math.Abs(b) <= Epsilon && OnSegment(firstStart, firstEnd, secondEnd)) return true;
            if (Math.Abs(c) <= Epsilon && OnSegment(secondStart, secondEnd, firstStart)) return true;
            return Math.Abs(d) <= Epsilon && OnSegment(secondStart, secondEnd, firstEnd);
        }

        private static double Cross(Point start, Point end, Point point)
        {
            return (end.X - start.X) * (point.Y - start.Y) - (end.Y - start.Y) * (point.X - start.X);
        }

        private static bool OppositeSigns(double first, double second)
        {
            return (first < -Epsilon && second > Epsilon) || (first > Epsilon && second < -Epsilon);
        }

        private static bool OnSegment(Point start, Point end, Point point)
        {
            return point.X >= Math.Min(start.X, end.X) - Epsilon && point.X <= Math.Max(start.X, end.X) + Epsilon
                && point.Y >= Math.Min(start.Y, end.Y) - Epsilon && point.Y <= Math.Max(start.Y, end.Y) + Epsilon;
        }
    }
}
