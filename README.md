# Research: Organized

RimWorld 1.6 mod. Sorts research projects into **tech-level tabs** and lays each tab out as a readable tree.

- Projects are mapped to Primitive, Neolithic, Medieval, Industrial, High/Late Industrial, Spacer, Ultra, Archotech, Anomaly, or Miscellaneous tabs.
- Each tab gets a generated, non-overlapping layout instead of the vanilla scatter, with columns capped by `maxNodesPerColumn`.
- Layout is computed by a layered graph-drawing pipeline that actively minimises edge crossings, rather than placing nodes greedily and hoping.
- Nodes are tinted by tech level, with finished/available/unavailable brightness levels. Colours are configurable, or can be turned off entirely.
- Empty tabs are removed from the def database and the rest are re-sorted into a configured order.
- Projects caught in a circular dependency are drawn with a red border and named in the log, so a broken modlist diagnoses itself.
- `virtualLinks` add prerequisite relationships for layout purposes only; `visibleLinks` also draw the connector.

Requires [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077).

Incompatible with other research-tab organisers (Tech Tree, TTPF, Research Tab Colors, Organized Research Tech, Organized Research Tab, Clean Research Sort) — see `About/About.xml`.

## Configuration

`1.6/Defs/TechTreeConfig.xml` defines a `ResearchOrganized.ResearchOrganizedConfig` def controlling tab order, ignored tabs, per-tab themes, spacing (`xStep` / `yStep`), and `maxNodesPerColumn`. Mod settings cover anchor thresholds, combining the industrial tabs, and the per-tech-level colour palette.

## How the layout works

`Source/Layout/` holds a standard four-stage layered graph-drawing pipeline (Sugiyama):

1. **Cycle removal** — a DFS finds back edges and reverses a small set of them, so the rest of the pipeline works on a clean DAG. Reversed edges are reported, and the projects involved are outlined in red.
2. **Layering** — Coffman–Graham labelling picks the order nodes are considered, then greedy assignment places each node one column right of its deepest parent, rolling forward when a column is full. `maxNodesPerColumn` is enforced here rather than patched up later.
3. **Crossing reduction** — dummy nodes split long edges so every edge spans one column, then alternating median sweeps with a transpose pass cut the number of crossings. The best ordering seen is kept, so the result can never be worse than the starting point.
4. **Coordinates** — the priority method nudges each node toward its neighbours' average position without reordering anything, so long dependency chains come out straight. Dummy nodes get top priority.

Each tab is laid out on its own, and within a tab each **connected component** is laid out separately and then shelf-packed left-aligned against the origin. This matters because a tab holds one tech level while prerequisites cross tech levels, so a tab is mostly loose projects plus a few short chains. Sharing one column budget across unrelated fragments pushes a project many columns away from its own parent and leaves the tab sprawling.

Nothing in `Source/Layout/` references RimWorld or Unity. That is what makes it testable.

## Tests

```
msbuild Tests\ResearchOrganized.Tests.csproj -p:Configuration=Debug
Tests\bin\Debug\ResearchOrganized.Tests.exe
```

A plain console exe rather than a test framework, so it needs no package restore and runs anywhere the mod builds. Exit code 0 means everything passed. It covers the layering invariant (every child right of its parents), the column cap, cycle handling, crossing reduction, minimum spacing, and determinism, plus a scale benchmark that fails if a 400-node tree takes more than ten seconds.

## Known gaps

- Cross-tab connection lines are not suppressed. Vanilla still draws connectors to prerequisites that live on another tab. The old no-op Harmony patch for this has been removed rather than left in place pretending to work.

## Install

Copy this folder to `RimWorld\Mods\`, or add it as a local mod in RimSort.

## Build

```
msbuild Source\ResearchOrganized.csproj -p:Configuration=Release
```

Output lands in `Source\bin\Release\ResearchOrganized.dll`; copy it to `1.6\Assemblies\` to ship it. The project targets .NET Framework 4.7.2 and references RimWorld's `Assembly-CSharp.dll`, the two UnityEngine modules, and `0Harmony.dll` from their Steam install paths.

## History

Supersedes [TechTreeProgression](https://github.com/Cruesoe/TechTreeProgression), which did the same job with XML patches through GonDragon's Tech Tree Patch Framework. This version replaces that with a Harmony assembly and no longer depends on TTPF.
