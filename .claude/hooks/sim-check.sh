#!/usr/bin/env bash
# PostToolUse: after an edit in Game.Sim or its tests, lint Sim for non-determinism
# and run the tests. Exit 2 sends stderr back to Claude so the failure gets fixed now.
set -uo pipefail

input=$(cat)
file=$(printf '%s' "$input" | python3 -c 'import sys, json; print(json.load(sys.stdin).get("tool_input", {}).get("file_path", ""))' 2>/dev/null || true)

root="${CLAUDE_PROJECT_DIR:-$(pwd)}"

# Balance tables: Sim loads Assets/Data/*.json, so the json must follow the csv.
case "$file" in
  */Docs/Design/*.csv|*/Tools/*.py|*/Assets/Data/*.json)
    if ! out=$(python3 "$root/Tools/csv2json.py" --check 2>&1); then
      echo "$out" >&2
      exit 2
    fi
    if ! out=$(cd "$root/Tools" && python3 -m unittest -q 2>&1); then
      echo "Tools: тесты csv2json упали:" >&2
      echo "$out" | tail -n 30 >&2
      exit 2
    fi
    exit 0
    ;;
esac

case "$file" in
  */Assets/Game.Sim/*|*/Game.Sim.Tests/*|*/Game.Sim.Dotnet/*|*/Assets/Game.Net/*|*/Game.Net.Dotnet/*) ;;
  *) exit 0 ;;
esac
sim="$root/Assets/Game.Sim"

# Plain Dictionary/HashSet: iteration order is not part of the contract.
pattern='\b(float|double|decimal)\b|System\.Random|\bDateTime\b|\bTime\.|UnityEngine|\bDictionary<|\bHashSet<'
if hits=$(grep -rnE "$pattern" --include='*.cs' "$sim"); then
  echo "Game.Sim: запрещённое для детерминизма (float/double/Random/DateTime/Time/UnityEngine/Dictionary/HashSet):" >&2
  echo "$hits" >&2
  exit 2
fi

log=$(mktemp)
if ! dotnet test "$root/Game.Sim.Tests" --nologo -v q >"$log" 2>&1; then
  echo "dotnet test Game.Sim.Tests упал:" >&2
  tail -n 60 "$log" >&2
  rm -f "$log"
  exit 2
fi
rm -f "$log"
exit 0
