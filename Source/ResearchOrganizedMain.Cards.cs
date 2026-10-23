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
        private const float NormalBrightness = 0.5f;
        private const float UnavailableBrightness = 0.2f;

        /// <summary>Marks Node Research's foundation technologies with a small triangle in the
        /// card's top-right corner. A border color was tried first, but vanilla already borders
        /// the selected project and colors the active one in similar gold, so a shape is used
        /// instead and the border is left to vanilla. Top-right because Anomaly cards put their
        /// knowledge icon on the left.</summary>
        private static readonly Color FoundationGold = new Color(0.75f, 0.62f, 0.32f, 0.9f);
        private const float FoundationNotchSize = 12f;
        private const int FoundationNotchTextureSize = 32;
        private static readonly Texture2D FoundationNotchTex = BuildCornerNotchTexture(FoundationNotchTextureSize);

        private static readonly Dictionary<TechLevel, ColorSet> TabColors = new Dictionary<TechLevel, ColorSet>();
        private static readonly Dictionary<string, ColorSet> TabColorOverrides = new Dictionary<string, ColorSet>();
        private static readonly Dictionary<string, TechLevel> TabToThemeMap = new Dictionary<string, TechLevel>();

        private struct ColorSet
        {
            public Color unavailable;
            public Color normal;
            public Color finished;
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
            // coloring them per project draws that one tab in mixed colors. Gravtech is
            // spacer-era content, so pin the whole tab to spacer. The defName covers the tab as it
            // ships; the sweep below covers a rename, since that extension is what VGE itself uses
            // to recognize a gravtech tab. A tabThemes entry in a config def still wins over both,
            // because LoadConfigs runs after this.
            foreach (var tab in DefDatabase<ResearchTabDef>.AllDefs)
            {
                if (IsGravtechTab(tab)) TabToThemeMap[tab.defName] = TechLevel.Spacer;
            }
        }

        /// <summary>Replaces ListProjects' card draw with <see cref="DrawCustomButtonText"/>, passing the project in place of its progress.</summary>
        public static IEnumerable<CodeInstruction> ColorTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            var targetMethod = AccessTools.GetDeclaredMethods(typeof(Widgets)).FirstOrDefault(m => m.Name == "CustomButtonText" && m.GetParameters().Length > 8) ?? AccessTools.Method(typeof(Widgets), "CustomButtonText");
            int idx = list.FindIndex(i => i.Calls(targetMethod));
            if (idx <= 2)
            {
                Log.Warning("[Research: Organized] Could not find the research card draw in the research window; cards keep vanilla colors.");
                return list;
            }
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
                TooltipHandler.TipRegion(rect, "ResearchOrganized_FoundationTech".Translate());
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
        ///   its two ends is selected, so its highlighted requirements still show on click - only
        ///   when the "Suppress Emergence Lines" setting is on.
        /// Everything else gets CurTab back unchanged, so vanilla behaves exactly as before.
        /// </summary>
        public static ResearchTabDef PrerequisiteLineTab(ResearchTabDef curTab, MainTabWindow_Research window,
            ResearchProjectDef project, ResearchProjectDef prerequisite)
        {
            if (curTab == null) return curTab;
            if (curTab == activeCombinedTab
                && ResearchOrganizedLayout.EraBucket(project) != ResearchOrganizedLayout.EraBucket(prerequisite)) return null;
            if (ResearchOrganizedMod.settings.suppressEmergenceLines
                && (ResearchOrganizedLayout.IsEraCapstone(project) || ResearchOrganizedLayout.IsEraCapstone(prerequisite)))
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

    }
}
