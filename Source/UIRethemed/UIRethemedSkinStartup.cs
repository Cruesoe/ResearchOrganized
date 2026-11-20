using ResearchOrganized;
using Verse;

namespace ResearchOrganizedUIRethemed
{
    [StaticConstructorOnStartup]
    public static class UIRethemedSkinStartup
    {
        static UIRethemedSkinStartup()
        {
            ResearchSkin.Register(new UIRethemedSkin());
        }
    }
}
