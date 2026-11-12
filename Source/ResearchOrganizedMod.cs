using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ResearchOrganized
{
    public class ResearchOrganizedMod : Mod
    {
        public static ResearchOrganizedSettings settings;

        private static readonly Color[] Palette = new Color[]
        {
            new Color(0.40f, 0.00f, 0.00f), new Color(0.80f, 0.30f, 0.30f), new Color(0.80f, 0.50f, 0.10f), new Color(0.45f, 0.33f, 0.24f),
            new Color(0.80f, 0.80f, 0.10f), new Color(0.40f, 0.40f, 0.00f), new Color(0.00f, 0.40f, 0.00f), new Color(0.30f, 0.70f, 0.30f),
            new Color(0.00f, 0.40f, 0.40f), new Color(0.30f, 0.80f, 0.80f), new Color(0.00f, 0.00f, 0.40f), new Color(0.30f, 0.30f, 0.80f),
            new Color(0.30f, 0.00f, 0.30f), new Color(0.60f, 0.30f, 0.60f), new Color(0.30f, 0.30f, 0.30f), new Color(0.60f, 0.60f, 0.60f)
        };

        private const float RowHeight = 24f;
        private const float ColumnGap = 24f;
        private const float SubOptionIndent = 24f;
        private const float ColorLabelWidth = 120f;
        private const float ResetButtonHeight = 32f;

        /// <summary>Values the layout was actually built with, so we know when to redo it.</summary>
        private bool appliedCombineAllTabs;
        private bool appliedCombineIndustrial;
        private bool appliedSeparateEras;
        private int appliedMinorAnchorThreshold;
        private int appliedMajorAnchorThreshold;
        private bool appliedWriteLayoutLog;

        private string minorThresholdBuffer;
        private string majorThresholdBuffer;

        private Vector2 settingsScroll;
        private float settingsHeight = 1000f;

        /// <summary>Translated settings text by key; cleared when the language changes.</summary>
        private readonly Dictionary<string, string> text = new Dictionary<string, string>();
        private LoadedLanguage textLanguage;

        public ResearchOrganizedMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<ResearchOrganizedSettings>();
            appliedCombineAllTabs = settings.combineAllTabs;
            appliedCombineIndustrial = settings.combineIndustrial;
            appliedSeparateEras = settings.separateEras;
            appliedMinorAnchorThreshold = settings.minorAnchorChildThreshold;
            appliedMajorAnchorThreshold = settings.majorAnchorChildThreshold;
            appliedWriteLayoutLog = settings.writeLayoutLog;
            minorThresholdBuffer = settings.minorAnchorChildThreshold.ToString();
            majorThresholdBuffer = settings.majorAnchorChildThreshold.ToString();
        }

        public override string SettingsCategory() => "Research: Organized";

        /// <summary>Saves, refreshes colors and the visibility filter, and rebuilds the layout when a structural setting changed.</summary>
        public override void WriteSettings()
        {
            bool structureChanged = settings.combineAllTabs != appliedCombineAllTabs
                                 || settings.combineIndustrial != appliedCombineIndustrial
                                 || settings.separateEras != appliedSeparateEras
                                 || settings.minorAnchorChildThreshold != appliedMinorAnchorThreshold
                                 || settings.majorAnchorChildThreshold != appliedMajorAnchorThreshold
                                 || (settings.writeLayoutLog && !appliedWriteLayoutLog);

            base.WriteSettings();
            ResearchOrganizedMain.RefreshColors();
            UnavailableProjectFilter.Invalidate();

            if (structureChanged)
            {
                appliedCombineAllTabs = settings.combineAllTabs;
                appliedCombineIndustrial = settings.combineIndustrial;
                appliedSeparateEras = settings.separateEras;
                appliedMinorAnchorThreshold = settings.minorAnchorChildThreshold;
                appliedMajorAnchorThreshold = settings.majorAnchorChildThreshold;
                ResearchOrganizedMain.OrganizeTabsAndLayout();
            }
            appliedWriteLayoutLog = settings.writeLayoutLog;
        }

        /// <summary>Two columns: research window and tabs on the left, colors and layout on the right, with the reset button below.</summary>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            if (textLanguage != LanguageDatabase.activeLanguage)
            {
                text.Clear();
                textLanguage = LanguageDatabase.activeLanguage;
            }

            Rect outRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height - ResetButtonHeight - 10f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, settingsHeight);
            float columnWidth = (viewRect.width - ColumnGap) / 2f;

            Widgets.BeginScrollView(outRect, ref settingsScroll, viewRect);

            var left = new Listing_Standard();
            left.Begin(new Rect(0f, 0f, columnWidth, viewRect.height));
            DrawResearchWindowSection(left);
            DrawTabsSection(left);
            float leftHeight = left.CurHeight;
            left.End();

            var right = new Listing_Standard();
            right.Begin(new Rect(columnWidth + ColumnGap, 0f, columnWidth, viewRect.height));
            DrawColorsSection(right);
            DrawLayoutSection(right);
            float rightHeight = right.CurHeight;
            right.End();

            settingsHeight = Mathf.Max(leftHeight, rightHeight) + 10f;
            Widgets.EndScrollView();

            var footer = new Listing_Standard();
            footer.Begin(new Rect(inRect.x, inRect.yMax - ResetButtonHeight, 200f, ResetButtonHeight));
            if (footer.ButtonText(T("ResearchOrganized_ResetDefaults")))
            {
                settings.CopyFrom(new ResearchOrganizedSettings());
                minorThresholdBuffer = settings.minorAnchorChildThreshold.ToString();
                majorThresholdBuffer = settings.majorAnchorChildThreshold.ToString();
            }
            footer.End();

            base.DoSettingsWindowContents(inRect);

            ResearchOrganizedMain.RefreshColors();
        }

        private void DrawResearchWindowSection(Listing_Standard listing)
        {
            SectionHeader(listing, "ResearchOrganized_SectionResearchWindow");
            Checkbox(listing, "ResearchOrganized_FullScreen", "ResearchOrganized_FullScreenTip", ref settings.fullScreenResearch);
            Checkbox(listing, "ResearchOrganized_HideUnavailable", "ResearchOrganized_HideUnavailableTip", ref settings.hideUnavailableProjects);
            Checkbox(listing, "ResearchOrganized_HideFutureTechLevels", "ResearchOrganized_HideFutureTechLevelsTip", ref settings.hideFutureTechLevels);
            Checkbox(listing, "ResearchOrganized_ShowTabCounts", "ResearchOrganized_ShowTabCountsTip", ref settings.showTabProjectCounts);
            Checkbox(listing, "ResearchOrganized_SuppressEmergenceLines", "ResearchOrganized_SuppressEmergenceLinesTip", ref settings.suppressEmergenceLines);
        }

        private void DrawTabsSection(Listing_Standard listing)
        {
            SectionHeader(listing, "ResearchOrganized_SectionTabs");

            // Combine All Tabs includes Combine Industrial, so it shows ticked and locked while that is on.
            bool combineAll = settings.combineAllTabs;
            if (combineAll)
            {
                bool included = true;
                Checkbox(listing, "ResearchOrganized_CombineIndustrial", "ResearchOrganized_CombineIndustrialTip", ref included, disabled: true,
                    disabledTipKey: "ResearchOrganized_CombineIndustrialIncludedTip");
            }
            else
            {
                Checkbox(listing, "ResearchOrganized_CombineIndustrial", "ResearchOrganized_CombineIndustrialTip", ref settings.combineIndustrial);
            }

            Checkbox(listing, "ResearchOrganized_CombineAllTabs", "ResearchOrganized_CombineAllTabsTip", ref settings.combineAllTabs);
            Checkbox(listing, "ResearchOrganized_SeparateEras", "ResearchOrganized_SeparateErasTip", ref settings.separateEras, SubOptionIndent,
                !combineAll, "ResearchOrganized_RequiresCombineAllTabs");
            Checkbox(listing, "ResearchOrganized_CrossEraLines", "ResearchOrganized_CrossEraLinesTip", ref settings.crossEraLines, SubOptionIndent,
                !combineAll, "ResearchOrganized_RequiresCombineAllTabs");
        }

        private void DrawColorsSection(Listing_Standard listing)
        {
            SectionHeader(listing, "ResearchOrganized_SectionColors");

            bool useColors = !settings.disableCustomColors;
            Checkbox(listing, "ResearchOrganized_ColorCards", "ResearchOrganized_ColorCardsTip", ref useColors);
            settings.disableCustomColors = !useColors;

            DrawColorPalette(listing, "ResearchOrganized_ColorUndefined", ref settings.colorUndefined, useColors);
            DrawColorPalette(listing, "ResearchOrganized_ColorAnimal", ref settings.colorAnimal, useColors);
            DrawColorPalette(listing, "ResearchOrganized_ColorNeolithic", ref settings.colorNeolithic, useColors);
            DrawColorPalette(listing, "ResearchOrganized_ColorMedieval", ref settings.colorMedieval, useColors);
            DrawColorPalette(listing, "ResearchOrganized_ColorIndustrial", ref settings.colorIndustrial, useColors);
            DrawColorPalette(listing, "ResearchOrganized_ColorSpacer", ref settings.colorSpacer, useColors);
            DrawColorPalette(listing, "ResearchOrganized_ColorUltra", ref settings.colorUltra, useColors);
            DrawColorPalette(listing, "ResearchOrganized_ColorArchotech", ref settings.colorArchotech, useColors);
            DrawColorPalette(listing, "ResearchOrganized_ColorAnomaly", ref settings.colorAnomaly, useColors);
        }

        private void DrawLayoutSection(Listing_Standard listing)
        {
            SectionHeader(listing, "ResearchOrganized_SectionLayout");
            NumericField(listing, "ResearchOrganized_MinorAnchor", "ResearchOrganized_MinorAnchorTip", ref settings.minorAnchorChildThreshold, ref minorThresholdBuffer, 1f);
            NumericField(listing, "ResearchOrganized_MajorAnchor", "ResearchOrganized_MajorAnchorTip", ref settings.majorAnchorChildThreshold, ref majorThresholdBuffer, 2f);
            Checkbox(listing, "ResearchOrganized_WriteLayoutLog", "ResearchOrganized_WriteLayoutLogTip", ref settings.writeLayoutLog);
        }

        private string T(string key)
        {
            if (!text.TryGetValue(key, out string value)) text[key] = value = key.Translate();
            return value;
        }

        private void SectionHeader(Listing_Standard listing, string key)
        {
            if (listing.CurHeight > 0f) listing.GapLine();
            Text.Font = GameFont.Medium;
            listing.Label(T(key));
            Text.Font = GameFont.Small;
            listing.Gap(4f);
        }

        /// <summary>A checkbox row with a tooltip; a disabled row is greyed and inert, with <paramref name="disabledTipKey"/> as its tooltip.</summary>
        private void Checkbox(Listing_Standard listing, string key, string tipKey, ref bool value, float indent = 0f,
            bool disabled = false, string disabledTipKey = null)
        {
            Rect rect = listing.GetRect(RowHeight);
            rect.xMin += indent;
            if (!disabled && Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);
            if (disabled) GUI.color = Color.gray;
            Widgets.CheckboxLabeled(rect, T(key), ref value, disabled);
            GUI.color = Color.white;
            TooltipHandler.TipRegion(rect, disabled && disabledTipKey != null ? T(disabledTipKey) : T(tipKey));
            listing.Gap(listing.verticalSpacing);
        }

        private void NumericField(Listing_Standard listing, string key, string tipKey, ref int value, ref string buffer, float min)
        {
            Rect rect = listing.GetRect(RowHeight);
            if (Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);
            Widgets.Label(new Rect(rect.x, rect.y, rect.width - 60f, rect.height), T(key));
            Widgets.TextFieldNumeric(new Rect(rect.xMax - 50f, rect.y, 50f, rect.height), ref value, ref buffer, min, 99f);
            TooltipHandler.TipRegion(rect, T(tipKey));
            listing.Gap(listing.verticalSpacing);
        }

        private void DrawColorPalette(Listing_Standard listing, string key, ref Color selectedColor, bool enabled)
        {
            Rect rect = listing.GetRect(RowHeight);
            if (!enabled)
            {
                GUI.color = Color.gray;
                TooltipHandler.TipRegion(rect, T("ResearchOrganized_RequiresColorCards"));
            }
            Widgets.Label(new Rect(rect.x, rect.y + 2f, ColorLabelWidth, rect.height - 2f), T(key));

            // Swatches share the space right of the label, up to 24 pixels apart.
            float step = Mathf.Min(24f, (rect.width - ColorLabelWidth) / Palette.Length);
            float size = Mathf.Min(step - 2f, rect.height - 2f);
            float curX = rect.x + ColorLabelWidth;
            foreach (Color color in Palette)
            {
                Rect swatchRect = new Rect(curX, rect.y + (rect.height - size) / 2f, size, size);
                Widgets.DrawBoxSolid(swatchRect, enabled ? color : Color.Lerp(color, Color.gray, 0.6f));

                if (ColorsMatch(color, selectedColor)) Widgets.DrawBox(swatchRect, 2);

                if (enabled && Widgets.ButtonInvisible(swatchRect)) selectedColor = color;

                curX += step;
            }
            GUI.color = Color.white;
            listing.Gap(2f);
        }

        private static bool ColorsMatch(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.01f &&
                   Mathf.Abs(a.g - b.g) < 0.01f &&
                   Mathf.Abs(a.b - b.b) < 0.01f;
        }
    }
}
