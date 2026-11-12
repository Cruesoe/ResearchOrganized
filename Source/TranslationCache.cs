using System.Collections.Generic;
using Verse;

namespace ResearchOrganized
{
    /// <summary>Translated keyed text for draw code, translated once per key and cleared when the language changes.</summary>
    public static class TranslationCache
    {
        private static readonly Dictionary<string, string> text = new Dictionary<string, string>();
        private static LoadedLanguage language;

        public static string Get(string key)
        {
            if (language != LanguageDatabase.activeLanguage)
            {
                text.Clear();
                language = LanguageDatabase.activeLanguage;
            }
            if (!text.TryGetValue(key, out string value)) text[key] = value = key.Translate();
            return value;
        }
    }
}
