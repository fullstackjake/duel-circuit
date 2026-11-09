# Duel Circuit: story review, structure lens (2026-11-06)

Read-only review of `campaign/story.txt` (as of tonight, `place:` keys ignored), the main cast's profiles,
`campaign/shop.txt`, `alley-voice.txt`, `alley-events.txt`, `circuits.txt`, `guests.txt`, `library.txt`, and the
docs named in the brief. Items already raised in docs/dev/STORY-REVIEW-2026-11-01.md are repeated only where they are
still open (marked **still open**). Binding canon: NO-LIMITS-STORY section 8; every recommendation below respects it
(Kit never through the depot door; "end chair", never "champion", behind the wall; "Local"; pilots never sign; no
as-built twins in a pilot's scene; no real people). Gus, Hollis, Ines and Ilse never come down.

Story scenes have **no `after:`/`before:` gates** (Story.cs reads only `when:` and `place:`); only shop.txt,
alley-voice.txt and van.txt pools do. Several fixes below are text-only; the ones that need an order-aware variant say so.

---

## 1. The spine as it plays (one page)

**Act I: the shop (Beginner, circuits 1-4).** `welcome` (Gus, Vera; era/open) → `gift-rookie-ryo` (the shop rule) →
`beginner-clear` + `gus-lessons` (bounty board) → [Ines unlocks as a guest here: no scene] → `rookie-cup-clear` (you
beat the mentor) → `tag-night` → [Town Tournament: `gift-vera`, no clear scene] → `novice` (lists, Ban Passes; "You
beat me twice").
**Act IIa: leaving town (Novice, Intermediate, Advanced).** `novice-tag`, `intermediate`, `advanced` (Wren, Gus's
four-hour grudge) → **`golden-age-clear`: the midpoint** ("do you belong at their table?"; Ines planted) →
`advanced-tag-clear` (optional: the corkboard, the Duchess).
**Act IIb: the big rooms (Expert, Master).** `expert` (4th pass) → `fall-format` → `master` (5th pass) →
`october-list` → `gauntlet`. Ten of the 21 circuits play no scene, including both Expert middles and both Master
middles (Link Wars, Graveyard Kings).
**Climax 1.** `champion` ("Then you belong here"; white coat; corkboard under Gus) + `champion-after`.

**Post-game: three strands that braid, each ending upstairs.**
- **A. The Library (Hollis): history.** `library-opens` → 12 cups in any order (intro + clear each; `first-stamp`,
  `stamp-goat`, `first-locals`, `first-bracket-title`) → `every-era-final` (Ilse arrives) → rules, rounds 2/4/6 →
  `every-era-clear` (your deck starts this year's binder) → `every-era-corkboard` (your name above Gus's; the ladder
  hint). Ends: "Shop's open tomorrow."
- **B. The Back Alley (Kit): what a rule costs.** Opens on the first cup clear: `alley-rumor` → `alley-kit` /
  `alley-baz` / `alley-upstairs` → `van-first` → Dumpster Row → Basement (`vera-ante`) → Black Market → three at once:
  Midnight Open (Nocturne's name), Orvel's Table, Nyx's Table → **`held-card` + `held-card-home`** (Orvel's clear ×
  `cup-goat-clear`: the only composite trigger, and the best scene in the game) → `nyx-table-clear` + `alley-closing`.
  Ends: "Somebody waited up."
- **C. No Limits (Junia): what's left when a rule takes your best thing.** Out-of-towners at the Brawl (after The
  Black Market) → `nl-wall-opens` / `nl-wall-kit` (the visit after `alley-closing`) → `nl-first-visit` → pilots
  (`nl-<id>`, `nl-<id>-beat`) → `nl-restored` → `nl-clear` → Invitational (wave 2, parked) → `nl-invitational-win`
  + `nl-invitational-home`. Ends: Gus's unpriced sleeve.

**The through-line is naming.** Each strand renames the player, and each name answers that strand's question:
"kid" → champion ("you belong") → a name above Gus's (history) → Upstairs, Curbside, Downstairs, Boots, Midnight
(the alley) → Local (No Limits). Every strand also ends upstairs with Gus. Both are strong and should be the frame
for any new scene.

**Tension curve.** Peaks: `rookie-cup-clear`, Town Tournament (Vera), `golden-age-clear`, `gauntlet`/`champion`,
`every-era-clear`, `held-card-home`, `nyx-table-clear`, `nl-invitational-win`. Valleys: circuits 5-10 and 13-20 are
silent; the Library's 12 cups are episodic with no midpoint; NL's middle is a gallery of 11 two-scene pilots.

---

## 2. Findings

### A. Arc and through-line

**A1. Ilse is a threshold guardian with no threshold.** Scenes `champion`, `advanced-tag-clear`, `every-era-final`,
`every-era-rules`, `every-era-clear`, `every-era-corkboard`; `ilse.profile`. The game's last unfought figure: her
name sits above yours on the corkboard, her profile has intro/win/lose and eight reaction lines for a duel that
never happens, she says "Show me none of it was luck" and "I'm near the top. I'm not in a hurry", and then she leaves.
In Campbell/Vogler terms the story crosses the last threshold without its guardian's test; in Chekhov's terms her
four house decks are a gun on the wall. **Recommend:** make her a guest that unlocks after `every-era-corkboard`
(guests.txt needs one new unlock rule, e.g. `unlock: final`), playing her four decks ("none of them belong to a
year") as a best of three or a four-duel sitting. Two scenes:
- `ilse-table` (before the first duel, `open:`-style or a guest trigger): "-: She hangs the white coat on Gus's
  coat rack. First time it's come off in this shop." / "ilse: Four decks. No year. This one's only between us."
- `gift-ilse` (`beat:ilse`; Ilse has no `signature:` yet, so give her one or let the trigger fire without a card): she unpins her own card from the corkboard and pins it under yours: "Under yours. I've
  wanted to see what it's like." Gus: "Took my glasses off for the last one. Not doing it twice."
That pays off the corkboard order (Duchess > Gus > you, then you > Gus, then you on top) and the white coat.
**Still open** from 2026-11-01 ("make her a guest unlocked by the final").

**A2. The Library has no midpoint and no rising line.** The 12 cups are self-contained vignettes (by design: any order),
so between `library-opens` and `every-era-final` the only threads that rise are Vera's and Ines's. Ilse's best line,
"I held the title for years. I never once beat a year with its own cards" (`every-era-clear`), has no setup: nobody
has said she ever tried. **Recommend:** plant it in `first-stamp`, the one Library scene every player sees early:
`hollis: One person asked me for this stamp and wouldn't play a year's own cards to earn it. White coat. She'll hear
about you.` That gives the Library a question (can you do what the Duchess wouldn't?) and turns her confession into
a payoff (Barthes' hermeneutic code: a question posed, then answered).

**A3. The main path plants the Duchess only once, in the climax.** The corkboard plant is in `advanced-tag-clear`
(optional), so for most players `champion` introduces the white coat and the Duchess's card in the same scene that
ends the act. **Recommend:** a new `graveyard-kings-clear` (`when: clear:Master League: Graveyard Kings`, a silent
circuit before the last two) with the plant on the critical path: Gus at the corkboard, "One name's been at the top
of this board since before Vera could shuffle. White coat. She never came back to defend it." It also breaks the
longest silent stretch in Act IIb.

**A4. "Lose and learn" is the game's first line and never happens to the player (still open).** `welcome` ("Lose, and
you still learn something"), `champion` ("You stopped giving me the chance"). The first scene that reacts to the
player losing is `first-stake-lost`, in the post-game. There is also a rule gap: the shop rule says the loser gives
the winner a card (`gift-rookie-ryo`), but when the player loses, nobody mentions it. **Recommend:** a `firstloss`
trigger (the first circuit loss) and one scene: `old-gus: Shop rule cuts one way for new faces, kid. You don't owe
anybody a card till your binder's worth asking for.` / `old-gus: Told you. You learned something. What was it?` It
dramatizes the controlling idea and plants the alley's ante (your binder will be worth asking for).

**A5. The post-game braids but never converges.** The only cross-strand knot is `heldcard` (Library × Alley). Each
strand ends upstairs, but no scene sees the whole: the player has three titles and three names, and nobody says them
together. **Recommend:** one finale scene on a composite trigger (like `heldcard`): `every-era-corkboard`,
`alley-closing` and `nl-invitational-win` (or `nl-clear` while the Invitational is parked) all seen. Canon-safe
staging: Gus props the shop's back door open (the door he "never fixed") and stands at the top of the step; he doesn't
come down. Below, Baz looks up and, for the first time, chalks a name for him (ALLEY-STORY section 4 says Baz has
never named Gus, but no shipped line plants it; plant it with a Baz footer or one line in `alley-baz`: "The man
upstairs doesn't get a name. He knows why."). Kit sits on the step between. Last line from Gus: "Shop's open
tomorrow. Both doors." The bell (Nocturne: "That bell used to hang over a shop door") rings.

### B. Character arcs

| Who | Arc type | Want / need | Status |
|---|---|---|---|
| Gus | transformative (Truby: moral) | keep his rules / let 2005 go | Complete, but the climax is off-screen (B1) |
| Vera | transformative | hit hardest / learn to wait | Complete except the final test (B2) |
| Hollis | quiet transformative | keep the lists / see why people won't let go | Complete (`unlisted`, `nl-restored`) |
| Ines | transformative | be exact / accept what was lost fair | Complete in `held-card-home`; no introduction (B3) |
| Kit | steadfast | watch / (unstated) | Motive unwritten (B4) |
| Ilse | static, promised change | ? / ? | Confession without setup or test (A1, A2) |
| Junia | transformative | keep the summer / lose fair | Turn at `nl-junia-beat`; final state unstated (B5) |
| Remy | comedic | the chair / not the only summer | Order-dependent (D1) |
| Baz, Moth, Nyx, Penny | flat keepers | — | Right for their function; Moth and Penny pay off |
| Circuit bosses | Proppian donors | — | Right (gift scenes, Library returns) |

**B1. Gus's transformation happens in a random footer.** `held-card-home`; shop.txt `after: held-card-home` ("Fixed
the back door. Don't read into it."). The back door is Gus's objective correlative: "Back door sticks. Never fixed it.
Never will." (footer), Kit holding it open with a foot (`alley-kit`). Its fixing is the visible proof he has let 2005
go, and it only shows if the footer happens to rotate in. **Recommend:** add two lines to the end of
`held-card-home`: "-: On your way out, the back door swings shut behind you without sticking." / "old-gus (from the
counter): Oiled it. Don't read into it." Keep the footer as the echo.

**B2. Vera's rival arc has no final duel.** Her last duels are the Town Tournament and Regional Qualifier; after that
she is commentary. Open promises: "Next time I'm setting a trap" (`novice`), "...Okay, set one" (`partner-vera`),
"Show me how you waited" (`cup-goat-clear`), "Don't speed up" (`every-era-round-4`), "So that's the name to beat
now. ...Good." (`every-era-corkboard`). The rival plot (Vogler's "shadow" as mirror) wants a last mirror match.
**Recommend:** Vera as a guest unlocked after `every-era-corkboard`, on a warriors build that runs and *waits on*
traps (a real deck, full strength), plus `vera-rematch` (before): "I set one. I'm going to wait for it. Don't laugh."
and an after-win scene (`beat:vera` is already spent on `gift-vera`, so this needs the guest's own first-win
trigger): "You still hit harder. I waited better. ...Call it a draw. It isn't."
If guests are too costly, at minimum make `every-era-corkboard`'s Vera line not promise a duel.

**B3. Ines has no introduction scene.** She unlocks as a guest straight after the lessons (`guests.txt`), her blurb
introduces her, and her first-win gift (Thousand-Eyes Restrict, `ines.profile signature:`) is the only signature in
the game with **no `beat:` scene** (every other signature has one). Her first story line for most players is
`golden-age-clear` or `cup-goat`. **Recommend:** `gift-ines` (`when: beat:ines`): "ines: Gus said you could count.
Gus can't, so I checked." / "-: She sets {card} down face up, square to the table edge." / "ines: Gus has hated this
card since 2005. Ask him about 2005. He won't answer. That's the answer." It plants the 2005 knot in Act I, so
`cup-goat-clear` and `held-card-home` pay off something the player has carried the whole game. (Move bond-ines-5's
"hated since 2005" to a different joke if it doubles.)

**B4. Kit's refusal has no motive on the critical path.** Kit's only stated wound is in `first-railgone` ("It
happened to me. I still look for it in other people's binders."), which plays only if the player lets one of their own
cards go off the rail. Without it, "This is where I get off" (`nl-wall-kit`) is a rule without a reason, and Kit
(canon: never through the door) reads as static by fiat. Kit also has no introduction: `partner-kit` is optional, and
`alley-rumor` assumes you know them. **Recommend:**
- `tag-night`: add "kit: Kit. Next table over. Nobody you've beaten wants you yet? I'll partner anyone." (Kit is a
  partner from the start, so this is accurate.)
- `nl-wall-kit`: after "Nobody behind there is carrying spares", add "kit: I put up a real card out here once. Not a
  spare. I still look for it in other people's binders." Keep `first-railgone`'s line; it becomes an echo.
That makes Kit a coherent steadfast character (the one who learned the alley's lesson before you arrived) and gives
`nl-invitational-win`'s "best thing I've ever not been in" its weight.

**B5. Junia's season never turns on screen, and "Local" is retired without a replacement.** `nl-junia-beat` ("It's
cooler in here than I remembered") is the turn; `nl-invitational-win` says "Local's the wrong word now" but gives no
new word, and her pools after the beat still say "It's still summer". **Recommend:** in `nl-invitational-win`,
"junia: Local's the wrong word now. {nick}, then. I'll say it at the next door." and "junia: Feels like September.
I'd forgotten September." (Her voice avoids "over" and "ended"; a month keeps it.) Canon item 6 anticipates a word
after the title.

**B6. Small arcs set up and never paid (low priority, still open).** Ryo's "You're who I want to be when I'm good"
(`bond-rookie-ryo-5`); Rhea and Seraph never acknowledged in a scene; Orvel and Ilara are Fall Format Tag Cup
opponents upstairs and nobody in the alley remembers (only line-safe if neutral: Orvel in `orvel-table`, "I've
played under upstairs rules. Down here, the table's rules are older."). One shop footer for Ryo after
`every-era-corkboard` would do: "old-gus: Ryo's started a second notebook. It's all your duels."

### C. Setup and payoff ledger

| Thread | Set up | Payoff | Verdict |
|---|---|---|---|
| Ines's lost 2005 card | `cup-goat-clear`, `van-first` (HELD), footers | `held-card`, `held-card-home`, `van:held-paid` | Paid; plant it earlier (B3) |
| Gus's folding table | footer "one folding table", `cup-goat` | `orvel-table` (Nocturne) | Paid |
| The shop's old bell | `welcome` (a bell), `alley-closing` (Nocturne) | none | Open: use it in the finale (A5) |
| Gus's back door | footers, `alley-kit` | footer only | On screen in B1 |
| Baz never names Gus | ALLEY-STORY only | none | Unplanted in game; plant and pay (A5) |
| Corkboard order | `advanced-tag-clear`, `champion` | `every-era-corkboard` (above Gus) | Half paid: the Duchess stays on top (A1) |
| White coat | `champion` | `every-era-final` | Paid |
| Ilse never beat a year | none | `every-era-clear` | Payoff without setup (A2) |
| Ilse's "ladder" | `every-era-corkboard` | unbuilt Champion's Ladder | Debt (owner 3) |
| Junia's "other doors" | `nl-invitational-win` | unbuilt | Same debt as the ladder (owner 3) |
| "This year's binder" | `cup-ryzeal` | `every-era-clear` | Paid |
| Nocturne's name | `nocturne.profile` lose line | `midnight-open-win` (drowned) | Paid as designed; "someone" should be "you" once scenes can gate (D6) |
| Who kept the key before Nyx | `nl-clear` | deliberately none (canon) | Keep; write a private bible answer so later lines can't contradict (owner 5) |
| Kit's lost card | `first-railgone` (optional) | none | Move to `nl-wall-kit` (B4) |
| Kit never through the door | `nl-wall-kit` | `nl-restored`, `nl-invitational`, `nl-invitational-win` | Paid; needs motive (B4) |
| Remy against Junia for the chair | `nl-remy` | `nl-remy-beat` | Order-dependent (D1) |
| Ines's Scapegoat back in her deck | ALLEY-STORY owner question 6 | none | Optional; Every Era round 2 could nod once scenes can gate (D6) |
| Vera's trap | `novice`, `partner-vera` | none | Debt (B2) |
| "Points get spent, cards keep" | `gus-lessons` | the alley's ante makes it literal; nobody says so | Free rhyme (E2) |
| Penny's streak | `dumpster-row`, pools | `penny-lost` | Paid |
| Penny drove the depot bus | `nl-wall-opens` | `nl-invitational-home` (Gus remembers) | Paid |

### D. Continuity and timeline

**D1. No Limits wave 2 breaks four scenes under the decided champion order.** `alley-events.txt` (`champions: remy,
junia`: the first ready, unbeaten champion closes). Once Remy is ready, a player who arrives fresh meets Remy in the
end chair, but:
- `nl-first-visit`: "junia: The end chair's mine. ... Come and get it." (he's in it);
- `nl-junia`: "baz: The end chair! Spoils!" (she's in a side seat);
- `nl-junia-beat`: "Two years in that chair. Sit in it a minute, Local." (she isn't in it);
- `nl-remy-beat`: "junia: Welcome to the summer after, Rotary." plays even if Junia hasn't lost yet, while she still
  insists "It is still summer": her arc runs backwards.
**Recommend (text-only, works with the decided order):**
- `nl-first-visit`: "junia: The end chair's been mine since the week they took Spoils. Whoever's in it tonight,
  come and get it."
- `nl-junia`: "baz: Spoils! The one who started it all! Three of the card the list took, and two years of not letting
  go!"
- `nl-junia-beat`: "junia: Two years I kept that chair warm. Go and sit in it a minute, Local. Whoever's there can
  move."
- `nl-remy-beat`: replace Junia's line with Rue's (she has no arc stake in it): "rue: Every summer thinks it's the
  only one, Rotary. Hers did too." Or keep Junia's line behind an `after: nl-junia-beat` gate (D6).
See owner decision 2 for the story-first alternative.

**D2. Nyx's Table no longer matches its intro.** `nyx-table`: "Witch, Sangan, the Emperor, the crow." The Phase D
difficulty pass moved seat 2 from Baron Corvus's chaos-yata (the Emperor and the crow) to Volta Kask's Thunder Dragon,
as built (`alley-events.txt`). Volta is a Master League boss and the Every Era's round 4, a 2018 deck, at a table
whose premise is "the cards say what they said when they were printed". Nobody acknowledges him. **Recommend:**
"nyx: Witch, Sangan, the old words. I keep them the old way. Somebody has to." then "nyx: The second seat's a
storm. Volta asked to sit where nobody searches." (Volta's own hook is "nobody searches"; the line explains a modern
deck at an old table.)

**D3. The Invitational's board contradicts canon item 8.** `nl-invitational`: "Sixteen names: half chalked, half
signed." The field (`alley-events.txt`) is you, eleven pilots and four alley regulars (Ilara, Orvel, Nyx, Nocturne):
pilots never sign (canon 8), alley regulars are chalked (ALLEY-STORY section 4), so nobody signs. **Recommend:**
"-: Baz has propped three chalkboards against the depot wall. Sixteen names, all in chalk. Eleven in the smaller
hand." (calls back `nl-outoftowners`' "smaller hand").

**D4. `alley-closing` can crown you over a reigning champion.** "Baz wipes the board down to bare slate, then chalks
one name back at the top. {nick}." Nyx's Table can clear before the Midnight Open title, while Ilara still holds "the
top of that board all season" (`black-market-clear`) and {nick} is still Boots. **Recommend:** "...then chalks one
name back, bigger than the rest."

**D5. Lines that anticipate what may already be past.** Tag Cups block nothing, so they can be cleared after the
champion title. `expert-tag-clear`: "When the Master League asks who you are, tell them about this." →
"When anybody asks who you are, tell them about this." Also `nl-outoftowners`: Kit's "They come through every few
months. Never the same town twice." contradicts itself (and Nyx's "Every few months ... They play them here") →
"They come through every few months. Never for more than a night." (matches Baz's "In for one night").

**D6. Order-dependent scenes need gates the scene format doesn't have.** Cases: D1's Junia line; Nocturne's "I
promised someone my name once" (it's "you" only if you beat Nocturne as a guest); `every-era-round-2` not knowing
whether Ines has her Scapegoat back (`held-card-home` can come before or after the final); `vera-ante` playing after
Vera's "Don't speed up" growth. **Recommend:** give story scenes the shop.txt gates (`after:` / `before:`,
repeatable, all must hold), so a scene can have an order-aware twin with the same trigger. Small engine change in
Story.cs's parser and `Pending`, plus a `--campaign-test` check that every gated id exists. Example once available:
`every-era-round-2b` (`after: held-card-home`): "ines: Played it with the bent corner. Still lost. ...Best summer."

**D7. Era and open play read differently down the alley, and the scenes don't know.** In open play your team never
answered to a list (Forbidden cards are legal upstairs), so `alley-kit`'s "No list down here. One copy of anything" is
a new restriction, and `nl-first-visit`'s "No list ... that goes for your deck too" is no news. The Library scenes
already split `[era]`/`[open]`. **Recommend:** `kit [open]: Upstairs nobody checks your deck. Down here, one copy of
anything, yours too. Everybody shows their contraband first.` and `junia [open]: You've never played under a list,
Local. I can tell. Neither have we. Three of anything.` (keep the current lines as `[era]`).

**D8. `held-card` can play straight after a Library scene with no time cut.** When `cup-goat-clear` is the second
condition, `held-card` follows Ines's "I lost a card in 2005" upstairs and opens at the van's hatch. The juxtaposition
is good; the jump is abrupt. **Recommend:** open with "-: That Friday, Moth is waiting at the van's hatch, holding
the toploader from the end of the rail."

**D9. Minor.** `partner-ines` "Twenty years" against "Twenty-one years" (`held-card`): fine as speech, but "Twenty-one
years, and she never once asked me that" would rhyme with Moth. Nyx's profile `lose:` ("Even the dead know when to
stay down") contradicts her table voice ("Old words don't end when they lose"); → "Back in the crate. They've rested
before." `nl-restored`'s Hollis coda reopens with the same sentence as `unlisted` ("Next morning, there's a ...
chalk-smudged list on Hollis's desk"); intended as a rhyme, it reads as a repeat: "Hollis has a second pencil out
before you've finished the story."

### E. Theme

**E1. The controlling idea (McKee) holds across all four parts:** *a card means something when it is played and lost
fair; rules exist so everybody gets to play, and they cost the people who keep them.* The shop rule (the loser gives
the winner a card), the bond trades, the alley's ante, Ines's card ("You won it fair, Gus. That was the whole
trouble"), and No Limits ("A list can't beat anybody. It just takes things") are one argument, stated four ways.
Collecting is its material form: the commons kept (`first-set`), Penny's Common, Ines's pack of commons, the Raw 6
Scapegoat worth more than any slab. The alley **strengthens** it (it gives the rules a cost and a history). No Limits
**extends** it to rules made far away (the list), and keeps Ilara as the counterweight ("Lists are weather").

**E2. Where it dilutes.** (a) The eras axis is climbed four times (circuits by strength, Library by year, alley by
contraband era, No Limits by month); each changes the question, but the repetition shows in No Limits' gallery of
eleven grievances, where only Junia, Remy and Rue have arcs. Keep the gallery, but make sure the Invitational is
Junia's scene, not the room's (B5). (b) Hollis, the list's advocate, never answers Junia; his only No Limits line
concedes. One sentence would keep the argument two-sided: in `nl-restored`'s coda, "hollis: A list is a promise that
everybody gets to play. Sometimes it breaks one to keep it." (Junia's rule in the list-keeper's mouth.) (c) The
"Points get spent, cards keep" lesson (`gus-lessons`) is the alley's whole economy, and nobody says so: add to
`first-ante-win` "kit: Points get spent. Cards keep. Gus taught you that? Down here it's the whole economy."

**E3. The theme's first statement is never dramatized** (A4).

### F. Gaps: beats a player would expect

1. A duel with the Duchess (A1).
2. A last match with the rival (B2).
3. A scene for your first loss (A4).
4. A scene on the critical path between `master` and `october-list`: Link Wars and Graveyard Kings, the hardest pair,
   are silent (A3; **still open** in part: 10 of 21 circuits have no scene).
5. Upstairs hearing about the alley's titles. There is no footer after `midnight-open-win` or `nl-clear`. shop.txt
   supports these now: `after: midnight-open-win` "old-gus: Somebody chalked MIDNIGHT on my back step. Damp cloth's
   under the counter."; `after: nl-clear` "hollis: A whole table of two-date cards. I've run out of column."
6. One finale that sees all three strands (A5).
7. New Game+ still has no story beyond `new-game-plus` (**still open**; Seen is kept). Needs D6's gates to replay
   variants; otherwise leave it.

---

## 3. Top 10, ranked by value for effort

1. **Make the No Limits chair scenes chair-neutral** (D1): four text edits in `nl-first-visit`, `nl-junia`,
   `nl-junia-beat`, `nl-remy-beat`. Prevents broken scenes the day wave 2 ships.
2. **Continuity one-liners** (D2, D3, D4, D5, D8): `nyx-table` seat 2, `nl-invitational` "all in chalk",
   `alley-closing` "bigger than the rest", `expert-tag-clear`, `nl-outoftowners` Kit, `held-card` "That Friday".
   Text-only, an hour.
3. **Plant the Duchess on the critical path and give her confession a setup** (A2, A3): one line in `first-stamp`, a
   new `graveyard-kings-clear` scene. Text-only (the triggers exist).
4. **Put Gus's back door on screen** (B1): two lines at the end of `held-card-home`.
5. **Give Kit an introduction and a motive** (B4): two lines in `tag-night`, one in `nl-wall-kit`.
6. **`gift-ines`** (B3): one scene on an existing trigger; plants 2005 in Act I.
7. **Junia's last word** (B5): `{nick}` and "September" in `nl-invitational-win`.
8. **`[open]` variants** for `alley-kit` and `nl-first-visit` (D7).
9. **The Duchess's table** (A1): a guest unlocked after the Every Era final (one new unlock rule), two scenes, the
   corkboard reorder. Medium effort, the highest value on this list.
10. **`after:`/`before:` gates for story scenes** (D6): a small Story.cs change that unlocks every order-aware variant
    (Junia/Remy, Ines's Scapegoat in the final, Nocturne's "you", New Game+).

Next in line: the `firstloss` trigger and scene (A4); Vera's rematch (B2); the three-strand finale with Baz naming
Gus (A5); the two upstairs footers (F5); Hollis's answer to Junia and Kit's "cards keep" line (E2).

---

## 4. Owner decisions (recommendation first)

1. **Does the Duchess duel?** *Recommended:* yes, as a guest after the Every Era final, her four house decks, and a
   win moves your name to the top of the corkboard. *Or* she never duels: then she moves her own card under yours in
   `every-era-corkboard`, and her "I'm near the top" ladder line goes.
2. **No Limits' end chair once Remy is ready.** *Recommended:* keep the decided order (Remy closes, Junia wildcards)
   and ship D1's chair-neutral text. *Or, story first:* Junia holds the chair until she's beaten, then Remy takes it
   (the old summer ends before the new one sits). Every shipped line then reads right as written, but it changes the
   decided rule and the end chair's 50% measuring while Junia sits.
3. **The two promises to an unbuilt ladder** (Ilse's "Past the binders there's a ladder", Junia's "other doors").
   *Recommended:* commit to the Champion's Ladder as the one place both lead (Ilse near the top, the out-of-towners
   as visiting rungs). *Or* soften both lines now so they promise nothing.
4. **Vera's last match.** *Recommended:* a post-final guest on a warriors build that waits on its traps. *Or* no duel,
   and `every-era-corkboard`'s "the name to beat" becomes "the name to watch".
5. **Who kept the key before Nyx** (canon: unanswered). *Recommended:* keep it unanswered in the game, but write the
   answer in a private bible so no later line contradicts it. *Or* leave it undecided even privately.
6. **A `firstloss` scene.** *Recommended:* yes, one scene (A4). *Or* keep the main campaign loss-silent and let the
   alley be where losing first costs something (then cut "Lose, and you still learn something" from `welcome`, since
   it promises a beat that never comes).
7. **A three-strand finale.** *Recommended:* yes, on a composite trigger, with Gus at the top of the back step (he
   doesn't come down) and Baz naming him. *Or* let each strand end on its own, as now.
