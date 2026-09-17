namespace ResearchOrganized.Layout
{
    /// <summary>
    /// Keeps a follow-up project out of an era before its prerequisite's. A mod can give a
    /// project a lower tech level than the project it depends on - Apex Mechanoids' Spacer
    /// follow-ups to its Ultra hub, for one - and laying that out by tech level puts the
    /// follow-up in an earlier block, with its connector running backwards across the tab.
    /// Each node is raised to its highest parent's era, transitively. The caller writes the
    /// result back as the project's real tech level.
    /// </summary>
    public static class EraPromotion
    {
        /// <param name="era">Each node's own era, higher is later.</param>
        /// <param name="unranked">An era value outside the order - no tech level. It neither
        /// raises a follow-up nor is raised itself.</param>
        public static int[] Raise(LayoutGraph graph, int[] era, int unranked)
        {
            var result = (int[])era.Clone();

            // Every pass can only raise values towards a finite maximum, so this settles;
            // the pass cap only guards against a mistake, since a chain of n nodes needs at
            // most n passes, cycles included.
            bool changed = true;
            for (int pass = 0; changed && pass <= graph.NodeCount; pass++)
            {
                changed = false;
                for (int node = 0; node < graph.NodeCount; node++)
                {
                    if (result[node] == unranked) continue;
                    var parents = graph.ParentsOf(node);
                    for (int i = 0; i < parents.Count; i++)
                    {
                        int parentEra = result[parents[i]];
                        if (parentEra != unranked && parentEra > result[node])
                        {
                            result[node] = parentEra;
                            changed = true;
                        }
                    }
                }
            }

            return result;
        }
    }
}
