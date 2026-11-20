using RimWorld;
using UIRethemed.Core;
using UnityEngine;
using Verse;
using ResearchOrganized;

namespace ResearchOrganizedUIRethemed
{
    public partial class MainTabWindow_Organized
    {
        // Active project bar: the project's name, then a full-height progress bar with the percent, points and time left on it.

        private static Texture2D activeIcon;
        private static readonly Color BarShadow = new Color(0f, 0f, 0f, 0.75f);
        private static readonly Color BarSheen = new Color(1f, 1f, 1f, 0.16f);
        private static readonly Color BarEdgeGlow = new Color(1f, 1f, 1f, 0.55f);
        private const int BarMotionSalt = 48213;
        private string noProject;
        private string basicLabel;
        private string advancedLabel;
        private ResearchProjectDef[] progressFor = new ResearchProjectDef[2];
        private readonly int[] progressShown = { -1, -1 };
        private readonly string[] progressText = new string[2];
        private readonly string[] progressTip = new string[2];
        private readonly int[] progressHour = { -1, -1 };
        private readonly bool[] progressTimeLeft = new bool[2];
        private readonly bool[] progressEstimate = new bool[2];

        private void DrawActiveBar(Rect rect)
        {
            activeIcon = activeIcon ?? CoreWidgets.Icon("flask");
            if (Event.current.type == EventType.Repaint)
            {
                Rounded.BeginBatch();
                try
                {
                    Rounded.Fill(rect, Theme.BG);
                    Rounded.Border(rect, Color.Lerp(Theme.BGDark, Theme.HighlightGray, 0.35f));
                }
                finally
                {
                    Rounded.EndBatch();
                }
            }
            noProject = noProject ?? "NoProjectSelected".Translate().ToString();
            Rect inner = rect.ContractedBy(6f, 4f);
            if (ModsConfig.AnomalyActive && CurTab == ResearchTabDefOf.Anomaly)
            {
                basicLabel = basicLabel ?? KnowledgeCategoryDefOf.Basic.LabelCap.ToString();
                advancedLabel = advancedLabel ?? KnowledgeCategoryDefOf.Advanced.LabelCap.ToString();
                float half = (inner.width - Gap * 2f) / 2f;
                DrawActiveSlot(new Rect(inner.x, inner.y, half, inner.height), 0, Find.ResearchManager.GetProject(KnowledgeCategoryDefOf.Basic), basicLabel);
                DrawActiveSlot(new Rect(inner.xMax - half, inner.y, half, inner.height), 1, Find.ResearchManager.GetProject(KnowledgeCategoryDefOf.Advanced), advancedLabel);
            }
            else
            {
                DrawActiveSlot(inner, 0, Find.ResearchManager.GetProject(), null);
            }
        }

        private void DrawActiveSlot(Rect rect, int slot, ResearchProjectDef project, string prefix)
        {
            CoreWidgets.DrawIcon(new Rect(rect.x, rect.center.y - 10f, 20f, 20f), activeIcon, project != null ? Theme.HighlightYellow : Theme.TextFaint);
            float x = rect.x + 28f;
            if (prefix != null)
            {
                Text.Font = GameFont.Small;
                float width = Text.CalcSize(prefix).x + 8f;
                CoreWidgets.Line(new Rect(x, rect.y, width, rect.height), prefix, Theme.TextDim);
                x += width;
            }
            if (project == null)
            {
                CoreWidgets.Line(new Rect(x, rect.y, rect.xMax - x, rect.height), noProject, Theme.TextFaint);
                return;
            }

            var info = Info(project);
            float labelWidth = Mathf.Min(260f, (rect.xMax - x) * 0.3f);
            Rect label = new Rect(x, rect.y, labelWidth, rect.height);
            CoreWidgets.Line(label, info.label, Color.white);
            if (Widgets.ButtonInvisible(label)) SelectProject(project, true);

            RefreshProgressText(slot, project, info);
            Rect bar = new Rect(label.xMax + Gap, rect.y, Mathf.Max(0f, rect.xMax - label.xMax - Gap), rect.height);
            DrawProgressBar(bar, slot, project.ProgressPercent, ResearchOrganizedMain.EraColor(project));
            CoreWidgets.Line(new Rect(bar.x + 1f, bar.y + 1f, bar.width, bar.height), progressText[slot], BarShadow, GameFont.Small, TextAnchor.MiddleCenter);
            CoreWidgets.Line(bar, progressText[slot], Color.white, GameFont.Small, TextAnchor.MiddleCenter);
            if (progressTip[slot] != null) TooltipHandler.TipRegion(bar, progressTip[slot]);
        }

        /// <summary>Rebuilds the bar's text when the shown progress, the hour or the time setting changes, or an estimate first appears.</summary>
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

            string line = project.ProgressPercent.ToStringPercent() + "   " + project.ProgressApparentString + " / " + info.cost;
            if (timeLeft)
            {
                int ticks = estimate ? ResearchRate.TicksLeft(project) : -1;
                if (ticks > 0) line += "   " + "ResearchOrganized_TimeLeft".Translate(ticks.ToStringTicksToPeriod());
                else if (!estimate && project.knowledgeCategory == null) line += "   " + "ResearchOrganized_TimeEstimating".Translate();
            }
            progressText[slot] = line;
            progressTip[slot] = timeLeft && !estimate ? "ResearchOrganized_HoverTimeUnknown".Translate().ToString() : null;
        }

        /// <summary>A rounded track with the done share filled in the project's era color, Core's backdrop pattern drifting over the fill, a sheen and a glowing leading edge.</summary>
        private static void DrawProgressBar(Rect bar, int slot, float percent, Color eraColor)
        {
            float shown = ThemeMotion.Follow(BarMotionSalt + slot, bar, Mathf.Clamp01(percent), 6f);
            if (Event.current.type != EventType.Repaint) return;
            Color fillColor = Color.Lerp(eraColor, Theme.HighlightYellow, 0.35f);
            Rect fill = new Rect(bar.x, bar.y, Mathf.Round(bar.width * shown), bar.height);
            Rounded.BeginBatch();
            try
            {
                Rounded.Fill(bar, Theme.BGDark);
                if (fill.width >= 6f) Rounded.Fill(fill, fillColor);
                Rounded.Border(bar, Color.Lerp(Theme.BGDark, Theme.HighlightGray, 0.45f));
            }
            finally
            {
                Rounded.EndBatch();
            }
            if (fill.width < 6f) return;

            Rect inside = fill.ContractedBy(2f);
            Theme.Pattern(inside, Color.white, true, Surface.Background);
            GUI.color = BarSheen;
            GUI.DrawTexture(new Rect(inside.x, inside.y, inside.width, inside.height / 2f), Theme.VGradientTex);
            if (shown < 0.999f)
            {
                GUI.color = BarEdgeGlow;
                GUI.DrawTexture(new Rect(fill.xMax - 18f, inside.y, 18f, inside.height), Theme.HGradientTex);
            }
            GUI.color = Color.white;
        }
    }
}
