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
    [StaticConstructorOnStartup]
    public static class ResearchOrganizedMain
    {
        private const string MultiAnalyzerDef = "MultiAnalyzer";
        private const string HiTechBenchDef = "HiTechResearchBench";
        private const string TabAnomalyDef = "Anomaly";
        private const string MainTabDef = "Main";
        private const string TabGravtechDef = "VGE_Gravtech";
        private const string GravtechExtensionName = "GravtechResearchExtension";

        private const float NormalBrightness = 0.5f;
        private const float UnavailableBrightness = 0.2f;

        /// <summary>Marks Node Research's foundation technologies with a small triangle in the
        /// card's top-right corner. A border colour was tried first, but vanilla already borders
        /// the selected project and colours the active one in similar gold, so a shape is used
        /// instead and the border is left to vanilla. Top-right because Anomaly cards put their
        /// knowledge icon on the left.</summary>
        private static readonly Color FoundationGold = new Color(0.75f, 0.62f, 0.32f, 0.9f);
        private const float FoundationNotchSize = 12f;
        private const int FoundationNotchTextureSize = 32;
        private const string FoundationTechTooltip = "Foundation technology";
        private static readonly Texture2D FoundationNotchTex = BuildCornerNotchTexture(FoundationNotchTextureSize);

        private static readonly Dictionary<TechLevel, ColorSet> TabColors = new Dictionary<TechLevel, ColorSet>();
        private static readonly Dictionary<string, ColorSet> TabColorOverrides = new Dictionary<string, ColorSet>();
        private static readonly Dictionary<string, TechLevel> TabToThemeMap = new Dictionary<string, TechLevel>();

        public static List<string> IgnoredTabs = new List<string>();
        public static List<string> PreservedTabs = new List<string>();
        public static List<string> GlobalTabOrder = new List<string>();
        public static Dictionary<ResearchProjectDef, List<ResearchProjectDef>> VirtualPrereqsCache = new Dictionary<ResearchProjectDef, List<ResearchProjectDef>>();
        public static Dictionary<string, LayoutConfig> TabLayouts = new Dictionary<string, LayoutConfig>();
        public static Dictionary<TechLevel, string> TechLevelTabOverrides = new Dictionary<TechLevel, string>();

        private static readonly List<ResearchTabDef> hiddenTabs = new List<ResearchTabDef>();

        /// <summary>The tab each project was on when this mod first saw it. Kept across passes.</summary>
        private static readonly Dictionary<ResearchProjectDef, ResearchTabDef> authoredTabs = new Dictionary<ResearchProjectDef, ResearchTabDef>();

        /// <summary>The tech level each project had when this mod first saw it. Kept across passes.</summary>
        private static readonly Dictionary<ResearchProjectDef, TechLevel> authoredTechLevels = new Dictionary<ResearchProjectDef, TechLevel>();
        private static string lastTechLevelReport;

        /// <summary>The combined tab the last layout pass built, read every frame by the line filter.</summary>
        private static ResearchTabDef activeCombinedTab;

        private static FieldInfo researchTabRecordDefField;
        private static ConstructorInfo researchTabRecordCtor;
        private static readonly FieldInfo researchWindowCurTabField = AccessTools.Field(typeof(MainTabWindow_Research), "curTabInt");
        private static readonly FieldInfo researchWindowSelectedProjectField = AccessTools.Field(typeof(MainTabWindow_Research), "selectedProject");
        private static readonly MethodInfo researchWindowUpdateSelectedMethod = AccessTools.Method(typeof(MainTabWindow_Research), "UpdateSelectedProject");
        private static FieldInfo researchWindowTabsField;

        private static readonly Dictionary<ResearchProjectDef, bool> reqMultiCache = new Dictionary<ResearchProjectDef, bool>();
        private static readonly Dictionary<ResearchProjectDef, bool> reqHiTechCache = new Dictionary<ResearchProjectDef, bool>();

        public static float GlobalXStep = 1f;
        public static float GlobalYStep = 0.63f;
        public static int GlobalMaxNodesPerColumn = 12;

        private struct ColorSet
        {
            public Color unavailable;
            public Color normal;
            public Color finished;
        }

        static ResearchOrganizedMain()
        {
            RefreshColors();
            InitTabThemes();

            var harmony = new Harmony("research.organized.organizer");

            // Patch ListProjects safely
            var listProjectsMethod = AccessTools.Method(typeof(MainTabWindow_Research), "ListProjects");
            if (listProjectsMethod != null)
            {
                harmony.Patch(listProjectsMethod, transpiler: new HarmonyMethod(typeof(ResearchOrganizedMain), nameof(ColorTranspiler)));
                harmony.Patch(listProjectsMethod, transpiler: new HarmonyMethod(typeof(ResearchOrganizedMain), nameof(CrossEraLineTranspiler)));
            }

            // Vanilla 1.6 already skips lines to prerequisites on other tabs. The second
            // transpiler above also skips lines between eras on the combined tab and lines to
            // emergence nodes; see PrerequisiteLineTab.

            // Some mods create their own ResearchProjectDefs from their own static
            // constructor - Node Research's per-era "advance to the next tech level" nodes,
            // for one - and static constructor order between mods is not guaranteed, so a
            // pass run here can run before those defs exist. Game.FinalizeInit fires once an
            // actual game has started, which is guaranteed to be after every mod's static
            // constructor has finished, so redoing the pass there catches anything created
            // late instead of leaving it wherever its creator left it.
            var finalizeInitMethod = AccessTools.Method(typeof(Game), nameof(Game.FinalizeInit));
            if (finalizeInitMethod != null)
            {
                harmony.Patch(finalizeInitMethod, postfix: new HarmonyMethod(typeof(ResearchOrganizedMain), nameof(OnGameFinalizeInit)));
            }

            // A research tab with neither a generalTitle nor a generalDescription - every tab
            // this mod adds, and vanilla's Main tab - still gets a tooltip, because vanilla's
            // ResearchTabRecord.GetTip colorizes the missing title and so hands TabDrawer
            // "<color=#FFFFFFFF></color>", which passes its NullOrEmpty check. The result is an
            // empty black box trailing the cursor along the tab strip. Blank the tip when
            // nothing but markup survives, which is the one case TabDrawer does skip.
            var researchTabRecordType = AccessTools.Inner(typeof(MainTabWindow_Research), "ResearchTabRecord");
            var tabTipMethod = researchTabRecordType != null ? AccessTools.Method(researchTabRecordType, "GetTip") : null;
            if (tabTipMethod != null)
            {
                harmony.Patch(tabTipMethod, postfix: new HarmonyMethod(typeof(ResearchOrganizedMain), nameof(BlankEmptyTabTip)));
            }
            if (researchTabRecordType != null)
            {
                researchTabRecordDefField = AccessTools.Field(researchTabRecordType, "def");
                researchTabRecordCtor = AccessTools.Constructor(researchTabRecordType,
                    new[] { typeof(ResearchTabDef), typeof(string), typeof(Action), typeof(Func<bool>) });
            }

            // Optional per-tab completed/total project counts (Settings). The tab strip is
            // rebuilt from ResearchTabDef.LabelCap once in PostOpen and never touched again, so
            // the counts are kept live by rewriting each TabRecord's label field every frame
            // instead - the private tabs list is reached by reflection since ResearchTabRecord
            // itself is a private nested type.
            researchWindowTabsField = AccessTools.Field(typeof(MainTabWindow_Research), "tabs");
            var doWindowContentsMethod = AccessTools.Method(typeof(MainTabWindow_Research), "DoWindowContents");
            if (doWindowContentsMethod != null && researchWindowTabsField != null && researchTabRecordDefField != null)
            {
                harmony.Patch(doWindowContentsMethod, prefix: new HarmonyMethod(typeof(ResearchOrganizedMain), nameof(UpdateTabLabelsWithCounts)));
            }

            // A small gear in the corner of the research window's left panel that opens this
            // mod's settings. Semi Random Research draws its own button in that same corner
            // from the same method, so ours sits just left of it when that mod is loaded.
            var drawLeftRectMethod = AccessTools.Method(typeof(MainTabWindow_Research), "DrawLeftRect");
            if (drawLeftRectMethod != null)
            {
                harmony.Patch(drawLeftRectMethod, postfix: new HarmonyMethod(typeof(ResearchOrganizedMain), nameof(DrawSettingsButton)));
            }
            semiRandomResearchActive = AccessTools.TypeByName(SemiRandomResearchPatchType) != null;

            OrganizeTabsAndLayout();
        }

        private const string SemiRandomResearchPatchType = "CM_Semi_Random_Research.MainTabWindow_Research_Patches";
        private const float SemiRandomResearchButtonSize = 32f;
        private const float SettingsButtonSize = 24f;
        private const float SettingsButtonGap = 4f;
        private static readonly Color SettingsButtonColor = new Color(0.6f, 0.6f, 0.6f);
        private static readonly Texture2D SettingsGearTex = BuildGearTexture(64);

        /// <summary>
        /// A white eight-toothed gear with a centre hole, tinted when drawn. Vanilla has no plain
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
                // offset centres a tooth there.
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

        public static void DrawSettingsButton(MainTabWindow_Research __instance, Rect leftOutRect)
        {
            if (researchWindowWidthStale)
            {
                researchWindowWidthStale = false;
                ApplyCurTab(__instance, __instance.CurTab);
            }

            float right = leftOutRect.xMax;
            if (semiRandomResearchActive) right -= SemiRandomResearchButtonSize + SettingsButtonGap;
            float top = leftOutRect.yMin + (SemiRandomResearchButtonSize - SettingsButtonSize) / 2f;
            var rect = new Rect(right - SettingsButtonSize, top, SettingsButtonSize, SettingsButtonSize);

            if (Widgets.ButtonImage(rect, SettingsGearTex, SettingsButtonColor, GenUI.MouseoverColor, true, "Research: Organized settings"))
            {
                var mod = LoadedModManager.GetMod<ResearchOrganizedMod>();
                if (mod != null) Find.WindowStack.Add(new Dialog_ModSettings(mod));
                Event.current.Use();
            }
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

                tabs.Clear();
                foreach (var tabDef in DefDatabase<ResearchTabDef>.AllDefs)
                {
                    var def = tabDef;
                    Action clicked = () =>
                    {
                        window.CurTab = def;
                        researchWindowUpdateSelectedMethod?.Invoke(window, new object[] { Find.ResearchManager });
                    };
                    Func<bool> selected = () => window.CurTab == def;
                    tabs.Add(researchTabRecordCtor.Invoke(new object[] { def, (string)def.LabelCap, clicked, selected }));
                }

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

        /// <summary>Sets the tab through the setter; clearing the backing field first makes it recompute the view width.</summary>
        private static void ApplyCurTab(MainTabWindow_Research window, ResearchTabDef tab)
        {
            researchWindowCurTabField.SetValue(window, null);
            window.CurTab = tab;
        }

        private static void OnGameFinalizeInit()
        {
            OrganizeTabsAndLayout();
        }

        /// <summary>Turns a tab tooltip that renders as nothing but markup into an empty string,
        /// which is what TabDrawer checks before deciding to show a tooltip at all. Always appends
        /// live completed/total project and research point counts, independent of the "Show Tab
        /// Project Counts" setting that only controls the tab header text - even onto a tab that
        /// would otherwise have gone tipless, which is why this runs after the blanking rather
        /// than being skipped by it.</summary>
        public static void BlankEmptyTabTip(object __instance, ref string __result)
        {
            if (!__result.NullOrEmpty() && __result.StripTags().Trim().Length == 0) __result = "";

            if (researchTabRecordDefField == null) return;
            if (!(researchTabRecordDefField.GetValue(__instance) is ResearchTabDef tabDef)) return;

            string counts = BuildTabCountsTooltip(tabDef);
            __result = __result.NullOrEmpty() ? counts : __result + "\n\n" + counts;
        }

        /// <summary>Rewrites every research tab's label to include its completed/total project
        /// count, live, whenever the setting is on. Runs every frame the research window draws
        /// since the game rebuilds nothing about the tab strip after it is first opened.</summary>
        public static void UpdateTabLabelsWithCounts(object __instance)
        {
            if (!(researchWindowTabsField.GetValue(__instance) is System.Collections.IEnumerable tabRecords)) return;

            foreach (object tabRecordObj in tabRecords)
            {
                if (!(researchTabRecordDefField.GetValue(tabRecordObj) is ResearchTabDef tabDef)) continue;
                var tabRecord = (TabRecord)tabRecordObj;
                tabRecord.label = ResearchOrganizedMod.settings.showTabProjectCounts
                    ? $"{tabDef.LabelCap} {BuildTabProjectCountLabel(tabDef)}"
                    : tabDef.LabelCap;
            }
        }

        private static void GetTabStats(ResearchTabDef tabDef, out int completedProjects, out int totalProjects, out float completedPoints, out float totalPoints)
        {
            completedProjects = 0;
            totalProjects = 0;
            completedPoints = 0f;
            totalPoints = 0f;
            foreach (var project in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
            {
                if (project.tab != tabDef) continue;
                totalProjects++;
                totalPoints += project.Cost;
                if (project.IsFinished)
                {
                    completedProjects++;
                    completedPoints += project.Cost;
                }
            }
        }

        private static string BuildTabProjectCountLabel(ResearchTabDef tabDef)
        {
            GetTabStats(tabDef, out int completed, out int total, out _, out _);
            return $"{completed}/{total}";
        }

        private static string BuildTabCountsTooltip(ResearchTabDef tabDef)
        {
            GetTabStats(tabDef, out int completedProjects, out int totalProjects, out float completedPoints, out float totalPoints);
            return $"Projects: {completedProjects}/{totalProjects}\nResearch points: {completedPoints:F0}/{totalPoints:F0}";
        }

        public static void RefreshColors()
        {
            TabColors[TechLevel.Undefined] = GenerateColorSet(ResearchOrganizedMod.settings.colorUndefined);
            TabColors[TechLevel.Animal] = GenerateColorSet(ResearchOrganizedMod.settings.colorAnimal);
            TabColors[TechLevel.Neolithic] = GenerateColorSet(ResearchOrganizedMod.settings.colorNeolithic);
            TabColors[TechLevel.Medieval] = GenerateColorSet(ResearchOrganizedMod.settings.colorMedieval);
            TabColors[TechLevel.Industrial] = GenerateColorSet(ResearchOrganizedMod.settings.colorIndustrial);
            TabColors[TechLevel.Spacer] = GenerateColorSet(ResearchOrganizedMod.settings.colorSpacer);
            TabColors[TechLevel.Ultra] = GenerateColorSet(ResearchOrganizedMod.settings.colorUltra);
            TabColors[TechLevel.Archotech] = GenerateColorSet(ResearchOrganizedMod.settings.colorArchotech);
            TabColorOverrides[TabAnomalyDef] = GenerateColorSet(ResearchOrganizedMod.settings.colorAnomaly);
        }

        private static ColorSet GenerateColorSet(Color baseColor)
        {
            return new ColorSet
            {
                finished = baseColor,
                normal = new Color(baseColor.r * NormalBrightness, baseColor.g * NormalBrightness, baseColor.b * NormalBrightness, 1f),
                unavailable = new Color(baseColor.r * UnavailableBrightness, baseColor.g * UnavailableBrightness, baseColor.b * UnavailableBrightness, 1f)
            };
        }

        private static void InitTabThemes()
        {
            TabToThemeMap["TabAnimal"] = TechLevel.Animal;
            TabToThemeMap["TabNeolithic"] = TechLevel.Neolithic;
            TabToThemeMap["TabMedieval"] = TechLevel.Medieval;
            TabToThemeMap["TabIndustrial"] = TechLevel.Industrial;
            TabToThemeMap["TabHighIndustrial"] = TechLevel.Industrial;
            TabToThemeMap["TabLateIndustrial"] = TechLevel.Industrial;
            TabToThemeMap["TabSpacer"] = TechLevel.Spacer;
            TabToThemeMap["TabUltra"] = TechLevel.Ultra;
            TabToThemeMap["TabArchotech"] = TechLevel.Archotech;

            // Vanilla Gravship Expanded's gravtech tab is left where the tab pass found it, and
            // the projects on it span tech levels - Odyssey's OrbitalTech is still Industrial - so
            // colouring them per project draws that one tab in mixed colours. Gravtech is
            // spacer-era content, so pin the whole tab to spacer. The defName covers the tab as it
            // ships; the sweep below covers a rename, since that extension is what VGE itself uses
            // to recognise a gravtech tab. A tabThemes entry in a config def still wins over both,
            // because LoadConfigs runs after this.
            foreach (var tab in DefDatabase<ResearchTabDef>.AllDefs)
            {
                if (IsGravtechTab(tab)) TabToThemeMap[tab.defName] = TechLevel.Spacer;
            }
        }

        /// <summary>Vanilla Gravship Expanded's gravtech tab, by defName or by the extension VGE
        /// itself uses to recognise one.</summary>
        private static bool IsGravtechTab(ResearchTabDef tab)
        {
            if (tab == null) return false;
            if (tab.defName == TabGravtechDef) return true;
            return tab.modExtensions != null && tab.modExtensions.Any(e => e.GetType().Name == GravtechExtensionName);
        }

        /// <summary>The single tab every project lands on under "Combine All Tabs", or null when
        /// the option is off.</summary>
        private static ResearchTabDef CombinedTab =>
            ResearchOrganizedMod.settings.combineAllTabs ? DefDatabase<ResearchTabDef>.GetNamed(MainTabDef, false) : null;

        public static void OrganizeTabsAndLayout()
        {
            try
            {
                ResetCaches();
                var tabInfoVisibility = SnapshotTabInfoVisibility();
                RestoreHiddenTabs();
                LoadConfigs();
                RaiseTechLevelsToPrerequisites();
                MapProjectsToTabs();
                var activeTabs = new HashSet<ResearchTabDef>(DefDatabase<ResearchProjectDef>.AllDefs.Select(p => p.tab).Where(t => t != null));
                HideEmptyTabs(activeTabs);
                SortAndIndexTabs();
                RebuildTabInfoVisibility(tabInfoVisibility);

                var combinedTab = CombinedTab;
                activeCombinedTab = combinedTab;
                Dictionary<ResearchProjectDef, int> anchorOrder;
                var anchors = FindAnchors(combinedTab, out anchorOrder);

                foreach (var tab in activeTabs)
                {
                    if (IgnoredTabs.Contains(tab.defName)) continue;
                    var projects = DefDatabase<ResearchProjectDef>.AllDefs.Where(p => p.tab == tab).ToList();
                    if (projects.Count == 0) continue;

                    // Contain failures to the tab that caused them. One bad tab used to
                    // abort this loop, leaving every remaining tab at its authored layout.
                    try
                    {
                        ResearchOrganizedLayout.ApplyLayout(projects, tab.defName, anchors, anchorOrder,
                            eraBuckets: tab == combinedTab);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[Research: Organized] Layout failed for tab '{tab.defName}', " +
                                  $"leaving it at its original coordinates. Other tabs are unaffected. {ex}");
                    }
                }

                // REQUIRED, and not for the reason its name suggests. The research window
                // renders from ResearchProjectDef.ResearchViewX/Y, which are properties over
                // private fields x/y - and this method is the only thing that copies
                // researchViewX/researchViewY into them. Without this call every coordinate
                // written above is ignored and the tree draws at its authored XML positions.
                //
                // Its actual de-overlap pass is a no-op for us: it only nudges projects on
                // the same tab that are within 0.5 in x AND 0.25 in y, while this layout
                // keeps real columns at least xStep (1.0) apart and rows at least yStep
                // (0.63) apart.
                ResearchProjectDef.GenerateNonOverlappingCoordinates();

                // Everything above has just moved projects off the tab Node Research parks
                // them on, so tell it where they went. Without this its own collapse is a
                // no-op - it still believes it holds - and its window comes up empty.
                NodeResearchCompat.SyncTabs();

                // Tabs may have been hidden, restored or re-indexed; the research window keeps a
                // tab strip and current tab of its own that must follow. No-op outside a game.
                RefreshOpenResearchWindow();
            }
            catch (Exception ex) { Log.Error($"[Research: Organized] Master Organizer Error: {ex}"); }
        }

        private static readonly FieldInfo tabInfoVisibilityField = AccessTools.Field(typeof(ResearchManager), "tabInfoVisibility");
        private static readonly FieldInfo defMapValuesField = AccessTools.Field(typeof(DefMap<ResearchTabDef, bool>), "values");

        /// <summary>
        /// The running game's per-tab "info visible" flags, keyed by tab rather than by index.
        ///
        /// ResearchManager keeps them in a DefMap: a list read by ResearchTabDef.index and sized
        /// to the tab count when the game was created or loaded. Hiding, restoring and
        /// re-indexing tabs mid-game changes both, so without this a setting that brings tabs
        /// back (turning "Combine All Tabs" off) reads past the end of that list and the
        /// research window throws on every frame. Null when no game is running or the map has
        /// not been created yet.
        /// </summary>
        private static Dictionary<ResearchTabDef, bool> SnapshotTabInfoVisibility()
        {
            if (!(GetTabInfoValues() is List<bool> values)) return null;
            var snapshot = new Dictionary<ResearchTabDef, bool>();
            foreach (var tab in DefDatabase<ResearchTabDef>.AllDefsListForReading)
            {
                if (tab.index < values.Count) snapshot[tab] = values[tab.index];
            }
            return snapshot;
        }

        /// <summary>Resizes the running game's per-tab flags to the tabs now in the database
        /// and puts each tab's flag at its new index. A tab not seen before gets its default.</summary>
        private static void RebuildTabInfoVisibility(Dictionary<ResearchTabDef, bool> snapshot)
        {
            if (snapshot == null || !(GetTabInfoValues() is List<bool> values)) return;
            var tabs = DefDatabase<ResearchTabDef>.AllDefsListForReading;
            values.Clear();
            for (int i = 0; i < tabs.Count; i++) values.Add(false);
            foreach (var tab in tabs)
            {
                if (tab.index >= values.Count) continue;
                values[tab.index] = snapshot.TryGetValue(tab, out bool visible) ? visible : tab.visibleByDefault;
            }
        }

        private static List<bool> GetTabInfoValues()
        {
            var manager = Current.Game?.researchManager;
            if (manager == null || tabInfoVisibilityField == null || defMapValuesField == null) return null;
            // Created lazily by TabInfoVisible, already at the right size when it is.
            var map = tabInfoVisibilityField.GetValue(manager);
            return map == null ? null : defMapValuesField.GetValue(map) as List<bool>;
        }

        private static void ResetCaches()
        {
            IgnoredTabs.Clear();
            PreservedTabs.Clear();
            GlobalTabOrder.Clear();
            TabLayouts.Clear();
            TechLevelTabOverrides.Clear();
            reqMultiCache.Clear();
            reqHiTechCache.Clear();
            VirtualPrereqsCache.Clear();
            ResearchOrganizedLayout.ClearCaches();
        }

        private static void LoadConfigs()
        {
            foreach (var config in DefDatabase<ResearchOrganizedConfig>.AllDefs)
            {
                if (config.ignoredTabs != null) IgnoredTabs.AddRange(config.ignoredTabs);
                if (config.preservedTabs != null) PreservedTabs.AddRange(config.preservedTabs);
                if (config.tabOrder != null) GlobalTabOrder.AddRange(config.tabOrder);
                if (config.tabThemes != null) foreach (var entry in config.tabThemes) if (!string.IsNullOrEmpty(entry.tabName)) TabToThemeMap[entry.tabName] = entry.techLevel;
                if (config.techLevelTabs != null)
                    foreach (var entry in config.techLevelTabs)
                        if (!string.IsNullOrEmpty(entry.tabName)) TechLevelTabOverrides[entry.techLevel] = entry.tabName;
                if (config.targetTabs != null && config.targetTabs.Count > 0)
                {
                    foreach (var tabName in config.targetTabs)
                    {
                        if (!TabLayouts.ContainsKey(tabName)) TabLayouts[tabName] = new LayoutConfig();
                        if (config.xStep > 0) TabLayouts[tabName].xStep = config.xStep;
                        if (config.yStep > 0) TabLayouts[tabName].yStep = config.yStep;
                        if (config.maxNodesPerColumn > 0) TabLayouts[tabName].maxNodesPerColumn = config.maxNodesPerColumn;
                    }
                }
                else
                {
                    if (config.xStep > 0) GlobalXStep = config.xStep;
                    if (config.yStep > 0) GlobalYStep = config.yStep;
                    if (config.maxNodesPerColumn > 0) GlobalMaxNodesPerColumn = config.maxNodesPerColumn;
                }
                ProcessLinks(config.virtualLinks, true);
                ProcessLinks(config.visibleLinks, false);
            }

            // When a configured override replaces an ignored built-in tab, let the empty
            // built-in tab be removed rather than retaining two tabs for the same era.
            foreach (var pair in TechLevelTabOverrides)
                IgnoredTabs.Remove("Tab" + pair.Key);
        }

        private static void ProcessLinks(IEnumerable<ResearchLinkBase> links, bool isVirtual)
        {
            if (links == null) return;
            foreach (var link in links)
            {
                var parent = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(link.parent);
                if (parent == null) continue;
                var children = new List<string>();
                if (!string.IsNullOrEmpty(link.child)) children.Add(link.child);
                if (link.children != null) children.AddRange(link.children);
                foreach (var childName in children)
                {
                    var child = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(childName);
                    if (child == null) continue;
                    if (isVirtual)
                    {
                        if (!VirtualPrereqsCache.ContainsKey(child)) VirtualPrereqsCache[child] = new List<ResearchProjectDef>();
                        if (!VirtualPrereqsCache[child].Contains(parent)) VirtualPrereqsCache[child].Add(parent);
                    }
                    else
                    {
                        if (child.prerequisites == null) child.prerequisites = new List<ResearchProjectDef>();
                        if (!child.prerequisites.Contains(parent)) child.prerequisites.Add(parent);
                    }
                }
            }
        }

        /// <summary>
        /// Finds the hubs each tab should be built around: a project with enough same-tab
        /// follow-ups is a hub, and once it is, its own children stop counting toward whether
        /// something upstream of it also qualifies - so a chain of hubs is not inflated by
        /// double-counting the same descendants.
        ///
        /// Evaluated bottom-up (deepest projects first) so that "already an anchor" is known
        /// for every child before its ancestors are checked. This is what the original mod
        /// used to isolate a big hub like Electricity onto its own column instead of burying
        /// it among a hundred other projects at the same depth.
        /// </summary>
        /// <param name="combinedTab">The "Combine All Tabs" tab, if any. Its era blocks are laid
        /// out as separate tabs would be, so only a follow-up in the same era counts there.</param>
        private static HashSet<ResearchProjectDef> FindAnchors(ResearchTabDef combinedTab, out Dictionary<ResearchProjectDef, int> anchorOrder)
        {
            var allProjects = DefDatabase<ResearchProjectDef>.AllDefsListForReading;

            var childrenMap = new Dictionary<ResearchProjectDef, List<ResearchProjectDef>>();
            foreach (var proj in allProjects)
            {
                if (ResearchOrganizedLayout.IsEraCapstone(proj)) continue; // never counts toward another project's hub status

                foreach (var pre in ResearchOrganizedLayout.GetDirectPrereqs(proj))
                {
                    if (proj.tab == null || pre.tab == null || proj.tab != pre.tab) continue;
                    if (proj.tab == combinedTab && ResearchOrganizedLayout.EraBucket(proj) != ResearchOrganizedLayout.EraBucket(pre)) continue;
                    if (!childrenMap.TryGetValue(pre, out var list)) childrenMap[pre] = list = new List<ResearchProjectDef>();
                    list.Add(proj);
                }
            }

            var ancestorCounts = new Dictionary<ResearchProjectDef, int>(allProjects.Count);
            foreach (var proj in allProjects) ancestorCounts[proj] = ResearchOrganizedLayout.GetAllAncestors(proj).Count;

            var bottomUp = new List<ResearchProjectDef>(allProjects);
            bottomUp.Sort((a, b) => ancestorCounts[b].CompareTo(ancestorCounts[a]));

            int minorThreshold = ResearchOrganizedMod.settings.minorAnchorChildThreshold;
            int majorThreshold = ResearchOrganizedMod.settings.majorAnchorChildThreshold;

            var majorAnchors = new HashSet<ResearchProjectDef>();
            var minorAnchors = new HashSet<ResearchProjectDef>();

            foreach (var proj in bottomUp)
            {
                if (ResearchOrganizedLayout.IsEraCapstone(proj)) continue; // never itself a hub - it always goes last, not off to the side with a fan of its own
                if (!childrenMap.TryGetValue(proj, out var children)) continue;

                int nonMajorChildren = children.Count(c => !majorAnchors.Contains(c));
                if (majorThreshold > 0 && nonMajorChildren >= majorThreshold) { majorAnchors.Add(proj); continue; }

                int nonAnchorChildren = children.Count(c => !majorAnchors.Contains(c) && !minorAnchors.Contains(c));
                if (minorThreshold > 0 && nonAnchorChildren >= minorThreshold) minorAnchors.Add(proj);
            }

            var anchors = new HashSet<ResearchProjectDef>(majorAnchors);
            anchors.UnionWith(minorAnchors);

            var anchorList = new List<ResearchProjectDef>(anchors);
            anchorList.Sort((a, b) =>
            {
                int byDepth = ancestorCounts[a].CompareTo(ancestorCounts[b]);
                if (byDepth != 0) return byDepth;
                int byTier = (majorAnchors.Contains(a) ? 0 : 1).CompareTo(majorAnchors.Contains(b) ? 0 : 1);
                if (byTier != 0) return byTier;
                return string.CompareOrdinal(a.defName, b.defName);
            });

            anchorOrder = new Dictionary<ResearchProjectDef, int>(anchorList.Count);
            for (int i = 0; i < anchorList.Count; i++) anchorOrder[anchorList[i]] = i;

            return anchors;
        }

        /// <summary>
        /// Raises every project to the latest tech level among its real prerequisites (hidden
        /// ones included, layout-only virtual links not), transitively. A mod can give a
        /// follow-up a lower tech level than what it depends on - Apex Mechanoids' Spacer
        /// projects behind its Ultra hub - which files it on an earlier tab or era block than
        /// its prerequisite, charges it an earlier era's research cost, and misleads anything
        /// else reading the tech level. This changes the def itself, so all of that follows.
        /// Projects with no tech level are left alone either way.
        ///
        /// Every pass starts from the tech levels first seen, so a rerun recomputes rather
        /// than compounding.
        /// </summary>
        private static void RaiseTechLevelsToPrerequisites()
        {
            const int unranked = int.MaxValue;
            var projects = DefDatabase<ResearchProjectDef>.AllDefsListForReading;
            var indexOf = new Dictionary<ResearchProjectDef, int>(projects.Count);
            var authored = new TechLevel[projects.Count];
            var era = new int[projects.Count];
            for (int i = 0; i < projects.Count; i++)
            {
                var project = projects[i];
                indexOf[project] = i;
                if (!authoredTechLevels.TryGetValue(project, out authored[i])) authoredTechLevels[project] = authored[i] = project.techLevel;
                era[i] = authored[i] == TechLevel.Undefined ? unranked : (int)authored[i];
            }

            var graph = new LayoutGraph(projects.Count);
            for (int i = 0; i < projects.Count; i++)
            {
                AddPrerequisiteEdges(graph, indexOf, projects[i].prerequisites, i);
                AddPrerequisiteEdges(graph, indexOf, projects[i].hiddenPrerequisites, i);
            }

            var raised = EraPromotion.Raise(graph, era, unranked);
            var described = new List<string>();
            for (int i = 0; i < projects.Count; i++)
            {
                var level = raised[i] == unranked ? TechLevel.Undefined : (TechLevel)raised[i];
                projects[i].techLevel = level;
                if (level != authored[i]) described.Add($"{projects[i].defName} ({authored[i]} -> {level})");
            }

            string report = string.Join(", ", described);
            if (described.Count > 0 && report != lastTechLevelReport)
            {
                Log.Message($"[Research: Organized] Raised {described.Count} project(s) to their prerequisites' tech level: [{report}]");
            }
            lastTechLevelReport = report;
        }

        private static void AddPrerequisiteEdges(LayoutGraph graph, Dictionary<ResearchProjectDef, int> indexOf,
            List<ResearchProjectDef> prerequisites, int child)
        {
            if (prerequisites == null) return;
            foreach (var prerequisite in prerequisites)
            {
                if (prerequisite != null && indexOf.TryGetValue(prerequisite, out int parent)) graph.AddEdge(parent, child);
            }
        }

        private static void MapProjectsToTabs()
        {
            var anomalyTab = DefDatabase<ResearchTabDef>.GetNamed(TabAnomalyDef, false);
            var highInd = DefDatabase<ResearchTabDef>.GetNamed("TabHighIndustrial", false);
            var lateInd = DefDatabase<ResearchTabDef>.GetNamed("TabLateIndustrial", false);
            var ind = DefDatabase<ResearchTabDef>.GetNamed("TabIndustrial", false);
            bool combineIndustrial = ResearchOrganizedMod.settings.combineIndustrial;
            var combinedTab = CombinedTab;
            foreach (var project in DefDatabase<ResearchProjectDef>.AllDefs)
            {
                // Route from the tab the project was authored on, not wherever the last pass left
                // it, so turning "Combine All Tabs" back off can return it to a preserved tab.
                if (!authoredTabs.TryGetValue(project, out var authoredTab)) authoredTabs[project] = authoredTab = project.tab;
                string currentTab = authoredTab?.defName;
                TechLevelTabOverrides.TryGetValue(project.techLevel, out string overrideTabName);
                var defaultTab = DefDatabase<ResearchTabDef>.GetNamed("Tab" + project.techLevel, false);
                bool isIndustrial = project.techLevel == TechLevel.Industrial;
                bool preserveCurrent = currentTab != null && PreservedTabs.Contains(currentTab);
                bool ignoreCurrent = currentTab != null && IgnoredTabs.Contains(currentTab);
                bool inspectIndustrialRequirements = isIndustrial && !combineIndustrial && combinedTab == null
                    && overrideTabName == null && !preserveCurrent && !ignoreCurrent;

                string targetName = TabRoutingPolicy.Resolve(new TabRoutingPolicy.Request
                {
                    CurrentTab = currentTab,
                    AnomalyTab = anomalyTab?.defName,
                    OverrideTab = overrideTabName,
                    DefaultTab = defaultTab?.defName,
                    IndustrialTab = ind?.defName,
                    HighIndustrialTab = highInd?.defName,
                    LateIndustrialTab = lateInd?.defName,
                    MainTab = combinedTab?.defName,
                    IsAnomaly = project.knowledgeCategory != null || authoredTab == anomalyTab,
                    CurrentTabIgnored = ignoreCurrent,
                    CurrentTabPreserved = preserveCurrent,
                    CurrentTabExcludedFromCombine = IsGravtechTab(authoredTab),
                    CombineAll = combinedTab != null,
                    IsIndustrial = isIndustrial,
                    CombineIndustrial = combineIndustrial,
                    RequiresHighTechBench = inspectIndustrialRequirements && RequiresBuildingCached(project, HiTechBenchDef, reqHiTechCache),
                    RequiresMultiAnalyzer = inspectIndustrialRequirements && RequiresBuildingCached(project, MultiAnalyzerDef, reqMultiCache)
                });

                if (targetName != null)
                {
                    var target = DefDatabase<ResearchTabDef>.GetNamed(targetName, false);
                    if (target != null) project.tab = target;
                }
            }
        }

        private static bool RequiresBuildingCached(ResearchProjectDef node, string bName, Dictionary<ResearchProjectDef, bool> cache, HashSet<ResearchProjectDef> visited = null)
        {
            if (visited == null) visited = new HashSet<ResearchProjectDef>();
            if (!visited.Add(node)) return false;
            if (cache.TryGetValue(node, out bool res)) return res;
            if (node.requiredResearchBuilding?.defName == bName || node.requiredResearchFacilities?.Any(f => f.defName == bName) == true) return cache[node] = true;
            var allPre = new List<ResearchProjectDef>();
            if (node.prerequisites != null) allPre.AddRange(node.prerequisites);
            if (node.hiddenPrerequisites != null) allPre.AddRange(node.hiddenPrerequisites);
            foreach (var p in allPre) if (RequiresBuildingCached(p, bName, cache, visited)) return cache[node] = true;
            return cache[node] = false;
        }

        private static void HideEmptyTabs(HashSet<ResearchTabDef> activeTabs)
        {
            try
            {
                var defsListField = AccessTools.Field(typeof(DefDatabase<ResearchTabDef>), "defsList");
                var defsByNameField = AccessTools.Field(typeof(DefDatabase<ResearchTabDef>), "defsByName");
                if (defsListField == null || defsByNameField == null) return;
                var defsList = (List<ResearchTabDef>)defsListField.GetValue(null);
                var defsByName = (Dictionary<string, ResearchTabDef>)defsByNameField.GetValue(null);
                // Vanilla's Main tab is emptied by the pass above and hidden with the rest of
                // the empties. Node Research still collapses onto it and reads it back through
                // its own DefsOf field and a fixed tab strip, never through this database, so
                // the removal costs it nothing and keeps an empty tab out of the vanilla window.
                // An ignored tab is normally kept even when empty. Under "Combine All Tabs" every
                // tab but Gravship's has been emptied on purpose, so only that one is kept.
                bool combineAll = ResearchOrganizedMod.settings.combineAllTabs;
                var toRemove = defsList.Where(t => !activeTabs.Contains(t)
                    && (combineAll ? !IsGravtechTab(t) : !IgnoredTabs.Contains(t.defName))).ToList();
                foreach (var tab in toRemove)
                {
                    defsList.Remove(tab);
                    defsByName.Remove(tab.defName);
                    hiddenTabs.Add(tab);
                }
            }
            catch (Exception ex) { Log.Error($"[Research: Organized] Hide Tabs Error: {ex.Message}"); }
        }

        /// <summary>
        /// Puts back every tab a previous pass removed.
        ///
        /// Hiding a tab deletes it from the def database, which used to be a one-way trip:
        /// re-running the organiser could never bring a tab back, so changing a setting that
        /// refills a tab left it permanently invisible. Keeping the removed defs lets the
        /// organiser be run more than once in a session.
        /// </summary>
        private static void RestoreHiddenTabs()
        {
            if (hiddenTabs.Count == 0) return;

            try
            {
                var defsListField = AccessTools.Field(typeof(DefDatabase<ResearchTabDef>), "defsList");
                var defsByNameField = AccessTools.Field(typeof(DefDatabase<ResearchTabDef>), "defsByName");
                if (defsListField == null || defsByNameField == null) return;
                var defsList = (List<ResearchTabDef>)defsListField.GetValue(null);
                var defsByName = (Dictionary<string, ResearchTabDef>)defsByNameField.GetValue(null);

                foreach (var tab in hiddenTabs)
                {
                    if (!defsByName.ContainsKey(tab.defName))
                    {
                        defsList.Add(tab);
                        defsByName[tab.defName] = tab;
                    }
                }
            }
            catch (Exception ex) { Log.Error($"[Research: Organized] Restore Tabs Error: {ex.Message}"); }
            finally { hiddenTabs.Clear(); }
        }

        private static void SortAndIndexTabs()
        {
            var defsListField = AccessTools.Field(typeof(DefDatabase<ResearchTabDef>), "defsList");
            if (defsListField == null) return;
            if (defsListField.GetValue(null) is List<ResearchTabDef> list)
            {
                list.Sort((a, b) => {
                    int iA = GlobalTabOrder.IndexOf(a.defName);
                    int iB = GlobalTabOrder.IndexOf(b.defName);
                    int pA = iA == -1 ? 999 : iA;
                    int pB = iB == -1 ? 999 : iB;
                    return pA != pB ? pA.CompareTo(pB) : string.Compare(a.label, b.label, StringComparison.OrdinalIgnoreCase);
                });
                for (int i = 0; i < list.Count; i++) list[i].index = (ushort)i;
            }
        }

        public static IEnumerable<CodeInstruction> ColorTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            var targetMethod = AccessTools.GetDeclaredMethods(typeof(Widgets)).FirstOrDefault(m => m.Name == "CustomButtonText" && m.GetParameters().Length > 8) ?? AccessTools.Method(typeof(Widgets), "CustomButtonText");
            int idx = list.FindIndex(i => i.Calls(targetMethod));
            if (idx <= 2) return list;
            list[idx].opcode = OpCodes.Call;
            list[idx].operand = AccessTools.Method(typeof(ResearchOrganizedMain), nameof(DrawCustomButtonText));
            if (list[idx - 1].labels.Count > 0) list[idx].labels.AddRange(list[idx - 1].labels);
            list.RemoveAt(idx - 1);
            return list;
        }


        public static bool DrawCustomButtonText(ref Rect rect, string label, Color bgColor, Color textColor, Color borderColor, Color unfilledBgColor, bool cacheHeight, float borderSize, bool doMouseOverSound, bool active, ResearchProjectDef project)
        {
            if (!ResearchOrganizedMod.settings.disableCustomColors)
            {
                ColorSet set = ResolveColorSet(project);
                unfilledBgColor = project.IsFinished ? set.finished : !project.PrerequisitesCompleted ? set.unavailable : set.normal;
                bgColor = set.finished;
            }
            bool isFoundation = ResearchOrganizedLayout.IsFoundationTech(project);
            if (ResearchOrganizedLayout.cyclicNodes.Contains(project)) { borderColor = Color.red; borderSize = 2f; }
            bool result = Widgets.CustomButtonText(ref rect, label, bgColor, textColor, borderColor, unfilledBgColor, cacheHeight, borderSize, doMouseOverSound, active, project.ProgressPercent);
            if (isFoundation)
            {
                var notch = new Rect(rect.xMax - FoundationNotchSize, rect.yMin, FoundationNotchSize, FoundationNotchSize);
                Color previous = GUI.color;
                GUI.color = FoundationGold;
                GUI.DrawTexture(notch, FoundationNotchTex);
                GUI.color = previous;
                TooltipHandler.TipRegion(rect, FoundationTechTooltip);
            }
            return result;
        }

        /// <summary>A white right-angled triangle filling the top-right half of the texture,
        /// with a soft diagonal edge, tinted by GUI.color when drawn. Built in code so the mod
        /// ships no texture. Unity texture rows run bottom-up, so screen row r is texture row
        /// size - 1 - r.</summary>
        private static Texture2D BuildCornerNotchTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            for (int row = 0; row < size; row++)
            {
                for (int column = 0; column < size; column++)
                {
                    float alpha = Mathf.Clamp01(column - row + 0.5f);
                    texture.SetPixel(column, size - 1 - row, new Color(1f, 1f, 1f, alpha));
                }
            }
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// Makes vanilla skip prerequisite lines that are only noise. ListProjects draws a line
        /// only when the prerequisite's tab equals CurTab; this sits on the CurTab side of that
        /// comparison and answers null to skip one, since null never equals a real tab.
        /// Skipped:
        /// - on the combined tab, any line between two eras;
        /// - on any tab, a line into or out of a Node Research emergence node, unless one of
        ///   its two ends is selected, so its highlighted requirements still show on click.
        /// Everything else gets CurTab back unchanged, so vanilla behaves exactly as before.
        /// </summary>
        public static ResearchTabDef PrerequisiteLineTab(ResearchTabDef curTab, MainTabWindow_Research window,
            ResearchProjectDef project, ResearchProjectDef prerequisite)
        {
            if (curTab == null) return curTab;
            if (curTab == activeCombinedTab
                && ResearchOrganizedLayout.EraBucket(project) != ResearchOrganizedLayout.EraBucket(prerequisite)) return null;
            if (ResearchOrganizedLayout.IsEraCapstone(project) || ResearchOrganizedLayout.IsEraCapstone(prerequisite))
            {
                var selected = researchWindowSelectedProjectField?.GetValue(window);
                if (selected != project && selected != prerequisite) return null;
            }
            return curTab;
        }

        /// <summary>
        /// Routes the prerequisite-tab comparison in ListProjects' line loop through
        /// <see cref="PrerequisiteLineTab"/>. Matches
        /// <c>ldloc item; ldfld prerequisites</c> to learn which local holds the project, then the first
        /// <c>ldloc prereq; ldfld tab; ldarg.0; call get_CurTab</c> after it, and appends the window,
        /// project and prerequisite plus the helper call. Leaves the method untouched, with a
        /// warning, if the shape is not found.
        /// </summary>
        public static IEnumerable<CodeInstruction> CrossEraLineTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            var prerequisitesField = AccessTools.Field(typeof(ResearchProjectDef), nameof(ResearchProjectDef.prerequisites));
            var tabField = AccessTools.Field(typeof(ResearchProjectDef), nameof(ResearchProjectDef.tab));
            var getCurTab = AccessTools.PropertyGetter(typeof(MainTabWindow_Research), nameof(MainTabWindow_Research.CurTab));
            var helper = AccessTools.Method(typeof(ResearchOrganizedMain), nameof(PrerequisiteLineTab));

            int prerequisitesIndex = list.FindIndex(i => i.LoadsField(prerequisitesField));
            if (prerequisitesIndex > 0 && list[prerequisitesIndex - 1].IsLdloc())
            {
                var loadProject = list[prerequisitesIndex - 1];
                for (int k = prerequisitesIndex; k + 3 < list.Count; k++)
                {
                    if (list[k].IsLdloc() && list[k + 1].LoadsField(tabField)
                        && list[k + 2].opcode == OpCodes.Ldarg_0 && list[k + 3].Calls(getCurTab))
                    {
                        list.InsertRange(k + 4, new[]
                        {
                            new CodeInstruction(OpCodes.Ldarg_0),
                            new CodeInstruction(loadProject.opcode, loadProject.operand),
                            new CodeInstruction(list[k].opcode, list[k].operand),
                            new CodeInstruction(OpCodes.Call, helper)
                        });
                        return list;
                    }
                }
            }

            Log.Warning("[Research: Organized] Could not find the prerequisite line check in the research window; " +
                        "lines between eras and to emergence nodes will still be drawn.");
            return list;
        }

        private static ColorSet ResolveColorSet(ResearchProjectDef project)
        {
            string tabName = project.tab?.defName;
            if (tabName != null && TabColorOverrides.TryGetValue(tabName, out var tabOverride)) return tabOverride;
            if (tabName != null && TabToThemeMap.TryGetValue(tabName, out var techLevel)) return TabColors[techLevel];
            if (TabColors.TryGetValue(project.techLevel, out var byTechLevel)) return byTechLevel;
            return TabColors[TechLevel.Undefined];
        }

        /// <summary>Node Research (ferny.noderesearch) parks every project its own window draws
        /// onto vanilla's Main tab, remembering where each came from so its "open the vanilla
        /// menu" button can put them back. Both halves of that go wrong when this mod reorganises
        /// the same projects: its collapse returns early while it believes it still holds, so its
        /// window lists an empty Main tab, and the tabs it would restore are the ones from before
        /// this mod sorted them. Pointing it at the finished layout fixes both, and lets the user
        /// switch between the two windows. Reflective and entirely optional - with Node Research
        /// absent, or its internals renamed, every call here is a no-op.</summary>
        private static class NodeResearchCompat
        {
            private const string StartupTypeName = "BetterResearchMenu.Startup";

            private static bool resolved;
            private static FieldInfo originalTabsField;
            private static FieldInfo isCollapsedField;

            public static bool Active
            {
                get { Resolve(); return originalTabsField != null && isCollapsedField != null; }
            }

            /// <summary>Repoints Node Research's remembered tabs at wherever this pass left each
            /// project, and clears its collapsed flag so its next window open really re-collapses.</summary>
            public static void SyncTabs()
            {
                if (!Active) return;
                try
                {
                    var remembered = (Dictionary<ResearchProjectDef, ResearchTabDef>)originalTabsField.GetValue(null);
                    if (remembered != null)
                    {
                        foreach (var project in DefDatabase<ResearchProjectDef>.AllDefs) remembered[project] = project.tab;
                    }
                    isCollapsedField.SetValue(null, false);
                }
                catch (Exception ex) { Log.Error($"[Research: Organized] Node Research sync error: {ex.Message}"); }
            }

            private static void Resolve()
            {
                if (resolved) return;
                resolved = true;
                var startupType = AccessTools.TypeByName(StartupTypeName);
                if (startupType == null) return;
                var tabs = AccessTools.Field(startupType, "originalTabs");
                var collapsed = AccessTools.Field(startupType, "isCollapsed");
                if (tabs != null && tabs.FieldType == typeof(Dictionary<ResearchProjectDef, ResearchTabDef>)) originalTabsField = tabs;
                if (collapsed != null && collapsed.FieldType == typeof(bool)) isCollapsedField = collapsed;
            }
        }
    }
}
