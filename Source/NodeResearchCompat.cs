using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ResearchOrganized
{
    /// <summary>Node Research (ferny.noderesearch) parks every project its own window draws
    /// onto vanilla's Main tab, remembering where each came from so its "open the vanilla
    /// menu" button can put them back. Both halves of that go wrong when this mod reorganizes
    /// the same projects: its collapse returns early while it believes it still holds, so its
    /// window lists an empty Main tab, and the tabs it would restore are the ones from before
    /// this mod sorted them. Pointing it at the finished layout fixes both, and lets the user
    /// switch between the two windows. Reflective and entirely optional - with Node Research
    /// absent, or its internals renamed, every call here is a no-op.</summary>
    internal static class NodeResearchCompat
    {
        private const string StartupTypeName = "BetterResearchMenu.Startup";

        private static bool resolved;
        private static FieldInfo originalTabsField;
        private static FieldInfo isCollapsedField;

        /// <summary>The tab each project was routed to by the last pass, before Node Research
        /// could move it.</summary>
        private static readonly Dictionary<ResearchProjectDef, ResearchTabDef> organizedTabs = new Dictionary<ResearchProjectDef, ResearchTabDef>();

        public static bool Active
        {
            get { Resolve(); return originalTabsField != null && isCollapsedField != null; }
        }

        /// <summary>Records where this pass routed every project. Called before anything else
        /// can touch the tabs.</summary>
        public static void RecordRouting()
        {
            if (!Active) return;
            organizedTabs.Clear();
            foreach (var project in DefDatabase<ResearchProjectDef>.AllDefs) organizedTabs[project] = project.tab;
        }

        /// <summary>Repoints Node Research's remembered tabs at wherever this pass routed each
        /// project, and clears its collapsed flag so its next window open really re-collapses.</summary>
        public static void SyncTabs()
        {
            if (!Active) return;
            try
            {
                var remembered = (Dictionary<ResearchProjectDef, ResearchTabDef>)originalTabsField.GetValue(null);
                if (remembered != null)
                {
                    foreach (var entry in organizedTabs) remembered[entry.Key] = entry.Value;
                }
                isCollapsedField.SetValue(null, false);
            }
            catch (Exception ex) { Log.Error($"[Research: Organized] Node Research sync error: {ex.Message}"); }
        }

        /// <summary>Postfix on Node Research's restore, run when its "open the vanilla menu"
        /// button is pressed. Puts every project back on the tab this mod chose, whatever
        /// Node Research remembered, and brings the new vanilla window's tabs up to date.</summary>
        public static void AfterRestore()
        {
            try
            {
                foreach (var entry in organizedTabs)
                {
                    if (entry.Value != null) entry.Key.tab = entry.Value;
                }
            }
            catch (Exception ex) { Log.Error($"[Research: Organized] Node Research restore error: {ex.Message}"); }
        }

        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;
            var startupType = AccessTools.TypeByName(StartupTypeName);
            if (startupType == null) return;
            var tabs = AccessTools.Field(startupType, "originalTabs");
            var collapsed = AccessTools.Field(startupType, "isCollapsed");
            if (tabs == null || tabs.FieldType != typeof(Dictionary<ResearchProjectDef, ResearchTabDef>)) return;
            if (collapsed == null || collapsed.FieldType != typeof(bool)) return;

            // Reading its fields would run Node Research's static constructor, which moves every
            // project to Main. Run it now, before this pass routes anything, rather than midway
            // through SyncTabs, where it left all projects on Main.
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(startupType.TypeHandle);
            originalTabsField = tabs;
            isCollapsedField = collapsed;

            var restore = AccessTools.Method(startupType, "Restore");
            if (restore != null)
            {
                new Harmony(ResearchOrganizedMain.HarmonyId).Patch(restore,
                    postfix: new HarmonyMethod(typeof(NodeResearchCompat), nameof(AfterRestore)));
            }
        }
    }
}
