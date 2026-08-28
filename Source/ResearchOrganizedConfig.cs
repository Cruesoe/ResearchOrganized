using RimWorld;
using System.Collections.Generic;
using Verse;

namespace ResearchOrganized
{
    public abstract class ResearchLinkBase
    {
        public string child = "";
        public List<string> children = new List<string>();
        public string parent = "";
    }

    public class VirtualLink : ResearchLinkBase { }

    public class VisibleLink : ResearchLinkBase { }

    public class TabThemeEntry
    {
        public string tabName = "";
        public TechLevel techLevel = TechLevel.Undefined;
    }

    public class ResearchOrganizedConfig : Def
    {
        public List<string> targetTabs = new List<string>();
        public List<string> ignoredTabs = new List<string>();
        public List<string> tabOrder = new List<string>();
        public List<VirtualLink> virtualLinks = new List<VirtualLink>();
        public List<VisibleLink> visibleLinks = new List<VisibleLink>();

        public float xStep = -1f;
        public float yStep = -1f;
        public int maxNodesPerColumn = -1;

        public List<TabThemeEntry> tabThemes = new List<TabThemeEntry>();
    }

    public class LayoutConfig
    {
        public float xStep = 1f;
        public float yStep = 0.63f;
        public int maxNodesPerColumn = 12;
    }
}