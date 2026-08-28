// TEMPORARY diagnostic — add this as a static method and call it once from your
// existing static constructor (before the DrawConnections patch attempt), then
// remove it once you've found the right method name.
//
// It will dump every method declared on MainTabWindow_Research (and its base type,
// MainTabWindow) to the log so you can see what changed in 1.6.4850.

using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace ResearchOrganized
{
    public static class MethodDiscoveryDiagnostic
    {
        public static void DumpResearchWindowMethods()
        {
            Log.Warning("[Research: Organized] ---- MainTabWindow_Research methods ----");
            foreach (var m in typeof(MainTabWindow_Research)
                         .GetMethods(BindingFlags.Instance | BindingFlags.Static |
                                     BindingFlags.Public | BindingFlags.NonPublic |
                                     BindingFlags.DeclaredOnly)
                         .OrderBy(m => m.Name))
            {
                var paramList = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name));
                Log.Warning($"  {m.ReturnType.Name} {m.Name}({paramList})");
            }

            // Also check the base class in case the method moved up the hierarchy
            Log.Warning("[Research: Organized] ---- MainTabWindow_Research base type ----");
            Log.Warning($"  Base type: {typeof(MainTabWindow_Research).BaseType?.FullName}");

            // Look for anything containing "Draw" or "Connection" or "Line" anywhere in the
            // type, including inherited members, in case it moved to a helper class entirely.
            Log.Warning("[Research: Organized] ---- Candidate methods (contains Draw/Connection/Line) ----");
            foreach (var m in typeof(MainTabWindow_Research)
                         .GetMethods(BindingFlags.Instance | BindingFlags.Static |
                                     BindingFlags.Public | BindingFlags.NonPublic)
                         .Where(m => m.Name.Contains("Draw") || m.Name.Contains("Connection") || m.Name.Contains("Line"))
                         .OrderBy(m => m.Name))
            {
                Log.Warning($"  {m.DeclaringType?.Name}.{m.Name}");
            }
        }
    }
}