using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ResearchOrganized
{
    /// <summary>A look supplied by an optional assembly (the UI Rethemed add-on, loaded only with UI Rethemed - Core).
    /// Draw methods return false when they drew nothing, and the caller then draws the mod's own look.</summary>
    public abstract class ResearchSkin
    {
        private static ResearchSkin registered;
        private static bool failed;

        public static void Register(ResearchSkin skin) => registered = skin;

        /// <summary>The add-on is loaded and has not failed this session.</summary>
        public static bool Available => registered != null && !failed;

        /// <summary>The add-on while the Match UI Rethemed setting is on.</summary>
        public static ResearchSkin Active =>
            Available && (ResearchOrganizedMod.settings == null || ResearchOrganizedMod.settings.matchUIRethemed) ? registered : null;

        /// <summary>A themed draw threw (usually a Core update that changed its API): the mod's own look is used for the rest of the session.</summary>
        public static void Fail(Exception e)
        {
            if (failed) return;
            failed = true;
            Log.Warning("[Research: Organized] UI Rethemed look turned off for this session after an error: " + e);
        }

        /// <summary>A card color by key (a tech level's name, "Anomaly"), from Core's per-mod palette; null leaves the mod's own.</summary>
        public abstract Color? CategoryColor(string key);

        /// <summary>A project card's face: fill, a progress share in progressColor and its border. Hover is animated by the skin.</summary>
        public abstract bool Card(Rect rect, Color fill, Color progressColor, float progress, Color edge, bool thickEdge);

        /// <summary>Icon button face only, by Core icon name; clicks and tooltips stay with the caller.</summary>
        public abstract bool IconButton(Rect rect, string icon);

        /// <summary>Button face and label only, in a tone; clicks stay with the caller.</summary>
        public abstract bool Button(Rect rect, string label, SkinTone tone, bool enabled);

        /// <summary>The research window's background, over the whole window.</summary>
        public abstract bool WindowBackground(Rect windowRect);

        /// <summary>A raised panel in place of vanilla's menu section (the tree's page, the active project box).</summary>
        public abstract bool Panel(Rect rect);

        /// <summary>A sunken panel in place of vanilla's highlight box (the locked start button).</summary>
        public abstract bool Well(Rect rect);

        /// <summary>A tab's face and label in the research window's tab strip; clicks, sounds and tooltips stay with vanilla.</summary>
        public abstract bool Tab(Rect rect, string label, bool selected, Color? labelColor);

        /// <summary>A progress bar's track and filled share; vanilla draws its label on top.</summary>
        public abstract bool Bar(Rect rect, float fill);

        public abstract bool Divider(float x, float y, float width);

        /// <summary>The search-match highlight around a project card.</summary>
        public abstract bool Glow(Rect rect);

        /// <summary>A vanilla research line or border color mapped to the theme's equivalent; unknown colors come back unchanged.</summary>
        public abstract Color ResearchColor(Color vanilla);

        /// <summary>Settings page: sections as cards in two columns.</summary>
        public abstract bool SettingsPage(Rect inRect, List<SkinSettingsSection> left, List<SkinSettingsSection> right);

        /// <summary>Checkbox row inside a settings card; value is flipped on click.</summary>
        public abstract bool Tick(Listing_Standard listing, string label, ref bool value, string tip, bool enabled);
    }
}
