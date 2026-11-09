# UI and presentation checklist

Ideas to make Duel Circuit look and feel better. Check one off (`[x]`) when it is done; add notes under it.

## In the duel

- [x] **1. Big summons.** Each summon type gets its own short scene: Fusion swirl, Synchro rings and stars, Xyz
  galaxy, Link arrows lighting up, Ritual and Pendulum effects.
  - Done: each scene plays before the monster lands, with its name; materials fly together first. Pendulum has no scene yet.
- [x] **2. Attacks with weight.** The attacker lunges, an impact flash with a little shake, a damage number, and a
  destroyed card shatters instead of disappearing.
  - Done: wind-up and lean, a streak, sparks and a shake that grows with ATK; destroyed cards shatter.
- [x] **3. Cards that travel.** Every move is visible: drawing from the Deck, going to the Graveyard, banishing (fades
  out), returning to the hand.
  - Done: arcs onto piles with a landing glow, banished cards dissolve, your draws flip face-up.
- [x] **4. A hand that looks like a hand.** Cards fan out in an arc, grow under the pointer, and can be dragged onto
  a zone to play them (clicking still works).
  - Done: an arc with overlap and tilt (both hands); hovering straightens, lifts and grows a card; drop a card on the field to play it (monsters are Summoned, or Set on the Spell & Trap row; Spells/Traps activate or Set), and the zone it was dropped on is used. Several options open the card's menu.
- [ ] **5. Duelists who react.** Portraits change expression on damage, big summons and low LP, with short speech
  bubbles during the duel. Needs 3-4 expressions per character (normal, smug, hurt, desperate), Vera and the bosses
  first.
  - Code done: speech bubbles, mood flashes (gold smug, red hurt, red pulse in danger), and faces swap in when
    `<id>_smug/_hurt/_desperate.png` exist. Lines written for Vera, Old Gus and the 19 circuit bosses; style-based
    lines for everyone else. Waiting on expression art (docs/dev/EXPRESSIONS.md).
- [x] **6. Life points with drama.** The number counts down, the bar drains, and the plate pulses below 1,000 LP.
  - Done: the number rolls down in red, a pale damage trail holds then drains behind the bar, the plate jolts and flashes red on a hit, and at 1,000 LP or less the frame and number beat like a heart.

## Around the duel

- [x] **7. A cleaner side panel in the campaign.** Hide the test tools (matchup pickers, speed, log) in the campaign;
  keep a card-info panel and a log you can pull out.
  - Done: campaign and Tag Cup duels show the circuit and match at the top, a big card view, the decision panel, and
    a Duel log button that opens the log over the panel. Pause, Step, Rematch, the picker and the replay buttons are
    free play only. The board itself stays its size (its layout is fixed), so "full width" became "uncluttered".
- [x] **8. A better card detail view.** Big art, Attribute and Level icons, a laid-out effect text, and a marker for
  an older version of the card.
  - Done: name bar in the card's frame color with an Attribute badge (地 水 炎 風 光 闇 神, 魔 for Spells, 罠 for
    Traps), the picture with a rarity badge (N, R, SR, UR...), Level/Rank/Link, ATK and DEF with icons, a
    [Type/Ability] bar, scrolling effect text, and "◆ Original (pre-errata)" / "◆ Goat format" for older versions.
    Full layout in the campaign; a compact one (text beside the picture) in free play's smaller panel.
- [x] **9. A circuit map.** The campaign as a map: each circuit is a place with its bosses' faces, locks and crowns,
  in place of the lists.
  - Done: seven painted regions (one per band) climbing from Beginner to the Gauntlet; circuits are round nodes with
    the boss's face on a winding gold path, locked/cleared/next-up states, and Tag Cups branch off. A picture per
    region (`art/map/<band>.png`) replaces the painting; prompts in docs/dev/MAP.md.
- [x] **10. A deck editor upgrade.** A grid of card art, filter buttons with icons, deck charts (monster/spell/trap
  split, Level spread), drag and drop between collection and deck.
  - Done: search results are a 5x5 grid of cards (copies in the deck, limits, and how many you own in the campaign),
    filter chips have icons and their kind's color, the Main Deck split and Level spread are charted, and the card
    panel is the Master Duel-style detail. Drag and drop was already in.

## Prompts on the board

- [x] **11. Prompts on the board (Master Duel style).** Every decision happens in the middle of the screen with the
  cards instead of in the side list:
  - [x] A card picker pop-up for choosing cards (Deck searches, sending to the GY): big cards, location tabs,
    checkmarks, a count (or Level stars), Select / Cancel, and a peek button to look at the board.
  - [x] Chain and yes/no dialogs that show the card involved ("X is activated. Chain another card or effect?").
  - [x] Gold key words in prompt text ("Select the **card** to send to the **Graveyard**").
  - [x] Choosing cards on the field: glowing cards, a top banner, and a Confirm disc on the card.
  - [x] "Use which effect?" as tabs with the effect text and OK.
  - [x] Icon buttons for Summon / Set / Activate and for Attack / Defense position.
  - [x] A full-width board in the campaign, with no right panel.
    - Done: in campaign and Tag Cup duels the mat and cards fill the screen (the board is centered), and a narrow
      column floats over the mat at the right: the circuit and match, the card detail, Log and speed. The decision
      list only appears for decisions the board can't show (declaring a number, a Type...). The detail went right,
      not left: the right edge of the mat is the free space. The board itself doesn't get bigger (a flat 16:9
      board is limited by its height); it gets the whole screen and the clutter is gone.

## Deck Workshop (from the Tag Force deck editor report)

Full plan: [DECK-WORKSHOP.md](DECK-WORKSHOP.md).

- [x] **12. Recipe Book.** Beating a duelist unlocks their deck as a recipe; a screen by map region with notes,
  how much you own, missing cards and where to get them, and Build from it.
  - Done: 74 recipes (every circuit and Tag Cup deck), unlocked by beating their duelist while they play it (worked out
    from your wins, so older saves have theirs), announced on the results screen, NEW until opened. Locked ones say
    who to beat and where. The editor's Recipe button now only uses unlocked recipes.
- [x] **13. A catalog that knows every card.** Unowned cards shown dimmed with where to get them, active filters as
  chips, Attribute/Type/Level pickers and sorting, related cards, a reading view, finer effect tags.
  - Done: the campaign catalog shows every card (yours first, the rest dimmed, "All cards / Owned only" toggle), with
    where to get one you don't own; 10 effect tags; Kind / Attribute / Type / Level pickers and 6 sorts; active
    filters as removable chips; Related cards (same archetype, cards that name it); click a card's text or picture
    for a large reading view with every printing.
- [x] **14. Safety and legality.** Autosave and restore, "not released yet" in an era, a reason for every problem,
  labels, folders and duplicating decks.
  - Done: every change is autosaved to a recovery file, and the editor offers unsaved work back; cards first printed
    after a ban list's format (until the next TCG list) are flagged "not released yet" (free play, and era-limited
    campaigns); every problem names its card and reason; Copy, a label per deck, and a Decks... browser grouped by
    label with each deck's era. Illegal decks still save.
- [x] **15. Extras.** Favorites, archetype completeness, Extra Deck check, partner identity locks, controller legend.
  - Done: ☆ Favorite on any card (stars on the grid, a Favorites view, saved in mods/decks/favorites.txt; Y on a
    controller); the deck's archetype (616 names from the card scripts) with how many of its key cards it plays (key
    cards = the ones the built-in decks play) and, in the campaign, how many you own; an Extra Deck check that names
    monsters the Main Deck looks unable to make (21 flags across the 71 built-in decks, mostly real splashes); the
    controller hint bar shows the editor's own buttons.
  - Not done: partner identity locks. Partners always play their own decks and you never edit them, so there is
    nothing to lock yet; it belongs with partner deck editing if that's ever added.

## Duel UX (from the Master Duel duel UI report)

Rules clarity first, an optional "why" layer second, spectacle on top and under the player's control. Skipped from
the report: phone/touch layouts (a Mac game), "why can't I activate this" reasons (the engine doesn't say; guessing
would mislead), a full cause-and-effect engine, telemetry.

- [x] **16. Response modes.** Auto / On / Off for when the game stops to ask "Chain?", shown on the board with what
  each means, plus hold-to-ask for one window.
  - Done: a "Chain: AUTO" pill under the phase button (click to cycle) and a Settings row; Auto (the default) asks
    when the opponent activates, summons or attacks, or when the engine marks one of your effects as suited to the
    moment; On asks every time; Off only for your own triggers after your plays. Hold Shift / LT to be asked. In 120
    scripted duels each: On asks at every chance, Auto at about half, Off at about 2%, no crashes.
- [x] **17. Chain panel and an explaining log.** The whole chain (each link's card, owner, target) in resolution
  order; a log grouped by chain that says what negated what; "-800 ATK" floaters; "Opponent is thinking";
  exact costs in prompts.
  - Done: a chain panel at the top right (newest link on top; owner color, card, targets or effect line, and its
    state as a word and symbol: activating, waiting, resolving, done, negated). The log numbers links, names
    targets, says "Solemn Judgment (Chain Link 2) negated the activation of Pot of Extravagance (Chain Link 1)" and
    sums up each chain. ATK/DEF changes pop up over the monster. "Opponent is playing..." / "Resolving..." while you
    wait. Selections say what they're for: "(to activate Book of Moon)" or "(for Dust Tornado's effect)". All from
    the engine's public messages (targets are named only when face-up or in a Graveyard).
- [x] **18. Presentation settings.** Full scenes the first time, shorter repeats; switches for cut-ins, shake,
  flashes and summon scenes; reduced motion; card text size.
  - Done (Settings > Duel presentation): summon scenes and spotlights play in full the first time in a duel and
    quicker after (no title), an ace's cut-in once per duel; switches for summon scenes, cut-ins, shake and flashes;
    Reduced motion turns those and the drifting particles off at once; card text Normal / Large / Larger for card
    effects, the log, dialogs and the reading view. The settings screen scrolls. -reducedmotion and -textsize n
    override them for one run (checks).
- [x] **19. Replays that teach.** A timeline that jumps to a turn, chain or big LP swing; boss Replay Lessons next to
  their recipes.
  - Done: Moments (in the replay controls) lists every turn, chains of 2+ links or with a negation, hits of 1000+
    and ace summons, found in the background; clicking one jumps to just before it (checked to the exact position).
    Every recipe in the Recipe Book has "Watch <owner> play it": a Replay Lesson made once (about 10 seconds, then
    kept in mods/replays/lessons) where the owner plays the deck against another duelist of that circuit, opened just
    before their key turn with a banner and a way back.
- [x] **20. Name the card behind a negation at resolution.** When an effect is negated as it resolves (Effect
  Veiler, Infinite Impermanence), the core only says "Chain Link 1's effect is negated", not by what.
  - Done: credited only on firm evidence: exactly one later link of the same chain resolved, its text negates, and it
    targeted that link's card where it was activated. The log then says "The effect of Altergeist Marionetter (Chain
    Link 1) is negated: Effect Veiler (Chain Link 2) targeted it". Anything less certain keeps the old wording.

## More from the Master Duel report

- [x] **21. Status badges.** A clear badge on cards whose effects are negated (Effect Veiler, Skill Drain...) and
  similar states, read from the engine's own card status, not guessed.
  - Done: the board reads each card's status and counters from the core. "⊘ NEGATED" with a dimmed face on face-up
    cards whose effects are negated (STATUS_DISABLED, the flag EDOPro uses); "◷ set this turn" on set Traps that
    can't be activated yet; a ⬢ counter badge. ("Unaffected" and similar aren't in the core's status, so they're
    left out rather than guessed.)
- [x] **22. Sound that knows what matters.** Music dips under negations, big hits and the final blow; distinct
  negated / destroyed / failed cues; a cooldown so long combos don't repeat one sound every second; a UI volume;
  a tension layer at 1000 LP or when an ace lands, crossfaded.
  - Done: the music dips to 35% under negations, big hits and the win/lose stinger and comes back over half a
    second; a negated effect sounds (and reads, "EFFECT NEGATED") different from a negated activation; the same
    sound repeated within 0.6 s gets quieter each time (to 40%); an Interface sounds volume; a tense duel loop
    (faster, darker, same key) crossfades in while someone is at 1000 LP or less or an ace is face-up.
- [x] **23. Pin a card.** Keep a card's detail on screen while hovering others (Master Duel's pinnable info).
  - Done: a Pin button on the card detail. While a card is pinned, its detail stays where it is and every other card
    you hover (or the duel shows) opens in a second panel beside it; Unpin closes the second panel.
- [x] **24. A chain tutorial.** A short guided duel: a 3-link chain resolving backwards, Auto vs On catching an
  end-of-turn window, and chaining to your own effect; told as an early campaign scene.
  - Done: Gus's Chain Lessons (Lessons, top of the campaign screen; Gus mentions them after the Beginner Circuit).
    Three short duels from a set board, Gus scripted to play only what the lesson needs, each judged from the core's
    messages with a hint banner that follows along:
    1. *Last in, first out:* Reinforcement of the Army, Gus chains Ash Blossom, you chain Called by the Grave.
    2. *Catch the End Phase:* keep Jar of Greed set and draw at the end of Gus's turn. On Auto the game never stops
       (nothing is played, and Jar of Greed has no End Phase timing), so it fails and tells you to turn Chain ON.
    3. *Chain to your own card:* Dark Hole, with Interdimensional Matter Transporter chained to save your Blue-Eyes.
  - Found while building it: Auto already stops for cards the scripts flag for a moment (Mystical Space Typhoon,
    Dust Tornado, Book of Moon in the End Phase), which is why lesson 2 uses a card without one. In a lesson the
    Chain pill starts on Auto and changes don't touch your saved setting. `--lesson-test` plays each lesson right
    and wrong.
- [x] **25. Not only color.** A second signal beside color for whose card it is and what can act (an owner mark, a
  glow or icon), and labels that stay readable on busy mats and map art.
  - Done: cards you can act on get a gold ▶ tab above them, besides the gold border (Settings > Playable card tabs
    turns the tabs off), and playable hand cards sit higher in the hand. Chain links say YOU / OPP under their number, besides the colored stripe. Raised and lowered
    stats get ▲ / ▼, not only blue and red. Pile counts (Deck, GY, Banished, Extra) sit on a dark plate over the mat
    art. Already covered: opponent field cards face them, zones you can choose say "Place here", the map's name tags
    have a backing, and locked and cleared circuits show lock and crown icons.
- [x] **26. Smaller, optional.** Replays: highlight what changed after a jump; replay codes that open at a moment;
  show both hands or one. Controller: remappable buttons; focus returns to the right card after Cancel.
  - Done: after jumping to a moment or the next turn, cards that arrived or changed glow cyan with NEW / CHANGED
    tags, empty zones show LEFT, piles and LP get "+2" / "−2400 LP" tags, and the log says what changed since where
    you were; the marks clear when the replay plays on. Copy code in the middle of a replay makes a code that opens
    there (`YGOR1:...@300`; older codes still open at the start). A Hands pill while watching shows both hands, one,
    or the other. Settings has a Controller section: click an action, then press the button you want (taking
    another action's button swaps them), plus Reset; the on-screen hints follow. Back closes a card's menu and puts
    the cursor back on the card, and cancelling a target or zone pick returns it to the card you were using.

## Play layout (no side panel)

- [x] **27. One duel screen for everything.** Campaign, Free Duel, Tag Duel, lessons and replays all use the
  full-width board. The old side panel (matchup picker, decision list, text log, test buttons) is the developer
  panel: F12, the gear, or `-devpanel`; the checks written against it still use it.
- [x] **28. Card detail only when you need it.** It slides in while you point at a card (on the side away from the
  pointer), stays a few seconds when the duel shows one, and slides away; Pin keeps it.
- [x] **29. A history strip and drawer.** The last six actions as tiles on the right edge (card art, YOU / OPP, an
  icon for what happened; hover for the card and a sentence). History opens the whole duel by turn.
- [x] **30. Messages for what you'd miss.** A short banner for the opponent's activations (what they answer, what
  of yours they target) and for your cards negated, destroyed or banished by them. Not for your own routine plays.
- [x] **31. A slim top bar.** Menu, the duel's title, the turn and phase, History and a gear (speed, Chain prompts,
  the text log, Save replay, Settings, the developer panel). Watching, the replay controls float bottom right.
- [x] **32. A Free Duel setup screen.** Versus style: each side's duelist (or you) and deck with arrows, Random
  opponent, Duel! or Watch. Free and tag duels end with Rematch / New matchup / Menu on the result banner.

## From the UI and UX history (docs/dev/UI-UX-HISTORY.md)

- [x] **33. A card-term glossary.** Master Duel's most repeated criticism is unexplained card text.
  - Done: 51 terms (Quick Effect, soft and hard once per turn, missing the timing, negate the activation vs the
    effect vs its effects, target, banish vs destroy vs send, the summon types, Tuner, detach, points to...) are blue
    and bold in the card detail at their first use, and explain themselves in a tooltip when you point at them. Terms
    inside quoted card names are left alone. A "?" on the type bar explains card-text punctuation (colon: condition;
    semicolon: cost or target). Settings > Card term help turns it off. `--glossary-test` runs it over every card
    (about 4 terms a card).

- [x] **34. Piles you can open and use.** (Asked for while playing.)
  - Done: double-click any Graveyard, banished pile or your Extra Deck to see its cards; cards banished face-down
    stay face-down for everyone (in the viewer and on top of the pile). A click on a pile you can use right now asks:
    View, Summon (Extra Deck) or Activate (GY, banished), Cancel. Summon / Activate shows only the cards you can use
    there, with Cancel to go back. Card menus end with Cancel, and Esc closes any menu, viewer or picker.
    `-pilecheck` sets up a board and checks each path.

- [x] **35. Duel controls from play.** (Asked for while playing.)
  - Done: the turn disc opens a phase bar (Master Duel's "Select Phase to Change to"): the six phases, the ones you can
    reach lit and the current one marked; one click goes there (Main 2 from Main 1 passes through the Battle Phase, End
    skips it). B and E, and the controller's Y, work too. Right-click a card (or press P) to pin it, so you never move
    the mouse over other cards to reach the Pin button; the pinned card gets a pin badge. Attacks get an arrow:
    pointing at a target while choosing draws it from your attacker with "3000 vs 1600 DEF" (green when you win, red
    when you lose); a declared attack (yours in blue, the opponent's in red) keeps its arrow until the attack is over.
    `-controlscheck` plays turns and checks each of these.
36. **Check Field (done, from Link Evolution).** Hold Space (or the controller's RT) and whatever covers the board, a
    question, the target banner, a picker or a menu, fades so you can look at the field; let go and it's back. The ▼ on
    a dialog or the target banner keeps it hidden until you click the pill at the top, press Esc, or the next decision.
37. **Spell Speed and genre tags in the card detail (done, from Link Evolution).** Under the type bar: the card's Spell
    Speed (gold, from its type and "(Quick Effect)" wording), then what it does (Negate, Search, Special Summon,
    Banish...) from the card database's search categories, carried in the card pack (version 3). Each chip explains
    itself on hover. `--tags-test` checks well-known cards; `-controlscheck` checks the chips and Check Field.
38. **Column guides (done).** While the card you're reading or placing is about columns (114 cards: Mekk-Knights,
    World Legacy, Infinite Impermanence...), the five columns show on the field with how many cards each holds. For
    "if 2 or more cards are in the same column" the qualifying columns glow gold; for the Mekk-Knights, which summon
    themselves into that column, only those where your Main Monster Zone is free ("2 · summon here"). Pin a column card
    to keep them up. `--tags-test` checks the counting; `-controlscheck` checks a board.

39. **Readable at a glance (done, from Link Evolution).** Pile counts big on the piles themselves (the name small
    underneath); empty zones and piles show a faint mark instead of a word (Field Zone: peaks and a sun; Extra Monster
    Zone and Extra Deck: a hexagon; GY: a grave cross; banished: a crossed card); monsters in your hand show their Level
    or Rank big on the left edge, the part still in view when the hand fans out.

40. **Skip to my turn (done).** During the opponent's turn a button under the status line (S, or the controller's Y)
    plays the rest of their turn at once, without animations. It stops for any decision of yours (a chance to chain)
    with the board shown, and goes on once you answer. When your turn comes a recap at the top lists what they did,
    and the board marks what changed. Your own duels only (not lessons, replays or tag duels).

41. **Choosing the order (done).** Several effects of yours activating at once, optional or required, are offered
    together in the chain dialog: each card says the Chain Link it would become, and a line explains that the first
    one picked becomes Chain Link 1 and resolves last. Cards placed on the Deck in an order you choose (Spellbook
    Organization, Card Advance) open an order screen: click them first to last (each shows "on top", "2nd from the
    top"... or, for a chain order, "Chain Link 2 · resolves 1st"), click one to take it back, Start over, or keep the
    default order. These used to be answered for you. `-ordercheck` plays Dark Hole on Sangan and Witch of the Black
    Forest, then Spellbook Organization, and checks the chain and the Deck.

## Open questions

- **Feel:** Master Duel (flashy), Tag Force (cozy, character-driven), Duel Links (fast, clean), or a mix?
  Screenshots or videos of favorite moments help, summon animations and menus most.
- **Expressions (#5):** can the AI art workflow make 3-4 expressions per character?
- **Speed vs. show:** full animations always (with a setting to shorten and a click to skip), or short by default
  with full ones only for big moments?
- **Controls:** mouse only, or plan for a game controller (affects #4 and #9)?
- **Look:** a logo, title art or font for Duel Circuit?
