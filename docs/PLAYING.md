# Playing Duel Circuit

This guide covers duels, the deck editor, controls and settings. For the campaign, the shop and the post-game, see
[CAMPAIGN.md](CAMPAIGN.md). Back to the [README](../README.md).

- [The main menu](#the-main-menu)
- [Playing a duel](#playing-a-duel)
- [Chains](#chains)
- [Reading the board](#reading-the-board)
- [Tag duels](#tag-duels)
- [Free Duel](#free-duel)
- [The deck editor](#the-deck-editor)
- [Searching for cards](#searching-for-cards)
- [Controls](#controls)
- [Settings](#settings)
- [Animations](#animations)
- [Rarity and foils](#rarity-and-foils)
- [Replays](#replays)
- [Known issues (full list)](#known-issues-full-list)

## The main menu

| Menu item | What it does |
|---|---|
| **Campaign** | Your story mode. It shows where you are, for example "Continue: Rookie Cup (1/3 won)". |
| **Free Duel** | Any deck against any duelist you've unlocked, or watch two CPU duelists duel. |
| **Tag Duel** | Two against two, with any decks. |
| **Deck Editor** | Build decks with every card. |
| **Replays** | Watch, keep and share your past duels. |
| **Settings** | Sound, speed, motion, foils, card pictures and controller buttons. |
| **Import Cards** | Add your own card pictures from a zip or picture files ([CARD-PICTURES.md](CARD-PICTURES.md#import-from-the-game)). Shut during a campaign duel. |
| **About** | Licenses, credits and where to get the source code. |
| **Quit** | Close the game. |

Use the mouse, the arrow keys and **Enter**, or a controller's d-pad and **A**.

In this guide, **CPU duelists** are the computer players.

## Playing a duel

### Who goes first

Every duel starts with a coin toss.

- **Win the toss:** choose **Go first** or **Go second**.
- **Lose the toss:** your opponent chooses.
- The toss screen gives you a tip when your deck usually does better going second. If the game has tested that match,
  it shows the result, for example "going second won 87% of test games; going first won 37%".

### Doing things with your cards

Duel Circuit plays like Master Duel: **click a card to act with it.**

1. Click a card in your hand or on the field.
2. A small menu opens beside it with what it can do now: **Summon**, **Set**, **Activate**, **Attack**, **To Defense**...
3. Click the action you want.
4. If the game needs a target or material, the cards you can pick glow. Click one.
5. When you place a card, the zones it can go to light up ("Place here"). Click one. This matters for decks that care
   about columns, like Mekk-Knights.

**More tips:**
- Cards you can use right now have a gold border and a gold ▶ tab. (Turn the tab off with **Playable card tabs** in
  Settings.)
- Click a Graveyard, your banished cards or your Extra Deck to look through them.
- Point at any card to read it in the side column. Click its picture for a bigger view.

**Changing phase:**
1. Click the **turn ring** on the center line, right of the board.
2. A **Change phase** panel opens. Click the phase you want.

Keys: **B** goes to the Battle Phase. **M** goes from the Battle Phase to Main Phase 2. **E** ends your turn.

**On your opponent's turn,** a **Skip to my turn »** button shows on the board (key: **S**). It plays their turn at
full speed with no animations. It still stops when you can respond. When your turn comes, a list shows what they did.
(Not in tag duels.)

## Chains

When you can respond to something, a **Chain?** dialog opens over the field:

- Its title says what was just activated, for example "Your opponent activated ... Chain another card or effect?"
- It shows your cards that can respond. Click one to chain it.
- Click **Don't chain** to let it go.

While a chain builds, the top right shows each effect being activated, with its chain link number. Each card in the
chain gets a numbered badge. The chain then resolves from the last link back to the first.

### When the game asks "Chain?"

The **Chain** pill sets how often the game stops to ask. It sits beside your Deck, at the bottom right of the field,
and reads "Chain: AUTO" (or ON, or OFF). Click it to change it, or change **Chain prompts** in Settings.

| Setting | The game asks you... |
|---|---|
| **Auto** (default) | when your opponent activates, summons or attacks, or when one of your effects suits the moment |
| **On** | at every chance |
| **Off** | only for your own cards' effects right after your plays |

**Want to respond at a moment the game skips?** Hold **Shift** (or **LT** on a controller). The game asks you at the
next chance, whatever the setting.

New to chains? In the campaign, **Gus's Lessons** teach them in three short duels (see [CAMPAIGN.md](CAMPAIGN.md#guss-lessons)).

## Reading the board

**Your panel and your opponent's.** Each duelist has a panel at the left edge: yours at the bottom (blue), theirs at
the top (red). It shows the duelist, their name, their Life Points (LP) and their Deck.

**Life Points.**
- The LP bar has 8 segments of 1,000 LP. A white trail shows what was just lost.
- Each change shows beside the number with a symbol: a sword for battle damage, a burst for effect damage, a coin for a
  cost, a heart for LP gained.
- The bar warns you without relying on color: amber at half, red with stripes at a quarter, and at 1,000 or less it
  pulses and reads "! Low".

**Turns and phases.**
- Phases and turns sweep in as banners.
- The turn ring on the center line shows the turn and phase: blue on your turn, red on theirs.
- The panel of the player whose turn it is reads "Your turn" or "Their turn"; the other one dims.

**Monsters.** ATK or DEF higher than the printed value shows in blue. Lower than the printed value shows in red.

**Field Spells.** While one is face-up, its artwork fades in over its owner's half of the field (if you've added card
pictures).

**Your set cards** carry a small name tag, and you can still read them by pointing at them.

**Look at the whole field.** Hold **Space** (or **RT**) to hide whatever covers the board (a question, a menu, a card
list). Let go to bring it back.

**The icons over the mat:**

| Icon | Where | What it does |
|---|---|---|
| **Menu** | top left | Back to the main menu |
| **History** | top right | The duel log: everything that happened, in words |
| **Gear icon** | top right | Duel speed, Chain prompts, the text log, **Save replay** and Settings |

**Duel speed.** The **+** and **−** buttons change how fast the duel plays.

## Tag duels

Two against two, the Tag Force way. Choose **Tag Duel** in the main menu, or play the Tag Cups in the campaign.

**The rules:**
- Each team shares **one field, one Graveyard and 8,000 LP**.
- Each duelist has their **own Deck, hand and Extra Deck**.
- Turns go in this order: you, one opponent, your partner, the other opponent. Then it starts again.
- Only the duelist whose turn it is can play or respond. Their hand is the one in play.
- You play your turns; your partner (a CPU) plays theirs.
- The duelist in play is named on the board and in the log.

**Setting one up:** the Tag Duel screen has four seats (you, your partner and two opponents). Pick a duelist and a deck
for each.

## Free Duel

Play any deck against any duelist you've unlocked. Some duelists are open from the start. The rest join as you beat
them in the campaign. The No Limits duelists never appear in Free Duel. You can also make two CPU duelists duel each
other and watch.

## The deck editor

Open **Deck Editor** from the main menu to build with every card. In the campaign, **New deck** and **Edit deck** open
it with only the cards you own (see [CAMPAIGN.md](CAMPAIGN.md#your-decks-in-the-campaign)).

The screen has three parts:

| Part | Where | What's in it |
|---|---|---|
| **Search** | right | Find cards (see [Searching for cards](#searching-for-cards)) |
| **Deck** | center | Your Main, Extra and Side Deck, with counts |
| **Tools** | left | The selected card, opening-hand odds, and test games |

### Adding and removing cards

| To do this | Do this |
|---|---|
| Add a card | Click it in the search results |
| Add a card to the Side Deck | Right-click it in the search results |
| Remove a card | Click it in your deck |
| Move a card between Main/Extra and Side | Right-click it in your deck |
| Move a card anywhere | Drag it. Drag it back onto the search results to remove it. |

Extra Deck monsters always go to the Extra Deck.

### The main buttons

The top row has **Save**, **New**, **Delete**, **Decks...** (open another deck), **Copy** and **Back**. Under it:

| Button | What it does |
|---|---|
| **Undo** / **Redo** | Takes back a change, or does it again |
| **Sort** | Sorts the deck |
| **Clear** | Empties the deck |
| **Test hand** | Draws a test opening hand (5 or 6 cards) |
| **Import** | Reads a deck you've copied: a YDKE link or a `.ydk` file's text |
| **Import .ydk** | Opens a `.ydk` file |
| **Export** | Copies the deck as a YDKE link, to paste anywhere |

A **YDKE link** is the deck-sharing link that EDOPro and deck websites make. A **`.ydk` file** is a deck file in the
format EDOPro uses.

In the campaign, **Import** and **Export** are replaced by **Auto-fix**, **Build** and **Recipe** (see
[CAMPAIGN.md](CAMPAIGN.md#your-decks-in-the-campaign)).

**Bringing in an EDOPro deck:**
1. In the deck editor, click **Import .ydk**.
2. Pick the `.ydk` file in the window that opens.

Or copy the file's text instead:
1. Open the `.ydk` file in a text editor (Notepad on Windows, TextEdit on a Mac).
2. Select all the text and copy it.
3. In the deck editor, click **Import**.

A YDKE link works the same way: copy it, then click **Import**. Decks you save are `.ydk` files, so EDOPro can open
them too.

**Saving over a deck:** if your changes would replace a saved deck with a very different one, the editor asks:
**Overwrite it**, or **Save as new**.

**Built-in decks** can't be changed. Saving one makes your own copy with the same name.

### Ban lists

The editor checks your deck as you build: 40 to 60 cards in the Main Deck, up to 15 in the Extra and 15 in the Side,
and no more than 3 copies of a card. Choose a ban list and it checks that too. Limit badges show on the cards.

You can pick today's lists, or dozens of historic TCG lists from 2004 to 2026. A historic deck switches to its own era's list
when you open it.

### Card versions

Some cards have more than one look or wording:

- **Alternate artworks:** arrows like `< 3 of 16 >` beside the picture switch between them.
- **Original text:** cards that were changed later (errata, or the Goat-format versions) have a switch between
  **Current text** and the original.

Your choice applies to every copy of that card in the deck, and to the copies you add after. All versions count as
the same card for limits and for your collection.

### Planning your opening

- **Opening hand odds** (left): select a card and click **Mark starter**. Do this for each card that can start your
  combo. The editor shows the exact chance to open at least one, going first and going second.
- **TEST AGAINST A BOT** (left): choose a CPU duelist and their deck, then click **Play 100 games**. Your deck plays
  100 quick games against them, and the editor shows your win rate going first and going second.

## Searching for cards

Type in the search box at the top of the search panel. Everything you type must match.

### What you can type

| Type this | To find |
|---|---|
| `destroy` | Cards with "destroy" in the name or text |
| `"destroy all"` | That exact phrase |
| `-token` | Leaves out cards with that word. A `-` in front works on anything below too: `-ritual` leaves out Ritual cards. |
| `lv8`, `rank4`, `link2`, `scale5` | Monsters with that Level, Rank, Link rating or Scale |
| `atk>2500`, `def<=1000` | Monsters by ATK or DEF |
| `monster`, `spell`, `trap`, `tuner`, `synchro`, `xyz`, `link`, `quickplay`... | Card kinds |
| `dark`, `dragon`, `spellcaster`... | Monsters with that Attribute or Type, or cards with that word in the name |
| `handtrap`, `searcher`, `draw`, `removal`, `negate`, `wipe`, `revive` | Cards by their role |
| `does:banish`, `does:search`... | Cards by what they do (see below) |
| `@"burning abyss"` or `@burningabyss` | Every card in an archetype |
| `name:dark` or `$dark` | The word in the **name** only |
| `text:dragon` | The word in the **card text** only |
| `pend:destroy` | The word in a **Pendulum Effect** only |
| `dragon or spellcaster` | Either one. `lv4 dragon or spellcaster` means Level 4, and Dragon or Spellcaster. |
| `legal` | Cards legal under the chosen ban list |
| `limit:0`, `limit:1`, `limit:2`, `limit:3` | Forbidden, Limited, Semi-Limited, Unlimited |

**Spelling doesn't have to be perfect.** `blueeyes`, `blue eyes` and even `bleu eyes` find Blue-Eyes White Dragon. When
no card has your words in its name, the game says "showing close names".

**Looks in:** the switch at the right of the box chooses where plain words look: **Name + text**, **Name** or **Text**.

### Quick filters

Under the box:
- **Chips:** Hand trap, Searcher, Draw, Sp. Summon, Revive, Removal, S/T removal, Negate, Banish, Board wipe. Click to
  turn one on or off.
- **Pickers:** card kind, Attribute, Type, Level, and **Sort**. Click for the next choice; right-click for the one before.

Every filter that's on shows in a row above the results. Click its **×** to remove it.

### More filters

Click **More** (at the end of the pickers row) to open a bigger panel:

- **Archetype:** type two letters or more and pick from the list.
- **Limit:** Forbidden, Limited, Semi-Limited, Unlimited, On the list, or **Legal here**.
- **Look** (campaign): cards you own as a Foil, Slabbed, Gem Mint or Worn copy, or cards with an Alternate art.
- **What it does:** 30 chips in groups (Removal, Cards and resources, Summoning, Battle, Life Points, Other). Point at
  one to see what it means.
- **Match all / Match any:** cards must do all the things you picked, or any one of them.

**Clear these** turns off everything in the panel. Close it with **Close** or **Esc**.

**More like this:** in the card detail, click a card's "what it does" chip or archetype chip to search for it.

### Which cards to search

The scope button switches between:

| Scope | Shows |
|---|---|
| **All cards** | Every card |
| **Owned only** (campaign) | Cards you own |
| **Favorites** | Cards you've starred |
| **Seen in duels** | Cards your opponents played in your last 20 duels, newest first. Pick one duel, or all. |
| **Recently viewed** | The last 40 cards you looked at in the editor |

"Seen in duels" answers the question "what was that card the boss just played?"

## Controls

### Mouse and keyboard

| Do this | How |
|---|---|
| Act with a card, pick a choice | Left-click |
| Second action (for example, add a card to the Side Deck) | Right-click |
| Move a card in the deck editor | Drag it |
| Battle Phase / Main Phase 2 / end your turn | **B** / **M** / **E** |
| Skip to my turn (on your opponent's turn) | **S** |
| Be asked "Chain?" at the next chance | Hold **Shift** |
| Look at the whole field | Hold **Space** |
| Close a window, go back | **Esc** |
| Move through menus and the shop | Arrow keys, then **Enter** (or **Space**) |
| Next line in a story scene | Click, **Space** or **Enter** |

### Controller

Xbox, PlayStation and Switch Pro controllers work. Plug one in and move the left stick: a cursor appears, with a line
of button hints along the bottom. Move the mouse and the cursor hides again.

| Button (Xbox names) | What it does |
|---|---|
| Left stick | Move the cursor (faster the further you push) |
| D-pad | Jump to the nearest button or card in that direction |
| **A** | Click. Hold **A** and move to drag a card. |
| **B** | Back: say no, cancel or close |
| **X** | Right-click |
| **Y** | The screen's extra action. In a duel: on your turn the **Change phase** panel, on your opponent's turn **Skip to my turn**. In the deck editor: favorite a card. |
| **LB** / **RB** | Previous / next page |
| **LT** (hold) | Be asked "Chain?" at the next chance |
| **RT** (hold) | Look at the whole field |
| **View** (the small button with two squares) | In a duel: explain the next blue term in the card text. In the deck editor: open **More** filters. |
| **Start** | Pause a duel you're watching |

On a PlayStation controller: **A** is Cross, **B** is Circle, **X** is Square, **Y** is Triangle, **LB**/**RB** are
L1/R1, **LT**/**RT** are L2/R2, **View** is Share or Create, and **Start** is Options.

**To change a button:** open **Settings**, scroll to **CONTROLLER**, click an action, then press the button you want.
**Reset buttons** puts them all back.

## Settings

Open **Settings** from the main menu, or from the gear icon in a duel. Click a setting to change it.

**Game**

| Setting | What it does |
|---|---|
| Foils | **On**, **Showcase** or **Off** (see [Rarity and foils](#rarity-and-foils)) |
| Foil intensity | How strongly foils shine: Subtle, Standard or Vivid |
| Card tilt | The big card view tilts toward the pointer |
| Card grades | **Full** shows wear on your cards; **Label only** shows the condition in words only |
| Prized moments | A Gem Mint Secret Rare or better of yours sparkles and chimes when it lands face-up, once a duel |
| Duel speed | How fast duels play |
| Chain prompts | Auto, On or Off (see [Chains](#chains)) |

**Duel presentation**

| Setting | What it does |
|---|---|
| Repeated animations | Big animations play in full the first time in a duel and quicker after |
| Reduced motion | Turns off shake, flashes, summon scenes, cut-ins and drifting particles in one go |
| Summon scenes | The special scene before Fusion, Synchro, Xyz, Link and Ritual Summons |
| Ace cut-ins | A big banner when an ace monster arrives (Level 8 or higher, or 3000 ATK or more) |
| Screen shake | The board shakes on big hits |
| Flashes | Full-screen color flashes on damage and the final blow |
| Slow down for combos | When a headliner (a duelist famous for one big combo) plays that combo, the duel slows to Normal speed so you can follow it |
| Card text size | Bigger text for card effects, the log and dialogs |
| Card term help | Card terms (Quick Effect, once per turn...) in blue; point at one to see what it means |
| Playable card tabs | A gold ▶ tab above every card you can play now |

**Sound and screen**

| Setting | What it does |
|---|---|
| Sound effects, Music, Interface sounds | Volume for each, from off to 100% |
| Reactive music | The music follows the duel: the boss's theme, more drive at low LP, a heartbeat when an attack could end it |
| Window | Full screen or a window |
| Card pictures | How many pictures the game found, and from which folders ([CARD-PICTURES.md](CARD-PICTURES.md)) |
| Prefer style | The art style to use when you have several ([CARD-PICTURES.md](CARD-PICTURES.md#art-styles)) |

## Animations

Everything that happens in a duel is animated before the duel moves on:

- Cards slide between hand, field and piles. Your draws flip face-up on the way to your hand.
- Activated cards are spotlighted in the middle of the mat.
- Big summons burst onto the field. Extra Deck and Ritual Summons get their own scene: a Fusion vortex, Synchro rings,
  an Xyz galaxy, Link Arrows lighting up, a Ritual circle.
- Attackers lunge at their target. Destroyed cards shatter; banished cards fade away in pale light.
- Direct attacks get a big "DIRECT ATTACK". The winning hit gets "FINAL BLOW!".
- Duelists react on their panels with a speech bubble when something big happens.

Too much? Turn on **Reduced motion**, or turn off single effects, in [Settings](#settings). You'll still see
everything that happens.

## Rarity and foils

Cards with a picture are drawn with their rarity's finish, like the real cards. There are 26 rarity finishes (the plain
Common finish is one of them), for example:

| Rarity | How it looks |
|---|---|
| Rare | Silver name |
| Super Rare | Shiny art |
| Ultra Rare | Shiny art, gold name |
| Secret Rare | Diagonal foil lines on the art, rainbow-silver name |
| Ultimate Rare | Raised-looking art |
| Ghost Rare | Silvery, ghostly art |
| Starlight Rare | Foil over the whole card |
| Quarter Century Secret | Like Starlight, with gold letters |

Move the mouse (or the controller cursor) near a card and the light follows it. Otherwise a glint sweeps across now and
then.

**The Foils setting:**
- **On** (default): in the campaign, each card shows your best copy. Elsewhere, its rarest classic printing (up to Secret Rare).
- **Showcase:** each card's rarest printing of all.
- **Off:** plain cards.

In the campaign every copy you own also has a grade and wear. See [CAMPAIGN.md](CAMPAIGN.md#every-copy-is-its-own-card).

## Replays

The game records every duel and keeps your **last 30**. You can watch them again, keep them, and share them.

**In the Replays menu:**
- **Watch** a duel.
- **Keep** a recent duel so it isn't replaced by newer ones.
- **Copy code** to share a duel. A code starts with `YGOR1:` and is about 1,000 characters of text.
- To watch a duel someone shared, click **Paste** (or paste the code into the box), then **Watch code**.

**During a duel:** the gear icon → **Save replay** keeps the duel so far and copies its code.

**While you watch:**
- Both players' hands are shown.
- Pause, step, and change the speed as in any duel.
- The bar at the bottom has **< Back** (one step back), **Next turn** (skip ahead), **Moments**, **Copy code** and
  **Replays** (back to the list).
- **Moments** lists the key moments: each turn, every long chain or negation, big hits and ace summons. Click one to
  jump to just before it.

A replay made in a different version of the game (with different card data) may play out differently. The game warns
you.

## Known issues (full list)

This is an early version (0.1.0). Here is everything we know about. The [README](../README.md#known-issues) lists the
ones most likely to affect you.

### Platforms

- These are the first public builds, and the Windows one hasn't been tried on many PCs yet.
- Macs need Apple silicon (M1 or newer). Intel Macs and Linux aren't supported.
- The game has no publisher signature, so Windows and macOS warn you the first time you open it. See
  [Install and play](../README.md#install-and-play).

### Things that work better on a Mac for now

- On Windows, when the game window isn't in front, your first click only brings it to the front.
- On Windows, the window can be made very small. (The game still remembers the window's size on both.)
- On Windows, the game may not find your EDOPro pictures by itself.
  [Tell it where they are](CARD-PICTURES.md#windows-tell-the-game-where-edopro-is).

### Things that affect your game

- **Warrior's Charge** isn't legal at the Back Alley when you play the shop's way: it has 3 Reinforcement of the Army,
  and the Alley allows 1. Edit the deck before you go. (Under Historic Rules you get the 2011 version, which has 1.)
- **No Limits** in the Back Alley is locked ("Coming soon").
- Two Back Alley opponents aren't in the game yet.
- Difficulty isn't tuned yet. The early circuits are easy.
- Every duel uses today's game rules (Master Rule 5). Historic formats use their old ban list, but not their old game
  rules.
- Don't open your save in an older version of the game: it can lose the parts it doesn't know. Copy your `mods` folder
  first if you want to try one.
- We haven't measured how the game runs on slow computers yet.

### Bugs

- Six renamed duelists and one redrawn one (Professor Hollis) still use their old portraits.
- Three portraits are cut off on the right in story scenes (Ilse, Garunix and Volta Raijin).
- The Champion's Ladder's top duelist has no portrait yet.
- Deck shuffling isn't perfect in a few rare cases:
  - A card that takes from "your hand or Deck" shuffles your Deck even when you took the card from your hand.
  - A few effects that look through your whole Deck without showing you anything may not shuffle it afterward.
- CPU duelists still make a few known mistakes.
- The deck editor can pause for about 2.5 seconds the first time it opens big HD card pictures.
- Some foils and wear look weak on small cards on the board.
- A tilted card picture can be cut to its untilted shape.
- Cards still light up under the mouse while the game is in the background.

### Replays

- A replay made in a different version of the game (with different card data) may play out differently. The game warns
  you.

Found something that isn't here? [Open a bug report](https://github.com/fullstackjake/duel-circuit/issues/new/choose).
