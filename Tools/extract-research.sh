#!/usr/bin/env bash
# Pulls every ResearchProjectDef out of the game and the installed mods into one
# pipe-delimited file, so the layout can be exercised against a realistic tree
# without launching RimWorld.
#
#   defName|techLevel|baseCost|prereq,prereq,...
#
# Caveat worth remembering when reading the output: PatchOperations are NOT applied.
# Mods that rewrite prerequisites through patches - which the Progression pack does a
# lot of - will differ from what the game actually loads. This is for judging whether
# a layout reads well on a realistic graph, not for reproducing one exact tab.
set -u

OUT="${1:-research-graph.txt}"
GAME="/c/Program Files (x86)/Steam/steamapps/common/RimWorld"
WORKSHOP="/c/Program Files (x86)/Steam/steamapps/workshop/content/294100"

: > "$OUT"

find "$GAME/Data" "$GAME/Mods" "$WORKSHOP" -ipath "*ResearchProjectDef*" -name "*.xml" -print0 2>/dev/null \
| xargs -0 awk '
  /<ResearchProjectDef/     { inDef=1; name=""; tech=""; cost=""; pre=""; inPre=0; next }
  inDef && /<\/ResearchProjectDef>/ {
      if (name != "") printf "%s|%s|%s|%s\n", name, tech, cost, pre
      inDef=0; next
  }
  !inDef { next }

  /<defName>/   { if (name=="") { s=$0; sub(/.*<defName>/,"",s); sub(/<\/defName>.*/,"",s); gsub(/[ \t\r]/,"",s); name=s } }
  /<techLevel>/ { s=$0; sub(/.*<techLevel>/,"",s); sub(/<\/techLevel>.*/,"",s); gsub(/[ \t\r]/,"",s); tech=s }
  /<baseCost>/  { s=$0; sub(/.*<baseCost>/,"",s); sub(/<\/baseCost>.*/,"",s); gsub(/[ \t\r]/,"",s); cost=s }

  /<prerequisites>|<hiddenPrerequisites>/   { inPre=1; next }
  /<\/prerequisites>|<\/hiddenPrerequisites>/ { inPre=0; next }

  inPre && /<li>/ {
      s=$0; sub(/.*<li[^>]*>/,"",s); sub(/<\/li>.*/,"",s); gsub(/[ \t\r]/,"",s)
      if (s != "") pre = (pre=="" ? s : pre "," s)
  }
' | sort -u -t'|' -k1,1 > "$OUT"

echo "wrote $(wc -l < "$OUT") projects to $OUT"
