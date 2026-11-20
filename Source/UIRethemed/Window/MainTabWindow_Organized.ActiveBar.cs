using RimWorld;
using UIRethemed.Core;
using UnityEngine;
using Verse;
using ResearchOrganized;

namespace ResearchOrganizedUIRethemed
{
    public partial class MainTabWindow_Organized
    {
        // Current project: a large research card across the top, with the progress filling it behind the text.

        private const int ActiveTitleSize = 20;
        private const int ActivePercentSize = 24;
        private const float ActiveIconSize = 40f;
        private const float ActiveUnlockSize = 30f;
        private const float ActiveStripHeight = 4f;
        private static readonly Color ActiveDoneFill = new Color(1f, 0.9f, 0.6f, 0.35f);
        private static readonly Color ActiveSubtext = new Color(0.92f, 0.88f, 0.8f);
        private static Texture2D activeIcon;
        private string noProject;
        private string basicLabel;
        private string advancedLabel;
        private readonly ResearchProjectDef[] progressFor = new ResearchProjectDef[2];
        private readonly int[] progressShown = { -1, -1 };
        private readonly string[] progressText = new string[2];
        private readonly string[] progressPercent = new string[2];
        private readonly int[] progressHour = { -1, -1 };
        private readonly bool[] progressTimeLeft = new bool[2];
        private readonly bool[] progressEstimate = new bool[2];

        private void DrawActiveBar(Rect rect)
        {
            activeIcon = activeIcon ?? CoreWidgets.Icon("flask");
            noProject = noProject ?? "NoProjectSelected".Translate().ToString();
            if (ModsConfig.AnomalyActive && CurTab == ResearchTabDefOf.Anomaly)
            {
                basicLabel = basicLabel ?? KnowledgeCategoryDefOf.Basic.LabelCap.ToString();
                advancedLabel = advancedLabel ?? KnowledgeCategoryDefOf.Advanced.LabelCap.ToString();
                float half = (rect.width - Gap) / 2f;
                DrawActiveCard(new Rect(rect.x, rect.y, half, rect.height), 0, Find.ResearchManager.GetProject(KnowledgeCategoryDefOf.Basic), basicLabel);
                DrawActiveCard(new Rect(rect.xMax - half, rect.y, half, rect.height), 1, Find.ResearchManager.GetProject(KnowledgeCategoryDefOf.Advanced), advancedLabel);
            }
            else
            {
                DrawActiveCard(rect, 0, Find.ResearchManager.GetProject(), null);
            }
        }

        private void DrawActiveCard(Rect card, int slot, ResearchProjectDef project, string prefix)
        {
            var settings = ResearchOrganizedMod.settings;
            Color era = project == null || settings.disableCustomColors ? Theme.HighlightGray : ResearchOrganizedMain.EraColor(project);
            float shown = project != null ? ThemeMotion.Follow(48213 + slot, card, Mathf.Clamp01(project.ProgressPercent), 6f) : 0f;
            float hover = project != null ? ThemeMotion.Hover(card, project.shortHash + 7919, Mouse.IsOver(card)) : 0f;

            if (Event.current.type == EventType.Repaint)
            {
                Rect done = new Rect(card.x, card.y, Mathf.Round(card.width * shown), card.height);
                Rounded.BeginBatch();
                try
                {
                    Rounded.Fill(card, project != null ? Color.Lerp(Theme.BGDark, era, 0.22f) : Theme.BG);
                    if (done.width >= 12f) Rounded.Fill(done, Faces.Fade(Color.Lerp(era, Theme.HighlightYellow, 0.35f), 0.38f));
                    if (hover > 0f) Rounded.Fill(card.ContractedBy(2f), new Color(1f, 1f, 1f, 0.05f * hover));
                    Rounded.Border(card, project != null ? Theme.HighlightYellow : Color.Lerp(Theme.BGDark, Theme.HighlightGray, 0.35f));
                }
                finally
                {
                    Rounded.EndBatch();
                }
                if (project != null)
                {
                    CoreWidgets.EdgeGlow(card, Theme.HighlightYellow);
                    if (done.width >= 12f)
                    {
                        // A bright strip along the bottom, and a soft glow at the leading edge of the done share.
                        Theme.Fill(new Rect(card.x + 4f, card.yMax - ActiveStripHeight - 3f, (card.width - 8f) * shown, ActiveStripHeight), Theme.HighlightYellow);
                        if (shown < 0.999f)
                        {
                            GUI.color = ActiveDoneFill;
                            GUI.DrawTexture(new Rect(done.xMax - 24f, card.y + 2f, 24f, card.height - 4f), Theme.HGradientTex);
                            GUI.color = Color.white;
                        }
                    }
                }
            }

            Rect inner = card.ContractedBy(10f, 6f);
            float x = inner.x;
            if (prefix != null)
            {
                Text.Font = GameFont.Small;
                float width = Text.CalcSize(prefix).x + 10f;
                CoreWidgets.Line(new Rect(x, inner.y, width, inner.height), prefix, Theme.TextDim);
                x += width;
            }
            if (project == null)
            {
                CoreWidgets.DrawIcon(new Rect(x, inner.center.y - 12f, 24f, 24f), activeIcon, Theme.TextFaint);
                CoreWidgets.Line(new Rect(x + 32f, inner.y, inner.xMax - x - 32f, inner.height), noProject, Theme.TextFaint);
                return;
            }

            var info = Info(project);
            RefreshProgressText(slot, project, info);

            // Icon: the first thing it unlocks, else the research flask.
            var icons = Icons(project, info);
            Rect iconRect = new Rect(x, inner.center.y - ActiveIconSize / 2f, ActiveIconSize, ActiveIconSize);
            if (icons.Count > 0) Widgets.DefIcon(iconRect, icons[0]);
            else CoreWidgets.DrawIcon(iconRect.ContractedBy(6f), activeIcon, Theme.HighlightYellow);
            x = iconRect.xMax + 12f;

            // Right: the percent, then the other unlock icons and requirement counters to its left.
            float percentWidth = TitleText.Width(progressPercent[slot], ActivePercentSize) + 4f;
            float right = inner.xMax - percentWidth;
            TitleText.Draw(new Rect(right, inner.y, percentWidth, inner.height), progressPercent[slot], Color.white, ActivePercentSize, TextAnchor.MiddleRight);
            right -= 16f;
            if (settings.cardUnlockIcons)
            {
                for (int i = icons.Count - 1; i >= 1 && right - ActiveUnlockSize > x + 200f; i--)
                {
                    Rect icon = new Rect(right - ActiveUnlockSize, inner.center.y - ActiveUnlockSize / 2f, ActiveUnlockSize, ActiveUnlockSize);
                    Widgets.DefIcon(icon, icons[i]);
                    right = icon.x - 6f;
                }
            }
            if (settings.cardChips)
            {
                Rect row = new Rect(x, inner.center.y - 10f, Mathf.Max(0f, right - x), 20f);
                if (project.TechprintCount > 0)
                    right = Counter(row, right, techprintIcon, Fraction(project.TechprintsApplied, project.TechprintCount), project.TechprintRequirementMet, 1f);
                if (project.RequiredAnalyzedThingCount > 0)
                    right = Counter(row, right, studyIcon, Fraction(project.AnalyzedThingsCompleted, project.RequiredAnalyzedThingCount), project.AnalyzedThingsRequirementsMet, 1f);
            }

            // Middle: the name large, with points and time left beneath.
            float textWidth = Mathf.Max(0f, right - x - 8f);
            float titleHeight = ActiveTitleSize + 6f;
            const float lineHeight = 20f;
            float top = inner.center.y - (titleHeight + lineHeight) / 2f;
            TitleText.Draw(new Rect(x, top, textWidth, titleHeight), info.label, Color.white, ActiveTitleSize);
            CoreWidgets.Line(new Rect(x, top + titleHeight, textWidth, lineHeight), progressText[slot], ActiveSubtext);

            if (ResearchOrganizedLayout.IsFoundationTech(project)) DrawFoundationMark(card, 1f, 1f);
            if (settings.cardHoverCard && Mouse.IsOver(card)) CoreWidgets.Tip(card, info.label, HoverText(project, info), null, era);
            if (Widgets.ButtonInvisible(card)) SelectProject(project, true);
        }

        /// <summary>Rebuilds the card's text when the shown progress, the hour or the time setting changes, or an estimate first appears.</summary>
        private void RefreshProgressText(int slot, ResearchProjectDef project, CardInfo info)
        {
            int shown = Mathf.FloorToInt(project.ProgressApparent);
            int hour = ResearchRate.Hour;
            bool timeLeft = ResearchOrganizedMod.settings.showTimeLeft;
            bool estimate = timeLeft && ResearchRate.HasEstimate;
            if (progressFor[slot] == project && progressShown[slot] == shown && progressHour[slot] == hour
                && progressTimeLeft[slot] == timeLeft && progressEstimate[slot] == estimate) return;
            progressFor[slot] = project;
            progressShown[slot] = shown;
            progressHour[slot] = hour;
            progressTimeLeft[slot] = timeLeft;
            progressEstimate[slot] = estimate;

            progressPercent[slot] = project.ProgressPercent.ToStringPercent();
            string line = project.ProgressApparentString + " / " + info.cost;
            if (timeLeft)
            {
                int ticks = estimate ? ResearchRate.TicksLeft(project) : -1;
                if (ticks > 0) line += "  ·  " + "ResearchOrganized_TimeLeft".Translate(ResearchRate.FormatPeriod(ticks));
                else if (!estimate && project.knowledgeCategory == null) line += "  ·  " + "ResearchOrganized_TimeEstimating".Translate();
            }
            progressText[slot] = line;
        }
    }
}
