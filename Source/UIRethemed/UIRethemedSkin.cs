using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ResearchOrganized;
using UIRethemed.Core;
using UnityEngine;
using Verse;

namespace ResearchOrganizedUIRethemed
{
    /// <summary>Research: Organized drawn with UI Rethemed - Core. Every Core call sits in its own non-inlined method inside
    /// try/catch, so a Core API change fails at JIT time into ResearchSkin.Fail and the mod's own look.</summary>
    public sealed class UIRethemedSkin : ResearchSkin
    {
        private const string PackageId = "cruesoe.research.organized";
        private const string SettingsPart = "ResearchOrganized.Settings";

        private static bool IsRepaint => Event.current.type == EventType.Repaint;

        private static bool Caught(Exception e)
        {
            Fail(e);
            return false;
        }

        public override Color? CategoryColor(string key)
        {
            if (!Available) return null;
            try { return CategoryColorCore(key); }
            catch (Exception e) { Caught(e); return null; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Color CategoryColorCore(string key) => Theme.ModColor(PackageId, key);

        public override bool Button(Rect rect, string label, SkinTone tone, bool enabled)
        {
            if (!Available) return false;
            if (!IsRepaint) return true;
            try
            {
                ButtonCore(rect, label, tone, enabled);
                return true;
            }
            catch (Exception e) { return Caught(e); }
        }

        // Drawn on repaint only, where Core's ButtonInvisible never reports a click.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ButtonCore(Rect rect, string label, SkinTone tone, bool enabled) =>
            CoreWidgets.Button(rect, label, null, ToneColor(tone), enabled, null, GameFont.Small, null, false, enabled && tone != SkinTone.Gray);

        private static Color ToneColor(SkinTone tone)
        {
            switch (tone)
            {
                case SkinTone.Green: return Theme.HighlightGreen;
                case SkinTone.Red: return Theme.HighlightRed;
                default: return Theme.HighlightGray;
            }
        }

        private static readonly HashSet<string> openSections = new HashSet<string>();
        private static readonly HashSet<string> seenSections = new HashSet<string>();
        private object settingsCards;
        private Action drawSettings;
        private Rect drawingRect;
        private Vector2 leftScroll;
        private Vector2 rightScroll;
        private float leftHeight = 600f;
        private float rightHeight = 600f;
        private List<SkinSettingsSection> drawingLeft;
        private List<SkinSettingsSection> drawingRight;
        private Func<float, float, float> leftCards;
        private Func<float, float, float> rightCards;

        public override bool SettingsPage(Rect inRect, List<SkinSettingsSection> left, List<SkinSettingsSection> right)
        {
            if (!Available) return false;
            try { return SettingsPageCore(inRect, left, right); }
            catch (Exception e) { return Caught(e); }
        }

        // Drawn inside Core's Failsafe, which turns this part off (and the page back to the plain one) after an error.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool SettingsPageCore(Rect inRect, List<SkinSettingsSection> left, List<SkinSettingsSection> right)
        {
            if (Failsafe.Failed(SettingsPart)) return false;
            if (settingsCards == null)
            {
                settingsCards = new SettingsCards("ResearchOrganized", () => openSections);
                leftCards = (y, width) => DrawCards(drawingLeft, y, width);
                rightCards = (y, width) => DrawCards(drawingRight, y, width);
                drawSettings = DrawSettings;
            }
            // Every section starts open; a player's collapse lasts for the session.
            OpenNew(left);
            OpenNew(right);
            drawingLeft = left;
            drawingRight = right;
            drawingRect = inRect;
            return Failsafe.Run(SettingsPart, drawSettings);
        }

        private void DrawSettings()
        {
            var cards = (SettingsCards)settingsCards;
            Theme.BeginMod(ResearchOrganizedMod.Instance);
            try
            {
                float columnWidth = Mathf.Floor((drawingRect.width - SettingsCards.ColumnGap) / 2f);
                Rect leftRect = new Rect(drawingRect.x, drawingRect.y, columnWidth, drawingRect.height);
                Rect rightRect = new Rect(drawingRect.xMax - columnWidth, drawingRect.y, columnWidth, drawingRect.height);
                leftHeight = cards.Column(leftRect, ref leftScroll, leftHeight, leftCards);
                rightHeight = cards.Column(rightRect, ref rightScroll, rightHeight, rightCards);
            }
            finally
            {
                Theme.CloseScrolls();
                Theme.Reset();
                Theme.EndMod();
            }
            cards.Finish();
        }

        private static void OpenNew(List<SkinSettingsSection> sections)
        {
            for (int i = 0; i < sections.Count; i++)
            {
                if (seenSections.Add(sections[i].key))
                    openSections.Add(sections[i].key);
            }
        }

        private float DrawCards(List<SkinSettingsSection> sections, float y, float width)
        {
            var cards = (SettingsCards)settingsCards;
            for (int i = 0; i < sections.Count; i++)
            {
                SkinSettingsSection section = sections[i];
                y = cards.Card(y, width, section.key, SectionIcon(section.key), section.title, null, section.body);
            }
            return y;
        }

        private static readonly Dictionary<string, Texture> sectionIcons = new Dictionary<string, Texture>();

        private static Texture SectionIcon(string key)
        {
            if (sectionIcons.TryGetValue(key, out Texture icon))
                return icon;
            string name;
            switch (key)
            {
                case "ResearchWindow": name = "frame-corners"; break;
                case "Tabs": name = "squares-four"; break;
                case "Cards": name = "cards"; break;
                case "Colors": name = "palette"; break;
                case "Layout": name = "tree-structure"; break;
                default: name = "sliders"; break;
            }
            icon = CoreWidgets.Icon(name);
            sectionIcons[key] = icon;
            return icon;
        }

        public override bool Tick(Listing_Standard listing, string label, ref bool value, string tip, bool enabled)
        {
            if (!Available) return false;
            try
            {
                TickCore(listing, label, ref value, tip, enabled);
                return true;
            }
            catch (Exception e) { return Caught(e); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void TickCore(Listing_Standard listing, string label, ref bool value, string tip, bool enabled) =>
            SettingsCards.Tick(listing, null, Theme.TextDim, label, ref value, tip, enabled);
    
        public override bool Slider(Listing_Standard listing, string label, string tip, ref float value, float min, float max, float step,
            float usual, Func<float, string> text, string resetTip)
        {
            if (!Available) return false;
            try
            {
                value = SliderCore(listing, label, tip, value, min, max, step, usual, text, resetTip);
                return true;
            }
            catch (Exception e) { return Caught(e); }
        }

        /// <summary>Core's slider row, with a reset button once the value is moved off <paramref name="usual"/>.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static float SliderCore(Listing_Standard listing, string label, string tip, float value, float min, float max, float step,
            float usual, Func<float, string> text, string resetTip)
        {
            value = SettingsCards.SliderLine(listing, label, tip, value, min, max, step, text, out Rect reset);
            if (Mathf.Abs(value - usual) > step * 0.01f && CoreWidgets.IconButton(reset, CoreWidgets.ResetTex, resetTip, null, false, null, true, 14f))
                value = usual;
            return value;
        }
    }
}
