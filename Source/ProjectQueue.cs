using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ResearchOrganized
{
    internal static class ProjectQueue
    {
        private static readonly List<ResearchProjectDef> empty = new List<ResearchProjectDef>();
        private static readonly Dictionary<ResearchProjectDef, int> indexes = new Dictionary<ResearchProjectDef, int>();
        private static List<ResearchProjectDef> indexedList;
        private static int indexedVersion = -1;

        /// <summary>Counts up whenever the queue changes, so cached positions are rebuilt.</summary>
        public static int Version { get; private set; }

        /// <summary>On with the Rethemed window, whose left panel shows it. Off while Research Queue is active, or while Semi Random
        /// Research's "Prohibit normal project selection" is on, since either then chooses what to research.</summary>
        public static bool Available => DisplayMode.RethemedActive && !WindowHandover.ResearchQueueActive && !WindowHandover.SemiRandomRestricts;

        private static ProjectQueueComponent Component => Current.Game?.GetComponent<ProjectQueueComponent>();

        public static List<ResearchProjectDef> Items => Available ? Component?.queue ?? empty : empty;

        public static void Patch(Harmony harmony)
        {
            var finish = AccessTools.Method(typeof(ResearchManager), nameof(ResearchManager.FinishProject));
            if (finish != null) harmony.Patch(finish, postfix: new HarmonyMethod(typeof(ProjectQueue), nameof(AfterFinish)));
        }

        /// <summary>The project's place in the queue, from 0; -1 when it isn't queued.</summary>
        public static int IndexOf(ResearchProjectDef project)
        {
            var items = Items;
            if (items.Count == 0) return -1;
            if (!ReferenceEquals(indexedList, items) || indexedVersion != Version)
            {
                indexedList = items;
                indexedVersion = Version;
                indexes.Clear();
                for (int i = 0; i < items.Count; i++) indexes[items[i]] = i;
            }
            return indexes.TryGetValue(project, out int index) ? index : -1;
        }

        public static bool CanQueue(ResearchProjectDef project) =>
            Available && project != null && !project.IsFinished && project.knowledgeCategory == null && project.baseCost > 0f;

        /// <summary>Queues the project after any unfinished prerequisites it still needs that aren't queued yet.</summary>
        public static void Add(ResearchProjectDef project)
        {
            var items = Component?.queue;
            if (items == null || !CanQueue(project) || items.Contains(project)) return;
            var visited = new HashSet<ResearchProjectDef>();
            AddWithPrerequisites(items, project, visited);
            Changed();
            StartNextIfIdle();
        }

        private static void AddWithPrerequisites(List<ResearchProjectDef> items, ResearchProjectDef project, HashSet<ResearchProjectDef> visited)
        {
            if (!visited.Add(project)) return;
            AddPrerequisites(items, project.prerequisites, visited);
            AddPrerequisites(items, project.hiddenPrerequisites, visited);
            if (CanQueue(project) && !items.Contains(project) && !Find.ResearchManager.IsCurrentProject(project)) items.Add(project);
        }

        private static void AddPrerequisites(List<ResearchProjectDef> items, List<ResearchProjectDef> prerequisites, HashSet<ResearchProjectDef> visited)
        {
            if (prerequisites == null) return;
            foreach (var prerequisite in prerequisites)
                if (prerequisite != null && !prerequisite.IsFinished) AddWithPrerequisites(items, prerequisite, visited);
        }

        public static void Remove(ResearchProjectDef project)
        {
            var items = Component?.queue;
            if (items != null && items.Remove(project)) Changed();
        }

        public static void Move(int from, int to)
        {
            var items = Component?.queue;
            if (items == null || from < 0 || from >= items.Count) return;
            var project = items[from];
            items.RemoveAt(from);
            items.Insert(UnityEngine.Mathf.Clamp(to, 0, items.Count), project);
            Changed();
        }

        private static void Changed() => Version++;

        /// <summary>FinishProject postfix: drops finished projects and, when nothing is being researched, starts the next.</summary>
        private static void AfterFinish()
        {
            var items = Component?.queue;
            if (items == null || !Available) return;
            if (items.RemoveAll(p => p == null || p.IsFinished) > 0) Changed();
            StartNextIfIdle();
        }

        /// <summary>Starts the first queued project that can start now, when no standard project is being researched.</summary>
        public static void StartNextIfIdle()
        {
            var items = Component?.queue;
            if (items == null || !Available || Find.ResearchManager.GetProject() != null) return;
            for (int i = 0; i < items.Count; i++)
            {
                var project = items[i];
                if (project.IsFinished || !project.CanStartNow) continue;
                items.RemoveAt(i);
                Changed();
                Find.ResearchManager.SetCurrentProject(project);
                Messages.Message("ResearchOrganized_QueueStarted".Translate(project.LabelCap), MessageTypeDefOf.SilentInput, false);
                return;
            }
        }
    }
}
