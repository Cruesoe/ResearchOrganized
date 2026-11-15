using System;
using System.Collections.Generic;

namespace ResearchOrganized.Layout
{
    /// <summary>
    /// Improves rows without changing columns, anchors, epochs, or capacity. Barycentric sweeps
    /// make broad moves toward connected neighbors; adjacent swaps then refine the result, and a
    /// final pass slides cards into free rows level with a prerequisite.
    /// Swap scoring is local to incident edges, avoiding a full graph recount per candidate.
    ///
    /// Judged on the drawn tree alone - fewest crossing connectors, then shortest connectors,
    /// then most level connectors. A project's authored row in its mod's XML is not considered.
    ///
    /// The caller passes the drawn view of the graph, so a link with no connector - a hidden
    /// prerequisite, a virtual one - leaves no pull here at all: it has already had its say in
    /// which column the card sits in, and there is no line to be seen crossing or wandering.
    /// </summary>
    public static class RowOptimizer
    {
        private const int MaxPasses = 6;

        private struct Score
        {
            public int Crossings;
            public long VerticalSpan;
            public int LevelLinks;
        }

        private struct LocalScore
        {
            public int Crossings;
            public long VerticalSpan;
            public int LevelLinks;
        }

        public static void Improve(LayoutGraph graph, LayoutOptions options, bool[] isAnchor, int[] column, int[] row)
        {
            if (graph.NodeCount < 2) return;
            if (graph.EdgeCount == 0) return; // nothing to uncross or shorten

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

            for (int pass = 0; pass < MaxPasses; pass++)
            {
                if (!StraightenSweep(graph, edges, options, isAnchor, column, row, ref current)) break;
            }
        }

        /// <summary>
        /// Slides each card into a free row level with one of its prerequisites, taking along any
        /// followers already level with it. Anchors stay put, so they stay centered on their followers.
        /// </summary>
        private static bool StraightenSweep(LayoutGraph graph, List<LayoutGraph.Edge> edges, LayoutOptions options,
            bool[] isAnchor, int[] column, int[] row, ref Score current)
        {
            var occupied = new HashSet<long>();
            var nodes = new List<int>();
            for (int node = 0; node < graph.NodeCount; node++)
            {
                occupied.Add(CellKey(column[node], row[node]));
                nodes.Add(node);
            }
            nodes.Sort((first, second) =>
            {
                if (column[first] != column[second]) return column[first].CompareTo(column[second]);
                return row[first].CompareTo(row[second]);
            });

            bool improved = false;
            foreach (int node in nodes)
            {
                if (isAnchor[node]) continue;
                IReadOnlyList<int> parents = graph.ParentsOf(node);
                for (int i = 0; i < parents.Count; i++)
                {
                    int target = row[parents[i]];
                    if (target == row[node]) continue;
                    List<int> block = LevelBlock(graph, isAnchor, row, node);
                    if (TryMoveBlock(graph, edges, options, column, row, occupied, block, target, ref current)) improved = true;
                }
            }
            return improved;
        }

        /// <summary>The card and every non-anchor follower reached through level connectors.</summary>
        private static List<int> LevelBlock(LayoutGraph graph, bool[] isAnchor, int[] row, int start)
        {
            var block = new List<int> { start };
            var seen = new HashSet<int> { start };
            for (int i = 0; i < block.Count; i++)
            {
                IReadOnlyList<int> children = graph.ChildrenOf(block[i]);
                for (int c = 0; c < children.Count; c++)
                {
                    int child = children[c];
                    if (!isAnchor[child] && row[child] == row[start] && seen.Add(child)) block.Add(child);
                }
            }
            return block;
        }

        private static bool TryMoveBlock(LayoutGraph graph, List<LayoutGraph.Edge> edges, LayoutOptions options,
            int[] column, int[] row, HashSet<long> occupied, List<int> block, int target, ref Score current)
        {
            for (int i = 0; i < block.Count; i++)
                if (occupied.Contains(CellKey(column[block[i]], target))) return false;

            int source = row[block[0]];
            HashSet<int> affectedEdges = IncidentEdges(edges, new HashSet<int>(block));
            LocalScore before = EvaluateLocal(edges, options, column, row, affectedEdges, graph);
            for (int i = 0; i < block.Count; i++) row[block[i]] = target;
            LocalScore after = EvaluateLocal(edges, options, column, row, affectedEdges, graph);
            Score candidate = ApplyDelta(current, before, after);

            if (!IsBetter(candidate, current))
            {
                for (int i = 0; i < block.Count; i++) row[block[i]] = source;
                return false;
            }

            current = candidate;
            for (int i = 0; i < block.Count; i++)
            {
                occupied.Remove(CellKey(column[block[i]], source));
                occupied.Add(CellKey(column[block[i]], target));
            }
            return true;
        }

        private static long CellKey(int column, int row)
        {
            return ((long)column << 32) ^ (uint)row;
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
                return first.CompareTo(second);
            });

            bool changed = false;
            for (int i = 0; i < candidateNodes.Count; i++)
                if (candidateNodes[i] != originalNodes[i]) { changed = true; break; }
            if (!changed) return false;

            HashSet<int> affectedEdges = IncidentEdges(edges, new HashSet<int>(originalNodes));
            LocalScore before = EvaluateLocal(edges, options, column, row, affectedEdges, graph);
            for (int i = 0; i < candidateNodes.Count; i++) row[candidateNodes[i]] = slots[i];
            LocalScore after = EvaluateLocal(edges, options, column, row, affectedEdges, graph);
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

                    HashSet<int> affectedEdges = IncidentEdges(edges, new HashSet<int> { first, second });
                    LocalScore before = EvaluateLocal(edges, options, column, row, affectedEdges, graph);
                    Swap(row, first, second);
                    LocalScore after = EvaluateLocal(edges, options, column, row, affectedEdges, graph);
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
            IReadOnlyList<int> neighbors = useChildren ? graph.ChildrenOf(node) : graph.ParentsOf(node);
            if (neighbors.Count == 0) neighbors = useChildren ? graph.ParentsOf(node) : graph.ChildrenOf(node);
            if (neighbors.Count == 0) return row[node];
            long total = 0;
            for (int i = 0; i < neighbors.Count; i++) total += row[neighbors[i]];
            return (double)total / neighbors.Count;
        }

        private static Score Evaluate(LayoutGraph graph, List<LayoutGraph.Edge> edges, LayoutOptions options, int[] column, int[] row)
        {
            var score = new Score { Crossings = CrossingCounter.Count(graph, column, row, options.xStep, options.yStep) };
            for (int i = 0; i < edges.Count; i++)
            {
                score.VerticalSpan += Math.Abs((long)row[edges[i].Parent] - row[edges[i].Child]);
                if (row[edges[i].Parent] == row[edges[i].Child]) score.LevelLinks++;
            }
            return score;
        }

        private static LocalScore EvaluateLocal(List<LayoutGraph.Edge> edges, LayoutOptions options, int[] column, int[] row,
            HashSet<int> affectedEdges, LayoutGraph graph)
        {
            var score = new LocalScore
            {
                Crossings = CrossingCounter.CountAffected(graph, edges, column, row, options.xStep, options.yStep, affectedEdges)
            };
            foreach (int edgeIndex in affectedEdges)
            {
                LayoutGraph.Edge edge = edges[edgeIndex];
                score.VerticalSpan += Math.Abs((long)row[edge.Parent] - row[edge.Child]);
                if (row[edge.Parent] == row[edge.Child]) score.LevelLinks++;
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
                VerticalSpan = current.VerticalSpan - before.VerticalSpan + after.VerticalSpan,
                LevelLinks = current.LevelLinks - before.LevelLinks + after.LevelLinks
            };
        }

        private static bool IsBetter(Score candidate, Score current)
        {
            if (candidate.Crossings != current.Crossings) return candidate.Crossings < current.Crossings;
            if (candidate.VerticalSpan != current.VerticalSpan) return candidate.VerticalSpan < current.VerticalSpan;
            return candidate.LevelLinks > current.LevelLinks;
        }

        private static void Swap(int[] values, int first, int second)
        {
            int temporary = values[first];
            values[first] = values[second];
            values[second] = temporary;
        }
    }
}
