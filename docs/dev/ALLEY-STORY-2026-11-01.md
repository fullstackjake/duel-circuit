# The Back Alley: story, cast voices and lines (2026-11-01)

The Narrative Designer's part of POST-GAME-PLAN-2026-10-31 step 9. It builds on that plan's "The Back Alley" and
"Decided" sections, docs/dev/HEADLINERS-2026-11-01.md, campaign/story.txt, campaign/shop.txt and
docs/dev/SHOP-VOICE-2026-11-01.md. Nothing in the game changed.

**Matched to the build spec** (docs/dev/ALLEY-SPEC-2026-11-01.md, section 12) **and to docs/dev/ALLEY-ECONOMY-2026-11-01.md**:
- **Triggers:** the spec's names (`alley`, `alleyvisit`, `open:`/`alleyclear:<table>`, `headliner:`/
  `headlinerbeat:`/`combobreaker:<id>`, `encore`, `fence`, `antewin`, `antelose`, `rail`, `buyback`, `railgone`,
  `turncap`, `firstturn`, `unlisted`, `brawl`, `midnight`, `bracketwin:<name>`). Every trigger the spec raises has a
  scene here, because `--campaign-test` flags any that don't.
- **Pools:** `campaign/alley-voice.txt` and `campaign/van.txt`, in shop.txt's format, with the spec's hook names.
- **Placeholders:** the spec's `{card}`, `{table}` and `{handle}`.
- **Handles:** the spec's `handle: <card> | <prefix>` and `names:` lines.
- **Proposed on top of the spec** (each marked **[new]** where it's used):
  - `heldcard`, in place of the spec's optional `inescard` (section 3);
  - `pennywin`;
  - the placeholders `{nick}`, `{price}`, `{left}` and `{step}`;
  - a few extra pool hooks;
  - the speaker token `opponent` (the CPU you just played, like `partner`).
- **Where the spec and the economy disagree, the lines fit both:**
  - fee or held stake: no line says whether a win returns your DP;
  - Contraband Packs sold at the van or prize-only: the `van:pack` lines read either way.
- **Moth is "he",** as the spec and the economy already write him. Every Moth line here is pronoun-free anyway.
- **Where things go:** the scenes are a new block in story.txt, after the Every Era block and before the level
  scenes. Gus's footers go in shop.txt.

## The story in one paragraph

Hollis's back room keeps every list since 2004. The shop's back door opens on a place that keeps none. In 2004 Gus
carried his folding table out there on Fridays, and the regulars played for each other's cards. In August 2005 he
beat Ines at that table, and her Scapegoat, the copy she'd played all summer, went on the rail. She wouldn't claim a
card she'd lost fair. He couldn't hand it back without making it charity. The week ran out, and he folded the table
and never went back out. The tables came back years later under Patron Orvel, with Gus's old table under a
tablecloth and Gus's rule still on it: spares only. Kit brings you down. You climb from the bins to the far lantern,
and Vera learns what anteing costs. When you finally take the House table, the card Moth has held for 21 years goes
back up the stairs with you, and Ines pays the claim herself.

**Theme:** the Library asks what each era's rules were. The alley asks what a rule costs the people who keep it.
- Gus keeps his rules: "A shop rule's a shop rule. I made it up, so I'd better keep it." (`gift-old-gus`)
- Ines keeps her values: "I check values." (`bond-ines-3`)
- Between them, it cost a summer's card for 21 years.

## 1. Hooks

| Trigger | Fires (spec section 12) | Scene(s) here |
|---|---|---|
| `alley` | The alley opens, after the first cup clear's scenes | `alley-rumor` |
| `alleyvisit` | The Back Alley tab first shown | `alley-kit`, `alley-baz`, `alley-upstairs` |
| `fence` | The van's first visit | `van-first` |
| `open:<table>` | A table's panel first opens | one per table |
| `alleyclear:<table>` | A table's first clear | one per table, plus `alley-closing` at Nyx's |
| `antewin` / `antelose` | Your first stake won / your first alley loss with a stake down | `first-ante-win` / `first-stake-lost` |
| `rail` | Your first card on Moth's rail (only at Orvel's Table) | `orvel-first-loss` |
| `buyback` / `railgone` | Your first buy-back / Moth first sells one of yours off the rail | `first-claim` / `first-railgone` |
| `turncap` / `firstturn` / `encore` | The first duel decided at turn 40 / the first first-turn refund / the first encore | one each |
| `headliner:<id>` | Before your first duel against a headliner | one per headliner |
| `headlinerbeat:<id>` | Your first win against a headliner | one per headliner |
| `combobreaker:<id>` | That headliner's Combo Breaker poster | one per headliner |
| `unlisted` | The first Unlisted page | `unlisted` |
| `brawl` / `midnight` | First entry | `alley-brawl` / `midnight-open` |
| `bracketwin:Alley Brawl` / `bracketwin:Midnight Open` | First title | one each |
| **[new]** `pennywin` | Your first win against Penny (her Common changes hands) | `penny-lost` |
| **[new]** `heldcard` | **Both** `alleyclear:Orvel's Table` and the `cup-goat-clear` scene seen, whichever comes second | `held-card`, `held-card-home` |

`heldcard` replaces the spec's optional `inescard` (a flagged stake in Orvel's binder). Section 3 says why.

**Placeholders:** `{card}`, `{table}`, `{handle}` (spec) and `{grade}` (plan). **[new]**:
- `{nick}`: Baz's chalk name for you (section 4);
- `{price}`: a rail tag;
- `{left}`: rail duels left;
- `{step}`: where a combo fizzled.

**The Back Alley tab's status line** (spec section 5 asks Narrative for it):
`The Back Alley · one copy of anything · stake Duel Points, win their card · your own cards ride only at Orvel's Table`

## 2. Scenes

```
# ---------------------------------------------------------------------------------------------------------------------
# The Back Alley (post-game, docs/dev/ALLEY-STORY-2026-11-01.md, ALLEY-SPEC section 12): behind the shop's back door, under
# string lights. Opens with the first Library cup clear, whichever cup it is, so no scene here names a cup. Gus, Hollis,
# Ines and Ilse never come down. Moth never says "stolen", and nobody calls Moth a fence. Kit is they/them.
# Threads paid off: Ines's lost 2005 card (cup-goat-clear; shop.txt's arrival:2005 and cup-goat-clear footer), Kit's
# bottomless binder (shop.txt footer), Nocturne's name (nocturne.profile lose line).
# ---------------------------------------------------------------------------------------------------------------------

scene: alley-rumor
when: alley
-: On your way out of the back room, you pass the stockroom door. Someone has chalked a bracket on it.
-: Eight names, none of them real. Underneath, in smaller chalk: FRIDAYS. SPARES ONLY.
kit: You saw that. I didn't chalk it. I know who did.
kit: Hollis keeps every list since 2004. Out back, past the bins, there's a place that keeps none.
hollis: "Place" is generous. It's an alley with opinions. I'm told it's very lively. I've never checked.
vera: A place with no list? And nobody told me?
kit: Because Gus is standing right there.
-: Gus is standing right there. He goes on pricing sleeves.

scene: alley-kit
when: alleyvisit
-: Friday, after close. Kit holds the shop's back door open with one foot. It sticks.
kit: Watch the step. And the bins. And Baz.
-: String lights zigzag between the fire escapes. Crates for chairs, chalk on the bricks.
-: An ice-cream van is parked by the bins, its hatch open.
kit: No list down here. One copy of anything. And everybody shows their contraband before the duel.
kit: You put up Duel Points, they put up a card. Win, and the card's yours.
kit: Nobody touches your binder. Except at one table, the one with a tablecloth. You'll know it.

scene: alley-baz
when: alleyvisit
-: A brass bell rings. A man in a chalk-dusted waistcoat climbs onto a crate.
baz: Spares! You brought somebody! Name? No, don't tell me. I'll only get it wrong on purpose.
baz: Came down from the shop, did you? Then you're Upstairs. Chalk it!
-: He writes UPSTAIRS at the bottom of the board and underlines it twice.
kit: That's Baz. Baz names everybody. I've been Spares for three years.
vera: What am I?
baz: Red. Obviously.

scene: alley-upstairs
when: alleyvisit
-: Later, upstairs. Gus is counting the register. He doesn't look up.
old-gus: Chalk on your sleeve, kid.
old-gus: Spares only. That's the whole speech.

scene: van-first
when: fence
-: The van's hatch is propped open with a broom handle.
-: Inside: a rail of toploaders on bulldog clips, and one lit glass shelf.
moth: Under the Counter. Used cards, graded cards, cards between binders.
moth: The rail's for claims. Lose a card down here, it hangs five duels. Claim it at the tag.
moth: The shelf's the good stuff. Every card on it got banned somewhere, sometime. Slabbed, and as-is.
-: At the far end of the rail hangs one toploader, face down, with a pencil tag gone grey: HELD.
moth: That one's not for sale. Never has been.

scene: dumpster-row
when: open:Dumpster Row
-: The first tables are crates by the bins. The contraband here is old.
-: Most of it was banned before half the crowd could shuffle.
baz: Dumpster Row! Street stakes! Three hundred on the crate, and their card goes up against it.
baz: Everybody shows what they're carrying. Down here, that's manners.
kit: The fourth seat isn't splashing a few cards. The fourth seat has a whole act.
penny: Evening, Upstairs. Same card as always. It likes the fresh air.

scene: first-ante-win
when: antewin
-: Your opponent slides {card} across the crate without a word. It's yours now.
kit: The first one's always strange. You get used to it. ...Mostly.
vera: You won their CARD? Out of their actual deck? Okay. I'm anteing. Tonight.
kit: Vera. Spares only.
vera: Sure. Spares. Totally.

scene: first-stake-lost
when: antelose
kit: That's only points. The crate keeps them, you keep your binder. That's the deal out here.
penny: I lost my first stake in about four turns. Then I found the right card.

scene: dumpster-row-clear
when: alleyclear:Dumpster Row
baz: The closer's down! Wipe the crate! Upstairs, you're not Upstairs anymore.
baz: You cleaned out the curb. That makes you Curbside. Chalk it!
baz: My chalkboard's open too. Alley Brawl: eight seats, one bell, a thousand to enter.
kit: The Basement's next. Different crowd. They count their outs.

scene: vera-ante
when: open:The Basement Game
-: Vera's at Moth's hatch, counting Duel Points into a paper cup.
vera: Don't. Don't say anything.
vera: I anted Marauding Captain. He's in my deck. He's always in my deck.
vera: One Change of Heart, and my own captain walked me off the crate. In my own colors.
moth: Claimed inside two duels. Tag price. As-is.
vera: Half again. I paid half again for my own card.
baz: HALF-AGAIN! Chalk it!
vera: Do NOT chalk it. ...I'm still coming Fridays. I'm just never anteing again.

scene: basement
when: open:The Basement Game
-: Down a stairwell under the laundromat. Dryers thump through the ceiling. The decks run from 2010 to 2014.
baz: The Basement Game! Den stakes! A thousand on the table, and the cards against it get serious.
kit: Synchros, Xyz, and the cards that made them unfair. Everybody down here keeps a Tuner in a pocket.

scene: basement-clear
when: alleyclear:The Basement Game
baz: The Basement's cleared! Somebody tell the dryers!
baz: Curbside went underground. You're Downstairs now. Chalk it!
kit: The Black Market's next. Newer cards, faster turns, and a tarp for the rain.

scene: black-market
when: open:The Black Market
-: Past the van, a row of hatchbacks with their boots open under a blue tarp.
-: It's a car-boot sale, and every deck in it is from 2015 to 2019.
baz: The Black Market! Everything under this tarp got banned for doing its job too well!
kit: Hand traps, Links, one-card turns. Keep something in hand for their turn.
mika: Chat's asking who Downstairs is. I told them to stay and find out.

scene: black-market-clear
when: alleyclear:The Black Market
baz: The tarp is yours! Downstairs, take a bow. You're Boots now. Chalk it!
baz: The Midnight Open's on my board. Sixteen seats, best of three in the final, and the champ's defending.
radiant-typhoon: I've held the top of that board all season. Come and take the weather off it.
-: Under the brightest bulb, someone smooths a white tablecloth over a folding table.
-: At the far end of the alley, under the one lantern, someone sets out four chairs.

scene: nyx-table
when: open:Nyx's Table
-: A lantern hangs on a nail over a card table.
-: Beside it sits a crate of decks, each one held shut with a rubber band.
nyx: The cards at this table say what they said when they were printed. Before anyone changed a word.
nyx: Witch, Sangan, the Emperor, the crow. I keep them the old way. Somebody has to.
nyx: Four seats. Mine is the last.
baz: Nyx's Table. Even I use my inside voice here.

scene: nyx-table-clear
when: alleyclear:Nyx's Table
nyx: Back in the crate, then. They've rested before.
nyx: Old words don't end when they lose. They wait for someone who'll read them properly.
-: Nyx turns the lantern down a notch, and the far end of the alley goes back to being a wall.
baz: Nobody clears Nyx's Table! ...Somebody just did! Big letters!

scene: alley-closing
when: alleyclear:Nyx's Table
-: Two in the morning. Baz wipes the board down to bare slate, then chalks one name back at the top.
baz: {nick}. Not your real one. I don't do real ones.
penny: Same time tomorrow? The card likes a routine.
nocturne: That bell used to hang over a shop door, Baz.
baz: It rings better out here.
kit: Gus is going to smell the chalk on you again. ...Worth it.
-: Upstairs, the light's still on in the shop. Somebody waited up.

scene: orvel-table
when: open:Orvel's Table
-: A folding table under the brightest bulb, a white tablecloth over it. One of its legs is taped straight.
power-patron: The House table. Three thousand on the cloth. I stake slabs, nine and ten.
power-patron: You stake one spare card. Never a card a deck of yours needs.
power-patron: That rule came with the table. I kept both.
power-patron: Lose, and your card goes on Moth's rail. Claim it at the tag, or it finds another binder.
nocturne: Your Gus set this table up in 2004. He folded it in 2005, after one bad night.
nocturne: He left it out here when he went back inside. He left the rule on it too.
odile: I'm only here for the slabs. Gauge & Gilt checks every one the House puts down.

scene: orvel-first-loss
when: rail
-: Orvel writes {card} in the ledger, in ink, and hands it to Moth.
moth: On the rail. Five duels. The tag's on it.
kit: Everybody loses one at that table. That's why it's spares only.
power-patron: A temporary position. Most people buy back in.

scene: first-claim
when: buyback
moth: Claimed inside the window. The tag goes in the drawer.
-: The drawer is full of old tags. At the end of the rail, one hook still has its tag on.
moth: Most people claim. One never did.

scene: first-railgone
when: railgone
moth: The clock ran out on {card}. It's between binders now. Not yours. As-is.
kit: It happens. It happened to me. I still look for it in other people's binders.

scene: orvel-table-clear
when: alleyclear:Orvel's Table
power-patron: The House pays. It always does, eventually. Your slab, and my compliments.
power-patron: People have beaten this table before. Never anyone from upstairs.
-: Orvel closes the ledger on its ribbon and stands up from the table for the first time all night.
baz: THE HOUSE IS DOWN! Somebody hold my chalk, I'm ringing this with both hands!

scene: held-card
when: heldcard
-: Moth is waiting at the van's hatch, holding the toploader from the end of the rail.
moth: August 2005. This came off the House table, and it's been held since.
moth: The one who lost it wouldn't claim it. The one who won it wouldn't take the claim.
moth: So I made a rule for it. It goes up the stairs with the next one from that shop to take that table.
moth: Twenty-one years. The shop finally sent somebody. As-is.
-: You turn it over. Scapegoat. One corner is soft, the way a card gets when someone holds it all summer.
odile: Raw. I'd call it a six, Excellent. Honest wear, and all of it from one summer.

scene: held-card-home
when: heldcard
-: Upstairs, Ines is at the counter. Gus is pricing sleeves that already have prices on them.
-: You set the toploader down between them.
ines: ...That's my corner. I bent it in August.
old-gus: Moth kept it. Course Moth kept it.
ines: You won it fair, Gus. That was the whole trouble.
old-gus: And if I'd handed it back, you'd have called it charity. You'd have been right.
ines: I still have the claim. Half again on the tag. I counted it out that week.
-: She takes an envelope from her coat. Its corners are soft too.
ines: Take this down to Moth. It's the right amount. It was always the right amount.
old-gus: Best year. I keep telling people.
-: Ines tears open the 2005 pack she's been carrying around. Commons, every one. She laughs anyway.

scene: first-turncap
when: turncap
baz: TURN FORTY! Cards down! Down here we don't play till sunrise. Higher Life Points takes it.

scene: first-firstturn
when: firstturn
baz: Lost before your first turn? The House doesn't charge for that. Pick your stake back up.
moth: Didn't get to draw, didn't get to pay. Fair's fair.

scene: first-encore
when: encore
baz: An ENCORE! Same act, same crate! The crowd paid for this. The crowd didn't pay. Encore anyway!

scene: alley-brawl
when: brawl
-: Baz chalks eight names onto the board. None of them are real.
baz: Alley Brawl! Eight seats, single elimination, one ring of the bell between rounds!
baz: A thousand to enter. The prize is contraband, sealed by Moth and opened by you.
mika: I'm streaming it. Don't look at the camera. ...Okay, look a little.

scene: alley-brawl-win
when: bracketwin:Alley Brawl
baz: {nick} takes the Brawl! Ring it twice!
-: Baz rings the bell twice, and the crates applaud by thumping.
mika: Chat says lucky. Chat is wrong. That was three clean lines.
moth: Sealed this afternoon. As-is.

scene: midnight-open
when: midnight
-: Midnight. Sixteen names on the board.
-: Some are chalked. Some are signed properly, by people down from the circuits and the Library.
baz: The Midnight Open! Circuit champs, Library bosses, alley regulars, and {nick}!
baz: Best of three in the final. The champ starts in the top seat. {nick} starts in the other one.
radiant-typhoon: Defending. The wind's been at my back all season. Let's see if it turns.
nocturne: I've played every Midnight since the first one. This one, I'm watching you.

scene: midnight-open-win
when: bracketwin:Midnight Open
-: Baz wipes the top of the board and chalks a new name over the old one.
baz: MIDNIGHT! That's the name now. Earned at two in the morning, best of three!
radiant-typhoon: The weather turned. It does that. I'll be back for the top seat.
nocturne: Come here. I promised someone my name once.
-: Nocturne leans close and says it. Baz rings the bell right over the last syllable.
baz: Sorry! Habit!

scene: unlisted
when: unlisted
-: Next morning, there's a decklist on Hollis's desk, chalk-smudged, on the back of a bracket sheet.
hollis: Kit brought this up. Kit wouldn't say from where. Kit is an appalling liar.
hollis: A deck that answers to no list. I've no year to file it under.
-: He takes an empty binder from the bottom shelf and writes on the spine: UNLISTED.
hollis: I won't be coming down. Someone should keep the lists. But I'd like a copy of every deck you beat.
hollis: Purely for scholarship. ...And the combos. I would very much like to see the combos.

scene: penny-lost
when: pennywin
-: Penny looks at the Kuriboh for a long moment, then slides it across the crate.
penny: Eleven years. It had a good run.
penny: Look after it. It likes the top of the deck.
baz: Lucky's lost the Common! Somebody check the sky!

# Headliners (HEADLINERS-2026-11-01): before the first duel (headliner:), the first win (headlinerbeat:) and the Combo
# Breaker poster (combobreaker:). Baz's tells also play on the versus (tell:<id> in alley-voice.txt); these play once.

scene: hl-yata-mags
when: headliner:yata-mags
baz: Tonight's act! Yata Mags, three Emperors and a crow! Keep a card back for after the wipe!
yata-mags: Empty hands, everybody. Then the crow.

scene: hl-yata-mags-beat
when: headlinerbeat:yata-mags
yata-mags: You drew past the crow. In 2004, people stood up for that.
baz: The crow's grounded! Chalk it!

scene: hl-yata-mags-breaker
when: combobreaker:yata-mags
baz: Stopped before the wipe! That goes on a poster!

scene: hl-return-rook
when: headliner:return-rook
baz: Return Rook! Banishes a whole field for one big comeback! Hit hard before it comes back!
return-rook: I send everything away. Then I bring it home.

scene: hl-return-rook-beat
when: headlinerbeat:return-rook
return-rook: I banished a whole field for that comeback, and you were still standing when it came back.
baz: Rook's comeback is cancelled!

scene: hl-return-rook-breaker
when: combobreaker:return-rook
baz: Nothing came home! Poster!

scene: hl-mass-driver-moss
when: headliner:mass-driver-moss
baz: Mass Driver Moss! Four hundred at a time! Bring something for the Graveyard!
mass-driver-moss: I can count to four hundred all night.

scene: hl-mass-driver-moss-beat
when: headlinerbeat:mass-driver-moss
mass-driver-moss: Four hundred at a time, and you outlasted the frogs.
baz: The Mass Driver's out of ammo!

scene: hl-mass-driver-moss-breaker
when: combobreaker:mass-driver-moss
baz: The loop's broken before the first shot! Poster!

scene: hl-trishula-trish
when: headliner:trishula-trish
baz: Trishula Trish! Wants a Synchro for nine! Stop the Tuner and the garden never grows!
trishula-trish: Plant it early, pick it at nine.

scene: hl-trishula-trish-beat
when: headlinerbeat:trishula-trish
trishula-trish: Nine stars, three cards gone, and you still had an answer. Rude.
baz: The garden's been weeded!

scene: hl-trishula-trish-breaker
when: combobreaker:trishula-trish
baz: Nothing bloomed! Poster!

scene: hl-master-plan-marlo
when: headliner:master-plan-marlo
baz: Master Plan Marlo! The whole board starts with one agent! Stop the first search!
master-plan-marlo: You don't have clearance for my first turn.

scene: hl-master-plan-marlo-beat
when: headlinerbeat:master-plan-marlo
master-plan-marlo: You read the plan. The plan was classified.
baz: Marlo's plan is declassified! Chalk it!

scene: hl-master-plan-marlo-breaker
when: combobreaker:master-plan-marlo
baz: The plan never left the briefing! Poster!

scene: hl-gumblar-gail
when: headliner:gumblar-gail
baz: Gumblar Gail! Links up into a hand rip! Spend your hand before Gail gets there!
gumblar-gail: Link, link, link, and then your hand.

scene: hl-gumblar-gail-beat
when: headlinerbeat:gumblar-gail
gumblar-gail: You spent your whole hand before Gumblar got there. Smart. Annoying.
baz: Gail's links are snapped!

scene: hl-gumblar-gail-breaker
when: combobreaker:gumblar-gail
baz: The chain snapped before the rip! Poster!

scene: hl-scientist-turtle
when: headliner:scientist-turtle
nyx: The third seat burns. It never attacks. It doesn't need to.
baz: Lab Coat Lyle! Mind your Life Points, not your monsters!
scientist-turtle: Science is mostly paperwork. And catapults.

scene: hl-scientist-turtle-beat
when: headlinerbeat:scientist-turtle
scientist-turtle: You ran out my budget. Nobody runs out my budget.
nyx: Rest, Lyle.

scene: hl-scientist-turtle-breaker
when: combobreaker:scientist-turtle
baz: Not one shot left the catapult! Poster!
```

**Running order** (one player's likely path):
1. `alley-rumor`
2. `alley-kit`, `alley-baz`, `alley-upstairs`
3. `van-first`
4. `dumpster-row`, then `first-ante-win` and `first-stake-lost`, then `dumpster-row-clear`
5. `alley-brawl` (and its win)
6. `vera-ante`, `basement`, then `basement-clear`
7. `black-market`, then `black-market-clear`
8. The three that open together (spec O1), in any order:
   - `midnight-open` (and its win);
   - `nyx-table`, then its clear and `alley-closing`;
   - `orvel-table`, `orvel-first-loss`, `first-claim`, `orvel-table-clear`
9. `held-card`, `held-card-home`

The rest fall wherever they land: `unlisted`, the headliner scenes, `penny-lost`, `turncap`, `firstturn`,
`encore` and `railgone`. No scene assumes an optional one played first, except the `heldcard` pair, which its
composite trigger gates.

## 3. Ines's card: the decision

**Yes, it's on Moth's rail.** It isn't for sale, and DP can't free it. It's the HELD toploader at the end of the rail,
seen from the first van visit.

**What happened.** August 2005, Gus's folding table out back. Gus played as the House, and Ines anted her Scapegoat
(one of the three in goat-control, the copy she'd played all summer) and lost it to him. The rule then was a week on
the rail, claimed at half again. Ines wouldn't claim a card she'd lost fair. Gus couldn't give it back without making
it charity, which she'd never take. The week ran out, and Gus folded the table that night. Moth was caught between
two people who each had a rule, so Moth made a third: the card goes back up the stairs with the next one from that
shop to take that table.

**Why this version:**
1. **The rail is the alley's memory of loss.** Making Ines's card its oldest entry ties the mechanic to the thread:
   your own lost cards hang on the same rail, under the same rule.
2. **No purchase.** If the player could buy it, a 21-year standoff would shrink to a price, and a story card would
   need an economy price (and an arbitrage check). HELD keeps it out of the market.
3. **Earned in a duel, at the one table where it fits.** Orvel's Table is the only place your own cards are at stake
   (Decided), and it's the same table Ines lost hers at. The House table's rule (spares only, never a card a deck
   needs) is Gus's lesson from that night. That makes the owner's safeguard part of the world.
4. **Ines resolves it on her terms.** She isn't given her card: she pays the claim she counted out in 2005, so her
   precision and pride hold ("It was always the right amount"). Gus's rule holds too. Neither breaks character. The
   player carries it.
5. **It pays off what's already planted:**
   - `cup-goat-clear`: "I lost a card in 2005", and Gus finding something to tidy;
   - "Scapegoat is a turn, not a card" (`cup-goat`), which reads twice;
   - the `arrival:2005` footer's "Best one, if you ask me", answered by "Best year. I keep telling people";
   - the `cup-goat-clear` footer's unopened 2005 pack, which she finally opens;
   - Gus's "Don't tell Ines" (his `lose:` line).
6. **It separates Ines from Wren** (STORY-REVIEW section 4: two older Goat Control women with a Gus history). Wren
   has the four-hour final. Ines has the lost card and the envelope.

**Why not the spec's `inescard`** (a flagged stake at position 6 in Orvel's binder, won into your collection):
- it makes Orvel the holder, so the House becomes a villain and the payoff a heist;
- the payoff would wait on stake order, possibly many sittings;
- you'd end up owning Ines's card (Paid 0) with nowhere to send it.

**Build cost of `heldcard`:**
- a composite trigger;
- one static row at the end of the rail in Used & graded: "HELD · not for sale", face down, not selectable, shown
  until `held-card-home` has been seen;
- the card never enters the collection;
- three van hooks.

**Why not the other alternatives:**
- **The glass case:** `bond-old-gus-5` opens it and shows only his Blue-Eyes.
- **Decision 14** (Gus's 2004 Scapegoat, graded 6, given back after your first slab): it puts the payoff at the
  Grading Desk, not the alley. This version keeps decision 14's card and grade. Odile reads it as a six, Excellent,
  in `held-card`.

**Gameplay consequences** (felt, even subtly):
- the HELD row is gone afterwards (`van:held-gone`), with a one-time `van:held-paid` line on the next visit;
- Gus's back-door footer;
- a new poster, **HELD** (the grey pencil tag);
- optionally, if CPU copies ever get looks: Ines's goat-control shows that copy (Raw 6, Excellent) wherever you meet
  her afterwards.

## 4. Cast voices

### Moth (`moth`): the fence, from the van
- **Role:** runs Under the Counter (the claim rail, the shelf, used cards, selling). Has been parked by the bins since
  2004. Held Ines's card for 21 years.
- **Want / need:** wants to stay out of other people's business; needs the held card to go home without picking a
  side.
- **Voice pillars:**
  - **Vocabulary:** a dealer's euphemisms for where cards come from: "between binders", "came my way", "as-is",
    "claim", "tag".
  - **Rhythm:** short, dry, transactional.
  - **Avoids:** "stolen", "fence", "home" (Odile's "good homes" is hers), and where any card came from.
  - **Tic:** ends a deal with "As-is."
  - **Subtext:** high. Kindness comes out as rules.
- **Would never say:**
  - "That card was stolen." (The word doesn't exist in that van.)
  - "Let me tell you about Ines and Gus." (Moth gives you a rule, never a story.)
  - "Great deal for you, friend!" (No salesmanship. The prices are the prices.)
- **Reference lines:**
  - "No receipts. Receipts are for people who plan to bring things back."
  - "Most people claim. One never did."
  - "Twenty-one years. The shop finally sent somebody. As-is."
- **Looks (proposal):** a watchful man in his fifties, a quilted vest over a faded polo from a no-name ice-cream
  company, reading glasses pushed up into grey-streaked hair, a bulldog clip on every pocket.
- **Note:** the spec's Sell-desk refusal ("Won at ante: Moth doesn't buy back his own.") fits his voice. The
  `van:sell` lines below offer in-voice alternatives.

### Baz "the Bell" Calloway (`baz`): the bracket MC
- **Role:** chalks the board, rings the round bell, calls the matches, warns you about headliners, gives everyone a
  name. Came with Orvel's reopening, and found the bell in a crate.
- **Want / need:** wants a good show every night; needs everybody, losers included, to feel part of it.
- **Voice pillars:**
  - **Vocabulary:** a barker's: "Chalk it!", "on the board", "Ring it!".
  - **Rhythm:** exclamations, all-caps calls on the big moments.
  - **Avoids:** real names, bodies, accents, money trouble, cruelty (he teases habits, never people).
  - **Tic:** "Chalk it!"
  - **Subtext:** none. He says it all, loudly.
- **Would never say:**
  - "Hello, Vera." (It's "Red".)
  - Anything about how someone looks.
  - "That loss was humiliating." (A loss is a story, never a shame.)
- **The nickname scheme:**
  - One chalk name each, from the first thing Baz notices: where you came from, a habit, or what you carry.
  - Names go up, never down. Vera's "Half-Again" is the one she earned the hard way.
  - Alley regulars and you are chalked by nickname on the alley board. People down from the circuits or the Library
    sign their own names.
  - **Yours (`{nick}`), changed by each first clear's "Baz chalks your name" line (spec section 6):** Upstairs (first
    visit) → Curbside (Dumpster Row) → Downstairs (The Basement Game) → Boots (The Black Market) → Midnight (the
    Midnight Open title, for good).
  - **The cast:** Kit is Spares, Vera is Red then Half-Again, Penny is Lucky, Moth is the Van, Orvel is the House,
    Ilara is Weather, Mika is Headset, Nyx is Lantern, Nocturne is Since-Always, Odile is the Loupe (Baz claims he
    named her; she says Gauge & Gilt did). Hollis is Footnote (from Kit's description; they've never met). Gus is
    only ever "the man upstairs": Baz has never once named him.
- **Reference lines:**
  - "Came down from the shop, did you? Then you're Upstairs. Chalk it!"
  - "Lost before your first turn? The House doesn't charge for that."
  - "Sorry! Habit!"
- **Looks (proposal):** a barrel-chested man in a chalk-dusted waistcoat and rolled shirtsleeves, a brass hand bell
  on a lanyard, a stub of chalk behind each ear.

### Penny (`penny`): the regular with the Common
- **Role:** antes the same Common Kuriboh every night and hasn't lost it in eleven years. Fills the spec's Dumpster
  Row cameo: seat 1 at `PennyChance` 1/5, warriors plus a full splash, skill 0.7, one Common in her binder.
- **Voice pillars:**
  - **Vocabulary:** a warm, superstitious grandmother's. The card is "it", and "it likes" things.
  - **Rhythm:** short and sunny.
  - **Avoids:** strategy ("I just play the card that likes me").
  - **Tic:** talks to the card.
  - **Subtext:** low.
- **Would never say:** "My deck's win rate against splash builds is..."
- **Sample lines:**
  - "Same card. Same crate. Same luck."
  - "It's never been on the rail. It doesn't know what the rail looks like."
  - "Up you come, dear." (summon)
  - after losing it: "New Common. Night one of a new streak." (Her binder holds a fresh Kuriboh from then on.)
- **Looks (proposal):** a retired bus driver in her seventies, cardigan with a transit pin, one toploader in her
  breast pocket.

### Patron Orvel (`power-patron`): the House
- **Role:** hosts and plays Orvel's Table, keeps the ledger, pays the prizes. He reopened the tables years after 2005
  and kept Gus's table and Gus's rule.
- **Voice pillars:**
  - **Vocabulary:** a financier's: ledger, position, books, underwrite.
  - **Rhythm:** measured.
  - **Avoids:** threats, raised voices, and saying who set up the table.
  - **Tic:** everything is "on the books".
  - **Subtext:** high: the unreadable smile, in words. Polite, never menacing.
- **Would never say:** "Pay up, or else." (The ledger is the only pressure, and it's paper.)
- **Sample lines:**
  - "The House pays. It always does, eventually."
  - "A temporary position. Most people buy back in."
  - "I don't gamble. I underwrite."
  - "Into the ledger. In ink. I don't own a pencil."

### Ilara Gale (`radiant-typhoon`): the reigning bracket champion
- **Role:** Midnight Open seed 1. You're seed 2, so you can only meet in the final.
- **Voice pillars:**
  - **Vocabulary:** a sailor's weather.
  - **Rhythm:** brisk.
  - **Avoids:** gloating. She's generous in defeat.
  - **Subtext:** low.
- **Sample lines:**
  - "Come and take the weather off it."
  - "The wind's been at my back all season. Let's see if it turns."
  - "Every board clears eventually. Mine just clears yours first."
  - "The weather turned. It does that."

### Mika (`mika`): streams the brackets with Baz
- **Role:** colour commentary: meta, chat and clips. In no circuit today.
- **Voice pillars:**
  - **Vocabulary:** a streamer's: chat, line, clip, GG.
  - **Rhythm:** quick and dry.
  - **Avoids:** hype she doesn't believe ("Chat is wrong" is her running gag).
  - **Subtext:** low.
- **Sample lines:**
  - "Chat says lucky. Chat is wrong."
  - "That's the clip. Somebody clip it."
  - "Clean interrupt. Highlights."

### Nyx (`nyx`): host of Nyx's Table, seat 4
- **Role:** keeps the oldest contraband with its original printed words. Seat 2 (chaos-yata as built) is "the
  Emperor, the crow".
- **Voice pillars:**
  - **Vocabulary:** a gravekeeper's: rest, keep, words, crate.
  - **Rhythm:** slow and quiet.
  - **Avoids:** threats. Death is rest, never violence.
  - **Tic:** speaks of cards as sleeping.
  - **Subtext:** medium.
- **Sample lines:**
  - "I keep them the old way. Somebody has to."
  - "Four seats. Mine is the last."
  - "Old words don't end when they lose."
  - "Rest. I'll wake you for the next one."

### Nocturne (`nocturne`): the oldest regular
- **Role:** has been coming down since 2004 and saw the night Gus folded the table. A Midnight Open entrant. In the
  alley, the one who tells you the history in a sentence.
- **Voice pillars:**
  - **Vocabulary:** spare.
  - **Rhythm:** one sentence, then a pause.
  - **Avoids:** their own name.
  - **Subtext:** very high.
- **Sample lines:**
  - "Your Gus set this table up in 2004."
  - "I've played every Midnight since the first one."
  - "That bell used to hang over a shop door, Baz."
  - "I promised someone my name once."
- **The name:** said at the Midnight Open win and drowned by Baz's bell. The promise is kept and the mystery stays
  (owner question 4).

### Upstairs, as the alley touches them
- **Kit (they/them):** the guide. Antes spares only, which is why Gus has never seen the bottom of their binder.
  Enthusiastic, has rules about binders, "...Mostly."
- **Vera:** loves it, antes a deck card against Kit's advice, loses Marauding Captain (in warriors.ydk) to a Change
  of Heart, and claims it at half again. She keeps coming and never antes again: Ines's mistake without the 21
  years.
- **Gus:** never comes down, and never lectures past "Spares only. That's the whole speech." His disapproval lives
  in the shop footers.
- **Hollis:** amused, never comes down, keeps the Unlisted binder "purely for scholarship".
- **Ines:** never comes down. Her whole alley story happens upstairs, over one envelope (continuity item 2).
- **Odile:** comes down only for Orvel's slabs, in her Grading Desk voice ("Honest wear").
- **Ilse:** never mentioned.

## 5. Line pools

**`campaign/alley-voice.txt`** (shop.txt's format; the spec's hooks, plus **[new]** ones):

```
# The Back Alley's voice (docs/dev/ALLEY-STORY-2026-11-01.md; hooks from ALLEY-SPEC section 12).
# Text can use {card}, {table}, {handle}, and [new] {nick}, {step}. Speaker "opponent" is the CPU you just played [new].

# ---- Regulars: their own intro, win and lose (spec: regular:intro|win|lose[:<table id>]) ----
# [new] regular:intro:card:<card name>: a regular's taunt when that card made its handle. Most specific wins.
# The 31 card taunts are in section 6's table; these are the table fallbacks.

pool: regular:intro
opponent: Show me yours, I showed you mine. Alley manners.
opponent: I've been sitting at this crate since before the lights were up.

pool: regular:intro:dumpster-row
opponent: Old cards, old tricks. Old tricks work.

pool: regular:intro:basement
opponent: Mind the dryer. It hits harder than I do. Nearly.

pool: regular:intro:black-market
opponent: Everything I'm running got banned for being right.

pool: regular:win
# You lose: the stake stays with them.
opponent: Thanks for the points. Come back and win them back.
opponent: Good duel. The crate keeps the stake. That's the crate's rule, not mine.
opponent: Tell Baz I said it was close. It wasn't, but tell him.

pool: regular:lose
# You win their card ({card}).
opponent: Take it. It liked me better, but take it.
opponent: {card}'s yours. Don't bother selling it to Moth. Moth won't buy it.
opponent: Fair's fair. Look after it. It's been good to me.

# ---- Baz on the versus and the table (spec: tell:<id>, baz:stake, baz:empty, baz:turncap, baz:firstturn, baz:orvel) ----

pool: baz:stake
baz: Points on the crate against {card}! Shake on it!
baz: Your points, their {card}. Everybody saw it. That's the contract.

pool: baz:empty
baz: {handle}'s binder is empty tonight! Duel for the glory! There's no glory either!
baz: Nothing in the binder but dust. The fee's still the fee.

pool: baz:turncap
baz: TURN FORTY! Bell's rung! Higher Life Points takes it!
baz: Forty turns! Cards down! Count the Life Points!

pool: baz:firstturn
baz: Lost before your first turn? The House doesn't charge for that. Pick your stake back up.

pool: baz:orvel
baz: The House shows its hand! Three cards, and every one of them insured!
baz: Orvel's Table! Spares only, slabs on the cloth, voices down!

# [new] baz:contraband: Baz over the versus screen's CONTRABAND row (the spec's label).
pool: baz:contraband
baz: {handle} is carrying! Three cards, face up, nothing up the sleeves.
baz: Read the crate, {nick}. That's what's coming.
baz: {handle} brought {card}. Plan for it, or plan to pay for it.
baz: Nobody hides contraband down here. Hiding's for upstairs.

# [new] baz:contraband:nyx: Nyx's seats.
pool: baz:contraband:nyx
baz: Nyx's crate. Old words. Read them carefully. They mean exactly what they say.

pool: tell:yata-mags
baz: Mags runs three Chaos Emperors and a crow. Keep a card back for after the wipe.

pool: tell:return-rook
baz: Rook's banishing a field's worth for one big comeback. Hit hard before it comes back.

pool: tell:mass-driver-moss
baz: Moss runs a Mass Driver. Bring something for the Graveyard.

pool: tell:trishula-trish
baz: Trish wants a Synchro for nine. Stop the Tuner and the garden never grows.

pool: tell:master-plan-marlo
baz: Marlo's whole board starts with one agent. Stop the first search.

pool: tell:gumblar-gail
baz: Gail links up into a hand rip. Spend your hand before Gail gets there.

pool: tell:scientist-turtle
baz: Lyle never attacks. Lyle doesn't need to. Count your Life Points, not your monsters.

# [new] Ante moments, beside the results screen's own lines: baz:antewin, baz:antelose, baz:rail, orvel:pays.
pool: baz:antewin
baz: {card} changes binders! Chalk it!
kit: Front page of the binder. It's the rule.

pool: baz:antelose
baz: The crate keeps the points! Better luck, {nick}!
moth: Points go in the jar. The jar never loses.
kit: Points gone, binder safe. That's the deal out here.

pool: baz:rail
power-patron: Into the ledger. {card}, five duels, claim at half again.
moth: {card}'s on the rail. Five duels on the clock.
baz: {card} goes on a hook! The clock is running!

pool: orvel:pays
power-patron: The House pays. Mind the case.
odile: I checked it twice. The House doesn't get a third look.

# Penny's seat ([new] regular:intro:penny, regular:win:penny).
pool: regular:intro:penny
penny: Same card. Same crate. Same luck.
penny: It's never been on the rail. It doesn't know what the rail looks like.

pool: regular:win:penny
penny: Still mine. Same time tomorrow?

pool: regular:intro:penny
after: penny-lost
penny: New Common. Night one of a new streak.

# ---- Brackets (spec: mc:<kind>:<round>, stream:<kind>:<round>; kind brawl | midnight) ----
# Round keys as Bracket.RoundName, lower-case: round-of-16, quarter-final, semi-final, final.
# [new] mc:upset, mc:out, mc:crowned: any kind, any round. mc:game:<n>: a best-of-three's game n (2, 3), picked once a
# game; plain mc:game is its fallback for a game-neutral line (none yet).

pool: mc:brawl:quarter-final
baz: Alley Brawl! Eight names on the slate! Single elimination, no do-overs, no crying on the van!
baz: {nick} versus {handle}! Shake hands and show your contraband!

pool: mc:brawl:semi-final
baz: Four left! Half of you are going home! The other half are going to the final!

pool: mc:brawl:final
baz: The Brawl final! Two names left and one bell!

pool: mc:midnight:round-of-16
baz: The Midnight Open! Sixteen names! Eyes on the board, hands off the bell!
baz: {nick} to the crate! {handle}, try to look surprised!

pool: mc:midnight:quarter-final
baz: Eight left! The slate's getting tidier!

pool: mc:midnight:semi-final
baz: Four names, two crates, and the top seat watching!

pool: mc:midnight:final
baz: The Midnight final! Best of three, and it's past two!

pool: mc:game:2
baz: Game two! Shuffle up, the slate's watching!

pool: mc:game:3
baz: Game three! All the chalk on one crate!

pool: mc:upset
baz: UPSET! Somebody owes somebody a coffee!
baz: Nobody had that on their card! Nobody! I didn't! Chalk it!

pool: mc:out
baz: That's the {round} for {nick}! Paid at the hatch! Stay and watch, or go home and think about it!
baz: The chalk stays up, {nick}. Names don't come off my board.

pool: mc:crowned
baz: Top of the board! Ring it twice!

pool: stream:brawl:quarter-final
mika: Streaming the Brawl. Chat's already wrong about the seeds.

pool: stream:brawl:final
mika: Brawl final. Clip it, whatever happens.

pool: stream:midnight:round-of-16
mika: Midnight Open. Sixteen decks, and half of them aren't legal anywhere. Chat's thrilled.

pool: stream:midnight:semi-final
mika: Semis. Somebody in chat just said "easy". Ban them.

pool: stream:midnight:final
mika: Best of three. If you're watching with the sound off, the bell means something happened.

# ---- Headliner commentary (HEADLINERS "The show": the combo, payoff and fizzle moments) ----

pool: baz:combo
baz: {handle}'S GOING FOR IT! Everybody off the crates!
baz: Here comes the act! Nobody blink!
mika: Chat, this is the line. Watch the Graveyard.

pool: baz:payoff
baz: THERE it is! That's the whole act!
mika: That's the clip. Somebody clip it.

pool: baz:fizzle
baz: FIZZLED at step {step}! Somebody ring the bell! ...I'm the bell!
baz: The act's cancelled! Refunds at the van! There are no refunds at the van!
mika: Clean interrupt. Highlights.
```

**`campaign/van.txt`** (spec hooks `van:hello`, `van:footer`, `van:rail`, `van:buyback`, `van:shelf`, `van:pack`,
`van:sell`, `van:empty`, plus **[new]** `van:used`, `van:sell-refused`, `van:held`, `van:held-gone`,
`van:held-paid`):

```
pool: van:hello
moth: Hatch is open. Browse with your eyes first.

pool: van:footer
moth: Evening. Everything's as-is, including me.
moth: No receipts. Receipts are for people who plan to bring things back.
moth: Cards come in, cards go out. I don't ask, and they don't say.
moth: Mind the freezer. It hasn't held ice cream in years. It holds slabs.
moth: The chime's broken. It plays three notes and stops. So do I.

pool: van:footer
after: vera-ante
moth: Your friend in red came by. Paid the tag and said a word I won't repeat.

pool: van:held-paid
# Plays once, on the first van visit after held-card-home.
moth: Claim's paid. Exact change. Of course it was.
moth: Tell her the tag's off. Tell him... no. Don't tell him anything. He knows.

pool: van:rail
moth: {card}. {left} duels left on the clock. Tag's {price}.
moth: Still hanging. Still yours, for {left} more.
moth: Everybody means to come back for theirs. Most do.

pool: van:empty
# Nothing of yours on the rail.
moth: Nothing of yours on the hooks. Good night for you.
moth: Rail's empty of yours. Keep it that way.

pool: van:buyback
moth: Back where it belongs. The tag goes in the drawer.
moth: Claimed. As-is. It missed you, if cards do that.

pool: van:held
before: held-card
moth: Held. Not for sale, not for trade, not for asking about.
moth: That one's waiting on somebody. It's more patient than they are.
moth: The tag's older than the van's paint.

pool: van:held-gone
after: held-card-home
moth: The hook's empty. I keep looking at it anyway.
moth: Took the tag home. It's pinned over my sink.

pool: van:shelf
moth: Every card on that shelf got banned somewhere, sometime, for doing its job too well.
moth: Slabbed by the Loupe. Priced by me. Neither of us haggles.
moth: That one came out of a collection. The collection didn't come with a name.
moth: Look all you like. Looking's free. The light costs me.

pool: van:pack
# Reads either way (spec O4: packs sold here; economy: prize-only, the tab opens won packs).
moth: Sealed this afternoon. Don't ask what's in it. I didn't look. Mostly.
moth: Five cards, every one banned somewhere. The odds are on the wrapper.

pool: van:used
moth: Used. Loved. Some of them loved hard. The grade tells you how hard.
moth: Every one of these was somebody's. Now they're between binders. Fix that.

pool: van:sell
moth: I don't pay what it's worth. I pay what it'll sit for.
moth: Sold. It'll be on a hook by Friday. Somebody's always looking.
moth: Cash, no questions. Mine or yours.

pool: van:sell-refused
# Won at ante: sells for 0. In-voice alternatives to the spec's refusal text.
moth: That one came off a table. I don't buy what the table gave you.
moth: If I bought those back, I'd be paying the tables twice.
```

**Gus's quiet disapproval: footers for shop.txt.** No lecture, ever. They rotate, so his disapproval reads as
weather, not as a scene.

```
pool: footer
after: alley-upstairs
before: held-card-home
old-gus: Chalk comes off with a damp cloth. Not that I'd know.
old-gus: Back door sticks. Never fixed it. Never will.
old-gus: Moth still parks that van by the bins? Huh. Don't tell Moth I asked.
old-gus: Friday nights, nobody buys sleeves. They're all out back with their spares.

pool: footer
after: alley-kit
kit: Where do my spares come from? Fridays. Where do they go? Also Fridays.

pool: footer
after: vera-ante
old-gus: Vera's gone quiet about Fridays. I haven't asked. She hasn't told me.

pool: footer
after: held-card-home
old-gus: Fixed the back door. Don't read into it.
old-gus: Ines bought a sleeve today. One sleeve. Said she had a card that needed it.
```

Keep both shipped 2005 echoes (`arrival:2005` and the `cup-goat-clear` footer): `held-card-home` pays them off.

## 6. Chalk handles

**For campaign/alley-events.txt, in the spec's format.** A regular's handle is the prefix of its most valuable added
card that has a `handle:` line, plus a first name from `names:` not used that night. With no matching line, it's
"<Base title> Regular".

```
handle: Pot of Greed | Three-Pot
handle: Raigeki | Thunder
handle: Harpie's Feather Duster | Duster
handle: Graceful Charity | Charity
handle: Change of Heart | Heartbreak
handle: Monster Reborn | Reborn
handle: Snatch Steal | Snatch
handle: Dark Hole | Dark Hole
handle: Premature Burial | Early
handle: Heavy Storm | Heavy Storm
handle: Black Luster Soldier - Envoy of the Beginning | Envoy
handle: Chaos Sorcerer | Sorcerer
handle: Brain Control | Brainy
handle: Witch of the Black Forest | Witch
handle: Sangan | Sangan
handle: Solemn Judgment | Solemn
handle: Blackwing - Steam the Cloak | Cloak
handle: Goyo Guardian | Goyo
handle: Glow-Up Bulb | Glow-Up
handle: Level Eater | Level Eater
handle: Artifact Scythe | Scythe
handle: Evilswarm Exciton Knight | Exciton
handle: Crush Card Virus | Crush Card
handle: Maxx "C" | Roach
handle: Call of the Haunted | Haunted
handle: Elder Entity Norden | Norden
handle: That Grass Looks Greener | Greener
handle: Mirage of Nightmare | Mirage
handle: Morphing Jar | Morphing
handle: Called by the Grave | Called-It
handle: Cyber Jar | Cyber-Jar
# One names: line (wrapped here only if the reader appends repeated names: lines; otherwise join them).
names: Teddy, Wick, Rosa, Dutch, Sal, Juno, Pike, Dee, Chuck, Hal, Ruth, Sully, Del, Perry, Hugo, Vic, Saul, Brody,
names: Winnie, Sam, Sol, Cleo, Gwen, Gilly, Lou, Sid, Ike, Cruz, Rocco, Hank, Nils, Greer, Mo, Joss, Cal, Bram
```

- **Names:**
  - "Fen" is out of the spec's starter list: Firewall Fen is retired, and Coralie Fenn and Fern Vasquez are close.
  - Every name avoids the 88 characters (no Hettie, because of Trapmaster Hattie; no Rex, Pip, Tock, Bea or Mae).
- **Taunts:** each line below is a regular's `intro` when its card made the handle (**[new]**
  `regular:intro:card:<card>`, one line each). The name shown is the alliterative one Baz would pick if he could.

| Handle | Contraband | Taunt |
|---|---|---|
| Three-Pot Teddy | Pot of Greed | Own three. Brought one. The other two stay home for luck. |
| Thunder Rosa | Raigeki | Line your monsters up nice. Saves me time. |
| Duster Dee | Harpie's Feather Duster | Set all the backrow you like. I tidy up. |
| Charity Chuck | Graceful Charity | I give. I just give to myself first. |
| Heartbreak Hal | Change of Heart | Your best monster's coming over to my side. Just for the evening. |
| Reborn Ruth | Monster Reborn | Nothing stays in a Graveyard at my crate. Yours or mine. |
| Snatch Sully | Snatch Steal | I'll borrow it. You'll get some Life Points for the trouble. |
| Dark Hole Del | Dark Hole | Mine go too. I don't mind. Do you? |
| Early Perry | Premature Burial | Buried is just a word. Back for a few Life Points. |
| Heavy Storm Hugo | Heavy Storm | Weather's turning. Your backrow first. |
| Envoy Vic | Black Luster Soldier - Envoy of the Beginning | One light, one dark, and a soldier who swings twice. |
| Sorcerer Saul | Chaos Sorcerer | Pick a monster. Any monster. Now it's gone. |
| Brainy Brody | Brain Control | Cheapest rental in town. Your best, for a few hundred. |
| Witch Winnie | Witch of the Black Forest | Go on, destroy her. She knows people. |
| Sangan Sam | Sangan | Little fella. Big address book. |
| Solemn Sol | Solemn Judgment | Half my Life Points say no. |
| Cloak Cleo | Blackwing - Steam the Cloak | Leaves a cloud behind. Leaves you guessing. |
| Goyo Gwen | Goyo Guardian | Your monster, my side. I'll take good care of it. |
| Glow-Up Gilly | Glow-Up Bulb | Little bulb. Very bright idea. |
| Level Eater Lou | Level Eater | One star. That's all I eat. Every time. |
| Scythe Sid | Artifact Scythe | Extra Deck's closed tonight. Kitchen's shut. |
| Exciton Ike | Evilswarm Exciton Knight | More cards than me? Not for long. |
| Crush Card Cruz | Crush Card Virus | I'll look at your hand. Then your deck. Then we'll talk. |
| Roach Rocco | Maxx "C" | Summon all you like. I'll be drawing. |
| Haunted Hank | Call of the Haunted | The Graveyard's just a waiting room. |
| Norden Nils | Elder Entity Norden | Cold in here. Something's coming back. |
| Greener Greer | That Grass Looks Greener | My Graveyard's about to outgrow your deck. |
| Mirage Mo | Mirage of Nightmare | I draw on your turn. Don't worry about it. |
| Morphing Joss | Morphing Jar | Everybody throws their hand in. Everybody draws five. Very fair. |
| Called-It Cal | Called by the Grave | Called it. |
| Cyber-Jar Wick | Cyber Jar (spec F10: not in alley.txt yet) | Flip it, and everybody starts over. I like starting over. |

That's 31 regular handles. Firewall Fen stays retired (HEADLINERS).

**Headliners** (the spec's `handle:` per headliner). For each `personalities/<id>.profile`, give `intro`, `win` and
`lose`, plus the HEADLINERS `combo` and `fizzle` lines:

| Id | Handle | `intro` | `win` | `lose` | `combo` | `fizzle` |
|---|---|---|---|---|---|---|
| `yata-mags` | Yata Mags | Empty hands, everybody. Then the crow. | No cards, no turns. That's 2004 for you. | You drew past the crow. | Clear the table. The crow's hungry. | No Emperor tonight. Fine. I'll play cards like a person. |
| `return-rook` | Return Rook | I send everything away. Then I bring it home. | All home. Every one. | It didn't come back in time. | Everything I banished? It's coming home. | Stuck in the other dimension. Typical. |
| `mass-driver-moss` | Mass Driver Moss | I can count to four hundred all night. | Four hundred, four hundred, four hundred... done. | You outlasted the frogs. | Loop's live. Count the four hundreds. | Loop's broken. The frogs are sulking. |
| `trishula-trish` | Trishula Trish | Plant it early, pick it at nine. | Three cards gone, and that was the end of it. | Nothing bloomed. | Bulb, Spore, Dandy. Nine stars. | The garden didn't grow. Weeds, I guess. |
| `master-plan-marlo` | Master Plan Marlo | You don't have clearance for my first turn. | Mission complete. You weren't briefed. | The plan leaked. | The plan's in motion. You're in the plan. | Plan B. I didn't bring a Plan B. |
| `gumblar-gail` | Gumblar Gail | Link, link, link, and then your hand. | Empty-handed. Like I promised. | You spent it all first. Smart. | Let's see that hand. All of it. | The chain broke at link three. Rude. |
| `scientist-turtle` | Lab Coat Lyle **[new name]** | Science is mostly paperwork. And catapults. | Results confirmed. | Budget cut. | Experiment one. Experiment two. Fire. | The lab's closed. Somebody touched my notes. |

- The tells are drafts until `--combo-interrupt` names an answer that works (HEADLINERS).
- `scientist-turtle` had no handle in the spec. "Lab Coat Lyle" is proposed.

## 7. Story and gameplay

| Story beat | Gameplay | Player feels |
|---|---|---|
| The rumor (`alley`) | The Back Alley tab; the NO LIST poster on the first visit | Invited somewhere you shouldn't be |
| Baz names you | `{nick}` on the alley board, rising with each first clear | Belonging, earned in chalk |
| Your first ante win | A won card: it plays, but sells for 0 | Power with strings |
| Vera's ante | Shows the rail and half-again before you can lose a card | Warned, by a friend |
| Gus's footers | The shop's voice changes | Quiet weight |
| The House table | Your own card at stake; the LOST CARD poster | Real risk, with the rule on your side |
| Taking the House table | Orvel's Marker poster; `heldcard` | Earned |
| Ines's card | The HELD row empties; the HELD poster; Gus's back door; optionally, her copy on her board | A 21-year knot untied, by the rules |
| A beaten contraband deck | A page in the Unlisted binder, with Watch the combo | The alley feeds the Library |
| The Midnight Open title | "Midnight" for good; Nocturne's name (almost) | The top of the board |

**Lore tiers:**
- **Surface (everyone who goes down):**
  - Kit, Baz and the rules;
  - Gus disapproves;
  - Vera's ante;
  - the table intros.
- **Engaged:**
  - the HELD toploader;
  - "Your Gus set this table up in 2004";
  - the van's pools;
  - Kit's footer about where the spares go;
  - Penny's streak.
- **Deep:**
  - the bell was the shop's old door bell;
  - Orvel's Table is the 2004 shop's folding table (the "one folding table" footer), its leg taped;
  - "Scapegoat is a turn, not a card", read twice;
  - "Best one, if you ask me" (`arrival:2005`).

The critical path needs none of it: the tables and brackets read on their own.

## 8. Continuity

**Must not be contradicted (world bible):**
1. **Timeline:**
   - 2004: Gus runs Friday tables out back, with the shop's folding table and its old door bell.
   - August 2005: Gus, as the House, wins Ines's Scapegoat. She doesn't claim it. He folds the table and leaves it
     out back.
   - Later (no year given), Orvel reopens the tables. Baz comes with the reopening, Penny has come for eleven years,
     Kit for three. Moth and Nocturne have been there since 2004.
2. **Never come down:** Gus, Hollis, Ines and Ilse.
3. **Moth never says "stolen".** Nobody in-world says "fence". Moth avoids "home" (Odile's "good homes").
4. **Gus's glass case** (`bond-old-gus-5`) never held Ines's card.
5. **Spares only, never a card a deck needs** is Gus's rule, born in 2005. Anything explaining Orvel's safeguard
   in-world should say so.

**Conflicts to fix in the other docs:**
1. **The spec's Midnight Open field includes `ines goat-control`.** That breaks item 2, and the payoff depends on it:
   she hasn't set foot out back in 21 years. Swap her for another Library boss of similar Elo (e.g.
   `six-samurai six-samurai` or `teledad teledad`). `nocturne hat` in the field is right: the scenes treat Nocturne
   as an entrant.
2. **`first-bracket-title` (`bracketwin`) assumes a Library Locals title** (Gus's corkboard; Hollis's prize packs).
   The spec keeps `bracketwin` firing for alley titles, and a first title can be the Alley Brawl. Fix
   (recommended): raise plain `bracketwin` only for Locals titles, and alley titles raise only `bracketwin:<name>`.
   Or rewrite that scene for any bracket.
3. **`heldcard` replaces `inescard`** (section 3). It must be composite. Otherwise a player who never cleared the
   Goat Cup gets the payoff without its setup. `held-card` is written place-neutral, because it can fire upstairs
   after a Goat Cup clear.
4. **Decision 14** in the plan is superseded by section 3 if the owner agrees: the same card and grade, a different
   route.
5. **Firewall Fen** is still in the plan's random-opponents line, and "Fen" is in the spec's `names:`. HEADLINERS
   retired it. Update both.
6. **Cyber-Jar Wick** needs Cyber Jar in alley.txt's Dumpster Row list (spec F10). Otherwise the handle line is
   simply never used.
7. **Fee or held stake** (spec section 9 against ALLEY-ECONOMY decision 1), and **packs at the van** (spec O4 against
   the economy's "no"): the lines fit either outcome. No line here says whether a win returns your DP.
8. **Kit's pronoun:** they/them is the owner's call. docs/dev/PERSONA-WALKTHROUGH-2026-10-30.md ("her") and PACKS-REVIEW
   ("his") are stale.
9. **"The 13th binder":** the Every Era epilogue starts "this year's binder" on the shelf. If that's a shelf item,
   Unlisted is the 14th spine. Call it "the Unlisted binder".
10. **Mika and Professor Mica** are one letter apart. They're never in a scene together.
11. **Penny's Kuriboh** echoes the Shop Bell poster's Kuriboh. That's intended. Change both or neither.
12. **Chalk names on the alley board only.** Library Locals keep real names. People down from the circuits and the
    Library sign their own at the Midnight Open (the scene says so).
13. **Line length:** every line is under about 105 characters. STORY-REVIEW's box trouble started at 128.
14. **No card text is quoted.** Taunts and tells paraphrase effects loosely, and card names are used as the story
    already uses them.

## Owner questions

1. **Ines's card (section 3).**
   - What happens: Gus won it, and Moth holds it on the rail. Taking Orvel's Table frees it, and Ines pays the claim
     herself.
   - What it changes: it replaces the spec's `inescard` and supersedes plan decision 14 (same card and grade).
   - Approve?
2. **Ines out of the Midnight Open field** (conflict 1): approve the swap?
3. **Penny:**
   - Can her eleven-year streak be broken (recommended: yes, `penny-lost`)?
   - Is her Common Kuriboh?
4. **Nocturne's name:** keep it drowned by Baz's bell (recommended: the promise is kept and the mystery stays), or
   reveal it?
5. **Baz's ladder for you** (Upstairs → Curbside → Downstairs → Boots → Midnight) as `{nick}` on the alley board:
   approve the names?
6. **Ines's worn Scapegoat on her board** after the payoff, if CPU copies ever get looks: wanted?
7. **Gus never comes down,** even for the payoff (recommended). Confirm?
8. **Moth is "he"** (as the spec and the economy write it). Confirm?
