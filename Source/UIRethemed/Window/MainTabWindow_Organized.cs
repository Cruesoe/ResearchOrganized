using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UIRethemed.Core;
using UnityEngine;
using Verse;
using ResearchOrganized;

namespace ResearchOrganizedUIRethemed
{
    /// <summary>
    /// The research window, drawn with UI Rethemed - Core: title bar, tab strip, filter bar, a pan-and-zoom tree,
    /// a foldable details panel and the active project bar. A subclass of the vanilla window, so everything that finds
    /// the research window by type and calls Select on it keeps working, and vanilla's own starting logic is reused.
    /// </summary>
    [StaticConstructorOnStartup]
    public partial class MainTabWindow_Organized : MainTabWindow_Research
    {
        private const string FailsafePart = "ResearchOrganized.Window";
        private const float TitleHeight = 40f;
        private const float Gap = 6f;
        private const float ActiveBarHeight = 64f;
        private const float PanelWidth = 380f;

        private static readonly AccessTools.FieldRef<MainTabWindow_Research, ResearchProjectDef> selectedProjectRef =
            AccessTools.FieldRefAccess<MainTabWindow_Research, ResearchProjectDef>("selectedProject");

        private delegate void UpdateSelectedDelegate(MainTabWindow_Research window, ResearchManager manager);
        private static readonly UpdateSelectedDelegate updateSelectedProject =
            AccessTools.MethodDelegate<UpdateSelectedDelegate>(AccessTools.Method(typeof(MainTabWindow_Research), "UpdateSelectedProject"));

        private float openedAt;
        private Rect drawingRect;
        private Action drawWindow;
        private Action close;

        public MainTabWindow_Organized()
        {
            doWindowBackground = false;
        }

        protected override float Margin => 0f;

        private const float MinHeight = 520f;

        public override Vector2 InitialSize =>
            new Vector2(UI.screenWidth, ResearchOrganizedMod.settings.rethemedFullScreen ? UI.screenHeight - 35f : FittedHeight());

        /// <summary>Out of full screen, just tall enough for the window's bars and the tallest tab, within the screen.</summary>
        private float FittedHeight()
        {
            float panelWidth = SidePanel.Width(ResearchOrganizedMod.settings.detailsFolded, PanelWidth);
            float treeWidth = UI.screenWidth - Gap * 4f - QueuePanelWidth - panelWidth;
            float chrome = Gap + TitleHeight + Gap + ActiveBarHeight + Gap + TabStripHeight(treeWidth) + Gap + FilterBarHeight + Gap + Gap;
            return Mathf.Clamp(chrome + TallestTabHeight(), MinHeight, UI.screenHeight - 35f);
        }

        private int sizedLayout = -1;
        private int sizedSettings = -1;

        /// <summary>Out of full screen, refits the window once the layout or settings change while it is open.</summary>
        private void RefitIfChanged()
        {
            int layout = ResearchOrganizedMain.LayoutVersion, settingsVersion = ResearchOrganizedMod.SettingsVersion;
            if (layout == sizedLayout && settingsVersion == sizedSettings) return;
            bool first = sizedLayout < 0;
            sizedLayout = layout;
            sizedSettings = settingsVersion;
            if (!first && !ResearchOrganizedMod.settings.rethemedFullScreen) SetInitialSizeAndPosition();
        }

        protected ResearchProjectDef Selected
        {
            get => selectedProjectRef(this);
            set => selectedProjectRef(this) = value;
        }

        public override void PreOpen()
        {
            // Projects Node Research parked on its own tab go back to theirs while this window is open.
            NodeResearchCompat.RestoreTabs();
            base.PreOpen();
            doWindowBackground = false;
            openedAt = Time.realtimeSinceStartup;
            OnOpenCanvas();
        }

        public override void DoWindowContents(Rect inRect)
        {
            windowRect.width = UI.screenWidth;
            if (Event.current.type == EventType.Layout) RefitIfChanged();
            ResearchOrganizedMain.RefreshTabStripIfDirty(this);
            ResearchOrganizedMain.UpdateTabLabelsWithCounts(this);

            // Drawn inside Core's failsafe; after an error this part is turned off and the vanilla window draws instead.
            if (!Failsafe.Failed(FailsafePart))
            {
                drawingRect = inRect;
                if (drawWindow == null)
                {
                    drawWindow = DrawWindow;
                    close = () => Close();
                }
                if (Failsafe.Run(FailsafePart, drawWindow))
                    return;
            }
            doWindowBackground = true;
            base.DoWindowContents(inRect.ContractedBy(18f));
        }

        private void DrawWindow()
        {
            Theme.BeginMod(ResearchOrganizedMod.Instance);
            try
            {
                Rect inRect = drawingRect;
                // The window spans the screen's width above the bottom bar, so no corner is rounded.
                Rounded.WindowBackground(inRect, Corners.None);
                Rect inner = inRect.ContractedBy(Gap);

                Rect titleBar = new Rect(inner.x, inner.y, inner.width, TitleHeight);
                DrawTitleBar(titleBar);

                // The current project's progress runs under the title bar; queue left, tree in the middle, details right.
                Rect activeBar = new Rect(inner.x, titleBar.yMax + Gap, inner.width, ActiveBarHeight);
                Rect body = new Rect(inner.x, activeBar.yMax + Gap, inner.width, inner.yMax - activeBar.yMax - Gap);

                float panelWidth = SidePanel.Width(ResearchOrganizedMod.settings.detailsFolded, PanelWidth);
                Rect panel = new Rect(body.xMax - panelWidth, body.y, panelWidth, body.height);
                Rect queue = new Rect(body.x, body.y, QueuePanelWidth, body.height);
                Rect tree = new Rect(queue.xMax + Gap, body.y, panel.x - Gap - queue.xMax - Gap, body.height);

                Rect strip = new Rect(tree.x, tree.y, tree.width, TabStripHeight(tree.width));
                Rect filters = new Rect(tree.x, strip.yMax + Gap, tree.width, FilterBarHeight);
                Rect canvas = new Rect(tree.x, filters.yMax + Gap, tree.width, tree.yMax - filters.yMax - Gap);

                DrawTabStrip(strip);
                DrawFilterBar(filters);
                DrawCanvas(canvas);
                DrawQueuePanel(queue);
                DrawDetailsPanel(panel);
                DrawActiveBar(activeBar);
            }
            finally
            {
                Theme.CloseScrolls();
                Theme.Reset();
                Theme.EndMod();
            }
        }

        private static Texture2D titleIcon;
        private static Texture2D fullScreenIcon;
        private static Texture2D exitFullScreenIcon;
        private static Texture2D nodeResearchIcon;
        private static Texture2D semiRandomIcon;
        private static Texture2D queueIcon;

        private void DrawTitleBar(Rect rect)
        {
            if (titleIcon == null)
            {
                titleIcon = CoreWidgets.Icon("flask");
                fullScreenIcon = CoreWidgets.Icon("arrows-out");
                exitFullScreenIcon = CoreWidgets.Icon("arrows-in");
                nodeResearchIcon = CoreWidgets.Icon("tree-structure");
                semiRandomIcon = CoreWidgets.Icon("dice-five");
                queueIcon = CoreWidgets.Icon("list-numbers");
            }
            float age = ThemeMotion.On ? Time.realtimeSinceStartup - openedAt : float.MaxValue;
            Rect room = CoreWidgets.TitleBar(rect, titleIcon, def.LabelCap, age, close, ResearchOrganizedMod.Instance);

            float right = room.xMax;
            float y = Mathf.Round(rect.center.y - 12f);
            var settings = ResearchOrganizedMod.settings;
            bool fullScreen = settings.rethemedFullScreen;
            if (HeaderTool(ref right, y, fullScreen ? exitFullScreenIcon : fullScreenIcon,
                    fullScreen ? "ResearchOrganized_ExitFullScreenTip" : "ResearchOrganized_EnterFullScreenTip"))
            {
                settings.rethemedFullScreen = !fullScreen;
                settings.Write();
                SetInitialSizeAndPosition();
            }
            // Buttons to the other research windows that are installed.
            if (WindowHandover.CanOpenQueue && HeaderTool(ref right, y, queueIcon, "ResearchOrganized_OpenResearchQueue"))
                WindowHandover.OpenQueue(this);
            if (WindowHandover.CanOpenSemiRandom && HeaderTool(ref right, y, semiRandomIcon, "ResearchOrganized_OpenSemiRandom"))
                WindowHandover.OpenSemiRandom(this);
            if (WindowHandover.CanOpenNodeResearch && HeaderTool(ref right, y, nodeResearchIcon, "ResearchOrganized_OpenNodeResearch"))
                WindowHandover.OpenNodeResearch(this);

            ResearchOrganizedMain.TotalCounts(out int finished, out int total);
            if (totalsLabel == null || finished != totalsFinished || total != totalsTotal)
            {
                totalsFinished = finished;
                totalsTotal = total;
                totalsLabel = "ResearchOrganized_TitleCounts".Translate(finished, total);
            }
            CoreWidgets.Line(new Rect(room.x + 12f, room.y, Mathf.Max(0f, right - room.x - 16f), room.height), totalsLabel, Theme.TextDim);
        }

        private string totalsLabel;
        private int totalsFinished = -1;
        private int totalsTotal = -1;

        /// <summary>Selects a project and switches to its tab, as vanilla's Select does, then brings it into view.</summary>
        private void SelectProject(ResearchProjectDef project, bool center)
        {
            if (project == null) return;
            if (CurTab != project.tab) CurTab = project.tab;
            Selected = project;
            lastSelected = project;
            OnSelectionChanged();
            if (center) CenterOnNextDraw(project);
        }

        private void ChangeTab(ResearchTabDef tab)
        {
            if (tab == null || tab == CurTab) return;
            CurTab = tab;
            updateSelectedProject?.Invoke(this, Find.ResearchManager);
            lastSelected = Selected;
            OnSelectionChanged();
        }
    }
}
