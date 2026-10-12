using RimWorld;

namespace ResearchOrganized
{
    /// <summary>Routes a tech level's projects to the given tab instead of this mod's own.</summary>
    public class TechLevelTabEntry
    {
        public TechLevel techLevel = TechLevel.Undefined;
        public string tabName = "";
    }
}
