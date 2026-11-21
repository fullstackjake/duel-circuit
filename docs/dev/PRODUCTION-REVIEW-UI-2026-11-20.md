# Production review: visual and interface (2026-11-20)

The visual and interface part of the production-value review. A review only: no code, art or data changed. The game
design and audio parts are in `PRODUCTION-REVIEW-GAME-2026-11-20.md` and `PRODUCTION-REVIEW-AUDIO-2026-11-20.md`.

**Question from the owner:** looking at the game as a player would, where does it look or feel less finished than it
could, and which changes raise the polish most for the effort?

## Verdict

With pictures installed, Duel Circuit looks close to finished: the painted map, the portraits, the logo, the code-drawn
summon scenes, the chain panel, LP plates and the Back Alley's brick-and-tape look are well above fan-tool level. **But
a player who installs the game as shipped has no card pictures, and that game looks much less finished than every
screenshot we have been judging it by.** All of today's check screenshots used the owner's EDOPro picture folder, which
the game finds on its own (CardArt.cs:582-590). Run with no pictures, the title screen is a wall of brown blanks, half the
main menu is empty, a pack is nine flat coloured slabs where an Ultra Rare looks like a Common, the versus screen shows
black boxes, slabs and the reading view are solid colour blocks, and the foil, rarity and grade systems disappear from
the cards. The second problem is text size: the shell is a fixed 1920x1080 layout, and at 1280x720 most secondary text
is 8-10 px. The third is that the reward moments (a pack, a circuit cleared, a win) are lists of text rather than events.

The three things that would raise it most:

1. **Design the no-picture card as the game's real card face** (one card renderer for every screen, with a code-drawn
   frame, attribute and level marks, a text box, and rarity and foil on the frame), and stop showing blanks anywhere.
2. **A legibility floor for 1280x720** across the shell and the board, with a small type scale and one display face.
3. **Ceremony for rewards**: a pack you open, a rarity you can see, a results screen that counts up and celebrates a
   circuit clear.

None of these needs new art from the owner. Fonts would need the owner's choice (open-licence only).

## How it was looked at

- **Existing evidence** (today's build): `build/qa-evidence/checks/<check>-<720|1080>/out/`: campaign (123 shots each
  size), layout, editor, search, focus, click, ydk at both sizes; board, pile, controls, tag, theme, shop at 1280x720.
  All of them show the owner's own pictures and card back (an EDOPro pics folder the game finds in the home folder),
  so they are the "with pictures" look.
- **My runs**, in a check copy `build/qa-review/` (`tools/qa_copy.sh`), one at a time, fresh `-mods`, `-mute`,
  `-hdpics off`, absolute paths. Every run but the first adds `-pics <empty folder>` so the game finds no pictures: the
  shipped look.

| Run | Size | Result | Shots |
|---|---|---|---|
| `menu-720` (`-menucheck`, with the auto-found pictures) | 1280x720 | 4 ok; the 2 sound steps FAIL muted, as expected | 6 |
| `menu-nopics-720` (`-menucheck`) | 1280x720 | same | 6 |
| `campaign-nopics-720` (`-campaigncheck`) | 1280x720 | 189 ok, 0 FAIL | 123 |
| `fx-nopics-720` (`-fxshots`) | 1280x720 | stills | 24 |
| `final-nopics-720` (`-finalcheck`) | 1280x720 | 46 ok, 0 FAIL | 112 |
| `ladder-nopics-720` (`-laddercheck`) | 1280x720 | 42 ok, 0 FAIL | 64 |
| `alley-nopics-720` (`-alleycheck`) | 1280x720 | 128 ok, 1 FAIL (step 17, a show route ending early on turn 3; gameplay, not looked into) | 99 |
| `about-720` (`-aboutcheck`) | 1280x720 | 30 ok, 0 FAIL | 9 |
| `import-720` (`-importcheck`) | 1280x720 | 53 ok, 11 FAIL: my `-hdpics off` stops the HD folder the check needs; the shots are still good | 11 |
| `mat-nopics-720` (`-matcheck`) | 1280x720 | 8 ok, 0 FAIL | 10 |
| `layout-nopics-1080` (`-layoutcheck`) | 1920x1080 | 54 ok, 0 FAIL | 27 |
| `replay-nopics-720` (`-replaycheck`) | 1280x720 | 16 ok, 0 FAIL | 6 |

  Output: `build/qa-review/checks/<run>/out/`. Below, `nopics/` means `build/qa-review/checks/campaign-nopics-720/out/`
  and `evidence/` means `build/qa-evidence/checks/`.
- **Code read**: `Ui.cs`, `UiTheme.cs`, `CardView.cs`, `MenuScreens.cs`, `CampaignScenes.cs` (versus, results),
  `StoryScreen.cs`, `ShopScreen.cs` (pack opening), `CardDetail.cs`, `CardFoil.cs` call sites, plus a count of text sizes
  and hard-coded colours across `unity/Assets/Scripts/`.
- **Not seen:** motion (stills only; animation judged from the code and `-fxshots`), hover timing, real mouse feel,
  sound, 1920x1080 without pictures beyond `-layoutcheck`, Windows, Steam Deck.

## Findings, ranked by how much they cost the game's look and feel

Severity: **High** = most players see it on the first screens or at the game's core moments; **Medium** = a common
screen looks unfinished or a group of players is hurt; **Low** = a detail. Status: CONFIRMED (seen in a shot, or plain in
the code) or PLAUSIBLE (from code only).

### 1. Without pictures, cards are blank slabs, and every screen has its own kind of blank (High, every new player; CONFIRMED)

The game ships no pictures (owner rule), so this is the look a new player gets until they import some, and many never
will. The text look is drawn as a fallback, not as a card:

- **Board, hand, editor, packs** (`CardView.cs:116-148`): a flat frame colour, the name at 12 (8 px at 720) in the top
  third, a stat line at the bottom, and nothing in between: no art window, attribute, level stars, type line or text.
  The name doesn't grow with the card, so big cards are worse: the activation spotlight is a 150x205 px orange slab with
  an 8 px name (`build/qa-review/checks/fx-nopics-720/out/fx-spotlight.png`, `nopics/campaign-prized-moment.png`).
- **Six other blanks**, each drawn its own way:
  - title screen: a wall of brown rectangles (`MenuScreens.cs:90-99`;
    `build/qa-review/checks/menu-nopics-720/out/menu-title.png`);
  - main menu: the right half is empty, the showcase card simply isn't drawn (`MenuScreens.cs:404-405`;
    `menu-nopics-720/out/menu-main.png`);
  - versus and Free Duel: black boxes with a white name (`CampaignScenes.cs:612`; `nopics/campaign-versus.png`);
  - the card detail and the reading view: a solid colour block, 240x350 px in the reading view
    (`nopics/campaign-catalog-reading.png`, `nopics/campaign-collection-editor.png`);
  - slabs: a solid orange card with no name at all (`nopics/campaign-slab-reveal.png`; also the Van,
    `evidence/theme-720/out/theme-back-alley-van-shelf.png`, where Synchro and Xyz slabs are blank white and black);
  - Locker posters and the duel plate: a big letter ("S") on a colour (`nopics/campaign-locker.png`,
    `nopics/campaign-duel-layout.png`); history rows and chain callouts: an empty dark square
    (`evidence/layout-720/out/layout-history.png`, `fx-nopics-720/out/fx-attack.png`).
- **Rarity, foil and grade vanish.** Foil is applied only to picture `RawImage`s (`CardView.cs:440-441`,
  `ShopScreen.cs:717`); the text look is an `Image` and gets none. In a pack, the Ultra Rare Vampire Lord is the same flat
  orange as the Commons; only an 8 px label under it and a soft glow behind differ (`nopics/campaign-pack-revealed.png`;
  the alley's Contraband Pack the same, `alley-nopics-720/out/alley-12-contraband-pack.png`). Settings > Foils,
  Foil intensity, Card grades and Prized moments do nothing visible for these players.
- **The docs promise more than it shows.** `docs/CARD-PICTURES.md:19` says the text look shows "name, stats and text in a
  clean card frame"; field, hand and pack cards show no text.
- **Our own evidence hides it.** Every screenshot in `build/qa-evidence/` and earlier reviews used the auto-found
  pictures, so checks and reviews have been judging the with-pictures game.

### 2. Text at 1280x720 is 8-10 px across the shell, not only in the deck editor (High, every 720 player; CONFIRMED)

- Every screen is a fixed 1920x1080 layout scaled to the window (`Ui.cs:86-88`): at 1280x720 everything is 2/3 size.
- Text sizes are literals: 40 different sizes from 8 to 140 in `Ui.Text` calls, and about 310 of the 670 calls are at
  11-16 (7-11 px at 720). No type scale, no floor.
- Seen at 720: circuit match rows' second lines and the footer (about 9 px, `nopics/campaign-circuits.png`); Settings help
  lines (10 px, `menu-nopics-720/out/menu-settings.png`); the duel's card detail panel (about 8 px,
  `nopics/campaign-duel-layout.png`); zone labels "GY", "Banished" (about 7 px); the bracket's Elo lines (6-7 px,
  `evidence/theme-720/out/theme-back-alley-bracket-16.png`); results unlock strips (8 px,
  `nopics/campaign-results-unlock.png`); shop odds lines (`nopics/campaign-shop.png`); text-look names (8 px).
- At 1080 the same screens read well (`evidence/campaign-1080/out/campaign-circuits.png`).
- TODO.md lists "Small text at 1280x720 (M1)" for the editor only.

### 3. The reward moments are lists, not events (High, everyone, at the core loop; CONFIRMED)

- **Packs:** no pack. Buying goes straight to nine card backs in a grid, with a tear sound and a deal-in scale
  (`ShopScreen.cs:1012-1020`; `nopics/campaign-pack-closed.png`). The side panel is empty until you hover ("Hover a
  revealed card to read it."). Hits glow faintly through their backs; once turned, a rarity is an 8 px word.
  Moth's line on the alley's Contraband Pack is "The odds are on the wrapper", and there is no wrapper; that screen
  also keeps Main Street's gold and navy inside the alley (`alley-nopics-720/out/alley-12-contraband-pack.png`).
- **Results:** static. The EXP bar is drawn at its final fill and DP don't count (`CampaignScenes.cs:899-960`). No
  portrait of the opponent, no picture of your best play, no duel facts (turns, damage, your ace). The DP panel is half
  empty on a plain win (`evidence/campaign-720/out/campaign-results.png`).
- **A circuit clear looks like a match win.** "Regional Qualifier cleared!", "Level 3!", "New circuit" and a gift are six
  identical 8 px olive strips under the same "Victory!" (`nopics/campaign-results-unlock.png`). The biggest beat of the
  campaign has no crown, trophy, or moment of its own.
- **Winning the whole game looks the same.** The Every Era final's last round gets the match-1 layout with two strips
  ("Every Era won! +3 Card Tokens...") (`build/qa-review/checks/final-nopics-720/out/final-50-round6-results.png`). A
  loss leaves the DP panel nearly empty (`final-36-round1-loss-results.png`).
  Reaching the top of the Champion's Ladder is the same screen headed "The top!", with ten progress chips squeezed
  until "Rung 1 WON" wraps to three lines (`build/qa-review/checks/ladder-nopics-720/out/ladder-38-top-results.png`).
- **The duel's end on the board:** a thin dark strip with "You win!" across the field
  (`evidence/campaign-720/out/campaign-result.png`).

### 4. One generic font for everything (Medium-High, everyone; CONFIRMED)

- The whole game uses Unity's built-in `LegacyRuntime.ttf` (an Arial-like sans) through one property (`Ui.cs:12`).
  Titles ("CAMPAIGN", "Victory!", "VS", "TAG DUEL", "SETTINGS") are the same face in bold, gold or white.
- Only the logo has character. The alley's stencil title font is wired (`UiTheme.cs:403-429`) but not supplied (TODO).
- This, more than colour, is what makes the shell read as a tool rather than a game: compare the title screen's logo
  with the "CAMPAIGN" header under it.
- The fix is cheap: one font property, plus the existing `UiTheme.Title` hook extended to Main Street.

### 5. The player has no face (Medium, everyone; CONFIRMED)

- Versus: the opponent gets a portrait, a title, a quote; "You" gets a name and empty space (the lower half of the panel
  is blank) (`nopics/campaign-versus.png`, `evidence/layout-720/out/layout-free-duel.png`).
- Tag setup: a grey word "YOU" where portraits go (`evidence/tag-720/out/tag-setup.png`); brackets: a "YOU" tile
  (`theme-back-alley-bracket-16.png`); the duel plate: the emblem, or a poster that is card art (a letter without
  pictures).
- Every screen is therefore lopsided toward the opponent, and the first screen of each duel says the least about you.

### 6. Empty areas on most shell screens (Medium, everyone; CONFIRMED)

- Main menu right half (finding 1). Campaign: the right panel below "Your deck" is about 160 px of nothing at 720
  (`nopics/campaign-circuits.png`). Versus: the lower half of the You panel. Results: half the DP panel. Story: the
  dialogue box is 1600x340 for usually one line, two-thirds empty, and narration keeps the 280 px portrait indent with no
  portrait (`StoryScreen.cs:82-93`; `evidence/campaign-720/out/campaign-story-partner.png`). Pack opening: the empty side
  panel. The Van's detail panel: one paragraph in a full-height box.
- The Format Library: the right half is an empty panel until you pick a cup, with no prompt
  (`nopics/campaign-library-shelf.png`).
- Each is a small thing; together they make screens look laid out on a grid that wasn't filled in.

### 7. The duel board: small cards in a wide screen, a seam through the mat, a tiny detail panel (Medium, everyone; CONFIRMED)

- The field uses about 45% of the width; field cards are about 57x80 px at 720 and 86x120 at 1080
  (`evidence/layout-720/out/layout-board.png`). The roadmap (item 11) explains why: a flat 16:9 board is limited by
  height.
  The space beside it holds only the plates and the Chain pill.
- Two mats meet at a hard horizontal line: the opponent's art on top, yours below (`layout-1080/out/layout-board.png`).
  It reads as two pictures taped together.
  In the alley the seam is pink brick against the gold starfield (`alley-nopics-720/out/alley-7-duel.png`).
- The card detail at the right is compact: 8 px text at 720 (`nopics/campaign-duel-layout.png`).
- The hand at rest is cut by the screen's bottom edge; cards rise on hover (by design).
- What works: code-drawn summon scenes (`fx-nopics-720/out/fx-scene-xyz.png`, `fx-scene-link.png`), the turn and
  phase banners, the attack arrow with "3000 vs 1600 DEF", the chain panel, LP plates. These need no pictures.

### 8. Settings: one long list, cosmetics first, buttons that don't say they cycle (Medium-Low, everyone; CONFIRMED)

- About 30 rows in one scroll (`MenuScreens.cs:549-635`). The first five are Foils, Foil intensity, Card tilt, Card grades
  and Prized moments; volume is about row 18 and Window about 22 (`menu-nopics-720/out/menu-settings.png`).
- Each value is a plain button that cycles when clicked: no arrows, no segmented look. Volume steps 0-100 by 20 and wraps,
  so going down one step is five clicks (`MenuScreens.cs:525`).
- Controller buttons are named in text ("LB", "RT"), not shown as glyphs (`menu-settings-controller.png`).

### 9. Colours outside the theme; the Back Alley stops at the campaign (Low-Medium, Back Alley players; CONFIRMED)

- `UiTheme` covers the campaign shell. Hard-coded colours remain in the main menu and Settings (`MenuScreens.cs`: 12
  `Ui.Hex`, 21 `new Color`), Story (6), Tag setup (11), Replays (7), prompt dialogs (19) and the board (108).
- The alley's versus and results are the main ones recoloured (`theme-720/out/theme-back-alley-versus.png`); the alley
  tab itself is the strongest themed screen (`theme-back-alley-alley.png`).
- In the alley theme the map caption wraps under the three tabs ("...to the / Gauntlet",
  `theme-back-alley-circuits.png`).

### 10. "New game" sits in the campaign's top bar (Low-Medium, everyone; CONFIRMED)

- A same-size button between Card Shop and Menu, red text (`CampaignScreen.cs:264`; `campaign-circuits.png`). It opens a
  confirm panel, so nothing is lost by a misclick, but a destructive action shouldn't share the navigation row.
- The bar grows to eight equal buttons (Guests, Lessons, Bounties, Recipe Book, Locker, Card Shop, New game, Menu) with
  no grouping (`nopics/campaign-bounty-board.png`).

### 11. Smaller things (Low)

- **Free Duel says "Not owned".** The board's card detail in Free Duel shows the campaign collection
  (`evidence/controls-720/out/controls-arrow-attack.png`); the editor was fixed today, the board wasn't
  (`CardDetail.cs:318-322`). CONFIRMED.
- **Map labels overlap the band footer**: "Beginner Circuit 0/3" sits on the BEGINNER strip at 720
  (`nopics/campaign-circuits.png`). CONFIRMED.
- **Three placeholder styles for one idea** (no portrait): a grey word "YOU", an empty area (Gumblar Gail in
  `tag-setup.png`), a hooded silhouette (the alley). CONFIRMED.
- **"Coming soon" in the shipped game**: the Back Alley lists a locked NO LIMITS table with "Coming soon"
  (`build/qa-review/checks/alley-nopics-720/out/alley-6-won.png`). A visible unfinished promise; hide it until it exists
  or give it a teaser line. CONFIRMED.
- **The developer layout's title bar** reads "Turn 1 - Bunny Rex -" between phases (`fx-nopics-720/out/fx-turn.png`).
  Developer view only. CONFIRMED.
- **Speech bubbles** in duels are plain white boxes with italic text and no tail to the speaker
  (`fx-nopics-720/out/fx-finalblow.png`, `fx-react-hit.png`); they read like tooltips. CONFIRMED.
- **The replay list** is text rows only: no portraits, no win/loss colour (`replay-nopics-720/out/replays-list.png`).
  CONFIRMED.
- **Results on a loss** keep Continue as the red primary and Rematch beside it; the DP panel is nearly empty
  (`final-nopics-720/out/final-36-round1-loss-results.png`). After a loss, Rematch is the likelier choice. CONFIRMED.

## What already works well (keep it)

- **The painted circuit map**, portraits on nodes, locks and crowns, band strips
  (`evidence/campaign-1080/out/campaign-circuits.png`).
- **Character art and the logo**: the title screen with the logo and a drifting wall (with pictures), the portraits on
  plates, versus and the Recipe Book.
- **Story backdrops**: 11 code-drawn places with crossfades give the scenes a sense of place (`theme-story-*.png`).
- **Code-drawn duel effects**: summon scenes, the attack arrow, LP plates that drain and crack, turn and phase banners,
  the chain panel with YOU/OPP and states. They hold up without pictures.
- **The navy card back with the gold star**: original, crisp at every size, and reads as Duel Circuit's own.
- **The Back Alley tab**: brick, chain-link, tape, stencil caps; a real second identity.
- **Clear primary actions**: one red button per screen (Duel!, Buy a pack, Continue) in both themes.
- **About and Import Cards**: clear, calm, well spaced, with keyboard focus drawn (`about-720/out/about-main.png`,
  `import-720/out/import-idle.png`).
- **The bracket**: portraits, seeds, a lit path and the prize ladder in one screen (`nopics/campaign-bracket.png`).
- **Hover and press feedback** across buttons, rows and map nodes (`theme-*-hover-*.png`).
- **Honest, specific copy**: shop "why this set", odds on every pack, scouting lines, results lines that name the gift.
- **The 1080 layout**: at 1920x1080 the shell screens are tidy and aligned on a consistent grid.

## Proposed changes

Costs are agent-days including a check that fails without the change. Every change to a shell screen needs the
matching app checks at 1280x720 and 1920x1080; every change touching cards needs them **with and without pictures**
(`-pics <empty folder>`), which no check does today. No change below needs new art from the owner unless marked **art**.

### Quick (a few hours each)

| # | Change | Fixes | Files | Cost |
|---|---|---|---|---|
| Q1 | A **no-pictures pass in the checks**: run `-campaigncheck`, `-layoutcheck`, `-themecheck`, `-shopcheck` with `-pics <empty>` too, and add steps that fail if a card slot is drawn blank (title wall, menu showcase, versus slot, slab name) | 1 (guards it) | `tools/` check scripts or DEVELOPING.md; the four checks | 0.5 |
| Q2 | Title wall without pictures: drift **text-look cards** (or the drawn card back) instead of brown blanks; main menu: show the showcase card as a text-look card | 1 | MenuScreens.cs:90-99, 404-410 | 0.5 |
| Q3 | Versus and Free Duel showcase: one call to the card renderer instead of the black box | 1 | CampaignScenes.cs:604-612 | 0.25 |
| Q4 | Slabs: the text-look card inside the slab keeps its name and stats | 1 | SlabView.cs, VanScreen.cs, GradingDeskScreen.cs | 0.5 |
| Q5 | Text-look names and stats **scale with the card's size** (spotlight, reading view, detail), with a floor of 14 at 1080 | 1, 2 | CardView.cs:126-139 | 0.5 |
| Q6 | Free Duel card detail without "Not owned" | 11 | CardDetail.cs / DuelController.cs (where ShowOwnership is set) | 0.1 |
| Q7 | Settings: values get **< >** arrows (left goes back), volume goes down as well as up; reorder to Sound, Display (window, text size), Duel, Cards | 8 | MenuScreens.cs:526-640 | 0.5 |
| Q8 | "New game" moves into Menu (or the bottom of the panel), out of the top bar | 10 | CampaignScreen.cs:264 | 0.25 |
| Q9 | Story narration without a portrait starts at the box's left padding; the box height fits two lines, growing for longer ones | 6 | StoryScreen.cs:81-94 | 0.5 |
| Q10 | Fix CARD-PICTURES.md's description of the text look (or make it true with M1) | 1 | docs/CARD-PICTURES.md | 0.1 |

### Medium (1 to 3 days each)

| # | Change | Fixes | Files | Cost |
|---|---|---|---|---|
| M1 | **A designed text-look card** (the "plain card"), drawn in code at every size: frame colour by kind, a name bar, attribute glyph and level/rank/link marks drawn in shapes (original, not the official card layout), an inner panel that shows the card's **type line and effect text** when big enough, ATK/DEF in a footer. **Rarity on the frame**: a foil sheen on the name bar and border for Rare and up, the rarity's colour in a corner mark, grade wear on corners, so `CardFoil`/`CardWear` work without pictures. One renderer used everywhere a card is drawn (board, hand, packs, editor, detail, reading view, slabs, versus, menu, history, callouts) | 1, 3 | CardView.cs (new text face), CardFoil.cs (a mode for `Image`/drawn faces), CardDetail.cs, SlabView.cs, BoardView.Callouts.cs, history strip; a new `-plaincheck` shooting every size | 3 |
| M2 | **Legibility floor and type scale**: 6 named sizes in `UiTheme` (caption, small, body, label, title, display) with a floor of 14 at 1080 (about 9-10 px at 720); replace the literals screen by screen, starting with campaign, results, shop, settings and the board's detail panel. Settings > Card text size scales the scale | 2 | UiTheme.cs, Ui.cs, then the shell screens; `-layoutcheck` rule: no text under the floor | 3 (shell); the editor's M1 from the editor review is separate |
| M3 | **A display face and a body face** (open licence, bundled with their licence in About): heading face through `UiTheme.Title` for both themes (screen titles, "Victory!", "VS", banners), body face through `Ui.Font`. Owner picks from 2-3 candidates | 4 | Ui.cs:12, UiTheme.cs:403-429, Resources/Fonts, AboutScreen.cs licence list; check: titles use the face | 1 |
| M4 | **Pack opening with a pack**: a code-drawn wrapper in the set's colours with its name and code, a tear or slide to open, cards fanned out face-down, the hits held to last with a rarity flash and the rarity's name in a big tag on reveal; Reveal all still there | 3 | ShopScreen.cs (pack screen), BoardFx textures; `-shopcheck` stills | 2-3 |
| M5 | **Results that celebrate**: DP and EXP count up with ticks; the opponent's portrait (with their loss or win expression once art exists); one fact line (turns, damage dealt, your ace); a **circuit clear screen** of its own (crown, circuit name, prize, what opened) before the per-match lines; unlock strips as icon rows at body size | 3 | CampaignScenes.cs:899-1000, DuelController.Results.cs; `-campaigncheck` stills and a count-up step | 2 |
| M6 | **A player identity**: a code-drawn crest (initial, a chosen colour and a shape from 6), set at New game and in the Locker, shown on versus, tag setup, brackets and the plate when no poster is hung. Fills the You panel's empty half with your record and your deck's three key cards | 5, 6 | CampaignScenes.cs (versus), TagSetupScreen.cs, BracketScreen.cs, BoardView.Panel.cs, CampaignState (a save field, migrated) | 2 |
| M7 | **Theme everything**: move Menu, Settings, Story, Tag setup, Replays and prompt dialogs onto `UiTheme` roles (no colour change on Main Street); fix the alley caption wrap | 9 | MenuScreens.cs, StoryScreen.cs, TagSetupScreen.cs, ReplayBrowser.cs, PromptDialogs.cs; `-themecheck` visits them | 1.5 |
| M8 | **One mat, not two halves**: a soft blend where the two mats meet (a gradient band and a thin centre line), or your mat only with the opponent's as a tint | 7 | Playmat.cs; `-matcheck` stills | 1 |

### Bigger (a week or more)

| # | Change | Fixes | Files | Cost |
|---|---|---|---|---|
| B1 | **A board that uses the width**: a slight perspective tilt (a 3D camera on the field only) so cards near you grow and the field fills more of 16:9, or a 2D layout with larger zones and the piles moved out to the sides; the card detail docks larger in the space freed | 7, 2 | BoardView*.cs, BoardFx.cs, Playmat.cs, every board check (`-layoutcheck`, `-boardcheck`, `-controlscheck`, `-pilecheck`, `-clickcheck`) | 8-12 |
| B2 | **A 1280x720 layout** for the shell (not only the editor): screens laid out for 720's height with bigger type, chosen when the window is under 900 px tall | 2 | every shell screen; `-layoutcheck` at both sizes | 6-8 |
| B3 | **TextMeshPro** in place of legacy Text: crisp outlines and shadows at any scale, real letter spacing, rich text without hacks | 2, 4 | every `Ui.Text` call (about 670) and the ~50 built by hand, Ui.cs | 6-8 |
| B4 | **Owner art for the shell** (**art**): a title-screen illustration that works without pictures, shop and alley interiors behind the shell screens, results backdrops per band | 1, 6 | art/, MenuScreens.cs, CampaignScenes.cs | 2 of code once art exists |

## Owner decisions (recommendation first)

1. **Which look is the game's real look?** *Recommended:* the no-pictures look is the default and gets designed (M1);
   pictures are an upgrade. *Or:* keep it a fallback and push Import Cards harder on first launch. Default chosen if
   building starts: design it.
2. **Fonts.** *Recommended:* one condensed display face for titles and one plain sans for body, both open-licence (OFL),
   listed in About. I'd shortlist three pairs for you to pick from on a sample screen. *Or:* keep the built-in font and
   only fix sizes (M2).
3. **720 players.** *Recommended:* a type floor now (M2), a 720 layout later only if 720 players complain (B2). *Or:* B2
   first if the Steam Deck matters.
4. **The board's size.** *Recommended:* leave the board as is for now and blend the mats (M8); revisit B1 with a
   prototype screenshot before committing a week. *Or:* B1 now.
5. **The player's face.** *Recommended:* a code-drawn crest (M6), no avatar art. *Or:* you draw 6-8 original player
   avatars (**art**) and M6 picks between them.
6. **What to build first.** *Recommended: one spec, "Plain cards and first impressions": Q1-Q5, Q10 and M1 (about 5.5
   agent-days).* It changes what every new player sees on every screen, and Q1 keeps it from slipping back. Then M2 + M3
   together (type, about 4 days), then M4 + M5 (rewards, about 5 days).

## Not verified

- **1920x1080 without pictures**: only `-layoutcheck` was run at 1080 without pictures; the campaign, shop and story
  screens at 1080 were judged from the with-pictures evidence.
- **Motion**: judged from stills, `-fxshots` and code. Count-ups, flips, the deal-in, hover timing and LP drains weren't
  watched.
- **Sound**: muted; out of scope (audio review).
- **The Settings screen below the first rows**, the Guests and Lessons screens, Structure Decks and the Card Trader:
  seen only in passing or in the with-pictures evidence; no separate findings for them.
- **The editor**: covered by `DECK-EDITOR-UX-REVIEW-2026-11-19.md` and today's quick fixes; only its look without
  pictures is reported here (finding 1).
- **The alley check's one FAIL** (step 17) and the import check's FAILs (caused by my `-hdpics off`) weren't looked into.
- **Windows, Steam Deck, 1280x800, ultrawide**: not run. The letterbox shot at 1280x1024 exists in `layout-*`.
- **Fonts' licences** and how candidate faces render in Unity's legacy Text at small sizes: not tried.
- **Costs** are estimates from reading the code, not from a spike.
