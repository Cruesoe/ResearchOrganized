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
            DisplayMode.RegisterWindow(typeof(MainTabWindow_Organized), () => new MainTabWindow_Organized());
        }
    }
}
