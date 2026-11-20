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
    /// <summary>
    /// Gives the vanilla research window the UI Rethemed look while Match UI Rethemed Theme is on: its background, tab strip,
    /// panels, start button, progress bar, divider, lines and search highlight. Each vanilla draw call is redirected to a
    /// helper with the same signature that draws the themed face, or calls vanilla when the theme is off or failed.
    /// A call whose signature differs from the helper's is left alone, so a game update leaves that part vanilla.
    /// </summary>
    public static class ResearchWindowTheme
    {
        private const string UIRethemedResearchPackageId = "LazyFridayStudio.UIRethemed.Research";

        /// <summary>UI Rethemed - Research draws this window itself; resolved once.</summary>
        private static readonly bool uiRethemedResearchActive = ModsConfig.IsActive(UIRethemedResearchPackageId);

        /// <summary>Set while vanilla draws the research window's tab strip, so the tab face patch knows which tabs are ours.</summary>
        private static bool drawingTabs;

        /// <summary>Whether this class turned the window's own background off, so it is only turned back on by us.</summary>
        private static bool backgroundTurnedOff;

        private static ResearchSkin Skin => uiRethemedResearchActive ? null : ResearchSkin.Active;

        public static void Patch(Harmony harmony)
        {
            Transpile(harmony, "DrawRightRect", nameof(DrawRightRectTranspiler));
            Transpile(harmony, "DrawProjectInfo", nameof(DrawProjectInfoTranspiler));
            Transpile(harmony, "DrawStartButton", nameof(DrawStartButtonTranspiler));
            Transpile(harmony, "DrawProjectProgress", nameof(DrawProjectProgressTranspiler));
            Transpile(harmony, "DrawProjectPrimaryInfo", nameof(DrawProjectPrimaryInfoTranspiler));
            Transpile(harmony, "ListProjects", nameof(ListProjectsTranspiler));

            var innerWindowOnGUI = AccessTools.Method(typeof(Window), "InnerWindowOnGUI");
            if (innerWindowOnGUI != null)
                harmony.Patch(innerWindowOnGUI, prefix: new HarmonyMethod(typeof(ResearchWindowTheme), nameof(BeforeWindowDraw)));
            var tabDraw = AccessTools.Method(typeof(TabRecord), nameof(TabRecord.Draw));
            if (tabDraw != null)
                harmony.Patch(tabDraw, prefix: new HarmonyMethod(typeof(ResearchWindowTheme), nameof(TabDrawPrefix)));
        }

        private static void Transpile(Harmony harmony, string method, string transpiler)
        {
            var target = AccessTools.Method(typeof(MainTabWindow_Research), method);
            if (target == null)
            {
                Log.Warning($"[Research: Organized] MainTabWindow_Research.{method} not found; that part of the window keeps the vanilla look.");
                return;
            }
            harmony.Patch(target, transpiler: new HarmonyMethod(typeof(ResearchWindowTheme), transpiler));
        }

        // Transpilers: each names the vanilla calls it redirects in one method.

        public static IEnumerable<CodeInstruction> DrawRightRectTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            Redirect(list, "DrawRightRect", m => IsWidgets(m, nameof(Widgets.DrawMenuSection)), nameof(MenuSection));
            Redirect(list, "DrawRightRect", m => m.DeclaringType == typeof(TabDrawer) && m.Name == nameof(TabDrawer.DrawTabsOverflow), nameof(TabsOverflow));
            return list;
        }

        public static IEnumerable<CodeInstruction> DrawProjectInfoTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            Redirect(list, "DrawProjectInfo", m => IsWidgets(m, nameof(Widgets.DrawMenuSection)), nameof(MenuSection));
            return list;
        }

        public static IEnumerable<CodeInstruction> DrawStartButtonTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            // The first button starts research, the second stops it.
            Redirect(list, "DrawStartButton", m => IsWidgets(m, nameof(Widgets.ButtonText)), nameof(StartButton), 0);
            Redirect(list, "DrawStartButton", m => IsWidgets(m, nameof(Widgets.ButtonText)), nameof(StopButton), 0);
            Redirect(list, "DrawStartButton", m => IsWidgets(m, nameof(Widgets.DrawHighlight)), nameof(LockedHighlight));
            return list;
        }

        public static IEnumerable<CodeInstruction> DrawProjectProgressTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            Redirect(list, "DrawProjectProgress", m => IsWidgets(m, nameof(Widgets.FillableBar)), nameof(ProgressBar));
            return list;
        }

        public static IEnumerable<CodeInstruction> DrawProjectPrimaryInfoTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            Redirect(list, "DrawProjectPrimaryInfo", m => IsWidgets(m, nameof(Widgets.DrawLineHorizontal)), nameof(DividerLine));
            return list;
        }

        public static IEnumerable<CodeInstruction> ListProjectsTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            Redirect(list, "ListProjects", m => IsWidgets(m, nameof(Widgets.DrawLine)), nameof(TreeLine));
            Redirect(list, "ListProjects", m => IsWidgets(m, nameof(Widgets.DrawStrongHighlight)), nameof(SearchHighlight));
            Redirect(list, "ListProjects", m => IsWidgets(m, nameof(Widgets.DrawLightHighlight)), nameof(CardHover));
            return list;
        }

        private static bool IsWidgets(MethodInfo method, string name) => method.DeclaringType == typeof(Widgets) && method.Name == name;

        /// <summary>
        /// Points calls matching <paramref name="match"/> at the helper of the same parameter and return types; with
        /// <paramref name="occurrence"/>, only the nth remaining match. Generic calls get the helper made with the same type arguments.
        /// </summary>
        private static void Redirect(List<CodeInstruction> list, string where, Func<MethodInfo, bool> match, string helperName, int occurrence = -1)
        {
            var helper = AccessTools.Method(typeof(ResearchWindowTheme), helperName);
            int seen = 0;
            bool any = false;
            for (int i = 0; i < list.Count; i++)
            {
                if ((list[i].opcode != OpCodes.Call && list[i].opcode != OpCodes.Callvirt) || !(list[i].operand is MethodInfo called) || !match(called))
                    continue;
                if (occurrence >= 0 && seen++ != occurrence)
                    continue;
                var replacement = helper.IsGenericMethodDefinition && called.IsGenericMethod
                    ? helper.MakeGenericMethod(called.GetGenericArguments())
                    : helper;
                if (!SameSignature(called, replacement))
                {
                    Log.Warning($"[Research: Organized] {where}: {called.Name} has changed; that part of the research window keeps the vanilla look.");
                    return;
                }
                list[i].opcode = OpCodes.Call;
                list[i].operand = replacement;
                any = true;
                if (occurrence >= 0)
                    return;
            }
            if (!any)
                Log.Warning($"[Research: Organized] {where}: no {helperName} call found; that part of the research window keeps the vanilla look.");
        }

        private static bool SameSignature(MethodInfo a, MethodInfo b)
        {
            if (a.ReturnType != b.ReturnType)
                return false;
            var pa = a.GetParameters();
            var pb = b.GetParameters();
            if (pa.Length != pb.Length)
                return false;
            for (int i = 0; i < pa.Length; i++)
            {
                if (pa[i].ParameterType != pb[i].ParameterType)
                    return false;
            }
            return true;
        }

        // Window background and tab faces.

        /// <summary>Runs before every window draws: turns the research window's background off and draws the themed one instead.</summary>
        public static void BeforeWindowDraw(Window __instance)
        {
            // The exact type: research windows from other mods that subclass it draw their own look.
            if (__instance.GetType() != typeof(MainTabWindow_Research))
                return;
            var skin = Skin;
            if (skin != null && skin.WindowBackground(__instance.windowRect.AtZero()))
            {
                __instance.doWindowBackground = false;
                backgroundTurnedOff = true;
            }
            else if (backgroundTurnedOff)
            {
                __instance.doWindowBackground = true;
                backgroundTurnedOff = false;
            }
        }

        public static TabRecord TabsOverflow<T>(Rect baseRect, List<T> tabs, float minTabWidth, float maxTabWidth) where T : TabRecord
        {
            drawingTabs = Skin != null;
            try
            {
                return TabDrawer.DrawTabsOverflow(baseRect, tabs, minTabWidth, maxTabWidth);
            }
            finally
            {
                drawingTabs = false;
            }
        }

        /// <summary>Draws the research window's tabs with the themed face; every other tab strip in the game is untouched.</summary>
        public static bool TabDrawPrefix(TabRecord __instance, Rect rect)
        {
            if (!drawingTabs)
                return true;
            var skin = Skin;
            return skin == null || !skin.Tab(rect, __instance.label, __instance.Selected, __instance.labelColor);
        }

        // Helpers with the vanilla call's signature.

        public static void MenuSection(Rect rect)
        {
            var skin = Skin;
            if (skin == null || !skin.Panel(rect))
                Widgets.DrawMenuSection(rect);
        }

        public static bool StartButton(Rect rect, string label, bool drawBackground, bool doMouseoverSound, bool active, TextAnchor? overrideTextAnchor) =>
            ToneButton(rect, label, SkinTone.Green, drawBackground, doMouseoverSound, active, overrideTextAnchor);

        public static bool StopButton(Rect rect, string label, bool drawBackground, bool doMouseoverSound, bool active, TextAnchor? overrideTextAnchor) =>
            ToneButton(rect, label, SkinTone.Red, drawBackground, doMouseoverSound, active, overrideTextAnchor);

        private static bool ToneButton(Rect rect, string label, SkinTone tone, bool drawBackground, bool doMouseoverSound, bool active, TextAnchor? overrideTextAnchor)
        {
            var skin = Skin;
            if (skin == null || !skin.Button(rect, label, tone, active))
                return Widgets.ButtonText(rect, label, drawBackground, doMouseoverSound, active, overrideTextAnchor);
            return active && Widgets.ButtonInvisible(rect, doMouseoverSound);
        }

        public static void LockedHighlight(Rect rect)
        {
            var skin = Skin;
            if (skin == null || !skin.Well(rect))
                Widgets.DrawHighlight(rect);
        }

        public static Rect ProgressBar(Rect rect, float fillPercent, Texture2D fillTex, Texture2D bgTex, bool doBorder)
        {
            var skin = Skin;
            if (skin == null || !skin.Bar(rect, fillPercent))
                return Widgets.FillableBar(rect, fillPercent, fillTex, bgTex, doBorder);
            return doBorder ? rect.ContractedBy(3f) : rect;
        }

        public static void DividerLine(float x, float y, float length, Color color)
        {
            var skin = Skin;
            if (skin == null || !skin.Divider(x, y, length))
                Widgets.DrawLineHorizontal(x, y, length, color);
        }

        public static void TreeLine(Vector2 start, Vector2 end, Color color, float width)
        {
            var skin = Skin;
            Widgets.DrawLine(start, end, skin == null ? color : skin.ResearchColor(color), width);
        }

        public static void SearchHighlight(Rect rect, Color? color)
        {
            var skin = Skin;
            if (skin == null || !skin.Glow(rect))
                Widgets.DrawStrongHighlight(rect, color);
        }

        /// <summary>Themed cards light up on hover themselves, so vanilla's square highlight is skipped.</summary>
        public static void CardHover(Rect rect)
        {
            if (Skin == null)
                Widgets.DrawLightHighlight(rect);
        }
    }
}
