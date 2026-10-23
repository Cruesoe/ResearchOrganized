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
        private static readonly List<ResearchTabDef> hiddenTabs = new List<ResearchTabDef>();

        /// <summary>The tab each project was on when this mod first saw it. Kept across passes.</summary>
        private static readonly Dictionary<ResearchProjectDef, ResearchTabDef> authoredTabs = new Dictionary<ResearchProjectDef, ResearchTabDef>();

        /// <summary>The tech level each project had when this mod first saw it. Kept across passes.</summary>
        private static readonly Dictionary<ResearchProjectDef, TechLevel> authoredTechLevels = new Dictionary<ResearchProjectDef, TechLevel>();
        private static string lastTechLevelReport;

        private static readonly Dictionary<ResearchProjectDef, bool> reqMultiCache = new Dictionary<ResearchProjectDef, bool>();
        private static readonly Dictionary<ResearchProjectDef, bool> reqHiTechCache = new Dictionary<ResearchProjectDef, bool>();

        /// <summary>Vanilla Gravship Expanded's gravtech tab, by defName or by the extension VGE
        /// itself uses to recognize one.</summary>
        private static bool IsGravtechTab(ResearchTabDef tab)
        {
            if (tab == null) return false;
            if (tab.defName == TabGravtechDef) return true;
            return tab.modExtensions != null && tab.modExtensions.Any(e => e.GetType().Name == GravtechExtensionName);
        }

        /// <summary>A tab that keeps its projects under "Combine All Tabs": Gravship's, or one listed in a config's uncombinedTabs.</summary>
        private static bool IsUncombinedTab(ResearchTabDef tab)
        {
            return tab != null && (IsGravtechTab(tab) || UncombinedTabs.Contains(tab.defName));
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
                    CurrentTabExcludedFromCombine = IsUncombinedTab(authoredTab),
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
                // tab but the uncombined ones has been emptied on purpose, so only those are kept.
                bool combineAll = ResearchOrganizedMod.settings.combineAllTabs;
                var toRemove = defsList.Where(t => !activeTabs.Contains(t)
                    && (combineAll ? !IsUncombinedTab(t) : !IgnoredTabs.Contains(t.defName))).ToList();
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
        /// re-running the organizer could never bring a tab back, so changing a setting that
        /// refills a tab left it permanently invisible. Keeping the removed defs lets the
        /// organizer be run more than once in a session.
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

    }
}
