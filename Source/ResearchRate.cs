using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ResearchOrganized
{
    /// <summary>The colony's recent research speed, from the points colonists add to the current project, kept in hourly
    /// buckets over the last day. Points are stored before the tech-level cost factor, so any project's time can be worked
    /// out from them. With Semi Random Research - Continued active, its measured speed is used instead, so both mods show
    /// the same times. Lifetime: one game; saved with it by ProjectQueueComponent and cleared when another game is loaded.</summary>
    internal static class ResearchRate
    {
        private const int BucketTicks = GenDate.TicksPerHour;
        private const int Buckets = 24;

        private static readonly float[] points = new float[Buckets];
        private static readonly int[] bucketHour = new int[Buckets];
        private static int firstTick = -1;
        private static Game game;

        private struct Sample
        {
            public ResearchProjectDef project;
            public float before;
        }

        public static void Patch(Harmony harmony)
        {
            var method = AccessTools.Method(typeof(ResearchManager), nameof(ResearchManager.ResearchPerformed));
            if (method == null) return;
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(ResearchRate), nameof(Before)),
                postfix: new HarmonyMethod(typeof(ResearchRate), nameof(After)));
        }

        private static void Before(ResearchManager __instance, out Sample __state)
        {
            var project = __instance.GetProject();
            __state = new Sample { project = project, before = project != null ? __instance.GetProgress(project) : 0f };
        }

        private static void After(ResearchManager __instance, Sample __state)
        {
            if (__state.project == null) return;
            float added = __instance.GetProgress(__state.project) - __state.before;
            if (added > 0f) Record(added * CostFactor(__state.project));
        }

        /// <summary>The project's cost factor for the colony's tech level, which vanilla divides each research step by.</summary>
        private static float CostFactor(ResearchProjectDef project)
        {
            var faction = Faction.OfPlayerSilentFail;
            return faction != null ? project.CostFactor(faction.def.techLevel) : 1f;
        }

        private static void EnsureGame()
        {
            if (Current.Game == game) return;
            game = Current.Game;
            Clear();
        }

        private static void Clear()
        {
            firstTick = -1;
            for (int i = 0; i < Buckets; i++)
            {
                points[i] = 0f;
                bucketHour[i] = -1;
            }
        }

        private static void Record(float added)
        {
            EnsureGame();
            int tick = Find.TickManager.TicksGame;
            if (firstTick < 0) firstTick = tick;
            int hour = tick / BucketTicks;
            int index = hour % Buckets;
            if (bucketHour[index] != hour)
            {
                bucketHour[index] = hour;
                points[index] = 0f;
            }
            points[index] += added;
        }

        /// <summary>Saves and loads the last day's buckets with the game, so estimates carry on after a load.</summary>
        public static void ExposeData()
        {
            List<float> savedPoints = null;
            List<int> savedHours = null;
            int savedFirst = firstTick;
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                EnsureGame();
                savedPoints = new List<float>(points);
                savedHours = new List<int>(bucketHour);
                savedFirst = firstTick;
            }
            Scribe_Collections.Look(ref savedPoints, "ratePoints", LookMode.Value);
            Scribe_Collections.Look(ref savedHours, "rateHours", LookMode.Value);
            Scribe_Values.Look(ref savedFirst, "rateFirstTick", -1);
            if (Scribe.mode != LoadSaveMode.LoadingVars) return;
            game = Current.Game;
            Clear();
            if (savedPoints == null || savedHours == null || savedPoints.Count != Buckets || savedHours.Count != Buckets) return;
            for (int i = 0; i < Buckets; i++)
            {
                points[i] = savedPoints[i];
                bucketHour[i] = savedHours[i];
            }
            firstTick = savedFirst;
        }

        /// <summary>The current game hour, so callers can refresh cached estimates once an hour.</summary>
        public static int Hour => Find.TickManager != null ? Find.TickManager.TicksGame / BucketTicks : 0;

        /// <summary>Research points per tick, before cost factors, over the last day; 0 until there is at least a quarter hour of data.</summary>
        private static float PointsPerTick()
        {
            EnsureGame();
            if (firstTick < 0 || Find.TickManager == null) return 0f;
            int tick = Find.TickManager.TicksGame;
            int span = Math.Min(tick - firstTick, Buckets * BucketTicks);
            if (span < BucketTicks / 4) return 0f;
            int hour = tick / BucketTicks;
            float sum = 0f;
            for (int i = 0; i < Buckets; i++)
                if (bucketHour[i] > hour - Buckets) sum += points[i];
            return sum / span;
        }

        // Semi Random Research - Continued's rate tracker gives shown points per day for a research category, averaged over up
        // to ten days of hourly samples. Its methods are resolved once; the tracker is looked up again for each world.
        private const string SemiRandomPackageId = "cruesoe.semirandompfork.continued";
        private const float SemiRandomMaxDays = 100000f;
        private static bool semiRandomResolved;
        private static Type semiRandomTrackerType;
        private static MethodInfo semiRandomRateMethod;
        private static Func<ResearchProjectDef, string> semiRandomCategoryKey;
        private static World semiRandomWorld;
        private static Func<string, float> semiRandomRate;
        private static readonly Dictionary<string, float> semiRandomRates = new Dictionary<string, float>();
        private static int semiRandomRatesHour = -1;

        private static void ResolveSemiRandom()
        {
            semiRandomResolved = true;
            if (!ModsConfig.IsActive(SemiRandomPackageId)) return;
            semiRandomTrackerType = AccessTools.TypeByName("CM_Semi_Random_Research.ResearchRateTracker");
            var rateMethod = semiRandomTrackerType != null
                ? AccessTools.Method(semiRandomTrackerType, "GetCategoryAverageRate", new[] { typeof(string) })
                : null;
            if (rateMethod != null && !rateMethod.IsStatic && rateMethod.ReturnType == typeof(float)) semiRandomRateMethod = rateMethod;
            var keyMethod = AccessTools.Method("CM_Semi_Random_Research.ResearchTracker:GetCategoryKey", new[] { typeof(ResearchProjectDef) });
            if (keyMethod != null && keyMethod.IsStatic && keyMethod.ReturnType == typeof(string))
                semiRandomCategoryKey = (Func<ResearchProjectDef, string>)Delegate.CreateDelegate(typeof(Func<ResearchProjectDef, string>), keyMethod);
        }

        /// <summary>Semi Random Research's rate for a category, in shown points per day, read once an hour; null without it.</summary>
        private static float? SemiRandomDailyRate(string category)
        {
            if (!semiRandomResolved) ResolveSemiRandom();
            if (semiRandomRateMethod == null || Find.World == null) return null;
            if (semiRandomWorld != Find.World)
            {
                semiRandomWorld = Find.World;
                semiRandomRate = null;
                semiRandomRates.Clear();
                foreach (WorldComponent component in Find.World.components)
                {
                    if (component == null || component.GetType() != semiRandomTrackerType) continue;
                    semiRandomRate = (Func<string, float>)Delegate.CreateDelegate(typeof(Func<string, float>), component, semiRandomRateMethod);
                    break;
                }
            }
            if (semiRandomRate == null) return null;
            int hour = Hour;
            if (hour != semiRandomRatesHour)
            {
                semiRandomRatesHour = hour;
                semiRandomRates.Clear();
            }
            if (!semiRandomRates.TryGetValue(category, out float rate))
            {
                try { rate = semiRandomRate(category); }
                catch (Exception) { rate = 0f; }
                semiRandomRates[category] = rate;
            }
            return rate;
        }

        private static string CategoryKey(ResearchProjectDef project) => semiRandomCategoryKey?.Invoke(project) ?? "Standard";

        /// <summary>Semi Random Research is active and its rate tracker was found, so its speed sets every estimate.</summary>
        private static bool UsingSemiRandom => SemiRandomDailyRate("Standard").HasValue;

        public static bool HasEstimate => UsingSemiRandom ? SemiRandomDailyRate("Standard") > 0f : PointsPerTick() > 0f;

        /// <summary>Ticks left on a standard research project at the recent pace; -1 when there is no estimate.</summary>
        public static int TicksLeft(ResearchProjectDef project)
        {
            if (project == null || project.knowledgeCategory != null || project.IsFinished || project.baseCost <= 0f) return -1;
            if (UsingSemiRandom)
            {
                // As Semi Random Research works it out: the shown points left over its category's points per day.
                float rate = SemiRandomDailyRate(CategoryKey(project)) ?? 0f;
                float remaining = project.CostApparent - project.ProgressApparent;
                if (rate <= 0f || remaining <= 0f || remaining / rate > SemiRandomMaxDays) return -1;
                return (int)Math.Min(remaining / rate * GenDate.TicksPerDay, int.MaxValue);
            }
            float left = project.Cost - project.ProgressReal;
            float perTick = PointsPerTick();
            if (left <= 0f || perTick <= 0f) return -1;
            return (int)Math.Min(left * CostFactor(project) / perTick, int.MaxValue);
        }

        /// <summary>A time as Semi Random Research shows it: hours to one decimal under a day, else whole days and years.</summary>
        public static string FormatPeriod(int ticks)
        {
            float days = ticks / (float)GenDate.TicksPerDay;
            if (days < 1f) return "ResearchOrganized_PeriodHours".Translate((days * 24f).ToString("F1"));
            int totalDays = Math.Max(1, (int)Math.Round(days));
            int years = totalDays / GenDate.DaysPerYear;
            int rest = totalDays % GenDate.DaysPerYear;
            string dayLabel = rest == 1 ? "ResearchOrganized_PeriodDay".Translate(1).ToString() : "ResearchOrganized_PeriodDays".Translate(rest).ToString();
            string yearLabel = years == 1 ? "ResearchOrganized_PeriodYear".Translate(1).ToString() : "ResearchOrganized_PeriodYears".Translate(years).ToString();
            if (years <= 0) return dayLabel;
            if (rest <= 0) return yearLabel;
            return yearLabel + " " + dayLabel;
        }
    }
}
