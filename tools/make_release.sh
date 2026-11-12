#!/bin/zsh
# Packs a release from builds made at a clean commit (docs/BUILDING.md, "Share a build"):
#   build/release/DuelCircuit-<version>-win64.zip        the Windows game, with a Licenses folder
#   build/release/DuelCircuit-<version>-mac.zip          the Mac app (Apple silicon), with a Licenses folder
#   build/release/ygopro-core-<commit>-patched.tar.gz    the rules engine's full source with our patches (AGPL)
# Build both games first (ProjectSetup.BuildWindows, then BuildMac), from the commit you're releasing. The script
# refuses when the working tree has changes or a build's SOURCE.txt names another commit. Then, for example:
#   gh release create v0.2.1 build/release/* --prerelease --title "Duel Circuit 0.2.1" --notes "..."
set -e
cd "$(dirname "$0")/.."
root=$(pwd)
out="$root/build/release"
win="$root/build/windows"
mac="$root/build/unity/Duel Circuit.app"

if [ -n "$(git status --porcelain --untracked-files=no --ignore-submodules=dirty)" ]; then
  echo "The working tree has changes: commit them, build, then pack." >&2; exit 1
fi
commit=$(git rev-parse HEAD)
version=$(sed -n 's/.*bundleVersion = "\(.*\)";.*/\1/p' unity/Assets/Editor/ProjectSetup.cs)
for source in "$win/Duel Circuit_Data/StreamingAssets/licenses/SOURCE.txt" "$mac/Contents/Resources/Data/StreamingAssets/licenses/SOURCE.txt"; do
  if [ ! -f "$source" ]; then echo "Missing $source: build both games first." >&2; exit 1; fi
  if ! grep -q "Built from commit $commit\$" "$source"; then
    echo "$source wasn't built from $commit (or had local changes): build again." >&2; exit 1
  fi
done

rm -rf "$out"; mkdir -p "$out"
stage=$(mktemp -d)
trap 'rm -rf "$stage"' EXIT

# Windows: the game folder as "Duel Circuit", without the build log, and its license files in plain sight.
mkdir -p "$stage/win"
rsync -a --exclude build.log "$win/" "$stage/win/Duel Circuit/"
cp -R "$win/Duel Circuit_Data/StreamingAssets/licenses" "$stage/win/Duel Circuit/Licenses"
(cd "$stage/win" && zip -qr "$out/DuelCircuit-$version-win64.zip" "Duel Circuit")

# Mac: the app and its license files in a "Duel Circuit" folder (ditto keeps the app bundle intact).
mkdir -p "$stage/mac/Duel Circuit"
ditto "$mac" "$stage/mac/Duel Circuit/Duel Circuit.app"
cp -R "$mac/Contents/Resources/Data/StreamingAssets/licenses" "$stage/mac/Duel Circuit/Licenses"
xattr -cr "$stage/mac"  # no ._ metadata files in the zip
ditto -c -k --norsrc --noextattr --keepParent "$stage/mac/Duel Circuit" "$out/DuelCircuit-$version-mac.zip"

# The patched rules engine: ygopro-core and its Lua at their pinned commits, plus our patches applied.
core="$root/vendor/ygopro-core"
core_commit=$(git -C "$core" rev-parse HEAD)
src="$stage/ygopro-core-$core_commit-patched"
mkdir -p "$src"
git -C "$core" archive HEAD | tar -x -C "$src"
mkdir -p "$src/lua/src"
git -C "$core/lua/src" archive HEAD | tar -x -C "$src/lua/src"
for p in "$root"/patches/ygopro-core/*.patch; do
  (cd "$src" && patch -p1 -s < "$p")
done
cp -R "$root/patches/ygopro-core" "$src/duel-circuit-patches"
cp "$root/tools/build_core.sh" "$root/tools/mingw-w64.ini" "$src/duel-circuit-patches/"
tar -czf "$out/ygopro-core-$core_commit-patched.tar.gz" -C "$stage" "$(basename "$src")"

ls -lh "$out"
echo "Packed release $version from $commit"
