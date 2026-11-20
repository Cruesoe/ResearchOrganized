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
        /// <summary>Pixels per unit of researchViewX and researchViewY, and the card size, for one card size setting.</summary>
        private struct CardMetrics
        {
            public float perX;
            public float perY;
            public float width;
            public float height;
            public float iconSize;
            public bool twoRows;
        }

        // Compact, Normal (vanilla's spacing and card width) and Large.
        private static readonly CardMetrics[] Sizes =
        {
            new CardMetrics { perX = 170f, perY = 78f, width = 130f, height = 40f, iconSize = 14f, twoRows = false },
            new CardMetrics { perX = 190f, perY = 100f, width = 140f, height = 52f, iconSize = 16f, twoRows = true },
            new CardMetrics { perX = 230f, perY = 125f, width = 180f, height = 66f, iconSize = 20f, twoRows = true }
        };

        /// <summary>The card size in use, read from the settings at the start of each canvas draw.</summary>
        private static CardMetrics M = Sizes[1];
        private int metricsSize = -1;
        private float metricsColumnSpacing = -1f;
        private float metricsRowSpacing = -1f;

        private float CardWidth => M.width;
        private float CardHeight => M.height;
        private const float KnowledgeStrip = 14f;
        private const float CanvasMargin = 24f;
        private const int CurveSegments = 10;

        // Largest first, as Core's ZoomView expects: wheel down steps towards the end of the list.
        private static readonly float[] ZoomSteps = { 1.3f, 1.15f, 1f, 0.85f, 0.7f, 0.6f, 0.5f };

        /// <summary>Each tab's pan and zoom for this game; cleared when another game is loaded.</summary>
        private readonly Dictionary<ResearchTabDef, ZoomView> views = new Dictionary<ResearchTabDef, ZoomView>();
        private Game viewsGame;

        /// <summary>The current tab's visible projects and their bounds, rebuilt when the tab, the visible list or the layout changes.</summary>
        private readonly List<ResearchProjectDef> tabProjects = new List<ResearchProjectDef>();
        private readonly HashSet<ResearchProjectDef> tabProjectSet = new HashSet<ResearchProjectDef>();
        private ResearchTabDef projectsTab;
        private List<ResearchProjectDef> projectsSource;
        private int projectsLayout = -1;
        private int projectsSettings = -1;
        private Rect tabBounds;

        /// <summary>The selected project's prerequisites, all the way back, on the current tab.</summary>
        private readonly HashSet<ResearchProjectDef> chain = new HashSet<ResearchProjectDef>();
        private ResearchProjectDef chainFor;
        private ResearchTabDef chainTab;

        private bool fitPending;
        private ResearchProjectDef centerPending;
        private bool selectingHere;
        private ResearchProjectDef lastSelected;
        private readonly HashSet<ResearchTabDef> homedTabs = new HashSet<ResearchTabDef>();

        private string notDiscovered;

        private void OnOpenCanvas()
        {
            if (Current.Game != viewsGame)
            {
                views.Clear();
                homedTabs.Clear();
                viewsGame = Current.Game;
                centerPending = Selected;
            }
            else if (ResearchOrganizedMod.settings.openOnCurrentProject)
            {
                centerPending = Selected;
            }
            projectsSource = null;
            // Vanilla reselects the current project on every open; only the cases above bring it into view.
            lastSelected = Selected;
        }

        private void OnSelectionChanged() => chainFor = null;

        private void CenterOnNextDraw(ResearchProjectDef project) => centerPending = project;

        private void FitNextDraw() => fitPending = true;

        private ZoomView View(ResearchTabDef tab)
        {
            if (!views.TryGetValue(tab, out var view))
            {
                view = new ZoomView(ZoomSteps);
                views[tab] = view;
            }
            return view;
        }

        private string ZoomLabel
        {
            get
            {
                float zoom = CurTab != null && views.TryGetValue(CurTab, out var view) ? view.zoom : 1f;
                return ZoomPercent(zoom);
            }
        }

        private static readonly Dictionary<int, string> percentLabels = new Dictionary<int, string>();

        private static string ZoomPercent(float zoom)
        {
            int percent = Mathf.RoundToInt(zoom * 100f);
            if (!percentLabels.TryGetValue(percent, out string label)) percentLabels[percent] = label = percent + "%";
            return label;
        }

        private Rect lastCanvas;

        private void ZoomBy(int direction)
        {
            if (CurTab == null) return;
            var view = View(CurTab);
            int nearest = 0;
            for (int i = 1; i < ZoomSteps.Length; i++)
                if (Mathf.Abs(ZoomSteps[i] - view.zoom) < Mathf.Abs(ZoomSteps[nearest] - view.zoom)) nearest = i;
            int next = Mathf.Clamp(nearest - direction, 0, ZoomSteps.Length - 1);
            Rect local = lastCanvas.AtZero();
            Vector2 center = view.ToContent(local.center);
            view.Home(local.center - center * ZoomSteps[next], ZoomSteps[next]);
        }

        private static float PosX(ResearchProjectDef project) => project.ResearchViewX * M.perX;
        private static float PosY(ResearchProjectDef project) => project.ResearchViewY * M.perY;

        private static float ExtraWidth(ResearchProjectDef project) =>
            ModsConfig.AnomalyActive && project.knowledgeCategory != null ? KnowledgeStrip : 0f;

        private static Rect CardRect(ResearchProjectDef project) =>
            new Rect(PosX(project), PosY(project), M.width + ExtraWidth(project), M.height);

        /// <summary>Picks up a changed card size or spacing; every tab is homed again, since its positions all moved.</summary>
        private void UpdateMetrics()
        {
            var settings = ResearchOrganizedMod.settings;
            int size = Mathf.Clamp(settings.cardSize, 0, Sizes.Length - 1);
            if (size == metricsSize && settings.columnSpacing == metricsColumnSpacing && settings.rowSpacing == metricsRowSpacing) return;
            bool changed = metricsSize >= 0;
            metricsSize = size;
            metricsColumnSpacing = settings.columnSpacing;
            metricsRowSpacing = settings.rowSpacing;
            M = Sizes[size];
            // Scales the gap between neighboring cards, one layout step apart, leaving the cards' own size alone.
            float xStep = Mathf.Max(0.1f, ResearchOrganizedMain.GlobalXStep), yStep = Mathf.Max(0.1f, ResearchOrganizedMain.GlobalYStep);
            M.perX = (M.width + (xStep * M.perX - M.width) * metricsColumnSpacing) / xStep;
            M.perY = (M.height + (yStep * M.perY - M.height) * metricsRowSpacing) / yStep;
            if (!changed) return;
            homedTabs.Clear();
            projectsSource = null;
            centerPending = Selected;
        }

        /// <summary>Keeps the tree filling the canvas: a tree wider or taller than the canvas can't be dragged past its edges,
        /// and one that fits sits against the top-left corner.</summary>
        private void KeepTreeInView(ZoomView view, Rect local)
        {
            Rect tree = view.ToScreen(tabBounds);
            if (tree.width <= local.width) view.offset.x += local.x - tree.x;
            else if (tree.x > local.x) view.offset.x += local.x - tree.x;
            else if (tree.xMax < local.xMax) view.offset.x += local.xMax - tree.xMax;
            if (tree.height <= local.height) view.offset.y += local.y - tree.y;
            else if (tree.y > local.y) view.offset.y += local.y - tree.y;
            else if (tree.yMax < local.yMax) view.offset.y += local.yMax - tree.yMax;
        }

        private readonly Dictionary<ResearchTabDef, Vector2> tabRows = new Dictionary<ResearchTabDef, Vector2>();

        /// <summary>The canvas height the tallest tab needs at full zoom: its cards, the margin round them and the era titles.</summary>
        private float TallestTabHeight()
        {
            UpdateMetrics();
            tabRows.Clear();
            foreach (var project in VisibleResearchProjects)
            {
                if (project.tab == null) continue;
                float top = PosY(project);
                tabRows[project.tab] = tabRows.TryGetValue(project.tab, out Vector2 rows)
                    ? new Vector2(Mathf.Min(rows.x, top), Mathf.Max(rows.y, top))
                    : new Vector2(top, top);
            }
            float tallest = 0f;
            foreach (Vector2 rows in tabRows.Values) tallest = Mathf.Max(tallest, rows.y - rows.x);
            tabRows.Clear();
            float height = tallest + M.height + CanvasMargin * 2f;
            if (ResearchOrganizedMod.settings.eraBands) height += EraLabelHeight;
            return height;
        }

        private void RefreshTabProjects()
        {
            var visible = VisibleResearchProjects;
            int layout = ResearchOrganizedMain.LayoutVersion;
            int settingsVersion = ResearchOrganizedMod.SettingsVersion;
            if (projectsTab == CurTab && ReferenceEquals(projectsSource, visible) && projectsLayout == layout && projectsSettings == settingsVersion) return;
            projectsTab = CurTab;
            projectsSource = visible;
            projectsLayout = layout;
            projectsSettings = settingsVersion;
            chainFor = null;
            tabProjects.Clear();
            tabProjectSet.Clear();
            bool any = false;
            Rect bounds = default;
            foreach (var project in visible)
            {
                if (project.tab != CurTab) continue;
                tabProjects.Add(project);
                tabProjectSet.Add(project);
                Rect card = CardRect(project);
                bounds = any ? Rect.MinMaxRect(Mathf.Min(bounds.xMin, card.xMin), Mathf.Min(bounds.yMin, card.yMin),
                    Mathf.Max(bounds.xMax, card.xMax), Mathf.Max(bounds.yMax, card.yMax)) : card;
                any = true;
            }
            tabBounds = any ? bounds.ExpandedBy(CanvasMargin) : new Rect(0f, 0f, 400f, 300f);
            RefreshEraBands();
            if (eraBands.Count > 0) tabBounds.yMin -= EraLabelHeight;
        }

        // Era bands: a tinted column behind each era, with its name along the top, on tabs laid out era by era.

        private const float EraLabelHeight = 56f;
        private const int EraTitleSize = 26;

        private struct EraBand
        {
            public float xMin;
            public float xMax;
            public string label;
            public float labelWidth;
            public Color color;
        }

        private readonly List<EraBand> eraBands = new List<EraBand>();
        private static readonly Dictionary<TechLevel, string> eraLabels = new Dictionary<TechLevel, string>();

        /// <summary>One band per era when the tab holds more than one and their columns don't overlap; none otherwise.</summary>
        private void RefreshEraBands()
        {
            eraBands.Clear();
            if (!ResearchOrganizedMod.settings.eraBands || tabProjects.Count == 0) return;
            var ranges = new SortedDictionary<TechLevel, EraBand>();
            foreach (var project in tabProjects)
            {
                Rect card = CardRect(project);
                if (ranges.TryGetValue(project.techLevel, out var band))
                {
                    band.xMin = Mathf.Min(band.xMin, card.xMin);
                    band.xMax = Mathf.Max(band.xMax, card.xMax);
                }
                else
                {
                    if (!eraLabels.TryGetValue(project.techLevel, out string label))
                        eraLabels[project.techLevel] = label = project.techLevel.ToStringHuman().ToUpper();
                    band = new EraBand { xMin = card.xMin, xMax = card.xMax, label = label, labelWidth = TitleText.Width(label, EraTitleSize), color = ResearchOrganizedMain.EraColor(project) };
                }
                ranges[project.techLevel] = band;
            }
            if (ranges.Count < 2) return;
            foreach (var band in ranges.Values) eraBands.Add(band);
            eraBands.Sort((a, b) => a.xMin.CompareTo(b.xMin));
            for (int i = 1; i < eraBands.Count; i++)
            {
                if (eraBands[i].xMin < eraBands[i - 1].xMax)
                {
                    eraBands.Clear();
                    return;
                }
            }
            // Each band reaches halfway into the gap on either side.
            for (int i = 0; i < eraBands.Count; i++)
            {
                var band = eraBands[i];
                float left = i > 0 ? (eraBands[i - 1].xMax + band.xMin) / 2f : band.xMin - CanvasMargin / 2f;
                float right = i < eraBands.Count - 1 ? (band.xMax + eraBands[i + 1].xMin) / 2f : band.xMax + CanvasMargin / 2f;
                band.xMin = left;
                band.xMax = right;
                eraBands[i] = band;
            }
        }

        private void DrawEraBands(ZoomView view, Rect local)
        {
            if (eraBands.Count == 0) return;
            bool colors = !ResearchOrganizedMod.settings.disableCustomColors;
            for (int i = 0; i < eraBands.Count; i++)
            {
                var band = eraBands[i];
                float xMin = Mathf.Max(local.xMin, view.ToScreen(new Vector2(band.xMin, 0f)).x);
                float xMax = Mathf.Min(local.xMax, view.ToScreen(new Vector2(band.xMax, 0f)).x);
                if (xMax <= xMin) continue;
                Color color = colors ? band.color : Theme.HighlightGray;
                Rect column = new Rect(xMin, local.y, xMax - xMin, local.height);
                if (Event.current.type == EventType.Repaint)
                {
                    Theme.Fill(column, Faces.Fade(color, i % 2 == 0 ? 0.07f : 0.11f));
                    if (i > 0) Theme.Fill(new Rect(xMin, local.y, 1f, local.height), Faces.Fade(color, 0.3f));
                }
            }
        }

        /// <summary>Each era's title, pinned to the top of the canvas over a shaded strip that cards pass beneath, with a rule under it.</summary>
        private void DrawEraTitles(ZoomView view, Rect local)
        {
            if (eraBands.Count == 0) return;
            bool colors = !ResearchOrganizedMod.settings.disableCustomColors;
            if (Event.current.type == EventType.Repaint)
                Theme.Fill(new Rect(local.x, local.y, local.width, EraLabelHeight - 4f), Faces.Fade(Theme.BGDark, 0.82f));
            for (int i = 0; i < eraBands.Count; i++)
            {
                var band = eraBands[i];
                float xMin = Mathf.Max(local.xMin, view.ToScreen(new Vector2(band.xMin, 0f)).x);
                float xMax = Mathf.Min(local.xMax, view.ToScreen(new Vector2(band.xMax, 0f)).x);
                if (xMax - xMin < 60f) continue;
                Color color = colors ? band.color : Theme.HighlightGray;
                Rect title = new Rect(xMin + 8f, local.y + 6f, xMax - xMin - 16f, EraTitleSize + 10f);
                TitleText.Draw(title, band.label, Color.Lerp(color, Color.white, 0.7f), EraTitleSize, TextAnchor.MiddleCenter);
                if (Event.current.type == EventType.Repaint)
                {
                    float rule = Mathf.Min(title.width, band.labelWidth + 24f);
                    Theme.Fill(new Rect(title.center.x - rule / 2f, title.yMax + 2f, rule, 2f), Faces.Fade(color, 0.9f));
                }
            }
        }

        private void RefreshChain()
        {
            var selected = Selected;
            if (chainFor == selected && chainTab == CurTab) return;
            chainFor = selected;
            chainTab = CurTab;
            chain.Clear();
            if (selected == null || !ResearchOrganizedMod.settings.highlightChain) return;
            var stack = new Stack<ResearchProjectDef>();
            stack.Push(selected);
            while (stack.Count > 0)
            {
                var project = stack.Pop();
                if (!chain.Add(project) || project.prerequisites == null) continue;
                foreach (var prerequisite in project.prerequisites)
                    if (prerequisite != null && tabProjectSet.Contains(prerequisite)) stack.Push(prerequisite);
            }
        }

        private void DrawCanvas(Rect rect)
        {
            lastCanvas = rect;
            if (Event.current.type == EventType.Repaint)
            {
                Rounded.BeginBatch();
                try
                {
                    Rounded.Fill(rect, Theme.BGDark);
                    Rounded.Border(rect, Color.Lerp(Theme.BGDark, Theme.HighlightGray, 0.35f));
                }
                finally
                {
                    Rounded.EndBatch();
                }
            }
            if (CurTab == null) return;

            if (!Find.ResearchManager.TabInfoVisible(CurTab))
            {
                notDiscovered = notDiscovered ?? "ResearchNotDiscovered".Translate().ToString();
                CoreWidgets.Line(rect, notDiscovered, Theme.TextFaint, GameFont.Small, TextAnchor.MiddleCenter);
                return;
            }

            UpdateMetrics();
            RefreshTabProjects();
            var view = View(CurTab);
            Rect local = rect.AtZero().ContractedBy(1f);
            view.content = tabBounds;

            // A project selected from outside (an alert, an info card link) is brought into view.
            if (Selected != lastSelected)
            {
                if (!selectingHere && Selected != null && Selected.tab == CurTab) centerPending = Selected;
                lastSelected = Selected;
            }
            selectingHere = false;
            RefreshChain();

            if (fitPending)
            {
                fitPending = false;
                view.Fit(local, tabBounds, 0f, 1f);
            }
            else if (homedTabs.Add(CurTab))
            {
                // First look at this tab: full size, top-left corner of the projects in view.
                view.Home(new Vector2(local.x, local.y) - tabBounds.position, 1f);
            }
            if (centerPending != null && centerPending.tab == CurTab && tabProjectSet.Contains(centerPending))
            {
                view.CentreOn(local, CardRect(centerPending).center);
                centerPending = null;
            }

            Widgets.BeginGroup(rect);
            try
            {
                HandleCardClick(view, local);
                view.HandleInput(local);
                KeepTreeInView(view, local);
                DrawEraBands(view, local);
                if (Event.current.type == EventType.Repaint) DrawLines(view, local);
                DrawCards(view, local);
                DrawEraTitles(view, local);
            }
            finally
            {
                Widgets.EndGroup();
            }
        }

        private void HandleCardClick(ZoomView view, Rect local)
        {
            Event current = Event.current;
            if (current.type != EventType.MouseDown || current.button != 0 || !Mouse.IsOver(local)) return;
            for (int i = tabProjects.Count - 1; i >= 0; i--)
            {
                var project = tabProjects[i];
                if (!view.ToScreen(CardRect(project)).Contains(current.mousePosition)) continue;
                // Shift-click adds the project to the queue, or takes it out again.
                if (current.shift && ProjectQueue.Available && !project.IsHidden)
                {
                    ToggleQueued(project);
                    current.Use();
                    return;
                }
                if (!project.IsHidden && Selected != project)
                {
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    Selected = project;
                    selectingHere = true;
                    OnSelectionChanged();
                }
                current.Use();
                return;
            }
        }

        private void DrawLines(ZoomView view, Rect local)
        {
            var settings = ResearchOrganizedMod.settings;
            Color normal = Color.Lerp(Theme.BGLight, Theme.TextFaint, 0.35f);
            Color lit = Theme.HighlightBlue;
            var selected = Selected;
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var project in tabProjects)
                {
                    var prerequisites = project.prerequisites;
                    if (prerequisites == null) continue;
                    for (int j = 0; j < prerequisites.Count; j++)
                    {
                        var prerequisite = prerequisites[j];
                        if (prerequisite == null || !tabProjectSet.Contains(prerequisite)) continue;
                        if (ResearchOrganizedMain.PrerequisiteLineTab(CurTab, this, project, prerequisite) == null) continue;
                        bool highlighted = selected == project || selected == prerequisite || (chain.Contains(project) && chain.Contains(prerequisite));
                        if (highlighted != (pass == 1)) continue;
                        Vector2 from = view.ToScreen(new Vector2(PosX(prerequisite) + CardWidth + ExtraWidth(prerequisite), PosY(prerequisite) + CardHeight / 2f));
                        Vector2 to = view.ToScreen(new Vector2(PosX(project), PosY(project) + CardHeight / 2f));
                        if (!LineInView(from, to, local)) continue;
                        DrawConnection(from, to, highlighted ? lit : normal, highlighted ? 3f : 2f, settings.curvedLines);
                    }
                }
            }
        }

        private static bool LineInView(Vector2 a, Vector2 b, Rect view) =>
            Mathf.Max(a.x, b.x) >= view.xMin && Mathf.Min(a.x, b.x) <= view.xMax && Mathf.Max(a.y, b.y) >= view.yMin && Mathf.Min(a.y, b.y) <= view.yMax;

        /// <summary>A straight line, or a horizontal S-curve drawn as short segments.</summary>
        private static void DrawConnection(Vector2 from, Vector2 to, Color color, float width, bool curved)
        {
            if (!curved || Mathf.Abs(from.y - to.y) < 1f)
            {
                Widgets.DrawLine(from, to, color, width);
                return;
            }
            float pull = Mathf.Max(20f, Mathf.Abs(to.x - from.x) * 0.5f);
            Vector2 c1 = new Vector2(from.x + pull, from.y);
            Vector2 c2 = new Vector2(to.x - pull, to.y);
            Vector2 previous = from;
            for (int i = 1; i <= CurveSegments; i++)
            {
                float t = i / (float)CurveSegments;
                float u = 1f - t;
                Vector2 point = u * u * u * from + 3f * u * u * t * c1 + 3f * u * t * t * c2 + t * t * t * to;
                Widgets.DrawLine(previous, point, color, width);
                previous = point;
            }
        }
    }
}
