using System;
using System.Collections.Generic;

namespace ResearchOrganized.Layout
{
    /// <summary>
    /// Improves rows without changing columns, anchors, epochs, or capacity. Barycentric sweeps
    /// make broad moves toward connected neighbours; adjacent swaps then refine the result.
    /// Swap scoring is local to incident edges, avoiding a full graph recount per candidate.
    /// </summary>
    public static class RowOptimizer
    {
        private const int MaxPasses = 6;

        private struct Score
        {
            public int Crossings;
            public long AuthoredMovement;
            public long VerticalSpan;
        }

        private struct LocalScore
        {
            public int Crossings;
            public long AuthoredMovement;
            public long VerticalSpan;
        }

        public static void Improve(LayoutGraph graph, LayoutOptions options, bool[] isAnchor, int[] column, int[] row)
        {
            if (graph.NodeCount < 2) return;
            if (graph.EdgeCount == 0 && (options.preferredRow == null || options.preferredRow.Length != graph.NodeCount)) return;

            List<LayoutGraph.Edge> edges = graph.AllEdges();
            Score current = Evaluate(graph, edges, options, column, row);
            int minColumn = int.MaxValue;
            int maxColumn = int.MinValue;
            for (int node = 0; node < graph.NodeCount; node++)
            {
                if (column[node] < minColumn) minColumn = column[node];
                if (column[node] > maxColumn) maxColumn = column[node];
            }

            for (int pass = 0; pass < MaxPasses; pass++)
            {
                bool reverse = (pass & 1) != 0;
                bool improved = BarycentricSweep(graph, edges, options, isAnchor, column, row,
                    minColumn, maxColumn, reverse, ref current);
                if (AdjacentSweep(graph, edges, options, isAnchor, column, row,
                    minColumn, maxColumn, reverse, ref current)) improved = true;
                if (!improved) break;
            }
        }

        private static bool BarycentricSweep(LayoutGraph graph, List<LayoutGraph.Edge> edges, LayoutOptions options,
            bool[] isAnchor, int[] column, int[] row, int minColumn, int maxColumn, bool reverse, ref Score current)
        {
            bool improved = false;
            int start = reverse ? maxColumn : minColumn;
            int end = reverse ? minColumn : maxColumn;
            int step = reverse ? -1 : 1;

            for (int currentColumn = start; reverse ? currentColumn >= end : currentColumn <= end; currentColumn += step)
            {
                var ordered = NodesInColumn(graph.NodeCount, currentColumn, column, row);
                int segmentStart = 0;
                while (segmentStart < ordered.Count)
                {
                    while (segmentStart < ordered.Count && isAnchor[ordered[segmentStart]]) segmentStart++;
                    int segmentEnd = segmentStart;
                    while (segmentEnd < ordered.Count && !isAnchor[ordered[segmentEnd]]) segmentEnd++;
                    if (segmentEnd - segmentStart > 1
                        && TryBarycentricSegment(graph, edges, options, column, row, ordered,
                            segmentStart, segmentEnd, reverse, ref current)) improved = true;
                    segmentStart = segmentEnd + 1;
                }
            }
            return improved;
        }

        private static bool TryBarycentricSegment(LayoutGraph graph, List<LayoutGraph.Edge> edges, LayoutOptions options,
            int[] column, int[] row, List<int> ordered, int start, int end, bool reverse, ref Score current)
        {
            var originalNodes = new List<int>();
            var slots = new List<int>();
            for (int i = start; i < end; i++)
            {
                originalNodes.Add(ordered[i]);
                slots.Add(row[ordered[i]]);
            }

            var candidateNodes = new List<int>(originalNodes);
            candidateNodes.Sort((first, second) =>
            {
                double firstCenter = Barycenter(graph, first, row, reverse);
                double secondCenter = Barycenter(graph, second, row, reverse);
                int comparison = firstCenter.CompareTo(secondCenter);
                if (comparison != 0) return comparison;
                comparison = PreferredRow(options, first, row).CompareTo(PreferredRow(options, second, row));
                if (comparison != 0) return comparison;
                return first.CompareTo(second);
            });

            bool changed = false;
            for (int i = 0; i < candidateNodes.Count; i++)
                if (candidateNodes[i] != originalNodes[i]) { changed = true; break; }
            if (!changed) return false;

            var affectedNodes = new HashSet<int>(originalNodes);
            HashSet<int> affectedEdges = IncidentEdges(edges, affectedNodes);
            LocalScore before = EvaluateLocal(edges, options, column, row, affectedNodes, affectedEdges, graph);
            for (int i = 0; i < candidateNodes.Count; i++) row[candidateNodes[i]] = slots[i];
            LocalScore after = EvaluateLocal(edges, options, column, row, affectedNodes, affectedEdges, graph);
            Score candidate = ApplyDelta(current, before, after);

            if (IsBetter(candidate, current))
            {
                current = candidate;
                for (int i = 0; i < candidateNodes.Count; i++) ordered[start + i] = candidateNodes[i];
                return true;
            }

            for (int i = 0; i < originalNodes.Count; i++) row[originalNodes[i]] = slots[i];
            return false;
        }

        private static bool AdjacentSweep(LayoutGraph graph, List<LayoutGraph.Edge> edges, LayoutOptions options,
            bool[] isAnchor, int[] column, int[] row, int minColumn, int maxColumn, bool reverse, ref Score current)
        {
            bool improved = false;
            int start = reverse ? maxColumn : minColumn;
            int end = reverse ? minColumn : maxColumn;
            int step = reverse ? -1 : 1;

            for (int currentColumn = start; reverse ? currentColumn >= end : currentColumn <= end; currentColumn += step)
            {
                var nodes = NodesInColumn(graph.NodeCount, currentColumn, column, row);
                int pairStart = reverse ? nodes.Count - 2 : 0;
                int pairEnd = reverse ? -1 : nodes.Count - 1;
                int pairStep = reverse ? -1 : 1;

                for (int i = pairStart; reverse ? i > pairEnd : i < pairEnd; i += pairStep)
                {
                    int first = nodes[i];
                    int second = nodes[i + 1];
                    if (isAnchor[first] || isAnchor[second]) continue;

                    var affectedNodes = new HashSet<int> { first, second };
                    HashSet<int> affectedEdges = IncidentEdges(edges, affectedNodes);
                    LocalScore before = EvaluateLocal(edges, options, column, row, affectedNodes, affectedEdges, graph);
                    Swap(row, first, second);
                    LocalScore after = EvaluateLocal(edges, options, column, row, affectedNodes, affectedEdges, graph);
                    Score candidate = ApplyDelta(current, before, after);

                    if (IsBetter(candidate, current))
                    {
                        current = candidate;
                        nodes[i] = second;
                        nodes[i + 1] = first;
                        improved = true;
                    }
                    else
                    {
                        Swap(row, first, second);
                    }
                }
            }
            return improved;
        }

        private static List<int> NodesInColumn(int nodeCount, int wantedColumn, int[] column, int[] row)
        {
            var nodes = new List<int>();
            for (int node = 0; node < nodeCount; node++) if (column[node] == wantedColumn) nodes.Add(node);
            nodes.Sort((first, second) => row[first].CompareTo(row[second]));
            return nodes;
        }

        private static double Barycenter(LayoutGraph graph, int node, int[] row, bool useChildren)
        {
            IReadOnlyList<int> neighbours = useChildren ? graph.ChildrenOf(node) : graph.ParentsOf(node);
            if (neighbours.Count == 0) neighbours = useChildren ? graph.ParentsOf(node) : graph.ChildrenOf(node);
            if (neighbours.Count == 0) return row[node];
            long total = 0;
            for (int i = 0; i < neighbours.Count; i++) total += row[neighbours[i]];
            return (double)total / neighbours.Count;
        }

        private static int PreferredRow(LayoutOptions options, int node, int[] row)
        {
            return options.preferredRow != null && options.preferredRow.Length > node ? options.preferredRow[node] : row[node];
        }

        private static Score Evaluate(LayoutGraph graph, List<LayoutGraph.Edge> edges, LayoutOptions options, int[] column, int[] row)
        {
            var score = new Score { Crossings = CrossingCounter.Count(graph, column, row, options.xStep, options.yStep) };
            for (int node = 0; node < graph.NodeCount; node++)
                score.AuthoredMovement += Math.Abs((long)row[node] - PreferredRow(options, node, row));
            for (int i = 0; i < edges.Count; i++)
                score.VerticalSpan += Math.Abs((long)row[edges[i].Parent] - row[edges[i].Child]);
            return score;
        }

        private static LocalScore EvaluateLocal(List<LayoutGraph.Edge> edges, LayoutOptions options, int[] column, int[] row,
            HashSet<int> affectedNodes, HashSet<int> affectedEdges, LayoutGraph graph)
        {
            var score = new LocalScore
            {
                Crossings = CrossingCounter.CountAffected(graph, edges, column, row, options.xStep, options.yStep, affectedEdges)
            };
            foreach (int node in affectedNodes)
                score.AuthoredMovement += Math.Abs((long)row[node] - PreferredRow(options, node, row));
            foreach (int edgeIndex in affectedEdges)
            {
                LayoutGraph.Edge edge = edges[edgeIndex];
                score.VerticalSpan += Math.Abs((long)row[edge.Parent] - row[edge.Child]);
            }
            return score;
        }

        private static HashSet<int> IncidentEdges(List<LayoutGraph.Edge> edges, HashSet<int> nodes)
        {
            var result = new HashSet<int>();
            for (int edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
                if (nodes.Contains(edges[edgeIndex].Parent) || nodes.Contains(edges[edgeIndex].Child)) result.Add(edgeIndex);
            return result;
        }

        private static Score ApplyDelta(Score current, LocalScore before, LocalScore after)
        {
            return new Score
            {
                Crossings = current.Crossings - before.Crossings + after.Crossings,
                AuthoredMovement = current.AuthoredMovement - before.AuthoredMovement + after.AuthoredMovement,
                VerticalSpan = current.VerticalSpan - before.VerticalSpan + after.VerticalSpan
            };
        }

        private static bool IsBetter(Score candidate, Score current)
        {
            if (candidate.Crossings != current.Crossings) return candidate.Crossings < current.Crossings;
            if (candidate.AuthoredMovement != current.AuthoredMovement) return candidate.AuthoredMovement < current.AuthoredMovement;
            return candidate.VerticalSpan < current.VerticalSpan;
        }

        private static void Swap(int[] values, int first, int second)
        {
            int temporary = values[first];
            values[first] = values[second];
            values[second] = temporary;
        }
    }
}
