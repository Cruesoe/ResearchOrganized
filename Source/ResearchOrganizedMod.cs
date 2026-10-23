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

        /// <summary>Values the layout was actually built with, so we know when to redo it.</summary>
        private bool appliedCombineAllTabs;
        private bool appliedCombineIndustrial;
        private int appliedMinorAnchorThreshold;
        private int appliedMajorAnchorThreshold;
        private bool appliedWriteLayoutLog;

        private string minorThresholdBuffer;
        private string majorThresholdBuffer;

        public ResearchOrganizedMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<ResearchOrganizedSettings>();
            appliedCombineAllTabs = settings.combineAllTabs;
            appliedCombineIndustrial = settings.combineIndustrial;
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
                appliedMinorAnchorThreshold = settings.minorAnchorChildThreshold;
                appliedMajorAnchorThreshold = settings.majorAnchorChildThreshold;
                ResearchOrganizedMain.OrganizeTabsAndLayout();
            }
            appliedWriteLayoutLog = settings.writeLayoutLog;
        }

        private Vector2 settingsScroll;
        private float settingsHeight = 1000f;

        public override void DoSettingsWindowContents(Rect inRect)
        {
            const float ResetButtonHeight = 32f;
            Rect outRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height - ResetButtonHeight - 10f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, settingsHeight);

            Widgets.BeginScrollView(outRect, ref settingsScroll, viewRect);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);

            SectionHeader(listing, "ResearchOrganized_SectionResearchWindow".Translate());

            listing.CheckboxLabeled("ResearchOrganized_HideUnavailable".Translate(), ref settings.hideUnavailableProjects,
                "ResearchOrganized_HideUnavailableTip".Translate());

            listing.CheckboxLabeled("ResearchOrganized_HideFutureTechLevels".Translate(), ref settings.hideFutureTechLevels,
                "ResearchOrganized_HideFutureTechLevelsTip".Translate());

            listing.CheckboxLabeled("ResearchOrganized_ShowTabCounts".Translate(), ref settings.showTabProjectCounts,
                "ResearchOrganized_ShowTabCountsTip".Translate());

            listing.CheckboxLabeled("ResearchOrganized_SuppressEmergenceLines".Translate(), ref settings.suppressEmergenceLines,
                "ResearchOrganized_SuppressEmergenceLinesTip".Translate());

            SectionHeader(listing, "ResearchOrganized_SectionTabs".Translate());

            listing.CheckboxLabeled("ResearchOrganized_CombineAllTabs".Translate(), ref settings.combineAllTabs,
                "ResearchOrganized_CombineAllTabsTip".Translate());

            if (settings.combineAllTabs)
            {
                Rect industrialRect = listing.GetRect(24f);
                GUI.color = Color.gray;
                Widgets.Label(industrialRect, "ResearchOrganized_CombineIndustrial".Translate());
                GUI.color = Color.white;
                Widgets.CheckboxDraw(industrialRect.xMax - 24f, industrialRect.y, true, true);
                TooltipHandler.TipRegion(industrialRect, "ResearchOrganized_CombineIndustrialIncludedTip".Translate());
                listing.Gap(listing.verticalSpacing);
            }
            else
            {
                listing.CheckboxLabeled("ResearchOrganized_CombineIndustrial".Translate(), ref settings.combineIndustrial,
                    "ResearchOrganized_CombineIndustrialTip".Translate());
            }

            SectionHeader(listing, "ResearchOrganized_SectionColors".Translate());

            bool useColors = !settings.disableCustomColors;
            listing.CheckboxLabeled("ResearchOrganized_ColorCards".Translate(), ref useColors,
                "ResearchOrganized_ColorCardsTip".Translate());
            settings.disableCustomColors = !useColors;

            listing.Gap(6f);
            DrawColorPalette(listing, "ResearchOrganized_ColorUndefined".Translate(), ref settings.colorUndefined, useColors);
            DrawColorPalette(listing, "ResearchOrganized_ColorAnimal".Translate(), ref settings.colorAnimal, useColors);
            DrawColorPalette(listing, "ResearchOrganized_ColorNeolithic".Translate(), ref settings.colorNeolithic, useColors);
            DrawColorPalette(listing, "ResearchOrganized_ColorMedieval".Translate(), ref settings.colorMedieval, useColors);
            DrawColorPalette(listing, "ResearchOrganized_ColorIndustrial".Translate(), ref settings.colorIndustrial, useColors);
            DrawColorPalette(listing, "ResearchOrganized_ColorSpacer".Translate(), ref settings.colorSpacer, useColors);
            DrawColorPalette(listing, "ResearchOrganized_ColorUltra".Translate(), ref settings.colorUltra, useColors);
            DrawColorPalette(listing, "ResearchOrganized_ColorArchotech".Translate(), ref settings.colorArchotech, useColors);
            DrawColorPalette(listing, "ResearchOrganized_ColorAnomaly".Translate(), ref settings.colorAnomaly, useColors);

            SectionHeader(listing, "ResearchOrganized_SectionLayout".Translate());

            listing.Label("ResearchOrganized_AnchorIntro".Translate());

            Rect minorRect = listing.GetRect(24f);
            Widgets.Label(new Rect(minorRect.x, minorRect.y, minorRect.width - 60f, minorRect.height), "ResearchOrganized_MinorAnchor".Translate());
            Widgets.TextFieldNumeric(new Rect(minorRect.xMax - 50f, minorRect.y, 50f, minorRect.height),
                ref settings.minorAnchorChildThreshold, ref minorThresholdBuffer, 1f, 99f);
            listing.Gap(listing.verticalSpacing);

            Rect majorRect = listing.GetRect(24f);
            Widgets.Label(new Rect(majorRect.x, majorRect.y, majorRect.width - 60f, majorRect.height), "ResearchOrganized_MajorAnchor".Translate());
            Widgets.TextFieldNumeric(new Rect(majorRect.xMax - 50f, majorRect.y, 50f, majorRect.height),
                ref settings.majorAnchorChildThreshold, ref majorThresholdBuffer, 2f, 99f);
            TooltipHandler.TipRegion(majorRect, "ResearchOrganized_MajorAnchorTip".Translate());
            listing.Gap(listing.verticalSpacing);

            listing.CheckboxLabeled("ResearchOrganized_WriteLayoutLog".Translate(), ref settings.writeLayoutLog,
                "ResearchOrganized_WriteLayoutLogTip".Translate());

            settingsHeight = listing.CurHeight + 10f;
            listing.End();
            Widgets.EndScrollView();

            Listing_Standard footer = new Listing_Standard();
            footer.Begin(new Rect(inRect.x, inRect.yMax - ResetButtonHeight, 200f, ResetButtonHeight));
            if (footer.ButtonText("ResearchOrganized_ResetDefaults".Translate()))
            {
                settings.CopyFrom(new ResearchOrganizedSettings());
                minorThresholdBuffer = settings.minorAnchorChildThreshold.ToString();
                majorThresholdBuffer = settings.majorAnchorChildThreshold.ToString();
            }
            footer.End();

            base.DoSettingsWindowContents(inRect);

            ResearchOrganizedMain.RefreshColors();
        }

        private static void SectionHeader(Listing_Standard listing, string title)
        {
            if (listing.CurHeight > 0f) listing.GapLine();
            Text.Font = GameFont.Medium;
            listing.Label(title);
            Text.Font = GameFont.Small;
            listing.Gap(4f);
        }

        private void DrawColorPalette(Listing_Standard listing, string label, ref Color selectedColor, bool enabled)
        {
            Rect rect = listing.GetRect(28f);
            if (!enabled)
            {
                GUI.color = Color.gray;
                TooltipHandler.TipRegion(rect, "ResearchOrganized_RequiresColorCards".Translate());
            }
            Widgets.Label(new Rect(rect.x, rect.y + 4f, 150f, rect.height), label);

            float curX = rect.x + 160f;
            foreach (Color color in Palette)
            {
                Rect swatchRect = new Rect(curX, rect.y + 2f, 24f, 24f);
                Widgets.DrawBoxSolid(swatchRect, enabled ? color : Color.Lerp(color, Color.gray, 0.6f));

                if (ColorsMatch(color, selectedColor)) Widgets.DrawBox(swatchRect, 2);

                if (enabled && Widgets.ButtonInvisible(swatchRect)) selectedColor = color;

                curX += 28f;
            }
            GUI.color = Color.white;
        }

        private bool ColorsMatch(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.01f &&
                   Mathf.Abs(a.g - b.g) < 0.01f &&
                   Mathf.Abs(a.b - b.b) < 0.01f;
        }
    }
}
