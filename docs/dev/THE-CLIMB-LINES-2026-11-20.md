# The climb: circuit final lines (draft, 2026-11-20)

Drafts for the owner's approval, for `docs/dev/THE-CLIMB-SPEC-2026-11-20.md` section 5 ("Billed circuit finals").
Nothing here is in the game yet: part D pastes the approved lines into `campaign/circuits.txt` as `finalline: <text>`,
and Sage Aurelio's two into `personalities/spellbook.profile`.

**What a final line is.** The quote on the versus screen (and on the circuit row) for a circuit's last match, said by
that match's character, who knows this duel decides the circuit. Rematches show the same line, so each one is written
in a different shape from that character's `intro:`.

**Limits followed.**

- Speaker: the circuit's last match, as `circuits.txt` lists it today (with the spec's deck swaps applied).
- At most 100 characters (lengths below are counted on the exact text); no `#`; one breath, no stage directions.
- No card names. The only deck words are ones the character's own lines already use (Wonderland, the Four Rulers'
  elements); nothing depends on the deck, so Chef Praline's line holds whether she plays Synchron or Yummy.
- No line repeats or paraphrases a line in the speaker's profile or in `story.txt` (checked by hand against both).
- Original characters only; no real people; no catchphrases from the anime or the official games; all-ages.
- Nothing from a later band: no line names the Duchess, the Library, the alley or what comes after the Gauntlet.
- The 22 recommended lines avoid a shared template: no "So you made it this far", and the upper hand moves around
  (eager: Ryo, Tink, Praline, Dottie; gracious: Hollis, Linden, Lisette; bored or lordly: Ione, Barrund, Kelda;
  menacing: Nerio, Ash, Rex; nervous only in the alternatives, for owners who want more of it).

**Reading the table.** Each circuit has two rows: **rec** (recommended) and **alt** (a different tone). Lines are
written exactly as they'd go into the data file.

| # | Circuit | Speaker | | Line | Len | Where it comes from in their voice |
|---|---|---|---|---|---|---|
| 1 | Beginner Circuit | `rookie-ryo` Ryo | rec | Okay. Last match of the circuit. I already wrote WIN on the next page. In pen. | 78 | His notebook and his shouted caps ("Did you SEE that?!") |
| | | | alt | Wait, this is the final? The actual final? Okay. Okay! Deep breaths. ...Okay! | 77 | Nervous: his `danger:` line's self-talk |
| 2 | Rookie Cup | `tink` Tink | rec | I saved my best parts for the last machine of the night. Wind it up! | 68 | The workshop and parts she swaps; her wind-up robots |
| | | | alt | Final match? Ooh. Then I'm taking the safety bolts out. | 55 | Gleeful tinkering, a prototype with the guards off |
| 3 | Town Tournament | `vera` Vera | rec | I hate waiting. I've been waiting for this one since your first night. | 70 | "I never wait": the one thing she has waited for is you (she met you in `welcome`) |
| | | | alt | Finals are the best part. Nobody holds anything back. Least of all me. | 70 | Pressure as a personality; short, flat sentences |
| 4 | Regional Qualifier | `hollis` Professor Hollis | rec | One more lecture between you and the regional circuit. I'm told I run long. | 75 | The lecturer; his dry joke on his own patience |
| | | | alt | Pass this and the regionals open. Fail, and we arrange a resit. I'm flexible on dates. | 86 | Grading and the syllabus; kindly, not threatening |
| 5 | Underdog Open | `burning-abyss` Nerio Malebranche | rec | Every poem needs a last canto. Tonight, I write yours. | 54 | The poet of the Ninth Circle: verses and stanzas, so a canto to end on |
| | | | alt | The underdogs have had their say. A tragedy always saves its ending for last. | 77 | Same poet, taking the circuit's "underdogs" billing as his cue |
| 6 | Trap Masters Cup | `altergeist` Ione Wraith | rec | Final process loaded. I ran this match a thousand times. You lost most of them. | 79 | Programs that "already ran"; bored superiority ("Cute.") |
| | | | alt | Last match of the cup, and my traps are idle. Give them something to do. | 72 | Idle processes; a ghost who wants to be busy |
| 7 | Small Engines Invitational | `yummy` Chef Praline | rec | And for dessert, the chef's special: the final! I hope you saved room. | 70 | Courses, orders and table service; bubbly |
| | | | alt | Every table's been served but yours. The final plate always comes out spiciest. | 79 | "Order up, hot and spicy!" turned into the last order |
| 8 | Synchro Dawn Classic | `infernity` Ash Hollow | rec | Win this and the circuit's yours. Lose, and you've got nothing. I recommend nothing. | 84 | Nothing as strength; deadpan |
| | | | alt | One match left. I came here with nothing. I'm leaving with the circuit. | 71 | Empty pockets, flat certainty |
| 9 | Xyz and Link Regionals | `gouki` Coach Gouki | rec | Main event! The title belt's on the line, and I don't drop title fights at home. | 80 | Wrestling coach with a championship belt |
| | | | alt | Championship round! Touch gloves, protect yourself at all times, and give me everything! | 88 | Sporting ("Respect."); a referee's instructions, gracious |
| 10 | Link Age Open | `maliss` White Rabbit Maliss | rec | Last door in Wonderland, and it's ever so small. Only one of us gets through. | 77 | Wonderland and its tiny door; prim menace |
| | | | alt | Oh dear, the final already? Then let's make it curious. | 55 | The fretting White Rabbit and "Curiouser and curiouser" |
| 11 | Golden Age Championship | `dragon-ruler` Ignatia Blaze | rec | Golden Age, last match. I've won it in four elements. Pick the one you'd like to lose to. | 89 | Fire, water, wind, earth; a cocky adventurer |
| | | | alt | The last old champion stands in the fire. Walk through it, if you can. | 70 | Heat and a dare; plainer and more solemn |
| 12 | Control Kings Cup | `true-draco` King Barrund | rec | Three kings fell to reach me. I won't even stand up. | 52 | The king who never leaves his seat; the circuit's other "kings" |
| | | | alt | So you've come to my throne room with a crown in mind. Bold. I like bold, briefly. | 82 | Royal condescension with a little warmth (his gift scene) |
| 13 | Hybrid Engines Invitational | `unchained` Abomination Rex | rec | Everything I had, I broke to get here. One thing left to break. Guess what it is. | 81 | "I break things. Mostly you." |
| | | | alt | You made it to the end! Nobody makes it to the end. This is going to be great. | 78 | Gleeful brute; his sporting lose line ("Well played.") |
| 14 | Expert Series: Shadows and Storms | `danger-dark-world` Dottie Larkin | rec | The final! They say you're rare. I've never caught anything rare. Until tonight! | 80 | Cryptid hunter; the player as the sighting |
| | | | alt | Okay. Big crowd, bad lighting, last match. Perfect conditions for a sighting. | 77 | Field-notes voice, a little nervous |
| 15 | Expert Series: Rituals and Fire | `tenpai-dragon` Kenta Mizorogi | rec | Final round. In mahjong, we call it All Last. I always win All Last. | 68 | His mahjong tile ("All Last" is mahjong's last hand of the game) |
| | | | alt | It's the final, so build it big. Bigger. More for me to break. | 62 | Board-breaker swagger; doesn't promise who goes first |
| 16 | Expert Series: The New Wave | `elfnote` Linden | rec | Every series ends with a coda. Tonight's is mine to conduct. | 60 | The conductor; music terms (crescendo, fortissimo) |
| | | | alt | The audience always stays for the last piece. Shall we give them one worth staying for? | 87 | Gracious: the duel as a performance for two |
| 17 | Expert Series: Fall Format | `lunalight` Lisette Morrow | rec | My card's been full all autumn. I saved the last dance for you. | 63 | Ballroom courtesy; a dance card, with a card-game wink |
| | | | alt | Midnight, and the last song. Every masquerade ends with someone unmasked. | 73 | Her masquerade mask; teasing menace |
| 18 | Master League: Link Wars | `spright` Zia Sparks | rec | Two of us left in the whole circuit. Two! That's my number. I like my odds. | 75 | Everything's a two |
| | | | alt | Okay, this is the big one. I'm not nervous. I'm two-thirds nervous, tops. | 73 | A hyper kid, nervous, still counting |
| 19 | Master League: Graveyard Kings | `k9-vanquish-soul` Rafael Lezcano | rec | The case closes tonight. I've read your whole file. All it's missing is the ending. | 83 | Detective: files, reports, the case |
| | | | alt | I've followed your trail across this whole league. It ends at my table. | 71 | The tracker; the K9 unit's hunt |
| 20 | Master League: Houston 2026 | `branded-despia` Corin Vasse | rec | Houston's final table. I keep my oaths, and I swore no one would take it from me. | 81 | The fallen knight; oaths and pride |
| | | | alt | The final, then. Judge me fairly. I will do the same for you. | 61 | Judgment ("Mirrorjade judged you."), said as courtesy |
| 21 | Master League: October 2026 | `chaos-ritual` Sir Lucan Graye | rec | I've cut through every regional this month. You're the last name on my scroll. | 78 | The ritual scroll on his scabbard; "the regionals are ours" (`october-list`) |
| | | | alt | Salute, and then we begin. A title this close deserves both our blades. | 71 | Knightly courtesy ("Draw your blade", "Well fought") |
| 22 | Tier 0 Gauntlet | `ishizu-tearlaments` Kelda Amin | rec | I've already started tonight's page. The result is the only column left blank. | 78 | The archivist; calm certainty, not yet "inevitable" |
| | | | alt | Every record I keep ends the same way. Tonight, I'd almost like to be surprised. | 80 | The same records, with a hint of the "...Then you belong here" to come |

**Kept clear of existing lines** (the closest calls): Ryo's alt avoids his rematch's "Round two"; Vera's rec doesn't
reuse "Don't make it boring" (`welcome`) or "I signed up" (`rookie-cup-clear`); Nerio avoids "stanza" (his `strike:`);
Ignatia avoids "They always come back" (in four of her lines) and "Halfway up the map" (Vera, `golden-age-clear`);
Corin avoids "I fell once" (`hit:`) and "nothing left to lose" (`danger:`); Lucan avoids "Light and darkness"
(`intro:`); Kelda avoids "I'm the fourth" and "whether you belong here" (`gauntlet`), and no line of hers says nobody
has cleared the Gauntlet (Hollis says someone in a white coat did, `champion`).

## Sage Aurelio (`spellbook.profile`): deck-neutral `intro:` and `win:`

**What was deck-specific.** `intro: Spellbook of Judgment. Read it and weep.` names a Spellbook card, and
`win: Jowgen locked the door. You had no key.` names Jowgen the Spiritualist, a Spellbook-deck lock card. Neither is
true when he plays Spellcaster's Command. The new lines lean on what's true of him in both: a prophet, a sage with books.
They stay clear of his `lose:` ("A page I didn't foresee.") and his `partner:` ("I've read ahead. We win."), and of any
circuit, so they read right in every place his intro and win appear (Underdog Open, the Novice Tag Cup, the Library).

| Key | | Line | Len | Where it comes from |
|---|---|---|---|---|
| `intro:` | rec | I saw this duel coming nights ago. Sit. Let's see how much of it you can change. | 80 | The prophet: a foretold duel, with the player invited to defy it |
| | alt | The stars named tonight's winner. I'll tell you who, afterwards. | 64 | Star-dusted robes; a smug, gentle tease |
| `win:` | rec | It was written. I only turned the pages. | 40 | Prophecy and his floating books; works for spells or spellbooks |
| | alt | The prophecy holds. It usually does. | 36 | Dry, self-satisfied; pairs with his `lose:` admitting it sometimes doesn't |

## Owner decisions (recommendation first)

1. **The recommended line for each circuit, or its alternative.** Recommended: take every **rec** line; swap in an
   **alt** where you'd like a different mood (the alts lean gracious or nervous). Undecided.
2. **Beginner Circuit: Ryo plays matches 1 and 3.** I wrote his line for match 3 only (the billed final), as the second
   time you meet him that night; it no longer shows his `rematch:` line there. Assumed; nothing else changes.
3. **Master League: Houston 2026 has three matches.** Corin Vasse plays the last, so his line is the final's; "Houston's
   final table" fits the circuit being named for that event. Assumed.
4. **Tier 0 Gauntlet bills only its fourth match** (spec section 5): Kelda's line, nothing for the three returning
   champions. As the spec says.
5. **Small Engines Invitational: Chef Praline's line is deck-neutral** (no Yummy or Synchron words), so it survives the
   swap and the spec's revert rule either way. As the brief asks.
6. **Lisette's rec plays on "dance card".** In a card game it can read as a pun or as a mistake. Recommended: keep it
   (it's her ballroom voice); the alt has no wordplay if you'd rather not.
7. **Kenta's rec uses a real mahjong term, "All Last".** Recommended: keep it (it's the tile he wears, and it means the
   final hand); his alt needs no mahjong.
8. **New Game+ shows the same lines again.** None of them says it's your first time through, so they hold in a second
   cycle. Assumed; no NG+ variants written.
