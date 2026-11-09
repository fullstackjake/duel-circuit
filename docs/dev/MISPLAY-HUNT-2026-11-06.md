# Misplay hunt (2026-11-06, third batch, chunk M)

Plan: [NEXT-BUILD-2026-11-06c.md](NEXT-BUILD-2026-11-06c.md), chunk M. The CPUs stay at full strength: every fix below
only makes a bot play better, behind its own toggle (default on) for the paired A/B. No deck, rating or skill changed.
Card versions phase 3.1 (base-code playbook matching) landed here too (owner decision 8 of the tuning plan).

## 1. What ran

| Run | What | Kept / analyzed |
|---|---|---|
| `--hunt-audit` (new: `src/Ygo.Harness/MisplayHunt.cs`) seed 1 | The audit's blunder watchers over **every character with every deck it plays** (118 entries from 112 characters: Ilse's four, Rue's Brawl deck, the Ladder rungs, the No Limits builds). Planned 1,500 games; stopped early (the machine was at load 40-110 from parallel work), after roughly 900 | 298 flagged games kept (`--keep flagged`) |
| `--hunt-audit --only <post-game decks>` seed 2 | 250 games, one seat drawn from the 28 post-game decks (Ladder rungs, Ilse's four, Invitational visitors, No Limits builds and Brawl decks), the other from everyone | 75 flagged games kept; 182 findings |
| `--analyze-replay` (full strength, planner) | 224 of the 298 roster games, all 75 post-game games, and the owner's 31 replays (copies in `build/analysis/input/owner`), plus `--as-played` on the owner's to tell a low-skill character's deliberate slips from its real choices | `build/hunt/analysis/{snap1,post,owner,owner-asplayed}` |

`nl-maliss` (Margo's deck) has no `.ydk`, so it wasn't played. `--round-robin` and `--circuit-study` don't take
`--keep-replays` (only the audit and the alley studies do), which is why the gathering run is a new audit variant.

## 2. Triage

Reviewed: the owner's top 45 decisions, the roster audit's top 50, the post-game audit's top 40, and every high flag
(missed lethal, lost to own card, decked out) plus the negated-own-effect and removal flags.

**How to read a disagreement.** The analyzer judges every CPU at skill 1.0 with the planner. A character below 0.9
plays without the planner (the heuristic bot), and every character takes a deliberate "beginner's slip" on (1 - skill)
× 60% of its judgement calls. `--as-played` reproduces the owner's games to 99.8% of decisions, and an empty reason line
there marks the slip. So most top-ranked disagreements are the skill design working, not bugs.

| Class | Owner (45) | Roster audit (50 + 18 flags) | Post-game (40 + 2 flags) | Total |
|---|---|---|---|---|
| **Real misplay** (root cause in the bot) | 3 | 3 | 1 | **7** |
| Skill design: a low-skill slip, or the heuristic bot where the planner would play otherwise | 18 | 31 | 26 | 75 |
| Judgement call (the planner's one-turn board score prefers another line, or the deck plays it on purpose) | 12 | 3 | 0 | 15 |
| Analyzer noise (see section 5) | 9 | 29 | 14 | 52 |
| Already fixed (the replay predates the 2026-11-06 removal-timing fix) | 3 | 0 | 0 | 3 |
| Open (needs a deck expert, or a fix outside this pass) | 0 | 2 | 1 | 3 |

Plus one real misplay from the last review (B.E.S. / Gladiator Beast battle triggers), which the audit can't see
because no roster deck plays those cards.

## 3. The real misplays, by root cause, and the fixes

| # | Root cause | Evidence | Fix (toggle) | Regression test |
|---|---|---|---|---|
| 1 | **A Quick-Play that only Sets cards from the Deck was held forever.** `MainPhaseActivationScore` holds every Quick-Play with no trait for the opponent's turn; Set Rotation's "Set 2 Field Spells ... from your Deck" isn't a search. The planner's rollouts play the same heuristic, so a line that starts with a summon never saw Set Rotation either | Owner, Juniper Pell #243 (2026-11-05 1634): the bot Normal Summoned Robina; the planner rated Set Rotation first 5,297 higher (after the fix: 868, a judgement call between two good lines) | A Quick-Play whose text opens with "Set N ... from your Deck" scores 90 in the Main Phase, **only while our Field Zone is empty** (review: a Field Spell Set over our own face-up one sends ours to the GY: Nightmare Throne, Necrovalley, the Map) (`UseDeckSetSense`; only Set Rotation in the card pool) | `--misplay-hunt-test`: Set Rotation, then Map; not over a face-up Map |
| 2 | **Spells that return cards from the GY to the hand weren't advantage.** The Warrior Returning Alive, Salvage, Fusion Recovery... had no trait, so they scored 50 and a Normal Summon went first | Owner, Vera #99 (2026-11-02 2354): Normal Summoned Gearfried instead of fetching Goblin Attack Force; the planner +2,880 | New `Trait.Recover` (first sentence: "...in your GY; add it to your hand" / "from your GY to your hand", **and no cards paid from the hand first**: Magical Stone Excavation, Spell Reproduction, Monster Reincarnation are out), outside `Trait.Advantage`, so deck building, the alley generator, card search and every Deck-thinning or "search" check read nothing new. After the review it changes **one thing only**: a Normal/Continuous/Field Spell with it scores 95 in our Main Phase (`UseRecoverySense`, `RecoversNow`). Traps and Quick-Plays keep their timing; the playbook gate, chain windows, GY effects and card values read the plain Advantage trait as before. Four roster decks hold one (warriors, mass-driver-moss, frog-ftk, blitzclique) | `--misplay-hunt-test`: WRA, then Goblin Attack Force; a playbook rule still goes first; Quick Charger (a Quick-Play) stays held; the trait on WRA and Salvage, not on Excavation |
| 3 | **Optional Spells before battle gave an on-activation Quick Effect its window.** S:P Little Knight ("When your opponent activates a card or effect: banish 2 face-up monsters") answered an optional Spell by banishing Blue-Eyes, so nothing attacked | Owner, Old Gus #114 (2026-11-02 2058): Stamping Destruction first; the planner rates attacking first +3,800 | When the opponent shows a face-up, unnegated monster whose Quick Effect answers any activation by removing a monster, and **that card** hasn't used an effect this turn (by card identity: a second copy keeps its window; `DuelContext.MonsterActivatedThisTurn`), and one of our attackers has an attack worth making, Spells and Traps the attack doesn't need wait for Main Phase 2. **Three cards** (Little Knight, Commander Souza, Tellusion; review: Red Nova Dragon/Assault Mode's attack-declaration answer isn't dodged by attacking first, so it's out). Monster removal, wipes, negation, summons, Equips, Field Spells, ATK pumps, and **Spell/Trap removal while the opponent has a set card** still go first: so Old Gus's own Stamping into a set Infinite Impermanence now goes first, and the fix covers optional plays like Trade-In. Only the heuristic bot (CPUs below 0.9, and the planner's rollouts); the planner's own choice is untouched (`UseActivationWindowSense`) | `--misplay-hunt-test`: attack, then Trade-In, against Little Knight; Stamping first against a set card; and the control (no Little Knight: Trade-In first) |
| 4 | **The planner skipped lethal for a later win.** A direct attack's trap-risk charge broke a tie between two winning lines in favour of the later one (a burn in Main Phase 2, an End Phase effect), which the opponent's hidden cards can still stop | Roster audit: Count Aurum (eldlich, 1.0) game 1220 #315, Augie Marr (nl-vanquish-soul-brawl, 1.0) game 1090 #301, Anselm Quist (nl-mitsurugi, 1.0) game 1355 #581 (all "missed lethal"; Count Aurum's later win slipped a turn) | **Reworked after the review** (zeroing the attack's charge broke the planner's bait ordering, and read lethal from printed ATK): every attack keeps its charge; when a direct attack's own rollout wins, stopping (Main Phase 2, the End Phase) is charged as much as the cheapest such attack (`TurnPlanner.LethalRiskSense`). After the fix the shadow attacks in all three | `--battle-misplay-test` (the planner): Blue-Eyes into 500 LP over Solar Flare Dragon's burn; Yaiba Robo baits Sakuretsu Armor before Blue-Eyes wins; Cyberdark Edge's halved direct attack isn't lethal (it keeps its charge, stopping gets none) |
| 5 | **Self-damage worded without "you" wasn't a cost.** "Special Summon this card, and if you do, take 400 damage" | Post-game audit, Fern Vasquez (plant-synchro, 0.82) game 34: lost to her own Blackwing - Zephyros the Elite | `Drawbacks.SelfDamage` reads "(if you do / then / and) take N damage" (21 cards, all genuine self-damage) into the LP-cost check (`UseSelfDamageSense`; the planner's rollouts take `TurnPlanner.SelfDamageSense`). "take N damage for each ..." reads N, a lower bound (the count isn't known ahead) | `--misplay-hunt-test`: no Zephyros at 300 LP |
| 6 | **Attacks that pay off through a battle trigger weren't valued** (from the last review). A B.E.S. loses a counter each battle (and the battle memory marked it a "survivor" after one); a Gladiator Beast tags out after battling | Review; no roster deck plays them (Lightsworn's Extra Deck has Gyzarus, with no Gladiator Beast to bring) | A B.E.S. ("if this card battled: Remove 1 counter ... If you cannot, destroy it") is worth value/(counters + 1) for each attack our attacker survives, and isn't marked a survivor; a tag-out attacker ("At the end of the Battle Phase, if this card battled ... Special Summon "X" from your Deck") makes a free attack (no damage back) worth 400 while the Deck holds an X (`UseBattleTriggerSense`) | `--battle-misplay-test`: B.E.S. Big Core three times (3 counters), and with no counter; Laquari into a tie for the tag-out, not into a higher DEF, not with no Gladiator Beast left |
| 7 | **Card versions phase 3.1: playbooks matched exact ids** (F8). An alt art or an original/GOAT text slipped past every playbook rule about its card | Decks holding a card whose base matches a playbook entry by another id (the Ash Blossom alt art, the GOAT-era decks' version ids) | Playbook rules, vetoes, picks, chain rules, bosses, own-targets, option and hint descriptions and every `PlaybookView` count compare base codes on both sides; the 11 playbook entries written with version ids still see the current card (`Playbook.UseBaseCodes`). Effect descriptions keep the card's own script id. **Review bug, fixed:** the card database also aliases different cards "always treated as" another (47 of them: Fallen of the White Dragon to Fallen of Albaz, Power Bond (Anime) to Polymerization, Pacifis to Umi), which the first version matched (Albaz in hand counted as the White Dragon in branded-despia, nl-branded and chaos-ritual). Now a base code counts only when the names agree up to a trailing "(Pre-Errata)", "(Anime)"... tag | `--misplay-hunt-test`: a playbook holding Vorse Raider's Normal Summon holds the alt art's; Albaz isn't held by a White Dragon hold; the playbook view counts the alt art, not Albaz |

## 4. Paired A/B

The pilot plays the same seed with the fix(es) and without; its opponent always has them. The error per deck is the
square root of the duels that changed winner (only those move the difference). Turn-one partings: duels whose two
versions first part on turn 1.

**After the review's fix round** (`build/bin/hunt-ab2`, `build/hunt/ab2/*.txt`), one fix at a time on the decks it reaches,
24 paired duels each, then every fix together over the whole roster:

| `--fix` | Decks (pilot) | Against | Paired | Fixed / before | Moved > 2 errors | Turn-one partings |
|---|---|---|---|---|---|---|
| basecodes | branded-despia, nl-branded, chaos-ritual | the roster | 72 | 67 / 67 | none | 0 (all 72 identical) |
| deckset | yubel, nl-yubel, nl-yubel-brawl, exosister, floowandereeze | the roster | 120 | 53 / 55 | none (exosister and floowandereeze net -1, error 1) | 5 (floowandereeze 3, yubel 1, exosister 1: Set Rotation on turn 1) |
| lethal | eldlich, nl-mitsurugi, nl-vanquish-soul-brawl, thunder-dragon, kashtira | goat-control, stall-burn, true-draco, gadgets (set cards) | 120 | 112 / 112 | none | 0 |
| recovery | warriors, mass-driver-moss, frog-ftk, blitzclique (every roster deck with a Recover card) | the roster | 96 | 39 / 39 | none | 0 |
| window | blackwing, warriors, plant-synchro, classic, exosister, geargia | snake-eye, k9-vanquish-soul, yubel, mitsurugi, tenpai-dragon (Little Knight decks) | 144 | 5 / 4 | none | 0 |
| **huntall** | **every roster deck (100)**, 12 each | the roster | **1,200** | **742 / 741** | **none** | 3 (exosister, mass-driver-moss, nl-yubel) |

The deck-set run's two -1s are the same two duels as in the first run (exosister game 19 vs Patron Orvel, floowandereeze
game 18 vs Nerio), each one changed duel against an error of 1, so neither blocks. In the whole-roster run 1,192 of
1,200 duels played identically and one changed winner (frog-ftk game 4 vs Pip Halloran: the fixed CPU won).

**For the tuning pass: no deck's A/B moved by more than two errors in any run**, so nothing here calls for a re-rate on
its own.

**The first run** (before the review, superseded): `--fix huntall` over the roster at 24 duels each, 2,400 paired, 1,075
against 1,076, no deck moved (`build/hunt/ab-huntall.txt`). Its turn-one partings in branded-despia and nl-branded came
from the "always treated as" cross-match (Fallen of Albaz read as Fallen of the White Dragon), not from a recovery
card: Branded in Red is in no deck. With that fixed, base codes part nothing on turn one.

## 5. Analyzer notes (for its owner)

- **Fresh-memory rows dominate the noise** (19 of the roster top 50, 14 of the post-game top 40). After a disagreement
  the shadow is rebuilt; its follow-ups that turn get high regrets that compare against a line it never played. They
  are marked "fresh memory" but still rank high: cap them at low as the spec says (section 3b), or drop them from the
  top-50 list.
- **A regret of 1.8e308** (post-game, Ilara Gale game 150 #202, "+179769313486231570814527..."): the planner's margin
  against an unscored usual play (`double.MaxValue`). The page should show "no score" there.
- **Watcher attribution:** "negated own effect" blames the card's controller when the opponent used it (Royal
  Oppression paid by Ryo against Damon Vey's Monster Reborn); "lost to own card" names no card for a forced payment
  ("their own a cost", Sorrel Kade); "destroyed by its own rule" counts a Burning Abyss monster destroyed because the
  opponent handed over a Kaiju (owner, Nerio #60); "decked out" doesn't say who emptied the Deck (Return Rook banished
  Orcust's, Runick milled Mass Driver Moss's).
- Skill-design disagreements are most of the top list. Showing the recorded skill next to each row, and an "a slip, by
  design" tag for rows where `--as-played` gives no reason line, would let the index rank real disagreements first.

## 6. Open

- **Fire King Avatar Arvata negating the deck's own monster effect** (Teodoro Brasa, fire-king-snake-eye, games 628 and
  1074): may be a deliberate Fire King line (Arvata destroys another FIRE monster) or a playbook slip; it needs someone
  who knows the deck.
- **Deck cost of Fusions with Deck materials** (Imogen Starling, nl-branded, post-game game 237: 10 cards to 3 in one
  turn of a stall, then decked out): `Drawbacks.DeckCost` doesn't read "...from your hand, Deck, or field as material".
- **Holding a wipe through a stall** (Gage Ironhart, geargia, game 634: Dark Hole held for 30 turns, then decked out):
  the Spending knob's margin; a personality matter, not touched (tuning is by deck choice).
- Not fixed, judgement calls: an Equip before an attack that already wins (Captain Brask's Axe of Despair), Set versus
  Normal Summon at low LP (Nerio's Cir, Ryo's Command Knight), Heavy Storm or Black Rose Dragon taking some of the bot's
  own cards with the opponent's.
- Radiant Typhoon's MSTs on cards that resolve anyway are its deck's own design (the MST activation is the payoff); the
  2026-11-06 removal-timing safelist leaves its playbook rules alone, as intended.

## 7. Checks

On `build/bin/hunt` (the tree with the other chunks' work as of 17:30): `--campaign-test` 479 ok, `--misplay-hunt-test`
6, `--battle-misplay-test` 42 (21 cases, the plain bot and the planner), `--forced-attack-test` 3, `--sense-test` 10,
`--removal-timing-test` 16, `--replay-test` 1/1 identical, `--analyze-test` 53; after the review's fix round
`--misplay-hunt-test` 14, `--battle-misplay-test` 46, `--forced-attack-test` 3, `--sense-test` 10,
`--removal-timing-test` 16, `--analyze-test` 54, `--campaign-test` 479. `--analyze-test`'s baseline check
(today's bots with the misplay toggles off replay the pre-fix duels answer for answer) now turns off this hunt's
toggles too (`ReplayAnalyzerTest.Rebuilt`), and passes 10 of 10: the toggles isolate every change here.
