#!/bin/sh
# Builds the rules engine (vendor/ygopro-core) with Duel Circuit's patches (patches/ygopro-core/*.patch) and copies
# libocgcore.dylib to the Unity plugins. Safe to run again: a patch already applied is skipped.
# The patches: DUEL_SECOND_PLAYER_STARTS (player 1 takes the first turn: going second after the coin toss), effect sets
# ordered by creation instead of memory address (so a replay plays out exactly like the duel it recorded), and
# DUEL_SHUFFLE_AFTER_DECK_LOOK (the Deck is shuffled after any look at it).
# `--windows`: also cross-compile ocgcore.dll for 64-bit Windows (brew install mingw-w64; tools/mingw-w64.ini) into
# unity/Assets/Plugins/Windows/x86_64, for ProjectSetup.BuildWindows.
set -e
cd "$(dirname "$0")/.."
root=$(pwd)
cd vendor/ygopro-core
git submodule update --init
for p in "$root"/patches/ygopro-core/*.patch; do
  if git apply --reverse --check "$p" 2>/dev/null; then
    echo "already applied: $(basename "$p")"
  else
    git apply "$p" && echo "applied: $(basename "$p")"
  fi
done
[ -d build ] || meson setup build --buildtype=release -Ddefault_library=shared
ninja -C build
cp build/libocgcore.dylib "$root/unity/Assets/Plugins/macOS/libocgcore.dylib"
echo "copied libocgcore.dylib to unity/Assets/Plugins/macOS (restart the Unity editor if it's open)"

if [ "$1" = "--windows" ]; then
  # Built outside the submodule (build/ is ignored); the DLL links its C++ runtime statically, so it needs only Windows' own.
  win="$root/build/core-win"
  [ -d "$win" ] || meson setup "$win" . --cross-file "$root/tools/mingw-w64.ini" --buildtype=release -Ddefault_library=shared
  ninja -C "$win"
  mkdir -p "$root/unity/Assets/Plugins/Windows/x86_64"
  x86_64-w64-mingw32-strip -o "$root/unity/Assets/Plugins/Windows/x86_64/ocgcore.dll" "$win/libocgcore.dll"
  echo "copied ocgcore.dll to unity/Assets/Plugins/Windows/x86_64"
fi
