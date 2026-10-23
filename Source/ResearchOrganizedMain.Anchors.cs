using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using ResearchOrganized.Layout;
using RimWorld;
using UnityEngine;
using Verse;

namespace ResearchOrganized
{
    public static partial class ResearchOrganizedMain
    {
        /// <summary>
        /// Finds the hubs each tab should be built around: a project with enough same-tab
        /// follow-ups is a hub, and once it is, its own children stop counting toward whether
        /// something upstream of it also qualifies - so a chain of hubs is not inflated by
        /// double-counting the same descendants.
        ///
        /// Evaluated bottom-up (deepest projects first) so that "already an anchor" is known
        /// for every child before its ancestors are checked. This is what the original mod
        /// used to isolate a big hub like Electricity onto its own column instead of burying
        /// it among a hundred other projects at the same depth.
        ///
        /// Run once per layout group, over that group's own links only, so nothing outside the
        /// block a project is drawn in can decide whether it is a hub or where it falls in the
        /// placement order.
        /// </summary>
        /// <param name="combinedTab">The "Combine All Tabs" tab, if any. Its era blocks are laid
        /// out as separate tabs would be, so each is its own group.</param>
        /// <param name="majorAnchors">The anchors that are major hubs, each placed alone in a new column.</param>
        private static HashSet<ResearchProjectDef> FindAnchors(ResearchTabDef combinedTab, out Dictionary<ResearchProjectDef, int> anchorOrder,
            out HashSet<ResearchProjectDef> majorAnchors)
        {
            var anchors = new HashSet<ResearchProjectDef>();
            anchorOrder = new Dictionary<ResearchProjectDef, int>();
            majorAnchors = new HashSet<ResearchProjectDef>();

            foreach (var group in GroupProjectsForLayout(combinedTab))
            {
                FindAnchorsInGroup(group, anchors, anchorOrder, majorAnchors);
            }

            return anchors;
        }

        /// <summary>
        /// The projects laid out together, and so able to influence each other's placement: one
        /// tab, or one era block of the combined tab, which <see cref="TabLayout.ComputeBuckets"/>
        /// lays out as a separate tab would be.
        /// </summary>
        private static List<List<ResearchProjectDef>> GroupProjectsForLayout(ResearchTabDef combinedTab)
        {
            var byTab = new Dictionary<ResearchTabDef, Dictionary<int, List<ResearchProjectDef>>>();
            foreach (var project in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
            {
                if (project.tab == null) continue;
                int era = project.tab == combinedTab ? ResearchOrganizedLayout.EraBucket(project) : 0;

                if (!byTab.TryGetValue(project.tab, out var byEra)) byTab[project.tab] = byEra = new Dictionary<int, List<ResearchProjectDef>>();
                if (!byEra.TryGetValue(era, out var members)) byEra[era] = members = new List<ResearchProjectDef>();
                members.Add(project);
            }

            var groups = new List<List<ResearchProjectDef>>();
            foreach (var byEra in byTab.Values) groups.AddRange(byEra.Values);
            return groups;
        }

        /// <summary>Projects that are always major anchors when they have follow-ups in their group, whatever the thresholds.</summary>
        private static readonly HashSet<string> ForcedMajorAnchors = new HashSet<string> { "ShipBasics" };

        /// <summary>
        /// Picks one group's hubs and their placement order, counting only links whose both ends
        /// are in the group. Order is numbered from zero per group; <see cref="EpochLayout"/> only
        /// ever compares two anchors drawn in the same block, so the numbers never need to mean
        /// anything across groups.
        /// </summary>
        private static void FindAnchorsInGroup(List<ResearchProjectDef> group,
            HashSet<ResearchProjectDef> anchors, Dictionary<ResearchProjectDef, int> anchorOrder, HashSet<ResearchProjectDef> majors)
        {
            var inGroup = new HashSet<ResearchProjectDef>(group);

            var childrenMap = new Dictionary<ResearchProjectDef, List<ResearchProjectDef>>();
            var parentMap = new Dictionary<ResearchProjectDef, List<ResearchProjectDef>>();
            foreach (var proj in group)
            {
                foreach (var pre in ResearchOrganizedLayout.GetDirectPrereqs(proj))
                {
                    if (!inGroup.Contains(pre)) continue;

                    if (!parentMap.TryGetValue(proj, out var parents)) parentMap[proj] = parents = new List<ResearchProjectDef>();
                    parents.Add(pre);

                    if (ResearchOrganizedLayout.IsEraCapstone(proj)) continue; // never counts toward another project's hub status
                    if (!childrenMap.TryGetValue(pre, out var children)) childrenMap[pre] = children = new List<ResearchProjectDef>();
                    children.Add(proj);
                }
            }

            var ancestorCounts = new Dictionary<ResearchProjectDef, int>(group.Count);
            foreach (var proj in group) ancestorCounts[proj] = CountAncestorsInGroup(proj, parentMap);

            var bottomUp = new List<ResearchProjectDef>(group);
            bottomUp.Sort((a, b) => ancestorCounts[b].CompareTo(ancestorCounts[a]));

            int minorThreshold = ResearchOrganizedMod.settings.minorAnchorChildThreshold;
            int majorThreshold = ResearchOrganizedMod.settings.majorAnchorChildThreshold;

            var majorAnchors = new HashSet<ResearchProjectDef>();
            var minorAnchors = new HashSet<ResearchProjectDef>();

            foreach (var proj in bottomUp)
            {
                if (ResearchOrganizedLayout.IsEraCapstone(proj)) continue; // never itself a hub - it always goes last, not off to the side with a fan of its own
                if (!childrenMap.TryGetValue(proj, out var children)) continue;
                if (ForcedMajorAnchors.Contains(proj.defName)) { majorAnchors.Add(proj); continue; }

                int nonMajorChildren = children.Count(c => !majorAnchors.Contains(c));
                if (majorThreshold > 0 && nonMajorChildren >= majorThreshold) { majorAnchors.Add(proj); continue; }

                int nonAnchorChildren = children.Count(c => !majorAnchors.Contains(c) && !minorAnchors.Contains(c));
                if (minorThreshold > 0 && nonAnchorChildren >= minorThreshold) minorAnchors.Add(proj);
            }

            var anchorList = new List<ResearchProjectDef>(majorAnchors);
            anchorList.AddRange(minorAnchors);
            anchorList.Sort((a, b) =>
            {
                int byDepth = ancestorCounts[a].CompareTo(ancestorCounts[b]);
                if (byDepth != 0) return byDepth;
                int byTier = (majorAnchors.Contains(a) ? 1 : 0).CompareTo(majorAnchors.Contains(b) ? 1 : 0); // minor hubs before major ones at equal depth
                if (byTier != 0) return byTier;
                return string.CompareOrdinal(a.defName, b.defName);
            });

            for (int i = 0; i < anchorList.Count; i++)
            {
                anchors.Add(anchorList[i]);
                anchorOrder[anchorList[i]] = i;
            }
            majors.UnionWith(majorAnchors);
        }

        /// <summary>Everything <paramref name="node"/> depends on without leaving its group.</summary>
        private static int CountAncestorsInGroup(ResearchProjectDef node,
            Dictionary<ResearchProjectDef, List<ResearchProjectDef>> parentMap)
        {
            var ancestors = new HashSet<ResearchProjectDef>();
            var stack = new Stack<ResearchProjectDef>();
            stack.Push(node);

            while (stack.Count > 0)
            {
                if (!parentMap.TryGetValue(stack.Pop(), out var parents)) continue;
                foreach (var parent in parents) if (ancestors.Add(parent)) stack.Push(parent);
            }
            return ancestors.Count;
        }

    }
}
