using System.Collections.Generic;
using System.Text;
using HarmonyLib;
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
        private const float ButtonRowHeight = 36f;
        private const float DevButtonHeight = 26f;
        private const int TitleSize = 22;
        private const float TileHeight = 30f;

        // Vanilla's start, so its ideology confirmation and other mods' patches on it (Semi Random Research's lock) still apply.
        private delegate void ProjectDelegate(MainTabWindow_Research window, ResearchProjectDef project);
        private static readonly ProjectDelegate attemptBeginResearch =
            AccessTools.MethodDelegate<ProjectDelegate>(AccessTools.Method(typeof(MainTabWindow_Research), "AttemptBeginResearch"));

        private static Texture2D detailsIcon;
        private static Texture2D metIcon;
        private static Texture2D unmetIcon;
        private static Texture2D playIcon;
        private static Texture2D stopIcon;
        private static Texture2D queueAddIcon;
        private Vector2 tabInfoScroll;
        private float tabInfoHeight;
        private Vector2 detailsScroll;

        private enum RowKind { Heading, Text, Requirement, Link, Tile, Note }

        /// <summary>One line of the details panel, laid out once per rebuild.</summary>
        private sealed class DetailRow
        {
            public RowKind kind;
            public string text;
            public string tip;
            public Color color = Color.white;
            public Def def;
            public ResearchProjectDef project;
            public bool met;
            public Rect rect;
        }

        // The selected project's rows, rebuilt when the selection, research progress, the panel's width, or every two seconds (benches) change.
        private readonly List<DetailRow> detailRows = new List<DetailRow>();
        private ResearchProjectDef detailsFor;
        private int detailsStats = -1;
        private float detailsWidth = -1f;
        private float detailsBuiltAt = -10f;
        private float detailsHeight;
        private string detailsStatus;
        private Color detailsStatusColor;
        private string detailsEra;
        private string detailsProgress;
        private string detailsTime;
        private float headerHeight;
        private readonly List<string> lockedReasons = new List<string>();
        private string lockedTip;

        private static Dictionary<ResearchProjectDef, List<ResearchProjectDef>> dependents;

        private void DrawDetailsPanel(Rect rect)
        {
            if (detailsIcon == null)
            {
                detailsIcon = CoreWidgets.Icon("info");
                metIcon = CoreWidgets.Icon("check-circle");
                unmetIcon = CoreWidgets.Icon("x-circle");
                playIcon = CoreWidgets.Icon("play");
                stopIcon = CoreWidgets.Icon("pause");
                queueAddIcon = CoreWidgets.Icon("list-plus");
            }
            var settings = ResearchOrganizedMod.settings;
            bool folded = settings.detailsFolded;
            bool open = SidePanel.Begin(rect, TranslationCache.Get("ResearchOrganized_Details"), detailsIcon, ref folded, SidePanel.Side.Right,
                out Rect inner, out _, out _);
            if (folded != settings.detailsFolded)
            {
                settings.detailsFolded = folded;
                settings.Write();
            }
            if (!open) return;

            var project = Selected;
            if (project == null)
            {
                DrawTabInfo(inner);
                return;
            }

            RefreshDetails(project, inner.width - 16f);
            Color era = settings.disableCustomColors ? Theme.HighlightGray : ResearchOrganizedMain.EraColor(project);

            Rect header = new Rect(inner.x, inner.y, inner.width, headerHeight);
            DrawDetailsHeader(header, project, era);

            Rect buttons = new Rect(inner.x, inner.yMax - ButtonRowHeight, inner.width, ButtonRowHeight);
            float bottom = buttons.y - Gap;
            if (Prefs.DevMode) bottom = DrawDevButtons(new Rect(inner.x, bottom - DevButtonHeight, inner.width, DevButtonHeight), project) - Gap;
            DrawDetailButtons(buttons, project);

            Rect scroll = new Rect(inner.x, header.yMax + Gap, inner.width, Mathf.Max(0f, bottom - header.yMax - Gap));
            Rect view = Theme.BeginScroll(scroll, ref detailsScroll, detailsHeight);
            try
            {
                float top = detailsScroll.y - 40f;
                float end = detailsScroll.y + scroll.height + 40f;
                for (int i = 0; i < detailRows.Count; i++)
                {
                    var row = detailRows[i];
                    if (row.rect.yMax < top || row.rect.y > end) continue;
                    DrawDetailRow(row, i);
                }
            }
            finally
            {
                Theme.EndScroll();
            }
        }

        private void DrawDetailsHeader(Rect header, ResearchProjectDef project, Color era)
        {
            if (Event.current.type == EventType.Repaint)
            {
                Rounded.BeginBatch();
                try
                {
                    Rounded.Fill(header, Color.Lerp(Theme.BG, era, 0.22f));
                    Rounded.Border(header, Color.Lerp(Theme.BGDark, era, 0.8f));
                }
                finally
                {
                    Rounded.EndBatch();
                }
                Theme.Fill(new Rect(header.x + 1f, header.y + 8f, 3f, header.height - 16f), era);
            }
            var info = Info(project);
            float x = header.x + 14f;
            float width = header.width - 24f;
            float titleHeight = TitleText.Height(info.label, width, TitleSize);
            TitleText.Draw(new Rect(x, header.y + 8f, width, titleHeight), info.label, Color.white, TitleSize, TextAnchor.UpperLeft, true);
            float y = header.y + 10f + titleHeight;

            // Tags: era, status, foundation, place in the queue.
            float tagX = x;
            tagX = Tag(tagX, y, detailsEra, era, header.xMax - 10f);
            tagX = Tag(tagX, y, detailsStatus, detailsStatusColor, header.xMax - 10f);
            if (ResearchOrganizedLayout.IsFoundationTech(project)) tagX = Tag(tagX, y, TranslationCache.Get("ResearchOrganized_Foundation"), FoundationGold, header.xMax - 10f);
            int queued = ProjectQueue.IndexOf(project);
            if (queued >= 0) tagX = Tag(tagX, y, "ResearchOrganized_QueuedAt".Translate(queued + 1), Theme.HighlightBlue, header.xMax - 10f);
            Tag(tagX, y, detailsTime, Theme.HighlightGray, header.xMax - 10f);
            y += 26f;

            if (!project.IsFinished && project.ProgressPercent > 0f || Find.ResearchManager.IsCurrentProject(project))
            {
                Rect bar = new Rect(x, y + 2f, width, 14f);
                Theme.Bar(bar, project.ProgressPercent, Find.ResearchManager.IsCurrentProject(project) ? Theme.HighlightYellow : Theme.HighlightBlue);
                CoreWidgets.Line(new Rect(x, bar.yMax, width, 20f), detailsProgress, Theme.TextDim, GameFont.Small, TextAnchor.MiddleRight);
            }
        }

        /// <summary>A small colored tag; returns the x after it.</summary>
        private static float Tag(float x, float y, string label, Color color, float right)
        {
            if (label.NullOrEmpty()) return x;
            float width = CoreWidgets.Width(label) + 14f;
            // Tags that don't fit on the line are left out.
            if (x + width > right) return x;
            Rect tag = new Rect(x, y, width, 22f);
            if (Event.current.type == EventType.Repaint)
            {
                Rounded.BeginBatch();
                try
                {
                    Rounded.Fill(tag, Color.Lerp(Theme.BGDark, color, 0.3f));
                    Rounded.Border(tag, color);
                }
                finally
                {
                    Rounded.EndBatch();
                }
            }
            CoreWidgets.Line(tag, label, Color.Lerp(color, Color.white, 0.6f), GameFont.Small, TextAnchor.MiddleCenter);
            return tag.xMax + 6f;
        }

        private void DrawDetailRow(DetailRow row, int index)
        {
            switch (row.kind)
            {
                case RowKind.Heading:
                    if (Event.current.type == EventType.Repaint)
                        Theme.Fill(new Rect(row.rect.x, row.rect.yMax - 2f, row.rect.width, 1f), Faces.Fade(Theme.HighlightGray, 0.35f));
                    TitleText.Draw(row.rect, row.text, Theme.TextBright, 15);
                    break;
                case RowKind.Text:
                case RowKind.Note:
                    Theme.Label(row.rect, row.text, TextAnchor.UpperLeft, row.color);
                    break;
                case RowKind.Requirement:
                case RowKind.Link:
                {
                    bool clickable = row.project != null || row.def != null;
                    float hover = clickable ? ThemeMotion.Hover(row.rect, 52000 + index, Mouse.IsOver(row.rect)) : 0f;
                    if (hover > 0f && Event.current.type == EventType.Repaint) Rounded.Fill(row.rect, new Color(1f, 1f, 1f, 0.05f * hover));
                    float x = row.rect.x + 4f;
                    if (row.kind == RowKind.Requirement)
                    {
                        CoreWidgets.DrawIcon(new Rect(x, row.rect.center.y - 8f, 16f, 16f), row.met ? metIcon : unmetIcon, row.met ? Theme.HighlightGreen : Theme.HighlightRed);
                        x += 22f;
                    }
                    if (row.def != null)
                    {
                        Widgets.DefIcon(new Rect(x, row.rect.center.y - 10f, 20f, 20f), row.def);
                        x += 26f;
                    }
                    else if (row.project != null)
                    {
                        Color era = ResearchOrganizedMod.settings.disableCustomColors ? Theme.HighlightGray : ResearchOrganizedMain.EraColor(row.project);
                        if (Event.current.type == EventType.Repaint) Rounded.Fill(new Rect(x + 3f, row.rect.center.y - 7f, 14f, 14f), era);
                        x += 26f;
                    }
                    CoreWidgets.Line(new Rect(x, row.rect.y, row.rect.xMax - x - 4f, row.rect.height), row.text, Color.Lerp(row.color, Color.white, hover * 0.4f));
                    if (!row.tip.NullOrEmpty()) CoreWidgets.Tip(row.rect, row.text, row.tip);
                    if (clickable && Widgets.ButtonInvisible(row.rect)) ActivateRow(row);
                    break;
                }
                case RowKind.Tile:
                {
                    float hover = CoreWidgets.Card(row.rect, false, null, true);
                    Widgets.DefIcon(new Rect(row.rect.x + 4f, row.rect.center.y - 11f, 22f, 22f), row.def);
                    CoreWidgets.Line(new Rect(row.rect.x + 32f, row.rect.y, row.rect.width - 36f, row.rect.height), row.text, Color.Lerp(Theme.TextBright, Color.white, hover));
                    if (Mouse.IsOver(row.rect)) CoreWidgets.Tip(row.rect, row.text, row.tip);
                    if (Widgets.ButtonInvisible(row.rect)) ActivateRow(row);
                    break;
                }
            }
        }

        private void ActivateRow(DetailRow row)
        {
            SoundDefOf.Click.PlayOneShotOnCamera();
            if (row.project != null) SelectProject(row.project, true);
            else if (row.def != null) new Dialog_InfoCard.Hyperlink(row.def).ActivateHyperlink();
        }

        private void DrawDetailButtons(Rect row, ResearchProjectDef project)
        {
            var manager = Find.ResearchManager;
            bool current = manager.IsCurrentProject(project);
            bool queueable = ProjectQueue.CanQueue(project);
            float queueWidth = queueable ? Mathf.Round(row.width * 0.38f) : 0f;
            Rect main = new Rect(row.x, row.y, row.width - (queueable ? queueWidth + Gap : 0f), row.height);

            if (current)
            {
                if (CoreWidgets.Button(main, TranslationCache.Get("ResearchOrganized_StopResearch"), stopIcon, Theme.HighlightRed, true, null, GameFont.Small, null, false, true))
                    manager.StopProject(project);
            }
            else if (project.IsFinished)
            {
                CoreWidgets.Button(main, TranslationCache.Get("ResearchOrganized_Finished"), metIcon, Theme.HighlightGreen, false);
            }
            else
            {
                bool canStart = project.CanStartNow;
                if (CoreWidgets.Button(main, TranslationCache.Get(canStart ? "ResearchOrganized_Research" : "ResearchOrganized_Locked"), canStart ? playIcon : lockedIcon,
                        Theme.HighlightGreen, canStart, canStart ? null : lockedTip, GameFont.Small, null, false, true) && canStart)
                {
                    attemptBeginResearch?.Invoke(this, project);
                }
            }

            if (!queueable) return;
            Rect queue = new Rect(main.xMax + Gap, row.y, queueWidth, row.height);
            bool queued = ProjectQueue.IndexOf(project) >= 0;
            if (CoreWidgets.Button(queue, TranslationCache.Get(queued ? "ResearchOrganized_Unqueue" : "ResearchOrganized_Enqueue"), queueAddIcon, Theme.HighlightBlue, true,
                    TranslationCache.Get("ResearchOrganized_EnqueueTip"), GameFont.Small, null, queued, true))
            {
                ToggleQueued(project);
            }
        }

        private void RefreshDetails(ResearchProjectDef project, float width)
        {
            int stats = ResearchOrganizedMain.StatsVersion;
            float now = Time.realtimeSinceStartup;
            if (detailsFor == project && detailsStats == stats && Mathf.Abs(detailsWidth - width) < 1f && now - detailsBuiltAt < 2f) return;
            if (detailsFor != project) detailsScroll = Vector2.zero;
            detailsFor = project;
            detailsStats = stats;
            detailsWidth = width;
            detailsBuiltAt = now;
            BuildDetails(project, width);
        }

        private void BuildDetails(ResearchProjectDef project, float width)
        {
            var manager = Find.ResearchManager;
            var info = Info(project);
            detailsEra = project.techLevel.ToStringHuman().CapitalizeFirst();
            bool current = manager.IsCurrentProject(project);
            if (project.IsFinished) { detailsStatus = TranslationCache.Get("ResearchOrganized_Finished"); detailsStatusColor = Theme.HighlightGreen; }
            else if (current) { detailsStatus = TranslationCache.Get("ResearchOrganized_Researching"); detailsStatusColor = Theme.HighlightYellow; }
            else if (project.CanStartNow) { detailsStatus = TranslationCache.Get("ResearchOrganized_Available"); detailsStatusColor = Theme.HighlightBlue; }
            else { detailsStatus = TranslationCache.Get("ResearchOrganized_Locked"); detailsStatusColor = Theme.HighlightRed; }
            detailsProgress = project.ProgressApparentString + " / " + info.cost;
            int ticks = current && ResearchOrganizedMod.settings.showTimeLeft ? ResearchRate.TicksLeft(project) : -1;
            if (ticks > 0) detailsProgress += ", " + "ResearchOrganized_TimeLeft".Translate(ticks.ToStringTicksToPeriod());
            int time = ResearchOrganizedMod.settings.showTimeLeft ? ResearchRate.TicksLeft(project) : -1;
            detailsTime = time > 0 ? "ResearchOrganized_TimeTag".Translate(time.ToStringTicksToPeriod(false, true)).ToString() : null;

            float headerWidth = width + 16f - 24f;
            bool progress = !project.IsFinished && project.ProgressPercent > 0f || current;
            headerHeight = 10f + TitleText.Height(info.label, headerWidth, TitleSize) + 26f + (progress ? 38f : 0f) + 8f;

            BuildLockedReasons(project);

            detailRows.Clear();
            Text.Font = GameFont.Small;
            string description = project.Description;
            if (!description.NullOrEmpty()) AddText(description, Theme.TextDim);
            if (project.techLevel > Faction.OfPlayer.def.techLevel && !project.IsFinished)
            {
                float factor = project.CostFactor(Faction.OfPlayer.def.techLevel);
                AddText("TechLevelTooLow".Translate(Faction.OfPlayer.def.techLevel.ToStringHuman(), project.techLevel.ToStringHuman(), (1f / factor).ToStringPercent()), Theme.TextYellow);
            }

            // Needs: prerequisites, benches, techprints, studies, and the rest of vanilla's start rules.
            int needsStart = detailRows.Count;
            AddHeading("ResearchOrganized_SectionNeeds");
            int needsHeading = detailRows.Count;
            AddPrerequisites(project.prerequisites);
            AddPrerequisites(project.hiddenPrerequisites);
            if (project.requiredResearchBuilding != null)
                AddRequirement(project.requiredResearchBuilding.LabelCap, HasBuilding(project.requiredResearchBuilding), project.requiredResearchBuilding, "RequiredResearchBench".Translate());
            else if (project.knowledgeCategory == null && !project.PlayerHasAnyAppropriateResearchBench)
                AddRequirement("ResearchOrganized_AnyResearchBench".Translate(), false, null, null);
            if (!project.requiredResearchFacilities.NullOrEmpty())
                foreach (var facility in project.requiredResearchFacilities)
                    AddRequirement(facility.LabelCap, HasActiveFacility(project.requiredResearchBuilding, facility), facility, "RequiredResearchBenchFacilities".Translate());
            if (project.TechprintCount > 0)
                AddRequirement("ResearchOrganized_Techprints".Translate(project.TechprintsApplied, project.TechprintCount), project.TechprintRequirementMet, null, TechprintHolders(project));
            if (project.RequiredAnalyzedThingCount > 0)
                foreach (var thing in project.requiredAnalyzed)
                    AddRequirement("ResearchOrganized_Study".Translate(thing.LabelCap), project.AnalyzedThingsRequirementsMet, thing, "StudyRequirements".Translate());
            if (project.requiresMechanitor)
                AddRequirement("MissingRequiredMechanitor".Translate(), project.PlayerMechanitorRequirementMet, null, null);
            if (ModsConfig.OdysseyActive && project.requireGravEngineInspected)
                AddRequirement("RequiresInspectionof".Translate() + " " + ThingDefOf.GravEngine.LabelCap, project.InspectionRequirementsMet, ThingDefOf.GravEngine, null);
            if (detailRows.Count == needsHeading) detailRows.RemoveRange(needsStart, detailRows.Count - needsStart);

            // Unlocks: what it unlocks without further research as tiles, then what also needs other research.
            var unlocked = project.UnlockedDefs;
            var later = new List<Def>();
            bool anyNow = false;
            foreach (var def in unlocked)
            {
                if (!UnlocksNow(project, def)) { later.Add(def); continue; }
                if (!anyNow) AddHeading("ResearchOrganized_SectionUnlocks");
                anyNow = true;
                detailRows.Add(new DetailRow { kind = RowKind.Tile, text = def.LabelCap, tip = def.description, def = def });
            }
            if (!project.customUnlockTexts.NullOrEmpty())
            {
                if (!anyNow) AddHeading("ResearchOrganized_SectionUnlocks");
                anyNow = true;
                foreach (var text in project.customUnlockTexts) AddText(text, Theme.TextBright);
            }
            if (later.Count > 0)
            {
                AddHeading("ResearchOrganized_SectionLater");
                foreach (var def in later)
                    detailRows.Add(new DetailRow { kind = RowKind.Link, text = def.LabelCap, tip = "ResearchOrganized_AlsoNeeds".Translate(OtherUnfinished(project, def)), def = def, color = Theme.TextDim });
            }

            // Leads to: projects that need this one.
            var next = Dependents(project);
            if (next.Count > 0)
            {
                AddHeading("ResearchOrganized_SectionLeadsTo");
                foreach (var dependent in next)
                    if (!dependent.IsHidden)
                        detailRows.Add(new DetailRow { kind = RowKind.Link, text = dependent.LabelCap, project = dependent, color = dependent.IsFinished ? Theme.TextDim : Color.white });
            }

            if (project.modContentPack != null && !project.modContentPack.IsCoreMod)
                AddNote("Stat_Source_Label".Translate() + ": " + project.modContentPack.Name);

            Layout(width);
        }

        private void AddText(string text, Color color) => detailRows.Add(new DetailRow { kind = RowKind.Text, text = text, color = color });
        private void AddNote(string text) => detailRows.Add(new DetailRow { kind = RowKind.Note, text = text, color = Theme.TextFaint });
        private void AddHeading(string key) => detailRows.Add(new DetailRow { kind = RowKind.Heading, text = TranslationCache.Get(key) });

        private void AddRequirement(string text, bool met, Def def, string tip) =>
            detailRows.Add(new DetailRow { kind = RowKind.Requirement, text = text, met = met, def = def, tip = tip, color = met ? Theme.TextBright : Color.Lerp(Theme.TextRed, Color.white, 0.25f) });

        private void AddPrerequisites(List<ResearchProjectDef> prerequisites)
        {
            if (prerequisites == null) return;
            foreach (var prerequisite in prerequisites)
                if (prerequisite != null)
                    detailRows.Add(new DetailRow { kind = RowKind.Requirement, text = prerequisite.LabelCap, met = prerequisite.IsFinished, project = prerequisite,
                        color = prerequisite.IsFinished ? Theme.TextBright : Color.Lerp(Theme.TextRed, Color.white, 0.25f) });
        }

        /// <summary>Places each row: text wraps to the width, tiles sit two to a line.</summary>
        private void Layout(float width)
        {
            float y = 0f;
            float tileWidth = (width - Gap) / 2f;
            int tileColumn = 0;
            Text.Font = GameFont.Small;
            foreach (var row in detailRows)
            {
                if (row.kind != RowKind.Tile && tileColumn > 0)
                {
                    y += TileHeight + 4f;
                    tileColumn = 0;
                }
                switch (row.kind)
                {
                    case RowKind.Heading:
                        y += 10f;
                        row.rect = new Rect(0f, y, width, 26f);
                        y += 30f;
                        break;
                    case RowKind.Text:
                    case RowKind.Note:
                        float height = Text.CalcHeight(row.text, width);
                        row.rect = new Rect(0f, y, width, height);
                        y += height + 6f;
                        break;
                    case RowKind.Requirement:
                    case RowKind.Link:
                        row.rect = new Rect(0f, y, width, 26f);
                        y += 27f;
                        break;
                    case RowKind.Tile:
                        row.rect = new Rect(tileColumn * (tileWidth + Gap), y, tileWidth, TileHeight);
                        if (++tileColumn == 2)
                        {
                            tileColumn = 0;
                            y += TileHeight + 4f;
                        }
                        break;
                }
            }
            if (tileColumn > 0) y += TileHeight + 4f;
            detailsHeight = y + 4f;
        }

        private void BuildLockedReasons(ResearchProjectDef project)
        {
            lockedReasons.Clear();
            if (!project.PrerequisitesCompleted) lockedReasons.Add("PrerequisitesNotCompleted".Translate());
            if (!project.TechprintRequirementMet) lockedReasons.Add("InsufficientTechprintsApplied".Translate(project.TechprintsApplied, project.TechprintCount));
            if (project.knowledgeCategory == null && !project.PlayerHasAnyAppropriateResearchBench) lockedReasons.Add("MissingRequiredResearchFacilities".Translate());
            if (!project.PlayerMechanitorRequirementMet) lockedReasons.Add("MissingRequiredMechanitor".Translate());
            if (!project.InspectionRequirementsMet) lockedReasons.Add("MissingGravEngineInspection".Translate());
            if (!project.AnalyzedThingsRequirementsMet)
                foreach (var thing in project.requiredAnalyzed) lockedReasons.Add("NotStudied".Translate(thing.LabelCap));
            lockedTip = lockedReasons.Count > 0 ? lockedReasons.ToLineList("- ") : null;
        }

        private static bool HasBuilding(ThingDef def)
        {
            foreach (var map in Find.Maps)
                foreach (var building in map.listerBuildings.allBuildingsColonist)
                    if (building.def == def) return true;
            return false;
        }

        /// <summary>A colony research bench (of the required kind, if any) with that facility linked and active.</summary>
        private static bool HasActiveFacility(ThingDef bench, ThingDef facility)
        {
            foreach (var map in Find.Maps)
            {
                foreach (var building in map.listerBuildings.allBuildingsColonist)
                {
                    if (!(building is Building_ResearchBench) || bench != null && building.def != bench) continue;
                    var comp = building.TryGetComp<CompAffectedByFacilities>();
                    if (comp == null) continue;
                    foreach (var linked in comp.LinkedFacilitiesListForReading)
                        if (linked.def == facility && comp.IsFacilityActive(linked)) return true;
                }
            }
            return false;
        }

        private static string TechprintHolders(ResearchProjectDef project)
        {
            if (project.heldByFactionCategoryTags.NullOrEmpty()) return null;
            var names = new List<string>();
            foreach (var tag in project.heldByFactionCategoryTags)
                foreach (var faction in Find.FactionManager.AllFactionsInViewOrder)
                    if (faction.def.categoryTag == tag && !names.Contains(faction.Name)) names.Add(faction.Name);
            return names.Count > 0 ? "ResearchTechprintsFromFactions".Translate() + "\n" + names.ToLineList("- ") : null;
        }

        private static string OtherUnfinished(ResearchProjectDef project, Def def)
        {
            var text = new StringBuilder();
            if (unlockedBy != null && unlockedBy.TryGetValue(def, out var projects))
            {
                foreach (var other in projects)
                {
                    if (other == project || other.IsFinished) continue;
                    if (text.Length > 0) text.Append(", ");
                    text.Append(other.LabelCap);
                }
            }
            return text.ToString();
        }

        /// <summary>Projects that list this one as a prerequisite. Lifetime: the session.</summary>
        private static List<ResearchProjectDef> Dependents(ResearchProjectDef project)
        {
            if (dependents == null)
            {
                dependents = new Dictionary<ResearchProjectDef, List<ResearchProjectDef>>();
                foreach (var other in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
                {
                    if (other.prerequisites == null) continue;
                    foreach (var prerequisite in other.prerequisites)
                    {
                        if (prerequisite == null) continue;
                        if (!dependents.TryGetValue(prerequisite, out var list)) dependents[prerequisite] = list = new List<ResearchProjectDef>();
                        if (!list.Contains(other)) list.Add(other);
                    }
                }
            }
            return dependents.TryGetValue(project, out var found) ? found : emptyProjects;
        }

        private static readonly List<ResearchProjectDef> emptyProjects = new List<ResearchProjectDef>();

        /// <summary>The open tab's title and description, shown while no project is selected.</summary>
        private void DrawTabInfo(Rect rect)
        {
            if (CurTab == null) return;
            Rect view = Theme.BeginScroll(rect, ref tabInfoScroll, tabInfoHeight);
            float y = 4f;
            string title = CurTab.generalTitle.CapitalizeFirst();
            float titleHeight = TitleText.Height(title, view.width, TitleSize);
            TitleText.Draw(new Rect(0f, y, view.width, titleHeight), title, Color.white, TitleSize, TextAnchor.UpperLeft, true);
            y += titleHeight + 8f;
            string description = CurTab.generalDescription ?? "";
            Text.Font = GameFont.Small;
            float height = Text.CalcHeight(description, view.width);
            Theme.Label(new Rect(0f, y, view.width, height), description, TextAnchor.UpperLeft, Theme.TextDim);
            y += height;
            tabInfoHeight = y;
            Theme.EndScroll();
        }

        /// <summary>Dev-mode finish and techprint buttons, as vanilla offers them; returns their top edge.</summary>
        private float DrawDevButtons(Rect row, ResearchProjectDef project)
        {
            float half = (row.width - Gap) / 2f;
            Rect finish = new Rect(row.x, row.y, half, row.height);
            Rect techprint = new Rect(finish.xMax + Gap, row.y, half, row.height);
            if (!Find.ResearchManager.IsCurrentProject(project) && !project.IsFinished
                && CoreWidgets.Button(finish, "Debug: Finish now", null, Theme.HighlightGray))
            {
                Find.ResearchManager.SetCurrentProject(project);
                Find.ResearchManager.FinishProject(project);
            }
            if (!project.TechprintRequirementMet && CoreWidgets.Button(techprint, "Debug: Apply techprint", null, Theme.HighlightGray))
            {
                Find.ResearchManager.ApplyTechprint(project, null);
                SoundDefOf.TechprintApplied.PlayOneShotOnCamera();
            }
            return row.y;
        }
    }
}
