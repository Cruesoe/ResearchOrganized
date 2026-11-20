using System.Collections;
using RimWorld;
using UIRethemed.Core;
using UnityEngine;
using Verse;
using Verse.Sound;
using ResearchOrganized;

namespace ResearchOrganizedUIRethemed
{
    public partial class MainTabWindow_Organized
    {
        private const float TabHeight = 32f;
        private const float TabRowGap = 3f;
        private const float TabMinWidth = 90f;
        private const float TabMaxWidth = 200f;
        private const float TabSpacing = 3f;
        private const float SearchWidth = 220f;

        private int TabRows(float width)
        {
            IList tabs = ResearchOrganizedMain.TabRecords(this);
            int count = tabs?.Count ?? 0;
            if (count == 0) return 1;
            float room = width - SearchWidth - Gap;
            int perRow = Mathf.Max(1, Mathf.FloorToInt((room + TabSpacing) / (TabMinWidth + TabSpacing)));
            return Mathf.CeilToInt(count / (float)perRow);
        }

        private float TabStripHeight(float width)
        {
            int rows = TabRows(width);
            return rows * TabHeight + (rows - 1) * TabRowGap;
        }

        /// <summary>The window's tab records (vanilla's list, kept current by the tab pass) as themed tabs, with the search box at the end of the first row.</summary>
        private void DrawTabStrip(Rect rect)
        {
            Rect search = new Rect(rect.xMax - SearchWidth, rect.y + (TabHeight - SearchField.Height) / 2f, SearchWidth, SearchField.Height);
            DrawSearch(search);

            IList tabs = ResearchOrganizedMain.TabRecords(this);
            if (tabs == null || tabs.Count == 0) return;
            int rows = TabRows(rect.width);
            int perRow = Mathf.CeilToInt(tabs.Count / (float)rows);
            float room = rect.width - SearchWidth - Gap;
            float width = Mathf.Clamp((room - (perRow - 1) * TabSpacing) / perRow, TabMinWidth, TabMaxWidth);

            for (int i = 0; i < tabs.Count; i++)
            {
                if (!(tabs[i] is TabRecord record)) continue;
                int row = i / perRow;
                int column = i % perRow;
                Rect tab = new Rect(rect.x + column * (width + TabSpacing), rect.y + row * (TabHeight + TabRowGap), width, TabHeight);
                var tabDef = ResearchOrganizedMain.TabRecordDef(record);
                bool open = tabDef != null && tabDef == CurTab;
                bool discovered = tabDef == null || Find.ResearchManager.TabInfoVisible(tabDef);
                DrawTab(tab, record.label, open, discovered);
                if (Mouse.IsOver(tab))
                {
                    string tip = record.GetTip();
                    if (!tip.NullOrEmpty()) CoreWidgets.Tip(tab, record.label, tip);
                }
                if (!open && Widgets.ButtonInvisible(tab))
                {
                    SoundDefOf.RowTabSelect.PlayOneShotOnCamera();
                    if (tabDef != null) ChangeTab(tabDef);
                    else record.clickedAction?.Invoke();
                }
            }
        }

        private static void DrawTab(Rect rect, string label, bool open, bool discovered)
        {
            float hover = open ? 0f : ThemeMotion.Hover(rect, 31419, Mouse.IsOver(rect));
            if (Event.current.type == EventType.Repaint)
            {
                Color edge = Color.Lerp(Theme.BGDark, Theme.HighlightGray, 0.35f);
                Color fill = open ? Theme.BG : Color.Lerp(Color.Lerp(Theme.BGDark, Theme.BG, 0.45f), Color.Lerp(Theme.BG, Theme.BGLight, 0.25f), hover * 0.8f);
                Rounded.BeginBatch();
                try
                {
                    Rounded.Fill(rect, fill);
                    Rounded.Border(rect, open ? Theme.HighlightGreen : edge);
                }
                finally
                {
                    Rounded.EndBatch();
                }
                if (open)
                    Theme.Fill(new Rect(rect.x + 8f, rect.yMax - 3f, rect.width - 16f, 2f), Theme.HighlightGreen);
                else if (hover > 0f)
                    Theme.Fill(new Rect(rect.x + 8f, rect.yMax - 3f, rect.width - 16f, 2f), Faces.Fade(Theme.HighlightGreen, 0.45f * hover));
            }
            Color color = !discovered ? Theme.TextFaint : open ? Color.white : Color.Lerp(Theme.TextDim, Theme.TextBright, hover);
            CoreWidgets.Line(new Rect(rect.x + 8f, rect.y, rect.width - 16f, rect.height), label, color, GameFont.Small, TextAnchor.MiddleCenter);
        }
    }
}
