# Deck Workshop plan

Planned from the *Tag Force Deck Editor Deep Dive* report (supplied by the user, October 2026). Its formula: **Tag
Force RPG progression + an efficient modern editing layout + historical-format support.** This compares the report's
priorities with what Duel Circuit's deck editor already does, then lays out what to build, in phases.

## Where the editor stands

| Report priority | Status | Notes |
|---|---|---|
| **P0** Catalog and deck visible together | Done | Inspector, deck and catalog side by side; drag and drop both ways |
| **P0** Fast combined search | Partly | Text search with filters (`lv8`, `atk>2000`, words in the text) and 8 tag buttons; the active filters aren't shown as removable chips, and there's no Attribute / Type / Level picker or sort choice |
| **P0** Expandable card inspector | Partly | Master Duel-style detail with scrolling text; no one-click big reading view |
| **P0** Durable saves | Partly | Save (a built-in deck saves as your own copy), import/export codes; **no autosave** |
| **P0** Format-aware legality | Mostly | Ban list per era, cards you own, original vs. errata text; it doesn't check whether a card was **released yet** in that era, and doesn't always say *why* a card is illegal |
| **P0** Undo | Done | Full undo / redo |
| **P1** NPC recipes as rewards | Not yet | The **Recipe** button builds from any known deck you own enough of; beating a duelist doesn't unlock anything |
| **P1** Missing-card acquisition view | Partly | Campaign search shows owned cards only; the wishlist ranks shop sets, but you can't browse a card you don't own and see where to get it |
| **P1** Related-card discovery | Not yet | |
| **P1** Favorites / custom tags | Not yet | |
| **P1** Recipe duplication / versions | Not yet | No duplicate, folders, labels or history |
| **P2** Opening-hand tools | Done, beyond the report | Odds, starters, test hands, and 100-game bot tests |
| **P2** Deck analytics | Partly | Split and Level charts; no Extra Deck feasibility or archetype completeness |
| **P2** Import / export | Done | Deck codes |

## Phase 1: the Recipe Book (Tag Force's standout feature)

The report rates recipes 9/10 and calls them the series' best idea. Duel Circuit already has the material: 61
historical decks with their era, tier, how they worked and why they mattered, plus every character's own decks.

- **Beating a duelist unlocks their deck as a recipe** (the first win against them). Circuit bosses' recipes are the
  prizes; partners' recipes unlock when they team with you.
- **A Recipe Book screen**, grouped by the seven map regions: each recipe shows its owner's portrait, era and ban
  list, the deck list, the "how it worked / why it mattered" notes, and **how much of it you own** (with a bar).
- **Missing cards** listed with where to get them (which shop set and its price, or a Card Token at the Trader), and
  one click puts them on the wishlist.
- **Build from it**: copies it into a new deck of yours, filled from your collection (the existing Recipe logic).
- A results-screen line when one unlocks ("Recipe unlocked: Dante's Burning Abyss").

## Phase 2: a catalog that knows every card

- In the campaign, the catalog can show **every card**, not just yours: unowned ones dimmed with `0/3`, and the
  inspector says where to get them. A toggle switches to owned only.
- **Active filters as chips** above the results ("Owned × · DARK × · Level 4 × · Searcher ×") with the result count.
- **Pickers** for Attribute, Type, Level/Rank, card kind and Spell/Trap kind, plus **sort** (name, Level, ATK, DEF,
  newest, most owned).
- **Related cards** in the inspector: same archetype, and cards that mention it.
- **Reading view**: click the card text for a large overlay with the full text and every printing.
- More **effect tags**, split the way the report suggests: search from Deck, Special Summon, revive from GY,
  destroy monster, destroy Spell/Trap, negate activation, negate effect, draw, banish, discard, board wipe. Kept
  plain on the surface; text search covers the precise cases.

## Phase 3: safety and legality that explains itself

- **Autosave** every change to a recovery copy; reopening the editor offers to restore unsaved work.
- **Not released yet**: in an era, cards first printed after the ban list's date are flagged, from the printings data.
- **Every problem says why**: Forbidden, Limited to 1, only 2 owned, not released until 2016, or not usable under the
  rules of the time. Illegal decks still save; only entering a duel is blocked (as now).
- **Labels and folders** for your decks (Six Samurai, 2011 format, Casual...), **duplicate**, and the era shown next
  to each deck's name.

## Phase 4: extras

- **Favorites** (a star on any card) and custom tags on cards.
- **Archetype completeness**: "Burning Abyss: you own 7 of the 9 key cards."
- **Extra Deck check**: which Extra Deck monsters your Main Deck can actually summon.
- **Partner identity locks** (Tag Force's favorite cards): only in Story partner decks, never in free play.
- **Controller legend** at the bottom of the editor.

## Order

Recipe Book first: it's the most "Tag Force" part, it turns the bosses into rewards, and the data is already there.
Then the catalog, then safety and legality, then extras.

## Status

Phases 1-4 are built (UI checklist items 12-15). The one item left out is partner identity locks: partners only
ever play their own decks, so there's nothing to lock until partner deck editing exists.
