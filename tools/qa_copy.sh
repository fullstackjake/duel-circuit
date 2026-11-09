#!/bin/zsh
# Copies a built app for checks and gives the copy the check builds' own bundle id (com.ygocampaign.game.qa, as ProjectSetup
# gives -buildout builds), re-signed ad hoc, so its runs keep their settings in their own preferences domain and never write
# the player's (com.ygocampaign.game).
#   tools/qa_copy.sh [<from .app>] <to folder>
#   e.g. tools/qa_copy.sh build/qa-quick        (copies build/unity/Duel Circuit.app into build/qa-quick)
# An app already in place (no <from>) is only re-identified: tools/qa_copy.sh --in-place "build/qa-quick/Duel Circuit.app"
set -e
cd "$(dirname "$0")/.."
QA_ID=com.ygocampaign.game.qa
if [ "$1" = "--in-place" ]; then
  APP="$2"
else
  if [ $# -ge 2 ]; then FROM="$1"; TO="$2"; else FROM="build/unity/Duel Circuit.app"; TO="$1"; fi
  [ -d "$FROM" ] || { echo "no app at $FROM"; exit 1; }
  mkdir -p "$TO"
  APP="$TO/$(basename "$FROM")"
  rm -rf "$APP"
  cp -R "$FROM" "$APP"
fi
PLIST="$APP/Contents/Info.plist"
if [ "$(/usr/libexec/PlistBuddy -c 'Print CFBundleIdentifier' "$PLIST")" != "$QA_ID" ]; then
  /usr/libexec/PlistBuddy -c "Set CFBundleIdentifier $QA_ID" "$PLIST"
  codesign --force --deep -s - "$APP"
fi
echo "$APP: $(/usr/libexec/PlistBuddy -c 'Print CFBundleIdentifier' "$PLIST")"
