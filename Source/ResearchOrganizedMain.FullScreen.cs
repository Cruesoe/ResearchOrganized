using System.Collections.Generic;
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
        private static readonly MethodInfo windowSetInitialSizeMethod = AccessTools.Method(typeof(Window), "SetInitialSizeAndPosition");
        private static readonly List<TabRecord> measureTabs = new List<TabRecord>();

        // Vanilla CoordToPixelsY: pixels per unit of researchViewY.
        private const float PixelsPerY = 100f;
        private const float CardHeight = 50f;
        // Below the tab strip: 16 for the horizontal scrollbar and 20 of padding above and below the projects.
        private const float TreeViewPadding = 56f;

        private static bool fullScreenApplied;
        private static bool resizedThisFrame;

        /// <summary>Height the full screen tree area had when last measured; 0 until the window has drawn in full screen.</summary>
        private static float measuredTreeHeight;

        /// <summary>The tree height the current layout was built for; 0 for the normal layout.</summary>
        private static float layoutTreeHeight;

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

        /// <summary>InitialSize postfix: full screen takes the whole height above the bottom bar.</summary>
        public static void FullScreenInitialSize(ref Vector2 __result)
        {
            if (ResearchOrganizedMod.settings.fullScreenResearch) __result.y = UI.screenHeight - 35;
        }

        /// <summary>DoWindowContents prefix: resizes the window when full screen was toggled, and redoes the layout when the rows that fit have changed.</summary>
        public static void ApplyFullScreenSize(MainTabWindow_Research __instance)
        {
            resizedThisFrame = false;
            if (Event.current.type != EventType.Layout) return;
            bool fullScreen = ResearchOrganizedMod.settings.fullScreenResearch;
            if (fullScreen != fullScreenApplied)
            {
                // Leaving full screen lays the tree out for normal view first, since the window's height comes from the tallest tree.
                if (!fullScreen && EffectiveRows(0f) != EffectiveRows(layoutTreeHeight)) OrganizeTabsAndLayout();
                fullScreenApplied = fullScreen;
                resizedThisFrame = true;
                windowSetInitialSizeMethod?.Invoke(__instance, null);
            }
            else if (EffectiveRows(WantedLayoutTreeHeight) != EffectiveRows(layoutTreeHeight))
            {
                OrganizeTabsAndLayout();
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
    }
}
