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
    public static partial class ResearchOrganizedMain
    {
        public const string HarmonyId = "cruesoe.research.organized";
        private const string MultiAnalyzerDef = "MultiAnalyzer";
        private const string HiTechBenchDef = "HiTechResearchBench";
        private const string TabAnomalyDef = "Anomaly";
        private const string MainTabDef = "Main";
        private const string TabGravtechDef = "VGE_Gravtech";
        private const string GravtechExtensionName = "GravtechResearchExtension";

        public static List<string> IgnoredTabs = new List<string>();
        public static List<string> PreservedTabs = new List<string>();
        public static List<string> UncombinedTabs = new List<string>();
        public static List<string> GlobalTabOrder = new List<string>();
        public static Dictionary<ResearchProjectDef, List<ResearchProjectDef>> VirtualPrereqsCache = new Dictionary<ResearchProjectDef, List<ResearchProjectDef>>();
        public static Dictionary<string, LayoutConfig> TabLayouts = new Dictionary<string, LayoutConfig>();
        public static Dictionary<TechLevel, string> TechLevelTabOverrides = new Dictionary<TechLevel, string>();

        /// <summary>The combined tab the last layout pass built, read every frame by the line filter.</summary>
        private static ResearchTabDef activeCombinedTab;

        public static float GlobalXStep = 1f;
        public static float GlobalYStep = 0.63f;
        public static int GlobalMaxNodesPerColumn = 12;

        static ResearchOrganizedMain()
        {
            RefreshColors();
            InitTabThemes();

            var harmony = new Harmony(HarmonyId);

            // Card colors, and skipping lines between eras on the combined tab and to emergence nodes.
            var listProjectsMethod = AccessTools.Method(typeof(MainTabWindow_Research), "ListProjects");
            if (listProjectsMethod != null)
            {
                harmony.Patch(listProjectsMethod, transpiler: new HarmonyMethod(typeof(ResearchOrganizedMain), nameof(ColorTranspiler)));
                harmony.Patch(listProjectsMethod, transpiler: new HarmonyMethod(typeof(ResearchOrganizedMain), nameof(CrossEraLineTranspiler)));
            }

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

            // Tab tooltips get live project and research point counts.
            var researchTabRecordType = AccessTools.Inner(typeof(MainTabWindow_Research), "ResearchTabRecord");
            var tabTipMethod = researchTabRecordType != null ? AccessTools.Method(researchTabRecordType, "GetTip") : null;
            if (tabTipMethod != null)
            {
                harmony.Patch(tabTipMethod, postfix: new HarmonyMethod(typeof(ResearchOrganizedMain), nameof(AppendTabCounts)));
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
                // Vanilla PostOpen refills the tab strip with every tab; empty ones are dropped again on the next draw.
                harmony.Patch(AccessTools.Method(typeof(MainTabWindow_Research), nameof(MainTabWindow_Research.PostOpen)),
                    postfix: new HarmonyMethod(typeof(ResearchOrganizedMain), nameof(OnResearchWindowOpened)));
                harmony.Patch(doWindowContentsMethod, prefix: new HarmonyMethod(typeof(ResearchOrganizedMain), nameof(RefreshTabStripIfDirty)));
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
            semiRandomResearchActive = SemiRandomResearchPatchTypes.Any(name => AccessTools.TypeByName(name) != null);

            // Full screen: the left panel shrinks to a strip of buttons and the window grows to the screen's height.
            if (doWindowContentsMethod != null && drawLeftRectMethod != null)
            {
                harmony.Patch(doWindowContentsMethod, prefix: new HarmonyMethod(typeof(ResearchOrganizedMain), nameof(ApplyFullScreenSize)),
                    postfix: new HarmonyMethod(typeof(ResearchOrganizedMain), nameof(DrawFullScreenPanel)),
                    transpiler: new HarmonyMethod(typeof(ResearchOrganizedMain), nameof(LeftPanelWidthTranspiler)));
                harmony.Patch(drawLeftRectMethod, prefix: new HarmonyMethod(typeof(ResearchOrganizedMain), nameof(SkipLeftPanelInFullScreen)));
                var drawRightRectMethod = AccessTools.Method(typeof(MainTabWindow_Research), "DrawRightRect");
                if (drawRightRectMethod != null)
                    harmony.Patch(drawRightRectMethod, prefix: new HarmonyMethod(typeof(ResearchOrganizedMain), nameof(MeasureTreeHeight)));
                harmony.Patch(AccessTools.PropertyGetter(typeof(MainTabWindow_Research), nameof(MainTabWindow_Research.InitialSize)),
                    postfix: new HarmonyMethod(typeof(ResearchOrganizedMain), nameof(FullScreenInitialSize)));
            }

            // Vanilla PreOpen arms a scroll to the current project on every open; keep the view where it was left instead.
            var preOpenMethod = AccessTools.Method(typeof(MainTabWindow_Research), nameof(MainTabWindow_Research.PreOpen));
            if (preOpenMethod != null && researchWindowScrollPositionerField != null)
            {
                harmony.Patch(preOpenMethod, postfix: new HarmonyMethod(typeof(ResearchOrganizedMain), nameof(KeepScrollOnReopen)));
            }

            // Cached tab counts are dropped whenever research progress can complete or reset a project.
            var invalidateStats = new HarmonyMethod(typeof(ResearchOrganizedMain), nameof(InvalidateTabStats));
            var statNames = new HashSet<string> { nameof(ResearchManager.FinishProject), nameof(ResearchManager.AddProgress), nameof(ResearchManager.ApplyKnowledge),
                nameof(ResearchManager.ResetAllProgress), nameof(ResearchManager.DebugSetAllProjectsFinished) };
            // Every overload: ApplyKnowledge has one per knowledge category and one per project.
            foreach (var method in AccessTools.GetDeclaredMethods(typeof(ResearchManager)).Where(m => statNames.Contains(m.Name)))
            {
                harmony.Patch(method, postfix: invalidateStats);
            }

            UnavailableProjectFilter.Patch(harmony);

            OrganizeTabsAndLayout();
        }

        private static void OnGameFinalizeInit()
        {
            OrganizeTabsAndLayout();
        }

        /// <summary>The single tab every project lands on under "Combine All Tabs", or null when
        /// the option is off.</summary>
        private static ResearchTabDef CombinedTab =>
            ResearchOrganizedMod.settings.combineAllTabs ? DefDatabase<ResearchTabDef>.GetNamed(MainTabDef, false) : null;

        public static void OrganizeTabsAndLayout()
        {
            try
            {
                // Lets Node Research finish its own start-up before this pass routes anything.
                _ = NodeResearchCompat.Active;
                CaptureLayoutTreeHeight();
                ResetCaches();
                var tabInfoVisibility = SnapshotTabInfoVisibility();
                RestoreHiddenTabs();
                LoadConfigs();
                RaiseTechLevelsToPrerequisites();
                MapProjectsToTabs();
                NodeResearchCompat.RecordRouting();
                var activeTabs = new HashSet<ResearchTabDef>(DefDatabase<ResearchProjectDef>.AllDefs.Select(p => p.tab).Where(t => t != null));
                HideEmptyTabs(activeTabs);
                SortAndIndexTabs();
                RebuildTabInfoVisibility(tabInfoVisibility);

                var combinedTab = CombinedTab;
                activeCombinedTab = combinedTab;
                Dictionary<ResearchProjectDef, int> anchorOrder;
                var anchors = FindAnchors(combinedTab, out anchorOrder, out var majorAnchors);

                foreach (var tab in activeTabs)
                {
                    if (IgnoredTabs.Contains(tab.defName)) continue;
                    var projects = DefDatabase<ResearchProjectDef>.AllDefs.Where(p => p.tab == tab).ToList();
                    if (projects.Count == 0) continue;

                    // Contain failures to the tab that caused them. One bad tab used to
                    // abort this loop, leaving every remaining tab at its authored layout.
                    try
                    {
                        ResearchOrganizedLayout.ApplyLayout(projects, tab.defName, anchors, anchorOrder, majorAnchors,
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

        private static void ResetCaches()
        {
            IgnoredTabs.Clear();
            PreservedTabs.Clear();
            UncombinedTabs.Clear();
            GlobalTabOrder.Clear();
            TabLayouts.Clear();
            TechLevelTabOverrides.Clear();
            reqMultiCache.Clear();
            reqHiTechCache.Clear();
            VirtualPrereqsCache.Clear();
            InvalidateTabStats();
            ResearchOrganizedLayout.ClearCaches();
        }

        private static void LoadConfigs()
        {
            foreach (var config in DefDatabase<ResearchOrganizedConfig>.AllDefs)
            {
                if (config.ignoredTabs != null) IgnoredTabs.AddRange(config.ignoredTabs);
                if (config.preservedTabs != null) PreservedTabs.AddRange(config.preservedTabs);
                if (config.uncombinedTabs != null) UncombinedTabs.AddRange(config.uncombinedTabs);
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
    }
}
