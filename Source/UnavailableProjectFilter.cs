using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ResearchOrganized
{
    /// <summary>
    /// "Hide Unavailable Projects" hides unfinished projects that cannot be started now; "Hide Future Tech Levels"
    /// hides unfinished projects above the colony's tech level. The current project always shows, and positions are unchanged.
    /// </summary>
    public static class UnavailableProjectFilter
    {
        private static readonly FieldInfo visibleCacheField = AccessTools.Field(typeof(MainTabWindow_Research), "cachedVisibleResearchProjects");

        public static void Patch(Harmony harmony)
        {
            var getter = AccessTools.PropertyGetter(typeof(MainTabWindow_Research), nameof(MainTabWindow_Research.VisibleResearchProjects));
            if (getter == null || visibleCacheField == null)
            {
                Log.Warning("[Research: Organized] Research window visibility list not found; Hide Unavailable Projects is inactive.");
                return;
            }
            harmony.Patch(getter,
                prefix: new HarmonyMethod(typeof(UnavailableProjectFilter), nameof(NoteRebuild)),
                postfix: new HarmonyMethod(typeof(UnavailableProjectFilter), nameof(FilterOnRebuild)));

            // Vanilla rebuilds the list only when the window opens; these change availability while it may be open.
            var postfix = new HarmonyMethod(typeof(UnavailableProjectFilter), nameof(Invalidate));
            foreach (var name in new[] { nameof(ResearchManager.FinishProject), nameof(ResearchManager.AddTechprints), nameof(ResearchManager.ResetAllProgress) })
            {
                var method = AccessTools.Method(typeof(ResearchManager), name);
                if (method != null) harmony.Patch(method, postfix: postfix);
            }
        }

        public static void NoteRebuild(MainTabWindow_Research __instance, out bool __state)
        {
            __state = visibleCacheField.GetValue(__instance) == null;
        }

        /// <summary>Filters the freshly built cache in place, so the check runs once per rebuild rather than every frame.</summary>
        public static void FilterOnRebuild(List<ResearchProjectDef> __result, bool __state)
        {
            if (__state) ResearchOrganizedMain.MarkTabStripDirty();
            var settings = ResearchOrganizedMod.settings;
            if (!__state || __result == null || !(settings.hideUnavailableProjects || settings.hideFutureTechLevels)) return;
            if (Current.ProgramState != ProgramState.Playing || Find.ResearchManager == null) return;
            // Node Research raises the player faction's tech level as eras emerge; otherwise it is the starting faction's.
            var colonyLevel = Faction.OfPlayer?.def.techLevel ?? TechLevel.Undefined;
            __result.RemoveAll(p =>
            {
                if (p.IsFinished || Find.ResearchManager.IsCurrentProject(p)) return false;
                if (settings.hideFutureTechLevels && colonyLevel != TechLevel.Undefined && p.techLevel > colonyLevel) return true;
                return settings.hideUnavailableProjects && !p.CanStartNow;
            });
        }

        /// <summary>Drops the research window's visible-project cache so it is rebuilt on its next draw.</summary>
        public static void Invalidate()
        {
            if (visibleCacheField == null || Current.ProgramState != ProgramState.Playing) return;
            if (MainButtonDefOf.Research?.TabWindow is MainTabWindow_Research window)
                visibleCacheField.SetValue(window, null);
        }
    }
}
