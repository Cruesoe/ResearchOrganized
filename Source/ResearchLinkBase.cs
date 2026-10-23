using System.Collections.Generic;

namespace ResearchOrganized
{
    public abstract class ResearchLinkBase
    {
        public string child = "";
        public List<string> children = new List<string>();
        public string parent = "";
    }

    /// <summary>Steers layout only; never added to the project's prerequisites.</summary>
    public class VirtualLink : ResearchLinkBase { }

    /// <summary>Added to the child's real prerequisites.</summary>
    public class VisibleLink : ResearchLinkBase { }
}
