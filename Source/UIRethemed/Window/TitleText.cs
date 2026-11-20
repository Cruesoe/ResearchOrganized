using System.Collections.Generic;
using UnityEngine;
using Verse;
using ResearchOrganized;

namespace ResearchOrganizedUIRethemed
{
    /// <summary>Text larger than GameFont.Medium, for titles: the game's Medium style at a set pixel size, made once per size.</summary>
    internal static class TitleText
    {
        private static readonly Dictionary<int, GUIStyle> styles = new Dictionary<int, GUIStyle>();
        private static readonly GUIContent content = new GUIContent();

        private static GUIStyle Style(int size)
        {
            if (styles.TryGetValue(size, out var style)) return style;
            style = new GUIStyle(Text.fontStyles[(int)GameFont.Medium])
            {
                fontSize = size,
                fontStyle = FontStyle.Bold,
                wordWrap = false,
                clipping = TextClipping.Clip
            };
            styles[size] = style;
            return style;
        }

        public static void Draw(Rect rect, string text, Color color, int size, TextAnchor anchor = TextAnchor.MiddleLeft, bool wrap = false)
        {
            if (Event.current.type != EventType.Repaint || text.NullOrEmpty()) return;
            var style = Style(size);
            style.alignment = anchor;
            style.wordWrap = wrap;
            style.normal.textColor = color;
            content.text = text;
            Color old = GUI.color;
            GUI.color = Color.white;
            GUI.Label(rect, content, style);
            GUI.color = old;
        }

        public static float Width(string text, int size)
        {
            var style = Style(size);
            style.wordWrap = false;
            content.text = text;
            return style.CalcSize(content).x;
        }

        public static float Height(string text, float width, int size)
        {
            var style = Style(size);
            style.wordWrap = true;
            content.text = text;
            return style.CalcHeight(content, width);
        }
    }
}
