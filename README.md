# Research: Organized

RimWorld 1.6 mod. Sorts research projects into **tech-level tabs** and lays each tab out as a readable tree.

- Projects are mapped to Primitive, Neolithic, Medieval, Industrial, High/Late Industrial, Spacer, Ultra, Archotech, Anomaly, or Miscellaneous tabs. When VFE Tribals is active, its Basics tab replaces Primitive and receives every Animal-tier project.
- Each tab gets a generated, non-overlapping layout instead of the vanilla scatter, with columns capped by `maxNodesPerColumn`.
- Layout is computed by an anchor-aware epoch pipeline followed by a bounded row optimizer that reduces connector crossings while preserving dependency columns and column limits.
- Nodes are tinted by tech level, with finished/available/unavailable brightness levels. Colours are configurable, or can be turned off entirely.
- Empty tabs are removed from the def database and the rest are re-sorted into a configured order.
- Projects caught in a circular dependency are drawn with a red border and named in the log, so a broken modlist diagnoses itself.
- `virtualLinks` add prerequisite relationships for layout purposes only; `visibleLinks` also draw the connector.

Requires [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077).

Incompatible with other research-tab organisers (Tech Tree, TTPF, Research Tab Colors, Organized Research Tech, Organized Research Tab, Clean Research Sort) — see `About/About.xml`.

## Configuration

`1.6/Defs/TechTreeConfig.xml` defines a `ResearchOrganized.ResearchOrganizedConfig` def controlling tab order, ignored tabs, per-tab themes, spacing (`xStep` / `yStep`), and `maxNodesPerColumn`. Mod settings cover anchor thresholds, combining the industrial tabs, and the per-tech-level colour palette.

## How the layout works

`Source/Layout/` holds a deterministic, anchor-aware graph layout pipeline:

1. **Cycle analysis and removal** — strongly connected components identify every project in a cycle, then a DFS reverses back edges so later stages receive a clean DAG.
2. **Epoch and anchor placement** — projects are processed by tech level. Cheap starter projects go first; high-fan-out anchors retain dedicated columns; each anchor's descendants are kept nearby; column height is capped throughout.
3. **Constrained row optimization** — adjacent non-anchor rows are exchanged only when the result has fewer crossings, or an equal crossing count with better authored-position and connector-span scores. Columns and anchors never move.
4. **Crossing measurement** — long connectors are split into virtual segments at column boundaries so crossings through intermediate columns are included in the final metric.

Each tab is laid out independently. Within it, loose projects and disconnected branches share the available column capacity, while dependency chains advance to the right of their deepest parent. This keeps sparse modded tabs compact without letting unrelated projects break dependency direction.

Nothing in `Source/Layout/` references RimWorld or Unity. That is what makes it testable.

## Tests

```
msbuild Tests\ResearchOrganized.Tests.csproj -p:Configuration=Debug
Tests\bin\Debug\ResearchOrganized.Tests.exe
```

A plain console exe rather than a test framework, so it needs no package restore and runs anywhere the mod builds. Exit code 0 means everything passed. It covers the layering invariant (every child right of its parents), the column cap, complete cycle reporting, long-edge crossing measurement and reduction, authored-row preference, minimum spacing, and determinism, plus a scale benchmark that fails if a 400-node tree takes more than ten seconds.

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
