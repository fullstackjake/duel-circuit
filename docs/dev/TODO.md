# TODO

Open follow-ups noticed while building, not yet scheduled. Check one off (`[x]`) when it's done; add notes under it.
UI polish ideas live in [UI-ROADMAP.md](UI-ROADMAP.md); the post-game's phases in
[POST-GAME-PLAN-2026-10-31.md](POST-GAME-PLAN-2026-10-31.md).

## Card looks (from the 2026-11-07 build; docs/dev/CARD-LOOKS-PLAN-2026-11-07.md)

- [ ] **Frame time not measured.** The app has no fps or frame-time log. Measure the deck editor and a full board on
  the slowest Mac available. The shader cost is estimated at about 2x the old one for Secret cards at board size;
  the cheap fallbacks are to skip the 45° band and the Shatterfoil Voronoi search when `_Small` is 1.
- [ ] **First-ever view stalls once.** The deck editor's first open of uncached HD cards took about 2.5 s; after that
  it's about 200 ms. A background pre-warm of the decks' cards at idle would remove it.
- [ ] **Still weak by look:**
  - the QCSR "25" mark is invisible on full-text cards;
  - Ultimate's stars look brownish;
  - grades 10 and 7 are close at board size;
  - Rare vs Common on the board rests on a 5 px silver name;
  - Pharaoh's glyphs are a little neon in the lobe;
  - the slab glare is faint over very bright cards;
  - the detail picture's rarity tag and Artwork chip stay flat while the picture tilts.
- [ ] **Not built:** wear on card backs (back-edge whitening), mesh foil curl, tilting slabs, tilting pack reveals and
  the spotlight (their stats strip is a sibling), 3D or parallax art layers.
- [ ] **The live tilt spring was never watched on screen.** The checks use fixed tilts. Check the feel, and that the
  side under the pointer dips away.

## Story screen and art

- [x] **Story scenes have no backgrounds.** (done 2026-11-06: 11 code-drawn places, a place: key per scene, crossfades) Every scene plays on the same dark screen: the shop door "after closing",
  the back room, "out front" at the corkboard. The two Every Era epilogue scenes run on with no visual change between
  them. A backdrop per place (shop floor, back room, corkboard), picked by the scene, would carry the moves.
- [ ] **Portraits cut off on the right.** Ilse, Garunix (fire-king-snake-eye) and Volta Raijin (thunder-dragon) have a
  hard vertical edge on the right of their story portraits (seen in the `-finalcheck` shots), and the name plate covers
  the bottom right of Ilse's. Re-crop or extend the art.

## The Every Era final and the Library

- [ ] **Re-measure four of the final's rounds at 72 duels.** goat-control, six-samurai, kozmo and eldlich rest on
  24-duel measurements (±20 points) in campaign/library-advice.txt: library keys 0.3, 2.3, 5.3 and 8.3.
- [ ] **No round of the final has been won in a real duel by automation.** `-finalcheck` plays round 1 for real and
  concedes, then records the wins and plays the real results screens and story. The QA stand-in can't reliably beat
  these bosses; a seeded win (or `-qaplay` reaching the final) would prove the whole path.
- [ ] **No Library cup duel played to its results in the app.** The checks reach a cup's versus screen; the Champion's
  table has never been played in the app either.
- [x] **A results screen can open over the next duel.** `ShowCupResults` and `ShowRoundResults` wait 2 s and only check
  that a cup (round) is still set: pressing the board's Rematch within those 2 s shows the old duel's results over the
  new one.
  - Done 2026-11-05: every delayed results screen checks a duel serial and drops itself if another duel or replay started (all 2 s waits share one helper); a final check step races it.
- [x] **Partner-deck edits count as yours for a cup's Format Master stamp.** The final excludes them (a partner's edit
  is theirs to bring); the stamp's "deck you built" check doesn't.
  - Done 2026-11-05: the stamp and the final share `Loaners.IsYours` (`Library.CountsForStamp`); the results say "a partner's edit is theirs to bring".
- [x] **The final's panel calls a custom loaner "ready".** Its Your deck line uses the cup's problems, which don't flag a
  loaner; the round's versus screen refuses it.
  - Done 2026-11-05: the panel, the round's versus and its start share `CampaignState.RoundProblems`.
- [x] **A round's result line skips the Yata-lock message** the cup duels show when a duel ends by Yata-lock.
  - Done 2026-11-05: a shared `YataLockLine` (verified by reading: no check can force a Yata-lock ending).
- [x] **The Every Era poster hard-codes the final.** `Posters.cs` checks six `lib:every-era.N` keys rather than asking
  the Library, so renaming the final or changing its round count breaks it silently.
  - Done 2026-11-05: the poster asks the loaded Library for its final (`Posters.Library`, set by `Library.Load`).
- [x] **Replays always show the default mats**, not the era mat you had on (the mat isn't recorded).
  - Done 2026-11-05: replay format 5 records each side's mat when the duel picked one by name; format 4 still loads.

- [ ] **Bug batch follow-ups.** Decided (owner, 2026-11-05): a bracket's Stuck? keeps counting by bracket round; the
  alley brackets keep the rating-based Make it yours offer; engraving skips a partner's edit like the Format Master
  stamp (being built with the Invitational). Guest and Tag Cup result lines show the Yata-lock line too (2026-11-06).
- [x] **The game's logo** (owner's art, 2026-11-05): art/logo/logo.png on the title screen and the menu (the text title
  stays as the fallback); the Mac app icon from unity/Assets/Art/AppIcon.png (the logo on a square transparent canvas).

- [ ] **Not verified (2026-11-05 bug batch):** the round's Yata-lock result line is checked by reading only; no check
  can force a Yata-lock ending. A scripted Yata-lock finish in `-finalcheck` would prove it.

- [x] **A held-back Invitational finish has no exit if its No Limits Pack can never be filled.** (done 2026-11-06: settled
  at the next launch, the packs owed until the pool can fill) `Brackets.Finish` keeps
  the bracket unpaid (so the packs can't be lost) and every Finish retries, but while it's held `EnterBlocked` refuses
  every bracket. Unreachable today (Opening needs every field build ready, which fills the pool); it could happen only if
  a build went back to `ready: no` after a player entered. A fallback: pay the DP and record now, owe the packs.

- [x] **Faces for the alley's nameless regulars** (owner's art, 2026-11-05): 40 portraits in
  art/portraits/alley-grunt-01..40.png (the second of the owner's two sheets, cut and trimmed). A regular's face is
  picked by its handle (`Portraits.GruntFor`), on the table panel, the bracket, the versus, the duel board and replays.
  The sheet's cells were small (about 190 x 150), so the faces are softer than the ~300 x 350 character portraits; a
  higher-resolution export would drop straight in.

- [ ] **From the overnight work (2026-11-06; docs/dev/OVERNIGHT-2026-11-06.md):**
  - QA (docs/dev/QA-OVERNIGHT-2026-11-06.md, 320 duels, no real bugs): saving a deck that already is its recipe still
    offers "save as new" and piles up identical copies (Gadgets (3)-(13)); the campaign header's wallet line wraps at
    1280x720; the QA harness remembers its "second deck" across a -qacontinue run (tooling only).
  - Shop keys: Left/Right in single-column lists turn the page (or do nothing?); Enter never buys (add a confirm and let
    it?); should the d-pad drive the new highlight instead of the virtual cursor? Moth's van has no arrows yet.
  - Story backdrops: nl-restored changes rooms inside one scene (split it, or allow a place change mid-scene); the
    Black Market's tarp and Nyx's lantern table could get places of their own; owner art goes in art/backdrops/<place>.png
    (11 keys; drawn at a 0.62 tint, and the theme check measures brightness).
  - The CPU's removal timing: covers MST-style pure removal only; a monster quick effect that destroys a chained Normal
    Spell (Ryzeal Detonator, SPYRAL Sleeper, Breaker) still wastes its use. "Destroyed own Spell/Trap" (Heavy Storm with
    the CPU's own set cards, 33 per 300 Beginner games) is untouched: a deliberate trade or a blunder? Artifact
    Sanctum's own trigger can destroy the CPU's Moralltach (pre-existing).
  - A held-back Invitational settles only at the next launch (no in-session button).
  - **Possible bugs (measurements, docs/dev/TUNING-MEASURE-2026-11-06.md):** nl-yubel and nl-yubel-brawl stop at the
    prompt cap in 1.5-3.3% and 1.1-2.2% of endless duels, some on turns 2-3 (a stalled prompt: find the prompt);
    nl-branded reaches turn 40 in 2.2-3.3%; one `--peak-check` duel (Lacrima's 60-duel run) hung in the engine's
    get_attack_target, and the peak check has no prompt cap or turn limit (add both). Outer Entity Azathot is never used
    (0 of 60).
  - Done 2026-11-06 (docs/dev/NEXT-BUILD-2026-11-06.md): the QA items (Save on an unchanged deck says so, Save as new of a
    deck you have opens it, the wallet is three scaled lines, the QA harness skips its second-deck check after other
    duels; the -qacontinue fix is compiled, not run) and both possible bugs (one bot loop under Nightmare Pain's forced
    attack: 0 stalls in 20,000 endless duels, Lacrima's peak check finishes; `--peak-check` has a 40-turn and 8,000-prompt
    cap; `--forced-attack-test`). nl-branded's turn-40 games are real long games, not a stall.

- [ ] **From the 2026-11-06 build (docs/dev/NEXT-BUILD-2026-11-06.md):**
  - Done 2026-11-06 (second batch): the two CPU misplays. Battle memory is keyed by card identity (DuelContext
    `MonsterIdentity`), and "Cannot be destroyed by battle" is read in any case with conditions rejected; attacks into a
    battle-proof monster whose controller takes no battle damage, or that reflects it, score -1. Paired A/B 1,240 duels:
    fixed 548, old 544, no deck dropped; `--battle-misplay-test`. Left open: attacks that pay off through the attacker's
    own battle trigger (B.E.S. counters, Gladiator Beast tag-outs) aren't valued; case-sensitive text matches in
    `SmartDuelist.Sense.cs` ("cannot attack": Forbidden Crown) and `Drawbacks.cs` (Gimmick Puppet Nightmare, The Big
    Saturn, five "opponent's field" cards); in Albion seed 701115 a Blackwing - Zephyros the Elite survives every battle
    with no visible protection (worth a look); MSG_SHUFFLE_SET_CARD keeps identities with zones.
  - The core sets `attack_cancelable` even under EFFECT_MUST_ATTACK (processor.cpp ~1871/2048); worked around in
    SmartDuelist, the core is untouched.
  - Not run: the full `--peak-check all --games 60`. Gumblar Gail's turn-40 line is OFF (3.6%) at `--endless-test --games
    3000`, before and after the fix (new since the overnight run).
  - **The Champion's Ladder** (owner, 2026-11-06: committed): Ilse's "ladder" and Junia's "other doors" lead there. A
    planned feature, not specced.
  - Vera's last match: a post-final guest on a warriors build that waits on its traps (owner: yes, later).
  - Story items the writing pass left: `after:`/`before:` gates for story scenes (review item 7); alley-upstairs on
    antewin; trimming gus-lessons and novice; `[open]` variants of alley-kit and nl-first-visit; Rhea and Seraph never
    mentioned; New Game+ story; the `regular:lose:card:<card>` hook.
  - Vera's rematch plays her circuit warriors deck (6 traps, aggression 0.95), so it doesn't "wait on its traps" as the
    owner asked: a trap-waiting build (new list or a per-guest personality) is for the tuning pass (owner, 2026-11-06).
  - **App checks write into the player's real preferences:** the built app's PlayerPrefs domain is
    `com.ygocampaign.game` (window size and position, foils, music volume, response settings), shared by the owner's
    installed game and every check run; checks that resize the window or toggle settings leave them there. Give checks
    their own prefs domain or restore what they change.
  - The theme check's story crossfade timing failed once at exactly its 0.54 s minimum (passed on rerun): widen its
    margin. Hover shows under the pointer while the game is in the background (IgnoreFocus); a "card not hit at its
    centre" count in the cover watch is cards mid-redraw, not covers.
  - Ada Quill (the Ladder's top) has no portrait yet (`art/portraits/ladder-top.png`).
  - Ilse: her guest row's status says "they can partner you" (no pronoun field); a sixth guest wouldn't fit the compact
    rows; the layout check never visits the guests screen; her real duels aren't played to the end in any check.

## Economy and levels

- [x] **The results EXP line at level 99 shows nonsense** ("49,750,980 / 5,050 EXP to level 100") when EXP passes the
  cap. Only test saves get there so far; show "Max level" instead.
  - Done 2026-11-05: "Max level" on the results and the Card Trader header.
- [x] **Route every DP sink through `Profile.Spend`.** Only era mats and engravings feed the `DpSpent` ledger; packs,
  outright buys, tokens, grading and the used market don't yet (POST-GAME-PLAN step 11).
  - Done 2026-11-05: all engine sinks already did; the alley stake hold and return go through `Profile.HoldStake`/`ReturnStake` tied to the ante, and LedgerCheck's SourceGuard fails any other Dp write. Dev-check DP in Unity goes through `Ledger.Debug`.

## From the QA playthrough (2026-11-01, 30 duels, `build/qa-quick/`)

Found by the Evidence Collector and Game Designer reviewing a 30-duel `-qaplay -qafull` run. Fixed the same day: the
coin toss came in streaks (it now scrambles the duel seed), Xyz monsters showed "R0" outside duels, and the tag check
failed when a random lineup won on turn 2.

- [x] **The guest tag duel against Ines & Old Gus is a wall.** The stand-in lost it six times in a row right after it
  unlocked (two grinds of 45 and 63 turns), and Gus's tag-duel lesson only comes after. Part of it was a weak
  auto-built deck (next item).
  - Done 2026-11-01: every guest's tag duel opens once Card Shop Tag Night is cleared (`tagunlock:` in guests.txt; the
    first point a partner can be picked); the guests screen says so and shows the button as locked.
- [x] **"Build" in the deck editor makes a much weaker deck and makes it your campaign deck.** From a recipe you don't
  own enough of, it pads with pack filler (Ojamuscle, Gokipon, Water Dragon) and leaves the Side Deck empty.
  - Done 2026-11-01: Build warns before replacing a full, legal deck and says afterwards when the result looks weaker
    (Undo brings it back); Save as new keeps your campaign deck, "Save new & play it" switches.
- [x] **After Save as new, the editor still shows the old deck's name**, and the duel's deck line drops "(new)", so the
  two decks look the same.
  - Done 2026-11-01: the name was there but clipped (the field now fits long names), and the copy gets its own title.
- [x] **The shop's era labels disagree.** The circuit map says "Novice: sets up to 2011" while the shop is already at
  2013, and Trap Masters Cup in the same band says "2014 to 2018"; the shop's header says sets open "each level", its
  footer "with later circuits".
  - Done 2026-11-01: one rule everywhere ("Sets up to 2013, to 2015 at level 12"); circuit blurbs say "decks from".
- [x] **Stuck? advice points at things you can't do yet**: picking a partner "on the Tag Cup screen" during a guest tag
  duel, before any Tag Cup is open.
  - Done 2026-11-01: it names where the partner is really picked and only mentions an open Tag Cup.
- [ ] **Early circuits are easy.** Apart from the guest wall, the only losses were two first matches, each won on the
  next try; 12 of 22 wins ended at full LP by turn 4-10. Vera's "the easy wins are over" is followed by a 4-0 circuit.
- [ ] **Chain panel text clips**: a link-2 panel's text shows through link 1, and a two-target line ends in a comma
  with the second target cut off.
- [ ] **The "summon here" column hint shows when you can't summon** (the opponent's turn, your End Phase).
- [ ] **Card names wrap mid-word on cards without a picture** ("Polymerizatio n"), and the editor's count badge covers
  part of the name.
- [ ] **Partner intro lines don't fit the moment**: Ryo says "My first real duel!" when he joins as a partner; Kit says
  "I brought my Synchrons" while playing a Warriors loaner. The versus deck line can run into the < arrow.
- [ ] **Cards without a picture.** Pictures come from the player's EDOPro install, and this machine's has 3,554; the
  rest (Axe of Despair, Water Dragon, most pack pulls) show the text look. Worth a one-time note telling players where
  pictures come from and how to get more.
  - 2026-11-07: the docs explain HD picture folders (`hd_pics:`, docs/CARD-PICTURES.md). An in-game note is still
    not done.
- [ ] **The deck editor's "don't own enough of any recipe" message shows twice.**
- [ ] **QA tool nits**: story shots are taken mid-typewriter, `01-campaign.png` shows the first-duel offer, and every duel
  logs "turn 0 (you)" whoever starts.

## Art needed from you (story and characters review, 2026-11-01)

The characters below were renamed or recast in text on 2026-11-01 ([STORY-REVIEW-2026-11-01.md](STORY-REVIEW-2026-11-01.md));
until new art arrives they keep their old portraits (`art/portraits/<id>.png`) and expression sheets
([EXPRESSIONS.md](EXPRESSIONS.md)). Each needs a new portrait and expression sheet. Original designs only: no card
art, no likeness to Yu-Gi-Oh! anime characters or other franchises. Their full looks are in [CHARACTERS.md](CHARACTERS.md).

- [ ] **Kelda Amin** (`ishizu-tearlaments`): her portrait was removed on 2026-11-08 (owner) before going public: it looked
  close to an anime character. She shows the code-drawn stand-in until a new portrait arrives.

- [ ] **Mara Venn, the Willing Wound** (`yubel`; was Nightmare Yubel). Late twenties, compact and muscular, olive
  skin, short dark undercut. A scuffed padded stunt jumpsuit in black with safety-orange panels, taped wrists, a fresh
  bruise on one cheekbone; a stunt helmet under one arm. Chin up, arms open, grinning as if inviting a hit. **No wings,
  matching eyes.**
- [ ] **Kelda Amin, the Archivist** (`ishizu-tearlaments`; was Seer Kelbek). Mid-forties, tall, dark brown skin, long
  box braids with reading glasses pushed up into them. A charcoal blazer, rust turtleneck, silver watch; a tablet with a
  tournament spreadsheet, archive boxes behind her. **No veil, kohl, gold necklace, sand or hieroglyphs.**
- [ ] **Ozzie Quintero, the Clockmaker's Apprentice** (`pendulum-magician`; was Odd-Eyes Ozzie). About nineteen,
  lanky, light-brown skin, curly dark hair, brown eyes. A mustard waistcoat over navy shirtsleeves, a jeweller's loupe
  on his brow, a canvas tool apron; a brass pocket watch mid-flip. **No top hat, no red or green, no odd eyes, no
  pendulum jewellery.**
- [ ] **Pilar Sendra, of the Kashtira Ridge** (`kashtira`; was Shangri Ira). Late thirties, lean and weathered,
  tanned skin, black hair in a low braid. A faded orange shell jacket over a climbing harness, quickdraws, chalked hands;
  a coiled rope over one shoulder. Standing still on a rock ledge, calm and watchful. **No beads, robes, bare feet or
  prayer pose.**
- [ ] **Professor Hollis** (`hollis`): redrawn as a Black professor in his sixties, close-cropped grey hair and a
  trimmed grey beard, round wire glasses, a tweed jacket with elbow patches over a green waistcoat, an annotated
  rulebook under one arm (so the two mentors don't read as the same grey-bearded man). Same role and warmth.
- [ ] **Sister Annick** (`exosister`; was Sister Elis): her current portrait shows crosses and praying hands; she now
  wears a chain of tiny gears at her belt instead of a rosary.
- [ ] **Jiro Kanemaki** (`zoodiac`; was Ratpier Rokuro): a Japanese karateka; the portrait's charms should be the
  twelve wooden eto (zodiac animal) charms, nothing from other cultures.
- [ ] **Check the other renamed characters' portraits for card-art likeness.** 31 more were renamed (their personal
  names were card names: Corin Vasse, Nell Ardent, Sumire Ishigaki, Coralie Fenn, Teodoro Brasa, Captain Mae Tolliver,
  Volta Kask, Nico and Nina Castell and others; the table is in the review's follow-up). Their looks didn't change, but
  a portrait drawn to match a card's monster should be redrawn as an original person.
- [ ] **Mat brief:** `ishizu-tearlaments`'s mat is now "teal waves over archive pages" ([MATS.md](MATS.md)); it's still
  painted in code, so a picture is optional.

## Card packs (review 2026-11-01)

- [x] **Shop and packs rework.** Built 2026-11-01 from [SHOP-REWORK-SPEC-2026-11-01.md](SHOP-REWORK-SPEC-2026-11-01.md),
  with prices from [SHOP-PRICES-2026-11-01.md](SHOP-PRICES-2026-11-01.md) and lines from
  [SHOP-VOICE-2026-11-01.md](SHOP-VOICE-2026-11-01.md):
  - Gus's Cards: featured shelves for each circuit, collapsed back shelves, New arrivals, pull tags and set progress.
  - Gus's house packs for the current and next circuit, booster boxes, and odds shown for every pack and Prize Pack.
  - Gus's and Kit's lines.
  - The catalog fix (157 → 229 sets) and the rarity-set rule.
  - The approved prices.
- [ ] **Re-run the economy sims on the rework.** The narrower house packs, boxes and new prices move what the
  `--campaign-sim` bot buys from Novice on, and `--postgame-sim`'s spending shares. The price runs in
  `build/shop-prices/` used the old house-pack rule. The open risk: a Master deck costs about 15k DP at the new
  prices, the floor of the 15-30k target. Each campaign run takes several hours.
  - The Economy Designer's probe on a real Master-start collection: recipe packs alone build a Master deck for about
    12.5k DP, under the 15k floor. That's the Master recipe pack price (2,500, decided 2026-10-31); about 3,000 would
    put it near 15k. One sample: confirm with the re-run before changing it (owner's call).
- [ ] **Shop polish seen in review, not yet on screen:** a greyed RARITY SET chip on a locked soon row; Ghost,
  Starlight and Quarter Century pull lines; the post-game shelf; the shop after New Game+.

## The Back Alley (step 9; spec docs/dev/ALLEY-SPEC-2026-11-01.md)

- [x] **Phase A, the loop** (built 2026-11-02):
  - the unlock and the Back Alley tab in its own theme;
  - Dumpster Row, The Basement Game and The Black Market, with seeded nights and Yata Mags and Mass Driver Moss;
  - the Alley list and the turn-40 rule;
  - held Street and Den stakes with binders, and the first-turn refund;
  - 20 scenes, and `-alleycheck`.
- [x] **Phase B** (built 2026-11-02):
  - Orvel's Table (your card against his slab, Moth's rail at 1.5x for 5 duels, buy-back), measured at 57.5% for the
    best QA deck (target 55-60%);
  - Moth's van (shelf, used & graded, sell), Contraband Packs as prizes, the Unlisted binder;
  - Penny's Kuriboh, the HELD card (Ines's payoff), six alley posters;
  - the look: code-drawn brick, chain-link and tape, the brick mat, the alley top bar, the rain bed and alley music;
  - ten scenes, `-alleycheck` steps 10-11.
- [ ] **The stencil font:** drop the owner's TTF anywhere under `unity/Assets/Resources/Fonts/` (Unity loads runtime
  fonts only from Resources; the spec said `unity/Assets/Fonts/`). Portraits for Moth, Baz, Odile and Penny and the
  hooded silhouette (`art/portraits/alley-hood.png`) are optional; code-drawn fallbacks are in.
- [x] **Queued story scenes live only in memory** (done 2026-11-06: saved as optional storyq lines, played once after a
  reload; a quit mid-batch replays only the scene on screen) until a results or campaign screen plays them: quitting the app in the
  2 s after any duel (alley, circuit, Tag Cup, guest, Library cup, final, bracket) loses that duel's first-time scenes,
  and nothing raises them again on load. Since 2026-11-05 every result uses one queue (per-duel batches), so the fix is
  one place: save the pending batches as an optional trailing field and replay them at load.
- [ ] **Not heard yet:** the rain bed, its fades and the alley/Orvel themes (the checks run muted).
- [x] **Phase C** (built 2026-11-02):
  - Baz's chalkboard: Alley Brawl and the Midnight Open, 16 players, the Open's final best of 3 (the loser chooses),
    Contraband Pack prizes and the Open's first-title token;
  - leaving an alley bracket duel counts as a loss (Locals still replay); alley brackets pay no card-unlock bonds;
  - Nyx's Table (four seats, Nyx's Eldlich-and-contraband deck at 54-56% for the best QA deck);
  - the Midnight Open field retuned (ryzeal and angelechy in for dragon-ruler and k9-vanquish-soul): 26-30% for a
    champion's best deck (target 15-30%);
  - `-alleycheck` steps 12-13.
- [x] **Alley Brawl runs easy for a champion:** 70-79% title odds at 1,880-1,900 Elo (target 30-45%).
  - Done 2026-11-05: NL-1's four out-of-towners join after The Black Market's clear: 46.4% / 49.4%, accepted by the
    owner (docs/dev/ALLEY-DIFFICULTY-2026-11-03.md).
- [x] **Nyx's Table, seats 1-3 run easy:** 77-90% for the best QA deck (Nyx's own seat 54%).
  - Done 2026-11-05 (e2a95d5): seat 2 is thunder-dragon as built (56%), seats 1 and 3 at skill 1.0 (about 70-75%,
    accepted as the table's ramp; seat 3 is fixed when Lab Coat Lyle is ready).
- [x] **Phase D, the headliners' show** (built 2026-11-02): the banner, sting, Slow down for combos, the combo meter,
  the payoff beat (counted only when it resolves), the fizzle, Baz/Mika commentary, Combo Breaker posters, Watch the
  combo on Unlisted pages, the watch-only exhibition. Headliners ready: Yata Mags, Mass Driver Moss, Gateway Gwen (the
  reserve in Trishula Trish's slot), Master Plan Marlo, Gumblar Gail.
- [ ] **Phase D's proof run** (the difficulty pass is done: docs/dev/ALLEY-DIFFICULTY-2026-11-03.md): `--alley-study` over
  every table, an alley pilot in `--postgame-sim`, a 60-duel `-qaplay`. Parked with the difficulty and CPU pass.
- [ ] **Headliners left no-go:** Trishula Trish 29%; Lab Coat Lyle's payoff comes around turn 10. Infernity, the other
  Basement reserve, isn't built.
  - Decided (owner, 2026-11-05): **Return Rook ships with the smaller show** (a two-monster return payoff; built 2026-11-05,
    finishes 51-55% on the honest rule, ready with his poster and clip); **Gateway Gwen approved as written** (name, title, lines,
    look, scenes); **Gumblar Gail's tell stays general** (no card named); **2024-26 cards keep their name plates** (no
    art assets).
- [ ] **The show's audio** (sting, roar, groan) hasn't been heard: every check runs muted.
- [x] **Alley difficulty (measure in Phase D):** Dumpster Row had the best QA deck winning 95% (target 75%) and the
  classic starter 28% (target 65%).
  - Measured 2026-11-05: 97.7% / 33.7%; the closer is warriors now. The owner keeps the targets and records the table as
    off target with the QA deck (goat-control, the deck at unlock, reads 66.7%). The regulars' event skills (0.7, 0.85,
    0.95) are a question for the later CPU pass (ALLEY-DIFFICULTY open question 4).
- [ ] **The Black Market's difficulty pass (unfinished; stopped 2026-11-05 for a bigger difficulty and CPU pass later).**
  Shipped table (Jolt, games 3, `--load build/qa-night5/mods`): the best deck wins 82.0% of 150 against the regulars
  (target 60%), 80.0% of 30 against the kozmo closer, 86.7% against Master Plan Marlo and 93.3% against Gumblar Gail
  (target 55%). The starter wins 11.3% of 150 against the regulars (target 40%), 6.7% against the closer, 0.0% against
  Marlo and 10.0% against Gail. Each base alone as every regular and the closer (headliners off, 180 + 30 duels):

  | Base | Best vs regulars | Best vs closer | Starter vs regulars |
  |---|---|---|---|
  | thunder-dragon, 0 / 1 / 2 cards | 69.4% / 64.4% / 62.8% | 76.7% / 76.7% / 70.0% | 13.9% / 10.6% / 10.0% |
  | danger-dark-world | 74.4% | 80.0% | 2.2% |
  | kozmo | 87.2% | 80.0% | 16.1% |
  | shaddoll | 88.3% | 93.3% | 6.1% |
  | abc, 0 cards | 85.0% | not run | not run |

  The three shipped bases average 83.3%, close to the mix's 82.0%. So a table's rate is about the mean of its bases'
  rates. Kozmo and shaddoll make it easy. The lead candidate wasn't measured: `bases: thunder-dragon, danger-dark-world`,
  `closer: thunder-dragon`, `size: thunder-dragon 2` for its `skip:` (expected about 66-68%). It would change Nyx's seats
  1 and 3, so remeasure Nyx after it.
  - The starter's 40% looks out of reach. Classic wins 2-16% against every eligible base, and every base is
    rated 1585-1693.
  - The headliners are a deck-strength problem: spyral rates 1466 and gouki 1259. Rebuilds belong to
    docs/dev/HEADLINERS-2026-11-01.md.
  - `size: <base> 0` still takes one `for:` card, because `Alley.Pick` takes `Math.Max(1, count / 2)` of them.
  - Still to do: the mix at games 5, Nyx's remeasure, `--alley-gen`, `--campaign-test`, `--endless-test`, `--arbitrage`
    and a docs/BLACK-MARKET-DIFFICULTY doc.
  - Scratch roots and logs are in build/bm-roots/ (mkroot.sh, queue.sh). No shared file was changed.
- [ ] **Starter decks are illegal in the alley as built:** Warrior's Charge runs 3 Reinforcement of the Army, which
  Traditional limits to 1, so an unedited starter is refused at the versus. Check the other starters, and decide
  whether the deck screen should offer a one-click Alley fix.
- [ ] **Yata Mags runs Fiber Jar and Magical Scientist**, both on the alley's exclusion lists (a reset, and a turn-1
  kill). Headliners play their own era's list at full strength, so the deck is unchanged. Watch for stalls in Phase D.
- [ ] **Mass Driver Moss's list:** the deck says TCG March 2010 and runs 3 Treeborn Frog, where that list allows 2;
  recorded as an approved overage for the Headliners author and owner to confirm.
- [x] **Result scenes can be lost:** if the menu, editor or shop opens during the 2 s wait after a circuit, Tag Cup,
  guest, Library cup, final or bracket duel, that duel's scenes never play. Alley duels now queue theirs; do the same
  for the others.
  - Done 2026-11-05: every result queues its scenes when the duel ends (the alley's queue); the results screen takes them, Rematch and the shop put them back.

## Card versions (planned 2026-11-03; spec docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md)

Collectible original and current texts by printing; an original plays as printed where its text was in force.
- [x] **Phase 1:** close the exploit (originals legal anywhere at 3 today) with "texts as of" dates per table, the
  errata-date table (campaign/texts.txt), CardVersions by entry not name, the duplicate rows, `#originals`. (9e4b141)
- [x] **Phase 2 (built 2026-11-05; As built in the spec's section 11):** copies carry set code, month and text; every
  source assigns a printing; the editor shows owned versions; a per-text shortfall plays current text with a note.
- [ ] **Phase 2 follow-ups:** decided (owner, 2026-11-05): `PrizePacks.PoolByName` stays off; one original copy may play
  in both decks of a tag duel (as card counts do). Still open: other shared-code reprints may exist (Silver Dragon value box, later 25th
  Anniversary sets after a data refresh). No structure deck file has `#set` yet.
- [ ] **Phase 3:** era texts for CPU decks (opt-in, re-measured) and alley splash originals.
- [ ] **Phase 4:** Goat duel mode for the Goat Cup, gated on 1,000 freeze-free duels and a re-measure.

## No Limits (planned 2026-11-02; spec docs/dev/NO-LIMITS-SPEC-2026-11-02.md)

The 2023-2026 meta decks as "peak builds" with their own banned cards back, behind Nyx's wall
(docs/dev/META-DECKS-2026-11-02.md; economy and story in NO-LIMITS-ECONOMY / NO-LIMITS-STORY).
- [x] **NL-0, prerequisites** (built 2026-11-02; five lists verified, 7 peak lists, `--rate-anchored`; four peak lists rate
  below their base until NL-1's playbook work): the missing TCG lists (Jan 2024, Sep 2025, Oct 2025, Feb 2026, May 2026), Djinn Releaser
  of Rituals and Archnemeses Protos into `Alley.BossOnly`, an anchored rater for the peak builds, the peak lists.
- [x] **NL-1, wave 1, built (2026-11-05; As built in the spec's section 14.3):** the table and seven builds (Snake-Eye,
  Mitsurugi, Ryzeal, Vanquish Soul, Tenpai, Branded Despia, Yubel), 2,500 DP stakes with the nightly restock, the Brawl's
  out-of-towners. The table and builds ship `ready: no` ("Coming soon") until the ship gate below.
- [ ] **NL-1's ship gate (parked with the difficulty and CPU pass):** every build 45-65% and the pool mean 55% ± 5 in
  `--alley-study --only "No Limits"`, `--endless-test`, `--peak-check all`; then flip the table and the ready builds to
  `ready: yes`. Today every build rates under the 1,840-1,990 window (1,673-1,865); nl-mitsurugi and nl-ryzeal rate under
  their base (the base playbook's work, section 7.2); nl-yubel hit the prompt cap in 3.3% of an endless smoke run.
- [ ] **No Limits leftovers (small):** (`baz:nolimits` now speaks on the panel, 2026-11-05) a saved build seat whose deck file goes missing isn't
  redrawn; the rotation's seat-4 step when no champion is unbeaten (spec 14.3 As built). The 2024-26 pieces keep their
  name plates (owner, 2026-11-05).
- [x] **NL-2's non-deck parts (built 2026-11-05; As built in the spec's section 14.4):** the No Limits Invitational and
  Moth's restored shelf slots. Both stay locked until wave 2's builds are ready.
- [ ] **NL-2, wave 2 (parked with the tuning pass):** Kewl Tune, Maliss, Light and Darkness Ritual, Toon (Mind Scan engine
  work; card data after Magnificent Maestros, 13 Nov 2026). The Invitational opens once all eleven builds are ready.
- [ ] **Proof:** one run with the alley's Phase D.

## Brackets and card unlocks (built 2026-11-01)

- [ ] **Five Locals run easy:** title odds at HAT & Shaddoll 46%, ABC to SPYRAL 55%, Synchro Peak 63%, TOSS & Eldlich
  66%, Rabbits & Rulers 74% (target 30-45%). Their eras have no stronger legal decks; options are later-era visitors
  in those fields or a smaller prize there. Measure with `--bracket-matchups` first (Elo is off by about 85 points).
- [x] **The bracket versus has no Stuck? advice or Make it yours offer** (it has Scout and the toss tip).
  - Done 2026-11-05: Locals use the cup's advice; the alley brackets name stronger recipes to own and offer the strongest unlocked recipe built under the Alley list.
- [ ] **Bounties and drops in QA:** the QA stand-in logs grants and takes bond trades; a long run should confirm the
  per-band unlock counts stay under 15% (`--campaign-sim` prints them).
- [ ] **The board check's "Back returns the cursor" step fails on a freshly copied app's first launch** (passes on
  every later run): a first-launch timing flake in the check, not the game. Make the step wait for the menu to settle.
- [ ] **The tag check's "under way" step can fail when the seats it draws make a short duel**: seen once on 2026-11-01
  (King Barrund's table ended on turn 4, so it saw 1 swap by turn 2); it passed on both re-runs. Make the step wait for
  the second swap, or count swaps over the whole duel.
- [x] **Gus's bounty board shows (and pays) before Gus introduces it.** In QA the very first duel paid two Beginner
  bounties ("no damage", "by turn 7") before the board's scene at the Beginner Circuit clear. Gate the board on that
  clear, and consider harder Beginner bounties so they read as odd jobs, not freebies.
  - Done 2026-11-05: the board opens after the Beginner Circuit's clear (or the "gus-lessons" scene, or New Game+); bounty difficulty unchanged.

## Proof pass follow-ups (2026-11-01, docs/dev/PROOF-2026-11-01.md)

- [x] **Level-up DP after the first win (owner's call).** Post-game repeat and Locals duels pay level-up DP with no
  ceiling: cup farming about 345 DP a duel (cap 300), a Locals title run about +500. Recommended: those level-ups pay
  Card Tokens, not DP.
  - Done 2026-11-01: those level-ups pay tokens only (at least 1); cup farming 150 a duel, a Locals title run −100.
- [x] **Pack and Prize Pack odds aren't shown** (E5): shown since the shop rework (2026-11-01).
- [ ] **QA stand-in deck picks:** it keeps an unmeasured legal deck over Make it yours advice, played an unmeasured
  loaner in a Locals final, and two purchase log lines name the wrong deck. Fix it before reading Library win rates
  from QA.
- [ ] **Goat Cup match 1 went 1 for 6 in QA** against a measured 75%: replay those seeds in the harness.
- [ ] **The campaign sim's casual pilot stalls at Expert Series: Rituals and Fire** and spends everything: give it a
  stronger pilot or a saving policy so it reaches the Gauntlet and writes a normal-wallet champion save.
- [ ] **A post-game QA run that reaches the Every Era final** (about 100 duels from the champion save).

## Replay analyzer (spec docs/dev/REPLAY-ANALYZER-SPEC-2026-11-06.md)

- [x] **Chunk 1 (harness only):** `--analyze-replay <file|folder>` (pages, index.html, analysis.json), the shadow bot's
  second opinion and the watchers' flags, `--keep-replays` with the `.replay.bot` sidecar on --audit and the studies, `--analyze-test`.
  - Done 2026-11-06: built into build/bin/analyzer; `--analyze-test` 22 ok.
- [x] **Chunk 2:** the read-only score callbacks (DecisionNotes.cs, SmartDuelist `Notes` and `CurrentExposure`, TurnPlanner `Scored`
  and `ScoreOptions` sharing Decide's loop), the scores tables and regret severities, and `--analyze-test`'s play-unchanged A/B.
  - Done 2026-11-06: `--analyze-test` 29 ok (6 seeded duels identical with the callbacks on); 12 audit duels identical to the build before.
