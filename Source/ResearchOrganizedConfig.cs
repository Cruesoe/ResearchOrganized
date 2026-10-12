using System.Collections.Generic;
using Verse;

namespace ResearchOrganized
{
    public class ResearchOrganizedConfig : Def
    {
        public List<string> targetTabs = new List<string>();
        public List<string> ignoredTabs = new List<string>();
        public List<string> preservedTabs = new List<string>();
        /// <summary>Tabs that keep their projects under "Combine All Tabs", alongside Gravship's.</summary>
        public List<string> uncombinedTabs = new List<string>();
        public List<string> tabOrder = new List<string>();
        public List<VirtualLink> virtualLinks = new List<VirtualLink>();
        public List<VisibleLink> visibleLinks = new List<VisibleLink>();

        public float xStep = -1f;
        public float yStep = -1f;
        public int maxNodesPerColumn = -1;

        public List<TabThemeEntry> tabThemes = new List<TabThemeEntry>();
        public List<TechLevelTabEntry> techLevelTabs = new List<TechLevelTabEntry>();
    }
}
