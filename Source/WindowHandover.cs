using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace ResearchOrganized
{
    /// <summary>Moves between this mod's research window and Node Research, Semi Random Research - Continued and
    /// Research Queue. Their own windows reach the research tree by opening a plain vanilla research window, which is
    /// swapped for this one; this window's title bar has a button back to each. Every lookup is resolved once and is
    /// a no-op when the mod is absent or its internals have changed.</summary>
    internal static class WindowHandover
    {
        private const string NodeResearchPackageId = "ferny.noderesearch";
        private const string SemiRandomPackageId = "cruesoe.semirandompfork.continued";
        private const string ResearchQueuePackageId = "cruesoe.researchqueue";

        public static readonly bool NodeResearchActive = ModsConfig.IsActive(NodeResearchPackageId);
        public static readonly bool SemiRandomActive = ModsConfig.IsActive(SemiRandomPackageId);
        public static readonly bool ResearchQueueActive = ModsConfig.IsActive(ResearchQueuePackageId);

        private static readonly AccessTools.FieldRef<MainButtonDef, MainTabWindow> tabWindowRef =
            AccessTools.FieldRefAccess<MainButtonDef, MainTabWindow>("tabWindowInt");

        private static bool resolved;
        private static Type nodeWindowType;
        private static Action<Window> openSemiRandom;
        private static Action<Window> openQueue;

        /// <summary>One window of each kind for the session, reused so each keeps its view.</summary>
        private static MainTabWindow nodeWindow;
        private static MainTabWindow_Research standaloneWindow;

        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;
            if (NodeResearchActive)
                nodeWindowType = AccessTools.TypeByName("BetterResearchMenu.MainTabWindow_BetterResearch");
            if (SemiRandomActive)
                openSemiRandom = Delegate(AccessTools.Method("CM_Semi_Random_Research.ResearchTabWindowSwitcher:SwitchToSemiRandomResearch", new[] { typeof(Window) }));
            if (ResearchQueueActive)
                openQueue = Delegate(AccessTools.Method("ResearchQueue.UI.ResearchScreens:OpenQueue", new[] { typeof(Window) }));
        }

        private static Action<Window> Delegate(System.Reflection.MethodInfo method)
        {
            if (method == null || !method.IsStatic) return null;
            try
            {
                return (Action<Window>)System.Delegate.CreateDelegate(typeof(Action<Window>), method);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static System.Reflection.FieldInfo semiRandomSettingsField;
        private static System.Reflection.FieldInfo semiRandomRestrictField;
        private static bool semiRandomResolved;
        private static int restrictsFrame = -1;
        private static bool restricts;

        /// <summary>Semi Random Research's "Prohibit normal project selection" setting, read once a frame; false without the mod.</summary>
        public static bool SemiRandomRestricts
        {
            get
            {
                if (!SemiRandomActive) return false;
                if (restrictsFrame == Time.frameCount) return restricts;
                restrictsFrame = Time.frameCount;
                if (!semiRandomResolved)
                {
                    semiRandomResolved = true;
                    var modType = AccessTools.TypeByName("CM_Semi_Random_Research.SemiRandomResearchMod");
                    if (modType != null) semiRandomSettingsField = AccessTools.Field(modType, "settings");
                    if (semiRandomSettingsField != null) semiRandomRestrictField = AccessTools.Field(semiRandomSettingsField.FieldType, "featureEnabled");
                }
                // Unreadable settings count as restricting, the mod's own default.
                object settings = semiRandomSettingsField?.GetValue(null);
                restricts = settings == null || semiRandomRestrictField == null || (bool)semiRandomRestrictField.GetValue(settings);
                return restricts;
            }
        }

        public static bool CanOpenNodeResearch { get { Resolve(); return nodeWindowType != null; } }
        public static bool CanOpenSemiRandom { get { Resolve(); return openSemiRandom != null; } }
        public static bool CanOpenQueue { get { Resolve(); return openQueue != null; } }

        public static void Patch(Harmony harmony)
        {
            var add = AccessTools.Method(typeof(WindowStack), nameof(WindowStack.Add));
            if (add != null) harmony.Patch(add, prefix: new HarmonyMethod(typeof(WindowHandover), nameof(SwapVanillaWindow)));
        }

        /// <summary>With the Rethemed window in use, a plain vanilla research window opened on its own (Node Research's vanilla
        /// button, Research Queue's tree button) becomes the Rethemed window. The Research button's own window is left to whichever mod set it.</summary>
        private static bool SwapVanillaWindow(ref Window window)
        {
            if (window == null || window.GetType() != typeof(MainTabWindow_Research) || !DisplayMode.RethemedActive) return true;
            var button = MainButtonDefOf.Research;
            if (button == null || tabWindowRef(button) == window) return true;
            if (standaloneWindow == null || standaloneWindow.GetType() != DisplayMode.ActiveWindowType) standaloneWindow = DisplayMode.CreateWindow();
            if (standaloneWindow == null) return true;
            standaloneWindow.def = button;
            if (Find.WindowStack.IsOpen(standaloneWindow)) return false;
            window = standaloneWindow;
            return true;
        }

        public static void OpenNodeResearch(Window current)
        {
            Resolve();
            if (nodeWindowType == null) return;
            var button = MainButtonDefOf.Research;
            current?.Close(false);
            if (button.tabWindowClass == nodeWindowType)
            {
                Find.MainTabsRoot.SetCurrentTab(button, false);
            }
            else
            {
                nodeWindow = nodeWindow ?? Activator.CreateInstance(nodeWindowType) as MainTabWindow;
                if (nodeWindow == null) return;
                nodeWindow.def = button;
                if (!Find.WindowStack.IsOpen(nodeWindow)) Find.WindowStack.Add(nodeWindow);
            }
            SoundDefOf.TabOpen.PlayOneShotOnCamera();
        }

        public static void OpenSemiRandom(Window current)
        {
            Resolve();
            openSemiRandom?.Invoke(current);
        }

        public static void OpenQueue(Window current)
        {
            Resolve();
            openQueue?.Invoke(current);
        }
    }
}
