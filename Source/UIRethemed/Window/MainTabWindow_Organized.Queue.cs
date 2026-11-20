using System.Collections.Generic;
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
        private const float QueueWidth = 290f;
        private const float QueueFoldedWidth = 44f;
        private const float QueueRowHeight = 40f;
        private const float QueueRowGap = 4f;
        private const float QueueSlot = 30f;
        private const float FoldedHead = 58f;

        private static Texture2D queueTitleIcon;
        private static Texture2D clearIcon;
        private static Texture2D removeIcon;
        private static Texture2D dragIcon;

        private Vector2 queueScroll;
        private int queuePressed = -1;
        private Vector2 queuePressedAt;
        private int queueDragged = -1;
        private int queueDrawnFrame = -10;

        // Each row's "done in" note, rebuilt when the queue, research progress or the game hour changes.
        private readonly List<string> queueNotes = new List<string>();
        private int notesQueueVersion = -1;
        private int notesStatsVersion = -1;
        private int notesHour = -1;

        private static float QueuePanelWidth => ResearchOrganizedMod.settings.queueFolded ? QueueFoldedWidth : QueueWidth;

        private void ToggleQueued(ResearchProjectDef project)
        {
            if (ProjectQueue.IndexOf(project) >= 0)
            {
                ProjectQueue.Remove(project);
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }
            else if (ProjectQueue.CanQueue(project))
            {
                ProjectQueue.Add(project);
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
            else
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
            }
        }

        private void DrawQueuePanel(Rect rect)
        {
            if (queueTitleIcon == null)
            {
                queueTitleIcon = CoreWidgets.Icon("list-numbers");
                clearIcon = CoreWidgets.Icon("trash");
                removeIcon = CoreWidgets.Icon("x");
                dragIcon = CoreWidgets.DragTex;
            }
            var settings = ResearchOrganizedMod.settings;
            string title = TranslationCache.Get("ResearchOrganized_Queue");
            if (settings.queueFolded)
            {
                DrawFoldedQueue(rect, title);
                return;
            }

            bool folded = false;
            bool open = SidePanel.Begin(rect, title, queueTitleIcon, ref folded, SidePanel.Side.Left, out Rect inner, out Rect titleBar, out float toolsRight, 28f);
            if (folded)
            {
                settings.queueFolded = true;
                settings.Write();
            }
            if (!open) return;

            var items = ProjectQueue.Items;
            if (items.Count > 0)
            {
                Rect clear = new Rect(toolsRight - 24f, Mathf.Round(titleBar.center.y - 12f), 24f, 24f);
                if (CoreWidgets.HeaderTool(clear, clearIcon, TranslationCache.Get("ResearchOrganized_QueueClear"), TranslationCache.Get("ResearchOrganized_QueueClearTip")))
                {
                    for (int i = items.Count - 1; i >= 0; i--) ProjectQueue.Remove(items[i]);
                }
            }

            if (!ProjectQueue.Available)
            {
                float y = inner.y + 4f;
                CoreWidgets.Wrapped(ref y, inner.x + 4f, inner.width - 8f,
                    TranslationCache.Get(WindowHandover.ResearchQueueActive ? "ResearchOrganized_QueueOffResearchQueue" : "ResearchOrganized_QueueOffSemiRandom"), Theme.TextFaint);
                return;
            }
            if (items.Count == 0)
            {
                float y = inner.y + 4f;
                CoreWidgets.Wrapped(ref y, inner.x + 4f, inner.width - 8f, TranslationCache.Get("ResearchOrganized_QueueEmpty"), Theme.TextFaint);
                return;
            }
            DrawQueueList(inner, items);
        }

        private void RefreshQueueNotes(List<ResearchProjectDef> items)
        {
            int hour = ResearchRate.Hour;
            if (notesQueueVersion == ProjectQueue.Version && notesStatsVersion == ResearchOrganizedMain.StatsVersion && notesHour == hour && queueNotes.Count == items.Count) return;
            notesQueueVersion = ProjectQueue.Version;
            notesStatsVersion = ResearchOrganizedMain.StatsVersion;
            notesHour = hour;
            queueNotes.Clear();
            // Time in front of each row: the current project's remainder, then each queued project's own time in turn.
            var current = Find.ResearchManager.GetProject();
            long ahead = current != null ? System.Math.Max(0, ResearchRate.TicksLeft(current)) : 0;
            bool timed = ResearchOrganizedMod.settings.showTimeLeft && ResearchRate.HasEstimate;
            for (int i = 0; i < items.Count; i++)
            {
                var project = items[i];
                string cost = Info(project).cost;
                int own = timed ? ResearchRate.TicksLeft(project) : -1;
                if (own > 0) ahead += own;
                if (own > 0)
                    queueNotes.Add(cost + "  ·  " + ResearchRate.FormatPeriod(own) + "  ·  "
                        + "ResearchOrganized_QueueDoneIn".Translate(ResearchRate.FormatPeriod((int)System.Math.Min(ahead, int.MaxValue))));
                else
                    queueNotes.Add(cost);
            }
        }

        private void DrawQueueList(Rect inner, List<ResearchProjectDef> items)
        {
            RefreshQueueNotes(items);
            if (ListDrag.Forget(ref queueDrawnFrame) || queueDragged >= items.Count || queuePressed >= items.Count)
            {
                queueDragged = -1;
                queuePressed = -1;
            }
            float step = QueueRowHeight + QueueRowGap;
            float contentHeight = items.Count * step;
            if (queueDragged >= 0) ListDrag.ScrollNearEdges(inner, ref queueScroll, contentHeight);
            Rect view = Theme.BeginScroll(inner, ref queueScroll, contentHeight);
            try
            {
                Event current = Event.current;
                int remove = -1;
                for (int i = 0; i < items.Count; i++)
                {
                    Rect row = new Rect(0f, i * step, view.width, QueueRowHeight);
                    if (DrawQueueRow(row, items[i], i)) remove = i;
                }

                // Press on a row: a click selects it, a drag past the threshold moves it.
                if (queuePressed >= 0 && queueDragged < 0 && ListDrag.Held && ListDrag.PastStart(queuePressedAt, current.mousePosition))
                {
                    queueDragged = queuePressed;
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                }
                if (queueDragged >= 0)
                {
                    int slot = Mathf.Clamp(Mathf.RoundToInt(current.mousePosition.y / step), 0, items.Count);
                    if (current.type == EventType.Repaint)
                    {
                        Theme.Fill(new Rect(0f, Mathf.Round(slot * step - QueueRowGap / 2f - 1f), view.width, 2f), Theme.HighlightGreen);
                        Rect floating = new Rect(0f, Mathf.Round(current.mousePosition.y - QueueRowHeight / 2f), view.width, QueueRowHeight);
                        DrawQueueRowFace(floating, items[queueDragged], queueDragged, true, 1f);
                    }
                    if (!ListDrag.Held)
                    {
                        int to = slot > queueDragged ? slot - 1 : slot;
                        if (to != queueDragged) ProjectQueue.Move(queueDragged, to);
                        SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                        queueDragged = -1;
                        queuePressed = -1;
                    }
                }
                else if (queuePressed >= 0 && !ListDrag.Held)
                {
                    if (queuePressed < items.Count) SelectProject(items[queuePressed], true);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    queuePressed = -1;
                }
                if (remove >= 0)
                {
                    ProjectQueue.Remove(items[remove]);
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                }
            }
            finally
            {
                Theme.EndScroll();
            }
        }

        /// <summary>One queued project; returns true when its remove button was clicked.</summary>
        private bool DrawQueueRow(Rect row, ResearchProjectDef project, int index)
        {
            bool dragging = queueDragged >= 0;
            Rect remove = new Rect(row.xMax - 26f, Mathf.Round(row.center.y - 11f), 22f, 22f);
            if (Event.current.type == EventType.Repaint) DrawQueueRowFace(row, project, index, false, queueDragged == index ? 0.4f : 1f);
            if (dragging) return false;

            if (CoreWidgets.HeaderButton(remove, removeIcon, Theme.HighlightRed, TranslationCache.Get("ResearchOrganized_QueueRemove"), null, 12f))
                return true;
            Rect body = new Rect(row.x, row.y, remove.x - 2f - row.x, row.height);
            if (Mouse.IsOver(body))
            {
                Event e = Event.current;
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    queuePressed = index;
                    queuePressedAt = e.mousePosition;
                    e.Use();
                }
                CoreWidgets.Tip(body, Info(project).label, TranslationCache.Get("ResearchOrganized_QueueRowTip"));
            }
            return false;
        }

        private void DrawQueueRowFace(Rect row, ResearchProjectDef project, int index, bool lifted, float alpha)
        {
            var info = Info(project);
            Color era = ResearchOrganizedMod.settings.disableCustomColors ? Theme.HighlightGray : ResearchOrganizedMain.EraColor(project);
            bool canStart = project.CanStartNow;
            float hover = lifted ? 1f : ThemeMotion.Hover(row, 41000 + index, queueDragged < 0 && Mouse.IsOver(row));
            Rounded.BeginBatch();
            try
            {
                Rounded.Fill(row, Faces.Fade(Color.Lerp(Theme.BG, era, 0.18f + 0.08f * hover), alpha));
                Rounded.Border(row, Faces.Fade(lifted ? Theme.HighlightGreen : Color.Lerp(Theme.BGDark, era, 0.6f), alpha));
            }
            finally
            {
                Rounded.EndBatch();
            }
            GUI.color = Faces.Fade(Color.Lerp(Theme.TextFaint, Color.white, hover), alpha);
            CoreWidgets.DrawIcon(new Rect(row.x + 4f, Mathf.Round(row.center.y - 7f), 14f, 14f), dragIcon);
            GUI.color = Color.white;

            Rect slot = new Rect(row.x + 22f, Mathf.Round(row.center.y - QueueSlot / 2f), QueueSlot, QueueSlot);
            DrawProjectSlot(slot, project, info, era, alpha);
            DrawSlotNumber(slot, index, alpha);

            float x = slot.xMax + 8f;
            float right = row.xMax - 30f;
            Color text = Faces.Fade(canStart || Find.ResearchManager.IsCurrentProject(project) ? Color.white : Theme.TextDim, alpha);
            CoreWidgets.Line(new Rect(x, row.y + 2f, right - x, 20f), info.label, text);
            string note = index < queueNotes.Count ? queueNotes[index] : info.cost;
            CoreWidgets.Line(new Rect(x, row.y + 19f, right - x, 18f), note, Faces.Fade(Theme.TextFaint, alpha));
        }

        /// <summary>A square in the era's color holding the first thing the project unlocks, or its initial.</summary>
        private static void DrawProjectSlot(Rect slot, ResearchProjectDef project, CardInfo info, Color era, float alpha)
        {
            if (Event.current.type == EventType.Repaint)
            {
                Rounded.BeginBatch();
                try
                {
                    Rounded.Fill(slot, Faces.Fade(Color.Lerp(Theme.BGDark, era, 0.35f), alpha));
                    Rounded.Border(slot, Faces.Fade(era, alpha));
                }
                finally
                {
                    Rounded.EndBatch();
                }
            }
            var icons = Icons(project, info);
            if (icons.Count > 0)
            {
                GUI.color = new Color(1f, 1f, 1f, alpha);
                Widgets.DefIcon(slot.ContractedBy(4f), icons[0]);
                GUI.color = Color.white;
            }
            else if (!info.label.NullOrEmpty())
            {
                CoreWidgets.Line(slot, info.label.Substring(0, 1), Faces.Fade(Color.white, alpha), GameFont.Medium, TextAnchor.MiddleCenter);
            }
        }

        private static void DrawSlotNumber(Rect slot, int index, float alpha)
        {
            string number = index < queueNumbers.Length ? queueNumbers[index] ?? (queueNumbers[index] = (index + 1).ToString()) : (index + 1).ToString();
            Rect badge = new Rect(slot.x - 5f, slot.y - 5f, 16f, 16f);
            if (Event.current.type == EventType.Repaint) Rounded.Fill(badge, Faces.Fade(Theme.HighlightBlue, alpha));
            CoreWidgets.Line(badge, number, Faces.Fade(Color.white, alpha), GameFont.Small, TextAnchor.MiddleCenter);
        }

        /// <summary>Folded: the panel's own unfold button, then a column of the queued projects' icons, which select them.</summary>
        private void DrawFoldedQueue(Rect rect, string title)
        {
            if (Event.current.type == EventType.Repaint)
                Rounded.Fill(new Rect(rect.x, rect.y + FoldedHead - 8f, rect.width, rect.height - FoldedHead + 8f), Theme.BGLight, Corners.Bottom);
            if (SidePanel.Folded(new Rect(rect.x, rect.y, rect.width, FoldedHead), queueTitleIcon, SidePanel.Side.Left, title))
            {
                ResearchOrganizedMod.settings.queueFolded = false;
                ResearchOrganizedMod.settings.Write();
            }
            var items = ProjectQueue.Items;
            float size = rect.width - 12f;
            float y = rect.y + FoldedHead + 6f;
            int fits = Mathf.Max(0, Mathf.FloorToInt((rect.yMax - 6f - y + 6f) / (size + 6f)));
            int shown = items.Count > fits ? Mathf.Max(0, fits - 1) : items.Count;
            for (int i = 0; i < shown; i++)
            {
                var project = items[i];
                var info = Info(project);
                Color era = ResearchOrganizedMod.settings.disableCustomColors ? Theme.HighlightGray : ResearchOrganizedMain.EraColor(project);
                Rect slot = new Rect(rect.x + 6f, y, size, size);
                DrawProjectSlot(slot, project, info, era, 1f);
                DrawSlotNumber(slot, i, 1f);
                if (Mouse.IsOver(slot))
                {
                    CoreWidgets.Tip(slot, info.label, queueNotes.Count > i ? queueNotes[i] : info.cost);
                    if (Widgets.ButtonInvisible(slot))
                    {
                        SoundDefOf.Click.PlayOneShotOnCamera();
                        SelectProject(project, true);
                    }
                }
                y += size + 6f;
            }
            if (shown < items.Count)
            {
                Rect more = new Rect(rect.x + 6f, y, size, size);
                CoreWidgets.Line(more, "+" + (items.Count - shown), Theme.TextDim, GameFont.Small, TextAnchor.MiddleCenter);
            }
        }
    }
}
