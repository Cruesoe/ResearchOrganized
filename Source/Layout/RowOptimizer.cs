using System;
using System.Collections.Generic;

namespace ResearchOrganized.Layout
{
    /// <summary>
    /// Improves the initial epoch layout without changing its structural decisions. Only rows
    /// of non-anchor nodes in the same column may be exchanged, so dependency direction,
    /// epochs, column capacity, and anchor columns remain intact.
    /// </summary>
    public static class RowOptimizer
    {
        private const int MaxPasses = 6;
        private const int MaxCandidateEvaluations = 256;

        private struct Score
        {
            public int Crossings;
            public long AuthoredMovement;
            public long VerticalSpan;
        }

        public static void Improve(LayoutGraph graph, LayoutOptions options, bool[] isAnchor, int[] column, int[] row)
        {
            if (graph.NodeCount < 2) return;
            if (graph.EdgeCount == 0 && (options.preferredRow == null || options.preferredRow.Length != graph.NodeCount)) return;

            int minColumn = int.MaxValue;
            int maxColumn = int.MinValue;
            for (int node = 0; node < graph.NodeCount; node++)
            {
                if (column[node] < minColumn) minColumn = column[node];
                if (column[node] > maxColumn) maxColumn = column[node];
            }

            Score current = Evaluate(graph, options, row, column);
            int candidateEvaluations = 0;
            for (int pass = 0; pass < MaxPasses; pass++)
            {
                bool improved = false;
                bool reverse = (pass & 1) != 0;
                int start = reverse ? maxColumn : minColumn;
                int end = reverse ? minColumn : maxColumn;
                int step = reverse ? -1 : 1;

                for (int currentColumn = start; reverse ? currentColumn >= end : currentColumn <= end; currentColumn += step)
                {
                    var nodes = new List<int>();
                    for (int node = 0; node < graph.NodeCount; node++)
                        if (column[node] == currentColumn) nodes.Add(node);
                    nodes.Sort((a, b) => row[a].CompareTo(row[b]));

                    int pairStart = reverse ? nodes.Count - 2 : 0;
                    int pairEnd = reverse ? -1 : nodes.Count - 1;
                    int pairStep = reverse ? -1 : 1;
                    for (int i = pairStart; reverse ? i > pairEnd : i < pairEnd; i += pairStep)
                    {
                        int first = nodes[i];
                        int second = nodes[i + 1];
                        if (isAnchor[first] || isAnchor[second]) continue;

                        Swap(row, first, second);
                        Score candidate = Evaluate(graph, options, row, column);
                        candidateEvaluations++;
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

                        if (candidateEvaluations >= MaxCandidateEvaluations) return;
                    }
                }

                if (!improved) break;
            }
        }

        private static Score Evaluate(LayoutGraph graph, LayoutOptions options, int[] row, int[] column)
        {
            var score = new Score { Crossings = CrossingCounter.Count(graph, column, row) };

            if (options.preferredRow != null && options.preferredRow.Length == graph.NodeCount)
            {
                for (int node = 0; node < graph.NodeCount; node++)
                    score.AuthoredMovement += Math.Abs((long)row[node] - options.preferredRow[node]);
            }

            foreach (LayoutGraph.Edge edge in graph.AllEdges())
                score.VerticalSpan += Math.Abs((long)row[edge.Parent] - row[edge.Child]);

            return score;
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
