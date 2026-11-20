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

        /// <summary>The add-on, while it is available.</summary>
        public static ResearchSkin Active => Available ? registered : null;

        /// <summary>A themed draw threw (usually a Core update that changed its API): the mod's own look is used for the rest of the session.</summary>
        public static void Fail(Exception e)
        {
            if (failed) return;
            failed = true;
            Log.Warning("[Research: Organized] UI Rethemed look turned off for this session after an error: " + e);
        }

        /// <summary>A card color by key (a tech level's name, "Anomaly"), from Core's per-mod palette; null leaves the mod's own.</summary>
        public abstract Color? CategoryColor(string key);

        /// <summary>Button face and label only, in a tone; clicks stay with the caller.</summary>
        public abstract bool Button(Rect rect, string label, SkinTone tone, bool enabled);

        /// <summary>Settings page: sections as cards in two columns.</summary>
        public abstract bool SettingsPage(Rect inRect, List<SkinSettingsSection> left, List<SkinSettingsSection> right);

        /// <summary>Checkbox row inside a settings card; value is flipped on click.</summary>
        public abstract bool Tick(Listing_Standard listing, string label, ref bool value, string tip, bool enabled);

        /// <summary>Slider row inside a settings card, with a reset button once the value is moved off <paramref name="usual"/>.</summary>
        public abstract bool Slider(Listing_Standard listing, string label, string tip, ref float value, float min, float max, float step,
            float usual, Func<float, string> text, string resetTip);
    }
}
