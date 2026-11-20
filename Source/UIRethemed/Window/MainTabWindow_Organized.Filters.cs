using System.Collections.Generic;
using RimWorld;
using UIRethemed.Core;
using UnityEngine;
using Verse;
using ResearchOrganized;

namespace ResearchOrganizedUIRethemed
{
    public partial class MainTabWindow_Organized
    {
        private const float FilterBarHeight = 30f;
        private const float ChipWidth = 130f;
        private const string SearchControl = "ResearchOrganizedTwoSearchField";

        private string searchText = "";

        /// <summary>Projects matching the search, by name or by anything they unlock; null while the search is empty.</summary>
        private HashSet<ResearchProjectDef> searchMatches;

        private static Texture2D lockIcon;
        private static Texture2D checkIcon;
        private static Texture2D futureIcon;
        private static Texture2D zoomInIcon;
        private static Texture2D zoomOutIcon;
        private static Texture2D fitIcon;

        private void DrawSearch(Rect rect)
        {
            if (SearchField.Draw(rect, ref searchText, SearchControl, TranslationCache.Get("ResearchOrganized_SearchHint")))
                UpdateSearch();
        }

        private void UpdateSearch()
        {
            string text = searchText?.Trim();
            if (text.NullOrEmpty())
            {
                searchMatches = null;
                return;
            }
            searchMatches = searchMatches ?? new HashSet<ResearchProjectDef>();
            searchMatches.Clear();
            foreach (var project in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
            {
                if (Matches(project.LabelCap, text)) { searchMatches.Add(project); continue; }
                var unlocked = project.UnlockedDefs;
                for (int i = 0; i < unlocked.Count; i++)
                {
                    if (Matches(unlocked[i].LabelCap, text)) { searchMatches.Add(project); break; }
                }
            }
        }

        private static bool Matches(string label, string text) =>
            !label.NullOrEmpty() && label.IndexOf(text, System.StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Show/hide chips for the visibility filters, remembered in the settings, and the zoom controls.</summary>
        private void DrawFilterBar(Rect rect)
        {
            if (lockIcon == null)
            {
                lockIcon = CoreWidgets.Icon("lock");
                checkIcon = CoreWidgets.Icon("check");
                futureIcon = CoreWidgets.Icon("hourglass");
                zoomInIcon = CoreWidgets.Icon("plus");
                zoomOutIcon = CoreWidgets.Icon("minus");
                fitIcon = CoreWidgets.Icon("frame-corners");
                cardSizeIcon = CoreWidgets.Icon("cards");
            }
            var settings = ResearchOrganizedMod.settings;
            float x = rect.x;
            if (FilterChip(ref x, rect, "ResearchOrganized_FilterLocked", "ResearchOrganized_FilterLockedTip", lockIcon, !settings.hideUnavailableProjects))
                SetFilter(ref settings.hideUnavailableProjects);
            if (FilterChip(ref x, rect, "ResearchOrganized_FilterFinished", "ResearchOrganized_FilterFinishedTip", checkIcon, !settings.hideFinishedProjects))
                SetFilter(ref settings.hideFinishedProjects);
            if (FilterChip(ref x, rect, "ResearchOrganized_FilterFuture", "ResearchOrganized_FilterFutureTip", futureIcon, !settings.hideFutureTechLevels))
                SetFilter(ref settings.hideFutureTechLevels);

            // Core's header-button look, right to left: fit, zoom in, zoom level, zoom out, then card size.
            float right = rect.xMax;
            float y = Mathf.Round(rect.center.y - 12f);
            if (HeaderTool(ref right, y, fitIcon, "ResearchOrganized_FitTip")) FitNextDraw();
            if (HeaderTool(ref right, y, zoomInIcon, "ResearchOrganized_ZoomInTip")) ZoomBy(1);
            Rect zoomLabel = new Rect(right - 48f, rect.y, 48f, rect.height);
            CoreWidgets.Line(zoomLabel, ZoomLabel, Theme.TextDim, GameFont.Small, TextAnchor.MiddleCenter);
            right = zoomLabel.x - 4f;
            if (HeaderTool(ref right, y, zoomOutIcon, "ResearchOrganized_ZoomOutTip")) ZoomBy(-1);
            right -= 8f;
            if (HeaderTool(ref right, y, cardSizeIcon, "ResearchOrganized_CardSize", CardSizeTip)) ResearchOrganizedMod.ChooseCardSize();
        }

        private static Texture2D cardSizeIcon;
        private string cardSizeTip;
        private int cardSizeTipFor = -1;

        private string CardSizeTip
        {
            get
            {
                int size = ResearchOrganizedMod.settings.cardSize;
                if (cardSizeTip == null || cardSizeTipFor != size)
                {
                    cardSizeTipFor = size;
                    cardSizeTip = "ResearchOrganized_CardSizeButtonTip".Translate(ResearchOrganizedMod.CardSizeLabel(size));
                }
                return cardSizeTip;
            }
        }

        /// <summary>A 24px header button placed left of <paramref name="right"/>, which moves past it.</summary>
        private static bool HeaderTool(ref float right, float y, Texture icon, string titleKey, string tip = null)
        {
            Rect button = new Rect(right - 24f, y, 24f, 24f);
            right = button.x - 4f;
            return CoreWidgets.HeaderTool(button, icon, TranslationCache.Get(titleKey), tip);
        }

        private static bool FilterChip(ref float x, Rect bar, string labelKey, string tipKey, Texture icon, bool shown)
        {
            Rect chip = new Rect(x, bar.y + 2f, ChipWidth, bar.height - 4f);
            x = chip.xMax + 6f;
            return CoreWidgets.Chip(chip, TranslationCache.Get(labelKey), shown ? ChipState.Include : ChipState.Off, Theme.HighlightGreen, -1,
                TranslationCache.Get(tipKey), icon);
        }

        private void SetFilter(ref bool hidden)
        {
            hidden = !hidden;
            ResearchOrganizedMod.SaveAndApply();
        }
    }
}
