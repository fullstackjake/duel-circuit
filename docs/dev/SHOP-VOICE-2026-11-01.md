# The shop's voice (2026-11-01)

The Narrative Designer's lines for the shop and packs rework. It builds on docs/dev/PACKS-REVIEW-2026-11-01.md section 2.
Nothing in the code was changed.
- **Who talks:** Old Gus owns the shop, so the footer, arrivals, boxes and odds are his. Kit, the regular at the next
  table, reacts to pulls and scouts the shelves. Hollis speaks only over his own Prize Packs.
- **What they know:** every line is gated by the story scenes you've seen (`after:`), so nothing comes before the
  scene that sets it up. "Gus's grudge against Wren" can't appear before `advanced`, for example.
- **The format:** docs/dev/SHOP-REWORK-SPEC-2026-11-01.md didn't exist when this was written, so below is a proposed
  `campaign/shop.txt` in story.txt's style. If the spec defines its own hook names, rename the `pool:` keys to match.
  The lines don't depend on the format.

## The format (proposed): campaign/shop.txt

```
# The shop's lines: pools of lines, one picked each time its hook fires (docs/dev/SHOP-VOICE-2026-11-01.md).
#   pool: <hook>           starts a pool (the hooks are listed below)
#   after: <scene id>      the pool plays only once that story scene has been seen (Profile.Seen); repeatable, all must hold
#   before: <scene id>     ...and stops once that scene has been seen
#   band: <band>[+]        only while your newest open circuit is in that band ("+": that band or any later one)
#   <speaker>: <text>      a line, as in story.txt: a character id, or "-" for a shop label; [era] / [open] work too
# Text can use {card}, {rarity}, {opponent}, {opponents}, {deck}, {year}, {packs} and {circuit}.
# A hook with several eligible pools draws from all of them. A more specific hook (pull:opponent:old-gus) wins over
# the general one (pull:opponent).
```

**Hooks:** `header`, `footer`, `arrival:<year>`, `arrival`, `pull:<tier>`, `pull:opponent`, `pull:opponent:<id>`,
`pull:recipe`, `pull:deck`, `house-pack:name`, `house-pack:about`, `house-pack:about-gus`, `shelf:<circuit>`,
`back-shelves`, `box`, `box-best`, `odds`, `kit-hello`, `prize-pull`.

**Rules for the code:**
- **`{opponent}`** is the display name ("Damon Vey", "Nico and Nina Castell"). The lines use it only as a possessive or
  an object, never as the subject of a verb, so the twins read right.
- **`{opponents}`** joins names with commas and "and" ("Captain Brask, Ryo and Vera").
- **Kit is never "he" or "she":** see the continuity notes.

## 1. Header and footer

**Header:** `GUS'S CARDS` replaces "CARD SHOP". It fits the 400px header box at 34pt. The story always calls it "the
shop", and "Card Shop Tag Night" keeps its name.

**Footer:**
- **What it is:** one Gus remark per shop visit, on the status line where the standing instruction is now ("Pick a set
  on the left…").
- **Messages win:** functional messages (Not enough DP, the wishlist notes) replace it until the next visit. The
  standing instruction moves into the empty detail panel, where it's needed anyway.
- **Picking:** a newly unlocked pool goes first, so a new remark gets heard. After that it's random, with no repeat
  until the eligible lines have all played.
- **Arrivals:** on a visit with an arrival (section 3), the arrival line takes the footer instead.

```
pool: header
-: GUS'S CARDS

# Small talk from the first tables. Not in New Game+ (it has its own pool).
pool: footer
band: Beginner
before: champion
old-gus: Prices are on the tags. Nobody haggles here. Ask Brask. He tried.
old-gus: Open your packs over the mat. Last kid who didn't is still looking for a foil under the radiator.
old-gus: Ryo bought six packs yesterday and wrote down every pull. Even the commons.
old-gus: Tink keeps the wrappers. I've stopped asking what for.
old-gus: Vera only buys a pack after she wins. Says it's better that way.
old-gus: Coffee's for paying customers. ...Fine. Have a cup.

# Any time.
pool: footer
old-gus: Sleeves are by the register. A card you're going to play deserves one.
old-gus: Back in '04, this whole shop was one folding table and a shoebox of packs.
old-gus: Don't lean on the glass case, kid. It's older than you and less forgiving.
old-gus: A pack's a maybe. A card in your deck is a yes. Know which one you're paying for.
old-gus: Commons win more duels than anybody admits.

pool: footer
after: tag-night
old-gus: Friday nights I sell more sleeves than packs. Tag duelists are nervous people.
old-gus: That's Kit at the next table. Same binder of spares every night. I've never seen the bottom of it.
kit: Gus says he doesn't sort the commons. Gus sorts the commons.

pool: footer
after: novice
old-gus: The regional crowd shops here now. They buy by the box and complain by the pack.
old-gus: A kid from the regional asked if I stock frogs. I said this is a card shop.
old-gus [era]: Hollis made me tape the list to the register. Now people read it instead of paying.

pool: footer
after: intermediate
old-gus: Synchro kids. They buy one Tuner and ask me to find them a deck that goes with it.
old-gus: Haven't seen this many out-of-towners since the shop had carpet.

pool: footer
after: advanced
old-gus: Wren came by. Bought one pack and took an hour to open it.

pool: footer
after: golden-age-clear
old-gus: Ines used to buy the last pack on the shelf just so I couldn't.

pool: footer
after: expert
old-gus: These days half my sales are cards that stop other cards. Back in my day, we had walls.
old-gus: Vera's been in every day since the Expert Series opened. Says she's just browsing.

pool: footer
after: master
old-gus: Master League folks come in to see where you started. I point at the table by the window.

pool: footer
after: october-list
old-gus: New list went up on the wall. Half my case dropped in price overnight. The other half doubled.

pool: footer
after: champion
old-gus: The champion's discount is zero percent. I checked.
old-gus: People ask which table the champion sat at. I've started charging. Not really. ...A little.

pool: footer
after: library-opens
old-gus: Hollis only comes out of that back room for coffee and receipts.

pool: footer
after: cup-goat-clear
old-gus: Ines came in and bought a 2005 pack. Didn't open it. Just looked at it a while.

pool: footer
after: every-era-corkboard
old-gus: Customers keep asking whose name is over mine on the board. I tell them: a regular.

pool: footer
after: new-game-plus
band: Beginner
old-gus: The rookies want to know why the new kid's binder is already full. I tell them to keep buying.
```

That's 31 remarks: 6 at the first tables, 5 any time, 18 that unlock with the story, and 2 for New Game+ and the
epilogue. A player in the Beginner band draws from 11 (14 after Tag Night). The first tables' 6 drop out after the
Beginner band, so the Master League pool is about 19.

**Kit's first line:** it plays once, before any other Kit line, the first time Kit would speak in the shop. Kit's
`partner-kit` scene can come earlier or later, and this line reads right either way.

```
pool: kit-hello
kit: Don't mind me. Kit, next table over. I watch everybody's pulls.
```

## 2. Big pulls

**Rationing** (so a line stays a moment):
- **How many:** at most one line per pack, under the summary once the last card is up. Never a line on a pack whose
  best card is below Ultra, unless it's an opponent, recipe or deck card (below).
- **Which one,** when a pack qualifies more than once: Starlight > Ghost > Quarter Century > opponent > recipe > deck >
  Collector's > Ultimate > Secret > Ultra.
- **Spacing:**
  - Ultra fires on about 1 in 3 Ultras, since it comes about once in 3.5 packs.
  - Secret, Ultimate and Collector's fire every time, unless the pack before had a line.
  - Ghost, Quarter Century and Starlight always fire.
- **No repeats** of a line until its pool has cycled.
- **Opponent lines:**
  - **The card:** one of that opponent's key cards (scouting.txt `keys:`, the playbook's bosses, or the signature),
    not a shared staple. Otherwise every Ash Blossom fires.
  - **Who counts:** an opponent you haven't beaten yet in the current circuit, or one in the next circuit.
  - **Once** per opponent per circuit.
- **Where they never fire:**
  - opponent lines on house packs (every card in one is the opponent's);
  - recipe lines on recipe packs (they're missing-first);
  - anything on Prize Packs, which get Hollis's own pool.
- **House packs top out at Ultra** (DeckAccess.cs `RarestUpTo(..., UltraRare)`), so only the Ultra pool fires on them.
  That's correct, not a bug.
- **Kit's lines** need `after: tag-night`. Before it, the Gus fallback lines cover opponent pulls, and the other pools
  just have fewer lines.

**Rarity tiers**, mapped from `Rarity` (Printings.cs):

| Tier | Rarities |
|---|---|
| `ultra` | UltraRare, UltraParallelRare, PharaohsRare, GoldRare, PremiumGoldRare, PlatinumRare |
| `secret` | SecretRare, PrismaticSecretRare, GoldSecretRare, ExtraSecretRare, PlatinumSecretRare |
| `ultimate` | UltimateRare |
| `collectors` | CollectorsRare |
| `ghost` | GhostRare, GhostGoldRare |
| `quarter-century` | QuarterCenturySecretRare |
| `starlight` | StarlightRare |

`{rarity}` is the pulled card's rarity name, so a Gold Rare is never called "Ultra Rare".

```
pool: pull:ultra
old-gus: There's the shiny one. Sleeve it before Tink asks what it's made of.
old-gus: {rarity}. That pack just paid for itself. Don't tell the next three.

pool: pull:ultra
after: tag-night
kit: Ooh. That's a corkboard card. ...Fine, a binder card. Same thing.

pool: pull:secret
old-gus: {rarity}. Hold it by the edges, kid. I've got a case for those.
old-gus: Pulled my first Secret in '04 and didn't play it for a year. Don't be me.

pool: pull:secret
after: tag-night
kit: No way. Hold it still, I want to see the foil under the light.

pool: pull:ultimate
old-gus: Run your thumb over it. They printed those to be felt, not just looked at.

pool: pull:collectors
old-gus: Collector's Rare, they call it. I call it a card. Play it.

pool: pull:ghost
old-gus: {rarity}. I've sold three of those since they started printing them. That makes four.

pool: pull:ghost
after: tag-night
kit: Is that a Ghost? It is. Okay. Give me a second.

pool: pull:quarter-century
old-gus: Quarter Century. Twenty-five years of cards, and the anniversary lands in your pack.

pool: pull:starlight
old-gus: ...Starlight. Don't move. I'm getting the good sleeve.

pool: pull:starlight
after: tag-night
kit: Starlight. I've only ever seen one at a regional, behind glass.

# A key card of an upcoming opponent. Kit scouts; Gus covers before Tag Night.
pool: pull:opponent
after: tag-night
kit: That's in {opponent}'s deck. Nice to see one from this side of the table.
kit: {card}. I watched that card win a game for {opponent}. Twice.
kit: Somewhere, {opponent} just felt a chill.
kit: That copy's worth a lot to {opponent}. Don't trade it.
kit: Remember that one. You'll see it again, across the table from {opponent}.
kit: Pulling {opponent}'s cards before you face {opponent}. That's either scouting or a sign.
kit: That's a page out of {opponent}'s deck. Read it before it gets read to you.

pool: pull:opponent
before: tag-night
old-gus: That's {opponent}'s kind of card, kid. Better in your binder than across the table.
old-gus: Same card {opponent} bought off this counter last month. Small town.

# The main cast, by id (these win over pull:opponent).
pool: pull:opponent:old-gus
old-gus: That's out of my own deck, kid. Sold it to you anyway. Fair's fair.

pool: pull:opponent:vera
after: tag-night
kit: Vera plays that. Don't let her see you pull it. She'll want a rematch before it's sleeved.

pool: pull:opponent:hollis
after: tag-night
kit: That's one of the Professor's. He'll ask which pack it came from. For his notes.

pool: pull:opponent:tink
after: tag-night
kit: That's a Tink part. Tink's going to want the wrapper too.

# A card your active recipe is missing (a new copy toward it), or a new copy or upgrade of a card in your deck.
pool: pull:recipe
kit: One off the recipe. Cross it out.
old-gus: That's the one your deck was waiting on. The pack earned its price.

pool: pull:deck
old-gus: That one already has a seat in your deck. Good. A card earns its price by getting played.
kit: You run that, right? Now you've got a spare. That's how a binder starts.
```

That's 13 rarity lines, 9 opponent lines plus 4 for the main cast, and 4 recipe and deck lines: 30 in all. The Kit
and Gus opponent pools never play at the same time.

**Gus's "Small town" line** (`before: tag-night`) can only fire in the Beginner band, whose opponents are all locals.
Tag Night opens at the Rookie Cup clear, so it holds.

**Hollis's Prize Packs:** the Library Locals bracket reveals them on this screen, and `first-bracket-title` has him say
"Open them here, where I can watch". They get his own two lines, at most one per bracket, on the slab or a Secret or
better.

```
pool: prize-pull
hollis: Ah. I'd hoped that one would find you. Noted.
hollis: A fine pull. I'd like to claim I planned it. Statistics won't let me.
```

## 3. New arrivals

**When it fires:** the first shop visit after `ShopYear` rises, whatever raised it. `ShopYear` also climbs a year per
level inside a band (Profile.cs `ShopYear`), so the shop can already be at 2011 before the regional circuit opens. That's
why these lines name the year and the stock and never the circuit.
- **Which line:** a year with its own pool (the era years in circuits.txt) uses that one; any other year uses the
  generic pool.
- **Several years at once:** the newest year speaks.
- **Where:** the line takes the footer for that visit, and the sets go on the "New arrivals" shelf.

```
# The first visit: the shop's opening stock.
pool: arrival:2005
old-gus: Everything on the wall's from 2005 or before. Good year. Best one, if you ask me.

pool: arrival:2011
old-gus: 2011 boxes came in this morning. Mind the frogs.

pool: arrival:2015
old-gus: Truck came in. 2015 stock's up front. Don't open it all at the counter.

pool: arrival:2019
old-gus: 2019 boxes, just in. Half the cards point arrows at each other. Ask Hollis, not me.

pool: arrival:2023
old-gus: 2023 stock's out. These cards have more words on them than my lease.

pool: arrival:2026
old-gus: 2026, kid. Same as the calendar. You've caught up.

# The years between, one a level.
pool: arrival
old-gus: A few {year} boxes came in. Top shelf, left side.
old-gus: Found some {year} stock in the back. Priced it fair.
old-gus: {year}'s on the shelf now. Open it over the mat.
```

**Notes:**
- **2005** is Gus and Ines's Goat summer (`cup-goat`). "Best one, if you ask me" plants it without explaining, as a
  reward for lore hunters.
- **2011 "Mind the frogs"** comes before the player has met Ribbit Ruiz on purpose. It pays off at the Underdog Open and
  the Novice Tag Cup flyer.
- **2019 "Ask Hollis"** is safe: Hollis is met at the Regional Qualifier, so before any route to 2019.

## 4. Gus's house packs (were "Selection Pack: <deck> 2009")

**What changes:** the name becomes "House Pack: <deck title>", and the fake year goes. In a shop like Gus's, a *repack*
is loose cards bagged and taped at the counter. That's exactly what these are: the cards of one circuit deck. They
also stay distinct from Hollis's Prize Packs ("a pack Hollis puts together himself", `first-locals`).

```
pool: house-pack:name
-: House Pack: {deck}

# The description line (detail panel and list row), with the deck's players from circuits.txt.
pool: house-pack:about
-: Gus's own repack of {opponents}'s deck, bagged at the counter.

# When Old Gus is among the deck's players (Legendary Dragons, Undead Legion).
pool: house-pack:about-gus
-: Gus's own repack of his deck. He swears he kept nothing back.
```

**Where it shows:**
- **List row:** "House pack" where the date was, e.g. "House pack · 41 cards · you have 12".
- **Detail line:** "House pack · Gus's own repack of Damon Vey's deck, bagged at the counter · 41 cards · 9 cards per
  pack".
- **Other wording to change with it:** ShopScreen.cs:236-238 and 257, the QA log at ShopScreen.cs:745, and anything
  else the player reads that says "selection pack". The code can keep its own names.

## 5. Featured shelves, the back shelves and boxes

**Shelves:**
- **Title:** "For the {circuit}" (e.g. "For the Rookie Cup", "For the Master League: Link Wars").
- **The line:** one per circuit, shown under the shelf while that circuit is the newest one open.
- **Tag Cups** use the shelf of their `after:` circuit.
- **The Library** has no featured shelf here; its binders are Hollis's.
- **What the lines do:** each tells you whose decks live on that shelf, through a person, not a mechanic.

```
pool: shelf:Beginner Circuit
old-gus: Ryo built a whole deck off this shelf, one allowance at a time. Brask bought the same packs in one go.

pool: shelf:Rookie Cup
old-gus: Tink's machines and Brask's both came out of these boxes. So did my dragons. No shame in it.

pool: shelf:Town Tournament
old-gus: My zombies, Tink's walls, Vera's warriors. All this shelf. Vera bought most of it.

pool: shelf:Regional Qualifier
old-gus: Hollis built his walls out of these packs and a library card. Vera borrowed my zombies. Same shelf.

pool: shelf:Underdog Open
kit: Ribbit Ruiz cleaned this shelf out once. Isamu Tokiwa bought what was left and bowed to the register.

pool: shelf:Trap Masters Cup
kit: Gears, traps and one little rat. Ostrand swears he never opened a pack in his life. These say otherwise.

pool: shelf:Small Engines Invitational
kit: Small monsters, every one. Pip bought these for the cat art and kept winning anyway.

pool: shelf:Synchro Dawn Classic
old-gus: Damon Vey's dragon, Kestrel's birds, Solenne's light. In 2009, every table in town ran something off this shelf.

pool: shelf:Xyz and Link Regionals
kit: Bunny Rex's rabbit, Hornet Vale's bugs, QL-1's machines. Coach Gouki just buys these for the wrestlers.

pool: shelf:Link Age Open
kit: Ace Raye runs one monster and a pile of spells. Every one of those spells is on this shelf.

pool: shelf:Golden Age Championship
old-gus: Wren's goats, Corvus's crow, Ignatia's dragons. I sold these packs the first time around. Some of them twice.

pool: shelf:Control Kings Cup
kit: King Barrund won't be seen buying packs. Bea's in here every week for parts.

pool: shelf:Hybrid Engines Invitational
kit: Celandine, Sorrel, Annick, Rex. Four engines that bolt onto anything. The bolts are on this shelf.

pool: shelf:Expert Series: Shadows and Storms
kit: Nadia buys strings here. Captain Mae buys ships. Tock buys one pack, then asks to see it again.

pool: shelf:Expert Series: Rituals and Fire
kit: Sotelo's fleet and Kenta's dragons both go off in one turn. Every piece of both turns is on this shelf.

pool: shelf:Expert Series: The New Wave
kit: The Castell twins bought two of everything on this shelf. Of course they did.

pool: shelf:Expert Series: Fall Format
kit: Nobody preps for the rogue decks. That's why this shelf is still full.

pool: shelf:Master League: Link Wars
old-gus: Aurum's gold, Volta's storms. I sold out of these twice the year they came out.

pool: shelf:Master League: Graveyard Kings
kit: Coralie fills her Graveyard. Pilar sends your cards away face-down. The cards that do both are on this shelf.

pool: shelf:Master League: Houston 2026
kit: Houston's top tables, a month old, and already on Gus's shelf. That's how you know it's real.

pool: shelf:Master League: October 2026
old-gus: New list, new decks. I put these out the morning it went up. Jolt was waiting at the door.

pool: shelf:Tier 0 Gauntlet
old-gus: Everything those four play is on this shelf. Whatever you buy tonight, buy it to play.
```

**Who has which shelf:**
- **Kit's** all come after the Town Tournament, so Kit is past `tag-night` by then.
- **Gus has** the first four, then the ones he lived through or stocks himself: Synchro Dawn, Golden Age, Link Wars,
  October and the Gauntlet.

**The back catalog** is named "Back shelves", with one line:

```
pool: back-shelves
old-gus: Everything else I've got, by year. The dust is free.
```

**Boxes:**
- `box` plays when a box is opened (once per box, not per pack).
- `box-best` replaces the rarity line on the pack holding the guaranteed foil.
- `{packs}` is the box's pack count, so the line doesn't depend on the spec's box size.

```
pool: box
old-gus: A whole box? Sit down, then. I'll get you a bin for the commons.
old-gus: {packs} packs. Open them slow, kid. A box only gets opened once.

pool: box
after: tag-night
kit: Box night! I'll sort your commons. Just the commons. ...Mostly.

pool: box-best
old-gus: There's the one the box promised. Everything else was your own luck.
```

## 6. Odds disclosure

One line heads the odds panel, and the numbers under it stay plain. A box's panel adds its guarantee as a plain line
("1 {rarity} or better guaranteed per box"), not in voice.

```
pool: odds
old-gus: Odds are printed on the wrapper. I can't change them, and I wouldn't.
old-gus: Same odds for everybody, me included. I've checked.
```

## Continuity notes and risks

1. **Kit's pronoun is inconsistent.** docs/dev/PERSONA-WALKTHROUGH-2026-10-30.md says "her", PACKS-REVIEW says "his", and
   the profile, the story and the code use neither. Every line here avoids one; an owner call would settle it.
2. **The Grading Desk already has a face:** Odile Varga, "the Loupe", for Gauge & Gilt (GradingDeskScreen.cs).
   PACKS-REVIEW's idea 4 (Hollis at the Grading Desk) is stale, so nothing here gives Hollis the desk. Kit running the
   Card Trader is still open but isn't written.
3. **Era openings aren't circuit openings,** because ShopYear climbs with levels. The arrival lines are written for
   that. The existing `intermediate` line, `hollis [open]: A new era: the shop now stocks sets up to 2015.`, can be
   stale when levels got there first. A softer line would work in both cases:
   `hollis [open]: A new era, and the shop's shelves go up to 2015.` The same goes for its `[era]` twin.
4. **The Back Alley thread:** Ines's lost 2005 card and Gus closing the alley in 2005 aren't shipped. Two lines echo it
   without resolving it: `arrival:2005` ("Best one, if you ask me") and the `cup-goat-clear` footer (Ines looking at an
   unopened 2005 pack). The Alley's writer should see both. If the Alley's payoff changes, cut the second line.
5. **Gus's glass case stays shut:** `bond-old-gus-5` says nobody has seen it open in years. Lines mention the case but
   never open it ("Don't lean on the glass case"; "I've got a case for those").
6. **Words kept fresh:** no line repeats a story line, since the rarest story moments lose force when the shop says them
   every visit. "Two bucks a pack" (`gift-old-gus`), "I don't do discounts. I do fair" (`bond-old-gus-3`) and "Points
   get spent, kid. Cards keep" (`gus-lessons`) all stay where they are.
7. **Who Gus knows when:**
   - Wren only after `advanced`, and Ines only after `golden-age-clear`.
   - Hollis's list taped to the register only in era play, after `novice`.
   - The Duchess never: she's planted by the corkboard and `champion`, and the shop shouldn't steal that.
8. **New Game+ keeps Profile.Seen,** so story-gated remarks stay unlocked on a second run. That's intended: the player
   knows it all. The Beginner small talk has `before: champion`, so a New Game+ run hears its own pool instead.
9. **Names with plurals or titles:** Nico and Nina Castell, Unit QL-1 and Captain Mae Tolliver all read right in every
   `{opponent}` line, because the name is never a verb's subject. The shelf lines use the short forms the story
   already uses (Captain Mae, Aurum, Sotelo).
10. **Opponent lines name people you haven't met yet** (the next circuit). The map already shows their names, and no
    line says anything about them beyond their deck.
11. **The footer shares its line with functional status text.** The rework needs a rule (messages win until the next
    visit), or the remark gets its own line.
