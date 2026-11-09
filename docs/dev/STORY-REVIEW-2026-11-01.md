# Story and characters review (2026-11-01)

Four agents read the whole story (`campaign/story.txt`, 63 scenes), all 88 characters (`personalities/*.profile`,
`docs/dev/CHARACTERS.md`), `campaign/circuits.txt`, `guests.txt` and `library.txt`, read-only, each with its own lens:
- **Narrative Designer:** continuity as played, using the engine's real scene order;
- **Narratologist:** structure and arcs;
- **Psychologist:** voice against personality, play-style numbers and decks;
- **Cultural Intelligence Strategist:** representation, stereotypes, and closeness to real people or existing characters
  (it viewed about 20 of the 88 portraits).

Nothing was changed. Line numbers are story.txt's unless a file is named.

## Verdict

The writing is economical and its running jokes pay off. Gus's arc (lessons, then "Took my glasses off to do it") and
Vera's (rival, then "Don't speed up… I can't believe I just said that") are the best things in it, and the epilogue
bookends the first scene. The weaknesses:
- **Structure:** no midpoint, and a champion scene that ends on menu directions.
- **Ilse:** never planted before she arrives.
- **Repeated beats:** ten cup clears repeat the line the player just read on the results screen.
- **Partner lines:** they break when Gus or Vera is the partner.
- **Mentor voices:** Gus, Hollis, Ines, Wren and Ilse sound alike (all patience and lessons).
- **Existing characters:** three characters reproduce existing anime characters too closely.

## 1. Decisions for you

These need your call, because they mean new art or change who a character is.

**Must fix: too close to existing characters** (Cultural Intelligence Strategist):
- **Nightmare Yubel** (`yubel.profile`): the GX character's name, her mismatched eyes, half-angel half-demon wings,
  the portrait, and her "My love… was not enough." Proposal: an original duelist who *plays* Yubel (e.g. "Mara Vell,
  the Willing Wound", a stunt performer), the love line dropped, the portrait redrawn without the wings.
- **Seer Kelbek "of the Ishizu Tomb"** (`ishizu-tearlaments.profile`): a veiled, kohl-eyed seer with a golden necklace
  who reads fate ("As foretold", "The sands shift") is Ishizu Ishtar and the Millennium Necklace, and the "exotic
  Egyptian mystic" trope. Proposal: drop "Ishizu" from the title; a modern archivist who "predicts" from records, in
  contemporary clothes.
- **Odd-Eyes Ozzie** (`pendulum-magician.profile`): red and green hair and eyes, a pendulum crystal and a showman's top
  hat read as Yuya Sakaki. Proposal: another palette, no odd eyes or pendulum jewellery (e.g. a clockmaker's apprentice).

**Should fix:**
- **Card-lore names on people:** Albaz the Fallen, Sister Elis, Ash Diabell, Habakiri, Kitkallos, Shangri Ira. Keep
  the card names on decks and titles; give the people original names.
- **Real people:** Damon Teller (a famous magician's name) → e.g. Damon Vey. Dante Malebranche (a gaunt poet with a
  book of verse reads as Dante Alighieri) → another first name; the Inferno flavour can stay.
- **Who holds authority:** all five lead speakers render light-skinned, and both mentors are near-identical
  grey-bearded white men (`old-gus`, `hollis`). The darkest-skinned character found is the desert mystic. Proposal:
  redraw Hollis (e.g. as a Black or South Asian professor), keeping the tweed and the role.
- **Shangri Ira** (`kashtira.profile`): a barefoot ascetic with prayer beads and a prayer pose ("Go in peace") is the
  "serene Eastern holy person" trope. Proposal: make her stillness a skill, not a religion (a mountain-rescue climber or
  lighthouse keeper); remove the prayer pose and the beads.

**Consider:**
- **Spanish surnames:** the only two belong to a gum-chewing frog joker and a gardener. Put non-Anglo names on authority
  roles too.
- **Ratpier Rokuro:** a Japanese name, a gi and Chinese-zodiac charms blend cultures. Pick one source culture.
- **Sister Elis:** her rosary is mild Catholic imagery; a gear chain instead.

**Gameplay or mode choices:**
- **The Gauntlet's list:** it plays TCG September 2026, which brings back the cards the October scene says are gone
  (167; circuits.txt:241). Either move it to October 2026 (a gameplay change, since era play would lose those cards
  again), or keep it and add `hollis [era]: The Gauntlet plays the champions' own list, September's. One night only.`
- **New Game+ has no story:** `Seen` is kept, so a second run plays no scenes. Decide whether New Game+ should replay
  some scenes or get its own.
- **The Champion's Ladder:** Ilse's "I'm near the top" points at something not built. Make her a guest unlocked by the
  final in the meantime, or leave the hint as it is.

## 2. Text fixes ready to apply

These are text-only and low-risk. The reviewers' suggested lines are in their reports; the most useful are given here.

**Contradictions:**
- **Ishizu (174)** says "You've beaten each of us once", but you've never met her before the Gauntlet → "Three of us
  you've beaten once. I'm the fourth. The Gauntlet asks for all four, in a row."
- **Gus (169-170)** says the October opponents "play what they always have", but their decks were built for the new
  list (circuits.txt:231) → `[era]` "Check your deck against the new list, kid. Theirs were built for it."
- **Gus's partner line (61)** says "Walls, waiting, and burn", a deck you never saw him play; he plays classic and
  zombies in the circuits → "You've seen my dragons. Today I brought the walls."
- **Hollis's champion line (183)** ("Every era, every list") assumes era play → add an `[open]` variant.
- **Vera's intro** (`vera.profile`) says "No traps, no tricks", but warriors.ydk runs six traps → "I run traps. I just
  never wait for them. Hit me if you can."
- **Seraph's intro** uses game-screen words ("campaign duelists", "Advanced band") → "I only stop for duelists people
  talk about. Lately, they talk about you."
- **Timing (387, 390):** "Later, after closing" against "as of about a minute ago" → drop "Later,".

**Lines that break with some partners:**
- **162:** Vera says "Partner with me next time" even when she was the partner → cut to "Fine. FINE. You're good."
- **116:** Gus says "Under yours, please" to himself when he's the partner.
- **135:** "I've played a hundred tag duels" is wrong for Ryo or Kit → keep "That one I'll remember."
- **The `novice-tag-clear` lines** read wrong from Gus.
- **Kit** gets the generic first-partner line "I play {partnerdeck}, and I play it my way", the opposite of Kit. Add
  `partner:kit`: "Finally! Hand me a deck, any deck. I'll play it the way you would. ...Mostly."

**Order:**
- **Champion, then the Library:** they play back to back, and Gus's 162-character New Game+ pointer (185) is overridden
  at once. Cut 185 to "Hollis has a back room he's been itching to show you." and start `library-opens` with
  "-: The next morning."
- **Level scenes:** move them (196) to the end of the file. They fire with cup clears and currently play first.
- **The stamp scene:** only the Goat Cup has one (234). Give it a first-stamp trigger so any first stamp explains it.

**Repeated beats:**
- **Ten cup-clear scenes repeat the boss's `lose:` line** the results screen just showed: Six Samurai 262, Shaddoll 286,
  Performapal 299, Kozmo 298, ABC 310, Thunder Dragon 321, Eldlich 332, Tearlaments 344, Kashtira 355, Ryzeal 374.
  Rewrite each to react to what comes next, e.g. Kozmo "Next year's fleet is already in the hangar. You'll see it."
- **Three clears end with the binder being handed over** (333, 356, 367). Vary one.
- **Characters naming their own card:** Kitkallos (339) and Shangri Ira (351) → "my namesake".

**Mentor voices (Psychologist):** give each mentor a lane.
- **Gus: thrift and warmth.** strike → "Didn't cost me a single card. That's the trick, kid."; lose → "Heh. Beat the old
  man with his own walls. Don't tell Ines."
- **Hollis: lectures.** summon → "And now, the point of today's lecture."; hit → "Hm. That goes in my notes."; cut
  "Interesting." from his danger line (Nocturne's).
- **Ines: precision and the rivalry.** summon → "Thousand-Eyes. Gus hates this one."; danger → "Fine. Now I'm
  counting every card."; win → "Counted it out three turns ago. Tell Gus I said hello."
- **Ilse: judging who is worthy.** She has no reaction lines at all, and the final's opponents are bosses, not her.
  Add them for when she's a guest or on the ladder: summon "This deck belongs to no year. Neither do I."; hit "Good.
  Again."; danger "There. Now you have my attention." Win → "Not yet. But I'll remember your name."

**Other lines:**
- **Maliss:** win and strike are the same line, and so are hit and lose → win "Tea's over. You may leave the table.";
  hit "How very rude."
- **Same stat brag:** Helix and Pepe both say "Twenty-nine of thirty-two"; keep it for Clausolas only.
- **Kit:** drop "Straight from your playbook!" (false when Kit plays its own Synchrons) for "That one's for the shop!"
- **Ban Passes (75):** Hollis calls them "waivers" and later scenes say "Ban Pass" undefined → "Three waivers,
  signed: Ban Passes. Each lets one card past it."
- **Lines too long for a box** (75, 86, 16, 17, 185; 128-162 characters): split each in two.

## 3. Structure (Narratologist)

**Diagnosis:**
- **Shape:** the main campaign is a ladder whose scenes sit only on band thresholds. Its question ("do you belong?")
  is first asked one scene before the champion scene answers it, with no midpoint.
- **Second climax:** the Every Era final reads as a second climax rather than a coda. That works if each climax owns
  its own question: belonging for the circuits, history for the Library.
- **Theme:** "lose and learn" is stated but never dramatized; no scene reacts to a loss.
- **Gaps:** no scenes for circuits 2-3, 5-6, 8-9, 11-12, 14-15 and 18-19, including the hardest Master pair.

**Proposals, by priority:**
1. **Scenes for beating the mentor and the rival.** Add `clear:Rookie Cup`: "old-gus: Beat by my own walls. ...That's
   the first lesson I didn't have to teach." In `novice`, Vera: "Twice. You beat me twice. ...Next time I'm setting
   a trap."
2. **A midpoint at the Golden Age Championship clear.** It pays off Gus's Wren grudge and plants Ines.
3. **Split the champion scene.** End it on feeling: "First day, I said you learn something when you lose. You stopped
   giving me the chance." The Library and New Game+ pointer moves to a second scene on the same trigger.
4. **Plant Ilse.** On the corkboard, `advanced-tag-clear`: "Mine's under the Duchess's. Has been for years." Or in
   `champion`: "Last one to clear that Gauntlet wore a white coat." The corkboard plant should also go in `champion`,
   since Tag Cups are optional.
5. **Show Vera failing at patience before it pays off.** In `cup-goat-clear`: "Ines beat me at Goat last night. She
   never attacked. I counted. ...Show me how you waited."
6. **Give Ines and Kit a presence.** Add `partner:ines` and `partner:kit`. Ines's lost 2005 card has no payoff until
   the Back Alley ships.
7. **Fix the partner collisions** (section 2), or skip lines spoken by the partner's own id in code.

## 4. Bigger work (for docs/dev/TODO.md)

- **Reaction lines:**
  - Coverage: 51 of 88 characters have none, and 36 have one line per moment. Kit, Gus, Vera and Hollis first: the
    player hears them most, Kit in dozens of tag duels.
  - Coping: about 10 hit lines play the damage down, 6 danger lines say "the end is my best part", and about 25 of 88
    lose lines are polite concessions. Give each character one main way of coping (excuses, self-blame, revenge, jokes,
    sulking) and write all four lines from it.
- **The 2026 meta duelists:** about 75 share three templates and explain their deck, not themselves. Put a motive in
  each win or lose line. The craft characters (painter, conductor, storyteller, dancer, magician) are interchangeable.
- **Wren and Ines overlap:** both are older women who play Goat Control with a history with Gus. Separate them.
- **Dangling threads:** Nocturne's "next time I'll tell you my name"; Rhea and Seraph are never acknowledged; Kit, "the
  Shop Regular", never appears in a shop scene; Hollis is named (16) before anyone introduces him.

## What's strong (keep)

- **Arcs and callbacks:**
  - Gus's corkboard scene, and "I'm out of lessons."
  - Vera's arc: "She says that to everyone she's worried about", "for both of us", "Don't speed up…".
  - "Shop's open tomorrow" opening and closing the story; the player's winning deck starting "this year's binder".
  - Hollis's "Take your time" answered by Ilse's "They won't"; the Ryzeal twins' "optional" table returning as "Not
    this time"; Ines's "insufferable" thread.
- **Craft:**
  - The `[era]`/`[open]` variants and the silent player character.
  - Bosses who return in the Library and the final.
  - Ryo's anxious spiral, Ash Hollow's "You can't take what I don't have", Mika's and Rhea's lines.
- **Representation:**
  - No accents or dialects written into speech, and no slurs.
  - The Six Samurai written with dignity.
  - Women in the top seats: the champion, the rival, Ines and the Gauntlet boss.
  - Older characters are the strongest, and age is never the joke.
  - Every character is stated as fictional (story.txt:8).

## Applied (2026-11-01)

Applied the same day, with the owner's go-ahead. Three agents worked on separate files, and the harness checks every
trigger and speaker.
- **Narrative Designer (story.txt):** section 2 and the seven structure proposals, now 69 scenes. New scenes:
  `rookie-cup-clear`, `golden-age-clear` (the midpoint), `champion-after` (the champion scene split), `first-stamp` (a
  new `stamp` trigger for the first stamp at any cup), `partner-kit` and `partner-ines`.
- **Psychologist (main cast profiles):** the mentors' lanes; Vera, Gus, Hollis and Kit at two or more reaction lines
  per moment; Ilse's reaction lines; the Maliss, Helix, Pepe and Seraph lines.
- **Cultural Intelligence Strategist (profiles and CHARACTERS.md):** the recasts and renames below. "Mara Vell" became
  "Mara Venn" (Mar-Vell). Two more franchise echoes were caught and renamed: Selune (Lisette Morrow) and Archmage Noctis
  (Archmage Halvard).

Not applied:
- the Gauntlet's list (it stays September 2026, with Hollis's line);
- a story for New Game+;
- section 4's bigger work (in docs/dev/TODO.md);
- new portraits (docs/dev/TODO.md, "Art needed from you").

| Id | Was | Now |
|---|---|---|
| yubel | Nightmare Yubel, of the Phantom Pain | Mara Venn, the Willing Wound (recast) |
| ishizu-tearlaments | Seer Kelbek, of the Ishizu Tomb | Kelda Amin, the Archivist (recast) |
| pendulum-magician | Odd-Eyes Ozzie, of the Pendulum Scales | Ozzie Quintero, the Clockmaker's Apprentice (recast) |
| kashtira | Shangri Ira, of the Kashtira Mountains | Pilar Sendra, of the Kashtira Ridge (recast) |
| exosister | Sister Elis | Sister Annick (no rosary) |
| teledad | Damon Teller | Damon Vey |
| burning-abyss | Dante Malebranche | Nerio Malebranche |
| zoodiac | Ratpier Rokuro | Jiro Kanemaki (eto charms) |
| branded-despia | Albaz the Fallen | Corin Vasse |
| snake-eye | Ash Diabell | Nell Ardent |
| mitsurugi | Habakiri | Sumire Ishigaki |
| tearlaments | Kitkallos | Coralie Fenn |
| bystial-dragon-link | Drake Magnamhut | Torvin Hale |
| fire-king-snake-eye | Garunix Azamina | Teodoro Brasa |
| nekroz | Lady Clausolas | Lady Merel |
| orcust | Harpist Galatea | Harpist Celandine |
| elfnote | Medius | Linden |
| drytron | Commander Meteonis | Commander Alma Sotelo |
| domain-monarch | Emperor Ehther | Emperor Ostrand |
| danger-dark-world | Nessie Grapha | Dottie Larkin |
| tri-brigade | Kitt Fraktall | Sorrel Kade |
| floowandereeze | Robina Empen | Juniper Pell |
| tenpai-dragon | Chundra Sangen | Kenta Mizorogi |
| k9-vanquish-soul | Razen Vanquish | Rafael Lezcano |
| altergeist | Hexstia Wraith | Ione Wraith |
| kozmo | Captain Farmgirl | Captain Mae Tolliver |
| spright | Sprigh Jet | Zia Sparks |
| satellarknight | Altair Knight | Sir Tobin Ashgrove |
| ryzeal | Ryzeal Duo | Nico and Nina Castell |
| purrely | Purrely Pip | Pip Halloran |
| dracotail | Ember Rahu | Ember Kovac |
| six-samurai | Shien Kurogane | Isamu Tokiwa |
| thunder-dragon | Volta Raijin | Volta Kask |
| true-draco | King Dinomight | King Barrund |
| yummy | Chef Cupsy | Chef Praline |
| lunalight | Selune | Lisette Morrow |
| light-and-darkness | Archmage Noctis | Archmage Halvard |
