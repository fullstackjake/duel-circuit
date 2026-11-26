# Research: procedural end games and currency sinks (2026-11-25)

Background for [ENDGAME-SPEC-2026-11-25.md](ENDGAME-SPEC-2026-11-25.md). Gathered by a research agent (Sonnet, web
search) and condensed here. **[D]** = read in a source; **[I]** = inference; **[U]** = not verified. No GDC page was read
directly; talks are cited through secondary sources.

## What other games do

| Game | What's generated | Difficulty knob | Reward / sink | Lesson for Duel Circuit |
|---|---|---|---|---|
| Slay the Spire | Map, rewards, events; Daily Climb adds 3 random modifiers [D] | 20 cumulative Ascensions: more elites, worse events, 10% dearer shops, harder movesets [D] | Prestige only [D] | Stack small rules that change decisions (prices are a knob) rather than raw stats [I] |
| Balatro | Shop, jokers, blinds; enterable seeds; seeded runs don't count for unlocks [D] | 8 cumulative stakes per deck: rewards halved, targets up, dearer packs, Eternal/Perishable jokers [D] | 15 decks x 8 stakes = a 120-cell completion grid [D] | Decks x stakes is a big grid for little content; watch top-tier variance (the developer conceded "possibly too much" randomness) [D] |
| Monster Train | Run content, boss rotation [D] | 25 covenant ranks; Expert Challenges after rank 25 [D] | Prestige; friends' progress shown [D] | Show "best rank per deck" even offline [I] |
| Inscryption: Kaycee's Mod | Randomized maps; first 3 bosses random, 4th fixed [D] | Opt-in challenges chosen before a run [D] | Unlocks cards, starters, challenges and **story files** (the reviewer's main draw) [D] | Let players pick the challenge; pay in narrative. Criticized: forced events, no skip [D] |
| Hearthstone Dungeon Run / Duels | Pick 1 of 3 themed sets after each win; one loss ends the run [D] | (not found) | Duels was retired; upkeep on a live card pool was one reason given [D, mixed] | Build on a sealed pool: no ongoing maintenance [I] |
| Marvel Snap Conquest | Single-loss bracket tiers [D] | Higher tiers need tickets (earned or bought) [D] | Mode medals buy a rotating shop; medals don't carry over [D] | An entry fee is a sink only if payouts stay below it; launch rewards were called underwhelming [D] |
| Gwent Rogue Mage | Node map, permadeath [D] | | Reviewed as shallow [D] | The run layer needs decision depth from the base game, which Yu-Gi-Oh has [I] |
| Pokémon Battle Tower | Rental teams, random trainers [D] | Streak length; a loss only ends the streak [D] | Battle Points, earned and spent only in the mode, at a prize counter [D] | A mode-local currency is a natural firewall against inflation [I] |
| Shadowverse Take Two | Draft a 30-card deck from pairs [D] | | Entry 1,000 Rupies or a ticket; prizes scale with wins; 0 wins still returns a pack ticket [D] | Fees with a participation floor feel fair [D] |
| Hades | | Pact of Punishment: player-chosen conditions [D] | Weapon Aspects pay for the risk [D] | Player-chosen constraints plus real rewards |
| Path of Exile Atlas / Diablo greater rifts | Random maps deeper and harder, fixed final bosses; infinite rift levels [D] | Depth [D] | Leaderboards criticized as only for the competitive [D] | Several valid endgames; don't make it only a number race [D] |
| Yu-Gi-Oh Master Duel Solo, Legacy of the Duelist | Hand-authored gates and 150+ duels [D] | | Fixed authored rewards [D] | The franchise's own games author rather than generate; nothing found on what kept players in Tag Force post-game [U] |

Draft and rental formats (cube, sealed, rentals) make a big collection irrelevant on purpose: everyone builds from the same
pool, so the player's 700-card collection can't solve the mode [D for cube; the "irrelevant on purpose" framing is I].

## Currency sinks

- **Stardew Valley's Qi shop:** Golden Walnuts open the room, and Qi Gems (from challenges and special orders) buy
  endgame goods. Gems can't be bought with gold, which keeps the loop from feeding back into gold [D; the "why" is I].
- **Animal Crossing's house loans:** paid in increments, the house grows after each payoff, and the last payoff gives a
  reward. A big goal you watch grow [D, wikis]. No primary quote that the debt is meant to be friendly [U].
- **General sink theory:** inflation comes when sources outpace sinks; a sink-to-faucet ratio is the health metric. Mostly
  from MMO and free-to-play sources, so applying it to a single-player game is inference [D/I].

## Pitfalls

- **Oatmeal:** outputs that are mathematically unique can look the same. Random recombination makes "noisy, perceptually
  similar, unremarkable content" (Rabii and Cook, 2023, argued from theory) [D].
- **Unwinnable seeds:** some Slay the Spire seeds are provably unwinnable and can be searched for [D]. An offline game
  has no server to catch this, so the generator itself must check.
- **Endless modes:** Slay the Spire 2 won't get one because it limits "infinite strategies" [D].

## Principles we took from it

1. Stack rules, not stats: CPUs stay at full strength, so tiers add player-facing rules.
2. Let the player choose the pain, and list the reward for each choice.
3. Rent or draft the player's deck in the generated mode, so the collection doesn't solve it.
4. Randomize the opponents and the route, not their strength; authored bosses anchor each act.
5. A mode-local currency, earned and spent only inside the mode; DP only buys entry, so DP never comes out positive.
6. Big-purchase goals with a visible bar, paid once, over fees.
7. Narrative unlocks beat numbers.
8. Variance control: rerolls and skips, no forced bad events, capped tail luck at the top tier.
9. Seeds: a daily or weekly seed is cheap and shareable; custom seeds don't pay permanent rewards; check winnability.
10. Test for oatmeal: sample many runs and count the ones that feel different; give each modifier an authored hook.
11. A sealed pool: no maintenance tied to a live card list.

## Sources

[Slay the Spire Ascension](https://slaythespire.wiki.gg/wiki/Ascension) ·
[Daily Climb](https://slaythespire.wiki.gg/wiki/Daily_Climb) ·
[Balatro decks and stakes](https://saschb2b.github.io/game-mechanics/games/balatro/decks-and-stakes) ·
[Balatro stakes (Dot Esports)](https://dotesports.com/indies/news/all-balatro-stakes-in-unlock-order) ·
[Balatro on RNG (GamesRadar)](https://gamesradar.com/games/roguelike/balatro-maker-promises-that-the-dodgiest-odds-in-the-hit-roguelike-arent-deceiving-you) ·
[Monster Train Expert Challenges](https://monster-train.fandom.com/wiki/Expert_Challenges?oldid=9905) ·
[Kaycee's Mod (Super Jump)](https://www.superjumpmagazine.com/breaking-kaycees-mod-a-look-at-inscryptions-next-phase/) ·
[Duels ending (Blizzard)](https://news.blizzard.com/en-us/article/24033780/duels-runs-coming-to-an-end) ·
[Dungeon Run design (GamesRadar)](https://gamesradar.com/delving-into-the-design-decisions-behind-the-dungeon-runs-of-hearthstones-kobolds-catacombs) ·
[Marvel Snap Conquest (Untapped)](https://blog.snap.untapped.gg/marvel-snap-wiki-conquest-mode-shop) ·
[Gwent Rogue Mage (Vice)](https://vice.com/en/article/the-witcher-gwent-rogue-mage-review) ·
[Battle Tower (Bulbapedia)](https://bulbapedia.bulbagarden.net/wiki/Battle_Tower_(Galar)) ·
[Take Two (GameWith)](https://gamewith.net/shadowverse-wb/69403) ·
[Hades interview (Tom's Guide)](https://www.tomsguide.com/features/hades-exclusive-interview-supergiant) ·
[Path of Exile Atlas (MMOBomb)](https://www.mmobomb.com/interview-atlas-worlds-reshapes-path-exiles-endgame) ·
[Greater rifts (Maxroll)](https://maxroll.gg/d3/resources/greater-rift-explained) ·
[Qi's Walnut Room](https://wiki.stardewvalley.net/Qi%27s_Walnut_Room) ·
[Iwata Asks: New Leaf](https://iwataasks.nintendo.com/interviews/3ds/animalcrossing-newleaf/0/0) ·
[Unity economy guide](https://unity.com/resources/game-economy-design-guide) ·
[Rabii and Cook 2023](https://ar5iv.labs.arxiv.org/html/2305.02131) ·
[Practical procedural generation](https://gamedeveloper.com/design/practical-procedural-generation-for-everyone-) ·
[Impossible seeds (PC Gamer)](https://pcgamer.com/does-an-impossible-slay-the-spire-seed-exist-this-players-doing-the-maths) ·
[Slay the Spire 2 and endless (GamesRadar)](https://www.gamesradar.com/games/roguelike/slay-the-spire-2-wont-get-an-endless-mode-like-balatro-because-it-limits-infinite-strategies-but-creator-says-maybe-a-modder-will-think-of-a-way-to-make-it-work/)
