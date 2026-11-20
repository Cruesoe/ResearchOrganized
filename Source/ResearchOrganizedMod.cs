using System;
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

        /// <summary>Counts up on every settings save, so the research window knows to rebuild what depends on them.</summary>
        public static int SettingsVersion { get; private set; }

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
        private const string RequiresRethemedKey = "ResearchOrganized_RequiresRethemed";

        /// <summary>Values the layout was actually built with, so we know when to redo it.</summary>
        private bool appliedCombineAllTabs;
        private bool appliedCombineIndustrial;
        private bool appliedSeparateEras;
        private int appliedMinorAnchorThreshold;
        private int appliedMajorAnchorThreshold;
        private int appliedMaxPerColumn;
        private bool appliedRethemed;
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
            appliedMaxPerColumn = settings.maxPerColumn;
            appliedWriteLayoutLog = settings.writeLayoutLog;
            minorThresholdBuffer = settings.minorAnchorChildThreshold.ToString();
            majorThresholdBuffer = settings.majorAnchorChildThreshold.ToString();
        }

        public override string SettingsCategory() => "Research: Organized";

        /// <summary>Saves a setting changed from the research window and applies it at once, as closing the settings page does.</summary>
        public static void SaveAndApply() => Instance.WriteSettings();

        /// <summary>Saves, refreshes colors, the visibility filter and the Research button's window, and rebuilds the layout when a structural setting changed.</summary>
        public override void WriteSettings()
        {
            bool rethemed = DisplayMode.RethemedActive;
            bool structureChanged = settings.combineAllTabs != appliedCombineAllTabs
                                 || settings.combineIndustrial != appliedCombineIndustrial
                                 || settings.separateEras != appliedSeparateEras
                                 || settings.minorAnchorChildThreshold != appliedMinorAnchorThreshold
                                 || settings.majorAnchorChildThreshold != appliedMajorAnchorThreshold
                                 || (rethemed && settings.maxPerColumn != appliedMaxPerColumn)
                                 || rethemed != appliedRethemed
                                 || (settings.writeLayoutLog && !appliedWriteLayoutLog);

            base.WriteSettings();
            SettingsVersion++;
            DisplayMode.Apply();
            ResearchOrganizedMain.RefreshColors();
            UnavailableProjectFilter.Invalidate();

            if (structureChanged)
            {
                appliedCombineAllTabs = settings.combineAllTabs;
                appliedCombineIndustrial = settings.combineIndustrial;
                appliedSeparateEras = settings.separateEras;
                appliedMinorAnchorThreshold = settings.minorAnchorChildThreshold;
                appliedMajorAnchorThreshold = settings.majorAnchorChildThreshold;
                appliedMaxPerColumn = settings.maxPerColumn;
                appliedRethemed = rethemed;
                ResearchOrganizedMain.OrganizeTabsAndLayout();
            }
            appliedWriteLayoutLog = settings.writeLayoutLog;
        }

        /// <summary>The window the current layout was built for, set once the first layout pass has run.</summary>
        internal void LayoutBuilt() => appliedRethemed = DisplayMode.RethemedActive;

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
                new SkinSettingsSection("Cards", T("ResearchOrganized_SectionCards"), DrawCardsSection),
                new SkinSettingsSection("Colors", T("ResearchOrganized_SectionColors"), DrawColorsSection),
                new SkinSettingsSection("Layout", T("ResearchOrganized_SectionLayout"), DrawLayoutSection)
            };
        }

        /// <summary>Two columns: research window and tabs on the left, cards, colors and layout on the right, with the reset button below.</summary>
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

        private static readonly string[] DisplayModeKeys = { "ResearchOrganized_DisplayModeClassic", "ResearchOrganized_DisplayModeRethemed" };

        private static string DisplayModeLabel(int mode) => TranslationCache.Get(DisplayModeKeys[Mathf.Clamp(mode, 0, DisplayModeKeys.Length - 1)]);

        /// <summary>The chosen window; Rethemed counts as Classic while UI Rethemed - Core is missing.</summary>
        private static bool Rethemed => DisplayMode.RethemedActive;

        /// <summary>Opens a menu of the display modes; Rethemed is greyed out without UI Rethemed - Core.</summary>
        private static void ChooseDisplayMode()
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption(DisplayModeLabel(DisplayMode.Classic), () => settings.displayMode = DisplayMode.Classic)
            };
            if (DisplayMode.RethemedAvailable)
                options.Add(new FloatMenuOption(DisplayModeLabel(DisplayMode.Rethemed), () => settings.displayMode = DisplayMode.Rethemed));
            else
                options.Add(new FloatMenuOption(DisplayModeLabel(DisplayMode.Rethemed) + " " + TranslationCache.Get("ResearchOrganized_RequiresCore"), null));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void DrawResearchWindowSection(Listing_Standard listing)
        {
            ChoiceRow(listing, "ResearchOrganized_DisplayMode", DisplayModeLabel(Rethemed ? DisplayMode.Rethemed : DisplayMode.Classic),
                DisplayMode.RethemedAvailable ? "ResearchOrganized_DisplayModeTip" : "ResearchOrganized_DisplayModeNoCoreTip", true, ChooseDisplayMode);

            // Each window keeps its own full screen choice.
            if (Rethemed) Checkbox(listing, "ResearchOrganized_FullScreen", "ResearchOrganized_FullScreenTip", ref settings.rethemedFullScreen);
            else Checkbox(listing, "ResearchOrganized_FullScreen", "ResearchOrganized_FullScreenTip", ref settings.fullScreenResearch);

            Checkbox(listing, "ResearchOrganized_HideUnavailable", "ResearchOrganized_HideUnavailableTip", ref settings.hideUnavailableProjects);
            Checkbox(listing, "ResearchOrganized_HideFutureTechLevels", "ResearchOrganized_HideFutureTechLevelsTip", ref settings.hideFutureTechLevels);
            Checkbox(listing, "ResearchOrganized_HideFinished", "ResearchOrganized_HideFinishedTip", ref settings.hideFinishedProjects);
            Checkbox(listing, "ResearchOrganized_ShowTabCounts", "ResearchOrganized_ShowTabCountsTip", ref settings.showTabProjectCounts);
            Checkbox(listing, "ResearchOrganized_SuppressEmergenceLines", "ResearchOrganized_SuppressEmergenceLinesTip", ref settings.suppressEmergenceLines);
            Checkbox(listing, "ResearchOrganized_OpenOnCurrent", "ResearchOrganized_OpenOnCurrentTip", ref settings.openOnCurrentProject, disabled: !Rethemed, disabledTipKey: RequiresRethemedKey);
            Checkbox(listing, "ResearchOrganized_CurvedLines", "ResearchOrganized_CurvedLinesTip", ref settings.curvedLines, disabled: !Rethemed, disabledTipKey: RequiresRethemedKey);
            Checkbox(listing, "ResearchOrganized_HighlightChain", "ResearchOrganized_HighlightChainTip", ref settings.highlightChain, disabled: !Rethemed, disabledTipKey: RequiresRethemedKey);
            Checkbox(listing, "ResearchOrganized_EraBands", "ResearchOrganized_EraBandsTip", ref settings.eraBands, disabled: !Rethemed, disabledTipKey: RequiresRethemedKey);
            Checkbox(listing, "ResearchOrganized_ShowTimeLeft", "ResearchOrganized_ShowTimeLeftTip", ref settings.showTimeLeft, disabled: !Rethemed, disabledTipKey: RequiresRethemedKey);
        }

        /// <summary>Labels of the card sizes, smallest first, as the setting stores them.</summary>
        internal static readonly string[] CardSizeKeys = { "ResearchOrganized_CardSizeCompact", "ResearchOrganized_CardSizeNormal", "ResearchOrganized_CardSizeLarge" };

        internal static string CardSizeLabel(int size) => TranslationCache.Get(CardSizeKeys[Mathf.Clamp(size, 0, CardSizeKeys.Length - 1)]);

        /// <summary>Opens a menu of the card sizes; picking one saves it at once.</summary>
        internal static void ChooseCardSize()
        {
            var options = new List<FloatMenuOption>();
            for (int i = 0; i < CardSizeKeys.Length; i++)
            {
                int size = i;
                options.Add(new FloatMenuOption(CardSizeLabel(size), () =>
                {
                    settings.cardSize = size;
                    SaveAndApply();
                }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void DrawCardsSection(Listing_Standard listing)
        {
            bool rethemed = Rethemed;
            ChoiceRow(listing, "ResearchOrganized_CardSize", CardSizeLabel(settings.cardSize), rethemed ? "ResearchOrganized_CardSizeTip" : RequiresRethemedKey, rethemed, ChooseCardSize);
            Slider(listing, "ResearchOrganized_ColumnSpacing", "ResearchOrganized_ColumnSpacingTip", ref settings.columnSpacing, 0.5f, 3f, 0.05f, 1f, PercentText, rethemed);
            Slider(listing, "ResearchOrganized_RowSpacing", "ResearchOrganized_RowSpacingTip", ref settings.rowSpacing, 0.5f, 3f, 0.05f, 1f, PercentText, rethemed);
            Checkbox(listing, "ResearchOrganized_CardUnlockIcons", "ResearchOrganized_CardUnlockIconsTip", ref settings.cardUnlockIcons, disabled: !rethemed, disabledTipKey: RequiresRethemedKey);
            Checkbox(listing, "ResearchOrganized_CardChips", "ResearchOrganized_CardChipsTip", ref settings.cardChips, disabled: !rethemed, disabledTipKey: RequiresRethemedKey);
            Checkbox(listing, "ResearchOrganized_CardProgressStrip", "ResearchOrganized_CardProgressStripTip", ref settings.cardProgressStrip, disabled: !rethemed, disabledTipKey: RequiresRethemedKey);
            Checkbox(listing, "ResearchOrganized_CardHoverCard", "ResearchOrganized_CardHoverCardTip", ref settings.cardHoverCard, disabled: !rethemed, disabledTipKey: RequiresRethemedKey);
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

            // The Rethemed window takes its era colors from Core's palette, picked on Core's settings page.
            if (Rethemed)
            {
                if (useColors) SubNote(listing, "ResearchOrganized_ColorsOnCoreTip");
                return;
            }

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
            NumericField(listing, "ResearchOrganized_MinorAnchor", "ResearchOrganized_MinorAnchorTip", ref settings.minorAnchorChildThreshold, ref minorThresholdBuffer, 1f);
            NumericField(listing, "ResearchOrganized_MajorAnchor", "ResearchOrganized_MajorAnchorTip", ref settings.majorAnchorChildThreshold, ref majorThresholdBuffer, 2f);
            float perColumn = settings.maxPerColumn;
            Slider(listing, "ResearchOrganized_MaxPerColumn", "ResearchOrganized_MaxPerColumnTip", ref perColumn, 6f, 30f, 1f, 16f, WholeText, Rethemed);
            settings.maxPerColumn = Mathf.RoundToInt(perColumn);
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

        /// <summary>A label with a button showing the current choice, which opens <paramref name="choose"/>; greyed and inert while disabled.</summary>
        private void ChoiceRow(Listing_Standard listing, string key, string current, string tipKey, bool enabled, Action choose)
        {
            Rect row = listing.GetRect(RowHeight + 4f);
            Rect button = new Rect(row.xMax - 120f, row.y, 120f, row.height);
            if (!enabled) GUI.color = Color.gray;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(row.x, row.y, row.width - 128f, row.height), T(key));
            Text.Anchor = TextAnchor.UpperLeft;
            var skin = ResearchSkin.Active;
            bool clicked = skin != null && skin.Button(button, current, SkinTone.Gray, enabled)
                ? Widgets.ButtonInvisible(button)
                : Widgets.ButtonText(button, current, true, true, enabled);
            GUI.color = Color.white;
            if (clicked && enabled) choose();
            TooltipHandler.TipRegion(row, T(tipKey));
            listing.Gap(listing.verticalSpacing);
        }

        private static readonly Dictionary<int, string> numberTexts = new Dictionary<int, string>();
        private static readonly Func<float, string> PercentText = value => CachedNumber(Mathf.RoundToInt(value * 100f), "%");
        private static readonly Func<float, string> WholeText = value => CachedNumber(Mathf.RoundToInt(value), "");

        private static string CachedNumber(int value, string suffix)
        {
            int key = suffix.Length > 0 ? value + 100000 : value;
            if (!numberTexts.TryGetValue(key, out string text)) numberTexts[key] = text = value + suffix;
            return text;
        }

        /// <summary>A slider row with a reset button once the value is moved off <paramref name="usual"/>; while disabled, a greyed row showing the value.</summary>
        private void Slider(Listing_Standard listing, string key, string tipKey, ref float value, float min, float max, float step, float usual,
            Func<float, string> text, bool enabled)
        {
            if (!enabled)
            {
                Rect locked = listing.GetRect(RowHeight);
                GUI.color = Color.gray;
                Widgets.Label(locked, T(key));
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(locked, text(value));
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                TooltipHandler.TipRegion(locked, T(RequiresRethemedKey));
                listing.Gap(listing.verticalSpacing);
                return;
            }

            var skin = ResearchSkin.Active;
            if (skin != null && skin.Slider(listing, T(key), T(tipKey), ref value, min, max, step, usual, text, T("ResearchOrganized_ResetValue"))) return;

            Rect rect = listing.GetRect(RowHeight);
            Widgets.Label(rect, T(key));
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(rect, text(value));
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(rect, T(tipKey));
            Rect bar = listing.GetRect(RowHeight);
            value = Widgets.HorizontalSlider(bar, value, min, max, true, null, null, null, step);
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
