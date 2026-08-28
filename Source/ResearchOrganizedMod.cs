using System;
using UnityEngine;
using Verse;

namespace ResearchOrganized
{
    public class ResearchOrganizedSettings : ModSettings
    {
        public bool combineIndustrial = false;
        public bool disableCustomColors = false;

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
            Scribe_Values.Look(ref combineIndustrial, "combineIndustrial", false);
            Scribe_Values.Look(ref disableCustomColors, "disableCustomColors", false);
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
            combineIndustrial = defaults.combineIndustrial;
            disableCustomColors = defaults.disableCustomColors;
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

    public class ResearchOrganizedMod : Mod
    {
        public static ResearchOrganizedSettings settings;


        private static readonly Color[] Palette = new Color[]
        {
            new Color(0.40f, 0.00f, 0.00f), new Color(0.80f, 0.30f, 0.30f), new Color(0.80f, 0.50f, 0.10f), new Color(0.45f, 0.33f, 0.24f),
            new Color(0.80f, 0.80f, 0.10f), new Color(0.40f, 0.40f, 0.00f), new Color(0.00f, 0.40f, 0.00f), new Color(0.30f, 0.70f, 0.30f),
            new Color(0.00f, 0.40f, 0.40f), new Color(0.30f, 0.80f, 0.80f), new Color(0.00f, 0.00f, 0.40f), new Color(0.30f, 0.30f, 0.80f),
            new Color(0.30f, 0.00f, 0.30f), new Color(0.60f, 0.30f, 0.60f), new Color(0.30f, 0.30f, 0.30f), new Color(0.60f, 0.60f, 0.60f)
        };

        /// <summary>Last value the layout was actually built with, so we know when to redo it.</summary>
        private bool appliedCombineIndustrial;

        public ResearchOrganizedMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<ResearchOrganizedSettings>();
            appliedCombineIndustrial = settings.combineIndustrial;
        }

        public override string SettingsCategory() => "Research: Organized";

        /// <summary>
        /// Reapplies the layout when a setting that changes structure has been touched.
        ///
        /// Previously the organiser only ever ran from the static constructor, so
        /// "Combine Industrial" appeared in this window, saved correctly, and then did
        /// nothing at all until the game was restarted.
        /// </summary>
        public override void WriteSettings()
        {
            bool structureChanged = settings.combineIndustrial != appliedCombineIndustrial;

            base.WriteSettings();
            ResearchOrganizedMain.RefreshColors();

            if (structureChanged)
            {
                appliedCombineIndustrial = settings.combineIndustrial;
                ResearchOrganizedMain.OrganizeTabsAndLayout();
            }
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.CheckboxLabeled("Combine Industrial", ref settings.combineIndustrial,
                "Merges High and Late Industrial technologies back into the main Industrial tab.");

            listing.Gap();

            listing.GapLine();

            listing.CheckboxLabeled("Disable Custom Colors", ref settings.disableCustomColors,
                "Reverts research nodes to their default vanilla colors.");

            listing.Gap();

            if (!settings.disableCustomColors)
            {
                Text.Font = GameFont.Medium;
                listing.Label("Tab Theme Colors (Finished State)");
                Text.Font = GameFont.Small;
                listing.Gap();

                DrawColorPalette(listing, "Undefined / Misc", ref settings.colorUndefined);
                DrawColorPalette(listing, "Animal / Primitive", ref settings.colorAnimal);
                DrawColorPalette(listing, "Neolithic", ref settings.colorNeolithic);
                DrawColorPalette(listing, "Medieval", ref settings.colorMedieval);
                DrawColorPalette(listing, "Industrial", ref settings.colorIndustrial);
                DrawColorPalette(listing, "Spacer", ref settings.colorSpacer);
                DrawColorPalette(listing, "Ultra", ref settings.colorUltra);
                DrawColorPalette(listing, "Archotech", ref settings.colorArchotech);
                DrawColorPalette(listing, "Anomaly", ref settings.colorAnomaly);
            }

            listing.GapLine();

            if (listing.ButtonText("Reset to Defaults"))
            {
                settings.CopyFrom(new ResearchOrganizedSettings());
            }

            listing.End();
            base.DoSettingsWindowContents(inRect);

            ResearchOrganizedMain.RefreshColors();
        }

        private void DrawColorPalette(Listing_Standard listing, string label, ref Color selectedColor)
        {
            Rect rect = listing.GetRect(28f);
            Widgets.Label(new Rect(rect.x, rect.y + 4f, 150f, rect.height), label);

            float curX = rect.x + 160f;
            foreach (Color color in Palette)
            {
                Rect swatchRect = new Rect(curX, rect.y + 2f, 24f, 24f);
                Widgets.DrawBoxSolid(swatchRect, color);

                if (ColorsMatch(color, selectedColor)) Widgets.DrawBox(swatchRect, 2);

                if (Widgets.ButtonInvisible(swatchRect)) selectedColor = color;

                curX += 28f;
            }
        }

        private bool ColorsMatch(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.01f &&
                   Mathf.Abs(a.g - b.g) < 0.01f &&
                   Mathf.Abs(a.b - b.b) < 0.01f;
        }
    }
}