using RimWorld;
using UIRethemed.Core;
using UnityEngine;
using Verse;
using ResearchOrganized;

namespace ResearchOrganizedUIRethemed
{
    public partial class MainTabWindow_Organized
    {
        // Active project bar.

        private static Texture2D activeIcon;
        private string noProject;
        private string basicLabel;
        private string advancedLabel;
        private ResearchProjectDef[] progressFor = new ResearchProjectDef[2];
        private readonly int[] progressShown = { -1, -1 };
        private readonly string[] progressText = new string[2];
        private readonly int[] progressHour = { -1, -1 };
        private readonly bool[] progressTimeLeft = new bool[2];

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
            float labelWidth = Mathf.Min(260f, (rect.xMax - x) * 0.35f);
            Rect label = new Rect(x, rect.y, labelWidth, rect.height);
            CoreWidgets.Line(label, info.label, Color.white);
            if (Widgets.ButtonInvisible(label)) SelectProject(project, true);

            int shown = Mathf.FloorToInt(project.ProgressApparent);
            int hour = ResearchRate.Hour;
            bool timeLeft = ResearchOrganizedMod.settings.showTimeLeft;
            if (progressFor[slot] != project || progressShown[slot] != shown || progressHour[slot] != hour || progressTimeLeft[slot] != timeLeft)
            {
                progressFor[slot] = project;
                progressShown[slot] = shown;
                progressHour[slot] = hour;
                progressTimeLeft[slot] = timeLeft;
                string line = project.ProgressApparentString + " / " + info.cost;
                int ticks = timeLeft ? ResearchRate.TicksLeft(project) : -1;
                if (ticks > 0) line += ", " + "ResearchOrganized_TimeLeft".Translate(ticks.ToStringTicksToPeriod());
                progressText[slot] = line;
            }
            Text.Font = GameFont.Small;
            float textWidth = Text.CalcSize(progressText[slot]).x + 8f;
            Rect text = new Rect(rect.xMax - textWidth, rect.y, textWidth, rect.height);
            Rect bar = new Rect(label.xMax + Gap, rect.center.y - 6f, Mathf.Max(0f, text.x - Gap - label.xMax - Gap), 12f);
            Theme.Bar(bar, project.ProgressPercent, Theme.HighlightYellow);
            CoreWidgets.Line(text, progressText[slot], Theme.TextDim, GameFont.Small, TextAnchor.MiddleRight);
        }
    }
}
