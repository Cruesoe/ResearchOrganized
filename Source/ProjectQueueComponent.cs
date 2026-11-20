using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ResearchOrganized
{
    /// <summary>The research queue and research pace, saved with the game: when the current project finishes, the first queued
    /// project that can start is researched next. Standard research only; Anomaly knowledge projects use their own slots.</summary>
    public class ProjectQueueComponent : GameComponent
    {
        public List<ResearchProjectDef> queue = new List<ResearchProjectDef>();

        public ProjectQueueComponent(Game game)
        {
        }

        /// <summary>Hourly, so a queued project starts once something that blocked it (a bench, a techprint) is in place.</summary>
        public override void GameComponentTick()
        {
            if (queue.Count > 0 && Find.TickManager.TicksGame % GenDate.TicksPerHour == 0) ProjectQueue.StartNextIfIdle();
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref queue, "queue", LookMode.Def);
            // The colony's research pace over the last day, so time estimates carry on after loading.
            ResearchRate.ExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                queue = queue ?? new List<ResearchProjectDef>();
                queue.RemoveAll(p => p == null);
            }
        }
    }
}
