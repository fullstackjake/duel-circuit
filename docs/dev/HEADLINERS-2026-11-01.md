# Headliners: the Alley's combo decks (2026-11-01)

The owner: challenges are good, but players also want to see the CPU try a crazy combo that today's lists don't allow.
The Alley's splash decks don't do that: they're ordinary decks with a few strong one-card effects, picked because the
bot plays them well (PHASE0-FINDINGS-2026-11-01, "The Back Alley"). This adds a second kind of Alley opponent: the
**headliner**, an authored deck built around one famous banned combo, with a CPU that knows the line and a show around it.

Part of POST-GAME-PLAN-2026-10-31 step 9 (the Back Alley). Phase 1 below is harness-only and can start before it.

## What's there today

- **The engine runs the combos.** 54 combo pieces checked (Yata-Garasu, Chaos Emperor Dragon, Magical Scientist,
  Catapult Turtle, the Frog FTK toads, Mass Driver, Dark Strike Fighter, Trishula, Cyber-Stein, Topologic Gumblar
  Dragon, Halqifibrax...): every one has card data and a script.
- **The bot can't pilot them yet.** `frog-ftk` wins 10% of its rating duels (Elo 1054, last of 90); `gouki` 27%;
  `zoodiac` 23%. None of the three has a combo route.
- **Combo routes already exist.** `Playbook.ComboRoute` plays a starter's line in order, with the picks each step
  wants, and picks up where it is after an interruption (Branded Despia, K9, Mekk-Knight use it). `Playbook.ActiveRoute`
  names the route playing this turn: the show hooks on to that.
- **Going second is already a deck property** (`Playbooks.SecondDecks`, used by the coin toss).
- **Reactions** (`unity/Assets/Scripts/Reactions.cs`) give duelists lines at moments (summon, strike, hit, danger),
  from their profile or by style.

**What "not allowed today" means.** The 2026.10 TCG list (`vendor/LFLists/0TCG.lflist.conf`) forbids Magical
Scientist, Cyber-Stein, Mass Driver, Ronintoadin, Fishborg Blaster, Trishula, Dandylion, Level Eater, Soul Charge,
SPYRAL Master Plan, PSY-Framelord Omega, Topologic Gumblar Dragon, Knightmare Mermaid, Zoodiac Broadbull, Pot of
Greed, Graceful Charity, Wind-Up Hunter and Butterfly Dagger - Elma, and limits Substitoad and Zoodiac Ratpier. Some
famous combo cards are legal again (Firewall Dragon, Yata-Garasu and the errata'd Chaos Emperor Dragon), so a
headliner's combo must need a card that is still banned, or an original text the repo keeps (`chaos-yata` plays the
2004 texts).

## What a headliner is

- **One combo, one deck, one regular.** An authored deck from the event's era, built so its combo comes up most
  games, played by a hooded regular with a chalk handle that names the combo (the plan's handles; no new art).
- **It closes its event, and the other can crash it.** Each event's fourth match is a headliner, drawn from the
  event's two and fixed until beaten, like every Alley opponent. When the event's opponents are drawn, the other
  headliner, if you haven't beaten it, has a 1 in 3 chance of taking one of the first three matches as a **wildcard**
  (seeded, so it's fixed too, and quitting never rerolls it). A headliner you've beaten comes back only as an
  **encore**: a rematch you pick at the same table.
- **A normal coin toss.** Whoever wins it picks, as in any duel; a headliner that wins goes first (its combo wants
  the first turn). So you may face the combo before you've played a card. That's part of the show, with one guard: a
  duel lost on its first turn costs no ante.
- **You're warned.** The versus screen's three show-off cards are the combo's pieces, and Baz says what's coming
  ("Moss runs a Mass Driver. Bring something for the Graveyard.").
- **It plays under its own era's list** (a visitor, like the Library's CPU decks), not the Alley's one-copy list:
  the combo is the point, and most need more than one copy (Phase 1 results).
- **It plays its line at full skill.** The challenge comes from the deck and your answers, not from the CPU
  fumbling. Fizzles come from your interruptions and from the draw.
- **Its stake is a combo piece.** Its binder (the plan's 12 stakes) leads with the banned card the combo turns on,
  so beating Moss can win you a Mass Driver.
- **The payoff:** the deck joins the "Unlisted" Library binder, as every beaten contraband deck does, with a
  **Watch the combo** button (below).

## The roster

Two headliners per event. Elo figures are the bot's today, without a route; they are expected to rise.

| Event | Handle | Combo | Deck | Banned today | Bot today |
|---|---|---|---|---|---|
| Dumpster Row (to 2006) | Yata Mags | Yata-lock: Chaos Emperor Dragon empties both hands and fields, then Yata-Garasu stops your draws | `chaos-yata` (2004 texts) | the 2004 Chaos Emperor Dragon, Witch and Sangan texts; Pot of Greed, Graceful Charity, Magical Scientist | 40%, no route |
| Dumpster Row | Return Rook | Chaos Return: banish a field's worth, then Return from the Different Dimension brings five back for a one-turn kill | new | Return from the Different Dimension, Dimension Fusion | not built |
| The Basement Game (to 2014) | Mass Driver Moss | Frog FTK: Substitoad tributes Frogs, Mass Driver burns 400 each, Ronintoadin and Swap Frog refill | `frog-ftk` | Mass Driver, Ronintoadin, Fishborg Blaster; Substitoad at 1 | 10%, no route |
| The Basement Game | Trishula Trish | Plant Synchro: Glow-Up Bulb, Spore and Dandylion into Trishula's three-card banish | `plant-synchro` (one Glow-Up Bulb and one Spore as built: the headliner runs more) | Trishula, Dandylion | 41%, no route |
| The Black Market (to 2019) | Master Plan Marlo | SPYRAL: Master Plan and Double Helix into a full board in one turn | `spyral` | SPYRAL Master Plan, PSY-Framelord Omega, Gofu | 47%, playbook, no route |
| The Black Market | Gumblar Gail | Knightmare Links: Knightmare Mermaid, Reprodocus and Soul Charge into Topologic Gumblar Dragon ripping your hand | `gouki` | Topologic Gumblar Dragon, Knightmare Mermaid, Soul Charge, Isolde | 27%, no route |

- **Reserves** (built if one above fails its measurements): Zoodiac with three Broadbull (`zoodiac`, Black Market),
  Six Samurai with three Gateway of the Six (`six-samurai`, Basement), Infernity with three Launcher (`infernity`,
  Basement).
- **Nyx's Table only, if they pass the endless-duel check and the goldfish targets:** Magical Scientist + Catapult
  Turtle (a burn kill with no attack), Cyber-Stein OTK, Dark Strike Fighter burn. They can kill on turn 1 after a won
  toss; at the boss table that's the surprise, and the first-turn guard keeps your ante. They leave the exclusion list
  for Nyx's authored decks only.
- **Not headliners:** Wind-Up Hunter's hand loop and Butterfly Dagger - Elma's loop. They stay excluded: there's
  nothing for you to answer, and the bots can't tell when to stop.
- **The plan's "Firewall Fen" handle is retired** (Firewall Dragon is legal on the 2026.10 list). "Three-Pot Teddy"
  and "Cyber-Jar Wick" stay splash regulars.

## The show

- **"Going for it."** When a headliner's route starts, a banner names it ("MOSS IS GOING FOR IT: FROG FTK"), a sting
  plays, and Baz calls it.
- **Slow down for combos** (setting, default On): while a route plays, the duel runs at Normal speed even when you've
  set it faster, so you can follow it. Off keeps your speed. Reduced motion drops the banner animation, not the
  banner.
- **The combo meter:** a small panel beside the chain shows the route's progress ("step 6 of 11") and its running
  total: summons, cards banished, burn damage, whatever that combo counts.
- **The payoff beat:** each route marks its payoff step (Chaos Emperor Dragon resolving, the first Mass Driver
  shot, Gumblar's hand rip). The camera holds and the callout plays at chain size.
- **Fizzle:** when a route can't go on (you interrupted it, or a piece is missing), the meter shows where it
  stopped ("fizzled at step 5"), Baz and Mika react, and the crowd groans. Stopping a combo should feel as good as
  surviving it.
- **Commentary:** new reaction moments `combo`, `payoff` and `fizzle`. Baz (MC) and Mika (streaming) get lines per
  headliner; the headliner gets its own `combo` and `fizzle` lines in its profile.
- **Combo Breaker posters:** stopping a headliner's route before its payoff, and going on to win, earns that
  headliner's poster (the existing posters system). One per headliner, six in all.
- **Watch the combo:** the Unlisted binder page plays the headliner's best clip, its route done in full against an
  empty board (a replay recorded by the goldfish run below). Players who want to see the combo can see it without
  losing to it.
- **The exhibition (watch-only):** Baz's chalkboard runs a live CPU-vs-CPU duel any time: two headliners you've met,
  or a headliner against a splasher, picked by you or drawn at random. Baz and Mika commentate, and the banner, meter
  and fizzle play as in your own duels. No bets, no DP, no stakes and no posters: it's only for watching. It opens with
  the first headliner you meet. It uses the duel view with both seats played by bots, as the in-app checks already do.

## Engine and harness work

- **Route signals:** `Playbook` raises route started (name, step count), step done, payoff and ended (finished,
  stuck) for the duel controller. Today a route ends silently, and stuck steps are skipped.
- **Route counters:** each route names what its meter counts (summons, banishes, damage) and which step is the
  payoff.
- **Playbooks:** new ones with routes for `chaos-yata`, `frog-ftk`, `plant-synchro` and `gouki`; a route added to
  `SpyralPlaybook`; a new deck and playbook for Return Rook.
- **`--combo-goldfish <deck> [--games N]`:** the headliner against a duelist that only passes. Reports how often the
  route starts by its third turn, how often it finishes, the turn of the payoff, and how often it wins that turn. It
  saves the best finished duel as the Watch-the-combo replay.
- **`--combo-interrupt <deck>`:** the same against a stand-in that holds one era-fair answer (a hand trap, a Trap or a
  negation, era by era). Reports fizzle rates per answer, so Baz's tell names one that works.
- **The existing checks:** `--circuit-study` at each headliner's match (win rate with the best option),
  `--endless-test` with headliners in the pool, `--alley-rate` unchanged for the splashers.

## Targets

| Measure | Target | Why |
|---|---|---|
| The route starts by the CPU's third turn (goldfish) | 50% or more | The show has to happen early (was 80% by the second turn: out of reach, Phase 1 results) |
| The route finishes, unopposed | 55-75% | It should land often, and fail sometimes even with no interruption |
| The player's best option wins (QA stand-in) | Dumpster Row 70%, Basement 60%, Black Market 55% | A closer, not a wall: 5 points under each event's target |
| ...when the headliner wins the toss | 40% or more | With a normal toss, losing it mustn't mean losing the duel |
| A wildcard (a headliner in matches 1-3) | the same targets as the closer | It turns up unannounced, so it can't be the harder of the two |
| An answer the tell names stops it (`--combo-interrupt`) | 60% or more | The warning has to mean something |
| Headliner duels past turn 40 | under 2% | Yata-lock's 200 damage a turn must not run into the cap |

**Go or no-go per combo:** if a route still finishes under 40% of goldfish games after a day of tuning, that combo
is too long or too branchy for the bot, and a reserve takes its slot.

## Phases

| Phase | What | Done when |
|---|---|---|
| 1 | Routes for Yata Mags and Mass Driver Moss, route signals, `--combo-goldfish` | Both meet the goldfish targets, or fail the go/no-go |
| 2 | The show layer: banner, slow-down setting, meter, payoff beat, fizzle, commentary moments | `-campaigncheck` plays one headliner duel through its payoff and one through a fizzle |
| 3 | The other four headliners, `--combo-interrupt`, wildcards and encores, the first-turn ante guard, Combo Breaker posters, Watch the combo, the exhibition | Every headliner meets its targets |

Phases 2 and 3 need the Alley itself (step 9). Phase 1 needs only the harness, so it can show before then whether
the bot can make a combo worth watching.

## Phase 1 results (2026-11-01)

**Built:**
- **Route signals:** `Playbook.RouteSignaled` (started, each step, payoff, ended finished or stuck). `.Payoff()` marks
  a route's payoff step.
- **Two route options for loop decks:** `.AnyDecision()` lets a route start later in the turn, when a draw spell finds
  the piece. `.Waiting()` waits out the decisions its rules play, and ends with the turn. Today's routes end at the
  first decision with no step on offer.
- **For playbooks:** `HoldNormalSummons(when)` keeps the turn's Normal Summon (or Set). `PlaybookView` adds
  `NormalSummonUsed` (counted in `DuelContext`) and both players' LP. `ActionsPerPhase` stays 30 by default, and the
  FTK sets 120.
- **Decks and tools:** `--combo-goldfish <deck>`; `decks/headliners/mass-driver-moss.ydk` and `yata-mags.ydk` (they
  play as `frog-ftk` and `chaos-yata` through `#recipe`); `FrogFtkPlaybook` and `YataLockPlaybook`.
- **A bot limit found:** a cost is paid before its card is on the chain, so the bot can't tell which card asks, and a
  preference tied to one card doesn't apply to its costs. The Frog FTK's Tribute and banish preferences apply whichever
  card asks.
- **No change for other decks:** the turn-1 boards of ten playbook decks (`--turn1`, 12 games each) are identical before
  and after. The Yata-lock playbook doesn't weaken `chaos-yata` as an ordinary deck (`--playbook-ab` against
  goat-control, warriors, teledad and gadgets, 400 duels: 43% on, 41% off). The Unity build compiles.

**Goldfish results** (400 games each, half going first, two seeds):

| Headliner | Starts by its turn 2 (3) | Finishes (by its turn 3) | Wins on the payoff turn | Wins by its turn 3 |
|---|---|---|---|---|
| Mass Driver Moss | 41% (54%) | 62% (36%) | 56% | 35% |
| Yata Mags, April 2004 list | 1% (1%) | 3-4% (1%) | 3% | 6% |
| Yata Mags, no list (3 Chaos Emperor Dragon, 3 Sangan, 3 Witch, 2 Yata) | 22% | 33% | 33% | 20% |
| Yata Mags, the wipe as payoff, April 2004 list | 11-14% (12-15%) | 20% (9-10%) | 4-6% | 6% |
| Yata Mags, the wipe as payoff, 3 Chaos Emperor Dragon (as built) | 23% (27-28%) | 40-49%, 42% at 1,200 games (18-24%) | 14-17% | 7-9% |

- **Mass Driver Moss: go.** Finishing is in range. Once the loop is on, it kills that turn 9 times in 10 (single
  games with seeds 8, 13 and 25 win on the duel's first turn). It meets the start target (54% by its third turn), but not the first draft's 80% by the second: Mass Driver had no
  search in 2010, and with three copies it's in hand by the start of the deck's second turn about 40% of the time. Upstart Goblin for more draw backfired: each one
  gives the opponent 1000 LP, and wins fell to 53%.
- **Yata Mags: no-go under its own list.** The lock needs Chaos Emperor Dragon, Yata-Garasu and Sangan or Witch, and
  the April 2004 list has each at one. The line works when it comes together (seed 37: the wipe, Witch finds Yata, and
  the lock ends the duel on the deck's sixth turn). With no list it's 33%, still under the go line.
- **The start target is now 50% by the third turn** (the owner, same day). The first draft's 80% by the second turn
  can't be met by a combo that needs one card no search can find. Moss meets the new target (54%). Yata Mags doesn't
  (27-28%) and is accepted as Dumpster Row's slower, rarer show: the wipe still comes in about 43% of whole duels,
  and the 40% finish line is what decides whether a combo stays a headliner.

**The wipe as Yata Mags's payoff (the owner's choice, same day).** A second route, "Chaos Emperor wipe", plays when
the lock can't come together this turn or next: Graceful Charity or Card Destruction for the LIGHT and DARK, Chaos
Emperor Dragon, and the wipe once the opponent has three cards or more to lose. Sangan or Witch, if they're on the
field, still find Yata. The lock route comes first and has the same payoff.
- Under the April 2004 list it finishes 20% (both seeds). When Chaos Emperor Dragon is in hand the wipe lands about
  two times in three (65-70 of about 100 starts); the limit is the one copy, which a goldfish game often never draws.
- With three Chaos Emperor Dragons, the only card over the list, it finishes about 42-44%: 40-49% over five 400-game
  runs, and 42% at 1,200 games. That's over the go line and under the target. Six to seven in ten lock attempts
  finish too. **The owner took the three copies** (same day): Yata Mags is the one headliner over its era's list.
- The runs' spread is wider than 400 games suggests (two orders of the same 41 cards measured 41% and 49% on one
  seed, and both 42% at 1,200 games). The opening hands are fair: Chaos Emperor Dragon starts in hand 31-34% of
  1,200 games, against 33% expected. Compare decks over 1,000 games or more, or over several seeds.

**At 4,000 games** (the overnight run, seed 11, half going first):

| Headliner | Starts by its turn 2 (3) | Finishes (by its turn 3) | Wins on the payoff turn | Wins by its turn 3 |
|---|---|---|---|---|
| Mass Driver Moss | 43% (58%) | 63% (37%) | 58% | 36% |
| Yata Mags | 25% (30%) | 43% (21%) | 14% | 8% |

Both hold their 400-game numbers. Yata Mags's starts are the wipe route four times in five (1,819 of 2,257).
Against the whole deck field (`--rate-decks`, 300 rounds), Mass Driver Moss wins 29% (Elo 1275), level with frog-ftk's
28%, and Yata Mags 39% (Elo 1402) to chaos-yata's 46%. The field is mostly modern decks, so these numbers say little
about the Alley. The first rating had Moss at 10%: the rater looked a playbook up by the deck's name, and a headliner
plays by its `#recipe`. The rater and the other harness tools now look it up from the deck.

**A rule settled while building:** a headliner plays under its own era's list, as a visitor (the Library's CPU decks
already do), not the Alley's one-copy list. The one exception is Yata Mags's three Chaos Emperor Dragons. Under the Alley list, Mass Driver Moss would have one Mass Driver and one
Ronintoadin, and no FTK.

**Decided after Phase 1 (the owner):**
1. **Yata Mags:** the wipe is its payoff, with three Chaos Emperor Dragons (about 43%).
2. **The start target:** 50% by the deck's third turn.

## Decisions (the owner, 2026-11-05)

- **Return Rook ships with the smaller show:** the payoff becomes a two-monster return, replacing the full route (34-36%,
  no-go). As built (2026-11-05): the route counts only monsters that can come back (Chaos Sorcerer, Black Luster Soldier
  and Dark Necrofear only when properly summoned), and a payoff bringing fewer than two home doesn't count as finished.
  `--combo-goldfish return-rook --games 1000`: finishes 51% (seed 11) and 55% (seed 12), 34-37% by his turn 3, payoff on
  turn 3.1; over the 40% go line. Ready, with a Combo Breaker poster (Return from the Different Dimension) and a clip.
- **Gateway Gwen is approved as written:** name, title, lines, look and her three scenes.
- **Gumblar Gail's tell stays general:** it names no card (Solemn Strike stops her best, but the tell gives advice any
  deck can act on).

## Decisions (the owner, 2026-11-01)

1. **Placement: both.** A headliner closes each event, and the other can turn up earlier as a wildcard until you beat
   it. (Recommended was closers only.)
2. **Turn order: a normal coin toss.** (Recommended was always second.) Added with it: a duel lost on its first turn
   costs no ante, and a target for when the headliner wins the toss.
3. **The burn kills: in at Nyx's Table** if they pass the endless-duel check and the goldfish targets.
4. **Betting: none, but a watch-only exhibition.** Live CPU-vs-CPU headliner duels with commentary, and no stakes.

## Phase 3 results (2026-11-02, engine, data and harness)

**Built** (the API for the show: ALLEY-SPEC Phase D, "As built (Phase D, engine)"):
- **Route meters and the payoff index:** `Route.Counts(name, value)`, and every route signal carries `PayoffStep`,
  `Counter` and `Count`; a new `RouteEvent.Count` when the meter moves between steps; `Playbook.Routes` lists the routes
  before they play. Frog FTK and the Yata routes count damage.
- **Combo Breaker:** `ComboWatch` (`Broken`; with the duel's context, `Stopped`: the opponent acted during the fizzled
  route) and `BackAlley.Record(comboBroken:)` → `AlleyResult.ComboBreaker`, "combobreaker:<id>", `HeadlinerState.Breaker`,
  one COMBO BREAKER poster per ready headliner (`Posters.BreakerFor`). Never for an encore.
- **Watch the combo:** `--combo-goldfish <id> --save-replay` keeps the best finished duel (won on its payoff turn,
  soonest), checked to play back the same, as campaign/replays/<id>.txt (`HeadlinerReplays`). Clips for all five ready
  headliners: Yata Mags (seed 87287, the lock, won on duel turn 3), Mass Driver Moss (87121, the turn-1 FTK), Gateway
  Gwen (87274, Shi En on turn 1, won on duel turn 2), Master Plan Marlo (87302, won on duel turn 4), Gumblar Gail (87291,
  won on duel turn 5).
- **`--combo-interrupt <deck> [--games N]`:** the goldfish's seeds against a stand-in that plays as the dummy and answers
  as the skill-1.0 bot, holding one era-fair answer (in hand, or Set before the duel). Per answer: how often the stand-in
  used it, how often the headliner's **first try** fizzles (the show's fizzle), the duel's payoff rate, how often it
  **never lands** (of the duels it finished unopposed), and the headliner's wins.
- **Headliners on their own recipe:** `#recipe <id>` (the playbook goes by the headliner's id, in the app's duel too) and
  `#rating <deck>` (the panel's Elo). The base decks play as before: no playbook for plant-synchro or gouki, spyral's and
  six-samurai's playbooks without the headliners' routes (`--turn1` boards of twelve playbook decks identical before and
  after, spyral and six-samurai among them).
- **Profiles:** `combo:`, `payoff:` and `fizzle:` lines are now read (they were ignored); five new profiles. story.txt has
  every hl-* scene and every -breaker scene; alley-voice.txt Gwen's tell.
- **Checks:** src/Ygo.Harness/HeadlinerTest.cs in `--campaign-test` (also `--headliner-test` alone): decks, routes,
  meters, legality, base decks unchanged, Elo, profiles, scenes, tells, posters, Record's comboBroken (win, loss, plain
  win, encore, save), ComboWatch, and each ready headliner's clip and route signals in a real duel; every ready
  headliner route has a payoff step; and a negated payoff in a real duel (a stand-in plays its one answer only on the
  payoff: Solemn Warning on Gwen's Shi En, Ash Blossom on Marlo's Master Plan search) is a fizzle, not PaidOff, and
  `Stopped`; ComboWatch's window (a link on the next turn doesn't count; the duel ending with a route you interrupted
  still live does).

**Goldfish** (2,000 games each: seeds 11 and 12, 1,000 a seed, half going first; Yata Mags and Mass Driver Moss at seed 11):

| Headliner | Deck (list) | Starts by its turn 3 | Finishes (by its turn 3) | Payoff turn | Verdict |
|---|---|---|---|---|---|
| Return Rook | return-rook, new (April 2005) | 24% | 34-36% (16-19%) | 3.9 | **no-go** (under 40%) |
| Trishula Trish | trishula-trish (March 2011) | 21-24% | 29% (19-21%) | 3.3 | **no-go**: Gateway Gwen takes the slot |
| Gateway Gwen (reserve) | gateway-gwen, six-samurai with three Gateway (March 2010) | 58-59% | 49-52% (48-50%) | 1.4 | **go** (under the 55% target, over the go line) |
| Master Plan Marlo | master-plan-marlo, spyral as built (November 2017) | 83-84% | 67% (55-56%) | 2.2 | **go**, on target |
| Gumblar Gail | gumblar-gail (September 2018) | 49-51% | 74-77% (34-37%) | 4.3 | **go**, at the top of the range |
| Lab Coat Lyle | scientist-turtle, new (April 2004, the 2004 texts) | 8-10% | 55% (4-6%) | 9.7 | **no-go** for Nyx's seat 3 |
| Mass Driver Moss (Phase 1) | | 58% | 63% | 3.8 | unchanged |
| Yata Mags (Phase 1) | | 30% | 41% | 5.0 | unchanged |

- **Return Rook:** Dimensional Fissure (or Banisher of the Light) banks every Foolish Burial and discard; Chaos Sorcerer,
  BLS, Dark Necrofear, Bazoo and Soul Release bank the rest; Return from the Different Dimension (Set a turn ahead) or
  Dimension Fusion brings three or more back in Main Phase 1. Three builds were tried (Chaos only 8%, Chaos with Fissure
  36%, Fissure only 33%); Soul Release now banks five (`TakeAll`), and Dimension Fusion waits while it would return more
  of theirs. With a **two**-monster payoff it finishes 48-52% (starts 38-40%): that's the owner's call, since the
  spec's show is "a field's worth". Against the dummy 12-13% of its goldfish duels run past turn 40 (it has no removal
  for the dummy's walls). Dumpster Row has no listed reserve: it keeps one headliner (no wildcards there).
- **Trishula Trish:** Debris Dragon + Dandylion (both limited in March 2011), a Tuner from the hand or the GY for the rest
  of nine, and a catch-all route for any Trishula the rules make. Nine Levels need too many specific pieces; holding the
  other Synchros for it made the deck weaker (it wins 85% of goldfish games, and 11% pass turn 40). Under 40%: **its
  reserve, Six Samurai with three Gateway of the Six, takes the slot** as Gateway Gwen (handle, lines and poster new:
  for the owner and ALLEY-STORY to confirm). Gwen's route: the Gateways and United up, Smoke Signal or Reinforcement for
  Kageki, Gateway's counters for Kagemusha if needed, Kageki brings Kagemusha, Shi En (the payoff); Pot of Duality for
  three Double-Edged Sword Technique took it from 44% to 49-52%.
- **Master Plan Marlo:** a route on `SpyralPlaybook(masterPlan: true)`: Resort, Super Agent on its guess, Double Helix,
  Double Helix calls SPYRAL Master Plan from the Deck (`ChooseOption`: summon it, not add it), Master Plan's MISSION
  search (the payoff). Two Master Plans finished 80% (over 75%): the as-built one copy is 67%.
- **Gumblar Gail:** a waiting route: Suprex (or Riscorpio, Headbatt, Instant Fusion, Malicious) for bodies, a Knightmare
  Link-2 from two Goukis when Re-Match can bring them back, Topologic Gumblar Dragon (the payoff, four material's worth).
  Elder Entity N'tss and Gem-Knight Seraphinite replace Invoked Raidjin and Binary Sorceress (the as-built Instant
  Fusion had no target). 74-77%: at the target's top.
- **Lab Coat Lyle:** one Magical Scientist (April 2004), three pre-errata Catapult Turtles (no limit), every revival for the
  Scientist, fifteen Level 6 Fusions. When the lab comes together it lands and kills (48-50% of goldfish duels win on the
  payoff turn), but the one Scientist makes it late (its turn 9-10 on average) and 10-11% of its goldfish duels pass
  turn 40. It fails the start target badly and the turn-40 check against the dummy: Nyx's seat 3 keeps its stand-in.

**Interruptions** (`--combo-interrupt`, 300 duels an answer, seed 11; "1st try" is the share of first routes that fizzle,
the show's fizzle; unopposed in brackets). Rerun 2026-11-02 after the review's fix (a payoff counts only once it
resolves: a negated payoff is a fizzle at its step):

| Headliner | Answers: 1st try stopped / never lands |
|---|---|
| Gateway Gwen (19%) | Solemn Warning 91% / 42%, Effect Veiler 72% / 41%, Bottomless Trap Hole 23% / 6%, MST 22% / 10%, Solemn Judgment **93% / 75%** (was 21% / 4%) |
| Master Plan Marlo (69%) | Ash Blossom 77% / 25%, Ghost Ogre 83% / 35%, Droll & Lock Bird 83% / 27%, Effect Veiler 71% / 17%, Solemn Strike 90% / 24% |
| Gumblar Gail (48%) | Ash Blossom 49% / 12%, Droll 47% / 9%, Ghost Belle 49% / 8%, Infinite Impermanence 54% / 7%, Effect Veiler 51% / 10%, Solemn Strike 63% / 10% |
| Mass Driver Moss (41%) | Macro Cosmos 86% / 83%, Solemn Warning 47% / 34%, Effect Veiler 40% / 26%, Bottomless 39% / 10%, D.D. Crow (never used by the bot) |
| Yata Mags (30%) | Solemn Judgment 59% / 54%, Magic Jammer 44% / 43%, Torrential Tribute 41% / 47%, Ring of Destruction and D.D. Crow (never used) |

- **The fix's moves:** Solemn Judgment against Gwen was counted as a payoff when it negated Shi En's Synchro Summon (the
  payoff was signaled as it was chosen): it stops her first try 93% and she never lands in 75% of the duels she finishes
  unopposed, the best era answer to her. Yata Mags's unopposed first-try fizzle rate is 30% (was 29%): 4 of 300 goldfish
  duels chose Chaos Emperor's wipe and then skipped it (the blunder check, 1000 LP to pay with 1000 left), which counted
  as a payoff before. The others are unchanged (their answers stop earlier steps). Unopposed finish rates: Gwen 54%,
  Marlo 69%, Gail 71%, Moss 64%, Yata Mags 41%.

- **The tells:** Moss's ("something for the Graveyard": Macro Cosmos, 86%) and Gwen's ("stop the first summon": Solemn
  Warning 91%, Effect Veiler 71%) name answers that work. Marlo's ("stop the first search": Ash Blossom 77%) meets 60%,
  but its first try fizzles 69% unopposed (Super Agent's and Double Helix's guesses), so Ash adds little. Gail's names no
  card; the best era answer is Solemn Strike at 63% (the route comes back next turn: under 12% never land). For
  ALLEY-STORY: Gail's tell could name Solemn Strike; Yata Mags's Solemn Judgment.
- **Combo Breaker and the draw:** unopposed, Marlo's and Gail's routes often fizzle on their own and come back, and some
  never land (Marlo 31% of duels). `ComboWatch.Stopped` (a fizzle during which the opponent activated something, in the
  route's own turn; the duel ending with one live and acted against counts too) is the poster's test; the show layer
  passes it.

**Endless check and the rest:** see "Phase 3 checks" below.

**Phase 3 checks:** the harness builds with 0 errors. `--endless-test --games 6000 --seed 2` with the five ready
headliners in the pool (250 duels each against splash decks): prompt cap 0%, decided at turn 40 0% for every headliner
(0.6% of all 6,000), draws 0%; their win rates against the alley's splash regulars: Yata Mags 30%, Mass Driver Moss
32%, Gateway Gwen 34%, Gumblar Gail 29%, Master Plan Marlo 51%. `--arbitrage --load build/qa-night5/mods`: 0
DP-positive sequences. `--campaign-test`: every headliner check passes; two of AlleyTest's checks assume Phase A's
headliners and fail now that three more are ready (its data check wants exactly 2 ready headliners; its binder check
wants 9 regulars over three tables, and a wildcard now takes one seat): AlleyTest's owner relaxes both.
