using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ResearchOrganized
{
    /// <summary>Research Icons widens research cards (172 px by default, settable per project) to fit
    /// its icon. Reflective and entirely optional - with Research Icons absent, or its internals
    /// renamed, every card reads as vanilla's width.</summary>
    internal static class ResearchIconsCompat
    {
        private const string ResolverTypeName = "Research_Icons.ResearchProjectIconResolver";

        /// <summary>Vanilla card width, before the Anomaly knowledge strip.</summary>
        public const float VanillaCardWidth = 140f;

        private static bool resolved;
        private static Func<ResearchProjectDef, float> getProjectWidth;

        /// <summary>The width a project's card is drawn at, before the Anomaly knowledge strip.</summary>
        public static float CardWidth(ResearchProjectDef project)
        {
            Resolve();
            if (getProjectWidth == null) return VanillaCardWidth;
            try { return getProjectWidth(project); }
            catch (Exception ex)
            {
                Log.Error($"[Research: Organized] Research Icons card width error: {ex.Message}");
                getProjectWidth = null;
                return VanillaCardWidth;
            }
        }

        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;
            var resolverType = AccessTools.TypeByName(ResolverTypeName);
            if (resolverType == null) return;
            var method = AccessTools.Method(resolverType, "GetProjectWidth", new[] { typeof(ResearchProjectDef) });
            if (method == null || method.ReturnType != typeof(float) || !method.IsStatic) return;
            getProjectWidth = (Func<ResearchProjectDef, float>)Delegate.CreateDelegate(typeof(Func<ResearchProjectDef, float>), method);
        }
    }
}
