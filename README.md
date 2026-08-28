# Research: Organized

RimWorld 1.6 mod. Sorts research projects into **tech-level tabs** and lays each tab out as a readable tree.

- Projects are mapped to Primitive, Neolithic, Medieval, Industrial, High/Late Industrial, Spacer, Ultra, Archotech, Anomaly, or Miscellaneous tabs.
- Each tab gets a generated, non-overlapping layout instead of the vanilla scatter, with columns capped by `maxNodesPerColumn`.
- Nodes are tinted by tech level, with finished/available/unavailable brightness levels. Colours are configurable, or can be turned off entirely.
- Empty tabs are removed from the def database and the rest are re-sorted into a configured order.
- Projects caught in a circular dependency are drawn with a red border and named in the log, so a broken modlist diagnoses itself.
- `virtualLinks` add prerequisite relationships for layout purposes only; `visibleLinks` also draw the connector.

Requires [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077).

Incompatible with other research-tab organisers (Tech Tree, TTPF, Research Tab Colors, Organized Research Tech, Organized Research Tab, Clean Research Sort) — see `About/About.xml`.

## Configuration

`1.6/Defs/TechTreeConfig.xml` defines a `ResearchOrganized.ResearchOrganizedConfig` def controlling tab order, ignored tabs, per-tab themes, spacing (`xStep` / `yStep`), and `maxNodesPerColumn`. Mod settings cover anchor thresholds, combining the industrial tabs, and the per-tech-level colour palette.

## Install

Copy this folder to `RimWorld\Mods\`, or add it as a local mod in RimSort.

## Build

```
msbuild Source\ResearchOrganized.csproj -p:Configuration=Release
```

Output lands in `Source\bin\Release\ResearchOrganized.dll`; copy it to `1.6\Assemblies\` to ship it. The project targets .NET Framework 4.7.2 and references RimWorld's `Assembly-CSharp.dll`, the two UnityEngine modules, and `0Harmony.dll` from their Steam install paths.

## History

Supersedes [TechTreeProgression](https://github.com/Cruesoe/TechTreeProgression), which did the same job with XML patches through GonDragon's Tech Tree Patch Framework. This version replaces that with a Harmony assembly and no longer depends on TTPF.
