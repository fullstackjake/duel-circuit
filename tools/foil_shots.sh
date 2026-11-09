#!/bin/zsh
# Saves the standard foil screenshots (the before/after set for card-look work) from the built app, under a fixed light so
# two runs give identical pictures. Paths are made absolute (the built app silently writes nothing with relative ones).
#   tools/foil_shots.sh <outdir> [extra app args...]
#   e.g. tools/foil_shots.sh build/foil-review-2026-11-07/baseline
#        tools/foil_shots.sh build/foil-review/after -foilstrength 0.8
# Sheets: all.png (all 26 finishes at detail size), all-board.png (the same with board-size copies), sweep-1..5.png (key
# rarities at 250x364 as a hovered light crosses them), slabs.png (Full, Reveal, Compact), slabs-1..5.png (the same as the
# light crosses them), slabs-labels.png (every label at every size; the label fit check: the script fails on a FAIL),
# slabs-context.png (slabs and grid rims on their grounds), reveal/ and reveal-calm/ (the desk's slab reveal frame by
# frame, normal and under -reducedmotion; its two frames at rest must be identical) and grades/ (the grade showcase, and
# grades-sweep-1..5.png as a hovered light crosses it), tilt-1..5.png (Secret, Starlight, Ghost, Ultimate and Gold at
# about the reading view's size, held at five tilts under a hovered light).
# FOIL_APP=<path to the app's binary> uses another build.
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APP="${FOIL_APP:-$ROOT/build/unity/Duel Circuit.app/Contents/MacOS/Duel Circuit}"
[ $# -ge 1 ] || { echo "usage: tools/foil_shots.sh <outdir> [extra app args...]"; exit 2; }
[ -x "$APP" ] || { echo "no app at $APP (build it first)"; exit 1; }
mkdir -p "$1"
OUT="$(cd "$1" && pwd)"
shift
EXTRA=("$@")

# One run: waits for the app to quit by itself (killed after three minutes if it hangs).
shot() {
  local name="$1"; shift
  echo "foil_shots: $name"
  "$APP" "$@" "${EXTRA[@]}" -logFile "$OUT/$name.log" -mute -screen-width 1920 -screen-height 1080 >/dev/null 2>&1 &
  local pid=$!
  ( for i in {1..180}; do sleep 1; kill -0 $pid 2>/dev/null || exit 0; done
    kill $pid 2>/dev/null && echo "foil_shots: $name timed out" ) &
  local watcher=$!
  wait $pid
  wait $watcher
}

KEY=SuperRare,UltraRare,SecretRare,StarlightRare,GhostRare,UltimateRare,CollectorsRare,GoldRare,PlatinumRare,QuarterCenturySecretRare
shot all -foilshowcase "$OUT/all.png" -foilall
shot all-board -foilshowcase "$OUT/all-board.png" -foilall -foilboard
shot sweep -foilshowcase "$OUT/sweep.png" -foilrarities $KEY -foilsweep 5
shot slabs -foilshowcase "$OUT/slabs.png" -foilslabs
shot slabs-sweep -foilshowcase "$OUT/slabs.png" -foilslabs -foilsweep 5
shot slabs-labels -foilshowcase "$OUT/slabs-labels.png" -foilslabs labels
shot slabs-context -foilshowcase "$OUT/slabs-context.png" -foilslabs context
shot reveal -slabreveal "$OUT/reveal"
shot reveal-calm -slabreveal "$OUT/reveal-calm" -reducedmotion
shot tilt -foilshowcase "$OUT/tilt.png" -foilrarities SecretRare,StarlightRare,GhostRare,UltimateRare,GoldRare -foiltilt sweep -foillight 0.5,0.6,1
shot grades -gradeshowcase "$OUT/grades"
shot grades-sweep -gradeshowcase "$OUT/grades" -gradesweep 5

missing=0
for f in all.png all-board.png sweep-{1..5}.png slabs.png slabs-{1..5}.png slabs-labels.png slabs-context.png \
    reveal/slab-reveal-{0150,0600,0750,1750,2600,rest-1,rest-2}.png reveal-calm/slab-reveal-{0150,0600,0750,1750,2600,rest-1,rest-2}.png \
    tilt-{1..5}.png grades/grades.png grades/grades-label-only.png grades/grades-sweep-{1..5}.png; do
  [ -s "$OUT/$f" ] || { echo "foil_shots: missing $OUT/$f"; missing=1; }
done
# The label fit check: every label text fits its row at every size.
if ! grep -q "slabfit: all fit" "$OUT/slabs-labels.log"; then
  grep "slabfit:.*FAIL" "$OUT/slabs-labels.log"; echo "foil_shots: the slab label fit check failed"; missing=1
fi
# At rest the reveal stops moving (and, calm, never moved): its two frames a second apart are identical.
for d in reveal reveal-calm; do
  cmp -s "$OUT/$d/slab-reveal-rest-1.png" "$OUT/$d/slab-reveal-rest-2.png" || { echo "foil_shots: $d's frames at rest differ"; missing=1; }
done
[ $missing = 0 ] && echo "foil_shots: saved to $OUT"
exit $missing
