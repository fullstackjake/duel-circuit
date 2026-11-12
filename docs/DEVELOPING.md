# Developing Duel Circuit

How the code is laid out, how a duel runs, how the CPU duelists think, and the tools that test and measure all of it.
Set up first with [BUILDING.md](BUILDING.md); for decks, characters and art, see [MODDING.md](MODDING.md).

- [Repository layout](#repository-layout)
- [Architecture](#architecture)
- [The CPU duelists](#the-cpu-duelists)
- [The harness](#the-harness)
- [App checks](#app-checks)
- [Sound and music](#sound-and-music)
- [Historic decks](#historic-decks)
- [Replays](#replays)
- [Rarity and foil showcase](#rarity-and-foil-showcase)
- [Downloading card pictures](#downloading-card-pictures)
- [Before you send a change](#before-you-send-a-change)
- [Work notes](#work-notes)

In the commands below, `harness` stands for the built harness. Either type the full command or set an alias once per
terminal:

```bash
alias harness='dotnet build/bin/Ygo.Harness/Release/net7.0/Ygo.Harness.dll'
```

## Repository layout

| Path | What it is |
|---|---|
| `src/Ygo.Engine/` | The C# library: native bindings, prompt parsing, the duel loop, the CPU duelists, the campaign rules, sound. Targets netstandard2.1 (C# 9) so Unity can use it as a local package |
| `src/Ygo.Harness/` | Console app (net7.0): CPU-vs-CPU runs, exports, tests and studies. Reads the card databases directly (SQLite) |
| `unity/` | The Unity 6 project (6000.3.27f1). Scripts in `Assets/Scripts/`, builds and editor menu in `Assets/Editor/ProjectSetup.cs`, the one scene `Assets/Scenes/Main.unity`, foil and slab shaders in `Assets/Resources/`, the rules engine binaries in `Assets/Plugins/`, generated card data in `Assets/StreamingAssets/` (not committed) |
| `vendor/ygopro-core/` | Submodule: the C++ rules engine ([edo9300/ygopro-core](https://github.com/edo9300/ygopro-core)), patched at build time. Built in `vendor/ygopro-core/build/` and copied to `unity/Assets/Plugins/macOS/` |
| `vendor/CardScripts/` | Submodule: Project Ignis's Lua script for every card's effects |
| `vendor/BabelCDB/` | Submodule: Project Ignis's card databases (SQLite: stats and text) |
| `vendor/LFLists/` | Submodule: Project Ignis's Forbidden/Limited lists (the ones EDOPro uses) |
| `patches/ygopro-core/` | Our three patches to the rules engine, applied by `tools/build_core.sh` |
| `personalities/` | One `.profile` per character: name, deck pool, dialogue, play-style knobs ([MODDING.md](MODDING.md#characters)) |
| `decks/` | Built-in decks: the starter decks and other built-in decks directly in `decks/`; `history/` (74 historic tournament decks and `SOURCES.md`), `structure/` (the shop's structure decks), `alley/` and `headliners/` (Back Alley tables), `no-limits/` (the locked No Limits table), `recipes/` (card-name recipes for `tools/build_deck.py`) |
| `campaign/` | The campaign as data: circuits, story, guests, shop lines, the Library, the Back Alley, the Ladder, mats, card text timeline, deck ratings, measured advice. Each file explains its format at the top |
| `lflists/` | `history.lflist.conf`: the 41 historic TCG lists, April 2004 to October 2026 |
| `art/` | Shipped art: `portraits/` (duelists and alley regulars), `mats/` (boss duel mats), `map/` (circuit map regions), `coin/` (coin toss faces), `logo/` |
| `tools/` | Scripts: `build_core.sh` (rules engine), deck and ban-list fetchers and checkers, `build_deck.py`, foil screenshot tools, `qa_copy.sh` |
| `docs/` | The guides ([index](README.md)); `docs/dev/` holds dated specs, QA reports and handoffs |
| `Directory.Build.props` | Sends every `dotnet build` output to `build/` (stray `bin/obj` under `src/` would break Unity's import) |
| `build/` | Not committed: harness builds, app builds, check output |
| `data/` | Not committed: YGOPRODeck's card-set data (`data/ygoprodeck/`) |
| `mods/` | Not committed: the editor's mods folder (save, decks, settings, your card pictures) |
| `rules/` | Not committed: a local copy of Konami's rulebook ([docs/dev/RULES.md](dev/RULES.md)) |

## Architecture

```
Unity front end (unity/Assets/Scripts)        Harness (src/Ygo.Harness)
            \                                   /
             Ygo.Engine (src/Ygo.Engine, C#, netstandard2.1)
                          |  P/Invoke (Native/OcgApi.cs)
             ygopro-core (C++, libocgcore.dylib / ocgcore.dll)
                          |  runs
             CardScripts (Lua)  +  card data (cards.bin or BabelCDB)
```

**The engine as a package.** `src/Ygo.Engine` is a Unity local package (`com.ygocampaign.engine`, listed in
`unity/Packages/manifest.json` as `file:../../src/Ygo.Engine`) and a normal project reference for the harness. The game
and the harness run the same duel code.

**The rules engine.** `Native/OcgApi.cs` binds ygopro-core's C API with `DllImport("ocgcore")`, which resolves to
`libocgcore.dylib` on a Mac and `ocgcore.dll` on Windows. The core is built with three patches
(`patches/ygopro-core/`):

1. `DUEL_SECOND_PLAYER_STARTS`: the second player can take the first turn (the coin toss; upstream always starts with
   player 0).
2. Effect sets ordered by creation instead of memory address, so a replay plays out exactly like the duel it recorded.
3. `DUEL_SHUFFLE_AFTER_DECK_LOOK`: the Deck is shuffled after any effect looks through it.

**How a duel runs.**

1. A `DuelRecipe` holds everything that defines a duel: the card data, the scripts, `DuelSettings` (seed, flags, who
   goes first) and both decks (four in a tag duel). The same recipe always makes the same duel.
2. `Duel` (`Duel.cs`) creates the duel in the core, adds the decks, and answers the core's callbacks: card data (from
   `cards.bin` in the game, from BabelCDB in the harness) and scripts (`FileScriptProvider`).
3. `DuelRunner` steps the core. Every message goes to listeners (the board, the log, the animation events in
   `Presentation/`, the sound). When the core asks a question, the runner hands the prompt (`Protocol/`) to that seat's
   `IDuelist`.
4. An `IDuelist` answers with response bytes: `SmartDuelist` and `RandomDuelist` (CPUs), `HumanDuelist` (you, through
   `Interactive/PromptPresenter`, which turns prompts into the choices the board shows), or a replay feeding back
   recorded answers. A human returns null until you've clicked; the runner asks again next frame.
5. Every response goes into a `ResponseLog`. Recipe plus responses reproduce the duel exactly: that's a replay, and it's
   how the turn planner tries out a line in a scratch copy.

**Tag duels.** Each team has a second core duelist whose Deck, hand and Extra Deck swap in at the start of the team's
turns (`MSG_TAG_SWAP`). `TagSeat` hands each prompt to whoever is in play; `TagDuel` seats them so the order is lead,
lead, partner, partner.

**The Unity side.** `DuelController` (a partial class over many `DuelController.*.cs` files) is the composition root:
it loads data, wires the screens and runs duels. `BoardView` draws the duel; the campaign, shop, editor and other screens
are their own components. The built game reads cards, scripts, decks, characters and campaign files from
`StreamingAssets` (copied there by `ProjectSetup`); the editor reads them straight from the repository, so a change to
`campaign/` or `personalities/` shows on the next Play.

**Developer mode.** The editor, a development build, `-dev`, or any `-<name>check` flag turns it on: **F12** then shows
the developer panel (matchup picker, decision list, log, test buttons). A normal build keeps these tools locked.

## The CPU duelists

**The bots.**

- `RandomDuelist` picks any legal answer. It stress-tests the engine and is the fallback when a smarter answer is
  rejected.
- `SmartDuelist` is a heuristic player for any deck: draws and searches first, removal when it helps, summons and Extra
  Deck plays that improve the board, attacks it can win, hand traps and traps saved for the opponent's plays. It reads
  card roles from card text (`CardTraits`, with per-card overrides) and only uses information a real player would see.

**Characters.** A character's `.profile` sets five 0-1 knobs that shift the smart bot's thresholds
([MODDING.md](MODDING.md#characters) has the table). `skill` sets how well they play: at 0.5 or more they use their
deck's playbook (0.7 with `YGO_RULES_SENSE=0`), at 0.9 or more they also plan ahead (`Personality.KnowsDeck`,
`Personality.PlansAhead`).

**Planning ahead.** `TurnPlanner` gives the bot one-step lookahead in Main Phase 1 and the Battle Phase. For each
option, it replays the duel in a scratch copy (same recipe plus every answer so far, from the `ResponseLog`), takes
that option, lets the generic bot finish the turn, and scores the board. It overrides the usual play only when another
option is clearly better, which finds combo lines for any deck without hand-written rules. It takes about 0.1 s per
decision.

Risk comes from public information only:

- **Interruptions:** each line is also played with its first interruptible step (monster effect, search, summon from
  the Deck) negated, and the results are blended by how likely an interruption is: more cards in the opponent's hand
  means more risk, and much more once they've shown a hand trap or negation this duel.
- **Battle traps:** each attack is charged for the chance a set card stops it and destroys the attacker.
- **Public responses:** in rollouts the opponent answers with the cards everyone can see (face-up on the field, in the
  Graveyard, banished), played by the generic bot. Their hand and set cards stay hidden and are covered by the two
  estimates above, so the planner never peeks. (Draw effects inside a rollout draw the real next cards: a slight peek.)

The planner also plans one-card selections in its own Main Phase 1 (which card to search, summon, discard or target),
overriding the usual pick when that line scores 150 points better. Planning multi-card choices (materials one at a
time, level sums, tributes) measured even, so it's off by default (`PlanMultiSelections`). A beam search over
sequences of up to four Main Phase actions (`TurnPlanner.ComboSearch`) is off by default too: it measured even with the
one-step planner at twice the thinking time. Each was measured over 1,000 or so games when it was added; the harness
bots below turn each one on or off for comparison.

**Playbooks** (`src/Ygo.Engine/AI/Playbooks/`: `Playbooks.Lookup` maps 50 deck names to 46 playbook classes) add deck
knowledge on top: priority rules, which cards to pick for materials and searches, and vetoes. A playbook is kept only if
it beats the generic bot in head-to-head runs; one for PePe made that deck worse and one for Floowandereeze came out
even, so they were left out. Frog FTK has one (`FrogFtkPlaybook`), which the Back Alley headliner Mass Driver Moss
plays through.
`Playbooks.SecondDecks` lists the decks that would rather go second, and why; the coin toss and the CPU's choice read
it. Set `YGO_DEBUG_PLAYBOOK=1` to log what a playbook could do when no rule fired.

**Rules sense.** On every path to a decision (the normal choice, the planner, a playbook, a beginner's sloppy pick, a
chain), the CPU vetoes plays no competent duelist makes (`SmartDuelist.Sense.cs`; `YGO_RULES_SENSE=0` turns it off):
summoning a card that burns it out first, paying LP or Deck it can't spare, negating its own card or summon, and
spending a support effect with nothing of its own to support. Facing a taxing negator (`SmartDuelist.Taxers.cs`: Light
and Darkness Dragon, Apollousa), it counts the negates left and never pays a second card or real LP into a certain
negate, unless it's the last one or wearing it down puts it under an attacker that turn (`YGO_TAX_SENSE=0` turns this
off).

Smaller generic rules found while rating decks: it makes Extra Deck monsters worth having for their effects, not
their ATK (except Links, where that hurt); it summons "?"-ATK Xyz in Attack Position; and it stops chaining the same
effect after three times in a turn, so a free effect that keeps getting negated can't freeze the duel.

**Deck ratings.** `campaign/deck-ratings.txt` is the strength ranking the campaign's bands come from: the saved output
of `--rate-decks`, which plays every deck against random opponents with the same balanced bot on both sides and computes
an Elo rating. That measures each deck as the bot plays it, so combo decks that need precise lines rate lower than they
did in their day; playbooks close that gap. `--skilled` plays both sides at skill 1.0 with the planner (how the late
campaign plays, about 80 seconds a round).

**The owner's rule:** CPUs always play their decks at full strength. Difficulty is tuned by which deck a CPU plays,
never by weakening a CPU; ban lists bind only your side.

## The harness

The harness plays CPU-vs-CPU duels without Unity, and holds the exports, tests and studies.

```bash
harness
```

With no options it plays one duel between `decks/classic.ydk` and `decks/modern.ydk` with a full play-by-play. More
than one game (`--games N`) runs quietly and prints totals. Pass two `.ydk` paths to pick the decks.

**Bots** for `--p0` and `--p1`:

| Bot | What it is |
|---|---|
| `random` | Any legal answer |
| `smart` | The generic heuristic bot (balanced personality); the default |
| `playbook` | `smart` plus the deck's playbook |
| `expert` | `smart` plus the playbook and the turn planner |
| `expert-noopp` | `expert` without public opponent responses in rollouts |
| `expert-nopick` | `expert` without one-card selection planning |
| `expert-multi` | `expert` with multi-card selection planning |
| `expert-combo` | `expert` with the combo search |
| a character id | That character, e.g. `vera` (their personality, playbook and planner by skill) |

**Flags.** Every flag the guides mention; `src/Ygo.Harness/Program.cs` has the rest, each with a comment.

| Flag | What it does |
|---|---|
| `--seed N` | Seed of the first duel (later duels add one) |
| `--games N` | Number of duels; more than one prints totals instead of a play-by-play |
| `--p0 BOT`, `--p1 BOT` | The bot in each seat (table above) |
| `--phases` | Prints phase changes in the play-by-play |
| `<deck0.ydk> <deck1.ydk>` | The two decks |
| `--matrix` | Every bot pairing in both seats, with win rates: checks that a bot change is an improvement |
| `--decks` | Every deck at the top of `decks/` against every other (balanced bots, seats alternate) |
| `--styles` | Every character against the balanced bot: win rate, attacks per turn, sets, disruption, Extra Deck summons, game length |
| `--goldfish` | The first deck going first against a dummy that only sets monsters, generic bot against `expert` |
| `--ui-test` | The human seat played with random clicks through the same choice lists the game shows; reports every prompt type and rejected answers. `--responses auto\|on\|off` picks the Chain prompt mode |
| `--deckbuilding-test` | The deck editor's engine pieces: search, ban lists, validation, odds, codes, the tester; historic decks legal under their lists |
| `--campaign-test` | The shop, pack odds, circuit payouts and unlocks, the copy cap, collection rules, saving, the story's triggers and speakers |
| `--import-test` | The card picture importer (Import Cards) on stand-in files in a temp folder: names, zips, existing pictures with both answers, size limits, unsafe names, cancel, the card back. Uses no card pictures |
| `--copies-test` | Each card keeps its copy's look through a duel; old replays still deal the same |
| `--profile-check <save>` | Loads a save (migrating an old one), reports its copies by grade; only reads the file |
| `--replay-test` | Bot duels round-tripped through replay codes, every core message compared |
| `--tag-test` | Tag duels with simple, playbook and planning bots: turn order, swaps, all four play, a tag replay round trip |
| `--sense-test` | The CPU's rules sense on set-up boards |
| `--first-turn-test` | The going-second core patch and the tag turn order |
| `--negator-test` | Play into a taxing negator (with `YGO_TAX_SENSE=0` for the CPU without that rule) |
| `--lesson <id> <deck> <rival id> <rival deck>` | Makes a Replay Lesson and prints its key turn |
| `--rate-decks [--rounds N] [--skilled]` | Elo rating of every deck (see [Deck ratings](#the-cpu-duelists)) |
| `--playbook-test [--from C] [--pilot casual]` | Every late boss match with and without the boss's playbook, against the campaign player |
| `--campaign-sim --starter <deck> --pilot casual\|skilled [--seed N]` | Plays the whole campaign as a player would: retries losses, buys packs, trades tokens, rebuilds its deck; prints every duel and a summary per circuit |
| `--circuit-study [--write <file>] [--circuits <file>]` | Measures decks at circuit matches (how `campaign/toss-advice.txt` and `campaign/library-advice.txt` are made) |
| `--library-check`, `--library-plan`, `--library-study [--games N]`, `--library-summary` | The Format Library's cups: decks legal as loaners, which to measure, the measurement, the best option per match |
| `--alley-gen [--only <table>] [--splashes K]` | Each Back Alley event's pool and K splash decks |
| `--alley-rate [--games N]` | Each splash deck against its base deck |
| `--endless-test [--games N]` | Prompt-cap stops, duels past turn 40 and draws |
| `--bracket-sim`, `--bracket-calibrate [--games N]` | Bracket title odds by rating, and those odds checked against real duels |
| `--export-cards <out.bin>` | Writes `cards.bin` ([BUILDING.md](BUILDING.md#5-export-the-card-data)) |
| `--export-printings <cardinfo.json> <out.bin>` | Writes `printings.bin` ([BUILDING.md](BUILDING.md#7-export-the-printings)) |
| `--export-sounds <folder>` | Every sound as a WAV, and the music themes ([Sound](#sound-and-music)) |
| `--sfx-wav <folder> [prefix]` | Only the effects whose names start with the prefix |
| `--boss-music` | Every boss duel with its theme and key, each rendered |
| `--download-pictures <cardinfo.json> <folder> [--limit N]` | See [Downloading card pictures](#downloading-card-pictures) |

Environment variables: `YGO_EVENTS=1` adds the animation events the game would play to a play-by-play;
`YGO_DEBUG_PLAYBOOK=1`, `YGO_RULES_SENSE=0` and `YGO_TAX_SENSE=0` are described under
[The CPU duelists](#the-cpu-duelists).

**Long runs.** Studies can take hours. Run them from a copy of the harness, so rebuilding meanwhile can't break them:

```bash
cp -R build/bin/Ygo.Harness/Release/net7.0 build/bin/snap-mystudy && dotnet build/bin/snap-mystudy/Ygo.Harness.dll --rate-decks --rounds 200
```

Several harness builds can work side by side if each builds into its own folder:
`dotnet build src/Ygo.Harness -c Release -o build/bin/<name>`.

**Measuring the post-game.** The Format Library, the Back Alley and brackets were measured in the harness before they
were built ([docs/dev/POST-GAME-PLAN-2026-10-31.md](dev/POST-GAME-PLAN-2026-10-31.md), step 2 and "Targets"). The
Library's 12 cups are in `campaign/library.txt`; the Back Alley's events, list, contraband pools and splash builder are
in `src/Ygo.Engine/Campaign/Alley.cs`. The `--library-*`, `--alley-*` and `--bracket-*` flags above are those
measurements.

## App checks

The built app runs automated checks of its own screens. Each check plays through a part of the game, saves
screenshots, logs a line per step (`<name> check: ok ...` or `<name> check: FAIL ...`) and quits.

Three rules:

1. **Absolute paths.** The Mac app silently writes nothing with a relative `-mods`, `-logFile` or check folder.
2. **A fresh `-mods` folder** for each run, so a check starts from a new save and never touches yours.
3. **A check build.** Build a separate app with `-buildout`, so its runs keep their settings apart from the game you
   play (a `-buildout` or `-qa` build gets its own bundle id, `com.ygocampaign.game.qa`).

Build a check app (the path is from the repository root):

```bash
/Applications/Unity/Hub/Editor/6000.3.27f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath unity -buildTarget OSXUniversal -executeMethod ProjectSetup.BuildMac -buildout "build/qa/Duel Circuit.app" -logFile build/qa/build.log
```

Or copy an existing build and give it the check bundle id: `tools/qa_copy.sh build/qa`.

Run one check:

```bash
APP="$PWD/build/qa/Duel Circuit.app/Contents/MacOS/Duel Circuit"; OUT="$PWD/build/qa/checks/campaign"; mkdir -p "$OUT/mods" "$OUT/out" && "$APP" -mods "$OUT/mods" -campaigncheck "$OUT/out" -mute -logFile "$OUT/log.txt"
```

Read the result:

```bash
grep -c "campaign check: ok" "$OUT/log.txt"; grep "check: FAIL" "$OUT/log.txt"
```

The first number is the steps that passed; any `FAIL` line names what broke.

Run several in a row, each in its own fresh folder, with a summary line each:

```bash
APP="$PWD/build/qa/Duel Circuit.app/Contents/MacOS/Duel Circuit"; for c in campaign shop layout; do OUT="$PWD/build/qa/checks/$c"; rm -rf "$OUT"; mkdir -p "$OUT/mods" "$OUT/out"; "$APP" -mods "$OUT/mods" -${c}check "$OUT/out" -mute -logFile "$OUT/log.txt"; echo "$c: $(grep -c "$c check: ok" "$OUT/log.txt") ok, $(grep -c "$c check: FAIL" "$OUT/log.txt") FAIL"; done
```

A check that stops on an exception logs `check: FAIL the check stopped on an exception` and quits.

A check run answers only the test devices it adds: the computer's own mouse and keyboard are switched off for the
run, so working beside it can't press anything in the check. Its window still comes to the front.

| Check | What it covers |
|---|---|
| `-campaigncheck <folder>` | A fresh campaign: shop, a pack, collection deck building, a match with random clicks; checks it pays and saves |
| `-menucheck <folder>` | Title screen, attract mode, menu, settings, campaign start. Run it without `-mute` (its sound checks fail muted) |
| `-replaycheck <folder>` | A duel, then its auto-saved replay played back to the same end |
| `-tagcheck <folder>` | The tag setup screen, a four-bot tag duel past a swap, its result and replay |
| `-boardcheck <folder>` | You play with random clicks: zone choice, the Graveyard viewer |
| `-matcheck <folder>` | Two Field Spell decks: the mats and a Field Spell taking over |
| `-pilecheck <folder>` | Pile menus, the picker, double-click to view, face-down banished cards, Cancel |
| `-controlscheck <folder>` | The phase bar, the attack arrow, pinning |
| `-layoutcheck <folder>` | The play layout through a Free Duel and a replay's controls |
| `-ordercheck <folder>` | Ordering effects that activate at once; the chain dialog's hint |
| `-clickcheck <folder>` | Clicks that must land the first time (drags in lists, hand cards, a press across a redraw) |
| `-shopcheck <folder>` | The card shop's shelves, tags, odds, a box, New arrivals |
| `-alleycheck <folder>` | The Back Alley's loop |
| `-finalcheck <folder>` | The Library's "Every Era" final to its epilogue |
| `-laddercheck <folder>` | The Champion's Ladder from its locked plaque to the top |
| `-themecheck <folder>` | The shell screens in both UI themes |
| `-musiccheck <folder>` | The duel music, including a real boss duel to the end (run it with `-mute`) |
| `-stylecheck <folder>` | Card art styles; run twice to check the choices are kept |
| `-searchcheck <folder>` | The deck editor's card search |
| `-aboutcheck <folder> [-aboutlicenses <folder>]` | The About screen: every notice, the license and third-party readers, missing files, keys, controller and mouse; it never opens a browser or Finder |
| `-ydkcheck <folder> -ydkfile <.ydk> -ydkfixture <.ydk>` | Importing `.ydk` files from paths |
| `-importcheck <folder>` | The Import Cards screen: the menu and screen layout, a zip, loose pictures and bad names, Keep mine and Replace them, HD, the card back, Cancel. It makes its own flat-colour pictures and never opens the file dialog |
| `-artcheck <folder>` | Card picture load times and memory (needs an HD picture folder) |
| `-editor <deck> -versioncheck <folder>` | Switching card versions in the editor |

Other launch options for checks and screenshots:

| Option | What it does |
|---|---|
| `-mods <folder>` | Use this mods folder |
| `-mute` | No sound this run (the volume settings are untouched) |
| `-gosecond` | Checks skip the coin toss and give you turn 1; this gives it to the other side |
| `-reducedmotion` | Reduced motion for this run |
| `-dev` | Developer mode in a normal build (F12 panel) |
| `-quitwatchdogtest`, `-quitwatchdogoff` | Windows: hang the quit on purpose, and leave out the timer that ends a hung quit after 10 seconds (`QuitWatchdog.cs`) |
| `-p0 <id\|you> -p1 <id> -deck0 <deck> -deck1 <deck>` | Start straight into that matchup (`you` plays the seat yourself) |
| `-autoclick` | Answers your prompts at random (human play end to end) |
| `-editor <deck> [-editor-search <query>] [-editor-test] [-editor-hand] [-editor-dragtest] [-controllertest]` | Open the deck editor on a deck, with optional search, tester, test hand, drag test or simulated controller (results in Player.log) |
| `-fxshots <folder>` | A screenshot of each kind of animation, then quit |
| `-hdpics <folder>` / `-hdpics off`, `-pics <folder>` | HD or EDOPro picture folders for this run only |
| `-qaplay <folder> [-qagames N]` | Plays the campaign like a player, logging every problem (seeds are fixed, so two fresh runs play the same duels) |

## Sound and music

Every sound is synthesized in code: no audio files ship with the game.

**Effects** (`src/Ygo.Engine/Audio/Sfx.cs`, prize chimes in `Sfx.Prize.cs`): 36 effects made from tones with pitch
sweeps, filtered noise, envelopes and a little echo.

- **Duels:** draws, shuffles, cards moving and landing, flips, summons (normal, special, and a bigger one for Level 7+
  and Extra Deck monsters), activations, two kinds of negation, attacks and their impact, destruction, damage (heavier at
  2,000 or more), LP paid and gained, each new turn, and a win fanfare or a falling loss.
- **Packs and menus:** the pack tearing open, a chime for rares and a burst for Ultra and above, clicks, hovers, DP
  counting in, level-ups, Card Tokens, and a buzz when something can't be done.
- **Prized moments:** chimes for a Gem Mint slab or a top rarity landing face-up, tuned to the duel's music.

Effects are timed with their animations and follow the duel speed (none at the fastest speeds, where animations are off
too). Settings has separate Sound effects and Music volumes, and Reactive music on or off.

**Music** (`src/Ygo.Engine/Audio/Sfx.Themes.cs`): a calm loop for the menus, and 17 duel themes written as chords, a
melody and a groove. Each renders as seven layers of the same length that loop in sync: bed (pad and bass), drums,
lead (the tune), drive (for low LP), dread (you're low), hope (they're low) and lethal (a heartbeat while a finishing
attack is pending). Each theme also has a sting, a victory and a defeat ending. The 17 themes:

- the everyday duel music;
- one per band (Beginner, Novice, Intermediate, Advanced, Expert, Master, Gauntlet);
- one for the Tag Cup finals;
- one per special guest (Ines, Nocturne, Rhea, Seraph);
- the Back Alley's everyday music, Orvel's Table and Nyx's Table;
- No Limits (written for the locked No Limits table).

Who gets which (`src/Ygo.Engine/Campaign/BossMusic.cs`):

- each circuit's last match plays its band's theme, every circuit in a band in its own key;
- all four Tier 0 Gauntlet matches play the Gauntlet theme, a semitone higher each match;
- each Tag Cup final plays the Tag Cup theme, and each guest plays theirs, one on one or in tag;
- in the Back Alley, Orvel's and Nyx's tables play their own themes at their host's seat; elsewhere the alley's music,
  a fourth up for a table's closer or headliner and for an encore;
- every other duel plays the everyday theme.

A boss's versus screen plays their sting and builds their theme on a worker thread (about a second); a duel whose
theme isn't built yet plays the everyday one until it is. Only themes in use stay in memory, and none are built while
the game is muted or the music is off. During the duel (`Sounds.DuelState`) the layers start with bed and drums; the
tune comes in once the boss's ace is out and stays; at a quarter of the LP or less, drive comes in with dread or hope;
an attack that would end the duel drops everything to the heartbeat within a third of a second (and brings it back as
fast if it doesn't); at the end the theme stops for its ending. Reduced motion makes every change a two-second fade;
Reactive music off keeps bed, drums and tune steady.

Listen without the game:

```bash
harness --export-sounds build/sounds
```

It writes every effect as a WAV, `tour.wav` with all of them in a row, each theme's layers in `themes/<name>/`, and
`themes/demo-<name>.wav` (a whole boss duel's music in order; `themes/demo-guide.txt` has the timings).

## Historic decks

`decks/history/` holds 74 real tournament lists, one for each deck in the game's competitive history from 2004 to 2026,
with their stories (`#era`, `#tier`, `#about`, `#why`, shown in the deck editor). `campaign/history.json` is the
catalog. Each deck has its own fictional duelist in `personalities/`.

**Credit and names.** A deck's `#source` line names its source, event and placing, but **never the player**: real
people's names never appear in the game. The builders are credited in
[decks/history/SOURCES.md](../decks/history/SOURCES.md).

**Ban lists.** `lflists/history.lflist.conf` holds the TCG Forbidden/Limited list in force for each deck's format (41
lists from April 2004 to October 2026: Format Library's, with the October 2026 list added by hand). Each deck names its list in a `#banlist` line, and the
deck editor switches to that list when it opens the deck. All 74 decks are legal under their own list
(`--deckbuilding-test` and `tools/check_history_decks.py` check it).

**Original card versions.** Decks from 2004-2005 play cards as they read then: pre-errata entries from
`cards-unofficial.cdb` and Goat-format entries from `goat-entries.cdb` with their `goat/` scripts (all in BabelCDB). The
engine loads them next to the current cards; they count as the same card for deck limits. `campaign/texts.txt` says
which text was in force when ([docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md](dev/CARD-VERSIONS-SPEC-2026-11-03.md)).

**Refreshing the lists.** Run from the repository root.

Fetch the lists again (Format Library's API through early 2026, YGOPRODeck's YCS Houston 2026 Top 32 for October 2026;
one request every 1.5 s):

```bash
python3 tools/fetch_history_decks.py --only goat-control
```

Leave out `--only` for every deck, and add `--force` to fetch decks that already have a list. It writes
`decks/history/<id>.ydk` (its `#source` line without the player's name) and the full credit to `SOURCES.md`.

Map card ids the engine doesn't know (such as EDOPro's temporary pre-release ids for the newest cards) by name, and
report any card without a script (needs `data/ygoprodeck/cardinfo.json`):

```bash
python3 tools/check_history_decks.py --fix
```

Fetch the ban lists again:

```bash
python3 tools/fetch_banlists.py
```

A full rewrite drops lists added one at a time with `--list "<Month Year>"`; the script's header says how to re-add
them.

## Replays

Every duel is recorded as its recipe (seed, flags, decks) and each answer either player gave. The core is
deterministic (that's the second core patch's job), so feeding the answers back replays it move for move.

- **Format** (`src/Ygo.Engine/Replays/Replay.cs`): a binary record, deflated and base64-encoded behind a `YGOR1:` prefix
  (about 1,000 characters). The file and the share code are the same text. Versions: 2 added tag partners, 3 the looks of
  your copies, 4 their slabs, 5 each side's mat, 6 the shuffle after a Deck look. Older replays still load; a replay
  with neither a mat nor the shuffle flag is still written as 4 so older games can read it.
- **Where:** the last 30 finished duels in `mods/replays/recent`, replays you keep in `mods/replays`, Replay Lessons in
  `mods/replays/lessons`.
- **Card database:** each replay stores the card count it was recorded with. A different count may mean changed cards,
  and the game warns that the replay may play out differently.
- **Checks:** `harness --replay-test --games 12` round-trips bot duels through codes and compares every core message;
  `-replaycheck <folder>` checks playback in the app. `harness --show-replay <file>` prints a saved replay's
  play-by-play.

## Rarity and foil showcase

Cards are drawn with their rarity's finish (26 finishes; the player's view is in [PLAYING.md](PLAYING.md)).
Printings come from `printings.bin` ([BUILDING.md](BUILDING.md#7-export-the-printings)).

`-foilshowcase <out.png>` shows one installed card per finish under a fixed light and saves a screenshot; two runs
give the same picture. Its options:

| Option | What it does |
|---|---|
| `-foilcards id,id,...` | Which cards |
| `-foilrarities r,r,...` | Forces each card's rarity (Rarity names, e.g. `UltraRare`) |
| `-foilall` | All 26 finishes |
| `-foillight x,y,hover` | Moves the light (`drift`: live, as in play) |
| `-foilsweep N` | N shots (`out-1.png`...) as the light crosses the cards |
| `-foilstrength s` | Foil strength |
| `-foilintensity subtle\|standard\|vivid` | The Foil intensity setting for this run |
| `-foilboard` | Adds a copy at board size and strength |
| `-foilslabs` | Slabs instead; `-foilslabs labels` (every grade's label at every size, a `slabfit:` log line each) or `-foilslabs context` (slabs on the grounds they're seen on) |

Related: `-gradeshowcase <folder>` (every grade's wear side by side), `-slabreveal <folder>` (the Grading Desk's reveal
frame by frame; add `-reducedmotion` for the calm one), `-foiltilt x,y` and `-cardtilt on|off`.

`tools/foil_shots.sh <folder>` saves the standard set from the built app (and fails if a slab label doesn't fit);
`tools/foil_compare.py <before> <after> <out>` lays two sets side by side in `<out>/index.html`.

## Downloading card pictures

The harness can download YGOPRODeck's small card pictures (168x246) into a folder, for your own use (the game itself
never downloads pictures). It goes a few at
a time at no more than 8 per second, skips pictures you already have, and can be stopped and resumed:

```bash
harness --download-pictures data/ygoprodeck/cardinfo.json mods/cards/images
```

**Personal use only. The images are © Konami. Never share them or commit them.** `mods/` is git-ignored so they stay
out of the repository; don't copy them anywhere that is shared, and never attach them to an issue.

## Before you send a change

Run the checks that cover what you touched. The usual set:

```bash
harness --campaign-test
```

```bash
harness --deckbuilding-test
```

```bash
harness --texts-check
```

```bash
python3 tools/check_history_decks.py
```

Each ends with its own summary; none should report a failure. For changes to the game's screens, run the matching
[app checks](#app-checks). For bot changes, compare before and after with `--matrix`, `--rate-decks` or the study that
fits, using the same seeds. [CONTRIBUTING.md](../CONTRIBUTING.md) has the rest (content rules, how to send it).

## Work notes

`docs/dev/` holds the project's working notes: dated specs, plans, QA reports and session handoffs. They're internal
and may be out of date; the code wins when they disagree. Good starting points:

- the newest `HANDOFF-*.md` in [docs/dev/](dev/) (how the work is run, owner rules, gotchas);
- [docs/dev/TODO.md](dev/TODO.md), open follow-ups;
- [docs/dev/UI-ROADMAP.md](dev/UI-ROADMAP.md), presentation ideas;
- [docs/dev/CHARACTERS.md](dev/CHARACTERS.md), every duelist (generated by `--characters`);
- [docs/dev/RULES.md](dev/RULES.md), where to check an official ruling.
