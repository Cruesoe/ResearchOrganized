using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
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

        private const float NormalBrightness = 0.5f;
        private const float UnavailableBrightness = 0.2f;

        private static readonly Dictionary<TechLevel, ColorSet> TabColors = new Dictionary<TechLevel, ColorSet>();
        private static readonly Dictionary<string, ColorSet> TabColorOverrides = new Dictionary<string, ColorSet>();
        private static readonly Dictionary<string, TechLevel> TabToThemeMap = new Dictionary<string, TechLevel>();

        public static List<string> IgnoredTabs = new List<string>();
        public static List<string> GlobalTabOrder = new List<string>();
        public static Dictionary<ResearchProjectDef, List<ResearchProjectDef>> VirtualPrereqsCache = new Dictionary<ResearchProjectDef, List<ResearchProjectDef>>();
        public static Dictionary<string, LayoutConfig> TabLayouts = new Dictionary<string, LayoutConfig>();

        private static readonly List<ResearchTabDef> hiddenTabs = new List<ResearchTabDef>();

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
            }

            // A DrawConnections patch used to be registered here to suppress connection lines
            // running between tabs, but its transpiler returned the instruction stream
            // untouched, so it only ever added overhead. Cross-tab line suppression is still
            // unimplemented; see the README.

            OrganizeTabsAndLayout();
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
        }

        public static void OrganizeTabsAndLayout()
        {
            try
            {
                ResetCaches();
                RestoreHiddenTabs();
                LoadConfigs();
                MapProjectsToTabs();
                var activeTabs = new HashSet<ResearchTabDef>(DefDatabase<ResearchProjectDef>.AllDefs.Select(p => p.tab).Where(t => t != null));
                HideEmptyTabs(activeTabs);
                SortAndIndexTabs();

                foreach (var tab in activeTabs)
                {
                    if (IgnoredTabs.Contains(tab.defName)) continue;
                    var projects = DefDatabase<ResearchProjectDef>.AllDefs.Where(p => p.tab == tab).ToList();
                    if (projects.Count > 0) ResearchOrganizedLayout.ApplyLayout(projects, tab.defName);
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
            }
            catch (Exception ex) { Log.Error($"[Research: Organized] Master Organizer Error: {ex}"); }
        }

        private static void ResetCaches()
        {
            IgnoredTabs.Clear();
            GlobalTabOrder.Clear();
            TabLayouts.Clear();
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
                if (config.tabOrder != null) GlobalTabOrder.AddRange(config.tabOrder);
                if (config.tabThemes != null) foreach (var entry in config.tabThemes) if (!string.IsNullOrEmpty(entry.tabName)) TabToThemeMap[entry.tabName] = entry.techLevel;
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

        private static void MapProjectsToTabs()
        {
            var anomalyTab = DefDatabase<ResearchTabDef>.GetNamed(TabAnomalyDef, false);
            var highInd = DefDatabase<ResearchTabDef>.GetNamed("TabHighIndustrial", false);
            var lateInd = DefDatabase<ResearchTabDef>.GetNamed("TabLateIndustrial", false);
            var ind = DefDatabase<ResearchTabDef>.GetNamed("TabIndustrial", false);
            bool combineIndustrial = ResearchOrganizedMod.settings.combineIndustrial;
            foreach (var project in DefDatabase<ResearchProjectDef>.AllDefs)
            {
                if (anomalyTab != null && (project.knowledgeCategory != null || project.tab == anomalyTab)) { project.tab = anomalyTab; continue; }
                if (project.tab != null && IgnoredTabs.Contains(project.tab.defName)) continue;
                if (project.techLevel == TechLevel.Industrial)
                {
                    if (combineIndustrial) project.tab = ind;
                    else project.tab = (lateInd != null && RequiresBuildingCached(project, MultiAnalyzerDef, reqMultiCache)) ? lateInd : (highInd != null && RequiresBuildingCached(project, HiTechBenchDef, reqHiTechCache)) ? highInd : ind;
                }
                else
                {
                    var target = DefDatabase<ResearchTabDef>.GetNamed("Tab" + project.techLevel, false);
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
                var toRemove = defsList.Where(t => !activeTabs.Contains(t) && !IgnoredTabs.Contains(t.defName)).ToList();
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
            if (ResearchOrganizedLayout.cyclicNodes.Contains(project)) { borderColor = Color.red; borderSize = 2f; }
            return Widgets.CustomButtonText(ref rect, label, bgColor, textColor, borderColor, unfilledBgColor, cacheHeight, borderSize, doMouseOverSound, active, project.ProgressPercent);
        }

        private static ColorSet ResolveColorSet(ResearchProjectDef project)
        {
            string tabName = project.tab?.defName;
            if (tabName != null && TabColorOverrides.TryGetValue(tabName, out var tabOverride)) return tabOverride;
            if (tabName != null && TabToThemeMap.TryGetValue(tabName, out var techLevel)) return TabColors[techLevel];
            if (TabColors.TryGetValue(project.techLevel, out var byTechLevel)) return byTechLevel;
            return TabColors[TechLevel.Undefined];
        }
    }
}