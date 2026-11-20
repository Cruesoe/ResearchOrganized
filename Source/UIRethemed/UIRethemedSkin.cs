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
            CoreWidgets.HeaderButtonFace(rect, texture, Theme.HighlightGray, 14f);
        }

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

        public override bool WindowBackground(Rect windowRect)
        {
            if (!Available) return false;
            try
            {
                WindowBackgroundCore(windowRect);
                return true;
            }
            catch (Exception e) { return Caught(e); }
        }

        // The window spans the screen's width above the bottom bar, so no corner is rounded.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WindowBackgroundCore(Rect windowRect)
        {
            Theme.BeginMod(ResearchOrganizedMod.Instance);
            try { Rounded.WindowBackground(windowRect, Corners.None); }
            finally { Theme.EndMod(); }
        }

        public override bool Panel(Rect rect)
        {
            if (!Available) return false;
            if (!IsRepaint) return true;
            try
            {
                PanelCore(rect);
                return true;
            }
            catch (Exception e) { return Caught(e); }
        }

        // Matches the page under Core's own tab strips.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void PanelCore(Rect rect)
        {
            Rounded.BeginBatch();
            try
            {
                Rounded.Fill(rect, Theme.BG);
                Rounded.Border(rect, PageEdge);
            }
            finally
            {
                Rounded.EndBatch();
            }
        }

        private static Color PageEdge => Color.Lerp(Theme.BGDark, Theme.HighlightGray, 0.35f);

        public override bool Well(Rect rect)
        {
            if (!Available) return false;
            if (!IsRepaint) return true;
            try
            {
                WellCore(rect);
                return true;
            }
            catch (Exception e) { return Caught(e); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WellCore(Rect rect)
        {
            Rounded.BeginBatch();
            try
            {
                Rounded.Fill(rect, Theme.BGDark);
                Rounded.Border(rect, Theme.BGLight);
            }
            finally
            {
                Rounded.EndBatch();
            }
        }

        public override bool Tab(Rect rect, string label, bool selected, Color? labelColor)
        {
            if (!Available) return false;
            try
            {
                TabCore(rect, label, selected, labelColor);
                return true;
            }
            catch (Exception e) { return Caught(e); }
        }

        // Vanilla lays tabs out 10 pixels apart for its slanted ends; the themed faces sit inside that, a few pixels apart.
        // The open tab reaches down into the page below it, as on Core's own tab strips.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void TabCore(Rect rect, string label, bool selected, Color? labelColor)
        {
            Rect face = new Rect(rect.x + 5f, rect.y + (selected ? 0f : 4f), rect.width - 12f, rect.height - (selected ? 0f : 4f));
            float hover = selected ? 0f : ThemeMotion.Hover(face, 31417, Mouse.IsOver(face));
            if (IsRepaint)
            {
                Rounded.BeginBatch();
                try
                {
                    if (selected)
                    {
                        Rounded.Fill(face, Theme.BG, Corners.Top);
                        Rounded.Border(face, PageEdge, Corners.Top);
                        Rounded.Fill(new Rect(face.x + 1f, face.yMax - 2f, face.width - 2f, 4f), Theme.BG, Corners.None);
                    }
                    else
                    {
                        Color fill = Color.Lerp(Color.Lerp(Theme.BGDark, Theme.BG, 0.45f), Color.Lerp(Theme.BG, Theme.BGLight, 0.25f), hover * 0.8f);
                        Rounded.Fill(face, fill, Corners.Top);
                        Rounded.Border(face, PageEdge, Corners.Top);
                    }
                }
                finally
                {
                    Rounded.EndBatch();
                }
                if (selected)
                    Theme.Fill(new Rect(face.x + 5f, face.y + 1f, face.width - 10f, 2f), Theme.HighlightGreen);
                else if (hover > 0f)
                    Theme.Fill(new Rect(face.x + 5f, face.y + 1f, face.width - 10f, 2f), Faces.Fade(Theme.HighlightGreen, 0.45f * hover));
            }
            Color color = labelColor ?? (selected ? Color.white : Color.Lerp(Theme.TextDim, Theme.TextBright, hover));
            CoreWidgets.Line(face.ContractedBy(6f, 0f), label.Truncate(face.width - 12f), color, GameFont.Small, TextAnchor.MiddleCenter);
        }

        public override bool Bar(Rect rect, float fill)
        {
            if (!Available) return false;
            if (!IsRepaint) return true;
            try
            {
                BarCore(rect, fill);
                return true;
            }
            catch (Exception e) { return Caught(e); }
        }

        // The title bar's face as the track, with the drifting pattern, and a rounded fill.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void BarCore(Rect rect, float fill)
        {
            Rounded.Fill(rect, Theme.TitleBarFill);
            Faces.FadingHex(rect, Theme.TitleAccent, Theme.TitleBarFill, 1f, 1f);
            Rounded.Border(rect, Theme.BGLight);
            Rect inner = rect.ContractedBy(3f);
            inner.width = Mathf.Round(inner.width * Mathf.Clamp01(fill));
            if (inner.width < 1f)
                return;
            Rounded.Fill(inner, Faces.Fade(Theme.HighlightBlue, 0.85f));
            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            GUI.DrawTexture(new Rect(inner.x, inner.y, inner.width, inner.height / 2f), Theme.VGradientTex);
            GUI.color = Color.white;
        }

        public override bool Divider(float x, float y, float width)
        {
            if (!Available) return false;
            if (!IsRepaint) return true;
            try
            {
                DividerCore(x, y, width);
                return true;
            }
            catch (Exception e) { return Caught(e); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DividerCore(float x, float y, float width) => Theme.Divider(x, y, width);

        public override bool Glow(Rect rect)
        {
            if (!Available) return false;
            if (!IsRepaint) return true;
            try
            {
                GlowCore(rect);
                return true;
            }
            catch (Exception e) { return Caught(e); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void GlowCore(Rect rect)
        {
            Rounded.Border(rect, Theme.HighlightYellow);
            CoreWidgets.EdgeGlow(rect, Theme.HighlightYellow);
        }

        // The vanilla colors these replace, read once.
        private static readonly Color VanillaLine = TexUI.DefaultLineResearchColor;
        private static readonly Color VanillaHighlightLine = TexUI.HighlightLineResearchColor;
        private static readonly Color VanillaBorder = TexUI.DefaultBorderResearchColor;
        private static readonly Color VanillaResearchingBorder = TexUI.BorderResearchingColor;
        private static readonly Color VanillaDependencyBorder = TexUI.DependencyOutlineResearchColor;

        public override Color ResearchColor(Color vanilla)
        {
            if (!Available) return vanilla;
            try { return ResearchColorCore(vanilla); }
            catch (Exception e) { Caught(e); return vanilla; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Color ResearchColorCore(Color vanilla)
        {
            if (vanilla == VanillaLine) return Color.Lerp(Theme.BGLight, Theme.TextFaint, 0.35f);
            if (vanilla == VanillaHighlightLine) return Theme.HighlightBlue;
            if (vanilla == VanillaBorder) return Theme.BGLight;
            if (vanilla == VanillaResearchingBorder) return Theme.HighlightYellow;
            if (vanilla == VanillaDependencyBorder) return Theme.HighlightRed;
            return vanilla;
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
