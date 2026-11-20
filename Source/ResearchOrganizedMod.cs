using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ResearchOrganized
{
    public class ResearchOrganizedMod : Mod
    {
        public static ResearchOrganizedSettings settings;

        /// <summary>This mod, for UI Rethemed - Core to look up its palette.</summary>
        public static ResearchOrganizedMod Instance;

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

        public ResearchOrganizedMod(ModContentPack content) : base(content)
        {
            Instance = this;
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

        /// <summary>Sections by column, built on first draw; each body draws into either page's listing.</summary>
        private List<SkinSettingsSection> leftSections;
        private List<SkinSettingsSection> rightSections;

        private void BuildSections()
        {
            if (leftSections != null) return;
            leftSections = new List<SkinSettingsSection>
            {
                new SkinSettingsSection("ResearchWindow", T("ResearchOrganized_SectionResearchWindow"), DrawResearchWindowSection),
                new SkinSettingsSection("Tabs", T("ResearchOrganized_SectionTabs"), DrawTabsSection)
            };
            rightSections = new List<SkinSettingsSection>
            {
                new SkinSettingsSection("Colors", T("ResearchOrganized_SectionColors"), DrawColorsSection),
                new SkinSettingsSection("Layout", T("ResearchOrganized_SectionLayout"), DrawLayoutSection)
            };
        }

        /// <summary>Two columns: research window and tabs on the left, colors and layout on the right, with the reset button below.</summary>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            BuildSections();
            Rect outRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height - ResetButtonHeight - 10f);

            // With UI Rethemed, the sections are Core's settings cards.
            var skin = ResearchSkin.Active;
            if (skin == null || !skin.SettingsPage(outRect, leftSections, rightSections))
            {
                Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, settingsHeight);
                float columnWidth = (viewRect.width - ColumnGap) / 2f;

                Widgets.BeginScrollView(outRect, ref settingsScroll, viewRect);

                var left = new Listing_Standard();
                left.Begin(new Rect(0f, 0f, columnWidth, viewRect.height));
                DrawColumn(left, leftSections);
                float leftHeight = left.CurHeight;
                left.End();

                var right = new Listing_Standard();
                right.Begin(new Rect(columnWidth + ColumnGap, 0f, columnWidth, viewRect.height));
                DrawColumn(right, rightSections);
                float rightHeight = right.CurHeight;
                right.End();

                settingsHeight = Mathf.Max(leftHeight, rightHeight) + 10f;
                Widgets.EndScrollView();
            }

            Rect resetRect = new Rect(inRect.x, inRect.yMax - ResetButtonHeight, 200f, ResetButtonHeight);
            string resetLabel = T("ResearchOrganized_ResetDefaults");
            skin = ResearchSkin.Active;
            bool reset = skin != null && skin.Button(resetRect, resetLabel, SkinTone.Gray, true)
                ? Widgets.ButtonInvisible(resetRect)
                : Widgets.ButtonText(resetRect, resetLabel);
            if (reset)
            {
                settings.CopyFrom(new ResearchOrganizedSettings());
                minorThresholdBuffer = settings.minorAnchorChildThreshold.ToString();
                majorThresholdBuffer = settings.majorAnchorChildThreshold.ToString();
            }

            base.DoSettingsWindowContents(inRect);

            ResearchOrganizedMain.RefreshColors();
        }

        private void DrawColumn(Listing_Standard listing, List<SkinSettingsSection> sections)
        {
            foreach (var section in sections)
            {
                SectionHeader(listing, section.title);
                section.body(listing);
            }
        }

        private void DrawResearchWindowSection(Listing_Standard listing)
        {
            // Only with UI Rethemed - Core, which loads the add-on that supplies the look.
            Checkbox(listing, "ResearchOrganized_MatchUIRethemed", "ResearchOrganized_MatchUIRethemedTip", ref settings.matchUIRethemed,
                disabled: !ResearchSkin.Available, disabledTipKey: "ResearchOrganized_MatchUIRethemedLockedTip");
            Checkbox(listing, "ResearchOrganized_FullScreen", "ResearchOrganized_FullScreenTip", ref settings.fullScreenResearch);
            Checkbox(listing, "ResearchOrganized_HideUnavailable", "ResearchOrganized_HideUnavailableTip", ref settings.hideUnavailableProjects);
            Checkbox(listing, "ResearchOrganized_HideFutureTechLevels", "ResearchOrganized_HideFutureTechLevelsTip", ref settings.hideFutureTechLevels);
            Checkbox(listing, "ResearchOrganized_ShowTabCounts", "ResearchOrganized_ShowTabCountsTip", ref settings.showTabProjectCounts);
            Checkbox(listing, "ResearchOrganized_SuppressEmergenceLines", "ResearchOrganized_SuppressEmergenceLinesTip", ref settings.suppressEmergenceLines);
        }

        private void DrawTabsSection(Listing_Standard listing)
        {
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

            bool useColors = !settings.disableCustomColors;
            Checkbox(listing, "ResearchOrganized_ColorCards", "ResearchOrganized_ColorCardsTip", ref useColors);
            settings.disableCustomColors = !useColors;

            // With the theme on, card colors are picked on UI Rethemed - Core's settings page instead.
            string paletteLockKey = !useColors ? "ResearchOrganized_RequiresColorCards"
                : ResearchSkin.Active != null ? "ResearchOrganized_ColorsOnCoreTip" : null;
            if (ResearchSkin.Active != null && useColors) SubNote(listing, "ResearchOrganized_ColorsOnCoreTip");
            DrawColorPalette(listing, "ResearchOrganized_ColorUndefined", ref settings.colorUndefined, paletteLockKey);
            DrawColorPalette(listing, "ResearchOrganized_ColorAnimal", ref settings.colorAnimal, paletteLockKey);
            DrawColorPalette(listing, "ResearchOrganized_ColorNeolithic", ref settings.colorNeolithic, paletteLockKey);
            DrawColorPalette(listing, "ResearchOrganized_ColorMedieval", ref settings.colorMedieval, paletteLockKey);
            DrawColorPalette(listing, "ResearchOrganized_ColorIndustrial", ref settings.colorIndustrial, paletteLockKey);
            DrawColorPalette(listing, "ResearchOrganized_ColorSpacer", ref settings.colorSpacer, paletteLockKey);
            DrawColorPalette(listing, "ResearchOrganized_ColorUltra", ref settings.colorUltra, paletteLockKey);
            DrawColorPalette(listing, "ResearchOrganized_ColorArchotech", ref settings.colorArchotech, paletteLockKey);
            DrawColorPalette(listing, "ResearchOrganized_ColorAnomaly", ref settings.colorAnomaly, paletteLockKey);
        }

        private void DrawLayoutSection(Listing_Standard listing)
        {
            NumericField(listing, "ResearchOrganized_MinorAnchor", "ResearchOrganized_MinorAnchorTip", ref settings.minorAnchorChildThreshold, ref minorThresholdBuffer, 1f);
            NumericField(listing, "ResearchOrganized_MajorAnchor", "ResearchOrganized_MajorAnchorTip", ref settings.majorAnchorChildThreshold, ref majorThresholdBuffer, 2f);
            Checkbox(listing, "ResearchOrganized_WriteLayoutLog", "ResearchOrganized_WriteLayoutLogTip", ref settings.writeLayoutLog);
        }

        private string T(string key)
        {
            return TranslationCache.Get(key);
        }

        private static void SectionHeader(Listing_Standard listing, string title)
        {
            if (listing.CurHeight > 0f) listing.GapLine();
            Text.Font = GameFont.Medium;
            listing.Label(title);
            Text.Font = GameFont.Small;
            listing.Gap(4f);
        }

        /// <summary>A checkbox row with a tooltip; a disabled row is greyed and inert, with <paramref name="disabledTipKey"/> as its tooltip.</summary>
        private void Checkbox(Listing_Standard listing, string key, string tipKey, ref bool value, float indent = 0f,
            bool disabled = false, string disabledTipKey = null)
        {
            string tip = disabled && disabledTipKey != null ? T(disabledTipKey) : T(tipKey);
            var skin = ResearchSkin.Active;
            if (skin != null && skin.Tick(listing, T(key), ref value, tip, !disabled)) return;

            Rect rect = listing.GetRect(RowHeight);
            rect.xMin += indent;
            if (!disabled && Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);
            if (disabled) GUI.color = Color.gray;
            Widgets.CheckboxLabeled(rect, T(key), ref value, disabled);
            GUI.color = Color.white;
            TooltipHandler.TipRegion(rect, tip);
            listing.Gap(listing.verticalSpacing);
        }

        /// <summary>A dimmed line of explanation under a section's first row.</summary>
        private void SubNote(Listing_Standard listing, string key)
        {
            GUI.color = Color.gray;
            listing.Label(T(key));
            GUI.color = Color.white;
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

        /// <summary>A row of color swatches; with <paramref name="lockedTipKey"/> it is greyed and inert, with that key as its tooltip.</summary>
        private void DrawColorPalette(Listing_Standard listing, string key, ref Color selectedColor, string lockedTipKey)
        {
            Rect rect = listing.GetRect(RowHeight);
            bool enabled = lockedTipKey == null;
            if (!enabled)
            {
                GUI.color = Color.gray;
                TooltipHandler.TipRegion(rect, T(lockedTipKey));
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
