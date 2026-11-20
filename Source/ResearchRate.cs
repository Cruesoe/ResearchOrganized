using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ResearchOrganized
{
    /// <summary>The colony's recent research speed, from the points colonists add to the current project, kept in hourly
    /// buckets over the last day. Points are stored before the tech-level cost factor, so any project's time can be worked
    /// out from them. Lifetime: one game; saved with it by ProjectQueueComponent and cleared when another game is loaded.</summary>
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
            int span = System.Math.Min(tick - firstTick, Buckets * BucketTicks);
            if (span < BucketTicks / 4) return 0f;
            int hour = tick / BucketTicks;
            float sum = 0f;
            for (int i = 0; i < Buckets; i++)
                if (bucketHour[i] > hour - Buckets) sum += points[i];
            return sum / span;
        }

        public static bool HasEstimate => PointsPerTick() > 0f;

        /// <summary>Points of work left on a standard research project, before cost factors; 0 when there is none to time.</summary>
        public static float WorkLeft(ResearchProjectDef project)
        {
            if (project == null || project.knowledgeCategory != null || project.IsFinished || project.baseCost <= 0f) return 0f;
            float left = project.Cost - project.ProgressReal;
            return left > 0f ? left * CostFactor(project) : 0f;
        }

        /// <summary>Ticks to research this much work at the recent pace; -1 when there is no estimate.</summary>
        public static int TicksFor(float work)
        {
            float rate = PointsPerTick();
            if (rate <= 0f || work <= 0f) return -1;
            return (int)System.Math.Min(work / rate, int.MaxValue);
        }

        /// <summary>Ticks left on a standard research project at the recent pace; -1 when there is no estimate.</summary>
        public static int TicksLeft(ResearchProjectDef project) => TicksFor(WorkLeft(project));
    }
}
