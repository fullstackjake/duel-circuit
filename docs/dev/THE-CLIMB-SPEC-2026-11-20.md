# The climb: build spec (2026-11-20)

The first build from [PRODUCTION-REVIEW-GAME-2026-11-20.md](PRODUCTION-REVIEW-GAME-2026-11-20.md): its M1 ("swap decks,
keep circuits", then raise the early skills), Q1 (repeat pay), Q2 (the starter tag), Q3 (deck-strength stars), Q4
(billed circuit finals), Q5 (duplicate recipe unlocks) and Q6 (guests' strength). The owner accepted all of them on
2026-11-20 (see Owner decisions). Line numbers are from commit 3b36438.

**Not in this build:** Vera's rival matches and New Game+ as the Champion's Circuit (later specs); recipe outright buys
(stay as they are); any new results or circuit-clear screen (the interface review's M5 rebuilds results later); the
versus screen's layout; the Library's own rows.

**A correction to the review.** Its finding 6 said a circuit's last match has "no different music cue". It has one:
`BossMusic.ForMatch` gives every circuit's last match its band's boss theme and a sting on the versus screen. The billing
below adds words to that moment, not sound.

## What the player gets

| # | Today | After |
|---|---|---|
| M1 | The Novice band's decks are the weakest in the game (mean Elo 1,255, Beginner 1,385) and all 12 play at skill 0.55; the same starter wins about 20 points more there than in the Beginner band | Three Novice duelists bring a stronger deck of their own theme (mean 1,309, the three circuit finals' rooms included), then the Novice and Intermediate duelists' skill rises toward full strength by measurement, until each band sits in its target |
| Q1 | A rematch of a won circuit, Tag Cup or guest match pays its full win DP (up to 2,588 DP) and level-ups pay DP | A rematch pays **150 DP**, as the Library does; its level-ups pay Card Tokens, not DP |
| Q2 | "Best first deck" is on Legendary Dragons, the weakest starter; Mekk-Knight's tile doesn't say the early circuits will be easy | The ribbon moves to **Warrior's Charge** (by the measured rule below); Mekk-Knight's tag says "Modern · makes the early circuits easy" |
| Q3 | Rows say "skill 3/10"; nothing says how strong a deck is | Five stars from the deck's measured rating on circuit rows, Tag Cup rows, guests and the Recipe Book; "skill N/10" goes |
| Q4 | A circuit's last match is "Match 3: Ryo, the Rookie" like any other | Its row reads "Final: Ryo, the Rookie" with the circuit's own final line; its versus says "Circuit final · Match 3 of 3" |
| Q5 | Beating Ryo's Warriors with Warrior's Charge "unlocks" a recipe you already own, marked NEW | The unlock line says "You already own every card in it." and the recipe isn't marked NEW |
| Q6 | The guests screen gives a blurb and a prize; Seraph reads as the next step after the Advanced band | Each open guest shows their deck's stars and "Best after the Master band" while that band isn't cleared |

## 1. The deck swaps (M1, part D)

**The rule.** A circuit's match keeps its character, its position and its key; only the deck changes, and only to a deck
that is already in `decks/`, already rated in `campaign/deck-ratings.txt`, of the character's own theme, and stronger.
No new deck lists, no new characters, no circuit moves. Three swaps qualify; the other Novice duelists have no
stronger deck of their own theme, and their band is left to the skill raise (section 2).

| Circuit (index) | Match | Character | Old deck (Elo) | New deck (Elo) | Why the character fits |
|---|---|---|---|---|---|
| Underdog Open (4) | 3 (key `4.2`) | Sage Aurelio, the Prophet (`spellbook`) | spellbook (1,180) | spellcasters-command (1,399), `decks/structure/` | A spellcaster sage; Spellcaster's Command is a spellcaster deck. His intro and win lines name Spellbook cards and are rewritten deck-neutral (section 5) |
| Trap Masters Cup (5) | 1 (key `5.0`) | Gage Ironhart, the Gearmaster (`geargia`) | geargia (1,338) | machine-re-volt (1,435), `decks/structure/` | An engineer of machines: Machine Re-Volt is a Machine deck. His lines ("my gears never stop", "Machine efficiency") fit as written; his gift scene and Geargiano signature stay |
| Small Engines Invitational (6) | 4 (key `6.3`, the circuit final) | Chef Praline, of the Yummy Way (`yummy`) | yummy (1,198) | synchron (1,527) | Her intro already says "Small monsters, tiny Synchros, lots of hand traps": Junk Synchron is exactly that. Her lines fit as written |

- **Circuit means after:** Underdog Open 1,256 → 1,310; Trap Masters Cup 1,267 → 1,291; Small Engines Invitational
  1,243 → 1,325. The Novice band 1,255 → 1,309. Still under the Beginner band (1,385): the skill raise does the rest.
- **The profiles** (`decks:` lines; the first deck stays first, since a match line without a deck and some screens take
  the first): `spellbook.profile` → `decks: spellbook, spellcasters-command`; `geargia.profile` → `decks: geargia,
  machine-re-volt`; `yummy.profile` → `decks: yummy, synchron`. Their other appearances (the Novice Tag Cup, the
  Library) keep their old decks.
- **`circuits.txt`** (exact lines):
  - Underdog Open: `match: spellbook spellcasters-command`, and blurb `Frog FTK, Six Samurai, Spellcaster's Command and
    Burning Abyss: underdogs from 2005 to 2014.`
  - Trap Masters Cup: `match: geargia machine-re-volt`, and blurb `Machine Re-Volt, Domain Monarch, Zoodiac and
    Altergeist: machines, monarchs and trap walls from 2008 to 2018.`
  - Small Engines Invitational: `match: yummy synchron`, and blurb `Floowandereeze, Runick Spright, Purrely and Junk
    Synchron: small monsters that add up, from 2008 to 2026.`
  - Under each changed circuit, the former match (section 4): `was: 3 spellbook spellbook`, `was: 1 geargia geargia`,
    `was: 4 yummy yummy` (1-based match number, then the line as it was).
  - The header comment gains `was:` and `finalline:` (format below).
- **`featured.txt`**: the three shelves must still pass the harness's rules (every match deck named by a `set:` or
  `soon:` line, sets released by the circuit's era, 2011). D replaces the `spellbook`, `geargia` and `yummy` names with
  the new decks on sets that hold their cards (Machine Re-Volt and Spellcaster's Command cards were printed by 2008;
  Junk Synchron by 2008), keeping 3-4 `set:` lines per shelf.
- **Text that names the old decks:** Sage's `intro:` ("Spellbook of Judgment. Read it and weep.") and `win:` ("Jowgen
  locked the door. You had no key.") are shown in all his appearances, so they become deck-neutral (section 5). Nothing
  else changes: `shop.txt` lines 315 ("Gears, traps and one little rat") and 318 ("Small monsters, every one") still fit;
  `story.txt` `gift-geargia` (lines 131-134) still fits; `scouting.txt` has a written `synchron` report (line 135) and the
  structure decks get the generated one.
- **Revert rule.** If, after the swaps and before any skill change, a swapped match measures under 40% for both Warrior's
  Charge and Undead Legion (two seeds), that swap is reverted: it made its slot a wall, which is a deck-choice mistake.
- **Fallback (not applied in this build):** if a Novice row is still above its band with every Novice skill at 1.0, the
  next lever is a whole-match swap to a Library character of the era (Lady Merel's nekroz 1,481, Agent Helix's spyral
  1,466, Ringmaster Pepe's performapal 1,412). Their Library scenes introduce them as strangers ("Pepe and Helix appear
  only here", `story.txt:560`), so that's the owner's call; D reports the rows and stops.

## 2. The skill raise and the measuring (M1, part D)

**Rules.** A skill only goes up, never down, and only to a value in the steps below. One skill per band, set on all of
that band's circuit characters (a character plays the same everywhere, so their Library, Tag Cup and guest-partner
appearances get stronger too; see Traps). Beginner skills don't change (Ryo 0.3 and Brask 0.4 are the first lessons).

- **Novice** (12 profiles at 0.55): frog-ftk, six-samurai, spellbook, burning-abyss, geargia, domain-monarch, zoodiac,
  altergeist, floowandereeze, runick-spright, purrely, yummy. Steps: 0.7, 0.85, 1.0.
- **Intermediate** (12 profiles at 0.7): teledad, blackwing, lightsworn, infernity, dino-rabbit, inzektor, qliphort,
  gouki, pendulum-magician, sky-striker, salamangreat, maliss. Steps: 0.85, 1.0.

**The rows and targets** (`[PLACEHOLDER]` working targets the owner accepted; the measurements may move them, and D
reports, never retunes, a target):

| Band | Target, each gate starter unedited | Today (review, seed 1, 16 a match) |
|---|---|---|
| Beginner | 55-70% | classic ~50, warriors 68 (Town Tournament) |
| Novice | 50-65% | classic ~71, warriors 78 (Underdog Open) |
| Intermediate | 45-60% | classic 53, warriors 64 (Synchro Dawn) |

- A **row** is one circuit's win rate for one starter: all its matches, `--games 24` a match, seeds 1 and 2 together
  (192 duels, about ±3.6 points).
- **Gate starters:** Warrior's Charge (`warriors`) and Undead Legion (`zombies`), the two "Best first deck" candidates.
  Legendary Dragons (`classic`) is measured and reported, and feeds Q2. Mekk-Knight Invoked is measured once (seed 1,
  one circuit a band) and reported only: no early band can be in target for it without walls for everyone else.
- A row within 3.6 points of a band edge is re-run at `--games 48` on both seeds before it decides anything.

**Picking a band's skill:** the highest step at which every gate row of that band is at or above the band's floor (Novice
50, Intermediate 45). If even the lowest step puts a row under the floor, the band keeps its skill. If every row is still
above the ceiling at 1.0, the band goes to 1.0, its rows are recorded "OFF (easy) at full strength", and the fallback in
section 1 goes to the owner.

**The harness.** D measures with one frozen copy, made before anything changes, so rebuilds can't move a number:
`cp -R build/bin/evidence build/bin/snap-climb`. Every study below runs from `build/bin/snap-climb/Ygo.Harness.dll`. It
reads the repository's live `campaign/` and `personalities/` (the harness finds its root by walking up to `vendor/`), so D
edits those files between runs and nobody else writes them during the build. Logs go to `build/climb/runs/`; finished
numbers go into this spec's "As measured" section as they land (scratchpads die on a restart).

One row:

```bash
H=build/bin/snap-climb/Ygo.Harness.dll; mkdir -p build/climb/runs
for s in 1 2; do dotnet $H --circuit-study --only "Underdog Open" --option warriors --games 24 --seed $s \
  > "build/climb/runs/<step>-underdog-warriors-s$s.txt" 2>&1; done
```

The ten early circuits' `--only` names: "Beginner Circuit", "Rookie Cup", "Town Tournament", "Regional Qualifier",
"Underdog Open", "Trap Masters Cup", "Small Engines", "Synchro Dawn", "Xyz and Link", "Link Age".

**Order and machine time** (measured today: about 45-60 s a circuit at 24 duels a match; runs go one after another, never
side by side, since each uses every core):

| Step | What | Runs | Time |
|---|---|---|---|
| D0 | Snapshot; baseline of all 10 circuits × classic, warriors, zombies × 2 seeds; Mekk once on Regional Qualifier, Underdog Open, Synchro Dawn | 63 | ~1 h |
| D0b | `dotnet build/bin/snap-climb/Ygo.Harness.dll --difficulty --games 12 --seed 1` (the whole map, its "after" column) | 1 | time it; expect 15-40 min |
| D1 | The swaps (section 1); `--campaign-test` from `build/bin/climb` once E has built it, else from the snapshot | | |
| D2 | Novice's 3 circuits × 3 starters × 2 seeds; apply the revert rule | 18 | ~20 min |
| D3 | Novice skill 0.7, 0.85, 1.0 in turn: 3 circuits × warriors, zombies × 2 seeds each (classic recorded at the chosen step) | 36 | ~40 min |
| D4 | Intermediate skill 0.85, 1.0: the same on its 3 circuits | 24 | ~25 min |
| D5 | Final: all 10 circuits × 3 starters × 2 seeds, `--difficulty` again; pick "Best first deck" (section 6) | 61 | ~1.5 h |

D's machine time is about 4-5 hours. `--arbitrage` and `--campaign-test` close it (section 9).

## 3. Repeat pay (Q1, part E)

**The rule.** A win in a match you've already won (before this duel) is a **rematch** and pays `Circuits.RepeatWinDp`
(150, the same constant as `Library.RepeatWinDp`). The first win in a match is unchanged (`WinDp` + the 200 first-win
bonus). It applies to:

- **Circuit matches** (`Circuits.Record`): repeat when `profile.HasWon(circuit, match)` before recording.
- **Tag Cup matches** (`Circuits.RecordTag`): repeat when `MatchWins` holds the tag key before recording.
- **Guests** (`GuestList.Record`): repeat when `MatchWins` holds the guest's key (one-on-one or tag) before recording.
  The guest's prize rule (`PrizeDue`) is unchanged; a win that pays the prize is never a repeat.
- **Loaners:** a rematch with a loaner pays half, 75 (`Loaners.DpShare`, rounded as today).
- **Level-ups:** a rematch's EXP still counts; its level-ups pay Card Tokens only (at least one each), as the Library's:
  `Leveling.AddExp(profile, exp, payDp: !repeat)`. A loss or draw in a match you've already won also pays its level-ups
  in tokens (`payDp: false`); a loss in a match not yet won pays as today.
- **Losses and draws** still pay 50 and 100 DP.
- **Ledger kinds don't change:** `circuit`, `tag`, `guest` and their `.loss` and `.prize` (`Circuits.PayDuel`). No new
  kind, so `LedgerCheck`'s source list (`LedgerCheck.cs:249`) stays as it is.
- **EXP** is unchanged (`Leveling.DuelExp`).

**On screen** (exact words; the interface review rebuilds the results screen later, so only the words change):

| Where | Today | After |
|---|---|---|
| Circuit row, won (`CampaignScreen.ShowCircuit`) | `Won ×2` / `Rematch: +1,050 DP` | `Won ×2` / `Rematch: +150 DP` |
| Circuit header, cleared | `Cleared. Replay any match for DP.` | `Cleared. A rematch pays 150 DP.` |
| Tag Cup row, won | `Rematch: +<WinDp> DP` | `Rematch: +150 DP` |
| Results DP line, circuit and tag rematch | `Win` · `+<WinDp>` | `Rematch` · `+150` |
| Results DP line, loaner rematch | `Win (half: loaner deck)` | `Rematch (half: loaner deck)` · `+75` |
| Results DP line, guest rematch | `Win` · `+<WinDp>` | `Rematch` · `+150` |
| First wins | unchanged | unchanged |

**CAMPAIGN.md** ("DP for each duel", for the Technical Writer at the end): replace the three bullets with:
"A win pays DP; later circuits pay more. The first time you win a match, you get 200 DP more. After that, a rematch pays
150 DP, and the levels it reaches pay Card Tokens instead of DP. A loss pays 50 DP and a draw 100 DP, so you always earn
something." The guests section gains: "A rematch against a guest pays 150 DP."

## 4. Old saves (part E)

A save that already beat a match whose deck changes must mean the same after the swap.

- **Match keys, cleared circuits, prizes, first-win records:** unchanged. The swap keeps each match's position, so key
  `4.2` still means "Underdog Open, match 3"; `MatchWins` isn't touched; `IsCleared` and the map are as they were; no
  prize or first-win bonus pays again (the match is already won, so the new opponent's next win is a 150 DP rematch).
- **Wins, losses and bonds per character:** unchanged (the characters are the same).
- **Recipe Book, the old recipe:** a save that won the old pairing keeps that recipe. `Circuits.Migrate` (it already runs
  on every load, `CampaignState.cs:252`, and for a new save at 406) gains a last step, once per save: for each `was:` line,
  if the save has won that match, add `formerrecipe:<owner>|<deck>` to `profile.Unlocks`; then add the gate key
  `climb:2026-11-20` so the step never runs again. `RecipeBook.All` lists former recipes (`Recipe.Former = true`, where
  "<circuit>, match N, in an earlier version") unless the same owner and deck is met elsewhere; `IsUnlocked` is true for a
  recipe whose `FormerKey` is in `Unlocks`. The Recipe Book lists a former recipe only once it's unlocked.
- **Recipe Book, the new recipe:** counts as unlocked for a save that won that match (the save beat "Underdog Open, match
  3"; `IsUnlocked` reads `MatchWins`). So a save past the Novice band gets up to three recipes it never played against:
  Sage's Spellcaster's Command, Gage's Machine Re-Volt and Chef Praline's Junk Synchron. No DP moves. (Decided here; see
  Owner decisions 7.)
- **New Game+:** `MatchWins` is cleared by `NewGamePlus` as today; the `formerrecipe:` keys stay in `Unlocks`.
- **Format:** no new save line; `formerrecipe:` and `climb:` ride on the existing `unlock` lines, which older builds read
  and keep.

## 5. Billed circuit finals (Q4)

**The key is `finalline:`, not `champion:`.** `champion:` is already a match key in this parser (`Profile.cs:1642`: the
Library's Champion's table) and would add a fifth match to the circuit.

- **Format** (`circuits.txt`, one per circuit, anywhere in its block): `finalline: <text>`. The last match's character
  says it. 100 characters at most. `#` ends a line in this file, so the text can't contain `#`.
- **Parse:** `Circuit.FinalLine` (empty when absent). A Tag Cup block ignores it.
- **Which match is the final:** `Circuits.IsFinal(circuit, match)`: the last match, as `BossMusic.ForMatch` and the Recipe
  Book's "boss" already have it. The Tier 0 Gauntlet bills only its fourth match.
- **On the circuit row** (`CampaignScreen.ShowCircuit`, the last row only): the title `Final: <DisplayName>` instead of
  `Match N: <DisplayName>` (same 21 px bold, same box; "Final: " is shorter than "Match 4: "); the quote line (14 px italic,
  `T.Intro`, its existing box) shows `FinalLine`, in quotes as today. With no final line: the title still reads
  `Final: ...` and the quote is the intro, as today.
- **On the versus** (`DuelController.ShowVersus(int circuit, ...)`): the subtitle `Circuit final · Match 3 of 3`
  instead of `Match 3 of 3`, and the opponent's quote is `FinalLine` when set. Nothing else on the versus changes.
- **Not on the results** (the interface review rebuilds them).

**The 22 lines** are drafted by a **Narrative Designer** agent (Opus) into `docs/dev/THE-CLIMB-LINES-2026-11-20.md` (its
only file) for the owner's approval; D pastes the approved ones. Until then the fallback shows. Its brief:

- **Who says each:** the circuit's last opponent (table below). Read their `.profile` (`intro`, `rematch`, `win`, `lose`,
  `look`, `partner`, the duel lines), their scenes in `story.txt` (grep their id) and `docs/dev/STORY-BIBLE.md`.
- **What it is:** their line before the circuit's deciding match: they know this match decides the circuit. One or two
  sentences, 100 characters at most, in their voice. It must not repeat or paraphrase any line already in their profile
  or scenes (`story.txt`'s header rule on repeated lines).
- **Rules:** original characters only; no real people, no Konami staff or anime characters; all-ages; no card names
  except the archetype names their own lines already use; no `#`.
- **Also:** two deck-neutral lines for Sage Aurelio (`spellbook.profile`), replacing `intro:` and `win:`, true whether
  he plays Spellbook or Spellcaster's Command (he plays both). And one line per circuit is the whole job: no scenes.

| Circuit | Final opponent (profile) | Deck | Their intro today |
|---|---|---|---|
| Beginner Circuit | Ryo, the Rookie (`rookie-ryo`) | warriors | "My first real duel! I've been practicing all week!" (his rematch line shows here today) |
| Rookie Cup | Tink, the Tinkerer (`tink`) | gadgets | "Red finds Green, Green finds Yellow..." |
| Town Tournament | Vera, the Vanguard (`vera`) | warriors | "I run traps. I just never wait for them." |
| Regional Qualifier | Professor Hollis, the Patient (`hollis`) | stall-burn | "Take your time. I certainly will." |
| Underdog Open | Nerio Malebranche, of the Ninth Circle (`burning-abyss`) | burning-abyss | "Every card I lose comes back to haunt you." |
| Trap Masters Cup | Ione Wraith, the Trap Haunter (`altergeist`) | altergeist | "My traps are programs. And they've already run." |
| Small Engines Invitational | Chef Praline, of the Yummy Way (`yummy`) | synchron (new) | "Small monsters, tiny Synchros, lots of hand traps. Delicious." |
| Synchro Dawn Classic | Ash Hollow, the Empty Hand (`infernity`) | infernity | "My hand is empty. That's when I'm strongest." |
| Xyz and Link Regionals | Coach Gouki, of the Link Ring (`gouki`) | gouki | "Every Gouki that falls tags in another. Ding ding!" |
| Link Age Open | White Rabbit Maliss (`maliss`) | maliss | "Down the rabbit hole... and into banishment." |
| Golden Age Championship | Ignatia Blaze, of the Four Rulers (`dragon-ruler`) | dragon-ruler | "Fire, water, wind, earth. They always come back." |
| Control Kings Cup | King Barrund, of the True Draco (`true-draco`) | true-draco | "Continuous cards fuel my kings..." |
| Hybrid Engines Invitational | Abomination Rex, the Unchained (`unchained`) | unchained | "I break my own cards to break yours." |
| Expert Series: Shadows and Storms | Dottie Larkin, of the Dark World (`danger-dark-world`) | danger-dark-world | "Reveal a card. Any card. Danger!" |
| Expert Series: Rituals and Fire | Kenta Mizorogi, the Tenpai Rush (`tenpai-dragon`) | tenpai-dragon | "I go second and break everything you built. One turn." |
| Expert Series: The New Wave | Linden, the Elfnote Conductor (`elfnote`) | elfnote | "A few small notes, and a whole Synchro symphony." |
| Expert Series: Fall Format | Lisette Morrow, the Moonlit Dancer (`lunalight`) | lunalight | "Shall we dance? Try to keep up under the moon." |
| Master League: Link Wars | Zia Sparks, of the Level Two (`spright`) | spright | "Level 2, Rank 2, Link 2. Everything's a two with me." |
| Master League: Graveyard Kings | Rafael Lezcano, of the K9 Unit (`k9-vanquish-soul`) | k9-vanquish-soul | "Show me your Attributes. I'll show you a loss." |
| Master League: Houston 2026 | Corin Vasse, of the Branded (`branded-despia`) | branded-despia | "Fusion is removal, and my Extra Deck is a toolbox." |
| Master League: October 2026 | Sir Lucan Graye, of the Chaos Rite (`chaos-ritual`) | chaos-ritual | "Light and darkness, joined by ritual. Draw your blade." |
| Tier 0 Gauntlet | Kelda Amin, the Archivist (`ishizu-tearlaments`) | ishizu-tearlaments | "Twenty-six of thirty-two at Pasadena..." |

## 6. "Best first deck" and the starter tiles (Q2)

- **The rule:** after D5, for each classic starter (Legendary Dragons, Warrior's Charge, Undead Legion), take its 7
  Beginner and Novice rows and sum how far each is outside its band (0 inside). The smallest sum gets the ribbon; a tie
  goes to Warrior's Charge. Decided (owner, 2026-11-20): the ribbon goes to Warrior's Charge after the curve is measured.
  If the rule picks another starter, D reports it and the ribbon still moves to Warrior's Charge only on the owner's word.
- **Text** (`CampaignState.Starters`, `CampaignState.cs:37-51`): `Badge = "Best first deck"` moves from `classic` to
  `warriors`. Mekk-Knight's `Tag` becomes `Modern · makes the early circuits easy` (was "Modern · strong from the start,
  hard to play well"; its blurb already says "Strong from the first duel, but its plays take practice").
- **CAMPAIGN.md** ("Starting out"): "New to Yu-Gi-Oh!? **Warrior's Charge** is marked **Best first deck**. **Mekk-Knight
  Invoked Evolved** is strong from the first duel and makes the early circuits easy, but its plays take practice."

## 7. Deck strength (Q3, Q6)

**The scale** (`DeckStrength.Stars`, from `deck-ratings.txt`'s Elo; peak builds from `peak-ratings.txt` as the loaded
ratings already merge them):

| Stars | Elo | Decks today (90 rated) | Examples |
|---|---|---|---|
| 1 | under 1,350 | 17 | classic 1,276, frog-ftk 1,054, gouki 1,259 |
| 2 | 1,350-1,499 | 31 | warriors 1,418, goat-control 1,401, machine-re-volt 1,435 |
| 3 | 1,500-1,649 | 21 | synchron 1,527, shaddoll 1,592, snake-eye 1,612 |
| 4 | 1,650-1,799 | 13 | thunder-dragon 1,693, light-and-darkness 1,768, eldlich 1,764 |
| 5 | 1,800 and up | 8 | tearlaments 1,947, chaos-ritual 1,896, ryzeal 1,867 |

An unrated deck (not in either file) shows no stars. The scale is fixed numbers, so a re-rating moves a deck's stars
only when it crosses a line.

**How they're drawn:** five `★` characters (the glyph the Library's stamp already renders), the filled ones in
`T.GoldHex`, the rest in `T.TextDimHex`, as rich text, at the size of the text they sit in. No literal colours.

**"skill N/10" goes.** `Personality.StyleSummary` drops its `· skill N/10` part and returns the trait words only ("attacks
hard, walks into traps"). Why one and not both: after the raise, most Novice and Intermediate duelists read 10/10, the
same as the Gauntlet's, so the number would say nothing about how hard a match is; the stars do. Every screen that shows
`StyleSummary` (circuit and Library rows, versus, brackets, the Ladder, tag setup, Free Duel's pickers) loses the number
with no other edit.

In the table below every `★★★★★` is five glyphs with as many gold as the deck's stars and the rest dim.

**Where they show** (each at or above the interface review's 14 px floor at 1080, in an existing text box, nothing else on
the row shrinks):

| Screen | Where exactly | Words |
|---|---|---|
| Circuit row (`CampaignScreen.ShowCircuit`, line 1303-1305) | First line of the reward column (the 16 px text at x 610, 160 wide), above "First win" or "Won ×N" | `★★★★★`, the first two gold, the rest dim |
| Tag Cup row (`CampaignScreen.ShowTagCup`, line 1475-1476) | The same place | Stars of the pair's mean Elo |
| Guests (`DuelController.ShowGuests`, the `Decks` string, line 53) | End of the 16 px decks line, open guests only | ` · ★★★★★ · Best after the Master band` (the band part only while that band isn't cleared) |
| Recipe Book row (`RecipeBookScreen.DrawRow`, line 203) | End of the 14 px owner line, open recipes only | `Captain Brask · boss · ★★★★★` |
| Recipe Book detail (`RecipeBookScreen.ShowDetail`, line 327-328) | End of the 15 px "by" line | `  ·  Strength ★★★★★` |

A guest's stars are its own deck's (the strongest of Ilse's four). **"Best after"** names the first band, in map order,
whose circuits' mean deck Elo is at least the guest's Elo minus 25 (`DeckStrength.BestAfterBand`); above every band it
names the Tier 0 Gauntlet ("Best after the Tier 0 Gauntlet"). With today's data: Ines → Beginner, Nocturne → Advanced,
Seraph → Master, Rhea → Master.

## 8. Duplicate recipe unlocks (Q5)

When a circuit win unlocks a recipe (`RecipeBook.JustUnlocked`, used at `DuelController.cs:5900`) and you already own
every card in it (`RecipeBook.OwnsAll`), the results line reads `Recipe unlocked: <owner>'s <title>. You already own every
card in it.` and the recipe is marked seen (`profile.Seen.Add(RecipeBook.SeenKey(recipe))`), so the Recipe Book shows no
NEW on it. Otherwise the line is unchanged.

## Parts and files

Three parts start together; the interfaces below are fixed, so none waits on another. A fourth, the Narrative Designer,
writes only its draft file. **Who may run Unity: part U only.**

### Part E: engine and harness (general-purpose, Opus)

**Owns:** `src/Ygo.Engine/Campaign/Profile.cs` (classes `DuelReward`, `Circuit`, `Circuits`: `Parse`, `Migrate`,
`Record`, `RecordTag`), `src/Ygo.Engine/Campaign/Guests.cs` (`GuestList.Record`), `src/Ygo.Engine/Campaign/RecipeBook.cs`,
`src/Ygo.Engine/Campaign/DeckStrength.cs` (new), `src/Ygo.Engine/AI/Personality.cs` (`StyleSummary` only),
`src/Ygo.Harness/CampaignTest.cs`, `src/Ygo.Harness/ClimbTest.cs` (new, if the steps don't fit `CampaignTest`) and its
flag line in `src/Ygo.Harness/Program.cs`. Builds into `build/bin/climb`. Touches no sound code, no `Unity` file and no
data.

**Public interface (exact):**

```csharp
// Profile.cs
public sealed class DuelReward
{
    /// <summary>A win in a match already won before this duel (circuit, Tag Cup or guest): paid <see cref="Circuits.RepeatWinDp"/>.</summary>
    public bool Repeat;
}
public sealed class Circuit
{
    /// <summary>The last match's line before the circuit's deciding duel ("finalline:"), or "".</summary>
    public string FinalLine = "";
    /// <summary>Matches as they were before a deck swap ("was: N character deck"): 0-based match index, the old pairing.</summary>
    public List<(int Match, CircuitMatch Was)> Former = new List<(int Match, CircuitMatch Was)>();
}
public sealed class Circuits
{
    public const int RepeatWinDp = Library.RepeatWinDp; // 150
    public const string ClimbMigrated = "climb:2026-11-20";
    /// <summary>A win's DP before the first-win bonus and the loaner share: <see cref="WinDp"/>, or <see cref="RepeatWinDp"/> for a rematch.</summary>
    public int DuelDp(int circuit, bool repeat);
    /// <summary>The circuit's last match: its final (billed on the row and the versus).</summary>
    public static bool IsFinal(Circuit circuit, int match);
}

// RecipeBook.cs
public sealed class Recipe { public bool Former; }
public static class RecipeBook
{
    public static string FormerKey(Recipe recipe);                      // "formerrecipe:" + recipe.Key
    public static bool OwnsAll(Deck recipe, Profile profile, PrintingIndex printings, ICardDataProvider cards);
}

// DeckStrength.cs (namespace Ygo.Engine.Campaign)
public static class DeckStrength
{
    public const int MaxStars = 5;
    public static readonly double[] Lines = { 1350, 1500, 1650, 1800 };
    public static int Stars(double elo);                                                    // 1-5
    public static int StarsFor(IReadOnlyDictionary<string, double> ratings, string deck);   // 0 when unrated
    public static int StarsFor(IReadOnlyDictionary<string, double> ratings, IEnumerable<string> decks); // mean Elo of the rated ones; 0 when none
    public static double Elo(IReadOnlyDictionary<string, double> ratings, IEnumerable<string> decks, bool strongest); // 0 when none
    public static double BandMean(Circuits circuits, IReadOnlyDictionary<string, double> ratings, string band);
    public static string BestAfterBand(Circuits circuits, IReadOnlyDictionary<string, double> ratings, double elo); // a band name, or "Tier 0 Gauntlet"
}
```

- `Parse` reads `finalline:` and `was:` inside a circuit block (`was:` with a match number outside 1..count, or an
  unknown character, is skipped). `OwnsAll` is `RecipeShop.Missing(recipe, profile, Banlist.None, int.MaxValue, printings,
  cards).Count == 0`.
- `Record`, `RecordTag`, `GuestList.Record`: section 3. `reward.Repeat` set; `reward.Summary` says `+150 DP (rematch)`.
- `Migrate`: section 4's last step.
- `RecipeBook.All` and `IsUnlocked`: section 4.

**Tests** (each fails without its change; `--campaign-test` gains them, or `--climb-test` if E makes its own):
1. A second win in `0.0` pays 150, `Repeat` true, `DpEarned["circuit"]` up by 150; the first paid `WinDp(0) + 200`.
2. A loaner rematch pays 75; a Tag Cup rematch 150; a guest rematch 150; a guest's prize win isn't a repeat.
3. A rematch that crosses a level pays that level in tokens (≥ 1) and 0 DP; a first win across a level pays its DP.
4. A loss in a won match pays 50 and token-only level-ups.
5. `finalline:` parses to `FinalLine`; a circuit without one has ""; `IsFinal` is true only for the last match.
6. `was:` parses to `Former`; a bad number is skipped.
7. Old save: a profile with wins in `4.2`, `5.0`, `6.3` and no `climb:` key, through `Migrate`: `Unlocks` gains the three
   `formerrecipe:` keys and `climb:2026-11-20`; `RecipeBook.Unlocked` holds Sage's spellbook, Gage's geargia and
   Chef Praline's yummy (Former) and the three new recipes; `IsCleared(4..6)` and every `MatchWins` value are unchanged; a
   second `Migrate` adds nothing; a profile with no wins there gets only the gate key.
8. The real-save shape: `--profile-check` on a copy of the owner's save (`build/qa-review-game/save-copy/profile.txt`,
   read-only; copy it again under `build/climb/` first) loads, migrates, and keeps its 10 cleared circuits.
9. `DeckStrength`: 1349.9 → 1, 1350 → 2, 1499 → 2, 1500 → 3, 1649 → 3, 1650 → 4, 1799 → 4, 1800 → 5; unrated → 0;
   `BestAfterBand` for 1768 is "Master", for 1401 "Beginner", for 2100 "Tier 0 Gauntlet".
10. `StyleSummary` contains no "skill".
11. `OwnsAll` true for a profile holding the whole recipe, false with one copy short.
12. Data (after D's edits, warnings until the lines are approved): every circuit has a `FinalLine` of at most 100
    characters; every `was:` names a deck whose recipe is reachable.

### Part D: data and measuring (general-purpose, Opus)

**Owns:** `campaign/circuits.txt`, `campaign/featured.txt`, the 24 Novice and Intermediate `personalities/*.profile`
`skill:` lines plus `spellbook.profile` (`decks:`, and `intro:`/`win:` once approved), `geargia.profile` and
`yummy.profile` (`decks:`); `build/bin/snap-climb`, `build/climb/`; this spec's "As measured" section. Never runs
Unity; never edits code.

Work in order: sections 1, 2 and 6, steps D0 to D5. Report each step's table (row, seed 1, seed 2, mean, in band?) into
"As measured" as it lands.

### Part U: screens (Unity Architect, Opus; the only agent that opens Unity)

**Owns (and the exact ranges in the shared files):**

| File | Method (lines at 3b36438) | Change |
|---|---|---|
| `unity/Assets/Scripts/CampaignScreen.cs` | `ShowCircuit` (1265-1313): lines 1277-1279 (cleared header), 1297 (title), 1299-1301 (quote), 1303-1305 (reward column, stars, `supportRichText`) | Sections 3, 5, 7 |
| `unity/Assets/Scripts/CampaignScreen.cs` | `ShowTagCup` (1381-1484): lines 1475-1476 | Repeat words, stars |
| `unity/Assets/Scripts/DuelController.cs` | `ShowVersus(int circuit, int match, bool cycled)` (869-911): lines 894 and 898 only | Final subtitle and quote |
| `unity/Assets/Scripts/DuelController.cs` | `ShowTagResults` (1077-1123): lines 1096-1102 | `Rematch` label and amount |
| `unity/Assets/Scripts/DuelController.cs` | `CampaignCheck` (2709-4019): one line after 4001, `yield return ClimbCampaignCheck(Report, Shot);` | The check hook |
| `unity/Assets/Scripts/DuelController.cs` | `ShowCampaignResults` (5862-5931): lines 5880-5888 and 5900-5902 | `Rematch` label and amount; Q5's unlock line |
| `unity/Assets/Scripts/DuelController.Guests.cs` | `ShowGuests` (27-~66): line 53; `ShowGuestResults` (256-289): line 271 | Stars and "Best after"; `Rematch` label |
| `unity/Assets/Scripts/CampaignScenes.cs` | `ShowGuests` (676-742): line 717 only, `.supportRichText = true` on the decks line | Lets the stars colour |
| `unity/Assets/Scripts/RecipeBookScreen.cs` | `Refresh` (150-186), `DrawRow` (188-228) line 203, `ShowDetail` (290-420) lines 327-328 | Hide locked former recipes; stars |
| `unity/Assets/Scripts/CampaignState.cs` | `Starters` (37-51) | Ribbon and Mekk tag (section 6) |
| `unity/Assets/Scripts/StrengthText.cs` (new) | `public static string Stars(int stars, UiTheme T)`: "" for 0, else five `★` in `GoldHex`/`TextDimHex` | Shared by the rows |
| `unity/Assets/Scripts/DuelController.ClimbCheck.cs` (new) | `private IEnumerator ClimbCampaignCheck(Action<string, bool> report, Func<string, IEnumerator> shot)` and `private static string WinLabel(DuelReward reward, bool loaner)` (used by both results methods) | The check |

Nothing else in `DuelController.cs`: the audio build's ranges (`UpdateMusic` near 2358, the draw ending near 5718) are not
touched. `DuelController.ShowVersus` near 869 and `ShowCampaignResults` near 5862 are the nearest; the audio build should
re-read line numbers after this one lands.

**`-campaigncheck` steps** (in `ClimbCampaignCheck`, on the check's own fresh save, each a `campaign check: ok/FAIL` line
and a shot under `climb-*`):
1. The Beginner Circuit's last row reads `Final: Ryo, the Rookie`; its quote is the circuit's `FinalLine` (or the intro
   when it has none). Shot `climb-final-row`.
2. No circuit row's second line contains "skill"; the reward column's star count equals `DeckStrength.StarsFor` of the
   match deck.
3. The last match's versus subtitle starts `Circuit final · `. Shot `climb-final-versus`.
4. After a staged win in `0.0`, the row reads `Rematch: +150 DP` and the cleared header (after staging all three)
   `Cleared. A rematch pays 150 DP.` Shot `climb-rematch-row`.
5. `WinLabel` for a repeat is `Rematch`, for a loaner repeat `Rematch (half: loaner deck)`, for a first win `Win`.
6. With Seraph unlocked (staged), the guests row's decks line ends ` · ` + four gold stars + ` · Best after the Master
   band`. Shot `climb-guests`.
7. The Recipe Book row of an unlocked recipe shows stars; a staged `formerrecipe:spellbook|spellbook` shows Sage's
   Spellbook recipe; without the key it isn't listed. Shot `climb-recipes`.
8. The starter screen's ribbon is on Warrior's Charge and Mekk-Knight's tag reads `Modern · makes the early circuits
   easy`. Shot `climb-starters`.
9. At 1280x720 and 1920x1080 (the check's window sizes), no text the build adds is drawn under 14 px at 1080 scale and
   none overflows its box (the row's existing texts keep their sizes).

## Traps

- **`champion:` is taken** (the Library's Champion's table): using it in `circuits.txt` adds a match. Use `finalline:`.
- **`#` starts a comment** in `circuits.txt`: a final line can't contain one.
- **Skills are per character.** Raising the Novice and Intermediate skills also strengthens their Library cups (Synchro
  Peak, TeleDAD, Rabbits & Rulers, HAT & Shaddoll, Nekroz & PePe, ABC to SPYRAL, Link Dawn, TOSS & Eldlich, Tearlaments,
  Kashtira & Snake-Eye, Ryzeal), the Every Era final's Synchro Peak round (six-samurai), the Novice and Intermediate Tag
  Cups, and Nocturne's and Rhea's tag partners (altergeist, sky-striker). Allowed (it only raises), but
  `library-advice.txt` and those `wall-advice.txt` rows read easier than the game until re-measured (Owner decision 5).
- **Studies read live data.** While D measures, nobody else writes `campaign/` or `personalities/`. E's tests run on
  whatever skills D has at that moment; they must not assert a skill value.
- **`featured.txt` is checked** by the harness: a swap without its shelf lines fails `--campaign-test`.
- **The match count check** (`CampaignTest.cs:65`, 83 matches) stays true: swaps don't add matches.
- **`StyleSummary` is shown in many places**: dropping the skill changes them all at once, on purpose; a check that
  matched "skill" text needs updating (E greps the harness and Unity checks for `skill ` and lists what it changed).
- **Old saves:** `Circuits.Migrate` first moves match keys when circuits were added (`AddedOctober2026`). Put the
  former-recipe step last, after that move, so the keys it reads are in today's numbering.
- **A rematch's `Summary`** feeds the results log and checks (`DuelController.LiveDuel.cs:112-122` compares loss DP):
  losses are unchanged, so that check holds.
- **No card pictures anywhere** (tests use the text look); LF line endings; no commit, stash, reset or branch switch.

## Verification

From the repository root, after E's build into `build/bin/climb` (CLAUDE.md: a named build folder, so the default
`build/bin/Ygo.Harness` and D's snapshot stay untouched):

```bash
H=build/bin/climb/Ygo.Harness.dll
dotnet $H --campaign-test            # 0 failures, with the new steps
dotnet $H --deckbuilding-test
dotnet $H --texts-check
dotnet $H --arbitrage                # 0 DP-positive
dotnet $H --games 3                  # smoke
dotnet $H --profile-check build/climb/save-copy/profile.txt
```

Then U's app checks, one at a time, each in a fresh absolute `-mods` folder (docs/DEVELOPING.md#app-checks):
`-campaigncheck` (with the climb steps), `-layoutcheck`, `-tagcheck`, `-finalcheck` (guests and versus), `-themecheck`.
The Evidence Collector reruns all of it on a fresh build, reads every `climb-*` shot, and confirms each test fails with its
change reverted.

**Costs** (agent-days, with checks): **D** 1.5 (plus about 4-5 h of machine time, D0-D5); **E** 1; **U** 1.5; the
Narrative Designer 0.25 (then the owner's approval). Review, one fix round and the Evidence pass about 1 more. Total about
5 agent-days.

## As measured

Measured 2026-11-20/21 by part D. Harness: `build/bin/snap-climb` (copy of `build/bin/evidence`, commit 3b36438), run
from a scratch root `build/climb/root/` (symlinks to the live `campaign/`, `decks/`, `vendor/`, each profile, plus
`000-standin.profile`, a copy of `anselm.profile`). Why: the circuit study's stand-in is the first profile by id among
the highest skill; at Novice 1.0 that would have been `altergeist` (cautious knobs) instead of Anselm Quist. The copy
keeps Anselm in every run (checked: a baseline row is duel-for-duel identical in both roots). 6 of 10 cores
(`DOTNET_PROCESSOR_COUNT=6`). Raw output and commands: `build/climb/runs/`; scripts `build/climb/run.sh`, `table.py`.
A row is all of a circuit's matches, 24 duels a match, seeds 1 and 2 (144 duels for a Beginner circuit, 192 otherwise).

**D0, baseline** (before any change):

| Circuit | classic s1 / s2 / mean | warriors s1 / s2 / mean | zombies s1 / s2 / mean | Target |
|---|---|---|---|---|
| Beginner Circuit | 61.1 / 44.4 / 52.8 | 84.7 / 86.1 / 85.4 | 80.6 / 84.7 / 82.6 | 55-70 |
| Rookie Cup | 47.2 / 56.9 / 52.1 | 68.1 / 73.6 / 70.8 | 88.9 / 93.1 / 91.0 | 55-70 |
| Town Tournament | 58.3 / 45.8 / 52.1 | 70.8 / 79.2 / 75.0 | 72.2 / 86.1 / 79.2 | 55-70 |
| Regional Qualifier | 47.2 / 41.7 / 44.4 | 52.8 / 68.1 / **60.4 IN** | 66.7 / 80.6 / 73.6 | 55-70 |
| Underdog Open | 71.9 / 58.3 / 65.1 | 78.1 / 81.2 / 79.7 | 84.4 / 84.4 / 84.4 | 50-65 |
| Trap Masters Cup | 71.9 / 82.3 / 77.1 | 92.7 / 93.8 / 93.2 | 89.6 / 91.7 / 90.6 | 50-65 |
| Small Engines | 77.1 / 78.1 / 77.6 | 91.7 / 94.8 / 93.2 | 93.8 / 87.5 / 90.6 | 50-65 |
| Synchro Dawn | 54.2 / 58.3 / **56.2 IN** | 70.8 / 77.1 / 74.0 | 79.2 / 82.3 / 80.7 | 45-60 |
| Xyz and Link | 54.2 / 57.3 / **55.7 IN** | 76.0 / 78.1 / 77.1 | 79.2 / 78.1 / 78.6 | 45-60 |
| Link Age | 68.8 / 54.2 / 61.5 | 87.5 / 87.5 / 87.5 | 82.3 / 84.4 / 83.3 | 45-60 |

Mekk-Knight Invoked (seed 1 only): Regional Qualifier 97.2, Underdog Open 99.0, Synchro Dawn 99.0.
D0b `--difficulty --games 12 --seed 1` ("after" column), Beginner to Link Age: 39, 39, 53, 44, 65, 38, 56, 25, 38, 46;
overall 32% (33.7 min).

**D1/D2, the swaps** (skills unchanged): Underdog Open classic 57.8 IN, warriors 76.0, zombies 80.7; Trap Masters Cup
73.4, 91.7, 87.5; Small Engines 66.1, 82.8, 83.9. The swapped matches, warriors and zombies over both seeds:
Spellcaster's Command 64 / 83, Machine Re-Volt 79 / 79, Junk Synchron 55 / 70. None under 40: no swap reverted.

**D3, Novice skill** (gate starters, both seeds, means):

| Step | Underdog Open w / z | Trap Masters Cup w / z | Small Engines w / z | Every row ≥ 50? |
|---|---|---|---|---|
| 0.55 (after swaps) | 76.0 / 80.7 | 91.7 / 87.5 | 82.8 / 83.9 | yes |
| 0.85 | 71.4 / 74.0 | 85.9 / 85.4 | 78.6 / 81.2 | **yes** |
| 1.0 | 46.9 / 49.0 | 52.1 / 55.2 | 31.2 / 34.9 | no |

Chosen: **0.85** (the highest step with every gate row at or above 50; 0.7 can't be chosen over it, so it wasn't run).
At 1.0, classic: 32.3, 42.7, 17.2. The jump from 0.85 to 1.0 is the planner switching on at 0.9 (`PlansAhead`).

**D4, Intermediate skill:**

| Step | Synchro Dawn w / z | Xyz and Link w / z | Link Age w / z | Every row ≥ 45? |
|---|---|---|---|---|
| 0.7 (D0) | 74.0 / 80.7 | 77.1 / 78.6 | 87.5 / 83.3 | yes |
| 0.85 | 67.7 / 76.6 | 77.6 / 78.1 | 84.9 / 81.2 | **yes** |
| 1.0 | 44.3 / 54.7 | 34.9 / 38.5 | 32.8 / 29.2 | no |

Chosen: **0.85**.

**D5, final** (Novice 0.85, Intermediate 0.85). The Beginner rows are D0's (no Beginner data changed and duels are
deterministic); warriors and zombies are the 0.85 rows above; classic was re-run:

| Circuit | classic | warriors | zombies | Target | In band |
|---|---|---|---|---|---|
| Underdog Open | 47.9 (51.0 / 44.8) | 71.4 | 74.0 | 50-65 | none (classic 2 under) |
| Trap Masters Cup | 72.9 (72.9 / 72.9) | 85.9 | 85.4 | 50-65 | none |
| Small Engines | 53.1 (54.2 / 52.1) | 78.6 | 81.2 | 50-65 | classic |
| Synchro Dawn | 54.2 (52.1 / 56.2) | 67.7 | 76.6 | 45-60 | classic |
| Xyz and Link | 57.3 (57.3 / 57.3) | 77.6 | 78.1 | 45-60 | classic |
| Link Age | 60.9 (62.5 / 59.4) | 84.9 | 81.2 | 45-60 | none (classic 0.9 over) |

Every gate row of Novice and Intermediate is still OFF (easy) at the chosen step, and every one is under the floor at
1.0. Neither of the spec's cases fits exactly (the fallback is for "above the ceiling at 1.0"); the band went as high
as the floor allows, and the owner decides what's next (see the part D report). `--difficulty` after, Beginner to
Link Age: 39, 39, 53, 44, 62, 46, 46, 46, 44, 46; overall 33% (D0b: 32%). Rows near an edge were not re-run at 48 a
match (none decided a skill: every gate row is 6+ points from the floor at 0.85 and 2+ points under it at 1.0).

**"Best first deck"** (section 6; distance outside the band, 4 Beginner + 3 Novice rows):

| Starter | Beginner | Novice | Sum |
|---|---|---|---|
| Legendary Dragons (classic) | 2.2 + 2.9 + 2.9 + 10.6 | 2.1 + 7.9 + 0 | **28.6** |
| Warrior's Charge (warriors) | 15.4 + 0.8 + 5.0 + 0 | 6.4 + 20.9 + 13.6 | 62.1 |
| Undead Legion (zombies) | 12.6 + 21.0 + 9.2 + 3.6 | 9.0 + 20.4 + 16.2 | 92.0 |

The rule picks **Legendary Dragons**, not Warrior's Charge. Per section 6, the ribbon moves to Warrior's Charge only on
the owner's word.

Machine time: D0 64 min, D0b 34, D2 19, D3 61, D4 38, D5 39 (13 min of rows, 34 of `--difficulty`): about 4.3 h.

## Owner decisions (recommendation first)

1. **The early curve.** Decided (owner, 2026-11-20): swap decks and keep circuits, then raise Novice and Intermediate
   skills toward 1.0 where the measured rows stay in band. Only three swaps qualify under "the character's own theme"
   (section 1); the band reaches its target through the skills.
2. **Repeat pay.** Decided (owner, 2026-11-20): the Library's 150 DP, token-only level-ups. This spec extends it to Tag
   Cup and guest rematches, which paid the same full rate (a champion's guest rematch paid 2,588 DP).
3. **New Game+.** Decided (owner, 2026-11-20): the Champion's Circuit, in a later spec.
4. **Recipe outright buys.** Decided (owner, 2026-11-20): unchanged.
5. **The Library's advice after the skill raise.**
   - Recommended: re-measure the affected cups in the tuning pass's Library step (TUNING-PLAN-2026-11-06.md, step 4,
     already planned and overnight), and accept that until then those cups' Stuck? odds read a little easy.
   - *Or* re-measure them in this build (about 12-16 h of machine time, `--library-study --only <cup> --write`).
   - *Or* raise skills on circuit appearances only (a per-match override; more code, and a duelist would play
     differently in the Library than on the map).
6. **"Best first deck".** Decided (owner, 2026-11-20): Warrior's Charge, after the curve is measured (rule in section 6).
7. **What an old save sees after a swap.** Recommended (and specced): it keeps its old recipe and counts the new one as
   unlocked, since it won that match; the next win there is a 150 DP rematch. *Or* the new recipe stays locked and the
   first win against the new deck pays as a first win (needs a per-match "former win" mark and a change to the results'
   unlock line; about half a day more).
8. **Uniform skill per band.** Recommended: one value for all 12 duelists of a band (simple to measure and explain). *Or*
   per circuit (three values a band, about three times the trial runs).
9. **Vera's rival matches.** Decided (owner, 2026-11-20): optional fifth matches, in a later spec.

## As built (2026-11-21)

Built as specified, then one fix round after an independent code review, with the deviations below. "As measured"
above is the data agent's and is unchanged. The lines are in [THE-CLIMB-LINES-2026-11-20.md](THE-CLIMB-LINES-2026-11-20.md).
Code: engine and harness: `Campaign/Profile.cs` (`Circuits.DuelDp`, `RepeatWinDp`, `IsFinal`, `FinalLine`, `Former`,
`Migrate`'s former-recipe step, `FormerDoneKey`, `WinPart`, `LevelParts`), `Campaign/Guests.cs` (guest rematches),
`Campaign/RecipeBook.cs` (`Former`, `FormerKey`, `OwnsAll`), `Campaign/DeckStrength.cs`, `AI/Personality.cs` (no skill in
`StyleSummary`), `ClimbTest.cs` (run inside `--campaign-test`); data: `campaign/circuits.txt`, `campaign/featured.txt`,
the 24 `personalities/*.profile` skill lines and the three swapped decks' profiles; Unity: `CampaignScreen.cs` (final
row, stars, rematch text), `RecipeBookScreen.cs` (stars, wrapped line, former recipes), `StrengthText.cs`,
`CampaignScenes.cs` and `CampaignState.cs` (versus, starter tiles), `DuelController.Guests.cs` (`GuestStrength`),
`DuelController.ClimbCheck.cs`. Models: spec by a Game Designer (Opus), data and measuring by a `general-purpose` agent
(Opus, about 4.3 h of studies), engine by a `general-purpose` agent (Opus), screens by a Unity Architect (Opus), lines by
a Narrative Designer (Opus), review by a Code Reviewer (Opus), docs by a Technical Writer (Sonnet).

What it does:

- **Swaps** (same characters, same match keys): Sage Aurelio, Spellbook to Spellcaster's Command (Underdog Open, match 3);
  Gage Ironhart, Geargia to Machine Re-Volt (Trap Masters Cup, match 1); Chef Praline, Yummy to Synchron (Small Engines
  Invitational, the final). The shop shelves in `featured.txt` follow.
- **Skills:** the 12 Novice characters 0.55 to 0.85 and the 12 Intermediate characters 0.7 to 0.85. They are also
  stronger wherever else they appear (Tag Cups, Library cups, two tag partners); those tables weren't re-measured.
- **Rematches:** a win in a match already won pays 150 DP (75 with a loaner deck) for circuit, Tag Cup and guest rematches.
  Level-ups in a match already won, whatever the result, pay Card Tokens, not DP. First wins and prizes are unchanged.
- **Old saves:** a save that beat a swapped match keeps the old recipe (`formerrecipe:` in `Unlocks`) and has the new one;
  no DP moves. Each swap gets one done mark (`wasdone:`). A New Game+ save gets the old recipe if it won the match this
  cycle, beat that duelist before, or opened that recipe.
- **Billed finals:** the last match of each of the 22 circuits reads "Final: <name>" on its row and "Circuit final ·
  Match N of N" on the versus screen, with the circuit's `finalline:`. Decided (owner, 2026-11-21): the recommended lines,
  with the alternative for Lisette, Kenta and Corin; Sage's two new lines as recommended.
- **Stars:** five-star deck strength (`DeckStrength`: Elo lines at 1,350, 1,500, 1,650 and 1,800) replaces "skill N/10"
  on circuit rows, Tag Cup rows (the pair's mean), the guests' decks line (with "Best after the X band" until cleared)
  and the Recipe Book (open recipes only).
- **Recipe Book:** the stars come first on the "by" line. Decided (owner, 2026-11-21): a long line wraps to a second line.
- **Duplicate unlocks** say "You already own every card in it." and aren't marked NEW.

Deviations:

1. **The key is `finalline:`**, as the spec already says (`champion:` is a match key). 22 circuits have one; a Tag Cup
   ignores it.
2. **Only three swaps fit the "own theme" rule** (owner decision 1); the band gets to its target through the skills.
3. **The migration gate is per `was:` line.** A `wasdone:` mark is written for each swap, so a later swap still applies.
   `Circuits.ClimbMigrated` (the first build's single gate) is kept so a save that already has the three swaps doesn't
   get them twice.
4. **Former recipes are listed for every caller of the recipe list** (`RecipeBook.All` adds them, flagged `Former`); only
   the Recipe Book screen filters them to the ones you hold.
5. **The guests' line names a tag partner by name only**, to leave room for the stars.
6. **The real-save test runs inside `--campaign-test`** (step 8 of `ClimbTest`), not as a separate flag. It loads and
   migrates copies in memory (`build/climb/save-copy`, `build/climb-e/saves`) and writes nothing; with no copy there it
   prints an `info` line and the step doesn't run, so it isn't proof on a clean checkout.
7. **The starter ribbon is on Warrior's Charge** in `CampaignState.cs` (as the owner decided before the measuring). The
   measured rule picks Legendary Dragons; see "Decided" below. Mekk-Knight's tile text now reads "Modern · makes the early
   circuits easy" (it said "hard to play well").

Checks: `--campaign-test` gained the climb's steps (rematch pay, finals, `was:`, the migration on synthetic and real saves,
stars, no skill in a style summary, `OwnsAll`); app `-climbcheck` (21 steps), also run at the end of `-campaigncheck`
(210 steps). `-finalcheck` and `-laddercheck` now expect stars where they expected "skill N/10". The Evidence Collector's
report is [EVIDENCE-2026-11-21.md](EVIDENCE-2026-11-21.md).

Decided (owner, 2026-11-21):

- **Early skills.** This build ships Novice and Intermediate at 0.85. A follow-up data round then sets both bands to 1.0
  (full strength) and gives easier decks to the circuits that fall under their band at 1.0 (measured: Small Engines, Xyz
  and Link, Link Age at 29-38%). Open, next.
- **The ribbon.** "Best first deck" stays on Warrior's Charge, as built. The measured rule picked Legendary Dragons, but the
  measurements used a skilled pilot and a beginner needs the kinder start.
- **Loss-farming.** Conceding in a match not yet won still pays level-up DP (about 110 DP a duel late on). Leave it for
  now; a TODO item for the Game Designer.

Not verified: a real played duel ending in a rematch; the Library cups, Tag Cups and guests after the skill raise;
Mekk-Knight after the changes.

Evidence (2026-11-21): the Evidence Collector tested fresh builds ([EVIDENCE-2026-11-21.md](EVIDENCE-2026-11-21.md)).
Harness: every check passes (`--campaign-test` with the climb's steps 1 to 12, `--deckbuilding-test`, `--texts-check`,
`--copies-test`, `--replay-test`); `--arbitrage` has 0 DP-positive; `--games 30` has 0 script errors and 0 crashed games.
App: all 25 planned runs (21 at 1280x720, 4 at 1920x1080) have 0 FAIL, exit 0, and no exception in any log. Old saves: the
owner's save (and its 9 backups) and the QA save keep their match wins, cleared circuits and DP, and gain only the 3
`formerrecipe:` keys (where the match was won) and 3 done marks; the real folders' file times are unchanged. Its four low
findings and its list of weak steps (its sections 5 and 6) are in `TODO.md`; none blocks the commit. One thing it flags as
built: after New Game+, a "once" guest beaten before pays the full win rate without the prize.
