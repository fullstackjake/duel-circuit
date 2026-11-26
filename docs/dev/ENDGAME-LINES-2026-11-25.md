# The Borrowed Road: lines (Narrative Designer, 2026-11-25)

Draft text for [ENDGAME-SPEC-2026-11-25.md](ENDGAME-SPEC-2026-11-25.md), from the Narrative Designer agent (Opus), in the
`campaign/story.txt` format where it applies. None of it is in the game yet; the wording is not yet confirmed by the owner.
No line runs past 100 characters.

Proposed engine additions:
- **Triggers:** `road` (not `clear:Tier 0 Gauntlet`, so saves that already cleared it still see the invite),
  `roadclear:<n>`, `roadchamp:<id>`, `roadchampbeat:<id>`, `bus:<n>`.
- **Place:** `bus`.
- **Tokens:** `{deck}`, `{deck2}`, `{town}`.
- **Pools:** a new `campaign/road.txt` in the `shop.txt` pool format.
- **New character:** Mabel (`mabel`).

**Kit's arc.** It builds on Kit's existing lines ("I put up a card out here once that wasn't really a spare", "It
happened to me, once", "...Mostly"):
- Kit lost the card at the centre of their own deck. Every deck they built after had a hole where it went, so they
  stopped building, and even Kit's Synchrons are spares.
- On the road, Kit finds the "mostly" was their own style all along.
- At Rank 10, Kit builds a deck from those habits, not around the lost card.
- The postcards never say where the card was lost, so they read right before or after the Back Alley.

## 1. Kit's postcards (one per first Road Rank clear)

```
scene: postcard-0
when: roadclear:0
place: corkboard
-: A postcard on the corkboard: a bus shelter in the rain, a tourist-board slogan peeling off it.
kit: Six towns, six decks you'd never shuffled, and you still beat me. I told Gus. Gus said "told you."
kit: Rank 1 next. The walls get thinner from here. Pack light.

scene: postcard-1
when: roadclear:1
place: corkboard
-: Postcard: a ferry on a grey river. Kit's handwriting gets smaller toward the edge.
kit: You asked on the bus why I never bring my own deck. I pretended to be asleep. Sorry.
kit: Even my Synchrons are spares. Forty cards I'd never miss. That's the whole point of them.

scene: postcard-2
when: roadclear:2
place: corkboard
-: Postcard: a market town strung with bunting. Someone has drawn a tiny bus in the corner.
kit: Every deck on every wall plays a little like whoever built it. You feel them in the shuffle.
kit: That's the part I like. Forty cards of somebody else's stubbornness, on loan.

scene: postcard-3
when: roadclear:3
place: corkboard
-: Postcard: a quarry, very grey. Kit has written "beautiful" on it and underlined it twice.
kit: Okay, the long answer. I had a deck once. My own. Built it a card at a time, like everybody.
kit: More next stop. I'm out of postcard.

scene: postcard-4
when: roadclear:4
place: corkboard
-: Postcard: a harbor. The stamp is crooked, as if it went on in a hurry.
kit: I lost a card once. Fair and square. It was meant to be a spare. It was the middle of my deck.
kit: Every deck I built after had a hole right where it went. So I stopped building.
kit: Borrowed decks don't have holes. Somebody else's whole heart is in them. Easier to carry.

scene: postcard-5
when: roadclear:5
place: corkboard
-: Postcard: a lighthouse. Two handwritings on the back, one of them much bigger.
vera: Kit let me ride one stop. Couldn't bring my deck, so I borrowed one and attacked anyway. GREAT.
kit: Every table got to pick who went first. Vera didn't care which. Won two of three, then slept.
vera: I rested my eyes. And let them pick. First or second, I'm attacking.

scene: postcard-6
when: roadclear:6
place: corkboard
-: Postcard: a mill town, all chimneys. Kit has circled one chimney for no clear reason.
kit: Twenty decks this summer, and I played every one a little wrong. The same wrong, every time.
kit: I hold one card back a turn too long. Always. That's the "mostly". It was me all along.

scene: postcard-7
when: roadclear:7
place: corkboard
-: Postcard: an observatory on a hill. On the back, a list.
kit: My habits, so far: hold one card back. Go second if they let me. Never set two traps. Ever.
kit: Looks a lot like a decklist, if you squint. I'm not squinting. Yet.
kit: Two strikes from here. Vera says two is plenty. Vera says that about everything.

scene: postcard-8
when: roadclear:8
place: corkboard
-: Postcard: a town square full of folding tables and binders. A swap meet.
kit: Forty binders at the swap meet, and I didn't look for it once. Not in one.
kit: Gus says that's either growth or a cold. I'll let you know.

scene: postcard-9
when: roadclear:9
place: corkboard
-: Postcard: an empty deck box on a bus seat. Kit had the photo printed specially.
kit: Bought a deck box. My own. It's been empty for a week. Mabel says that's how they all start.
kit: No spare tire in the trunk this time, and you still beat me. Mabel checked twice. I took notes.

scene: postcard-10
when: roadclear:10
place: corkboard
-: No picture this time. The postcard is a decklist, forty lines in Kit's handwriting.
kit: My deck. Every habit on the list, built in. Not one card borrowed. Not that card, either.
kit: It holds one card back a turn too long. On purpose, now.
kit: Next Friday I'm bringing my own. Save me the seat by the radiator, partner?
```

## 2. The invite (after `champion-after`)

```
scene: road-invite
when: road
place: corkboard
-: Your name is on the corkboard now. Kit is standing in front of it, holding a bus timetable.
kit: Champion! Great. Next question: can you win with a deck you've never shuffled?
kit: I've watched you since your first night. You didn't learn that deck. You turned into it.
kit: So I found a bus. It runs. ...Mostly. Six towns, six card shops, one summer.
kit: House rules out there: you play off the shop's deck wall. Your binder stays home.
old-gus: Every deck plays by the list of its own year. Hollis made Kit a chart. Then one for himself.
kit: Beat a deck and it's yours for the next town. The last stop's mine.
kit: I'll be holding the best deck you passed up on the way. So pass carefully.
old-gus: Fare's on you, kid. Kit pays in spares, and the bus company doesn't take spares.
```

## 3. Kit at the last stop (`road.txt` pools: they play every run)

```
pool: road-kit
kit: Remember {deck}? Back down the road, the one you didn't pick? I picked it.
kit: You walked right past {deck}. I didn't. Let's find out which of us was right.
kit: {deck}. You passed it up in {town}. It's been riding in the back all week, sulking.
kit: Every road you didn't take ends here. Tonight it's {deck}, and I'm driving.

pool: road-kit-double
kit: Two decks tonight, {deck} and {deck2}. One sitting. I've been saving them all summer.

pool: road-kit-fallback
kit: Nothing you passed up was heavy enough tonight. So I picked {deck}. You haven't met it yet.
```

`road-kit-double` is Rank 10, where Kit plays the two strongest decks in 1,740-1,840 (not necessarily passed up, so the
line doesn't say so). `road-kit-fallback` is for a seeded pick from Kit's window, a deck not met this run (ENDGAME-SPEC
§3, steps 2-3).

## 4. House champions (`roadchamp:<id>` first meeting, `roadchampbeat:<id>` beaten)

Many of them were already met in the circuits or the Library, so no line claims a first meeting. None repeats a
profile's intro or lose line. The ten in ENDGAME-SPEC §3: spyral, tri-brigade, nekroz, power-patron, dracotail, elfnote,
drytron, mermail-atlantean, then lunalight and artmage from bus stage 4.

```
spyral: Cover story: I run this shop's deck wall. Real story: I've watched that bus since the last town.
spyral: Debrief: borrowed kit, strange terrain, mission complete. Take my deck. It's been reassigned.
tri-brigade: Nobody here owns a deck. Brigade rules: we share the wall and march what's on it. You'll fit in.
tri-brigade: You marched a stranger's deck straight through mine. Take one of ours along. It likes the road.
nekroz: A borrowed deck. Look in my mirror, then, and tell me whose face looks back.
nekroz: The mirror showed me you, not the deck. I had hoped it would show me otherwise.
artmage: This town hangs its best decks on the wall like paintings. I'm the one they don't lend out.
artmage: Take a deck off my wall on your way out. Not that one. ...Fine. That one.
lunalight: The bus always gets in at dusk here. We dance at the shop until the moon's done with us.
lunalight: Take my deck to the next town, if you like. It knows the steps. You only have to follow.
drytron: Port rules are fleet rules: you fly what's in the hangar, not what's in your bag.
drytron: Log it: one visiting pilot, flying a borrowed ship, broke my fleet. I'll want to read that entry.
```

Unused for now (no longer champions; kept in case the Level Designer swaps one back). Their lines name their old home
towns' trades, so they'd need a town if used:

```
shaddoll: A borrowed deck. How fitting. Every deck in this town has somebody else's hands on it.
shaddoll: Cut with someone else's scissors, too. ...I'll admit that stings a little.
mermail-atlantean: Harbor town, harbor rules. Everybody sails somebody else's ship here. Mind the tide.
mermail-atlantean: Good seamanship, in a stranger's ship. Take mine for the next leg. It's earned a voyage.
elfnote: A visiting soloist, on a borrowed instrument. The good ones always sound like themselves anyway.
elfnote: You played a stranger's score and made it yours. That is the whole of conducting, really.
dracotail: Every deck on my wall bites. The ones on your bus too? Good. Whose dragons are hungrier?
dracotail: You rode in on somebody else's dragons and tamed mine. Take one along. They travel well.
power-patron: Every shop on this road keeps a house champion. In this town, the house is me. It suits.
power-patron: The house settles its accounts. Take what you like from the wall. My compliments to the driver.
exosister: Our abbey keeps one rule for visitors: what you bring in, you leave at the door. Even decks.
exosister: You played a borrowed deck like it was given to you. That's rarer than winning. Go in peace.
adamancipator: Quarry town. Every deck on the wall was dug out of somebody's binder. Mine went deepest.
adamancipator: Layered, that. Borrowed deck, your own hands. I'll be studying the sample for weeks.
```

## 5. Run posters (themes)

The Level Designer's 12 (ENDGAME-LEVEL §5); the exact constraints live there. Underdog Day and Back Roads arrive at bus
stage 1.

| Name | Hook | Bus-stop line |
|---|---|---|
| Throwback Weekend | Old decks on the early roads: 2016 or earlier at S1, S2 and S4 | `-: The flyer at the stop is photocopied, a bit crooked, and dated in a year nobody's sure of.` |
| Fresh Off the Truck | The late towns feature 2024-26 decks | `-: The last two shops have boxes stacked by the door, still taped. Somebody's unpacking decks.` |
| Extra Deck Expo | A Synchro at S1, an Xyz at S2, a Fusion at S4 | `-: Every shop on the route has pushed two tables together. They're going to need the room.` |
| Speed Week | Fast decks (10 turns or fewer) at S2, S4 and S5 | `kit: Speed Week. Don't blink at the stops. The bus won't wait, and neither will they.` |
| The Long Game | Slow decks (12 turns or more) at S1 and S2 | `-: The bench at the stop has a thermos on it, and a sign: DUELS MAY RUN LONG. BRING A SNACK.` |
| Fusion Festival | Fusion decks at S4 and S5 | `-: The bunting at the last two stops is two strings of bunting, knotted together in the middle.` |
| Graveyard Shift | Graveyard decks at S1 and S2 | `-: The bus runs late on purpose. Every shop on this route opens at dusk.` |
| Trap Night | Control or stun decks at S1, S2 and S4 | `-: The flyer says TRAP NIGHT. Somebody has set a second flyer face-down underneath it.` |
| Beatdown Boulevard | Beatdown decks at S2 and S5 | `-: Someone has punched clean through the flyer. There's a red thread caught in the staple.` |
| Rookie Road | Two of the three start offers are easy to pilot | `-: Someone has chalked training wheels on the bus stop sign. Kit swears it wasn't them.` |
| Underdog Day | The start offer sits 60 Elo lower | `mabel: Underdog Day. Long odds at the start, dear. The best stories on this bus begin that way.` |
| Back Roads | A seldom-seen champion, and decks the circuits barely use | `-: The bus turns off the main road. The sign at this stop has never once been painted.` |

The red thread on Beatdown Boulevard is a nod to Vera's jacket.

## 6. The bus and the souvenir stand

The bus is a retired route bus with its destination sign stuck on "9 DEPOT" (Gus mentions Penny "used to drive the
nine": a quiet link to the alley).

```
scene: bus-1
when: bus:1
place: bus
kit: The windows open now! I put a poster frame in each one. Towns are already sending us more.
```

Stages 2-5 follow the same pattern (`scene: bus-<n>`, `when: bus:<n>`, `place: bus`):
2. `kit: The destination sign works. Punch in a route code and it shows the same six towns every time.`
3. `kit: New tires. We can take the long way round now. Scenic, and no extra fare.`
4. `kit: Pulled the back seats out for a Pilot's Log wall. Two more towns called. They want a turn.`
5. `-: Kit has a photo taken of the bus, finished, with every house champion squeezed in front of it.`

Stage 5 is a photo, not a postcard: the RR10 postcard stays Kit's last. (ENDGAME-SPEC §4's stage 5 row still says
"Kit's last postcard"; it should read "Kit's photo".)

**Mabel** (`mabel`, new) keeps the stand: a retired tour guide who came with the bus. Tour-guide patter, calls people
"dear", firm that the stand takes Miles only.

```
pool: stand
mabel: Pins, backs, frames and tints. Miles only, dear. Duel Points are the shop's business, not mine.
mabel: Beat a deck on the road and its pin's on my table. Log it with Kit, and I rim it in gold.
mabel: To your left, a card back. To your right, a better card back. Thirty years of tours. Habit.
mabel: Can't afford it yet? Then it's a reason for the next town. That's how souvenirs work.
mabel: Kit's jacket started with one pin. Ask them which. They'll pretend they don't remember.
mabel: No refunds. A souvenir is a promise to remember, and you don't get those back.
```

## 7. Recommendations

**Named towns.** Postcards need a picture on the front, `{town}` lets Kit say where you passed a deck up, and route codes
feel like real trips.
- Each house champion has a home town, used when they're the S3 stop.
- Any town can come up as an ordinary stop.
- Kit's stop is always "the end of the line", unnamed.
- The names are invented compounds; a plain English compound can still match a tiny real hamlet, so flag them to the
  owner.

| Town | Flavor | Home of |
|---|---|---|
| Wirecross | junction | spyral |
| Spindleford | looms | tri-brigade |
| Glassmere | glassworks | nekroz |
| Tollgate Ferry | tolls | power-patron |
| Cinder Hollow | foundry | dracotail |
| Fermata Falls | concerts | elfnote |
| Orrery Point | stargazing | drytron |
| Saltmarsh Reach | harbor | mermail-atlantean |
| Moonwell | nights | lunalight (bus stage 4) |
| Gallery Hill | artists | artmage (bus stage 4) |
| Shufflebury | market | |
| Quarry End | quarry | |

**Vera:** the RR5 postcard only (Kit quotes her once more at RR7, on two strikes). She stays out of the opponent seats: her deck (1,418) sits below the road's windows,
and she's better as a rival than as a stop.

**Milestone posters** (`Posters.cs`):
- **Placement:** add four Pilot's Log posters to `Posters.All` before `midnight-bill`, so `All[^1]` stays the same.
- **Setup:** `HiddenUntil = "road-invite"`, like No Limits' posters; ids `road-log-10`, `road-log-25`, `road-log-50`,
  `road-log-all`. `Earned` reads the log count.
- **Titles:**
  - "The Borrowed Road · ten decks that weren't yours"
  - "· twenty-five decks, one bus"
  - "· fifty decks"
  - "· every deck on the road"
- **Ink only:** no card art. Each draws its title band and fallback in its ink (`Card = 0`).
- **The last threshold** is the live count of reachable decks (ENDGAME-SPEC §4), not a hard-coded 87.
- **Run themes:** the 12 can also be earnable posters (prefix `road:`, earned by clearing a run under that theme), made
  by a factory like `Breaker()`.

**Open for the owner:** after Rank 10, does Kit's Tag Cup partner deck switch from spare Synchrons to Kit's own deck?
