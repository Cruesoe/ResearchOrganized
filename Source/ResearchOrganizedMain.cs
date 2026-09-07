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
        private const string TabGravtechDef = "VGE_Gravtech";
        private const string GravtechExtensionName = "GravtechResearchExtension";

        private const float NormalBrightness = 0.5f;
        private const float UnavailableBrightness = 0.2f;

        /// <summary>Marks Node Research's foundation technologies. Nothing else in this UI is gold.
        /// Muted and semi-transparent so it reads as a hint, not an alert like the red cyclic border.</summary>
        private static readonly Color FoundationGold = new Color(0.75f, 0.62f, 0.32f, 0.65f);
        private const float FoundationBorderSize = 1.5f;
        private const string FoundationTechTooltip = "Foundation technology";

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

            OrganizeTabsAndLayout();
        }

        private static void OnGameFinalizeInit()
        {
            OrganizeTabsAndLayout();
        }

        /// <summary>Turns a tab tooltip that renders as nothing but markup into an empty string,
        /// which is what TabDrawer checks before deciding to show a tooltip at all.</summary>
        public static void BlankEmptyTabTip(ref string __result)
        {
            if (!__result.NullOrEmpty() && __result.StripTags().Trim().Length == 0) __result = "";
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
            TabToThemeMap[TabGravtechDef] = TechLevel.Spacer;
            foreach (var tab in DefDatabase<ResearchTabDef>.AllDefs)
            {
                if (tab.modExtensions != null && tab.modExtensions.Any(e => e.GetType().Name == GravtechExtensionName)) TabToThemeMap[tab.defName] = TechLevel.Spacer;
            }
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

                Dictionary<ResearchProjectDef, int> anchorOrder;
                var anchors = FindAnchors(out anchorOrder);

                foreach (var tab in activeTabs)
                {
                    if (IgnoredTabs.Contains(tab.defName)) continue;
                    var projects = DefDatabase<ResearchProjectDef>.AllDefs.Where(p => p.tab == tab).ToList();
                    if (projects.Count == 0) continue;

                    // Contain failures to the tab that caused them. One bad tab used to
                    // abort this loop, leaving every remaining tab at its authored layout.
                    try
                    {
                        ResearchOrganizedLayout.ApplyLayout(projects, tab.defName, anchors, anchorOrder);
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
        private static HashSet<ResearchProjectDef> FindAnchors(out Dictionary<ResearchProjectDef, int> anchorOrder)
        {
            var allProjects = DefDatabase<ResearchProjectDef>.AllDefsListForReading;

            var childrenMap = new Dictionary<ResearchProjectDef, List<ResearchProjectDef>>();
            foreach (var proj in allProjects)
            {
                if (ResearchOrganizedLayout.IsEraCapstone(proj)) continue; // never counts toward another project's hub status

                foreach (var pre in ResearchOrganizedLayout.GetDirectPrereqs(proj))
                {
                    if (proj.tab == null || pre.tab == null || proj.tab != pre.tab) continue;
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
                // Vanilla's Main tab is emptied by the pass above and hidden with the rest of
                // the empties. Node Research still collapses onto it and reads it back through
                // its own DefsOf field and a fixed tab strip, never through this database, so
                // the removal costs it nothing and keeps an empty tab out of the vanilla window.
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
            bool isFoundation = ResearchOrganizedLayout.IsFoundationTech(project);
            if (isFoundation) { borderColor = FoundationGold; borderSize = Mathf.Max(borderSize, FoundationBorderSize); }
            if (ResearchOrganizedLayout.cyclicNodes.Contains(project)) { borderColor = Color.red; borderSize = 2f; }
            bool result = Widgets.CustomButtonText(ref rect, label, bgColor, textColor, borderColor, unfilledBgColor, cacheHeight, borderSize, doMouseOverSound, active, project.ProgressPercent);
            if (isFoundation) TooltipHandler.TipRegion(rect, FoundationTechTooltip);
            return result;
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