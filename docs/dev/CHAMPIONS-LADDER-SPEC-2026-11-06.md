# Spec (2026-11-06): the Champion's Ladder, v1

Owner commitment: [STORY-REVIEW-2026-11-06.md](STORY-REVIEW-2026-11-06.md), answer 3, and
[story-review/STRUCTURE-2026-11-06.md](story-review/STRUCTURE-2026-11-06.md), ledger C and decision 3. Build plan:
[NEXT-BUILD-2026-11-06b.md](NEXT-BUILD-2026-11-06b.md), chunk L (this spec only). It pays two debts: Ilse's line in
`every-era-corkboard` ("Past the binders there's a ladder ... I'm near the top") and Junia's in `nl-invitational-win`
("The road's long, and it has other doors"). Values marked `[PLACEHOLDER]` are hypotheses. Tuning is parked (section 11).
The scene text belongs to the Narrative Designer. This spec gives only the triggers and scene ids.

## 1. What it is

A fixed ladder of **ten rungs** above the Format Library. Rungs 1-8 are held by champions you have already met, each
playing their own deck at full strength. You climb one rung at a time: beat the holder of the rung above you and you
take it. **Duchess Ilse holds rung 9**, one under the top, and to take her rung you have to beat all four of her decks
in one sitting. **The top rung's holder is new** (section 2a): the one person Ilse has never got past. Once you have won the No Limits Invitational, the **out-of-towners visit**. One at a time, each takes the rung
directly above yours, and you can't climb past them until you beat them.

- **The fantasy:** you are the champion of the circuit and of every era, and now you face the people who play only for
  the next rung. Each step is one clear choice: which of your decks to bring against which champion.
- **The pillars:** short (one duel a rung), legible (you always know your next opponent), and never weakened (every
  holder plays at skill 1.0, and difficulty comes from deck choice and format alone).

## 2. Rungs, climbing, falling

**Data:** a new file, `campaign/ladder.txt`. Rungs are listed from the bottom. Each holder plays their profile deck. The
order follows `deck-ratings.txt` (Elo in brackets). The roster is `[PLACEHOLDER]` until the tuning pass.

| Rung | Holder (`personalities/`) | Deck | Why they're here |
|---|---|---|---|
| 1 | Rhea (`rhea`) | thunder-dragon (1693) | The reigning tag champion, one on one at last |
| 2 | Count Aurum (`eldlich`) | eldlich (1764) | The 2021 binder's champion and the Gauntlet's opener |
| 3 | Seraph (`seraph`) | light-and-darkness (1768) | The traveling pro |
| 4 | Grandmaster Celeste (`angelechy`) | angelechy (1810) | |
| 5 | Pilar Sendra (`kashtira`) | kashtira (1828) | Halfway |
| 6 | Corin Vasse (`branded-despia`) | branded-despia (1830) | |
| 7 | Mage Valdris (`dark-magician-chaos-ritual`) | dark-magician-chaos-ritual (1839) | |
| 8 | Nico and Nina Castell (`ryzeal`) | ryzeal (1867) | The 2025 binder's twins, now playing for no year |
| 9 | **Duchess Ilse** (`ilse`) | synchron, zombies, gadgets, modern | "Near the top": one sitting, all four decks (below) |
| 10 | **The holder** (`ladder-top`, new, section 2a) | chaos-ritual (1896) | The strongest deck that is legal today |

- **Why chaos-ritual at the top.** The owner wants the top on the strongest deck. It's the highest-rated deck that is
  fully legal under the Tier 0 Gauntlet's list, TCG September 2026. I checked main and extra decks against
  `lflists/history.lflist.conf`, and it's also legal under October 2026, the deck's own `#banlist`. It's the "October
  2026 TCG meta" deck, so it is this week's deck, which suits a holder who plays only the current list. The decks rated
  above it are all over that list: tearlaments 1947 (Kitkallos, three Havnis/Merrli/Scheiren) and ishizu-tearlaments
  1879. CPUs aren't bound by lists, but the top should be beatable by the same deck the player is allowed to build.
  `peak-ratings.txt` doesn't apply, because its builds are No Limits contraband and the strongest rates 1865. There's
  no new deck list. The Ladder takes the deck as rated.
- **The reshuffle.** Sir Lucan Graye played chaos-ritual on rung 8, so he and his deck leave the Ladder (he keeps his
  circuit match). The twins move down to 8, and Mage Valdris (dark-magician-chaos-ritual 1839, also legal) takes 7.
- **Coralie Fenn drops off.** At 1947 she would out-rate the top from a lower rung and turn the climb upside down near
  the end. She stays the Gauntlet's boss.
- **Two rituals in a row.** Rungs 7 and 10 are both Chaos rituals. That's accepted for v1 and noted for tuning
  (section 11).

```
opens: ilse                # the Every Era final won and Ilse beaten once at her guest table
prize: 5000                # [PLACEHOLDER] the top, once; plus 3 Card Tokens and the poster
rung: rhea thunder-dragon
...
rung: ilse synchron, zombies, gadgets, modern   # more than one deck: one sitting, every deck in order
rung: ladder-top chaos-ritual                    # the new holder (personalities/ladder-top.profile)
visitors: invitational     # the No Limits Invitational won (Brackets.AlleyRecord(profile, InvitationalKind).Titles > 0)
visitor: rue yubel
visitor: sasha tenpai-dragon
visitor: augie k9-vanquish-soul
visitor: junia snake-eye   # last: "other doors" pays off when she climbs the stairs
```

- **Climbing:** your next opponent is the active visitor if there is one, otherwise rung `Climbed + 1`, otherwise
  nobody (you hold the top). A win takes that rung. A loss or a draw changes nothing.
- **Falling: none in v1.** A loss keeps your rung, as a lost Every Era round does. On paper, with 10 rungs and a 60%
  win rate per duel, the climb takes about 17 duels. A fall of one rung per loss takes about 40, and a fall that stops
  at landings on rungs 4 and 7 takes about 28 (a 40,000-run simulation). Most of that extra is rematches against
  holders you've already beaten, and we can't measure it while tuning is parked. The stakes come from Ilse's sitting and
  from the visitors. Falling is decision 2.
- **Ilse's sitting:** Synchron, Zombies, Gadgets and Modern, one duel each, in her guest order. A loss or a draw ends
  the sitting, and the next one starts again at Synchron. You may change decks between duels. Her rung falls on the
  fourth win. Her decks are weaker than her neighbors' (1384-1527), so the format is what makes her hard, not her decks.
  At 80% per duel a sitting clears 41% of the time, which is about 7 duels in all. At 90% it clears 66% of the time,
  about 5 duels. A single duel at the top rung at 40% takes about 2.5. This is the only "series" state, saved as
  `ladsit` (section 7).
- **Visitors** (Rue, Sasha, Augie, Junia: the four you met at the Alley Brawl) play the **2024 base decks** their Brawl
  builds were cut from. These are full lists, already rated, with playbooks. They bring no Basement splash and no peak
  build: upstairs plays by the list, and only you are bound by it. A visitor arrives only when no sitting is in
  progress. Beat one and the next arrives. If you already hold the top, the visitor's row sits above yours ("Junia is
  visiting: beat her to keep the top"). Your rung number never changes.
- **Every holder plays at skill 1.0.** `Ladder.Opponent` returns `Character.Copy()` with `Skill = 1.0` (Rhea, Seraph,
  Celeste and the twins have lower profile skills). Profiles are untouched.
- **After the top**, any rung or beaten visitor can be challenged again from the board. That's a rematch: your standing
  doesn't change and it pays the repeat rate.

## 2a. The top rung's holder (a brief for the Narrative Designer)

The id `ladder-top` is fixed, so the data needn't change. The display name, title and pronouns are the writer's.
Original only: no real player's name and no likeness to any anime or manga character.

- **Role:** the one at the top of the Ladder, who has never been seen in the shop, on the circuit or in the alley. They
  don't play for a year (Hollis), for no year (Ilse) or for no list (Junia). They play **this week's list**: they rebuild
  the day a list comes out and are on top by the weekend. Their single theme is "the next rung is the only one".
- **Why above Ilse:** Ilse came to the ladder after she stepped away from the circuit. She has sat one rung below this
  person ever since, with decks from no year, and has never once taken the rung. That's the real weight under "I'm near
  the top". The holder respects her. Ilse's own line is the one that never got past them.
- **Look (one line):** a plain grey cardigan over a pressed collar, one battered deck box held shut with a binder clip,
  this month's ban list folded in the breast pocket, and ink on two fingertips.
- **Voice:** brief, dry, courteous, always in the present tense. They talk about "this week", never "back then". They
  never mention a year.
- **Beats they carry:**
  - planted, unnamed, in `ladder-opens` and `ladder-ilse`;
  - their entrance in `ladder-holder` (`ladderseat:ladder-top`);
  - the top in `ladder-top` (`laddertop`). Rung 10 falling raises `ladderbeat:ladder-top` too, so `ladder-top` is the
    one scene for it; don't write a separate beat scene.
  - From then on, the board shows "Top: You".
- **Profile file (the writer's):** `personalities/ladder-top.profile`.
  - Text lines: `name`, `title`, `intro`, `win`, `lose`, `look`, `partner` (or none: they never partner, and the
    skipped-line fallback handles that), and `summon`/`strike`/`hit`/`danger`.
  - **The data fields the builder needs, with these values:** `skill: 1.0`, `decks: chaos-ritual`, `ladder: 0`, and
    the play knobs copied from `chaos-ritual.profile` (`aggression 0.6`, `caution 0.45`, `patience 0.5`,
    `spending 0.55`), so the deck plays as it was rated.
  - **No `signature:`** in v1. A gift would need its own `gift-` scene and a card nobody else signs. That's deferred.
  - **Portrait optional:** `art/portraits/ladder-top.png` when the owner has one, otherwise the fallback.

## 3. Where it lives and when it opens

- **Place:** "Past the binders". It's a plaque on the Library shelf **below the Every Era plaque** (shelf index
  `Cups.Count + 2`, next to `UnlistedShelf`). Selecting it shows the **ladder board** on the right: ten rows from the
  bottom up, each with the rung, portrait, name and deck. It marks your rung, highlights the next opponent, shows Ilse's
  row with four pips for her sitting, and puts a visitor's row above yours. It has Duel and Scout buttons. There's no
  new screen and no new art. Portraits are optional: the four visitors have none and get the existing fallback.
- **Opens:** `Library.FinalCleared(profile) && profile.WinsAgainst("ilse") > 0`. This honors "Before you start
  climbing, come and sit across from me". `WinsAgainst` survives New Game+ (her guest `MatchWins` key doesn't). The
  plaque appears with the final's clear, locked, and says: `Locked: Sit across from Duchess Ilse first (Campaign >
  Guests)`. That points the player to her table.
- **Rules:** the last circuit's (Tier 0 Gauntlet, TCG September 2026, its Ban Passes per the campaign's mode), always,
  as for Ilse's table. NG+ never puts the ladder under the Beginner list. **No loaners** (decision 7). A deck that isn't
  legal gets a refusal with its first problem, as Every Era round 1 does. Stuck? and Scout work as on any versus.
- **Ilse's guest table is unchanged.** At the table she brings the next deck after each win you take from her, and she
  plays one on one, with prizes. On the ladder she brings all four in a sitting. They use separate keys and separate
  progress.
- **No Limits stays locked and the ladder doesn't wait for it.** Only the visitors depend on No Limits, through the
  Invitational title, so they switch on by themselves the day it ships (the finale's pattern). Junia's line can't play
  before then either, so the promise and its payoff arrive together.

## 4. What it reuses

- **Engine:** a new `src/Ygo.Engine/Campaign/Ladder.cs` (about 250 lines) has `Ladder.Load(path, roster)`, `IsOpen`,
  `Climbed`, `Next`, `DeckFor` (the sitting's deck), `Opponent`, `Record` and `Events`. It reads the same
  `character deck[, deck...]` grammar as guests.txt, using `GuestList.Parse`'s splitter as the model.
- **Pay:** `Circuits.PayDuel(profile, Ledger.Ladder, ...)`, so every DP change goes through `Profile.Earn`. EXP comes
  from `Leveling.DuelExp(library.ExpCircuit, ...)` with `payDp` only on a first climb (as `Library.RecordFinal` does).
  Wins call `profile.AddWin`, so bonds and `beat:` gifts work as everywhere.
- **App:** the guest flow is the template (`DuelController.Guests.cs`): versus, `CoinToss` with `MeasuredToss`,
  `BeginLiveDuel`, results through `ShowResultsSoon`, rematch and shop buttons. The shelf and panel follow the Every Era
  plaque (`ShowFinal`).
- **LiveDuels:** a new kind, `LiveDuels.Ladder`. A ladder duel left live settles as a loss (rung kept, sitting reset,
  `ladder.loss` paid). `Settle` takes an optional `Ladder? ladder = null`.

## 5. Rewards and the economy

| What | DP (ledger) | Once? | Rationale |
|---|---|---|---|
| First climb of a rung, or first win against a visitor | 800 (`ladder`) `[PLACEHOLDER]` | Once ever | `Library.WinDp` + `FirstWinBonus`, the post-game's going rate |
| A win inside Ilse's sitting (the first three) | 150 (`ladder`) | Each | `Library.RepeatWinDp`. The fourth pays the 800 |
| Rematch win (after the top) | 150 (`ladder`) | Each | Same cap: a held ladder isn't a DP farm |
| Loss / draw | 50 / 100 (`ladder.loss`) | Each | `Circuits.LossDp` / `DrawDp` |
| The top, first time | 5,000 (`ladder.prize`) + 3 Card Tokens `[PLACEHOLDER]` | Once ever | Below the final's 10,000 and Ilse's 10,000. A champion holds about 300k DP, so DP isn't the pull |
| The top | Poster `ladder` (a card's artwork; the builder picks a climbing-themed card and names it in the As built) | Once | `Posters.All` entry, `Earned` = top reached |

- **The whole ladder pays at most** 10 × 800 + 4 × 800 + 5,000 = **16,200 DP** once ever, plus 150 a win after that.
- **Nothing is DP-positive:** the ladder has no entry, stake or sink, so no buy-then-win loop runs through it. Its only
  sources are duel pay and one prize, and neither repeats across New Game+.
- `Ledger.Ladder = "ladder"` joins the source list, so the DP-write source guard covers it. `--arbitrage` gets a ladder
  sequence (section 9).

## 6. Story beats (triggers and ids; the Narrative Designer writes them)

| Trigger (new) | When | Scene ids (required **bold**) | Place |
|---|---|---|---|
| `ladder` | The first campaign-screen Open with `IsOpen` true (a catch-up next to the finale's in `AlleyOnOpen`) | **`ladder-opens`** | library |
| `ladderseat:<character>` | Duel pressed against that rung holder or visitor, before the versus, once, only if a scene exists (copies `guest:`) | **`ladder-ilse`**, **`ladder-holder`** (`ladderseat:ladder-top`), **`ladder-visit-junia`**; optional `ladder-visit-rue` / `-sasha` / `-augie` | library |
| `ladderbeat:<character>` | Results of your first ladder win against them (Ilse: her rung falling) | **`ladder-ilse-beat`**, **`ladder-visit-junia-beat`** | back-room / library |
| `ladderrung:<n>` | Results, the first time rung n is climbed | optional `ladder-halfway` (`ladderrung:5`) | library |
| `laddertop` | Results, the top first reached (after its `ladderrung:10` and `ladderbeat:` scenes, in file order) | **`ladder-top`**, **`ladder-top-home`** (Gus) | library / shop-floor |

Notes for the writer:
- `ladder-opens` may play long after Ilse's win (old saves), so it must not assume it comes straight after `gift-ilse`.
- The top holder is new to the player. Plant them in `ladder-opens` and `ladder-ilse` without a name, and introduce
  them in `ladder-holder` (section 2a).
- No scene except the visitor scenes may mention No Limits, the Invitational or the out-of-towners. A save can reach
  the top without them.
- Ilse's table scene says "This one's only between us". `ladder-ilse` should set her rung apart: "all four, one
  sitting".
- story.txt's Every Era header comment ("the Champion's Ladder is only a hint") needs updating, along with the trigger
  list. Both are the writer's.

## 7. New Game+ and old saves

- **New save lines:** `ladwin <key> <n>` (keys `rung.<i>`, `visitor.<id>`, `top`) and `ladsit <n>`, in a new
  `Profile.LadderWins` / `LadderSitting`. A save without them loads with the ladder unclimbed.
- **Old saves load unchanged.** A save that already won the final and beat Ilse sees the ladder open, and
  `ladder-opens` plays on its first Open, once.
- **New Game+ leaves the ladder alone** (like `LibraryWins`). `NewGamePlus()` doesn't touch either field, so a first
  climb never pays twice. The rules stay the Gauntlet's in any cycle.

## 8. Edge cases

- A draw ends Ilse's sitting (as a loss does) and pays `DrawDp`.
- When a visitor arrives during a sitting, they wait until the sitting ends.
- A quit mid-duel is a loss through `LiveDuels`.
- A deck file that's missing from the data skips that rung, and `Ladder.Problems` reports it. The harness fails on it.
- A holder who is also a guest (Rhea, Seraph, Ilse) keeps separate guest and ladder keys. Ladder wins add to their
  record (`Wins`), as every duel does.
- There are no tag duels on the ladder.

## 9. Checks

**Harness:** a new `src/Ygo.Harness/LadderTest.cs`, run by `--ladder-test` (about 3 s) and from `--campaign-test`.
1. ladder.txt parses: 10 rungs, 4 visitors, every deck file exists, every character is known (`ladder-top` included),
   and Ilse's rung has 4 decks. Rung 10's deck has no problems under the last circuit's rules
   (`Circuits.Problems`-style, the engine's own check). `ladder-top` has skill 1.0, `ladder: 0`, no signature, and
   knobs equal to chaos-ritual's. Print each rung's rating as information (no fail on order while tuning is parked).
2. Opening: a veteran save with the final won and no Ilse win stays closed. One Ilse win opens it, and it stays open
   after `NewGamePlus()`. With no Library loaded it stays closed.
3. Climb: from fresh, Next is rung 1. A win moves to rung 2. A loss and a draw keep the rung.
4. Sitting: three wins and then a loss reset it to Synchron. Four wins take rung 9, and the sitting is back at 0.
5. Every `Opponent` has Skill 1.0, and the roster's personalities are unchanged.
6. Pay: the ledger lines add up to the DP change. The first climb pays 800, a rematch 150, a loss 50 (`ladder.loss`),
   and the top 5,000 plus 3 tokens once. A replay after `NewGamePlus()` pays no first climbs. The poster is earned at
   the top.
7. Visitors: they're inactive without an Invitational title. With a test record that has one, Next is Rue even in the
   middle of the climb. Beating Rue brings Sasha. At the top a visitor still shows up. With all four beaten, Next is
   null. No visitor arrives during a sitting.
8. Events: `ladder` comes once. `ladderrung:<n>` and `ladderbeat:<id>` come only on the first win. `laddertop` comes
   once. `ladderseat:<id>` is pending only before the first ladder duel against that holder.
9. Saves: `ladwin`/`ladsit` survive a round trip. An old save with no lines loads.
10. `LiveDuels`: a live ladder duel settles as a loss, keeps the rung and resets the sitting.
11. The required scenes in section 6 exist. This fails until the writer's chunk lands, which is intended for the
    combined build. `TriggerOk` in CampaignTest.cs accepts the five new triggers.

**`--arbitrage`:** a ladder sequence that climbs everything, then 50 rematches, then NG+ and climbs again. It must
show ladder DP at most 16,200 + 150 per rematch win, nothing spent, and no first-climb pay after NG+.

**App: a new `-laddercheck`** (`DuelController.LadderCheck.cs`, a 15th line in `build/qa-quick/checks.sh`):
1. On a save that's past the final, without an Ilse win: the plaque is locked and says so.
2. Give the save an Ilse win and Open: `ladder-opens` plays once.
3. The board shows 10 rows with you at the bottom, all clear of Back at 1920x1080 and 1280x720 (screenshots).
4. Rung 1's versus says Tier 0 Gauntlet rules and skill 10/10, and refuses a loaner.
5. A win shows "Rung 1 +800". A concede keeps the rung.
6. Jump to 8 climbed: `ladder-ilse` plays, and the versus shows Synchron (1 of 4). After two wins and a concede it's
   Synchron again.
7. Jump to 9: pressing Duel on rung 10 plays `ladder-holder` before a versus that shows chaos-ritual. Winning the top
   shows the prize, the tokens and the poster, then `ladder-top` and `ladder-top-home`.
8. A relaunch replays nothing. A rematch pays 150. After NG+ the board is unchanged.
9. With visitors forced (a check-only override): `ladder-visit-rue` plays before her duel, and her row sits above yours.

Report each scene as seen. No other app check changes.

## 10. Build plan (disjoint files, split around the story-gates/Vera build)

Another builder (NEXT-BUILD-2026-11-06b chunk G) is editing these right now: `Story.cs`, `Guests.cs`, `CampaignState*.cs`,
`CampaignScenes.cs`, `DuelController.Guests.cs`, `CampaignTest.cs`, `guests.txt` and story.txt's header. So the Ladder
starts with new files only, and the hooks in shared files come later.

**Can start now:**

| # | Agent | Job | Files (only these) |
|---|---|---|---|
| 1 | Senior Developer | The engine, the data and the harness test, all in new files | new `src/Ygo.Engine/Campaign/Ladder.cs`; new `campaign/ladder.txt`; new `src/Ygo.Harness/LadderTest.cs`; new `src/Ygo.Harness/Arbitrage.Ladder.cs`; `src/Ygo.Harness/Program.cs` (one line, `--ladder-test`; it's not on the busy list, so move it to 1b if it gets taken) |
| W1 | Narrative Designer | The top holder's profile | new `personalities/ladder-top.profile` (text lines, and the data fields with the values section 2a lists) |

- **Chunk 1 is self-contained.**
  - Ladder.cs holds a `LadderState`: the wins and the sitting, with its own `ladwin`/`ladsit` line reader and writer.
  - Every call takes the state explicitly (`Next(profile, state, library)`, `Record(profile, state, ...)`). Chunk 1b only
    has to add `profile.Ladder` and pass it.
  - The ledger kind is `Ladder.LedgerKind = "ladder"`.
  - `Ladder.SettleLoss(profile, state)` is ready for LiveDuels.
  - It calls existing APIs without editing them: `Circuits.PayDuel` (so `Profile.Earn`), `Profile.AddWin`,
    `Leveling`, `Library.FinalCleared`, `Brackets.AlleyRecord`.
- **LadderTest runs under `--ladder-test` straight away.** Its checks 1-10 test `LadderState` directly. Check 11 (the
  scenes) and rung 10's character fail until W1 and W2 land, as intended.

**After chunk G lands (later):**

| # | Agent | Job | Shared files it touches |
|---|---|---|---|
| 1b | Senior Developer | Engine and harness hooks | `Profile.cs` (the `Ladder` field, save and load of the two lines, `Ledger.Ladder` = `Ladder.LedgerKind`); `LiveDuels.cs` (a `Ladder` kind, an optional `Ladder?` on `Settle`); `Posters.cs` (one poster); `Story.cs` (the trigger doc comment only); `src/Ygo.Harness/CampaignTest.cs` (`TriggerOk` takes the five triggers, and it calls `LadderTest`); `src/Ygo.Harness/Arbitrage.cs` (one line) |
| 2 | Unity Architect (after 1b) | App | `CampaignState.cs` (load the Ladder after the Library); `CampaignState.LiveDuel.cs` (pass it to `Settle`); new `CampaignState.Ladder.cs` (`LadderDue`); `CampaignScreen.cs` (shelf entry and dispatch, about 10 lines); `CampaignScreen.Alley.cs` (the `ladder` catch-up beside the finale's); `DuelController.cs` (one line in the finish chain); new `CampaignScreen.Ladder.cs`, new `DuelController.Ladder.cs` |
| 3 | Unity Architect (after 2) | App check | new `DuelController.LadderCheck.cs`; `build/qa-quick/checks.sh` (untracked) |
| W2 | Narrative Designer (after G's header edit) | Scenes | `campaign/story.txt`: the section 6 scenes and the header's trigger list |

- **All the shared files the later chunks touch:** `Profile.cs`, `LiveDuels.cs`, `Posters.cs`, `Story.cs`,
  `CampaignTest.cs`, `Arbitrage.cs`, `CampaignState.cs`, `CampaignState.LiveDuel.cs`, `CampaignScreen.cs`,
  `CampaignScreen.Alley.cs`, `DuelController.cs` and `campaign/story.txt`.
- **The Ladder never touches** `Guests.cs`, `guests.txt`, `CampaignScenes.cs` (it uses `ShowVersus` and `ShowResults`
  as they are) or `DuelController.Guests.cs`.

That's 5 build chunks (1, 1b, 2, 3, and W1 with W2), about one session. Then an independent Code Reviewer, a fix round,
and the harness plus all 15 app checks on one combined build. The shared rules are as in NEXT-BUILD: the Unity lock,
absolute paths, own harness folders, and no deck retuning.

## 11. Deferred, and tuning for later

- **Deferred:**
  - title defences (holders coming back for the top) and any falling;
  - a painted `ladder` place;
  - No Limits peak builds or Remy as rungs, and META-DECKS' rule that the oldest build retires to the ladder;
  - more visitors;
  - a signature gift for the top holder;
  - Ilse's "one deck for the whole sitting" lever;
  - a ladder board on the shop's corkboard.
- **Tuning (with the parked pass):**
  - order the rungs by measured odds for the QA stand-in's best deck;
  - set Ilse's sitting target;
  - choose the visitors' decks (the 2024 bases rate 1612-1675, below the residents);
  - set the DP and prize numbers;
  - confirm the expected duel count;
  - decide whether two Chaos rituals (rungs 7 and 10) is too samey.
- **"Broken" is:** any rung under 30% for a champion's best deck, a climb over 40 duels, or Ilse's sitting under 25%.

## Decisions for the owner (recommendation first)

1. **When it opens:** after the final *and* your first win against Ilse. *Or* with the final, alongside her table.
2. **Falling:** none in v1. A loss keeps your rung, and the stakes come from Ilse's sitting and the visitors. *Or* fall
   one rung per loss, never below a landing on rung 4 or 7 (about 28 duels to the top against 17).
3. **The roster:** the ten in section 2, with Coralie Fenn on Tearlaments at the top. *Or* a new original character at
   the top (a profile and lines, portrait optional, still on the strongest deck).
4. **Ilse's rung:** all four decks in one sitting, restarting on a loss. *Or* one duel on a single deck.
5. **The visitors:** the four Brawl out-of-towners on their 2024 base decks, after the Invitational title, so they stay
   dormant until No Limits ships. *Or* after you meet them at the Brawl, so they're playable now (Junia's "you'll hear
   from us" would then come after you already have).
6. **Rewards:** 800 a rung, and 5,000 DP, 3 tokens and a poster at the top, paid once ever (NG+ leaves the ladder
   alone). *Or* NG+ resets the ladder for a re-climb that pays nothing.
7. **Loaners:** none, your own decks only, as in the final. *Or* loaners at half DP.

## The owner's answers (2026-11-06)

1. Opens after the Every Era final and your first win against Ilse (as recommended).
2. No falling in v1.
3. **The top rung is a new original character on the strongest deck**, not Coralie Fenn.
4. Ilse's rung: all four decks in one sitting, a loss restarts it.
5. The visitors arrive after the Invitational title (dormant until No Limits ships).
6. Rewards once ever; New Game+ leaves the Ladder alone.
7. No loaners.
Build v1 now.

Amended for answer 3 (2026-11-06): the top rung is `ladder-top` on chaos-ritual, the strongest deck legal under the
Gauntlet's list. Sir Lucan and Coralie leave the roster (section 2), the holder's brief is section 2a, and the build is split
around chunk G (section 10).

## The owner's answers after review (2026-11-06)

- The Ladder always plays the Tier 0 Gauntlet's list (TCG September 2026), in open play too. Ilse's guest table keeps
  following the mode.
- Ilse's and Vera's first-win prizes pay once ever (New Game+ no longer repays them); this replaces "each cycle" for Ilse.
- Vera's rematch ships on her circuit warriors deck; a trap-waiting build goes in the tuning pass.
