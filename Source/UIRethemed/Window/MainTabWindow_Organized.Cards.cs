using System.Collections.Generic;
using System.Text;
using RimWorld;
using UIRethemed.Core;
using UnityEngine;
using Verse;
using ResearchOrganized;

namespace ResearchOrganizedUIRethemed
{
    public partial class MainTabWindow_Organized
    {
        private const int MaxCardIcons = 3;

        /// <summary>A project's unchanging card text and hover card, built once per project for the session's language.</summary>
        private sealed class CardInfo
        {
            public string label;
            public string cost;
            public string hoverBody;
            public int hoverVersion = -1;
            public int hoverHour = -1;
            public readonly List<Def> icons = new List<Def>();
            public int iconsVersion = -1;
            public string time;
            public int timeHour = -1;
            public int timeStats = -1;
            public string status;
            public string statusFirst;
            public string statusTime;
        }

        /// <summary>Up to three things the project unlocks without further research, refreshed as research completes.</summary>
        private static List<Def> Icons(ResearchProjectDef project, CardInfo info)
        {
            int version = ResearchOrganizedMain.StatsVersion;
            if (info.iconsVersion == version) return info.icons;
            info.iconsVersion = version;
            info.icons.Clear();
            var unlocked = project.UnlockedDefs;
            for (int i = 0; i < unlocked.Count && info.icons.Count < MaxCardIcons; i++)
            {
                if ((unlocked[i] is BuildableDef || unlocked[i] is RecipeDef recipe && recipe.ProducedThingDef != null) && UnlocksNow(project, unlocked[i]))
                    info.icons.Add(unlocked[i]);
            }
            return info.icons;
        }

        private static readonly Dictionary<ResearchProjectDef, CardInfo> cardInfo = new Dictionary<ResearchProjectDef, CardInfo>();
        private static LoadedLanguage cardLanguage;
        private static readonly Dictionary<int, string> fractions = new Dictionary<int, string>();
        private static readonly string[] percents = new string[101];

        private static Texture2D techprintIcon;
        private static Texture2D studyIcon;
        private static Texture2D doneIcon;
        private static Texture2D lockedIcon;

        private static CardInfo Info(ResearchProjectDef project)
        {
            if (cardLanguage != LanguageDatabase.activeLanguage)
            {
                cardInfo.Clear();
                cardLanguage = LanguageDatabase.activeLanguage;
            }
            if (cardInfo.TryGetValue(project, out var info)) return info;
            info = new CardInfo
            {
                label = project.LabelCap,
                cost = project.CostApparent.ToString("F0")
            };
            cardInfo[project] = info;
            return info;
        }

        /// <summary>The hover card's text, rebuilt when research progress or completion may have changed what the project unlocks now.</summary>
        private static string HoverText(ResearchProjectDef project, CardInfo info)
        {
            int version = ResearchOrganizedMain.StatsVersion;
            int hour = ResearchRate.Hour;
            if (info.hoverBody == null || info.hoverVersion != version || info.hoverHour != hour)
            {
                info.hoverBody = HoverBody(project);
                info.hoverVersion = version;
                info.hoverHour = hour;
            }
            return info.hoverBody;
        }

        /// <summary>Every project that lists each def among its unlocks. Lifetime: the session, since defs don't change.</summary>
        private static Dictionary<Def, List<ResearchProjectDef>> unlockedBy;

        /// <summary>A def counts as unlocked by the project now when every other project that also unlocks it is finished.</summary>
        private static bool UnlocksNow(ResearchProjectDef project, Def def)
        {
            if (unlockedBy == null)
            {
                unlockedBy = new Dictionary<Def, List<ResearchProjectDef>>();
                foreach (var other in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
                {
                    foreach (var unlocked in other.UnlockedDefs)
                    {
                        if (!unlockedBy.TryGetValue(unlocked, out var list)) unlockedBy[unlocked] = list = new List<ResearchProjectDef>();
                        list.Add(other);
                    }
                }
            }
            if (!unlockedBy.TryGetValue(def, out var projects)) return true;
            for (int i = 0; i < projects.Count; i++)
                if (projects[i] != project && !projects[i].IsFinished) return false;
            return true;
        }

        private static string HoverBody(ResearchProjectDef project)
        {
            var text = new StringBuilder();
            if (!project.description.NullOrEmpty()) text.AppendLine(project.description).AppendLine();
            if (ResearchOrganizedLayout.IsFoundationTech(project)) text.AppendLine("ResearchOrganized_HoverFoundation".Translate()).AppendLine();
            text.AppendLine("ResearchOrganized_HoverCost".Translate(project.CostApparent.ToString("F0")));
            string time = HoverTime(project);
            if (time != null) text.AppendLine(time);
            if (!project.prerequisites.NullOrEmpty())
            {
                text.Append("ResearchOrganized_HoverNeeds".Translate()).Append(' ');
                for (int i = 0; i < project.prerequisites.Count; i++)
                {
                    if (i > 0) text.Append(", ");
                    text.Append(project.prerequisites[i].LabelCap);
                }
                text.AppendLine();
            }
            // Only what this project unlocks on its own once finished; things that also need unfinished research are left out.
            var unlocked = project.UnlockedDefs;
            bool any = false;
            for (int i = 0; i < unlocked.Count; i++)
            {
                if (!UnlocksNow(project, unlocked[i])) continue;
                text.Append(any ? ", " : "ResearchOrganized_HoverUnlocks".Translate() + " ");
                text.Append(unlocked[i].LabelCap);
                any = true;
            }
            return text.ToString().TrimEnd();
        }

        /// <summary>The hover card's time line: the estimate at the recent pace, or when one will appear; null when there is nothing to time.</summary>
        private static string HoverTime(ResearchProjectDef project)
        {
            if (!ResearchOrganizedMod.settings.showTimeLeft || ResearchRate.WorkLeft(project) <= 0f) return null;
            int ticks = ResearchRate.TicksLeft(project);
            return ticks > 0 ? "ResearchOrganized_HoverTime".Translate(ticks.ToStringTicksToPeriod(false)) : "ResearchOrganized_HoverTimeUnknown".Translate();
        }

        /// <summary>"a/b", cached by its two numbers.</summary>
        private static string Fraction(int done, int total)
        {
            int key = done * 10000 + total;
            if (!fractions.TryGetValue(key, out string text)) fractions[key] = text = done + "/" + total;
            return text;
        }

        private static string Percent(float fraction)
        {
            int percent = Mathf.Clamp(Mathf.FloorToInt(fraction * 100f), 0, 100);
            return percents[percent] ?? (percents[percent] = percent + "%");
        }

        private void DrawCards(ZoomView view, Rect local)
        {
            if (techprintIcon == null)
            {
                techprintIcon = CoreWidgets.Icon("scroll");
                studyIcon = CoreWidgets.Icon("magnifying-glass");
                doneIcon = CoreWidgets.Icon("check-circle");
                lockedIcon = CoreWidgets.Icon("lock");
            }
            var settings = ResearchOrganizedMod.settings;
            foreach (var project in tabProjects)
            {
                Rect card = view.ToScreen(CardRect(project));
                if (!card.Overlaps(local)) continue;
                DrawCard(project, card, view.zoom, settings);
            }
        }

        private void DrawCard(ResearchProjectDef project, Rect card, float zoom, ResearchOrganizedSettings settings)
        {
            var info = Info(project);
            bool current = Find.ResearchManager.IsCurrentProject(project);
            bool finished = project.IsFinished;
            bool locked = !finished && !current && !project.CanStartNow;
            bool selected = Selected == project;
            bool hidden = project.IsHidden;
            bool foundation = ResearchOrganizedLayout.IsFoundationTech(project);
            bool searchedOut = searchMatches != null && !searchMatches.Contains(project);
            float alpha = searchedOut ? 0.35f : 1f;
            Color era = settings.disableCustomColors ? Theme.HighlightGray : ResearchOrganizedMain.EraColor(project);
            float hover = searchedOut ? 0f : ThemeMotion.Hover(card, project.shortHash, Mouse.IsOver(card));

            if (Event.current.type == EventType.Repaint)
            {
                Color fill = finished ? Color.Lerp(Theme.BG, era, 0.5f)
                    : locked ? Color.Lerp(Theme.BGDark, era, 0.15f)
                    : Color.Lerp(Theme.BG, era, 0.28f);
                Color edge = ResearchOrganizedLayout.cyclicNodes.Contains(project) ? Theme.HighlightRed
                    : selected ? Color.white
                    : current ? Theme.HighlightYellow
                    : chain.Contains(project) ? Theme.HighlightBlue
                    : foundation ? Color.Lerp(FoundationGold, Theme.BG, locked ? 0.45f : 0f)
                    : locked ? Theme.BGLight
                    : era;
                Rounded.BeginBatch();
                try
                {
                    Rounded.Fill(card, Faces.Fade(fill, alpha));
                    if (hover > 0f) Rounded.Fill(card.ContractedBy(2f), new Color(1f, 1f, 1f, 0.06f * hover));
                    Rounded.Border(card, Faces.Fade(edge, alpha));
                    if (selected || current) Rounded.Border(card.ContractedBy(1f), Faces.Fade(edge, 0.8f * alpha));
                }
                finally
                {
                    Rounded.EndBatch();
                }
                if (current || hover > 0f) CoreWidgets.EdgeGlow(card, Faces.Fade(current ? Theme.HighlightYellow : era, alpha));
                if (searchMatches != null && !searchedOut) CoreWidgets.EdgeGlow(card, Theme.HighlightYellow);

                float progress = project.ProgressPercent;
                if (settings.cardProgressStrip && progress > 0f && !finished)
                {
                    Rect strip = new Rect(card.x + 3f, card.yMax - 4f, (card.width - 6f) * Mathf.Clamp01(progress), 2f);
                    Theme.Fill(strip, Faces.Fade(current ? Theme.HighlightYellow : Theme.HighlightBlue, alpha));
                }
                float extra = ExtraWidth(project) * zoom;
                if (extra > 0f)
                {
                    GUI.color = Faces.Fade(project.knowledgeCategory.color, alpha);
                    GUI.DrawTexture(new Rect(card.x + 3f, card.y, extra, card.height), project.knowledgeCategory.Tex, ScaleMode.ScaleToFit);
                    GUI.color = Color.white;
                }
            }

            if (zoom >= 0.6f) DrawCardText(project, info, card, zoom, finished, current, locked, hidden, alpha, settings);
            if (foundation) DrawFoundationMark(card, zoom, alpha);
            int queued = ProjectQueue.IndexOf(project);
            if (queued >= 0) DrawQueueBadge(card, queued, alpha);

            if (settings.cardHoverCard && !hidden && Mouse.IsOver(card))
                CoreWidgets.Tip(card, info.label, HoverText(project, info), null, era);
        }

        /// <summary>Node Research's foundation projects: a gold rule along the top and a crown medallion on the top-right corner.</summary>
        internal static readonly Color FoundationGold = new Color(0.93f, 0.76f, 0.32f);
        private static Texture2D crownIcon;

        private static void DrawFoundationMark(Rect card, float zoom, float alpha)
        {
            crownIcon = crownIcon ?? CoreWidgets.Icon("crown-simple");
            if (Event.current.type != EventType.Repaint) return;
            Theme.Fill(new Rect(card.x + 8f, card.y + 1f, card.width - 16f, 2f), Faces.Fade(FoundationGold, 0.85f * alpha));
            float size = Mathf.Round(Mathf.Clamp(20f * zoom, 14f, 24f));
            Rect medal = new Rect(card.xMax - size * 0.7f, card.y - size * 0.3f, size, size);
            Rounded.BeginBatch();
            try
            {
                Rounded.Fill(medal, Faces.Fade(Color.Lerp(Theme.BGDark, FoundationGold, 0.25f), alpha));
                Rounded.Border(medal, Faces.Fade(FoundationGold, alpha));
            }
            finally
            {
                Rounded.EndBatch();
            }
            GUI.color = Faces.Fade(FoundationGold, alpha);
            CoreWidgets.DrawIcon(medal.ContractedBy(size * 0.2f), crownIcon);
            GUI.color = Color.white;
        }

        private static readonly string[] queueNumbers = new string[100];

        /// <summary>The project's place in the research queue, on its top-left corner.</summary>
        private static void DrawQueueBadge(Rect card, int index, float alpha)
        {
            string number = index < queueNumbers.Length ? queueNumbers[index] ?? (queueNumbers[index] = (index + 1).ToString()) : (index + 1).ToString();
            Text.Font = GameFont.Small;
            float width = Mathf.Max(20f, Text.CalcSize(number).x + 8f);
            Rect badge = new Rect(card.x - 6f, card.y - 8f, width, 20f);
            if (Event.current.type == EventType.Repaint)
            {
                Rounded.BeginBatch();
                try
                {
                    Rounded.Fill(badge, Faces.Fade(Theme.HighlightBlue, alpha));
                    Rounded.Border(badge, Faces.Fade(Theme.BGDark, alpha));
                }
                finally
                {
                    Rounded.EndBatch();
                }
            }
            CoreWidgets.Line(badge, number, Faces.Fade(Color.white, alpha), GameFont.Small, TextAnchor.MiddleCenter);
        }

        private void DrawCardText(ResearchProjectDef project, CardInfo info, Rect card, float zoom, bool finished, bool current, bool locked,
            bool hidden, float alpha, ResearchOrganizedSettings settings)
        {
            float left = card.x + 6f + ExtraWidth(project) * zoom;
            float width = card.xMax - 6f - left;
            // Room for the foundation medallion on the top-right corner.
            float labelWidth = ResearchOrganizedLayout.IsFoundationTech(project) ? width - Mathf.Clamp(20f * zoom, 14f, 24f) * 0.6f : width;
            Color text = Faces.Fade(locked ? Theme.TextDim : Color.white, alpha);
            string label = hidden ? TranslationCache.Get("ResearchOrganized_UnknownProject") : info.label;
            if (!M.twoRows)
            {
                // Compact cards hold the name only, centered.
                CoreWidgets.Line(new Rect(left, card.y, labelWidth, card.height), label, text);
                return;
            }
            CoreWidgets.Line(new Rect(left, card.y + 2f, labelWidth, 22f), label, text);
            if (zoom < 0.85f || hidden) return;

            // Second row: status on the left, requirement chips and unlock icons on the right.
            float iconSize = M.iconSize;
            Rect row = new Rect(left, card.yMax - Mathf.Max(24f, iconSize + 6f), width, Mathf.Max(20f, iconSize + 4f));
            float right = row.xMax;
            if (settings.cardUnlockIcons)
            {
                var icons = Icons(project, info);
                for (int i = icons.Count - 1; i >= 0 && right - iconSize > row.x + 50f; i--)
                {
                    Rect icon = new Rect(right - iconSize, row.y + 2f, iconSize, iconSize);
                    Widgets.DefIcon(icon, icons[i]);
                    right = icon.x - 3f;
                }
            }
            if (settings.cardChips)
            {
                if (project.TechprintCount > 0)
                    right = Counter(row, right, techprintIcon, Fraction(project.TechprintsApplied, project.TechprintCount), project.TechprintRequirementMet, alpha);
                if (project.RequiredAnalyzedThingCount > 0)
                    right = Counter(row, right, studyIcon, Fraction(project.AnalyzedThingsCompleted, project.RequiredAnalyzedThingCount), project.AnalyzedThingsRequirementsMet, alpha);
            }

            Rect status = new Rect(row.x, row.y, Mathf.Max(0f, right - row.x - 2f), row.height);
            if (finished)
            {
                CoreWidgets.DrawIcon(new Rect(status.x, status.y + 3f, 14f, 14f), doneIcon, Faces.Fade(Theme.HighlightGreen, alpha));
            }
            else if (current)
            {
                CoreWidgets.Line(status, StatusLine(project, info, true), Faces.Fade(Theme.TextYellow, alpha));
            }
            else
            {
                if (locked)
                {
                    CoreWidgets.DrawIcon(new Rect(status.x, status.y + 3f, 14f, 14f), lockedIcon, Faces.Fade(Theme.TextFaint, alpha));
                    status.xMin += 18f;
                }
                CoreWidgets.Line(status, StatusLine(project, info, false), Faces.Fade(locked ? Theme.TextFaint : Theme.TextDim, alpha));
            }
        }

        /// <summary>The project's time to research at the recent pace, short form ("2.3 days"); null without an estimate.</summary>
        private static string ResearchTime(ResearchProjectDef project, CardInfo info)
        {
            if (!ResearchOrganizedMod.settings.showTimeLeft) return null;
            int hour = ResearchRate.Hour;
            int stats = ResearchOrganizedMain.StatsVersion;
            if (info.timeHour != hour || info.timeStats != stats)
            {
                info.timeHour = hour;
                info.timeStats = stats;
                int ticks = ResearchRate.TicksLeft(project);
                info.time = ticks > 0 ? ticks.ToStringTicksToPeriod(false, true) : null;
            }
            return info.time;
        }

        /// <summary>Cost (or progress, for the current project) and research time, as the card's status line.</summary>
        private static string StatusLine(ResearchProjectDef project, CardInfo info, bool current)
        {
            string time = ResearchTime(project, info);
            string first = current ? Percent(project.ProgressPercent) : info.cost;
            if (time == null) return first;
            if (!ReferenceEquals(info.statusFirst, first) || !ReferenceEquals(info.statusTime, time))
            {
                info.statusFirst = first;
                info.statusTime = time;
                info.status = first + "  ·  " + time;
            }
            return info.status;
        }

        /// <summary>An icon and "a/b" right-aligned before <paramref name="right"/>; returns the new right edge.</summary>
        private static float Counter(Rect row, float right, Texture icon, string text, bool met, float alpha)
        {
            Text.Font = GameFont.Small;
            float textWidth = Text.CalcSize(text).x;
            Rect label = new Rect(right - textWidth, row.y, textWidth, row.height);
            Rect iconRect = new Rect(label.x - 16f, row.y + 3f, 14f, 14f);
            if (iconRect.x < row.x + 40f) return right;
            Color color = Faces.Fade(met ? Theme.TextDim : Theme.TextRed, alpha);
            CoreWidgets.DrawIcon(iconRect, icon, color);
            CoreWidgets.Line(label, text, color);
            return iconRect.x - 5f;
        }
    }
}
