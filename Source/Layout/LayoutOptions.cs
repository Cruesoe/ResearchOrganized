using System.Collections.Generic;

namespace ResearchOrganized.Layout
{
    public sealed class LayoutOptions
    {
        public float xStep = 1f;
        public float yStep = 0.63f;

        /// <summary>Hard cap on cards per column. 0 or less means unbounded.</summary>
        public int maxNodesPerColumn = 12;

        /// <summary>Which tech level (or other grouping) each node belongs to. Processed in ascending order.</summary>
        public int[] epoch;

        /// <summary>Whether each node is a hub, placed ahead of the followers it claims.</summary>
        public bool[] isAnchor;

        /// <summary>Whether each anchor is a major hub: placed alone, in a new column after everything placed before it.</summary>
        public bool[] isMajorAnchor;

        /// <summary>Whether each node is its era's finale: the last anchor placed in its epoch, followed only by its own descendants.</summary>
        public bool[] isFinale;

        /// <summary>
        /// Global placement order for anchors, lower first - normally shallowest in the
        /// research tree first, so a node depending on two anchors reads as belonging to the
        /// deeper, more specific one.
        /// </summary>
        public int[] anchorOrder;

        /// <summary>Tie-break for otherwise-equal placement choices, lower first - the caller puts cheap projects first.</summary>
        public int[] tieRank;

        /// <summary>
        /// A node that always goes last in its epoch, after everything else is placed,
        /// regardless of what prerequisites it does or does not have - an era's "advance to
        /// the next tech level" node, from mods like Node Research.
        /// </summary>
        public bool[] isCapstone;

        /// <summary>When set, placement decisions are appended here as text, naming nodes from <see cref="nodeNames"/>.</summary>
        public List<string> trace;
        public string[] nodeNames;
    }
}
