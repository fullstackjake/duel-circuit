# Fact sheet: the end game and DP economy as built (2026-11-25)

Background for [ENDGAME-SPEC-2026-11-25.md](ENDGAME-SPEC-2026-11-25.md). Read from the code by an Explore agent
(Sonnet); `Camp/` = `src/Ygo.Engine/Campaign/`. INFERRED marks what wasn't read directly. The code wins over this page.

## 1. After the Tier 0 Gauntlet

- **Format Library** (`campaign/library.txt`, `Camp/Library.cs`): 12 cups, each with its own ban list and pool cutoff;
  opens when the Gauntlet is cleared (Library.cs:44,204-207). Each cup has 4 fixed boss matches, some with an optional
  "Champion's table". A Format Master stamp needs your own deck (Library.cs:224). Pay:
  - first win 600 + 200;
  - rematch 150;
  - cup clear 2,500 DP + 2 tokens;
  - stamp +1 token.

  The **Every Era final** opens after all 12 cups: 6 rounds, your own deck, no loaners and no Ban Passes; it pays
  10,000 DP + 3 tokens + a poster. Nothing here is random.
- **Locals brackets** (`Camp/Brackets.cs`): 8-player brackets per cup. Entry 1,000; pay 900/650/400/150 plus 2/1/0/0
  packs; 3 per cup. Fields are seeded from the save's salt.
- **Champion's Ladder** (`campaign/ladder.txt`, `Camp/Ladder.cs`): opens after the Every Era final and beating Ilse.
  10 fixed rungs at skill 1.0, climbed one at a time, no falling. First climb 800; top prize 5,000 + 3 tokens + a poster;
  rematch 150. Visitors arrive only after the No Limits title, and No Limits is `ready: no`, so they're unreachable today
  (INFERRED from the flags). Nothing is random.
- **Back Alley** (`campaign/alley*.txt`, `Camp/BackAlley.cs`, `Ante.cs`, `AlleyEconomy.cs`): opens after the first
  Library cup.
  - **Stakes:** Street 300 DP, Den 1,000 DP, Orvel 3,000 DP plus a card worth 1,000 or more. A win takes the CPU's
    card and returns your stake; a loss forfeits it. Lost Orvel cards hang on Moth's rail at 1.5x for 5 duels.
  - **Pay:** alley duels pay 0 DP; a first table clear pays 1 token.
  - **Brackets:** Alley Brawl (1,000 entry) and Midnight Open (2,500 entry) pay DP and Contraband Packs. The No Limits
    Invitational is "Coming soon".
  - **This is the one procedural system:** a night's seats come from `Hash.Mix(salt, table, night)`
    (BackAlley.cs:982-994). A regular is a base deck plus a "splash" of contraband (`Alley.SplashChecked`), with a
    generated handle, name and tint. The CPU's stake binder holds 12 seeded cards. Nights are saved: a reload never
    rerolls.
  - **Moth's van** (`Camp/MothsVan.cs`): a contraband shelf at 1.5x value, new every 10 alley duels; 18 used listings,
    new every 5. The sell desk pays a quarter of value, never more than half of what you paid.
- **Bounties** (`campaign/bounties.txt`): 18 fixed, paid once, circuit duels only.
- **Featured** (`campaign/featured.txt`): a curated shop shelf.
- **Guests:** Ilse and Vera pay 10,000 "once" after the final; earlier guests 1,500-8,000, paid again each New Game+.
- **New Game+** (Profile.cs:905-914): clears match wins and loss streaks, bumps the cycle, keeps everything else. The same
  decks at the same skill; about 222k DP and 41 tokens paid again per lap (PRODUCTION-REVIEW-GAME:138). Nothing is
  random or harder.
- **Era mats and engravings** (`Camp/Cosmetics.cs:15`): a mat costs 8,000 DP per cleared cup; an engraved plate 2,500
  per (cup, deck). These are the only DP cosmetics. Posters are earned, not bought.
- **Make it yours** (Profile.cs:645): buy a loaner's list at card price plus a service fee.
- Also: Scouting, wall and toss advice, OCG import packs, Tag Cups, Gus's 3 chain lessons (`Camp/ChainLessons.cs`:
  scripted setup and judge).

## 2. DP economy

- **Ledger:** `Profile.Spend(sink, dp)` (Profile.cs:854) and `Profile.Earn(source, dp)` (:863) write `DpSpent`/`DpEarned`.
  The invariant is earned − spent = Dp − 1000. Held alley stakes use `HoldStake`/`ReturnStake`.
  - **Sources** (Profile.cs:618-661): circuit, tag, guest, cup, final, locals, ladder, alley, nolimits (each as a win,
    `.loss` or `.prize`); levelup; refunds; sell; passrefund; debug.
  - **Sinks:** packs (plain, selection, box), recipe.pack, recipe.outright, makeityours, structure, tokens, grading,
    used, order, mats, engrave, brackets, alley.stake/rail/shelf/used/brackets, nolimits.stake/entry.
- **Income:**
  - circuit win (150 + 75 × circuit) × 1.5 in the late bands, so the Gauntlet pays 2,588;
  - first-win bonus +200;
  - rematch 150, loss 50, draw 100;
  - level-up 100 + 25 × level;
  - tokens per clear (1, or 3 in late bands).
- **Prices:**
  - boosters 150/200/300/450/600 by set year, rarity sets x1.5/2.5/3.5;
  - boxes are 24 packs at x0.8;
  - selection packs 200-1,250;
  - recipe packs 300-2,500 (1,000 in the Library);
  - outright copies 40/80/150/250/400;
  - tokens 1,000 + 500 each time;
  - grading 100-2,500;
  - mats 8,000, engravings 2,500, brackets 1,000-3,000, stakes 300-3,000.
- **`--arbitrage`** (src/Ygo.Harness/Arbitrage.cs:10-17): tries every buy-to-sell/refund loop (greedy plus exhaustive
  1-3 action sequences, brackets, alley, No Limits) and fails on any DP-positive loop. It also checks cup farming stays
  at or under 300 DP a duel. It does **not** bound duel-win farming in absolute terms; wins are the faucet.
- **Champions' DP:** QA champions banked 139k-412k (qa-night5: 354,554), having spent 6.6-8.7%. That was before the
  climb cut rematch pay, so it's likely lower now (INFERRED). POST-GAME-PLAN:584: "The game still needs another DP sink."

## 3. Procedural and random generation

- **Seeded from the save's salt:** alley nights, splashes and handles, binder stakes, Locals and alley bracket fields,
  the used market, the van shelf. Pack opening uses `Random`.
- **Fixed:** bounties, featured, circuits, guests, Library, Ladder.
- **Pools:**
  - 114 decks: history 74, no-limits 11, root 11, headliners 8, structure 8, alley 2;
  - 112 personalities;
  - 41 historic ban lists;
  - `campaign/deck-ratings.txt` rates 90 decks by bot-vs-bot Elo;
  - `AutoDeck.Build`/`FromRecipe` builds decks from a collection;
  - about 15,282 cards with print dates and rarities (`printings.bin`);
  - `campaign/history.json` (meta decks with tier, how and why).

## 4. What the reviews call weak (PRODUCTION-REVIEW-GAME-2026-11-20.md, TODO.md)

- Wins stop meaning anything, and champions sit on 300k+ with nothing to want. B4 asks for late-game wants: a
  collection log with completion rewards, a Gem Mint chase, and a Champion's shelf of slabs.
- New Game+ "replays the same campaign with nothing at stake". **B2, the Champion's Circuit:** stronger decks in each
  match from cycle 1, prizes once per save; 4-6 days.
- **B1, Vera's rival matches:** an optional 5th match per band; 4-5 days.
- **B3, Gus's puzzle book:** "win this turn" boards on the lesson machinery; 6-8 days.
- The post-game is mostly rematches. The Library has only 4 decks the circuits lack, the Ladder's rungs are circuit or
  guest decks, and the novel content (No Limits) is locked.

## 5. Constraints and patterns

- **Owner rules** (CLAUDE.md):
  - CPUs at full strength, difficulty only from deck choice;
  - every DP change through `Spend`/`Earn`, nothing DP-positive;
  - old saves must load;
  - no card pictures, nothing of Konami's, original characters, all-ages.
- **Saves:** plain text `mods/save/profile.txt`, one fact per line. Load is a keyed switch that ignores unknown keys, so
  new state goes in as new optional lines. Migration is in code (`Circuits.Migrate`, `ClimbMigrated` and so on).
- **The pattern for a new campaign data file** (copy the Ladder):
  - `campaign/<x>.txt` with a commented format header, parsed by a static `Load(path, roster, deckExists)` into a data
    class with a `Problems` list;
  - loaded in Unity's `CampaignState` and in the harness (CampaignTest, LedgerCheck, Arbitrage);
  - per-save state in a small class with `Write`/`ReadLine` hooked into `Profile.Save`/`Load`;
  - pay through `Circuits.PayDuel` with a ledger kind;
  - a dedicated test, a `--campaign-test` step, an `--arbitrage` loop, and a story hook.
