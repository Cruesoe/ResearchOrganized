using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using ResearchOrganized.Layout;
using RimWorld;
using UnityEngine;
using Verse;

namespace ResearchOrganized
{
    public static partial class ResearchOrganizedMain
    {
        private static FieldInfo researchTabRecordDefField;
        private static ConstructorInfo researchTabRecordCtor;
        private static readonly FieldInfo researchWindowCurTabField = AccessTools.Field(typeof(MainTabWindow_Research), "curTabInt");
        private static readonly FieldInfo researchWindowSelectedProjectField = AccessTools.Field(typeof(MainTabWindow_Research), "selectedProject");
        private static readonly MethodInfo researchWindowUpdateSelectedMethod = AccessTools.Method(typeof(MainTabWindow_Research), "UpdateSelectedProject");
        private static FieldInfo researchWindowTabsField;
        private static readonly FieldInfo researchWindowScrollPositionerField = AccessTools.Field(typeof(MainTabWindow_Research), "scrollPositioner");
        private static Game lastResearchWindowGame;

        // Semi Random Research's DrawLeftRect patch class: P-Fork Continued, then older versions.
        private static readonly string[] SemiRandomResearchPatchTypes =
        {
            "CM_Semi_Random_Research.Patch_MainTabWindow_Research_DrawLeftRect",
            "CM_Semi_Random_Research.MainTabWindow_Research_Patches"
        };
        private const float SemiRandomResearchButtonSize = 32f;
        private const float SettingsButtonSize = 24f;
        private const float SettingsButtonGap = 4f;
        private static readonly Color SettingsButtonColor = new Color(0.6f, 0.6f, 0.6f);
        private static readonly Texture2D SettingsGearTex = BuildGearTexture(64);

        /// <summary>
        /// A white eight-toothed gear with a center hole, tinted when drawn. Vanilla has no plain
        /// settings gear to borrow, so it is built here like the foundation notch. Each pixel is
        /// 4x4 supersampled for smooth edges; one tooth points straight up, and teeth taper
        /// slightly towards the tip.
        /// </summary>
        private static Texture2D BuildGearTexture(int size)
        {
            const int teeth = 8;
            const int samples = 4;
            const float toothRadius = 0.47f, bodyRadius = 0.34f, holeRadius = 0.14f, toothDuty = 0.5f, taper = 0.35f;

            bool Inside(float x, float y)
            {
                float dx = x - 0.5f, dy = y - 0.5f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                if (r < holeRadius || r > toothRadius) return false;
                if (r <= bodyRadius) return true;
                // Screen y grows downward here, so angle -90 degrees is straight up; the 0.5
                // offset centers a tooth there.
                float turn = Mathf.Atan2(dy, dx) / (2f * Mathf.PI) * teeth + 0.5f;
                float fraction = turn - Mathf.Floor(turn);
                float halfWidth = toothDuty / 2f * (1f - taper * (r - bodyRadius) / (toothRadius - bodyRadius));
                return Mathf.Abs(fraction - 0.5f) <= halfWidth;
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            for (int row = 0; row < size; row++)
            {
                for (int column = 0; column < size; column++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < samples; sy++)
                        for (int sx = 0; sx < samples; sx++)
                            if (Inside((column + (sx + 0.5f) / samples) / size, (row + (sy + 0.5f) / samples) / size)) hits++;
                    float alpha = hits / (float)(samples * samples);
                    texture.SetPixel(column, size - 1 - row, new Color(1f, 1f, 1f, alpha));
                }
            }
            texture.Apply();
            return texture;
        }
        private static bool semiRandomResearchActive;

        /// <summary>Disarms vanilla's scroll to the current project on every open but the first in each game.</summary>
        public static void KeepScrollOnReopen(MainTabWindow_Research __instance)
        {
            if (lastResearchWindowGame == Current.Game)
                (researchWindowScrollPositionerField.GetValue(__instance) as ScrollPositioner)?.Arm(false);
            lastResearchWindowGame = Current.Game;
        }

        public static void DrawSettingsButton(MainTabWindow_Research __instance, Rect leftOutRect)
        {
            if (researchWindowWidthStale)
            {
                researchWindowWidthStale = false;
                ApplyCurTab(__instance, __instance.CurTab);
            }

            // Normal: a row left of Semi Random Research's button. Full screen: a column below it in the narrow strip.
            bool fullScreen = ResearchOrganizedMod.settings.fullScreenResearch;
            Rect gearRect, toggleRect;
            if (fullScreen)
            {
                float x = leftOutRect.xMax - (FullScreenStripWidth + SettingsButtonSize) / 2f;
                float y = leftOutRect.yMin + (semiRandomResearchActive ? SemiRandomResearchButtonSize + SettingsButtonGap : 0f);
                gearRect = new Rect(x, y, SettingsButtonSize, SettingsButtonSize);
                toggleRect = new Rect(x, gearRect.yMax + SettingsButtonGap, SettingsButtonSize, SettingsButtonSize);
            }
            else
            {
                float right = leftOutRect.xMax;
                if (semiRandomResearchActive) right -= SemiRandomResearchButtonSize + SettingsButtonGap;
                float top = leftOutRect.yMin + (SemiRandomResearchButtonSize - SettingsButtonSize) / 2f;
                gearRect = new Rect(right - SettingsButtonSize, top, SettingsButtonSize, SettingsButtonSize);
                toggleRect = new Rect(gearRect.x - SettingsButtonGap - SettingsButtonSize, top, SettingsButtonSize, SettingsButtonSize);
            }

            if (Widgets.ButtonImage(gearRect, SettingsGearTex, SettingsButtonColor, GenUI.MouseoverColor, true, "ResearchOrganized_SettingsButtonTip".Translate()))
            {
                var mod = LoadedModManager.GetMod<ResearchOrganizedMod>();
                if (mod != null) Find.WindowStack.Add(new Dialog_ModSettings(mod));
                Event.current.Use();
            }

            string toggleTip = (fullScreen ? "ResearchOrganized_ExitFullScreenTip" : "ResearchOrganized_EnterFullScreenTip").Translate();
            if (Widgets.ButtonImage(toggleRect, fullScreen ? ExitFullScreenTex : EnterFullScreenTex, SettingsButtonColor, GenUI.MouseoverColor, true, toggleTip))
            {
                ResearchOrganizedMod.settings.fullScreenResearch = !fullScreen;
                ResearchOrganizedMod.settings.Write();
                Event.current.Use();
            }
        }

        private static readonly Texture2D EnterFullScreenTex = BuildFullScreenTexture(64, false);
        private static readonly Texture2D ExitFullScreenTex = BuildFullScreenTexture(64, true);
        private static readonly MethodInfo windowSetInitialSizeMethod = AccessTools.Method(typeof(Window), "SetInitialSizeAndPosition");
        private static bool fullScreenApplied;

        /// <summary>Width of the left panel in full screen: just wide enough for its buttons.</summary>
        private static float FullScreenStripWidth => semiRandomResearchActive ? SemiRandomResearchButtonSize : SettingsButtonSize;

        /// <summary>
        /// A white full-screen icon, tinted when drawn: four corner brackets pointing outwards to enter,
        /// or inwards to exit. Each pixel is 4x4 supersampled; coordinates are folded into the top-left quadrant.
        /// </summary>
        private static Texture2D BuildFullScreenTexture(int size, bool exit)
        {
            const int samples = 4;
            const float margin = 0.1f, arm = 0.3f, thickness = 0.1f;

            bool Inside(float x, float y)
            {
                float fx = Mathf.Min(x, 1f - x), fy = Mathf.Min(y, 1f - y);
                if (fx < margin || fy < margin || fx > margin + arm || fy > margin + arm) return false;
                // The bracket's corner sits at the outer edge to enter, and at the inner end of its arms to exit.
                float edge = exit ? margin + arm - thickness : margin;
                return (fy >= edge && fy <= edge + thickness) || (fx >= edge && fx <= edge + thickness);
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            for (int row = 0; row < size; row++)
            {
                for (int column = 0; column < size; column++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < samples; sy++)
                        for (int sx = 0; sx < samples; sx++)
                            if (Inside((column + (sx + 0.5f) / samples) / size, (row + (sy + 0.5f) / samples) / size)) hits++;
                    texture.SetPixel(column, size - 1 - row, new Color(1f, 1f, 1f, hits / (float)(samples * samples)));
                }
            }
            texture.Apply();
            return texture;
        }

        /// <summary>DoWindowContents transpiler: routes the left panel's width through <see cref="LeftPanelWidth"/>.</summary>
        public static IEnumerable<CodeInstruction> LeftPanelWidthTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            var max = AccessTools.Method(typeof(Mathf), nameof(Mathf.Max), new[] { typeof(float), typeof(float) });
            int idx = list.FindIndex(i => i.Calls(max));
            if (idx < 0)
            {
                Log.Warning("[Research: Organized] Could not find the research window's left panel width; full screen keeps the panel.");
                return list;
            }
            list[idx].operand = AccessTools.Method(typeof(ResearchOrganizedMain), nameof(LeftPanelWidth));
            return list;
        }

        public static float LeftPanelWidth(float minimum, float share) =>
            ResearchOrganizedMod.settings.fullScreenResearch ? FullScreenStripWidth : Mathf.Max(minimum, share);

        /// <summary>DrawLeftRect prefix: skips the project panel in full screen; postfixes still draw their buttons.</summary>
        public static bool SkipLeftPanelInFullScreen() => !ResearchOrganizedMod.settings.fullScreenResearch;

        /// <summary>InitialSize postfix: full screen takes the whole height above the bottom bar.</summary>
        public static void FullScreenInitialSize(ref Vector2 __result)
        {
            if (ResearchOrganizedMod.settings.fullScreenResearch) __result.y = UI.screenHeight - 35;
        }

        /// <summary>DoWindowContents prefix: resizes the open window when full screen was toggled from the button or settings.</summary>
        public static void ApplyFullScreenSize(MainTabWindow_Research __instance)
        {
            bool fullScreen = ResearchOrganizedMod.settings.fullScreenResearch;
            if (fullScreen == fullScreenApplied) return;
            fullScreenApplied = fullScreen;
            windowSetInitialSizeMethod?.Invoke(__instance, null);
        }

        /// <summary>
        /// Brings the research window, open or not, up to date after a re-layout. The window builds its
        /// tab strip only in PostOpen and caches the current tab's width, so without this a
        /// settings change made from the gear button shows removed tabs and a stale scroll
        /// width until the window is reopened. PostOpen itself is not rerun because it also
        /// starts the window's sounds. Mirrors what PostOpen does to the tab list.
        /// </summary>
        public static void RefreshOpenResearchWindow()
        {
            try
            {
                // The research tab's window is one persistent instance, reused on every open, so
                // fix it up even while closed: its remembered tab may now be hidden, and a hidden
                // tab's stale index would be read past the end of the tab-info flags on reopen.
                if (Current.Game == null) return;
                var window = MainButtonDefOf.Research?.TabWindow as MainTabWindow_Research
                             ?? Find.WindowStack?.WindowOfType<MainTabWindow_Research>();
                if (window == null || researchWindowTabsField == null || researchTabRecordCtor == null || researchWindowCurTabField == null) return;
                if (!(researchWindowTabsField.GetValue(window) is System.Collections.IList tabs)) return;

                BuildTabStrip(window, tabs, null);
                tabStripDirty = true;

                var target = window.CurTab;
                if (target == null || !DefDatabase<ResearchTabDef>.AllDefsListForReading.Contains(target))
                {
                    var selectedProject = researchWindowSelectedProjectField?.GetValue(window) as ResearchProjectDef;
                    target = selectedProject?.tab ?? DefDatabase<ResearchTabDef>.AllDefsListForReading.FirstOrDefault();
                }

                // The setter measures text through Unity's GUI, which crashes the game off the main
                // thread, and a save loads on a background thread. There, only store the tab and
                // let the left panel, drawn before the tree, recompute the width.
                if (UnityData.IsInMainThread) ApplyCurTab(window, target);
                else
                {
                    researchWindowCurTabField.SetValue(window, target);
                    researchWindowWidthStale = true;
                }
            }
            catch (Exception ex) { Log.Error($"[Research: Organized] Research window refresh error: {ex.Message}"); }
        }

        private static bool researchWindowWidthStale;

        /// <summary>Set when the window's visible projects may have changed; the next draw rebuilds the tab strip.</summary>
        public static bool tabStripDirty = true;

        /// <summary>Fills the window's tab strip the way vanilla PostOpen does, keeping only <paramref name="shown"/> tabs when given.</summary>
        private static void BuildTabStrip(MainTabWindow_Research window, System.Collections.IList tabs, HashSet<ResearchTabDef> shown)
        {
            tabs.Clear();
            foreach (var tabDef in DefDatabase<ResearchTabDef>.AllDefs)
            {
                if (shown != null && !shown.Contains(tabDef)) continue;
                var def = tabDef;
                Action clicked = () =>
                {
                    window.CurTab = def;
                    researchWindowUpdateSelectedMethod?.Invoke(window, new object[] { Find.ResearchManager });
                };
                Func<bool> selected = () => window.CurTab == def;
                tabs.Add(researchTabRecordCtor.Invoke(new object[] { def, (string)def.LabelCap, clicked, selected }));
            }
        }

        /// <summary>While a visibility filter is on, drops tabs with no visible project from the strip and moves off a dropped current tab.</summary>
        public static void RefreshTabStripIfDirty(MainTabWindow_Research __instance)
        {
            if (!tabStripDirty || researchTabRecordCtor == null) return;
            if (!(researchWindowTabsField.GetValue(__instance) is System.Collections.IList tabs)) return;
            // Reading the list may rebuild it, which marks the strip dirty again; clear the flag afterwards.
            var visible = __instance.VisibleResearchProjects;
            tabStripDirty = false;

            var settings = ResearchOrganizedMod.settings;
            HashSet<ResearchTabDef> shown = null;
            if (settings.hideUnavailableProjects || settings.hideFutureTechLevels)
            {
                shown = new HashSet<ResearchTabDef>(visible.Select(p => p.tab).Where(t => t != null));
                if (shown.Count == 0) shown = null;
            }
            BuildTabStrip(__instance, tabs, shown);

            if (shown == null || shown.Contains(__instance.CurTab))
            {
                // Vanilla sizes the scroll view only when the tab changes, so projects revealed since would sit past its edge.
                if (__instance.CurTab != null && researchWindowViewSizeMethod != null && researchWindowViewWidthField != null)
                    researchWindowViewWidthField.SetValue(__instance, ((Vector2)researchWindowViewSizeMethod.Invoke(__instance, new object[] { __instance.CurTab })).x);
                return;
            }
            var selectedTab = (researchWindowSelectedProjectField?.GetValue(__instance) as ResearchProjectDef)?.tab;
            var target = selectedTab != null && shown.Contains(selectedTab)
                ? selectedTab
                : DefDatabase<ResearchTabDef>.AllDefsListForReading.First(shown.Contains);
            ApplyCurTab(__instance, target);
        }

        private static readonly MethodInfo researchWindowViewSizeMethod = AccessTools.Method(typeof(MainTabWindow_Research), "ViewSize");
        private static readonly FieldInfo researchWindowViewWidthField = AccessTools.Field(typeof(MainTabWindow_Research), "rightViewWidth");

        public static void MarkTabStripDirty() => tabStripDirty = true;

        /// <summary>Sets the tab through the setter; clearing the backing field first makes it recompute the view width.</summary>
        private static void ApplyCurTab(MainTabWindow_Research window, ResearchTabDef tab)
        {
            researchWindowCurTabField.SetValue(window, null);
            window.CurTab = tab;
        }


        /// <summary>PostOpen postfix: vanilla refills the tab strip with every tab, so it is rebuilt on the next draw, and counts are recomputed.</summary>
        public static void OnResearchWindowOpened()
        {
            fullScreenApplied = ResearchOrganizedMod.settings.fullScreenResearch;
            MarkTabStripDirty();
            InvalidateTabStats();
        }

        /// <summary>Strips a tab tooltip that is nothing but markup, then appends the tab's live project and research point counts.</summary>
        public static void AppendTabCounts(object __instance, ref string __result)
        {
            if (!__result.NullOrEmpty() && __result.StripTags().Trim().Length == 0) __result = "";

            if (researchTabRecordDefField == null) return;
            if (!(researchTabRecordDefField.GetValue(__instance) is ResearchTabDef tabDef)) return;

            var stats = GetTabStats(tabDef);
            string counts = "ResearchOrganized_TabCounts".Translate(stats.completedProjects, stats.totalProjects,
                stats.completedPoints.ToString("F0"), stats.totalPoints.ToString("F0"));
            __result = __result.NullOrEmpty() ? counts : __result + "\n\n" + counts;
        }

        /// <summary>Sets every tab's label, with its completed/total count when that setting is on. Runs every frame the window draws.</summary>
        public static void UpdateTabLabelsWithCounts(object __instance)
        {
            if (!(researchWindowTabsField.GetValue(__instance) is System.Collections.IEnumerable tabRecords)) return;

            bool showCounts = ResearchOrganizedMod.settings.showTabProjectCounts;
            foreach (object tabRecordObj in tabRecords)
            {
                if (!(researchTabRecordDefField.GetValue(tabRecordObj) is ResearchTabDef tabDef)) continue;
                ((TabRecord)tabRecordObj).label = showCounts ? GetTabStats(tabDef).countLabel : tabDef.LabelCap.ToString();
            }
        }

        private sealed class TabStats
        {
            public int completedProjects;
            public int totalProjects;
            public float completedPoints;
            public float totalPoints;
            public string countLabel;
        }

        private static readonly Dictionary<ResearchTabDef, TabStats> tabStats = new Dictionary<ResearchTabDef, TabStats>();
        private static bool tabStatsValid;

        /// <summary>Drops the cached tab counts; they are rebuilt in one pass on next use.</summary>
        public static void InvalidateTabStats() => tabStatsValid = false;

        private static TabStats GetTabStats(ResearchTabDef tabDef)
        {
            if (!tabStatsValid)
            {
                tabStats.Clear();
                foreach (var project in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
                {
                    if (project.tab == null) continue;
                    if (!tabStats.TryGetValue(project.tab, out var entry)) tabStats[project.tab] = entry = new TabStats();
                    entry.totalProjects++;
                    entry.totalPoints += project.Cost;
                    if (project.IsFinished)
                    {
                        entry.completedProjects++;
                        entry.completedPoints += project.Cost;
                    }
                }
                tabStatsValid = true;
            }

            if (!tabStats.TryGetValue(tabDef, out var stats)) tabStats[tabDef] = stats = new TabStats();
            if (stats.countLabel == null) stats.countLabel = $"{tabDef.LabelCap} {stats.completedProjects}/{stats.totalProjects}";
            return stats;
        }
    }
}
