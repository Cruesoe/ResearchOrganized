using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ResearchOrganized
{
    public static class ResearchOrganizedLayout
    {
        private const float FloatEpsilon = 0.01f;

        private static readonly Dictionary<ResearchProjectDef, List<ResearchProjectDef>> cachedPrereqs =
            new Dictionary<ResearchProjectDef, List<ResearchProjectDef>>();

        private static readonly Dictionary<ResearchProjectDef, HashSet<ResearchProjectDef>> ancestorsCache =
            new Dictionary<ResearchProjectDef, HashSet<ResearchProjectDef>>();

        public static HashSet<ResearchProjectDef> cyclicNodes = new HashSet<ResearchProjectDef>();

        public static void ClearCaches()
        {
            cachedPrereqs.Clear();
            ancestorsCache.Clear();
            cyclicNodes.Clear();
        }

        public static void ApplyTopologicalEpochLayout(List<ResearchProjectDef> tabNodes, string tabName, List<string> minorAnchors, List<string> majorAnchors)
        {
            var allAnchors = minorAnchors.Concat(majorAnchors).Distinct().ToList();
            var sortedAnchors = allAnchors
                .Select(a => DefDatabase<ResearchProjectDef>.GetNamedSilentFail(a))
                .Where(def => def != null)
                .OrderBy(def => (int)def.techLevel)
                .ThenBy(def => GetDirectPrereqs(def).Count > 0 ? GetDirectPrereqs(def).Min(p => (int)p.techLevel) : 0)
                .ThenBy(def => GetAllAncestors(def).Count)
                .Select(def => def.defName)
                .ToList();

            HashSet<ResearchProjectDef> placedNodes = new HashSet<ResearchProjectDef>();
            float currentX = 0f;

            float xStep = ResearchOrganizedMain.GlobalXStep;
            float yStep = ResearchOrganizedMain.GlobalYStep;
            int maxNodes = ResearchOrganizedMain.GlobalMaxNodesPerColumn;

            if (ResearchOrganizedMain.TabLayouts.TryGetValue(tabName, out var customLayout))
            {
                xStep = customLayout.xStep;
                yStep = customLayout.yStep;
                maxNodes = customLayout.maxNodesPerColumn;
            }

            var presentTechLevels = tabNodes.Select(n => n.techLevel).Distinct().OrderBy(t => (int)t).ToList();

            foreach (var techLevel in presentTechLevels)
            {
                var epochNodes = tabNodes.Where(n => n.techLevel == techLevel).ToList();
                if (epochNodes.Count == 0) continue;

                var occupiedSlots = new HashSet<Vector2>();
                var colOccupancy = new Dictionary<float, int>();
                float epochBaseX = currentX;

                var currentTabAnchors = sortedAnchors.Where(a => epochNodes.Any(n => n.defName == a)).ToList();

                var starterNodes = epochNodes.Where(n =>
                    !currentTabAnchors.Contains(n.defName) &&
                    !GetAllAncestors(n).Any(a => currentTabAnchors.Contains(a.defName))
                ).ToList();

                if (starterNodes.Count > 0)
                {
                    PlaceNodesDAG(starterNodes, ref currentX, placedNodes, occupiedSlots, colOccupancy, xStep, yStep, maxNodes);
                }

                var currentTabAnchorsProjects = sortedAnchors
                    .Select(a => epochNodes.FirstOrDefault(n => n.defName == a))
                    .Where(n => n != null && !placedNodes.Contains(n))
                    .ToList();

                while (currentTabAnchorsProjects.Count > 0)
                {
                    var readyAnchor = currentTabAnchorsProjects.FirstOrDefault(a =>
                        GetDirectPrereqs(a).All(p => placedNodes.Contains(p) || !epochNodes.Contains(p)));

                    if (readyAnchor == null)
                    {
                        readyAnchor = currentTabAnchorsProjects
                            .OrderBy(a => GetDirectPrereqs(a).Count(p => epochNodes.Contains(p) && !placedNodes.Contains(p)))
                            .First();
                    }

                    currentTabAnchorsProjects.Remove(readyAnchor);

                    var dependentNodes = epochNodes.Where(n =>
                        !placedNodes.Contains(n) &&
                        !currentTabAnchors.Contains(n.defName) &&
                        IsNodeInThisAnchorEpoch(n, readyAnchor.defName, currentTabAnchors)
                    ).ToList();

                    float anchorStartX = epochBaseX;
                    var placedParents = GetDirectPrereqs(readyAnchor).Where(p => placedNodes.Contains(p)).ToList();

                    if (placedParents.Count > 0)
                    {
                        anchorStartX = placedParents.Max(p => p.researchViewX) + xStep;
                    }

                    if (majorAnchors.Contains(readyAnchor.defName))
                    {
                        anchorStartX = Mathf.Max(anchorStartX, currentX);
                    }

                    int chosenYRow = 0;
                    while (occupiedSlots.Contains(new Vector2(anchorStartX, chosenYRow)) && chosenYRow < maxNodes)
                    {
                        chosenYRow++;
                    }

                    if (chosenYRow >= maxNodes)
                    {
                        anchorStartX += xStep;
                        chosenYRow = 0;
                        while (occupiedSlots.Contains(new Vector2(anchorStartX, chosenYRow)))
                        {
                            chosenYRow++;
                        }
                    }

                    readyAnchor.researchViewX = anchorStartX;
                    readyAnchor.researchViewY = chosenYRow * yStep;
                    occupiedSlots.Add(new Vector2(anchorStartX, chosenYRow));
                    colOccupancy[anchorStartX] = colOccupancy.TryGetValue(anchorStartX, out int cnt) ? cnt + 1 : 1;
                    placedNodes.Add(readyAnchor);

                    if (dependentNodes.Count > 0)
                    {
                        float childStartX = anchorStartX + xStep;
                        PlaceNodesDAG(dependentNodes, ref childStartX, placedNodes, occupiedSlots, colOccupancy, xStep, yStep, maxNodes);

                        if (childStartX > currentX) currentX = childStartX;

                        var directChildren = dependentNodes.Where(n => GetDirectPrereqs(n).Contains(readyAnchor)).ToList();
                        if (directChildren.Count > 0)
                        {
                            float minY = directChildren.Min(c => c.researchViewY);
                            float maxY = directChildren.Max(c => c.researchViewY);
                            float idealY = (minY + maxY) / 2f;

                            int idealRow = Mathf.RoundToInt(idealY / yStep);
                            if (idealRow >= maxNodes) idealRow = maxNodes - 1;

                            if (idealRow != chosenYRow && !occupiedSlots.Contains(new Vector2(readyAnchor.researchViewX, idealRow)))
                            {
                                occupiedSlots.Remove(new Vector2(readyAnchor.researchViewX, chosenYRow));
                                readyAnchor.researchViewY = idealRow * yStep;
                                occupiedSlots.Add(new Vector2(readyAnchor.researchViewX, idealRow));
                            }
                        }
                    }

                    if (anchorStartX + xStep > currentX) currentX = anchorStartX + xStep;
                }

                var orphanedNodes = epochNodes.Where(n => !placedNodes.Contains(n)).ToList();
                if (orphanedNodes.Count > 0)
                {
                    PlaceNodesDAG(orphanedNodes, ref currentX, placedNodes, occupiedSlots, colOccupancy, xStep, yStep, maxNodes);
                }

                PostProcessAnchorAlignment(epochNodes, xStep, yStep, maxNodes);
            }
        }

        private static void PostProcessAnchorAlignment(List<ResearchProjectDef> epochNodes, float xStep, float yStep, int maxNodes)
        {
            var occupiedGrid = new HashSet<Vector2>();
            foreach (var node in epochNodes)
            {
                occupiedGrid.Add(new Vector2(Mathf.RoundToInt(node.researchViewX / xStep), Mathf.RoundToInt(node.researchViewY / yStep)));
            }

            var rightToLeftNodes = epochNodes.OrderByDescending(n => n.researchViewX).ToList();

            foreach (var node in rightToLeftNodes)
            {
                var children = epochNodes.Where(c => GetDirectPrereqs(c).Contains(node)).ToList();
                if (children.Count == 0) continue;

                int currentGridX = Mathf.RoundToInt(node.researchViewX / xStep);
                int currentGridY = Mathf.RoundToInt(node.researchViewY / yStep);

                int targetGridX = Mathf.RoundToInt(children.Min(c => c.researchViewX) / xStep) - 1;

                var parents = GetDirectPrereqs(node).Where(p => epochNodes.Contains(p)).ToList();
                int minAllowedX = parents.Count > 0 ? Mathf.RoundToInt(parents.Max(p => p.researchViewX) / xStep) + 1 : 0;

                if (targetGridX > currentGridX && targetGridX >= minAllowedX)
                {
                    bool pathClear = true;
                    for (int x = currentGridX + 1; x <= targetGridX; x++)
                    {
                        if (occupiedGrid.Contains(new Vector2(x, currentGridY)))
                        {
                            pathClear = false;
                            break;
                        }
                    }

                    if (pathClear)
                    {
                        occupiedGrid.Remove(new Vector2(currentGridX, currentGridY));
                        node.researchViewX = targetGridX * xStep;
                        currentGridX = targetGridX;
                        occupiedGrid.Add(new Vector2(currentGridX, currentGridY));
                    }
                }

                float avgChildY = children.Average(c => c.researchViewY);
                int targetGridY = Mathf.RoundToInt(avgChildY / yStep);

                if (targetGridY != currentGridY && targetGridY >= 0 && targetGridY < maxNodes)
                {
                    if (!occupiedGrid.Contains(new Vector2(currentGridX, targetGridY)))
                    {
                        occupiedGrid.Remove(new Vector2(currentGridX, currentGridY));
                        node.researchViewY = targetGridY * yStep;
                        occupiedGrid.Add(new Vector2(currentGridX, targetGridY));
                    }
                }
            }
        }

        private static void PlaceNodesDAG(
            List<ResearchProjectDef> nodesToPlace,
            ref float currentX,
            HashSet<ResearchProjectDef> placedNodes,
            HashSet<Vector2> occupiedSlots,
            Dictionary<float, int> colOccupancy,
            float xStep,
            float yStep,
            int maxNodes)
        {
            var unplaced = new HashSet<ResearchProjectDef>(nodesToPlace);
            var epochBaseX = currentX;
            float localMaxX = epochBaseX;

            colOccupancy.Clear();
            foreach (var slot in occupiedSlots)
            {
                colOccupancy[slot.x] = colOccupancy.TryGetValue(slot.x, out int existing) ? existing + 1 : 1;
            }

            while (unplaced.Count > 0)
            {
                var readyPool = unplaced.Where(n => GetDirectPrereqs(n).All(p => !unplaced.Contains(p))).ToList();

                if (readyPool.Count == 0)
                {
                    var trappedNode = unplaced
                        .OrderBy(n => GetDirectPrereqs(n).Count(p => unplaced.Contains(p)))
                        .First();
                    cyclicNodes.Add(trappedNode);
                    var unresolved = GetDirectPrereqs(trappedNode).Where(p => unplaced.Contains(p)).Select(p => p.defName);
                    Log.Error($"[Research: Organized] Circular dependency detected on '{trappedNode.defName}'. " +
                              $"Unresolved prerequisites still in queue: [{string.Join(", ", unresolved)}]. " +
                              $"The node will be placed but its connections may look incorrect. " +
                              $"This is usually caused by a mod conflict or malformed XML.");
                    readyPool.Add(trappedNode);
                }

                while (readyPool.Count > 0)
                {
                    var parentChildCounts = new Dictionary<ResearchProjectDef, int>();
                    foreach (var n in readyPool)
                    {
                        var placedParents = GetDirectPrereqs(n).Where(pr => placedNodes.Contains(pr)).ToList();
                        foreach (var p in placedParents)
                        {
                            if (!parentChildCounts.ContainsKey(p)) parentChildCounts[p] = 0;
                            parentChildCounts[p]++;
                        }
                    }

                    ResearchProjectDef bestParent = null;
                    int maxCount = 0;
                    foreach (var kvp in parentChildCounts)
                    {
                        if (kvp.Value > maxCount)
                        {
                            maxCount = kvp.Value;
                            bestParent = kvp.Key;
                        }
                    }

                    List<ResearchProjectDef> siblingBlock = new List<ResearchProjectDef>();

                    if (bestParent != null)
                    {
                        siblingBlock = readyPool.Where(n => GetDirectPrereqs(n).Contains(bestParent)).ToList();
                    }
                    else
                    {
                        siblingBlock = readyPool.Where(n => !GetDirectPrereqs(n).Any(p => placedNodes.Contains(p))).ToList();
                        if (siblingBlock.Count == 0) siblingBlock.Add(readyPool.First());
                    }

                    var siblingDesiredY = new Dictionary<ResearchProjectDef, float>();
                    float blockMinX = epochBaseX;

                    foreach (var node in siblingBlock)
                    {
                        var @placedParents = GetDirectPrereqs(node).Where(p => placedNodes.Contains(p)).ToList();
                        if (@placedParents.Count > 0)
                        {
                            float maxParentX = @placedParents.Max(p => p.researchViewX);
                            blockMinX = Mathf.Max(blockMinX, maxParentX + xStep);

                            var immediateParents = @placedParents
                                .Where(p => Mathf.Abs(p.researchViewX - maxParentX) < FloatEpsilon)
                                .ToList();
                            siblingDesiredY[node] = immediateParents.Average(p => p.researchViewY / yStep);
                        }
                        else
                        {
                            siblingDesiredY[node] = (maxNodes - 1) / 2f;
                        }
                    }

                    siblingBlock = siblingBlock.OrderBy(n => siblingDesiredY[n]).ToList();

                    float targetX = blockMinX;

                    var chunks = new List<List<ResearchProjectDef>>();
                    for (int i = 0; i < siblingBlock.Count; i += maxNodes)
                    {
                        chunks.Add(siblingBlock.GetRange(i, Math.Min(maxNodes, siblingBlock.Count - i)));
                    }

                    foreach (var chunk in chunks)
                    {
                        int blockHeight = chunk.Count;

                        while (true)
                        {
                            int countInCol = colOccupancy.TryGetValue(targetX, out int c) ? c : 0;
                            if (countInCol + blockHeight <= maxNodes) break;
                            targetX += xStep;
                        }

                        float avgDesiredY = chunk.Average(n => siblingDesiredY[n]);
                        int idealStartRow = Mathf.RoundToInt(avgDesiredY) - (blockHeight / 2);
                        if (idealStartRow < 0) idealStartRow = 0;
                        if (idealStartRow + blockHeight > maxNodes) idealStartRow = maxNodes - blockHeight;

                        int chosenStartRow = idealStartRow;
                        float currentChosenX = targetX;

                        while (true)
                        {
                            int offset = 0;
                            bool blockFound = false;

                            while (offset < maxNodes)
                            {
                                int testStart = idealStartRow + offset;
                                if (testStart >= 0 && testStart + blockHeight <= maxNodes)
                                {
                                    bool allFree = true;
                                    for (int i = 0; i < blockHeight; i++)
                                    {
                                        if (occupiedSlots.Contains(new Vector2(currentChosenX, testStart + i)))
                                        {
                                            allFree = false; break;
                                        }
                                    }
                                    if (allFree) { chosenStartRow = testStart; blockFound = true; break; }
                                }

                                testStart = idealStartRow - offset;
                                if (testStart >= 0 && testStart + blockHeight <= maxNodes)
                                {
                                    bool allFree = true;
                                    for (int i = 0; i < blockHeight; i++)
                                    {
                                        if (occupiedSlots.Contains(new Vector2(currentChosenX, testStart + i)))
                                        {
                                            allFree = false; break;
                                        }
                                    }
                                    if (allFree) { chosenStartRow = testStart; blockFound = true; break; }
                                }

                                offset++;
                                if (offset == 1) continue;
                            }

                            if (blockFound) break;
                            currentChosenX += xStep;
                        }

                        for (int i = 0; i < chunk.Count; i++)
                        {
                            var node = chunk[i];
                            readyPool.Remove(node);

                            node.researchViewX = currentChosenX;
                            node.researchViewY = (chosenStartRow + i) * yStep;
                            occupiedSlots.Add(new Vector2(currentChosenX, chosenStartRow + i));
                            colOccupancy[currentChosenX] = colOccupancy.TryGetValue(currentChosenX, out int cnt) ? cnt + 1 : 1;
                            placedNodes.Add(node);
                            unplaced.Remove(node);
                        }

                        targetX = currentChosenX;
                        localMaxX = Mathf.Max(localMaxX, currentChosenX);
                    }

                    var newlyReady = unplaced
                        .Where(n => !readyPool.Contains(n) && GetDirectPrereqs(n).All(p => !unplaced.Contains(p)))
                        .ToList();
                    readyPool.AddRange(newlyReady);
                }
            }

            currentX = localMaxX + xStep;
        }

        private static bool IsNodeInThisAnchorEpoch(ResearchProjectDef node, string anchorDefName, List<string> currentTabAnchors)
        {
            var ancestors = GetAllAncestors(node);
            if (!ancestors.Any(a => a.defName == anchorDefName)) return false;

            int currentAnchorIdx = currentTabAnchors.IndexOf(anchorDefName);

            foreach (var a in ancestors)
            {
                int idx = currentTabAnchors.IndexOf(a.defName);
                if (idx > currentAnchorIdx)
                {
                    return false;
                }
            }
            return true;
        }

        public static HashSet<ResearchProjectDef> GetAllAncestors(ResearchProjectDef node)
        {
            if (ancestorsCache.TryGetValue(node, out var cached)) return cached;

            HashSet<ResearchProjectDef> ancestors = new HashSet<ResearchProjectDef>();
            Stack<ResearchProjectDef> stack = new Stack<ResearchProjectDef>();
            stack.Push(node);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                foreach (var pre in GetDirectPrereqs(current))
                {
                    if (ancestors.Add(pre)) stack.Push(pre);
                }
            }

            ancestorsCache[node] = ancestors;
            return ancestors;
        }

        public static List<ResearchProjectDef> GetDirectPrereqs(ResearchProjectDef def)
        {
            if (cachedPrereqs.TryGetValue(def, out var res)) return res;
            var list = new HashSet<ResearchProjectDef>(def.prerequisites ?? new List<ResearchProjectDef>());
            if (def.hiddenPrerequisites != null) foreach (var p in def.hiddenPrerequisites) list.Add(p);
            if (ResearchOrganizedMain.VirtualPrereqsCache.TryGetValue(def, out var v)) foreach (var vp in v) list.Add(vp);
            return cachedPrereqs[def] = list.ToList();
        }
    }
}