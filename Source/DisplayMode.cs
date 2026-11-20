using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

// The UI Rethemed add-on's Rethemed window uses this assembly's internals.
[assembly: InternalsVisibleTo("ResearchOrganizedUIRethemed")]

namespace ResearchOrganized
{
    /// <summary>Which research window the Research button opens: the vanilla window (Classic) or the Rethemed window,
    /// which the UI Rethemed add-on registers when UI Rethemed - Core is active.</summary>
    public static class DisplayMode
    {
        public const int Classic = 0;
        public const int Rethemed = 1;

        private static readonly AccessTools.FieldRef<MainButtonDef, MainTabWindow> tabWindowRef =
            AccessTools.FieldRefAccess<MainButtonDef, MainTabWindow>("tabWindowInt");

        private static Type windowType;
        private static Func<MainTabWindow_Research> createWindow;

        /// <summary>Called by the add-on at startup with the Rethemed window's type and a way to make one.</summary>
        public static void RegisterWindow(Type type, Func<MainTabWindow_Research> create)
        {
            windowType = type;
            createWindow = create;
            Apply();
            // A layout already built for the Classic window is redone with the Rethemed window's projects per column.
            if (RethemedActive && ResearchOrganizedMain.LayoutVersion > 0) ResearchOrganizedMain.OrganizeTabsAndLayout();
        }

        /// <summary>The Rethemed window can be used: UI Rethemed - Core is active and the add-on has not failed.</summary>
        public static bool RethemedAvailable => windowType != null && ResearchSkin.Available;

        /// <summary>The Rethemed window is chosen and available.</summary>
        public static bool RethemedActive => RethemedAvailable && ResearchOrganizedMod.settings?.displayMode == Rethemed;

        /// <summary>The Rethemed window's type while it is in use, for other mods to open; null otherwise.</summary>
        public static Type ActiveWindowType => RethemedActive ? windowType : null;

        /// <summary>A new Rethemed window, or null when it isn't in use.</summary>
        public static MainTabWindow_Research CreateWindow() => RethemedActive ? createWindow?.Invoke() : null;

        /// <summary>Points the Research button at the chosen window, unless Node Research has taken it or Semi Random Research decides.</summary>
        public static void Apply()
        {
            var button = MainButtonDefOf.Research;
            if (button == null || WindowHandover.NodeResearchActive || WindowHandover.SemiRandomActive) return;
            Type wanted = RethemedActive ? windowType : typeof(MainTabWindow_Research);
            bool ours = button.tabWindowClass == typeof(MainTabWindow_Research) || (windowType != null && button.tabWindowClass == windowType);
            if (!ours || button.tabWindowClass == wanted) return;
            MainTabWindow open = tabWindowRef(button);
            if (open != null && Find.WindowStack != null && Find.WindowStack.IsOpen(open)) open.Close(false);
            button.tabWindowClass = wanted;
            tabWindowRef(button) = null;
        }
    }
}
