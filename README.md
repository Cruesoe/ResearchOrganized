# Research: Organized

RimWorld 1.6 mod. Sorts research projects into **tech-level tabs** and lays each tab out as a readable tree.

- Projects are mapped to Primitive, Neolithic, Medieval, Industrial, High/Late Industrial, Spacer, Ultra, Archotech, Anomaly, or Miscellaneous tabs. When VFE Tribals is active, its Basics tab replaces Primitive and receives every Animal-tier project.
- **Combine All Tabs** (mod setting) puts every project except Anomaly and Gravship on the vanilla Main tab instead. Each tech level is laid out as its own block, exactly as its tab would be, and the blocks run left to right with a blank column between them, so no era starts before the previous one ends. Projects with no tech level form the last block. Turning the setting off returns every project to the tab it would normally use, including preserved tabs. Connector lines between two eras are not drawn there, so only each block's own dependencies show.
- A project is raised to the latest tech level among its prerequisites, so a follow-up never sits in an earlier era than what it depends on. This changes the project's real tech level, and with it the research cost.
- Node Research's foundation technologies are marked with a small gold triangle in the card's top-right corner.
- A gear in the research window's left panel opens these settings.
- Each tab gets a generated, non-overlapping layout instead of the vanilla scatter, with columns capped by `maxNodesPerColumn`.
- Layout is computed by an anchor-aware epoch pipeline followed by a bounded row optimizer that reduces connector crossings while preserving dependency columns and column limits.
- Nodes are tinted by tech level, with finished/available/unavailable brightness levels. Colours are configurable, or can be turned off entirely.
- Empty tabs are removed from the def database and the rest are re-sorted into a configured order.
- Projects caught in a circular dependency are drawn with a red border and named in the log, so a broken modlist diagnoses itself.
- With Node Research active, linked emergence nodes stay beside their prerequisite branch; the VFE Tribals Basics tab also uses compact, non-overlapping spacing so the combined graph is less likely to force horizontal scrolling.
- `virtualLinks` add prerequisite relationships for layout purposes only; `visibleLinks` also draw the connector.

Requires [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077).

Incompatible with other research-tab organisers (Tech Tree, TTPF, Research Tab Colors, Organized Research Tech, Organized Research Tab, Clean Research Sort) — see `About/About.xml`.

## Configuration

`1.6/Defs/TechTreeConfig.xml` defines a `ResearchOrganized.ResearchOrganizedConfig` def controlling tab order, ignored and preserved tabs, tech-level tab overrides, per-tab themes, spacing (`xStep` / `yStep`), and `maxNodesPerColumn`. The VFE Tribals integration is data-driven here: `Animal` routes to `VFET_Basics`, while that external tab is preserved for any projects it authors itself. Mod settings cover anchor thresholds, combining all tabs or just the industrial tabs, and the per-tech-level colour palette.

## How the layout works

`Source/Layout/` holds a deterministic, anchor-aware graph layout pipeline:

1. **Cycle analysis and removal** — strongly connected components identify every project in a cycle, then a deterministic low-feedback ordering reverses the conflicting edges so later stages receive a clean DAG while retaining as many authored directions as practical.
2. **Epoch and anchor placement** — projects are processed by tech level. Cheap starter projects go first; high-fan-out anchors retain dedicated columns; each anchor's descendants are kept nearby; column height is capped throughout.
3. **Constrained row optimization** — alternating barycentric sweeps make broad row improvements, then adjacent swaps refine them. A candidate is accepted only when it has fewer crossings, or an equal crossing count with better authored-position and connector-span scores. Local delta scoring keeps the work bounded; columns and anchors never move.
4. **Renderer-matched crossing measurement** — the score uses RimWorld 1.6's actual straight prerequisite lines and its card spacing, including long connectors crossing through intermediate columns.

Each tab is laid out independently. Within it, loose projects and disconnected branches share the available column capacity, while dependency chains advance to the right of their deepest parent. This keeps sparse modded tabs compact without letting unrelated projects break dependency direction.

Nothing in `Source/Layout/` references RimWorld or Unity. That is what makes it testable.

## Tests

```
dotnet run --project Tests\ResearchOrganized.Tests.csproj -c Release
```

A plain console exe rather than a test framework, so it needs no test package and runs anywhere the mod builds. Exit code 0 means everything passed. It covers tab-routing precedence, the layering invariant (every child right of its parents), the column cap, complete and deterministic cycle handling, renderer-matched crossing measurement and reduction, authored-row preference, minimum spacing, and determinism. It also exercises the complete VFE Tribals Basics graph, Industrial and Spacer slices from a checked-in active-mod-list dump, and a 400-node scale benchmark with a ten-second budget.

## Connection lines

RimWorld 1.6 already skips a connector when the prerequisite is on another tab. On the combined tab, a transpiler on the research window extends that same check so connectors between two eras are skipped too, leaving only the lines within each era block.

## Install

Copy this folder to `RimWorld\Mods\`, or add it as a local mod in RimSort.

## Build

```
dotnet build Source\ResearchOrganized.sln -c Release
```

The repository-local build targets stage the Release DLL in `1.6\Assemblies\` and omit the PDB. The project targets .NET Framework 4.7.2 and references RimWorld's `Assembly-CSharp.dll`, the required UnityEngine modules, and `0Harmony.dll` directly from their Steam install paths.

## History

Supersedes [TechTreeProgression](https://github.com/Cruesoe/TechTreeProgression), which did the same job with XML patches through GonDragon's Tech Tree Patch Framework. This version replaces that with a Harmony assembly and no longer depends on TTPF.
