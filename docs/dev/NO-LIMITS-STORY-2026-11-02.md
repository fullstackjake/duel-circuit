# No Limits: story, cast voices and lines (2026-11-02)

The Narrative Designer's part of the No Limits tier the owner approved in docs/dev/META-DECKS-2026-11-02.md (Appendix A).
It builds on docs/dev/ALLEY-STORY-2026-11-01.md, campaign/story.txt (the Back Alley block),
campaign/alley-voice.txt, campaign/van.txt, campaign/alley-events.txt, ALLEY-SPEC section 12, and
docs/dev/META-RESEARCH-2026-11-02.md for each deck's real arc. Nothing in the game changed.

**Coordination.** The Game Designer (docs/dev/NO-LIMITS-SPEC-2026-11-02.md) and the Economy Designer
(docs/dev/NO-LIMITS-ECONOMY-2026-11-02.md) are writing in parallel; neither had landed when this was written.
- **Triggers, hooks, ids and placeholders below are proposals.** Each is marked **[new]**. Where the spec names
  them differently, the spec wins and these rename one-for-one; the lines don't depend on the names.
- **The lines fit any stake model.** No line says how many DP you put up, whether it comes back, or how many copies
  of a restored card a pilot holds. `{card}` is whatever changed hands.
- **The lines fit either champion order** (Snake-Eye closing, or Kewl Tune closing once ready). Where it matters,
  pools are gated on the Kewl Tune pilot's first scene. See owner question 2.
- **Real people.** Every pilot is fictional and original. Lines say what a *deck* did ("the deck won the biggest
  weekend of the year"), never who played it, and name no event a real person won. No character is styled on a real
  player or on an existing anime or manga character.

## The story in one paragraph

At the far end of the alley, Nyx's lantern hangs on a nail beside a wall. The night you clear her table she turns it
down, and the wall is only a wall. The next Friday she turns it all the way up, and there's a steel door in the
bricks you never noticed, painted to match. Behind it is the old bus depot, and a game the out-of-towners carry from
town to town: **No Limits**. No list at all. Three of anything. Every deck at the long table is one the list cut down
at its best, played the way it was the month before the cut, and every pilot stakes the very card the list took. Its
host is Junia Calder, who started the game in a car park the week the list took her Spoils, and has held the end
chair ever since. The list never beat any of them. It only took things. You can beat them, and when you do, the card
is lost fair for the first time, and they can finally put it down.

**Theme.**
- The Library asks what each era's rules were.
- The alley asks what a rule costs the people who keep it (Gus's "spares only", Ines's "I check values").
- **No Limits asks what's left of you after a rule takes your best thing, and whether you'd want it back.**
  - Junia wants it back forever: it's "still July" at her table.
  - Ilara Gale is the counterweight: lists are weather, and you trim your sails.
  - The player is the answer neither of them has: a fair loss. A list takes a card; a duel beats you with it.
- It rhymes with Ines without undoing her: her Scapegoat was *lost fair*, and that was the whole trouble. Their
  cards were taken *unfairly*, and that's theirs. Your win gives them what Ines had and they never got.

## 1. What No Limits is in the world

### The place: the Depot
- The steel door in Nyx's wall opens on the old bus depot: one long shed, boarded inspection pits, strip lights, a
  smell of diesel and arguments. Penny drove the number 9 out of it for thirty years.
- One long table runs down the middle. Three chairs down the sides, one chair at the far end with its back to the
  wall: **the end chair** (seat 4, the champion's seat). In-world, nobody says "champion": it's "the end chair",
  so the word stays Ilara's (the Midnight Open's "champ").
- Baz follows you in with the bell and props a chalkboard against an old timetable. Kit doesn't come through.

### Who runs it
- **Nyx keeps the door.** She hosts nothing and stakes nothing behind it. She is the gravekeeper, and she keeps all
  the graves, the fresh ones too: "The old ones rest. These were put down too young. They don't rest yet." The
  alley's oldest contraband sleeps in her crate; the newest is played behind her wall until it can be let go.
- **Junia Calder runs the game.** She started it the week the list took Original Sinful Spoils: a folding table in a
  car park, a sign that said NO LIST, twelve people. It has travelled since, and it plays wherever someone keeps a
  door for it. Here, that's Nyx. Junia holds the end chair, deals the seats and sets the one rule.
- **Baz MCs,** as he does every board in the alley ("A bell's a bell! It rings anywhere!").
- **Who had the key before Nyx** is left a mystery (Tier 3; Nocturne points at it and stops).

### Why "no list"
- The alley's "one copy of anything" is still a list (Traditional). Behind the wall there's none: **three of
  anything**, for you and for them. That's the symmetry the design wants: you can finally run what they run.
- **The one rule:** "Everybody gets to play. No card that stops the other one." This is the in-world
  form of `Alley.Never` and `Alley.BossOnly` (loops the bots can't stop, floodgates, Barrier Statues, Djinn,
  Protos). Junia says it isn't a limit, it's manners. Baz's turn-40 bell and the first-turn guard still hold: they're
  Baz's and the House's, and Baz brought the bell.
- **The stake:** each pilot puts up the card the list took (the binder leads with it). It's the reason they're
  there: a card taken by a list is a grievance; a card lost at a table is finished.

### How it changes the alley's ending without undoing Nyx's
- **Nyx's clear still ends the alley's story.** `nyx-table-clear` and `alley-closing` play exactly as shipped, and
  that night "the far end of the alley goes back to being a wall". It does.
- **The door opens on a later visit.** The `nolimits` trigger fires the next time the Back Alley tab is shown after
  `alley-closing` has been seen, never the same night. The alley's last word stays "Somebody waited up."
- **It was always a wall with a door in it.** The lantern was the signal: down means closed. Nothing shipped says
  there was no door.
- **No Limits is about the wider world arriving,** not the alley's own threads (Gus, Ines, Moth, Orvel, Nyx's crate),
  which all stay closed. Its own story closes with the Invitational title.
- **Baz's ladder stays finished.** "Midnight" is for good (`midnight-open-win`). The out-of-towners have their own
  word for you, **Local**, until the Invitational title.

### The tab's status line [new]
`No Limits · no list, three of anything · they stake the card the list took · everybody gets to play`

## 2. Hooks

| Trigger | Fires | Scene(s) here |
|---|---|---|
| **[new]** `outoftowner` | The first Alley Brawl field (bracket screen, before round 1) that shows an out-of-towner | `nl-outoftowners` |
| **[new]** `outoftowner:<id>` | Before your first Brawl match against that out-of-towner | `nl-oot-<id>` (4) |
| **[new]** `nolimits` | The wall opens: the next Back Alley tab shown after `alley-closing` was seen (also the catch-up for saves already past it) | `nl-wall-opens`, `nl-wall-kit` |
| `open:No Limits` | The table's panel first opens (existing pattern) | `nl-first-visit` |
| **[new]** `pilot:<id>` | Before your first No Limits duel against that pilot (the `headliner:` pattern) | `nl-<id>` (11) |
| **[new]** `pilotbeat:<id>` | Your first win against that pilot at No Limits | `nl-<id>-beat` (11) |
| **[new]** `restored` | The first time you win a pilot's restored card (any pilot) | `nl-restored` |
| `alleyclear:No Limits` | The table's first clear (existing pattern) | `nl-clear` |
| **[new]** `invitational` | Your first Invitational entry | `nl-invitational` |
| `bracketwin:No Limits Invitational` | Your first Invitational title | `nl-invitational-win`, `nl-invitational-home` |

- Existing triggers keep working at No Limits: `encore`, `turncap`, `firstturn` (their first-time scenes are
  table-neutral). `unlisted` (the first Unlisted page) may now come from a pilot; its scene is neutral too.
- **Placeholders:** the alley's `{card}`, `{handle}`, `{nick}`, `{round}`. No new ones. "Local" is literal text.
- **Speaker ids [new]:** `junia`, `remy`, `anselm`, `tova`, `augie`, `sasha`, `imogen`, `rue`, `lux`, `margo`,
  `inka` (first names, like `penny` and `kit`; each needs a profile, section 6). The speaker token `opponent` works
  in `nl-restored`.
- **If the spec prefers fewer triggers:** `pilot:<id>` can be dropped (its 11 scenes become versus pools only);
  `pilotbeat:<id>` and `restored` carry the story. `outoftowner:<id>` can likewise fold into `outoftowner`.

## 3. The cast

### The out-of-towners

Every pilot carries a toploader on a cord around their neck (the costume motif, in every `look:`). At the Brawl it
holds what the street's one-copy rule won't let them play. Behind the wall it's empty, because the card is in the
deck. Baz chalks each by what they carry; that's the handle.

| Id | Name | Pronouns | Handle | Deck (peak build) | Restored (own banned cards) | Title | Hook (the deck's real arc) |
|---|---|---|---|---|---|---|---|
| `junia` | Junia Calder | she/her | Spoils | Snake-Eye (Fiendsmith, 2024) | Original Sinful Spoils - Snake-Eye; Fiendsmith's Lacrima | the Long Summer | The deck of the three years; owned 2024; Spoils taken in December. She started No Limits that week. **End chair, wave 1. Brawl.** |
| `remy` | Remy Okoro | he/him | Rotary | Kewl Tune (2026) | Kewl Tune Rotary | on the Decks | The deck that won the biggest table of 2026; Rotary pulled three weeks later. He's come for Junia's chair. **Champion, wave 2.** |
| `anselm` | Anselm Quist | he/him | Habakiri | Ryzeal Mitsurugi (2025) | Ame no Habakiri no Mitsurugi | the Untouched | Top of the room all of 2025 and never hit at its peak; banned a year late, twice. |
| `tova` | Tova Kincaid | she/her | Detonator | Ryzeal (Fiendsmith, winter 2024-25) | Ryzeal Detonator; Ext, Ice and Sword Ryzeal | Half the Room | Nearly half of every top table one winter; she played the mirror all day. Her hybrid wins got filed under Mitsurugi. |
| `augie` | Augie Marr | he/him | Noroi | K9 Vanquish Soul (2025) | K9-04 Noroi; Razen, Stake your Soul!, Hollie Sue, Jokul | with the Hounds | The 2025 world deck; cards taken, given back, taken again. **Brawl.** |
| `sasha` | Sasha Penhallow | they/them | Sangen | Tenpai Dragon (2024) | Sangen Summoning, Tenpai Dragon Chundra, Sangen Kaimen | After You | The going-second deck; second-most played in 2024's biggest room. Second is the seat they chose. **Brawl.** |
| `imogen` | Imogen Starling | she/her | Albion | Branded Despia (2026) | Albion the Branded Dragon, Branded Expulsion; Branded Fusion, High Spirits | the Understudy | Thirteen national titles in 2026 and never the big one; spliced with Lux's deck for the year's biggest final, second. |
| `rue` | Rue Marchbanks | they/them | Lacrima | Fiendsmith Yubel (2024) | Fiendsmith's Lacrima; Phantom of Yubel, Opening of the Spirit Gates | the Night Shift | The list took Lacrima the week the deck won the biggest weekend of 2024. Junia had the summer; Rue's deck had the weekend. **Brawl.** |
| `lux` | Lux Ferrand | they/them | Dragoon | Light and Darkness Ritual (2026) | Magicians' Souls, Red-Eyes Dark Dragoon, Zenna's, Azamina Ilia Silvia | the Monday After | The freshest cut: the deck won a big room on a Saturday; on Monday the list took four cards. |
| `margo` | Margo Kessel | she/her | Dormouse | Maliss (2025) | Maliss <Q> White Binder; Dormouse, White Rabbit, Underground | On Call | Patched four times in a year and still running, until the fourth. **Wave 2.** |
| `inka` | Inka Moll | she/her | Bookmark | Toon (OCG 2026) | Toon Table of Contents, Toon Bookmark (Toon World the Perfect World) | the Early Edition | Top of its own region for a summer; limited here before it even landed. The most out-of-town of all. **Wave 2.** |

- **The two champions.** Kewl Tune (Remy, the 2026 deck) is the owner's. I propose **Snake-Eye (Junia)** as the
  other: it's the deck of the three years, it gives No Limits its founder, and the pair is a story by itself: two
  summers, the old one that won't end and the new one that just got cut. Rue's deck took the one weekend Junia's
  summer didn't, so Junia also has a rival in the room from wave 1.
- **The four Brawl out-of-towners** are the same people as their pilots (Junia, Rue, Sasha, Augie), on their legal
  2024-25 decks (snake-eye, yubel, tenpai-dragon, k9-vanquish-soul as built). They're "passing through", always ask
  about the lantern, and never stay past the bell. They introduce the cast and the rivalry before you've met Nyx.
- **Pronouns:** five she, three he, three they. Kit stays they/them; Moth and Baz he; Nyx she (her profile's look).

**Relationships (so the room argues with itself):**
- **Junia and Remy:** two summers. "Every summer thinks it's the only one." / "Every summer's right."
- **Junia and Rue:** the summer and the weekend. "One weekend, Rue." / "The one that counted."
- **Tova and Anselm:** her hybrid wins were filed under his deck. "File that one under Mitsurugi. Everything else is."
- **Tova and Margo:** one winter, there were two decks in the room, hers and Margo's.
- **Imogen and Lux:** their decks were stitched together for the biggest final of 2026, and came second. Imogen has a
  speech about it; Lux has heard it.
- **Sasha and Imogen:** two ways to be second. Sasha chose the seat; Imogen was put in it.
- **Ilara and all of them:** she's sailed against them on the circuits ("everything I play, they played a year
  before me"), and disagrees with them about lists, kindly.

### Voices

Each pilot: voice pillars, how they see the player, what they'd never say, and three reference lines.

#### Junia Calder (`junia`), "Spoils": the end chair, wave 1
- **Wound / desire / need:** the list took her deck's heart at the end of the best year anyone had. She wants it to
  stay summer; she needs someone to beat her fair, so it can end.
- **Vocabulary:** summer, July, heat, warm, "still"; a host's hospitality ("sit down", "welcome").
- **Rhythm:** unhurried, warm, short declaratives.
- **Avoids:** "banned" (it's "the week they took it"), "over", "ended", the past tense about 2024.
- **Tic:** "Still summer." Sunglasses at night.
- **Subtext:** medium. The hospitality is a refusal.
- **Sees you as:** the guest who might finally end her summer. She hopes so and dreads it.
- **Would never say:** "That was a great year." / "I suppose the list was right." / "I'm over it."
- **Reference:** "It's still July at my table." / "A list can't beat anybody. It just takes things." / "Two years in
  that chair. Sit in it a minute. It's warm."

#### Remy Okoro (`remy`), "Rotary": the champion, wave 2
- **Wound / desire / need:** the deck was the summer of 2026; three weeks after its biggest night, Rotary was pulled.
  He wants the end chair; he needs to learn he isn't the only summer.
- **Vocabulary:** a DJ's: set, track, drop, cue, the crowd, "on the one". He calls a turn a track.
- **Rhythm:** quick, counted, clipped; talks over your turn.
- **Avoids:** the past, waiting, the word "banned" ("they pulled the record").
- **Tic:** counts in ("One, two.").
- **Subtext:** low. Cheerful, cocky, never cruel.
- **Sees you as:** the opening act between him and Junia's chair.
- **Would never say:** "Wait your turn." / "Back in my day." / "Slow down."
- **Reference:** "Your turn, my turn. Same song." / "Didn't come for you, Local. Came for the chair." / "Taking that
  one off the set."
- **Not the world champion.** The deck won the biggest table of 2026; Remy wasn't sitting at it ("I've played it at
  every other table since"). He's champion here by his No Limits record. Owner question 3.

#### Anselm Quist (`anselm`), "Habakiri"
- **Wound:** nobody touched his deck in its year; the list came a year late, twice. He wants that untouched year to
  count; he needs to lose once and survive it.
- **Vocabulary:** formal, ceremonial ("kindly", "in my year", "correspondence"). Tributes are payments.
- **Rhythm:** measured, complete sentences, faint condescension.
- **Avoids:** "I lost" (it's "outplayed, briefly"), slang, haste.
- **Tic:** "In my year..."
- **Subtext:** high. Wounded vanity under serenity.
- **Sees you as:** a promising student who might break the one claim he has left.
- **Would never say:** "Fair enough." / "My bad." / "They got me."
- **Reference:** "In my year, nobody touched me." / "They wrote to me late. I don't read late correspondence." /
  "Outplayed. Briefly. I'll note the date."

#### Tova Kincaid (`tova`), "Detonator"
- **Wound:** one winter, nearly half of every top table was her deck; she played the mirror until she hated it. Her
  wins in the hybrid were filed under Anselm's deck. She wants an opponent who isn't her.
- **Vocabulary:** demolition (charges, clear, stand back); crowd words (the room, the mirror, again).
- **Rhythm:** deadpan, bored, two to five words.
- **Avoids:** enthusiasm, her deck's name said with pride.
- **Tic:** "Again."
- **Subtext:** medium. The boredom is loneliness.
- **Sees you as:** a relief, if you're not on her deck.
- **Would never say:** "I love the mirror!" / long speeches.
- **Reference:** "One winter I played myself eleven times in a day." / "Half my wins are filed under somebody
  else." / "Not a mirror. Finally."

#### Augie Marr (`augie`), "Noroi": Brawl and No Limits
- **Wound:** the list took his cards, gave some back, took others. He's stopped unpacking. He wants a fair fight; he
  needs nothing, which is his charm.
- **Vocabulary:** a roadie's (load-in, load-out, set list, the van, the dogs/the hounds).
- **Rhythm:** gruff, friendly, run-on with "and".
- **Avoids:** bluffing, hiding a card. He plays face-up and says so.
- **Tic:** "Show you mine." Calls you "kid", as Gus does.
- **Subtext:** low.
- **Sees you as:** a fellow road dog. Respects anyone with honest hands.
- **Would never say:** "You'll never see it coming." / anything sly.
- **Reference:** "Show you mine. Then the dogs show me yours." / "Took it, gave it back, took it again. I've stopped
  unpacking." / "You won that fair. More than the list ever did."

#### Sasha Penhallow (`sasha`), "Sangen": Brawl and No Limits
- **Wound:** none they'd admit. Their deck goes second and wins in one Battle Phase; the list cut its engine at its
  peak. They want one turn; they've always had it.
- **Vocabulary:** a doorman's courtesy ("after you", "please", "mind the step", "if you wouldn't mind").
- **Rhythm:** soft and slow, then one blunt sentence.
- **Avoids:** going first, raising their voice, gloating.
- **Tic:** "After you." Stands until you sit.
- **Subtext:** high. Courtesy as a threat.
- **Sees you as:** a host. They want to see what you build before they take it apart.
- **Would never say:** "Me first!" / "Hurry up."
- **Reference:** "After you. Please. Build whatever you like." / "I've always gone second. It's the best seat in the
  house." / "One turn. Thank you for waiting."

#### Imogen Starling (`imogen`), "Albion"
- **Wound:** thirteen titles in a year and never the big one; her deck, stitched to Lux's, came second in the
  biggest final of 2026. She wants top billing; she needs to enjoy a show she doesn't headline.
- **Vocabulary:** theatre (billing, notices, curtain, places, the lead, the understudy).
- **Rhythm:** florid, then self-puncturing.
- **Avoids:** "win" about herself (it's a "notice").
- **Tic:** "Second billing."
- **Subtext:** low; she says the bitter part out loud and makes it a bit.
- **Sees you as:** the lead she's understudied all year. If she beats you, the poster changes.
- **Would never say:** "Second is fine." (That's Sasha's line, and she knows it.)
- **Reference:** "Thirteen good notices. Never the opening night." / "Tonight I'm the lead. Somebody tell the
  lights." / "It's a good speech. I've had practice."

#### Rue Marchbanks (`rue`), "Lacrima": Brawl and No Limits
- **Wound:** the list took Lacrima the week their deck won the biggest weekend of 2024. They're at peace with it,
  nearly. They want nothing from you but an honest hit.
- **Vocabulary:** a night-shift nurse's (pressure, shift, triage, "where does it hurt").
- **Rhythm:** low, calm, clinical tenderness.
- **Avoids:** drama, complaining about the list, raised voices.
- **Tic:** "Where does it hurt?"
- **Subtext:** medium. Teases Junia gently, always.
- **Sees you as:** a patient. They'll take every hit you give and send it back.
- **Would never say:** "Attack me. I want you to." (That's Mara Venn's; Rue is care, not appetite.)
- **Reference:** "Go on. I've seen worse on a Tuesday." / "One weekend, Rue." "The one that counted." / "There.
  You found where it hurts."

#### Lux Ferrand (`lux`), "Dragoon"
- **Wound:** the newest. Saturday the deck won a big room; Monday the list took four of its cards. They want someone
  to blame; they need to rest, and Nyx tells them so.
- **Vocabulary:** days of the week, counting; ritual words used bitterly.
- **Rhythm:** fast, jittery, jokes with an edge.
- **Avoids:** calm. Can't not mention it.
- **Tic:** counts days without saying the number ("Not that I'm counting. I'm counting.").
- **Subtext:** low. Raw.
- **Sees you as:** the first person they can take it out on, and they know that isn't fair.
- **Would never say:** "It's fine, it was time." / "I've moved on."
- **Reference:** "Saturday it won. Monday they took four cards. Sit down." / "Let's see if you're fair. The list
  wasn't." / "Saturday, Monday, you. It's been a week."
- Avoid any line that dates the wound ("sixteen days"): it goes stale.

#### Margo Kessel (`margo`), "Dormouse": wave 2
- **Wound:** her deck was patched four times in a year and kept working, until the fourth. She wants a ticket she
  can close.
- **Vocabulary:** IT support (ticket, patch, known issue, reboot, escalate, on call). Banish-and-return is "turn it
  off and on again".
- **Rhythm:** flat, weary, procedural politeness.
- **Avoids:** "rabbit hole" (everybody says it to her; she hates it), excitement.
- **Tic:** "Have you tried that? Try that."
- **Subtext:** low; dry.
- **Sees you as:** a bug she can't reproduce.
- **Would never say:** "Down the rabbit hole!" / "Curiouser and curiouser." (White Rabbit Maliss's world, not hers.)
- **Reference:** "Banish it, bring it back. Have you tried that? Try that." / "Patched four times. Still running." /
  "Can't reproduce your fix. Ticket closed."

#### Inka Moll (`inka`), "Bookmark": wave 2
- **Wound:** her deck was the summer's where she's from; here, the list limited it before it landed. She took that
  as an invitation.
- **Vocabulary:** a cartoonist's (panel, ink, page, caption, sketch, "draw" in both senses).
- **Rhythm:** bright, quick, sketches while she talks.
- **Avoids:** seriousness, explaining the joke, naming where she's from.
- **Tic:** "I drew you already."
- **Subtext:** low; playful.
- **Sees you as:** a character she's drawing. She's decided your ending and hopes you'll ruin it.
- **Would never say:** "This is no laughing matter."
- **Reference:** "Hold still. I'm drawing you." / "Here they limited it before it landed." / "Not how I drew it. I
  like yours better."
- **Dates:** no line names when the deck's cards reach the TCG; that date will pass.

### The alley's people behind the wall

| Who | Role at No Limits |
|---|---|
| Nyx (`nyx`, she) | Keeps the door and the lantern. Speaks to the grief (Lux), says "Now it can rest" at the first restored card, enters the Invitational with her Gravekeeper's ("old words, on a new table"). |
| Baz (`baz`, he) | Follows you in with the bell; MCs the table and the Invitational; reads the UNBANNED row on the versus (`baz:restored`). Chalks the out-of-towners by handle; keeps you as `{nick}`. |
| Kit (`kit`, they) | Won't go through: "Nobody behind there is carrying spares." Holds the door with one foot and asks for the story. Watches the Invitational final from a crate in the doorway, on the alley side. Just. |
| Moth (`moth`, he) | New shelf stock "off the depot"; won't buy a restored card ("That one's got somewhere to be"). Never "stolen", "fence" or "home". |
| Ilara Gale (`radiant-typhoon`, she) | The local who knows them from the circuits. Her view of lists is the counterweight ("Lists are weather"). Invitational entrant on her own deck, legal. |
| Patron Orvel (`power-patron`, he) | Invitational entrant: "The House doesn't usually travel." |
| Nocturne (`nocturne`) | Invitational entrant. Points at the mystery of the door and stops. |
| Penny (`penny`, she) | Knew the depot when it was a depot. Tier 2 only. |
| Mika (`mika`, she) | Streams the Invitational from the doorway ("Signal's one bar"). |
| Gus, Hollis, Ines, Ilse | Never come down. Gus smells the diesel; Hollis files the peak months; Ines and Ilse aren't mentioned. |
| Vera | Upstairs only: one footer ("THREE of anything? ...I'm not anteing. I'm asking."). |

## 4. Scenes

A new block in campaign/story.txt, after the Back Alley block (after `alley-closing`) and before the level scenes.
Add the new triggers to the header comment.

```
# ---------------------------------------------------------------------------------------------------------------------
# No Limits (post-game, docs/dev/NO-LIMITS-STORY-2026-11-02.md): the old bus depot behind Nyx's wall. The wall opens on the
# first alley visit after alley-closing has been seen (nolimits), never the same night, so Nyx's clear stays the
# alley's last word. The out-of-towners are met first at the Alley Brawl on legal decks; no scene assumes you met them.
# Every pilot is fictional: lines name what a deck did, never who played it. Kit doesn't go through the door. Gus,
# Hollis, Ines and Ilse never come down. Nobody behind the wall says "champion": seat 4 is "the end chair".
# ---------------------------------------------------------------------------------------------------------------------

# The out-of-towners at the Alley Brawl: the first field that shows one, then each before your first match against them.
scene: nl-outoftowners
when: outoftowner
-: Four of tonight's chalk names are in a smaller hand, squeezed in at the edge of the board.
baz: Out-of-towners! In for one night, they say. They always say that.
kit: They come through every few months. Never the same town twice. They always ask about the lantern.
-: Each of them wears a toploader on a cord around the neck. Nobody opens theirs.
junia: Street rules tonight. One of everything. The rest stays in the toploader.
rue: She means the rest stays warm. She thinks it's still summer.
junia: It is still summer.

scene: nl-oot-junia
when: outoftowner:junia
baz: Spoils! Started a game somewhere with no list at all. Nobody's seen it. Everybody's heard of it.
junia: Evening, Local. One copy tonight. Where I usually play, it's three. Remember I said that.

scene: nl-oot-rue
when: outoftowner:rue
baz: Lacrima! Carries a card around their neck and won't play it on my street! I respect a mystery!
rue: Where does it hurt? Nowhere yet. Let's start.

scene: nl-oot-sasha
when: outoftowner:sasha
baz: Sangen! Holds the door for everybody, goes second every time, and ends it in one swing!
sasha: After you. Please. Build whatever you like.

scene: nl-oot-augie
when: outoftowner:augie
baz: Noroi! Brought the dogs! ...He brought the lead. There are no dogs. I checked.
augie: Show you mine, kid. One hound short tonight. They kept the other one.

# No Limits: the wall opens (nolimits). Fires on the next Back Alley visit after alley-closing; catches up older saves.
scene: nl-wall-opens
when: nolimits
-: Friday again. At the far end of the alley, the lantern is turned all the way up.
-: In its light the wall has a door in it: steel, painted brick-red, a padlock hanging open on the hasp.
nyx: You read the old words properly. There are newer ones behind this door.
nyx: The old ones rest. These were put down too young. They don't rest yet.
nyx: Every few months, people bring the cards the list took last year. They play them here.
nyx: Until they can let them go. Some never can.
radiant-typhoon: I've sailed against them on the circuits. Whatever I play, they played a year ago.
radiant-typhoon: Lists are weather. You trim your sails. They'd rather sail straight into it.
penny: That's the old bus depot. I drove the number nine out of there for thirty years.
penny: Lovely echo. The card won't like it.

scene: nl-wall-kit
when: nolimits
-: Kit stops a step short of the door and puts one foot against it.
kit: This is where I get off. Nobody behind there is carrying spares.
kit: Three of anything, no list, and every card on the table is somebody's whole deck.
kit: I'll hold the door. Tell me everything after. ...Leave out the parts I'd hate.

scene: nl-first-visit
when: open:No Limits
-: The depot is one long shed. The inspection pits are boarded over. One table runs down the middle.
-: Three chairs down the sides. At the far end, one chair with its back to the wall.
junia: Welcome to No Limits, Local. No list. Three of anything, and that goes for your deck too.
junia: One rule, and it isn't a limit. Everybody gets to play. No card that stops the other one.
junia: We put up the cards the list took. You put up points. Beat us, and the card goes with you.
junia: A list can't beat anybody. It just takes things. A duel can beat you. That's the difference.
-: A brass bell rings behind you. Baz has followed you in, chalk and all.
baz: A bell's a bell! It rings anywhere! Somebody find me a wall for my board!
junia: The end chair's mine. Has been since the week they took Spoils. Come and get it.

# Before your first No Limits duel against each pilot (pilot:<id>). Baz reads the act; the pilot answers.
scene: nl-junia
when: pilot:junia
baz: The end chair! Spoils! Three of the card the list took, and two years of not letting go!
junia: It's still July at my table. Sit down. I'll show you why nobody's ended it.

scene: nl-remy
when: pilot:remy
-: Someone at the long table has headphones on, one cup taped, nodding to a beat nobody else can hear.
baz: Rotary! New in town! This year's deck, and it plays on your turn as well as its own!
remy: Didn't come for you, Local. Came for the chair. You're between me and it, so. One, two.
junia: Every summer thinks it's the only one.
remy: Every summer's right.

scene: nl-anselm
when: pilot:anselm
baz: Habakiri! Top of the room for a whole year, and the list didn't lay a finger on it till after!
anselm: In my year, nobody touched me. They waited until it was over, and then they wrote. Kindly sit.

scene: nl-tova
when: pilot:tova
baz: Detonator! One winter, half the top tables were her deck! The other half wished they were!
tova: Tell me you're not on my deck. ...Don't. I'll find out.

scene: nl-augie
when: pilot:augie
baz: Noroi! Cards taken, cards given back, cards taken again! The man's stopped unpacking!
augie: Both hounds tonight, kid. Show you mine. Then they'll show me yours.

scene: nl-sasha
when: pilot:sasha
baz: Sangen! Lets you build the whole board, then takes it apart in one Battle Phase! Hold one back!
sasha: After you. I'd like to see what you build. Then I'd like to see it come down.

scene: nl-imogen
when: pilot:imogen
baz: Albion! Good notices all year, never the opening night! Tonight she wants top billing!
imogen: Second billing, all year. Tonight I'm the lead. Somebody tell the lights.

scene: nl-rue
when: pilot:rue
baz: Lacrima! The list took it the week their deck won the biggest weekend of the year! Won anyway!
rue: Go on, then. I've seen worse on a Tuesday. Where would you like to start?

scene: nl-lux
when: pilot:lux
baz: Dragoon! Won a big room on a Saturday, lost four cards on the Monday! Freshest chalk on the board!
lux: Saturday it won. Monday they took four cards out of it. Sit down. I've had a week.

scene: nl-margo
when: pilot:margo
baz: Dormouse! Patched four times in one year, and it kept working! Bring something for the banished!
margo: Ticket's open. Describe the problem. Is the problem me? It's usually me.

scene: nl-inka
when: pilot:inka
baz: Bookmark! From further out of town than anybody! Her deck got limited here before it landed!
inka: Hold still. I'm drawing you. ...Done. You came out funny. Let's see if you play funny.

# Your first win against each pilot (pilotbeat:<id>). Written to fit whatever card changed hands.
scene: nl-junia-beat
when: pilotbeat:junia
junia: ...Huh. It's cooler in here than I remembered.
junia: Two years in that chair. Sit in it a minute, Local. It's warm.
rue: Told you. Summers end. Usually on a weekend.
baz: SPOILS IS DOWN! Somebody open a window! There are no windows! Ring it!

scene: nl-remy-beat
when: pilotbeat:remy
remy: You played on my turn. Nobody plays on my turn. ...Okay. Taking that one off the set.
junia: Welcome to the summer after, Rotary. It's not so bad. The drinks are colder.
baz: The record skipped! Chalk it!

scene: nl-anselm-beat
when: pilotbeat:anselm
anselm: Outplayed. Briefly. I'll note the date. It's the first one I've had to.
tova: File that one under Mitsurugi, Anselm. Everything else is.

scene: nl-tova-beat
when: pilotbeat:tova
tova: Not a mirror. Not one mirror all duel.
tova: I'd forgotten what losing to somebody else feels like. ...Better. It's better.
baz: The Detonator's defused! Everybody can uncover their ears!

scene: nl-augie-beat
when: pilotbeat:augie
augie: Good hands. Honest hands. The dogs liked you, and they don't like anybody.
augie: You won that fair, kid. That's more than the list ever did.

scene: nl-sasha-beat
when: pilotbeat:sasha
sasha: You held something back. Nobody holds something back.
sasha: After you, then. I mean it differently this time.
baz: Sangen's seated! Somebody hold a door for them for once!

scene: nl-imogen-beat
when: pilotbeat:imogen
imogen: Beaten by the lead. Of course. Still, a better notice than second. It's a matter of billing.
lux: Don't. She gives that speech after every final.
imogen: It's a good speech. I've had practice.

scene: nl-rue-beat
when: pilotbeat:rue
rue: There. That's where it hurts. You found it.
rue: Don't apologise. I'd have been disappointed if you hadn't.
junia: One weekend, Rue.
rue: The one that counted. ...Tonight it counted for somebody else.

scene: nl-lux-beat
when: pilotbeat:lux
lux: Saturday I won. Monday, the list. Tonight, you. ...It's been a long week.
nyx: Rest a little. It doesn't mean stopping.

scene: nl-margo-beat
when: pilotbeat:margo
margo: Can't reproduce your fix. That's the worst kind. ...Ticket closed. Resolved by user.
tova: One winter there were two decks in every room, hers and mine. Neither of us lost to a third.
margo: We do now.

scene: nl-inka-beat
when: pilotbeat:inka
inka: That's not how I drew it. ...I like your ending better. I'll redraw.
-: She tears a page out of her sketchbook and hands it over: you, mid-duel, scowling at your hand.
baz: Bookmark's lost the plot! She's handing it out! Chalk it!

# The first restored card you win, from any pilot (restored). {card} is the card the list took; "opponent" is the pilot.
scene: nl-restored
when: restored
-: Your opponent holds {card} a moment longer than they need to. Then they slide it down the long table.
opponent: The list took this. It never beat me with it. You just did.
opponent: That's different. I'd forgotten how different.
nyx: Lost fair. Now it can rest.
-: In the doorway, Kit lets the door swing a little wider.
kit: First one of theirs I've seen leave that table. ...Look after it.
-: Next morning, there's a chalk-smudged list on Hollis's desk, and a pencil mark against {card}.
hollis: This one has two dates. The month it was the deck to beat, and the month they took it.
hollis: I've filed both. Purely for scholarship. ...I can see why they couldn't let it go.

scene: nl-clear
when: alleyclear:No Limits
-: The end chair is empty. Nobody at the long table says anything for a while.
baz: NO LIMITS IS CLEARED! Four chairs! No list! I'm ringing this till the roof comes down!
junia: We've played this game in a dozen towns. Nobody local ever cleared the table.
junia: So we'll do it properly. Sixteen seats. Best of three in the final. Both sides of the wall.
baz: An Invitational! On MY board! I'm going to need more board!
nyx: I'll bring the old words. Somebody should read them to this lot.
nocturne: That door is older than their game. Ask Nyx who kept the key before her.
nyx: ...
nocturne: She won't say. I've asked.

# The No Limits Invitational: first entry and first title.
scene: nl-invitational
when: invitational
-: Baz has propped three chalkboards against the depot wall. Sixteen names: half chalked, half signed.
baz: The No Limits Invitational! Out-of-towners against the alley! And {nick}, who's a bit of both!
power-patron: The House doesn't usually travel. Fifty yards is a long way for a ledger.
nocturne: I've played every Midnight since the first one. This one starts after it.
nyx: Old words, at a new table. Mind them.
radiant-typhoon: Same weather. Bigger sea.
-: In the doorway, Kit has turned a crate over to sit on. On the alley side of the door. Just.
mika: Streaming from the doorway. The signal's bad. The decks aren't.

scene: nl-invitational-win
when: bracketwin:No Limits Invitational
-: Baz climbs onto the long table, which nobody has ever let him do, and rings the bell twice.
baz: {nick} takes the Invitational! No list, no limits, and nobody left to beat!
junia: A dozen towns, and a Local takes the room. I'll be telling that one at the next door.
junia: Local's the wrong word now. The road's long, and it has other doors. You'll hear from us.
nyx: The lantern stays up for you. Whenever you like.
-: In the doorway, Kit is on their feet. Kit has been on their feet for the whole final.
kit: I didn't come in. I want that noted. ...That was the best thing I've ever not been in.

scene: nl-invitational-home
when: bracketwin:No Limits Invitational
-: Upstairs, Gus is pricing sleeves. There's depot dust on your coat, and he notices.
old-gus: Bus depot. Smells like diesel and arguments.
old-gus: Penny used to drive the nine out of there. Don't tell Penny I remember that.
-: He goes back to pricing. Then he slides one sleeve across the counter, with no price on it.
old-gus: For whatever you won down there that somebody couldn't let go of. Look after it.
```

**Running order** (one player's likely path):
1. Early, at the Alley Brawl: `nl-outoftowners`, then `nl-oot-junia` / `-rue` / `-sasha` / `-augie` as you meet
   them (optional: the Brawl is never required).
2. After Nyx's Table and `alley-closing`, the next visit: `nl-wall-opens`, `nl-wall-kit`.
3. `nl-first-visit`, then the pilots you draw: `nl-<id>` before, `nl-<id>-beat` after a first win, and
   `nl-restored` once (with Hollis's coda).
4. `nl-clear` (the first clear, with seat 4's `-beat` scene on the same night).
5. Wave 2: `nl-remy`, `nl-margo`, `nl-inka`, `nl-lux` whenever they first sit.
6. `nl-invitational`, then `nl-invitational-win` and `nl-invitational-home`.

**Order notes.**
- `nl-clear` and seat 4's `-beat` co-fire on the first clear. They don't repeat each other: the beat is personal,
  the clear is about the room. The beat should play first (the spec's call; same rule as gift-before-clear).
- `nl-restored` can co-fire with a `-beat`. The beat is the pilot's reaction to losing; the restored scene is about
  the card. Beat first, then restored.
- `nl-remy` is written so it reads right whether Remy takes the end chair or a side seat.
- `nl-wall-opens` must play before `nl-first-visit` (it's the only way to reach the panel).

## 5. Line pools

### campaign/alley-voice.txt: additions

New **[new]** hooks:
- `pilot:intro:<id>`, `pilot:win:<id>`, `pilot:lose:<id>`: the No Limits versus and results (the `regular:` pattern,
  per pilot);
- `pilot:restored:<id>`: the results screen when you win that pilot's restored card (every time);
- `pilot:encore:<id>`: the champions' encore;
- `outoftowner:intro|win|lose:<id>`: the Alley Brawl versus and results for the four;
- `baz:restored`, `baz:restored:<id>`: Baz over the versus screen's UNBANNED row;
- `baz:nolimits`, `baz:stake:nolimits`, `mc:brawl:outoftowner`;
- `mc:invitational:<round>`, `stream:invitational:<round>` (kind `invitational`; the spec names the kind).

```
# ---- No Limits (docs/dev/NO-LIMITS-STORY-2026-11-02.md): the out-of-towners behind Nyx's wall ----
# [new] pilot:intro|win|lose:<id> (No Limits), pilot:restored:<id> (their own banned card, won), pilot:encore:<id>,
# outoftowner:intro|win|lose:<id> (the Alley Brawl, legal decks), baz:restored[:<id>] (the versus's UNBANNED row),
# baz:nolimits, baz:stake:nolimits, mc:brawl:outoftowner, mc:invitational:<round>, stream:invitational:<round>.
# {card}: the restored card (baz:restored, pilot:restored) or the card won (pilot:lose). "Local" is literal: the
# out-of-towners' word for you. Gated variants use after:/before: on the No Limits scenes.

# ---- Baz behind the wall ----

pool: baz:nolimits
baz: No Limits! No list, three of anything, and one rule: everybody gets to play!
baz: Three chairs and the end chair! Mind the pits. They're boarded. Mostly.
baz: Voices up! It's a depot! It echoes! I LOVE it!

pool: baz:stake:nolimits
baz: Your points against {card}! Not a spare, not a slab. The card itself!
baz: {handle} puts up the card the list took. You put up points. Fairer than the list!

pool: baz:restored
baz: UNBANNED! {handle}'s got {card}, and not one copy! Three!
baz: Read that row twice, {nick}. That's what the list took. It's back for the night.
baz: {card}! Back from the list! Somebody check its pulse!

pool: baz:restored:junia
baz: Spoils at three! That's what a whole summer looked like!
pool: baz:restored:remy
baz: Rotary's back in the deck! It'll be playing on your turn! Everything is!
pool: baz:restored:anselm
baz: Habakiri! The blade they took a year too late! It's on time tonight!
pool: baz:restored:tova
baz: Detonators, plural! Half the room had one, one winter! Now it's all hers!
pool: baz:restored:augie
baz: Noroi's off the lead! It sniffs your hand! Hold it close!
pool: baz:restored:sasha
baz: Sangen at full strength! Build it tall, {nick}! It's coming down in one swing!
pool: baz:restored:imogen
baz: Albion AND Expulsion! The lead's costume and the trapdoor!
pool: baz:restored:rue
baz: Lacrima's back! Every hit you land comes back with interest!
pool: baz:restored:lux
baz: Four cards back that the Monday took! Still warm! VERY warm!
pool: baz:restored:margo
baz: Every patch, undone! Bring something for the banished, and something for when they come back!
pool: baz:restored:inka
baz: Bookmark at three! Every page she needs, and she knows which ones!

# ---- The pilots at No Limits (versus intro; they win; they lose: {card} is yours) ----

pool: pilot:intro:junia
junia: It's still July at my table. Sit down.
junia: A list never beat me. You're welcome to try.
junia: Three of it tonight, Local. Not one. Three.

pool: pilot:intro:junia
after: nl-oot-junia
junia: Told you, Local. Three, where I usually play. Here we are.

pool: pilot:intro:junia
after: nl-junia-beat
junia: You've sat in my chair. Don't get comfortable. It's still summer.

pool: pilot:intro:junia
# Only if Rotary takes the end chair once ready (owner question 2).
after: nl-remy
junia: Rotary's got my chair tonight. I've got the rest of the summer.

pool: pilot:win:junia
junia: Still summer.
junia: Come back when it's cooler. It won't be.

pool: pilot:lose:junia
junia: {card}. Look after it. It's had a long summer.
junia: Hm. That's the first breeze in two years.

pool: pilot:restored:junia
junia: {card}. The list took it in a week. You took it in a duel. I prefer yours.

pool: pilot:encore:junia
junia: Again? Good. Summer's long.

pool: pilot:intro:remy
remy: Your turn, my turn. Same song.
remy: One, two. Drop.
remy: Didn't come for you. Came for the chair.

pool: pilot:intro:remy
after: nl-remy-beat
remy: Rematch. I put you on the set list. Top billing. Don't tell Albion.

pool: pilot:win:remy
remy: Set's over. Tip the DJ.
remy: You were dancing on my turn. That one's on you.

pool: pilot:lose:remy
remy: Record skipped. Keep {card}. It's a good track.
remy: Okay. Okay. Taking that one off the set.

pool: pilot:restored:remy
remy: {card}. They pulled the record three weeks after its biggest night. Spin it for me.

pool: pilot:encore:remy
remy: Encore? Crowd asked. I play what the crowd asks.

pool: pilot:intro:anselm
anselm: In my year, nobody touched me.
anselm: Every Tribute pays me. Kindly keep giving.
anselm: They wrote to me late. I don't read late correspondence.

pool: pilot:intro:anselm
after: nl-anselm-beat
anselm: You have the date. I have the rest of the calendar. Sit.

pool: pilot:win:anselm
anselm: As it was in my year.
anselm: Untouched. Again.

pool: pilot:lose:anselm
anselm: Outplayed. Briefly. {card} is yours.
anselm: I'll note the date.

pool: pilot:restored:anselm
anselm: {card}. They took it a year after it mattered. You took it while it did. Thank you.

pool: pilot:intro:tova
tova: Tell me you're not on my deck. ...Don't. I'll find out.
tova: One winter I played myself eleven times in a day.
tova: Half my wins are filed under somebody else.

pool: pilot:win:tova
tova: Again.
tova: Stand back. ...Done.

pool: pilot:lose:tova
tova: Not a mirror. Finally. Take {card}.
tova: Different. Better.

pool: pilot:restored:tova
tova: {card}. Half the room had one, one winter. Now you've got mine, and the room's empty.

pool: pilot:intro:augie
augie: Show you mine. Then the dogs show me yours.
augie: Took it, gave it back, took it again. I've stopped unpacking.
augie: Both hounds tonight, kid.

pool: pilot:win:augie
augie: Good boys.
augie: Load-out, kid. Back of the van.

pool: pilot:lose:augie
augie: Honest hands win. {card}'s yours.
augie: You won that fair. More than the list did.

pool: pilot:restored:augie
augie: {card}. The lead's yours now. It comes when it likes.

pool: pilot:intro:sasha
sasha: After you. Please.
sasha: Build whatever you like. I'll wait.
sasha: I've always gone second. It's the best seat in the house.

pool: pilot:win:sasha
sasha: One turn. Thank you for waiting.
sasha: Mind the step on your way out.

pool: pilot:lose:sasha
sasha: After you, then. {card} too.
sasha: You held something back. Nobody does that.

pool: pilot:restored:sasha
sasha: {card}. I'd hold the door for it, but it's going with you. After you both.

pool: pilot:intro:imogen
imogen: Second billing all year. Tonight I'm the lead.
imogen: Thirteen good notices. Never the opening night.
imogen: Places, please.

pool: pilot:win:imogen
imogen: Curtain. And they say I can't carry a show.
imogen: Top billing. Somebody photograph the poster.

pool: pilot:lose:imogen
imogen: Second again. {card}'s yours. Mind the curtain.
imogen: Understudy to the understudy. A new low. Bravo.

pool: pilot:restored:imogen
imogen: {card}. The lead's costume. It never fit me anyway. ...It fit beautifully.

pool: pilot:intro:rue
rue: Where does it hurt?
rue: Go on. I've seen worse on a Tuesday.
rue: Hit me. I'll send it back with notes.

pool: pilot:win:rue
rue: Keep pressure on it. You'll be fine.
rue: Every hit you gave me, I gave back. That's all it was.

pool: pilot:lose:rue
rue: There. You found where it hurts. {card} is yours.
rue: Don't apologise. I'd be disappointed.

pool: pilot:restored:rue
rue: {card}. The deck won its biggest weekend without it. It'll manage without me too.

pool: pilot:intro:lux
lux: Saturday it won. Monday they took four cards. Sit down.
lux: Not that I'm counting days. I'm counting days.
lux: Let's see if you're fair. The list wasn't.

pool: pilot:intro:lux
after: nl-lux-beat
lux: Better week. Not good. Better. Sit down.

pool: pilot:win:lux
lux: That's for Monday.
lux: Better. A little better.

pool: pilot:lose:lux
lux: Saturday, Monday, you. {card}'s yours. It's been a week.
lux: Fine. Fine. ...Fine.

pool: pilot:restored:lux
lux: {card}. I wasn't ready to put it down. Turns out nobody asks if you're ready.

pool: pilot:intro:margo
margo: Ticket's open. Describe the problem.
margo: Banish it, bring it back. Have you tried that? Try that.
margo: Patched four times. Still running.

pool: pilot:win:margo
margo: Resolved. Closing ticket.
margo: Known issue. The known issue is you.

pool: pilot:lose:margo
margo: Can't reproduce your fix. {card}'s yours.
margo: Escalating. To bed.

pool: pilot:restored:margo
margo: {card}. One of the first things they patched. Keep it running for me.

pool: pilot:intro:inka
inka: Hold still. I'm drawing you.
inka: I drew you already. You came out funny.
inka: Where I'm from, this was the summer's deck. Here they limited it before it landed.

pool: pilot:win:inka
inka: Last panel. The end.
inka: Ink's dry. You lost.

pool: pilot:lose:inka
inka: Not how I drew it. I like yours better. {card}'s yours.
inka: Redraw. Redraw!

pool: pilot:restored:inka
inka: {card}. Keep it in the margins. That's where the good bits go.

# ---- The four out-of-towners at the Alley Brawl (legal decks, one copy of everything) ----

pool: mc:brawl:outoftowner
baz: {nick} versus an out-of-towner! {handle}'s carrying, but not playing it! Street rules!

pool: outoftowner:intro:junia
junia: One copy tonight. Street rules. The rest stays in the toploader.
pool: outoftowner:intro:junia
after: nl-wall-opens
junia: Street rules, Local. You know what I carry behind the wall.
pool: outoftowner:win:junia
junia: One copy was plenty.
pool: outoftowner:lose:junia
junia: Good. Now come and try it against three.

pool: outoftowner:intro:rue
rue: Where does it hurt? Nowhere yet. Give it a minute.
pool: outoftowner:intro:rue
after: nl-wall-opens
rue: Street night. The card stays round my neck. You know where it comes off.
pool: outoftowner:win:rue
rue: Keep pressure on it.
pool: outoftowner:lose:rue
rue: Good. Behind the wall, I hit back properly.

pool: outoftowner:intro:sasha
sasha: After you. One copy each. It's only polite.
pool: outoftowner:intro:sasha
after: nl-wall-opens
sasha: After you. You know where the rest of my deck lives now.
pool: outoftowner:win:sasha
sasha: One turn. Thank you for waiting.
pool: outoftowner:lose:sasha
sasha: You held the door yourself. Well done.

pool: outoftowner:intro:augie
augie: One hound short tonight, kid. Still got the nose.
pool: outoftowner:intro:augie
after: nl-wall-opens
augie: Street rules. The other hound waits behind the wall.
pool: outoftowner:win:augie
augie: Good boy. Just the one tonight.
pool: outoftowner:lose:augie
augie: Fair's fair. Come and meet the other one.

# ---- The No Limits Invitational (kind invitational; rounds as Bracket.RoundName, lower-case) ----

pool: mc:invitational:round-of-16
baz: The No Limits Invitational! Sixteen names, three boards, and a depot full of echo!
baz: {nick} versus {handle}! The alley against out of town! Shake on it!
baz: The top seat waits for the final! So does {nick}! Somebody's not waiting long!

pool: mc:invitational:quarter-final
baz: Eight left! Half the out-of-towners are packing their bags! Not into Moth's van! A different van!

pool: mc:invitational:semi-final
baz: Four names! The depot's never been this quiet! Somebody cough!

pool: mc:invitational:final
baz: The Invitational final! Best of three! No list, no limits, nobody leaves till it's done!
baz: Top seat against {nick}! Everybody stand! We need the chairs!

pool: stream:invitational:round-of-16
mika: Invitational. Chat wants the decklists. Chat, there's no list. That's the whole point.

pool: stream:invitational:quarter-final
mika: Every deck still in got cut by a list somewhere. Chat says museum. Chat is wrong. It's a zoo.

pool: stream:invitational:semi-final
mika: Semis. I'm in a doorway with one bar. If the stream drops, assume something amazing.

pool: stream:invitational:final
mika: Best of three. Kit's on a crate in the doorway, pretending not to watch. Clip that too.
```

- The existing `mc:upset`, `mc:out`, `mc:crowned`, `mc:game:2|3` and the turn-40 and first-turn pools stay as they
  are and read right in the depot.
- The Invitational's field reuses the alley names' own pools where they have them (Ilara's, Orvel's profile lines,
  Nyx's, Nocturne's).

### campaign/van.txt: additions

```
pool: van:shelf
after: nl-wall-opens
moth: New stock off the depot. Newer than the van. Newer than me.
moth: These came out of last year's lists. Still warm. As-is.

# [new] van:sell-refused:restored: a restored card won at No Limits sells for 0, like any won card.
pool: van:sell-refused:restored
moth: Not that one. That one's got somewhere to be, and it's your binder.
moth: Somebody carried that for a year to lose it fair. I'm not buying the end of that.

pool: van:footer
after: nl-wall-opens
moth: Lantern's up most Fridays now. Business is good. Don't tell Nyx I said business.
```

### campaign/shop.txt: footers

```
pool: footer
after: nl-wall-opens
old-gus: You smell like diesel. That's new.
old-gus: Three of anything, I hear. In 2004 we had three of anything too. We just didn't have anything.
vera: THREE of anything? ...I'm not anteing. I'm asking. There's a difference.
kit: I hold the door down there. Only job behind that wall that needs no spares.
hollis: Every deck from behind that wall has a month on it. Its best month. I've started a column.

pool: footer
after: nl-restored
after: held-card-home
kit: Somebody upstairs lost a card fair once. Took twenty-one years to come back. Theirs take a night.
```

`after:` lines add up ("all must hold"), so Kit's Ines echo plays only for players who've seen both payoffs.

## 6. Profiles

Each pilot needs `personalities/<id>.profile`. The fields are the existing ones; nothing new is required.

| Field | What goes in it |
|---|---|
| header comment | Who, pronouns, handle, the deck's arc in one line, where they appear |
| `name`, `title` | From the cast table |
| `aggression`, `caution`, `patience`, `spending` | **The base deck's profile values** (snake-eye, yubel, tenpai-dragon, ...), so the playbook plays the peak build as it plays the base. Character goes in the lines, not the knobs |
| `skill` | 1.0 (Appendix A: peak builds at skill 1.0) |
| `decks` | The peak build id (the spec's; `nl-<base>` is used below as a placeholder), plus the Brawl deck for the four |
| `intro`, `win`, `lose` | Fallbacks; the pools in section 5 take over at the table |
| `look` | Portrait brief; every look has the toploader on a cord |
| `ladder` | 0 (not a circuit opponent) |
| `summon`, `strike`, `hit`, `danger` | In-duel chatter, one each (more may be added, as Kit's) |
| `combo`, `fizzle` | Champions only, if seat 4 gets the headliners' show (Appendix A, 1.5; optional) |

The handle ("Spoils") isn't a profile field today: it goes where the spec puts pilots, as `handle:` does for
headliners in alley-events.txt.

```
# Junia Calder, "Spoils" (docs/dev/NO-LIMITS-STORY-2026-11-02.md): she/her. Hosts No Limits from the end chair. Snake-Eye,
# the 2024 summer: Original Sinful Spoils and Fiendsmith's Lacrima taken. Also an Alley Brawl out-of-towner (legal).
name: Junia Calder
title: the Long Summer
aggression: 0.7
caution: 0.35
patience: 0.4
spending: 0.7
skill: 1.0
decks: nl-snake-eye, snake-eye
intro: It's still July at my table. Sit down.
win: Still summer.
lose: Hm. That's the first breeze in two years.
look: A tall woman in her thirties in a sun-bleached linen suit, sunglasses pushed into dark curls at midnight, a toploader on a cord at her throat.
ladder: 0   # not a circuit opponent: No Limits' end chair and the Alley Brawl
summon: Feel that? That's July.
strike: Hot one.
hit: Barely warm.
danger: Late summer. Still summer.
combo: Mill it, raise it, burn it. It's still July.
fizzle: Cloud over the sun. It'll pass.

# Remy Okoro, "Rotary": he/him. No Limits champion (wave 2). Kewl Tune, the 2026 deck: Rotary pulled three weeks after
# the deck's biggest night. Not that night's winner: the deck was, he wasn't at that table.
name: Remy Okoro
title: on the Decks
aggression: 0.7
caution: 0.35
patience: 0.4
spending: 0.7
skill: 1.0
decks: nl-kewl-tune
intro: Your turn, my turn. Same song.
win: Set's over. Tip the DJ.
lose: Record skipped. Good track, though.
look: A lanky man in his twenties in a satin tour jacket, headphones round his neck with one cup taped, a toploader on a cord where a lanyard should be.
ladder: 0   # not a circuit opponent: No Limits
summon: And... drop.
strike: On the one.
hit: Off-beat. Rude.
danger: Last track. Make it count.
combo: Your turn's my turn. One, two.
fizzle: Needle skipped. I'll cue it again.

# Anselm Quist, "Habakiri": he/him. Ryzeal Mitsurugi, 2025: top of the room all year, no card hit at its peak, then
# Habakiri Limited and Forbidden the next year.
name: Anselm Quist
title: the Untouched
aggression: 0.7
caution: 0.35
patience: 0.4
spending: 0.7
skill: 1.0
decks: nl-mitsurugi
intro: In my year, nobody touched me.
win: Untouched. Again.
lose: Outplayed. Briefly. I'll note the date.
look: An immaculate older man in a three-piece suit the green of a grass snake, a silver letter-opener for a tie pin, a toploader on a cord tucked into his waistcoat.
ladder: 0
summon: Kindly make room.
strike: Paid in full.
hit: How irregular.
danger: An unusual position. Not an unfamiliar one.

# Tova Kincaid, "Detonator": she/her. Ryzeal, winter 2024-25: nearly half of every top table; Detonator Limited, the
# engine cut after. Her hybrid wins were filed under Mitsurugi.
name: Tova Kincaid
title: Half the Room
aggression: 0.7
caution: 0.35
patience: 0.4
spending: 0.7
skill: 1.0
decks: nl-ryzeal
intro: Tell me you're not on my deck. Don't. I'll find out.
win: Again.
lose: Not a mirror. Finally.
look: A woman in a hi-vis vest over a black turtleneck, ear defenders round her neck, a stopwatch and a toploader on the same cord.
ladder: 0
summon: Stand back.
strike: Clear.
hit: Noted.
danger: Short fuse. Mine.

# Augie Marr, "Noroi": he/him. K9 Vanquish Soul, 2025's world deck: cards Limited, freed, Limited again; Noroi
# Forbidden. Also an Alley Brawl out-of-towner (legal).
name: Augie Marr
title: with the Hounds
aggression: 0.5
caution: 0.5
patience: 0.55
spending: 0.5
skill: 1.0
decks: nl-k9-vanquish-soul, k9-vanquish-soul
intro: Show you mine. Then the dogs show me yours.
win: Good boys.
lose: You won that fair. More than the list did.
look: A big grey-bearded roadie in a band tee older than the band, gaffer tape on his belt, an empty dog lead over one shoulder and a toploader on a cord.
ladder: 0   # not a circuit opponent: No Limits and the Alley Brawl
summon: Load-in!
strike: Sic.
hit: Ow. Fair.
danger: Last song. Make it loud.

# Sasha Penhallow, "Sangen": they/them. Tenpai Dragon, 2024: the going-second deck; Sangen Summoning, Chundra and
# Kaimen Limited at its peak. Also an Alley Brawl out-of-towner (legal).
name: Sasha Penhallow
title: After You
aggression: 0.7
caution: 0.35
patience: 0.4
spending: 0.7
skill: 1.0
decks: nl-tenpai-dragon, tenpai-dragon
intro: After you. Please.
win: One turn. Thank you for waiting.
lose: You held something back. Nobody does that.
look: A slight, neat person in a doorman's grey coat with brass buttons and white gloves, a toploader on a cord under the collar, always standing until you sit.
ladder: 0   # not a circuit opponent: No Limits and the Alley Brawl
summon: If you wouldn't mind.
strike: Mind the step.
hit: Of course.
danger: After you. One last time.

# Imogen Starling, "Albion": she/her. Branded Despia, 2026: thirteen national titles, never the big one; spliced with
# Light and Darkness Ritual, second in the year's biggest final. Albion Forbidden; Expulsion never came back.
name: Imogen Starling
title: the Understudy
aggression: 0.5
caution: 0.5
patience: 0.55
spending: 0.5
skill: 1.0
decks: nl-branded-despia
intro: Second billing all year. Tonight I'm the lead.
win: Curtain. And they say I can't carry a show.
lose: Second again. Mind the curtain.
look: A woman in her forties in a rehearsal-worn velvet coat, a programme folded in her pocket with her name in the small print, a toploader on a ribbon.
ladder: 0
summon: Places, please!
strike: Exit, pursued.
hit: Bad notice.
danger: Second act. The good one.

# Rue Marchbanks, "Lacrima": they/them. Fiendsmith Yubel, 2024: the list took Lacrima the week the deck won the
# biggest weekend of the year. Also an Alley Brawl out-of-towner (legal).
name: Rue Marchbanks
title: the Night Shift
aggression: 0.5
caution: 0.5
patience: 0.55
spending: 0.5
skill: 1.0
decks: nl-yubel, yubel
intro: Where does it hurt?
win: Keep pressure on it. You'll be fine.
lose: There. You found where it hurts.
look: A tired, kind-faced nurse in scrubs under a long black coat, a fob watch pinned upside down, a red teardrop pin, a toploader on a cord.
ladder: 0   # not a circuit opponent: No Limits and the Alley Brawl
summon: Hold still.
strike: That'll sting.
hit: Seen worse.
danger: Long shift. Nearly done.

# Lux Ferrand, "Dragoon": they/them. Light and Darkness Ritual, 2026: the deck won a big room on a Saturday; the Monday
# list took four of its cards. The freshest wound at the table.
name: Lux Ferrand
title: the Monday After
aggression: 0.7
caution: 0.35
patience: 0.4
spending: 0.7
skill: 1.0
decks: nl-light-and-darkness
intro: Saturday it won. Monday they took four cards. Sit down.
win: That's for Monday.
lose: Saturday, Monday, you. It's been a week.
look: A young person in a tuxedo with the bow tie undone and a weekend's creases, last night's eyeliner, a lanyard from that weekend and a toploader on the same cord.
ladder: 0
summon: Saturday!
strike: For Monday.
hit: Of course. Of course.
danger: Still standing. Still counting.

# Margo Kessel, "Dormouse": she/her. Maliss, 2025: patched four times in a year (Dormouse, White Rabbit, Underground,
# then White Binder Forbidden). Wave 2.
name: Margo Kessel
title: On Call
aggression: 0.7
caution: 0.35
patience: 0.4
spending: 0.7
skill: 1.0
decks: nl-maliss
intro: Ticket's open. Describe the problem.
win: Resolved. Closing ticket.
lose: Can't reproduce your fix. Ticket closed.
look: A sleepless woman in her thirties in a fleece with server-room keycards on a lanyard, a dormouse sticker on a cracked laptop, a toploader on the same lanyard.
ladder: 0
summon: Back online.
strike: Patched.
hit: Known issue.
danger: Escalating.

# Inka Moll, "Bookmark": she/her. Toon (OCG, 2026): the summer's deck where she's from; here Table of Contents and
# Bookmark were Limited before the deck landed. Wave 2; an authored seat (Appendix B, 6).
name: Inka Moll
title: the Early Edition
aggression: 0.7
caution: 0.35
patience: 0.4
spending: 0.7
skill: 1.0
decks: nl-toon
intro: Hold still. I'm drawing you.
win: Last panel. The end.
lose: Not how I drew it. I like yours better.
look: A small woman in paint-flecked dungarees with ink on every fingertip, a sketchbook full of other duelists' faces, a toploader on a cord.
ladder: 0
summon: Ta-da!
strike: Splat.
hit: Smudged it.
danger: Cliffhanger!
```

- **Knobs:** Toon has no base profile; 0.7/0.35/0.4/0.7 (the aggressive template) suits a going-second direct-attack
  deck. Kewl Tune likewise. Both are the deck designer's to confirm.
- **Portraits:** eleven new ones (Appendix A lists a portrait per pilot). Every look avoids an existing pilot's
  silhouette: Nell Ardent's fire hair, Kenta Mizorogi's mahjong tile, Mara Venn's stunt jumpsuit, Halvard's
  half-white robe, White Rabbit Maliss's Alice dress, Sumire Ishigaki's hakama.

## 7. Story and gameplay

| Story beat | Gameplay | Player feels |
|---|---|---|
| Out-of-towners at the Brawl | Four named entrants on legal decks; the toploader motif | A wider world, glimpsed early |
| The wall opens | The No Limits tier unlocks, a visit after Nyx's clear | The ending was a door |
| Junia's one rule | No list for you; `Never`/`BossOnly` still out | Symmetry, with manners |
| The UNBANNED row, Baz's read | Restored cards at 3 on the versus | Knowing exactly what's coming |
| Their card on the table | The binder leads with the restored card | The stake is their grievance |
| The first restored card won | `nl-restored`; Hollis marks the Unlisted page; Moth won't buy it | You ended something fair |
| A pilot's first loss | Their intros change after it (`after: nl-<id>-beat`) | The room remembers you |
| Rotary arrives (wave 2) | A second champion; Junia's lines shift if he takes the chair | The meta moves on, like it does |
| The table's first clear | The Invitational opens | Earned |
| The Invitational title | Junia's "other doors"; Gus's unpriced sleeve | The top of the alley, and a road out |

**Lore tiers.**
- **Surface:** the wall opens; Junia's rule and stakes; Baz's handles and reads; each pilot's one-line hook; the
  Invitational.
- **Engaged:** the Brawl meetings; the relationships (two summers, the filing, the stitched final); Moth's and Gus's
  lines; Hollis's column; Kit holding the door.
- **Deep:** the depot was Penny's (and Gus remembers); Nocturne and Nyx's key; Kit's Ines echo; the toploaders hold
  the card at the Brawl and nothing behind the wall.
- The critical path needs none of it: the table and the Invitational read on their own.

**Environmental brief: the Depot.**
- **What happened here:** a bus depot, closed for years; the out-of-towners' game has met in it whenever Nyx's
  lantern goes up.
- **What the player should infer:** a place for things at the end of their run. Retired buses then, retired decks now.
- **What stays mysterious:** who held the key before Nyx; why she keeps it.
- **Props:** the long table on trestles over a boarded pit; a route board with every number chalked out but 9; Baz's
  chalkboard against a timetable; eleven empty toploader cords hung on a coat rack by the door.
- **Light:** cold strip lights down the shed, one warm bulb over the end chair. **Sound:** a long echo; Baz's bell
  rings twice as long in here.

## 8. Continuity

**World bible additions (must not be contradicted):**
1. **Nyx's clear ends the alley's story;** the wall opens on a later visit. Nobody says the door wasn't there.
2. **Nyx keeps the door;** Junia Calder runs No Limits; Baz MCs. Nyx stakes nothing behind the wall.
3. **No Limits started the week the list took Original Sinful Spoils** (December 2024), in a car park, and has
   travelled since.
4. **The one rule:** "Everybody gets to play. No card that stops the other one." It's the in-world name
   for `Never` and `BossOnly`; any future exclusion should fit it.
5. **"The end chair",** never "champion", behind the wall. "Champ" stays Ilara's.
6. **"Local"** is the out-of-towners' word for you; Baz's `{nick}` doesn't change ("Midnight" is for good).
7. **Kit never goes through the door.** Watching from the doorway is the limit.
8. **The pilots never sign a board.** Baz chalks them by handle, like the alley's regulars.
9. **Pilots and their decks' as-built owners never share a scene** (Nell Ardent, Teodoro Brasa, Mara Venn, Kenta
   Mizorogi, Rafael Lezcano, the Castells, Sumire Ishigaki, Corin Vasse, White Rabbit Maliss, Halvard, Lucan Graye),
   matching Appendix A's "no peak build with its as-built twin".
10. **No real people.** A deck "won the biggest weekend of the year"; nobody in a line won it. No real event names in
    dialogue.

**Conflicts and notes for the other docs:**
1. **`nolimits` timing.** If the spec opens No Limits on `alleyclear:Nyx's Table` itself, the table and Nyx's "goes
   back to being a wall" contradict on screen. Recommended: the next tab show after `alley-closing` is seen.
2. **Champion order** (Appendix A, 1.3): one closes, one wildcards. Junia's gated line assumes Remy closes once ready;
   drop that one pool if the owner keeps Junia closing.
3. **The Invitational field.** Appendix A has 10 builds plus 5 alley names; with Toon that's 11 plus 4. The scenes
   name Ilara, Orvel, Nyx and Nocturne, so the dropped alley name should be the headliner seat.
4. **The Brawl four on legal decks** run their bases through the Alley list, so Junia and Sasha play one copy of
   their restored cards there. The toploader fiction ("the rest stays in the toploader") covers it; nobody claims the
   card is absent from their Brawl deck.
5. **Tova's line "Half my wins are filed under somebody else"** reflects the research's merge (Ryzeal Mitsurugi counted
   as Mitsurugi). Harmless if the classification changes; it's her grievance, not a statistic.
6. **Stake semantics.** `pilot:restored:<id>` plays every time; the economy decides how many copies a pilot holds. If
   it's a single copy each, consider gating a "resting" variant of each intro on a future `restored:<id>` trigger.
7. **Speaker ids** must not collide with existing profile ids (checked: none of the eleven exists).
8. **Line length:** every dialogue line here is under about 105 characters; profile `look:` lines are longer, as
   the shipped ones are.
9. **No card text is quoted;** effects are paraphrased loosely, card names used as the story already uses them.
10. **Narrative debt opened:** Junia's "other doors" (the Champion's Ladder, when built), Nocturne's question about the
    key (deliberately unanswered), and Remy against Junia for the chair (paid off by whichever closes).

## Owner questions (recommendation first)

**Decided (owner, 2026-11-02): every recommendation below is accepted.** The out-of-towners' timing and decks
follow NO-LIMITS-SPEC section 16 (The Black Market's clear, authored splash decks).

1. **Who runs it.** Nyx keeps the door, Junia (Snake-Eye) runs the game from the end chair, Baz MCs. *Or Ilara Gale
   opens the wall (Appendix A's suggestion); here she's the local guide and counterweight instead.*
2. **The champions.** Snake-Eye (Junia) as the second champion. Once Kewl Tune is ready, **Remy closes and Junia
   wildcards** (the new summer takes the chair). *Or Junia keeps closing as the host, Remy wildcards.*
3. **Kewl Tune's pilot isn't the world champion.** The deck won the biggest table of 2026; Remy wasn't at it. *Or make
   him the in-world winner of a fictional world final (closer to a real person; not recommended).*
4. **The wall opens a visit later,** not on Nyx's clear (continuity 1). Approve?
5. **Kit never goes through,** only watches the Invitational final from the doorway. Approve?
6. **"Local" as the out-of-towners' word for you,** with Baz's ladder unchanged. Approve?
7. **The Depot** (the old bus depot, Penny's), and Gus remembering it. Approve?
8. **The cast's names, handles and pronouns** (section 3). Approve, or flag any to change.
9. **The trigger set** (section 2): keep the 11 `pilot:<id>` entrance scenes, or fold them into versus pools only.
