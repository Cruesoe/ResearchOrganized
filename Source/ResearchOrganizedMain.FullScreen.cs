using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ResearchOrganized
{
    public static partial class ResearchOrganizedMain
    {
        private static readonly Texture2D EnterFullScreenTex = BuildFullScreenTexture(64, false);
        private static readonly Texture2D ExitFullScreenTex = BuildFullScreenTexture(64, true);
        private static readonly Texture2D ProjectPanelTex = BuildPanelTexture(64);
        private static readonly MethodInfo windowSetInitialSizeMethod = AccessTools.Method(typeof(Window), "SetInitialSizeAndPosition");
        private static readonly MethodInfo researchWindowDrawLeftRectMethod = AccessTools.Method(typeof(MainTabWindow_Research), "DrawLeftRect");
        private static readonly Rect[] ButtonRects = new Rect[3];
        private static readonly List<TabRecord> measureTabs = new List<TabRecord>();

        // Vanilla CoordToPixelsY: pixels per unit of researchViewY.
        private const float PixelsPerY = 100f;
        private const float CardHeight = 50f;
        // Below the tab strip: 16 for the horizontal scrollbar and 20 of padding above and below the projects.
        private const float TreeViewPadding = 56f;

        private static bool fullScreenApplied;
        private static bool resizedThisFrame;
        private static bool fullScreenPanelOpen;
        private static bool drawingFullScreenPanel;
        private static bool projectCardClicked;
        private static EventType? blockedEventType;
        private static float normalLeftPanelWidth = 270f;

        /// <summary>Height the full screen tree area had when last measured; 0 until the window has drawn in full screen.</summary>
        private static float measuredTreeHeight;

        /// <summary>The tree height the current layout was built for; 0 for the normal layout.</summary>
        private static float layoutTreeHeight;

        /// <summary>Width of the left panel in full screen: just wide enough for its buttons.</summary>
        private static float FullScreenStripWidth => semiRandomResearchActive ? SemiRandomResearchButtonSize : SettingsButtonSize;

        private static float WantedLayoutTreeHeight => ResearchOrganizedMod.settings.fullScreenResearch ? measuredTreeHeight : 0f;

        private static int RowsFor(float treeHeight, float yStep) =>
            treeHeight <= 0f ? 0 : Mathf.FloorToInt((treeHeight - CardHeight) / (yStep * PixelsPerY)) + 1;

        /// <summary>Projects per column that fit the full screen tree the layout is being built for; 0 outside full screen.</summary>
        public static int FullScreenRows(float yStep) => RowsFor(layoutTreeHeight, yStep);

        /// <summary>Called as a layout pass starts: it is built for the current full screen tree height.</summary>
        private static void CaptureLayoutTreeHeight() => layoutTreeHeight = WantedLayoutTreeHeight;

        private static int EffectiveRows(float treeHeight) => Mathf.Max(GlobalMaxNodesPerColumn, RowsFor(treeHeight, GlobalYStep));

        /// <summary>
        /// A white full-screen icon, tinted when drawn: four corner brackets pointing outwards to enter,
        /// or inwards to exit. Each pixel is 4x4 supersampled; coordinates are folded into the top-left quadrant.
        /// </summary>
        private static Texture2D BuildFullScreenTexture(int size, bool exit)
        {
            const float margin = 0.1f, arm = 0.3f, thickness = 0.1f;
            return BuildIconTexture(size, (x, y) =>
            {
                float fx = Mathf.Min(x, 1f - x), fy = Mathf.Min(y, 1f - y);
                if (fx < margin || fy < margin || fx > margin + arm || fy > margin + arm) return false;
                // The bracket's corner sits at the outer edge to enter, and at the inner end of its arms to exit.
                float edge = exit ? margin + arm - thickness : margin;
                return (fy >= edge && fy <= edge + thickness) || (fx >= edge && fx <= edge + thickness);
            });
        }

        /// <summary>A white panel icon, tinted when drawn: a window outline with a filled left-hand column.</summary>
        private static Texture2D BuildPanelTexture(int size)
        {
            const float margin = 0.12f, thickness = 0.08f, column = 0.4f;
            return BuildIconTexture(size, (x, y) =>
            {
                if (x < margin || y < margin || x > 1f - margin || y > 1f - margin) return false;
                return x <= column || x >= 1f - margin - thickness || y <= margin + thickness || y >= 1f - margin - thickness;
            });
        }

        /// <summary>Renders a shape test into a white texture with 4x4 supersampled alpha; y runs down the screen.</summary>
        private static Texture2D BuildIconTexture(int size, System.Func<float, float, bool> inside)
        {
            const int samples = 4;
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
                            if (inside((column + (sx + 0.5f) / samples) / size, (row + (sy + 0.5f) / samples) / size)) hits++;
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

        public static float LeftPanelWidth(float minimum, float share)
        {
            normalLeftPanelWidth = Mathf.Max(minimum, share);
            return ResearchOrganizedMod.settings.fullScreenResearch ? FullScreenStripWidth : normalLeftPanelWidth;
        }

        /// <summary>DrawLeftRect prefix: in full screen the panel is drawn only as the overlay; postfixes still draw their buttons in the strip.</summary>
        public static bool SkipLeftPanelInFullScreen() => !ResearchOrganizedMod.settings.fullScreenResearch || drawingFullScreenPanel;

        /// <summary>InitialSize postfix: full screen takes the whole height above the bottom bar.</summary>
        public static void FullScreenInitialSize(ref Vector2 __result)
        {
            if (ResearchOrganizedMod.settings.fullScreenResearch) __result.y = UI.screenHeight - 35;
        }

        private static Rect FullScreenPanelRect(Rect inRect) => new Rect(0f, 0f, normalLeftPanelWidth, inRect.height);

        /// <summary>
        /// DoWindowContents prefix: resizes the window when full screen was toggled, redoes the layout when the
        /// rows that fit have changed, and keeps mouse events over the open panel overlay from reaching the tree.
        /// </summary>
        public static void ApplyFullScreenSize(MainTabWindow_Research __instance, Rect inRect)
        {
            bool fullScreen = ResearchOrganizedMod.settings.fullScreenResearch;
            resizedThisFrame = fullScreen != fullScreenApplied;
            if (resizedThisFrame)
            {
                fullScreenApplied = fullScreen;
                fullScreenPanelOpen = false;
                windowSetInitialSizeMethod?.Invoke(__instance, null);
            }
            else if (Event.current.type == EventType.Layout && EffectiveRows(WantedLayoutTreeHeight) != EffectiveRows(layoutTreeHeight))
            {
                OrganizeTabsAndLayout();
            }

            projectCardClicked = false;
            var current = Event.current;
            if (fullScreen && fullScreenPanelOpen && (current.isMouse || current.type == EventType.ScrollWheel)
                && FullScreenPanelRect(inRect).Contains(current.mousePosition))
            {
                blockedEventType = current.type;
                current.type = EventType.Ignore;
            }
        }

        /// <summary>DrawRightRect prefix: measures the height left for projects below the tab strip in full screen.</summary>
        public static void MeasureTreeHeight(MainTabWindow_Research __instance, Rect rightOutRect, float maxTabX)
        {
            if (!ResearchOrganizedMod.settings.fullScreenResearch || resizedThisFrame || Event.current.type != EventType.Layout) return;
            if (!(researchWindowTabsField?.GetValue(__instance) is System.Collections.IEnumerable tabs)) return;
            measureTabs.Clear();
            foreach (TabRecord tab in tabs) measureTabs.Add(tab);
            Rect tabRect = rightOutRect;
            tabRect.xMax = maxTabX;
            measuredTreeHeight = rightOutRect.height - TabDrawer.GetOverflowTabHeight(tabRect, measureTabs, 100f, 200f) - TreeViewPadding;
        }

        /// <summary>Card draw hook: a clicked project card opens the panel overlay in full screen.</summary>
        private static void NotifyProjectCardClicked() => projectCardClicked = true;

        /// <summary>DoWindowContents postfix: draws the vanilla project panel over the left of the tree while it is open in full screen.</summary>
        public static void DrawFullScreenPanel(MainTabWindow_Research __instance, Rect inRect)
        {
            if (blockedEventType.HasValue)
            {
                Event.current.type = blockedEventType.Value;
                blockedEventType = null;
            }
            if (!ResearchOrganizedMod.settings.fullScreenResearch) return;
            if (projectCardClicked) fullScreenPanelOpen = true;
            if (!fullScreenPanelOpen || researchWindowDrawLeftRectMethod == null) return;

            Rect panel = FullScreenPanelRect(inRect);
            Widgets.DrawWindowBackground(new Rect(panel.x, panel.y, panel.width + 6f, panel.height));
            drawingFullScreenPanel = true;
            try { researchWindowDrawLeftRectMethod.Invoke(__instance, new object[] { panel }); }
            finally { drawingFullScreenPanel = false; }
        }
    }
}
