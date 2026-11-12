# Public README: plan (2026-11-08)

A new front page for going public: what Duel Circuit is, features, install and play, known issues, that it's free to
use and copy and everyone is welcome, credits, license and disclaimer. Groundwork came from three reports
(features and doc split, known issues, licensing); their findings are folded in below. None of this is legal advice.

## 0. Easy to read (owner, 2026-11-08)

Who reads what, and how each is written:

- **README.md, docs/PLAYING.md, docs/CAMPAIGN.md, docs/CARD-PICTURES.md: players.** Many are casual Yu-Gi-Oh! fans,
  some young, some reading English as a second language. Write for them:
  - Plain words and short sentences. "You", not "the player". Say what to click, in order.
  - No developer words: no "harness", "submodule", "StreamingAssets", "AGPL section 7", "build target", "seed",
    "headliner gate". If a technical name is unavoidable (a folder path), show it in a code block they can copy.
  - Yu-Gi-Oh! terms are fine (Chain, Extra Deck, ban list): the audience knows them.
  - Steps as numbered lists, one action each. Choices as short tables. No paragraph longer than about four lines.
  - Lead with what they want: what the game is, how to get it running, what to do first. Background comes later.
  - Licenses and the disclaimer in one plain paragraph each ("You can play, copy, share and change this game for free.
    If you share a changed version, share its source too."), with the legal text linked, not pasted.
- **CONTRIBUTING.md, docs/BUILDING.md, docs/MODDING.md: people who want to help or tinker.** Some have never built a
  game: every step is a command to copy, with one line on what it does and what you should see.
- **docs/DEVELOPING.md and docs/dev/: developers.** Technical language is fine, still clear and short.
- **Checked before it's done:** an independent readability review reads the player pages as a newcomer would and flags
  any sentence that's hard to follow or uses a word they won't know; the fixes go in before anything is committed.

## 1. What changes in this pass

**README.md** becomes a page a newcomer reads in about five minutes:

1. Title "Duel Circuit" (not "YgoCampaign"), the logo (`art/logo/logo.png`), a one-line tagline ("A solo Yu-Gi-Oh!
   campaign: from the corner card shop to the Tier 0 Gauntlet"), and badges (release, license, platforms).
2. Two to four screenshots with **no card art** in them (section 3).
3. **What is this?** Free, fan-made, single-player, offline; climbs 22 circuits of CPU duelists playing real decks from
   2004 to 2026; runs on EDOPro's rules engine; ships no card art.
4. **Features** in short groups with real counts: 22 circuits in 7 bands, 227 story scenes, 111 original duelists,
   7 Tag Cups, the Format Library, Back Alley, brackets and Champion's Ladder after the Gauntlet, 74 historic tournament
   decks, 35 historic ban lists, CPU duelists with 47 deck playbooks, the deck editor and its new search, packs from
   real sets, every copy with its own rarity, grade and wear, 26 foil finishes, code-made music, controllers,
   accessibility settings, replays. Things that are locked ("Coming soon": No Limits) aren't listed as features.
5. **System requirements:** Windows 10/11 64-bit or macOS on Apple silicon (no Intel Mac or Linux yet), about 200 MB,
   no internet, controller optional; frame rate on slow machines not yet measured.
6. **Install and play** (today's Getting started "Play" part, made public-ready), **Card pictures**, **Your save**.
7. **How to play**, quickstart in five lines plus a controls table, linking to the player guide.
8. **Known issues**: the verified list (below), with platform limits first.
9. **FAQ**: no card pictures? official? need EDOPro? online play? EDOPro decks work? Intel Mac or Linux? "app can't be
   opened"/SmartScreen? where's my save? controller? too hard or too easy? modding? found a rules bug?
10. **Everyone's welcome**: free to play, copy, share and change; how to report a bug (platform, version, `Player.log`,
    the replay code: replays are deterministic, so a code reproduces it); how to contribute; link to CONTRIBUTING.md.
11. **Build from source**: three lines and a link to docs/BUILDING.md.
12. **Credits**: ygopro-core (edo9300 and contributors), Project Ignis (CardScripts, BabelCDB, LFLists), YGOPRODeck
    (card-set data), Format Library (historic decks and ban lists) and the deck builders in decks/history/SOURCES.md,
    Unity, Lua, meson/ninja/mingw-w64/.NET; the art's origin (AI-generated portraits and mats, the owner's logo);
    inspirations (Tag Force, Master Duel, Link Evolution, Duel Links) named as inspiration only.
13. **License** and **Disclaimer** (decisions 1–3).

**Known issues to list** (verified open as of 3a358a3):
- Platform: only a Windows test build is published (untested on a PC); no Mac download yet; Apple silicon only;
  unsigned (Gatekeeper/SmartScreen); the .ydk file dialog and the window/click fixes are Mac-only.
- Content: No Limits is locked; two Back Alley headliners are no-go; difficulty isn't tuned (early circuits are easy);
  every duel uses Master Rule 5 (historic formats get their ban lists, not their era's rules).
- Bugs: the Warrior's Charge starter is illegal at the Back Alley; three portraits are cut off on the right and seven
  renamed duelists still wear old portraits, and a few have none; rare deck-shuffle edge cases; a handful of known CPU
  misplays; a 2.5 s stall the first time the editor opens uncached HD pictures; some foil/grade looks weak at board size.
- Replays from a different card database may play out differently (the game warns).
- Don't open a save in an older version of the game (unknown lines are dropped).

**New docs** (the developer manual moves out of the README; text kept, the stale counts fixed):

| File | From today's README |
|---|---|
| docs/PLAYING.md | Playing a duel, Tag duels (rules), Deck editor, Game controllers, Animation, Rarity and foils, Replays (player parts) |
| docs/CAMPAIGN.md | Campaign, Circuit map, Recipe Book, Story, the Library, Back Alley, brackets and Ladder (player view) |
| docs/CARD-PICTURES.md | Card art (pictures, EDOPro folders, HD folders, styles, card back, cache paths for Mac and Windows) |
| docs/MODDING.md | Characters (profile format), Decks, Character portraits, story/map/mat overrides, extra ban lists |
| docs/BUILDING.md | Build from a fresh clone, Build, Unity game (build parts), Windows build, Updating to the newest cards |
| docs/DEVELOPING.md | Layout (rewritten), Bots, Planning ahead, Sound, historic-deck playbooks and ratings, the harness, app checks |

Stale lines fixed on the way: "twenty circuits" (22), "Six cups" (7), "63 decks" (74), "29 effects / 13 themes"
(36 / 17), the menu list (Tag Duel), the "spectator mode" framing, "Test decks", the controller table, Mac-only cache
paths. A `docs/README.md` index splits the guides from the dated working docs.

**New files:** LICENSE (decision 1), a separate license for art and story (decision 2), THIRD_PARTY_NOTICES.md (each
component, its license, link, pinned commit, and what we changed: the three core patches), CONTRIBUTING.md (how to
report and contribute, the content rules: no card art, no Konami assets, original characters, no real people's names
or likenesses; CPUs stay at full strength), CODE_OF_CONDUCT.md (Contributor Covenant), and issue templates (a bug
report asking for platform, version, `Player.log` and the replay code; a rule against attaching card images).

## 2. Before flipping the repo public

Status after the go-public prep (2026-11-08):

1. [x] **Card-art screenshots in git history**: done by a fresh repo. The old repo is now the private
   `fullstackjake/duel-circuit-archive` (full history, release v0.1.0); `fullstackjake/duel-circuit` starts from one
   commit of the cleaned tree, signed with the owner's GitHub private email.
2. [x] **Release**: v0.1.1 (pre-release) on the new repo, built from a commit by `tools/make_release.sh`: Windows and
   Mac zips with a Licenses folder (SOURCE.txt names the commit) and the patched ygopro-core source tarball.
3. [x] **About / Licenses screen** in the game (main menu → About), `-aboutcheck`.
4. [~] **Ask edo9300 and Project Ignis** (draft: docs/dev/PERMISSION-REQUEST-DRAFT.md). edo9300 answered
   (2026-11-08). Project Ignis: ticket opened 2026-11-08, no reply as of 2026-11-12; THIRD_PARTY_NOTICES.md says so.
5. [~] **Portrait review**: Kelda Amin's portrait removed (stand-in until new art, docs/dev/TODO.md). The other art
   files haven't been checked one by one for likeness.
6. [x] **Internal notes** trimmed (home path, the owner's picture folder); the story bible is marked as spoilers.
7. [~] **GitHub settings**: description, topics, issues with forms, wiki and projects off, vulnerability alerts on.
   Private vulnerability reporting, secret scanning with push protection and branch protection on `main` need a
   public repo on the free plan: turn them on right after flipping it. The owner's account setting "Block command line
   pushes that expose my email" is the owner's to switch on (github.com → Settings → Emails).
8. [ ] **Try the Windows build on a real PC** before announcing it.

## 3. Screenshots

The existing check screenshots were taken with real card pictures installed, so most can't be used. New ones are
taken from the app checks with a fresh `-mods` folder and `-hdpics off` (cards show their text look): the circuit
map, a duel board mid-chain, a pack opening, and the story screen. Saved in docs/images/ and checked by eye for any
card art before they're committed.

## Owner decisions (recommendation first)

**Decided (owner, 2026-11-08): accept all seven recommendations.**

1. **License for the code.** *Recommend AGPL-3.0-or-later for all of our code, with an added permission (AGPL
   section 7) to combine it with the Unity runtime.* It matches the engine and scripts we ship (a build that contains
   the AGPL core is under the AGPL anyway), it says exactly "free to use, copy, change and share", and forks stay open.
   The added permission must go in before anyone else contributes. Alternative: MIT for our code (people can reuse
   pieces anywhere, but shipped builds are still AGPL, so the README has to explain two layers). "Non-commercial"
   can't be part of an open license; the README says it as a request, and Konami's rights are what stop sales.
2. **License for the art, story and characters.** *Recommend CC BY-SA 4.0* (share and adapt with credit, changes stay
   open; compatible with GPLv3), *with the name "Duel Circuit" and the logo kept out of it* so forks can't pose as the
   game. Alternatives: CC BY 4.0 (no share-alike), or the logo under the same license as everything else.
3. **The Unity-plus-AGPL-core question.** Strictly read, shipping the AGPL core inside a Unity build needs the core
   authors' permission. *Recommend writing to edo9300 (ygopro-core) and Project Ignis* to ask, keeping the public
   repo and builds as they are meanwhile, and recording the answer in THIRD_PARTY_NOTICES.md. Alternative: ship as is
   and accept the gray area, as many fan projects do.
4. **How much leaves the README.** *Recommend the six guides above* plus moving the ~80 dated specs, QA reports and
   handoffs into docs/dev/ (links updated). Alternative: the six guides only, dated docs left where they are.
5. **Card-picture download instructions.** The README has a copy-paste command that downloads every card picture
   and links a third-party HD downloader: the most visible "helps copy Konami's art" item. *Recommend keeping
   bring-your-own pictures and EDOPro folder support in the player docs, moving the `--download-pictures` command to
   docs/DEVELOPING.md with a "personal use only, never share or commit them" note, and dropping the HD downloader
   link.* Alternative: keep the instructions as they are.
6. **Real players' names in the shipped historic decks.** The 74 decks' `#source` lines name the players who built
   them, and the deck editor shows that line, against your rule that real names never appear in the game (credit
   lives in SOURCES.md). *Recommend stripping the names from the .ydk files in this pass* (the editor then shows the
   event and placing only). Alternative: keep them as deliberate credit and change the rule.
7. **Contact for rights holders.** *Recommend GitHub issues only* ("open an issue and we'll act promptly").
   Alternative: also a dedicated email address you create for the project.
