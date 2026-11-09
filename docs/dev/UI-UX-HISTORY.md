# Yu-Gi-Oh! video games: UI and UX lessons for Duel Circuit

A look back at how Yu-Gi-Oh! games have presented the card game since the late 1990s: what worked, what didn't, what
makes Master Duel the strongest version so far, and what it still lacks. The last part turns those lessons into
suggestions for Duel Circuit, checked against what this project already does (October 2026).

How to read it:

- **Sources.** Claims about reviews and player feedback come from the sources at the end. Some are forum threads,
  which are opinion rather than data, and many are from launch periods (2019 to 2024). Background on the older
  games (formats, release order, mechanics) is from general knowledge of the series and is marked "roughly" where
  dates may be off by a year.
- **Scope.** This is about the screen and the experience: reading cards, making decisions, waiting, learning,
  building decks, collecting and progressing, and the feel of the world. Card balance comes up only where it
  shaped the experience.
- **Earlier reports.** The UI roadmap ([UI-ROADMAP.md](UI-ROADMAP.md)) and the Deck Workshop plan
  ([DECK-WORKSHOP.md](DECK-WORKSHOP.md)) already drew on two reports, a Master Duel duel UI report and a Tag Force
  deck editor report. This document is wider (the whole history) and doesn't repeat what those built.

---

## 1. The short version

Across twenty-five years, the same handful of things decide whether a Yu-Gi-Oh! game feels good:

1. **Can you read the cards?** Small scans, tiny text and unexplained terms are the most repeated complaint, from
   the 2010s console games to Master Duel.
2. **How much do you wait?** Slow animations in older games, and the opponent's ten-minute combo turns in Master
   Duel. Waiting is the biggest source of frustration in modern play.
3. **Does it teach the game, or only the buttons?** Most games explain the controls and basic rules. Few explain
   card text or how a deck is meant to play, and that gap is where new players give up.
4. **Is collecting fair and legible?** Forbidden Memories' drop rates and Master Duel's costs pull in opposite
   directions. Players stay when they can see a path to the deck they want.
5. **Is there a world and people in it?** The games remembered fondly (Tag Force, Duel Links, Forbidden Memories)
   have characters and places. The ones that felt bare (Millennium Duels) had a sterile table and static portraits.
6. **Does the format fit the session?** Duel Links' two-minute Speed Duels fit a phone. Full Master Rules fit a
   desk. Games that changed the format too far (Cross Duel's four-player mode) didn't last.

Master Duel gets 1, 3 (partly) and most of the presentation right, and it keeps improving. It is weakest on 2, on
teaching card text and decks, on single-player depth, and on the warmth of 5. Those are exactly where a
single-player, character-driven game like Duel Circuit can do better.

---

## 2. Timeline at a glance

| Era (roughly) | Games | Format | What it taught the series |
|---|---|---|---|
| 1998–2002 | Duel Monsters (Game Boy, Japan), Forbidden Memories (PS1), Dark Duel Stories | Simplified rules, few or no effects | Collecting and discovery are addictive; grind and obscure rules are not |
| 2002–2004 | Eternal Duelist Soul (GBA), Duelists of the Roses (PS2), Capsule Monster Coliseum | Closer to real rules; experimental boards | Faithful rules matter to fans; experimental boards split them |
| 2004–2011 | World Championship series (GBA, then DS), Nightmare Troubadour | Near-real rules on small screens | Rules accuracy and puzzles win long playtimes; cramped screens and thin filters hurt |
| 2006–2011 (and 2015) | Tag Force series (PSP), ARC-V Tag Force Special | Real rules, anime characters | A world, partners and a card shop make a card game feel like an RPG |
| 2010–2019 | Decade Duels, Millennium Duels, Legacy of the Duelist, Link Evolution | Real rules, big card pools | Card pools grew faster than the interfaces that show them |
| 2017–present | Duel Links (mobile, PC) | Speed Duel, later Rush Duel | A format and onboarding built for the platform; fast but swingy |
| 2022–2023 | Cross Duel (mobile) | Four-player variant | Diverging too far from the card game, with heavy gacha, doesn't hold players |
| 2022–present | Master Duel (all platforms) | Full Master Rules | Production values and a faithful engine; the rest is catching up |
| 2025 | Early Days Collection (Switch, PC) | 14 classic games | The old games are charming history, but their interfaces have aged badly |
| Ongoing | Fan simulators (EDOPro, YGO Omega, Dueling Book) | Any format, any list | Choice of format and ban list, fast automation, utilitarian looks |

---

## 3. Era by era

### 3.1 The first games (late 1990s to early 2000s)

**Forbidden Memories** (PS1) is the touchstone here. You fused cards straight from your hand, and the game had a
story and its own Egyptian setting. Players still remember the fusions as fun to discover.

- **Worked:** the joy of discovering fusions; fast, simple controls; a story with a destination; music people
  remember.
- **Didn't work:** the grind. Wanted cards had very low drop rates, and players report beating the same opponents
  hundreds of times. The rules also weren't the card game's, which matters more as players learn the real one.

**Early Days Collection** (February 2025) re-released fourteen of these games (five never released outside Japan)
with online play. Reviews call it valuable preservation, but say the menus are unwieldy, deck building is clunky
with few filters, and there's little onboarding for newcomers. Two titles stood out to critics: Monster Capsule and
the two World Championship Tournament games. Overall reception was middling.

**Takeaway for Duel Circuit:** discovery and collecting are the oldest hook in the series, and grind is the oldest
complaint. A game can keep the first without the second if the player can always see the next step.

### 3.2 The World Championship and DS years (2004 to 2011)

The handheld World Championship games tracked the real card game closely, with tutorials, duel puzzles and, in the
2011 game, beginner aids such as deck diagnostics and in-duel assistance. They had loyal followings and long
playtimes. The DS-era interfaces drew complaints ("wonky", cramped on two small screens), and deck builders had
limited sorting and filtering.

- **Worked:** rules accuracy; puzzles as a way to teach; beginner aids added in later entries.
- **Didn't work:** small screens and dense menus; limited search.

### 3.3 Tag Force (PSP, 2006 to 2011, plus 2015's Tag Force Special)

Seven games, mostly on PSP: the first four follow GX and the later ones 5D's, and the last one (ARC-V Tag Force
Special) mixes every series. You walk around anime locations, pick a partner from the cast and play 2-on-2 Tag
Duels. A new game came out every year from 2006 to 2011.

- **Worked:**
  - **A world with people.** Places to visit, characters with their own decks and personalities, and a partner who
    grows with you. This is the "cozy RPG" feel fans miss.
  - **The card shop and packs.** Buying packs with what you win is a satisfying, legible loop.
  - **Character decks you can study.** Recipes and opponents' decks are a way to learn what good decks look like
    (the Deck Workshop plan rates recipes as the series' best idea).
  - **Presentation that grew.** Later entries added animations and 3D Field Spell backdrops from the card art.
- **Didn't work:**
  - Dialogue and errands repeat over hundreds of duels.
  - Menus and duel animations were slow by modern standards, and the deck editor was built around a PSP's buttons.
  - Each game changed what carried over; the yearly cadence meant iteration more than reinvention.

**Takeaway:** Tag Force proves the card game can carry an RPG. Duel Circuit is in this tradition and already has
its strengths: circuits, characters, a shop, recipes and Tag Cups.

### 3.4 The console games of the 2010s

**Millennium Duels** (PS3 / Xbox 360, 2014): reviews found the gameplay solid but the rest bare. They called the
card art tiny and hard to make out, and the table sterile. Opponents were static, voiceless portraits, the only way
to tell them apart. There was a tutorial up front.

**Legacy of the Duelist** (2015) and **Link Evolution** (2019 on Switch, then PC): campaigns through each anime
series with story duels, a very large card pool and challenge content. Reviews of Link Evolution split:

- Some found the menus easy and touch-friendly for sorting thousands of cards. A "Related Cards" button suggested
  support for a chosen monster.
- Others found deck editing clunky with both buttons and touch, and the card view too small (small scans at the side
  of the screen).
- A player reported that the recipe feature flagged owned cards as missing when a recipe needed three copies.

**Takeaway:** as card pools grew past ten thousand, the hardest part of the UI became *finding and reading cards*,
not playing them. The games that invested in big card views and good filters aged better.

### 3.5 Duel Links (2017 to now)

Duel Links rebuilt the format for phones. It has three Monster and three Spell/Trap Zones, 4,000 LP, four-card
opening hands and no Main Phase 2. It started players on a smaller, older card pool, and added character Skills, a
world map and the anime cast. Rush Duel came later on the same interface.

- **Worked:**
  - **Bite-sized duels.** Reviewers note duels rarely last more than about two minutes, which suits short sessions
    and invites one more game.
  - **Onboarding by subtraction.** The smaller, older pool and simpler format let new players learn before the
    modern game hits them.
  - **Characters and places**, in the Tag Force spirit, on a phone.
- **Didn't work:**
  - **Swinginess.** With 4,000 LP an early advantage can end a duel in seconds, and some argue the format
    disadvantages the player going first.
  - Skills and power creep became balance problems over the years (widely discussed in the community, not covered
    by the sources here).

**Takeaway:** fit the format to the session. A short mode can live alongside full duels.

### 3.6 Cross Duel (2022 to 2023)

A four-player variant on mobile that launched in 2022 and shut down on September 4, 2023, within a year. Konami gave
only "various circumstances" as the reason. Coverage points to weak traction beside Master Duel and Duel Links, and
to fan complaints that its gacha was pay-to-win.

**Takeaway:** a new format needs to stay recognizably Yu-Gi-Oh!, and progression that feels rigged drives players
away fast.

### 3.7 Fan simulators

EDOPro (whose rules engine Duel Circuit uses), YGO Omega and Dueling Book serve players the official games don't.
They offer any format, the real TCG ban lists (players often prefer these to Master Duel's own list), custom duel
settings, tag and speed modes, and they're free. Their weak spots are looks, onboarding and, for Omega, timer
settings some players dislike.

**Takeaway:** players value *choice of format and ban list*. Duel Circuit's era-by-era ban lists and house rules
are a real strength.

---

## 4. Master Duel: why it's the best yet, and what it lacks

Master Duel launched in January 2022 on every platform, with full Master Rules and a free-to-play model. It drew
strong reviews: about 80 on Metacritic for PC and PS5, 72% on OpenCritic, and "Mostly Positive" on Steam at launch.
Shortly after launch, over 250,000 players were online at once on Steam.

### 4.1 What makes it great

- **Production values.** PC Gamer called them among the best of any digital card game. The 3D field, summon
  animations and ace cut-ins keep coming: new animations shipped in January, April and July 2026.
- **A faithful engine with a clear duel screen.** Full rules with the decisions on the board: card pickers,
  confirmations on the card, and a chain shown as it resolves.
- **Control over interruptions.** The chain setting (ON / Auto / OFF) decides when the game stops to ask. A quick
  hold on the button switches it for one moment.
- **Steady quality-of-life work**, which matters as much as any single feature:
  - Replay saving (from version 1.2.0), improved in October 2024 with tags and featured cards.
  - Checking your Deck mid-duel, and card activation effects (2024).
  - An improved Duel Log (May 2024).
  - Deck export and bulk deck deletion.
  - In April 2026: effect-based filters in the deck editor, a wider "Related Cards" (cards likely to share a deck),
    separate protectors for Main and Extra Deck, an Activation Order setting for effects at the start of the Battle
    Phase, and a new font.
- **Solo Mode with lore.** Each "Gate" covers a deck type's story with guided duels and two-part tutorials, and a
  new Gate arrived in February 2026. Reviewers note that each scenario brings you up to speed on specific decks and
  combos.
- **A competitive structure.** Ranked play, events, and from June 2026 a Duelist Level (DLv) system that rises and
  falls with wins and losses and counts toward regional qualifiers.
- **Free, with crafting.** Every card can be crafted, so a deck is a target you can work towards rather than a lottery
  ticket (though see below on cost).

### 4.2 What it's missing, or does poorly

1. **Teaching card text and decks.** The strongest critique in reviews: the tutorials cover the client and the base
   rules, but card-level mechanics, long descriptions and terms are left unexplained. One review called it among
   the least beginner-friendly card games; several suggest new players learn from a friend or outside guides.
   Suggestions in reviews include a complexity rating for decks and a newcomer-only mode.
2. **Waiting on the opponent.** Players describe waiting nearly ten minutes for an opponent's first turn. Turn-timer
   debates split the community: short timers punish genuine reading of complex text, long ones allow stalling. Early
   reviews already flagged sitting idle through long combos.
3. **Single-player depth.** PC Gamer's verdict was that it could use more single-player content. Solo Mode is lore
   and tutorials, not a campaign with characters or progression.
4. **Solo loaners that feel unfair.** A recurring forum complaint: Solo loaner decks are weaker than the AI's, with
   restarts until a playable opening hand. Some challenges are described as more cliff than hill.
5. **No practice sandbox.** Players ask for a training mode where they can set their own starting hand and the
   AI's, to practise lines and answers.
6. **Its own ban list, and a meta that shapes everything.** Master Duel uses a list of its own rather than the TCG's
   or OCG's, which some players dislike. Combo-heavy turns and full-negate boards dominate complaints about the
   experience.
7. **Cost and casual space.** PC Gamer estimated $30–60 to build several competitive decks. Players ask why there's
   no casual queue (the answer given: casual players would bring the same decks), and some suggest a casual format
   with simpler combos.
8. **A world.** There's no cast, no places, no partner: a menu of modes around a duel table. That's the gap between
   Master Duel and Tag Force or Duel Links in how the game *feels*.
9. **Accessibility is hard to judge.** No documentation turned up on colorblind modes or text size settings in
   Master Duel. That isn't proof they're missing, only that they aren't prominent.

### 4.3 The one-line verdict

Master Duel is the best *duel screen* the series has had and the best way to play today's game against people. It is
not yet a good *teacher* or a warm *world*, and its single-player is a side mode.

---

## 5. Patterns across every era

| Pattern | Worked | Didn't |
|---|---|---|
| **Reading cards** | Big detail panels (Master Duel); related-card hints (Link Evolution, Master Duel) | Tiny scans (Millennium Duels, Link Evolution); unexplained terms (Master Duel) |
| **Waiting** | Bite-sized duels (Duel Links); chain modes that skip pointless stops (Master Duel) | Slow animations (older consoles); long opponent combos and stalling (Master Duel) |
| **Teaching** | Puzzles and beginner aids (World Championship); deck-story tutorials (Master Duel Solo) | Teaching only the buttons; no deck-level guidance; few extras in re-releases |
| **Collecting** | Card shop loops (Tag Force); crafting toward a target (Master Duel) | Rare drops and repeated grinding (Forbidden Memories); pay-to-win gacha (Cross Duel) |
| **World and people** | Partners and places (Tag Force); the anime cast on a map (Duel Links) | Static voiceless portraits and a sterile table (Millennium Duels) |
| **Format** | Formats fitted to the device (Duel Links); real rules for desks (Master Duel); format choice (simulators) | Formats too far from the card game (Cross Duel); swingy early turns (Duel Links) |
| **Deck building** | Good filters and related cards; recipes to learn from | Clunky controls, few filters, recipe bugs |
| **Fairness** | Seeing a path to the deck you want | Loaners weaker than the opponent's deck; rigged-feeling luck |

---

## 6. What this means for Duel Circuit

Duel Circuit already covers a lot of this list. The table says where it stands on each lesson, then the
suggestions follow in priority order.

### 6.1 Where it stands

| Lesson | Duel Circuit today |
|---|---|
| A big, readable card detail | Done: Master Duel-style detail, pinning to compare, text size setting; readable on 16:9 since October 2026 |
| Chain stops under control | Done: Auto / On / Off, hold Shift (or LT) to be asked once, a chain panel in resolution order |
| Knowing what happened | Done: a history strip and drawer, messages for what you'd miss, an explaining log |
| Replays that teach | Done: a timeline, jumps to a turn or chain, boss Replay Lessons, replay codes |
| Presentation without the wait | Done: full scenes the first time, shorter repeats; switches for each effect; Reduced motion; a speed setting |
| Teaching the rules | Mostly: Old Gus's chain lessons, and a card-term glossary in the card detail (built from suggestion 1) |
| Teaching decks | Partly: over 60 historical decks with notes on how they worked and why they mattered, and 30 deck playbooks the CPU follows, but no player-facing "how this deck plays" |
| Practice | Partly: test hands, opening-hand odds, 100-game bot tests in the editor; no board you can set up and play from |
| Collecting with a visible path | Done: era-based shop, selection packs, recipe packs and outright purchase, loaners, a wishlist |
| Fair single-player | Mostly: CPUs with rules sense and deck playbooks; loaners at half DP; Ban Passes |
| A world and people | Mostly: circuits on a map, characters with portraits and lines, story scenes, Tag Cups; expressions wait on art (#5) |
| Format choice | Done: era ban lists per circuit, house rules (Beginner circuits, the Yata-lock) |
| Not only color | Done: ▶ tabs (now optional), YOU / OPP labels, ▲ / ▼ stats |

### 6.2 Suggestions

**First: the biggest gaps Master Duel leaves open**

1. **A card-term glossary in the card detail** (done, roadmap #33). Master Duel's most repeated criticism is unexplained card text.
   Underline terms in the detail panel and explain them on hover: "Quick Effect", "once per turn", "banish",
   "Tribute", "Special Summon", "negate the activation" vs "negate the effect", "target". Add a one-line key to
   card-text punctuation (a colon marks the activation condition, a semicolon separates the cost from the effect).
   This is cheap to build and helps every duel.
2. **"How this deck plays" pages from the playbooks.** Each of the 30 deck playbooks already encodes a deck's combo
   order, what it searches and what it shouldn't waste. Showing that as a short guide in the Recipe Book or before a
   boss duel ("Bonfire finds Snake-Eye Ash; Ash searches a Level 1 FIRE; ...") would do what Master Duel's Solo
   Gates do, with real decks and no extra writing. A "Show me the line" button could play the first turn as a
   demonstration.
3. **A practice board (Puzzle and Sandbox).** The engine can already set up a board: the Yata-lock and negator tests
   place cards on the field before the duel starts. Expose that as:
   - **Puzzles:** set boards with a goal ("win this turn", "survive this turn"), in the World Championship
     tradition and as EDOPro offers.
   - **Sandbox:** choose your hand and the CPU's board, then play it out. Master Duel players ask for exactly this.
4. **Less waiting through the CPU's long turns.** In single-player, nobody else needs the time. Two options:
   - "Skip to my turn" in your own duels, during the CPU's turn. Replays already have a **Next turn** button that
     jumps ahead and summarises what changed on the board; the same for the CPU's turn in a live duel would remove
     most of the waiting. It would have to stop whenever you get a decision (a chain you could answer).
   - A one-tap "fast-forward this chain" that runs effects at top speed.

**Next: fairness, warmth and format**

5. **Show the luck after a loss.** Master Duel's Solo complaints are about feeling cheated by draws. After a loss,
   show what the CPU held and the turning point ("they drew into Ash Blossom on turn 3"). The Replay Lessons already
   find big moments; this is a short version on the result screen.
6. **Partner bonds for Tag Cups.** Tag Force's best memories are partners. Duel Circuit's partners are any duelist
   you've beaten. A small bond meter (duels together, wins) that unlocks a partner's line, a deck tweak or a
   recipe would add warmth without dating-sim errands. The expression art (#5) makes this land.
7. **A Quick Duel mode.** For short sessions, a Free Duel rule set with 4,000 LP and a smaller field, or a turn
   limit, in the Duel Links spirit. It could use the same house-rule mechanism as the Yata-lock.
8. **A colorblind-safe option.** Red and blue mark the two sides, and gold marks what you can act on. Offer a palette
   swap (for example orange and blue, with a second accent for playable cards) beside the existing not-only-color
   cues.

**Later: polish from the history**

9. **Target arrows on the field.** The chain panel names targets ("→ Blue-Eyes White Dragon"). A line from the
   activating card to its target on the board would make chains readable at a glance.
10. **Complexity ratings in the Recipe Book.** One review suggested a complexity rating for Master Duel's decks.
    Duel Circuit can compute one from the playbooks and the CPU's measurements (how many actions a typical turn
    takes) and show it beside each recipe.
11. **Keep grind away from the late game.** The simulated campaigns already measure the late walls. Keep watching
    "duels per circuit" and keep loaners, recipe packs and Ban Passes as the pressure valves, so the late game never
    turns into Forbidden Memories' farming.

### 6.3 Things to avoid

- Making the player wait for anything that isn't a decision (Master Duel's biggest complaint).
- Rewards that depend on rare luck, with no visible path (Forbidden Memories).
- A sterile table with static opponents (Millennium Duels).
- Formats that stop feeling like Yu-Gi-Oh! (Cross Duel).
- Menus that teach the buttons but not the game (most of the series).

---

## Sources

Master Duel
- [Yu-Gi-Oh! Master Duel review, PC Gamer](https://pcgamer.com/yu-gi-oh-master-duel-review)
- [Yu-Gi-Oh! Master Duel, Wikipedia](https://en.wikipedia.org/wiki/Yu-Gi-Oh!_Master_Duel)
- [Master Duel surpasses 250,000 concurrent players on Steam, Out of Games](https://outof.games/news/4173-konamis-yu-gi-oh-master-duel-surpasses-250000-concurrent-players-on-steam/)
- [Master Duel review, GGRecon](https://www.ggrecon.com/reviews/yu-gi-oh-master-duel-review/)
- [Master Duel review, Nintendo Life](https://nintendolife.com/reviews/switch-eshop/yu-gi-oh-master-duel)
- [Master Duel review, Pocket Tactics](https://www.pockettactics.com/yu-gi-oh-master-duel/review)
- [Master Duel is more accessible, TheGamer](https://www.thegamer.com/yu-gi-oh-master-duel-more-accessible-preview/)
- [Master Duel review, Keen Gamer](https://www.keengamer.com/articles/reviews/pc-reviews/yu-gi-oh-master-duel-review-a-pharaohs-addiction/)
- [April 2026 app update, Master Duel Meta](https://www.masterduelmeta.com/articles/news/april-30-2026/master-duel-update)
- [What changed in Master Duel in 2026, Gamelook](https://gamelook.yoizuki.dev/games/yu-gi-oh-master-duel/changes/2026)
- [Master Duel patch notes, SteamDB](https://steamdb.info/app/1449850/patchnotes/)
- [Master Duel update 2.01 quality-of-life additions, MP1st](https://mp1st.com/title-updates-and-patches/yu-gi-oh-master-duel-update-2-01-delivers-quality-of-life-additions)
- [Master Duel adds deck export and bulk delete, Out of Cards](https://outof.cards/news/5582-yu-gi-oh-master-duel-adds-quality-of-life-improvements-to-the-game-including-the-ability-to-export-decks-and-to-delete-multiple-decks-at-once/)
- [Master Duel 1.2.0 release notes, APKMirror](https://apkmirror.com/apk/konami/yu-gi-oh-master-duel/yu-gi-oh-master-duel-1-2-0-release)
- [Update disappoints fans with lack of banlist and long Duel Pass, Gfinity](https://www.gfinityesports.com/article/update-disappoints-fans-banlist-duel-pass)
- [Solo Mode, Yugipedia](https://yugipedia.com/wiki/Solo_Mode)
- [Master Duel Solo Mode guide, Upcomer](https://upcomer.com/yugioh-master-duel-solo-mode-guide/)
- Steam community discussions on long turns, timers, casual play and Solo Mode:
  [1](https://steamcommunity.com/app/1449850/discussions/0/3792632416034949902),
  [2](https://steamcommunity.com/app/1449850/discussions/0/3386156547855749408),
  [3](https://steamcommunity.com/app/1449850/discussions/0/4209245188598503458),
  [4](https://steamcommunity.com/app/1449850/discussions/0/3187990555102932396),
  [5](https://steamcommunity.com/app/1449850/discussions/0/6553383891354596071),
  [6](https://steamcommunity.com/app/1449850/discussions/0/4846526727957281687)

Duel Links, Cross Duel and the older games
- [Duel Links review, Pocket Gamer](https://www.pocketgamer.com/yu-gi-oh-duel-links/yu-gi-oh-duel-links-review/)
- [Duel Links review, PhoneArena](https://www.phonearena.com/news/Yu-Gi-Oh-Duel-Links-Review-A-mobile-reimagination-of-the-classic-TCG_id90027)
- [Duel Links Rush Duel preview, GGRecon](https://www.ggrecon.com/articles/yu-gi-oh-duel-links-preview-rush-duels-make-it-a-new-game-that-s-perfect-for-old-duelists)
- [Cross Duel shutting down after less than a year, Pocket Tactics](https://www.pockettactics.com/yu-gi-oh-cross-duel/shut-down)
- [Cross Duel shuts down on September 4, Final Weapon](https://finalweapon.net/2023/03/28/yu-gi-oh-cross-duel-shuts-down-on-september-4/)
- [Tag Force series, Yugipedia](https://yugipedia.com/wiki/Yu-Gi-Oh!_GX_Tag_Force_series)
- [GX Tag Force 2, Wikipedia](https://en.wikipedia.org/wiki/Yu-Gi-Oh!_GX_Tag_Force_2)
- [Legacy of the Duelist: Link Evolution review, Way Too Many Games](https://waytoomany.games/2019/08/23/review-yu-gi-oh-switch/)
- [Link Evolution review, Seafoam Gaming](https://seafoamgaming.com/2019/08/19/yu-gi-oh-legacy-of-the-duelist-link-evolution-nintendo-switch-review/)
- [Link Evolution tips, iMore](https://www.imore.com/yu-gi-oh-legacy-duelist-link-evolution-tips-tricks)
- [Link Evolution recipe discussion, Steam](https://steamcommunity.com/app/1150640/discussions/0/4578439633200751197)
- [Millennium Duels review, Digitally Downloaded](https://www.digitallydownloaded.net/?p=48294)
- [Millennium Duels review, Push Square](https://www.pushsquare.com/reviews/psn/yu_gi_oh_millennium_duels)
- [World Championship 2008 reader review, Cubed3](https://cubed3.com/reader_review/613/1/yu-gi-oh-world-championship-2008-nintendo-ds.html)
- [Early Days Collection review, MP1st](https://mp1st.com/reviews/yu-gi-oh-early-days-collection-review-its-time-to-duel)
- [Early Days Collection review, Nintendo World Report](https://www.nintendoworldreport.com/review/70092/yu-gi-oh-early-days-collection-switch-review)
- [Early Days Collection, OpenCritic](https://opencritic.com/game/18160/-/reviews)
- [Forbidden Memories retrospective, Five Paths](https://raba.fivepaths.com/?p=12746)
- [Forbidden Memories player review, Backloggd](https://backloggd.com/u/TheBeautifulEric/activity/)
