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

        public override bool Card(Rect rect, Color fill, Color progressColor, float progress, Color edge, bool thickEdge)
        {
            if (!Available) return false;
            if (!IsRepaint) return true;
            try
            {
                CardCore(rect, fill, progressColor, progress, edge, thickEdge);
                return true;
            }
            catch (Exception e) { return Caught(e); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CardCore(Rect rect, Color fill, Color progressColor, float progress, Color edge, bool thickEdge)
        {
            float hover = ThemeMotion.Hover(rect, 52931, Mouse.IsOver(rect));
            Rounded.BeginBatch();
            try
            {
                Rounded.Fill(rect, fill);
                if (progress > 0f)
                {
                    float width = Mathf.Round(rect.width * Mathf.Clamp01(progress));
                    if (width >= 1f)
                    {
                        Rounded.Fill(new Rect(rect.x, rect.y, width, rect.height), progressColor,
                            width >= rect.width - Rounded.Radius ? Corners.All : Corners.TopLeft | Corners.BottomLeft);
                    }
                }
                if (hover > 0f)
                    Rounded.Fill(rect.ContractedBy(2f), new Color(1f, 1f, 1f, 0.06f * hover));
                Rounded.Border(rect, edge);
                if (thickEdge)
                    Rounded.Border(rect.ContractedBy(1f), Faces.Fade(edge, 0.8f));
            }
            finally
            {
                Rounded.EndBatch();
            }
            if (hover > 0f)
                CoreWidgets.EdgeGlow(rect, progressColor);
        }

        private static readonly Dictionary<string, Texture> icons = new Dictionary<string, Texture>();

        public override bool IconButton(Rect rect, string icon)
        {
            if (!Available) return false;
            if (!IsRepaint) return true;
            try
            {
                IconButtonCore(rect, icon);
                return true;
            }
            catch (Exception e) { return Caught(e); }
        }

        // Drawn on repaint only, where Core's ButtonInvisible never reports a click.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void IconButtonCore(Rect rect, string icon)
        {
            if (!icons.TryGetValue(icon, out Texture texture))
                icons[icon] = texture = CoreWidgets.Icon(icon);
            CoreWidgets.IconButton(rect, texture, null, null, false, null, true, Mathf.Round(rect.width * 0.75f));
        }

        public override bool Button(Rect rect, string label, bool enabled)
        {
            if (!Available) return false;
            if (!IsRepaint) return true;
            try
            {
                ButtonCore(rect, label, enabled);
                return true;
            }
            catch (Exception e) { return Caught(e); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ButtonCore(Rect rect, string label, bool enabled) =>
            CoreWidgets.Button(rect, label, null, Theme.HighlightGray, enabled);

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
    }
}
