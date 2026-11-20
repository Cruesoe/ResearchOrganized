using UnityEngine;
using Verse;

namespace ResearchOrganized
{
    public class ResearchOrganizedSettings : ModSettings
    {
        public bool combineAllTabs = false;
        public bool combineIndustrial = false;
        public bool separateEras = false;
        public bool crossEraLines = true;
        public int minorAnchorChildThreshold = 3;
        public int majorAnchorChildThreshold = 7;
        public bool disableCustomColors = false;
        public bool showTabProjectCounts = false;
        public bool suppressEmergenceLines = true;
        public bool writeLayoutLog = false;
        public bool hideUnavailableProjects = false;
        public bool hideFutureTechLevels = false;
        public bool fullScreenResearch = false;
        public int displayMode = DisplayMode.Classic;
        public bool rethemedFullScreen = true;
        public bool hideFinishedProjects = false;
        public bool cardUnlockIcons = true;
        public bool cardHoverCard = true;
        public bool cardProgressStrip = true;
        public bool cardChips = true;
        public bool curvedLines = true;
        public bool highlightChain = true;
        public bool openOnCurrentProject = false;
        public bool detailsFolded = false;
        public bool queueFolded = false;
        public bool eraBands = true;
        public bool showTimeLeft = true;
        public int cardSize = 1;
        public int maxPerColumn = 16;
        public float columnSpacing = 1f;
        public float rowSpacing = 1f;

        public Color colorUndefined = new Color(0.60f, 0.60f, 0.60f);
        public Color colorAnimal = new Color(0.45f, 0.33f, 0.24f);
        public Color colorNeolithic = new Color(0.40f, 0.00f, 0.00f);
        public Color colorMedieval = new Color(0.40f, 0.40f, 0.00f);
        public Color colorIndustrial = new Color(0.00f, 0.40f, 0.00f);
        public Color colorSpacer = new Color(0.00f, 0.40f, 0.40f);
        public Color colorUltra = new Color(0.00f, 0.00f, 0.40f);
        public Color colorArchotech = new Color(0.60f, 0.30f, 0.60f);
        public Color colorAnomaly = new Color(0.30f, 0.00f, 0.30f);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref combineAllTabs, "combineAllTabs", false);
            Scribe_Values.Look(ref combineIndustrial, "combineIndustrial", false);
            Scribe_Values.Look(ref separateEras, "separateEras", false);
            Scribe_Values.Look(ref crossEraLines, "crossEraLines", true);
            Scribe_Values.Look(ref minorAnchorChildThreshold, "minorAnchorChildThreshold", 3);
            Scribe_Values.Look(ref majorAnchorChildThreshold, "majorAnchorChildThreshold", 7);
            Scribe_Values.Look(ref disableCustomColors, "disableCustomColors", false);
            Scribe_Values.Look(ref showTabProjectCounts, "showTabProjectCounts", false);
            Scribe_Values.Look(ref suppressEmergenceLines, "suppressEmergenceLines", true);
            Scribe_Values.Look(ref writeLayoutLog, "writeLayoutLog", false);
            Scribe_Values.Look(ref hideUnavailableProjects, "hideUnavailableProjects", false);
            Scribe_Values.Look(ref hideFutureTechLevels, "hideFutureTechLevels", false);
            Scribe_Values.Look(ref fullScreenResearch, "fullScreenResearch", false);
            Scribe_Values.Look(ref displayMode, "displayMode", DisplayMode.Classic);
            Scribe_Values.Look(ref rethemedFullScreen, "rethemedFullScreen", true);
            Scribe_Values.Look(ref hideFinishedProjects, "hideFinishedProjects", false);
            Scribe_Values.Look(ref cardUnlockIcons, "cardUnlockIcons", true);
            Scribe_Values.Look(ref cardHoverCard, "cardHoverCard", true);
            Scribe_Values.Look(ref cardProgressStrip, "cardProgressStrip", true);
            Scribe_Values.Look(ref cardChips, "cardChips", true);
            Scribe_Values.Look(ref curvedLines, "curvedLines", true);
            Scribe_Values.Look(ref highlightChain, "highlightChain", true);
            Scribe_Values.Look(ref openOnCurrentProject, "openOnCurrentProject", false);
            Scribe_Values.Look(ref detailsFolded, "detailsFolded", false);
            Scribe_Values.Look(ref queueFolded, "queueFolded", false);
            Scribe_Values.Look(ref eraBands, "eraBands", true);
            Scribe_Values.Look(ref showTimeLeft, "showTimeLeft", true);
            Scribe_Values.Look(ref cardSize, "cardSize", 1);
            Scribe_Values.Look(ref maxPerColumn, "maxPerColumn", 16);
            Scribe_Values.Look(ref columnSpacing, "columnSpacing", 1f);
            Scribe_Values.Look(ref rowSpacing, "rowSpacing", 1f);
            Scribe_Values.Look(ref colorUndefined, "colorUndefined", new Color(0.60f, 0.60f, 0.60f));
            Scribe_Values.Look(ref colorAnimal, "colorAnimal", new Color(0.45f, 0.33f, 0.24f));
            Scribe_Values.Look(ref colorNeolithic, "colorNeolithic", new Color(0.40f, 0.00f, 0.00f));
            Scribe_Values.Look(ref colorMedieval, "colorMedieval", new Color(0.40f, 0.40f, 0.00f));
            Scribe_Values.Look(ref colorIndustrial, "colorIndustrial", new Color(0.00f, 0.40f, 0.00f));
            Scribe_Values.Look(ref colorSpacer, "colorSpacer", new Color(0.00f, 0.40f, 0.40f));
            Scribe_Values.Look(ref colorUltra, "colorUltra", new Color(0.00f, 0.00f, 0.40f));
            Scribe_Values.Look(ref colorArchotech, "colorArchotech", new Color(0.60f, 0.30f, 0.60f));
            Scribe_Values.Look(ref colorAnomaly, "colorAnomaly", new Color(0.30f, 0.00f, 0.30f));
        }

        public void CopyFrom(ResearchOrganizedSettings defaults)
        {
            combineAllTabs = defaults.combineAllTabs;
            combineIndustrial = defaults.combineIndustrial;
            separateEras = defaults.separateEras;
            crossEraLines = defaults.crossEraLines;
            minorAnchorChildThreshold = defaults.minorAnchorChildThreshold;
            majorAnchorChildThreshold = defaults.majorAnchorChildThreshold;
            disableCustomColors = defaults.disableCustomColors;
            showTabProjectCounts = defaults.showTabProjectCounts;
            suppressEmergenceLines = defaults.suppressEmergenceLines;
            writeLayoutLog = defaults.writeLayoutLog;
            hideUnavailableProjects = defaults.hideUnavailableProjects;
            hideFutureTechLevels = defaults.hideFutureTechLevels;
            fullScreenResearch = defaults.fullScreenResearch;
            displayMode = defaults.displayMode;
            rethemedFullScreen = defaults.rethemedFullScreen;
            hideFinishedProjects = defaults.hideFinishedProjects;
            cardUnlockIcons = defaults.cardUnlockIcons;
            cardHoverCard = defaults.cardHoverCard;
            cardProgressStrip = defaults.cardProgressStrip;
            cardChips = defaults.cardChips;
            curvedLines = defaults.curvedLines;
            highlightChain = defaults.highlightChain;
            openOnCurrentProject = defaults.openOnCurrentProject;
            eraBands = defaults.eraBands;
            showTimeLeft = defaults.showTimeLeft;
            cardSize = defaults.cardSize;
            maxPerColumn = defaults.maxPerColumn;
            columnSpacing = defaults.columnSpacing;
            rowSpacing = defaults.rowSpacing;
            colorUndefined = defaults.colorUndefined;
            colorAnimal = defaults.colorAnimal;
            colorNeolithic = defaults.colorNeolithic;
            colorMedieval = defaults.colorMedieval;
            colorIndustrial = defaults.colorIndustrial;
            colorSpacer = defaults.colorSpacer;
            colorUltra = defaults.colorUltra;
            colorArchotech = defaults.colorArchotech;
            colorAnomaly = defaults.colorAnomaly;
        }
    }
}
