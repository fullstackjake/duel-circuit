# Card search: build spec (2026-11-07)

Finding a card you don't know the name of. Six pieces borrowed from other Yu-Gi-Oh games and tools: search by what a
card does (Link Evolution's genres, YGOPRODeck's effect tags, EDOPro's categories), cards you saw in duels (Master Duel
and Duel Links' Card History), archetype search (EDOPro's `@`), name-only / text-only search (EDOPro's `$`, the official
database's tabs), a ban list filter (Link Evolution's "Limitation"), and a handful of smaller ones (OR, Pendulum
Effects, card looks, close names).

Everything here lives in the deck editor's search panel (free play and the campaign catalog). Nothing changes the duel,
the economy or profile.txt: **old saves load unchanged** and no DP moves.

## 0. Where things stand

- `CardSearch` ([CardSearch.cs](../../src/Ygo.Engine/DeckBuilding/CardSearch.cs)) parses one box: words (name or text),
  `"phrases"`, `-word`, `lv8`/`atk>2000`/`scale5`, kinds, Attributes and Types (`attr:`/`type:` strict forms), the
  bots' roles (`handtrap searcher draw removal negate wipe revive specialsummon destroymonster destroyst`) and six text
  tags. Every term must match (AND). Results rank exact name, name starts with, name has the words, text only.
- `DeckEditor.RunSearch` ([DeckEditor.cs:2284](../../unity/Assets/Scripts/DeckEditor.cs:2284)) joins the box, the 10 effect
  chips and 4 pickers into one query, then applies Related, the scope (All / Owned only / Favorites) and the sort (6).
- The panel is 500 px wide at x 1410: search box (y 10), 2 rows of chips (56, 86), pickers (118), count + active
  filters + scope (152), the 5x5 grid (184–934), paging (940), help (980). **There's no free row**, so new filters go
  in a panel that opens over the grid (section 7).
- The card database's search categories are already loaded (`CardInfo.Category`, card pack v3) and drawn as the card
  detail's genre chips (`CardTags.GenresOf`, 29 tags), but they can't be searched. Coverage: 10,917 of the 14,518
  non-token cards in cards.cdb have categories; 1,622 Effect Monsters have none.
- Archetype names: `Archetypes.Load` reads 616 names from archetype_setcode_constants.lua; the editor holds them in
  `_archetypes` (set by `SetAnalysisData`). `CardSearch` doesn't know them.
- `CardSearch` lowercases each card's name and text on every term for every card, on every keystroke.

## 1. Search by what a card does

**Engine.** `CardTags` exposes its 29 genres as a public list, each with a search token:

| Token | Genre | Token | Genre | Token | Genre |
|---|---|---|---|---|---|
| `does:negate` | Negate | `does:control` | Take control | `does:token` | Token |
| `does:search` | Search | `does:damage` | Damage | `does:columns` | Columns |
| `does:specialsummon` | Special Summon | `does:gainlp` | Gain LP | `does:fusion` | Fusion |
| `does:draw` | Draw | `does:atkdef` | ATK/DEF | `does:ritual` | Ritual |
| `does:destroymonster` | Destroy monsters | `does:level` | Level | `does:synchro` | Synchro |
| `does:destroyst` | Destroy S/T | `does:position` | Position | `does:xyz` | Xyz |
| `does:banish` | Banish | `does:piercing` | Piercing | `does:link` | Link |
| `does:tohand` | To hand | `does:directattack` | Direct attack | `does:counters` | Counters |
| `does:todeck` | To Deck | `does:extraattacks` | Extra attacks | `does:coin` | Coin / dice |
| `does:mill` | Mill | `does:discard` | Discard | `does:win` | Wins the Duel (new, see below) |

- The `does:` prefix keeps these from colliding with name words ("Token", "Level", "Link", "Draw").
- **A card with categories matches on its categories only.** A card with `Category == 0` falls back to what we can
  read without them: the bots' roles (`Trait.Search`, `Draw`, `Negate`, `MonsterRemoval`, `SpellTrapRemoval`,
  `SpecialSummon`, `Revive`) and the text tags (`banish`, `discard`, `burn` for Damage, `gainlp`). Genres without a
  fallback simply don't match those cards. The fallback only runs for uncategorized cards, so categorized ones stay
  precise.
- `does:win` is the one new tag (YGOPRODeck's "Win the Duel"): text contains "win the duel". It's added to
  `CardTags.Genres` as text-derived, so the card detail shows it too.
- The existing 10 chips and role words stay as they are: they're the fast path, and the bots' roles (Hand trap, Board
  wipe, Revive) aren't categories.

**UI.**
- The **More filters** panel (section 7) lists all 30 genres as toggle chips, grouped: *Removal* (Destroy monsters,
  Destroy S/T, Banish, To hand, To Deck, Take control, Position), *Cards and resources* (Search, Draw, Mill, Discard),
  *Summoning* (Special Summon, Token, Fusion, Ritual, Synchro, Xyz, Link), *Battle* (ATK/DEF, Level, Piercing, Direct
  attack, Extra attacks), *Life Points* (Damage, Gain LP), *Other* (Negate, Counters, Coin / dice, Columns, Wins the
  Duel). Each chip shows its meaning on hover (the `Tag.Meaning` the card detail already uses).
- **Match all / Match any** switch above the genres (Master Duel's AND/OR): all is the default; any ORs the picked
  genres together (one `(a OR b OR c)` group, section 6.1).
- **The card detail's genre chips become clickable** in the deck editor: click "Banish" on a card and the search
  becomes that genre ("more cards like this"). In a duel they stay hover-only.
- Picked genres show in the active filter row like the effect chips ("Banish ×").

## 2. Cards you saw in duels

The case it answers: "what was that card the boss played?"

**Engine.** `SeenCards` (new, src/Ygo.Engine/DeckBuilding/SeenCards.cs), built like `DuelTally`: `Observe(byte[])`
subscribed to `DuelRunner.MessageReceived` beside the tally ([DuelController.cs:1884](../../unity/Assets/Scripts/DuelController.cs:1884)).
It records the **base code** (`Banlist.BaseCode`) of every opponent card that became known: activated (`Activate` by
the opponent's seat), Normal/Special/Flip Summoned by the opponent, or moved with a known code (`Move` with
`Code != 0`) while on the opponent's side. Tokens are skipped. It keeps each card's first-seen order and a count.
In a tag duel, both opponents count.

**Store.** `SeenLog` keeps the **last 20 duels** in `mods/decks/seen.txt`, beside favorites.txt, shared by free play
and the campaign and kept through Start Over (it's a convenience, like favorites). Plain text, newest first:

```
duel 2026-11-07 21:14	Professor Mica	campaign
89631139 2
14558127 1
```

The opponent's name is the in-game character's (never a real person's). A missing or broken file reads as empty;
writing caps it at 20 duels. Each duel is written when it ends (win, loss, draw or concession) and never for replays,
show duels, the deck tester's bot games, the campaign sim, or check runs (unless the check asks).

**UI.**
- The scope button gains **Seen in duels** and **Recently viewed**: All cards → Owned only → Favorites → Seen in duels
  → Recently viewed (campaign); All cards → Favorites → Seen in duels → Recently viewed (free play). Right-click goes
  back, as the pickers do.
- **Recently viewed** (owner decision 2): the last 40 cards you selected in the editor or opened in the reading view,
  newest first, no repeats. Kept in seen.txt after the duels as `viewed <code> <code> ...` (one line). Hovering doesn't
  count, and neither does anything in a duel. Empty: "Cards you look at in the editor show here."
- In that scope the results are newest duel first, in the order the cards appeared, and the line under each card reads
  "vs Professor Mica" (the latest duel it was seen in). Other filters still apply on top. Sort picks still work.
- A **duel picker** shows in the active row while in this scope: "All 20 duels" → "Last duel" → "vs Professor Mica ·
  7 Nov" → ... (click / right-click).
- Empty log: the results say "Cards your opponents play show here after a duel."

## 3. Archetype search

**Engine.** `CardSearch` takes the `Archetypes` (a `SetArchetypes` call, as the editor's `SetAnalysisData` does) and
reads **`@name`** (EDOPro's syntax): the cards in any archetype whose squashed name (lowercase, no spaces, hyphens or
underscores) equals the squashed term, or starts with it when nothing equals it. `@"burning abyss"`, `@burningabyss`
and `@burning_abyss` all work. Membership is `Archetypes.IsIn` (sub-archetypes included). An `@term` that names no
archetype matches nothing, and the status line says "No archetype called ...".

Members only: cards that just mention the archetype stay in Related (owner decision 3).

**UI.**
- An **Archetype** box in the More filters panel: type two or more letters for up to 8 suggestions with member counts
  ("Burning Abyss · 24"). Only archetypes with 2+ cards in the search set are listed. Picking one adds `@name`, which
  shows in the active row as "Burning Abyss ×".
- The card detail in the editor shows the card's archetype names as clickable chips after its genres (a card can be
  in several); clicking one searches that archetype.

## 4. Name only, text only

**Engine.** New prefixes, quoted values allowed:

- `name:word`, plus `$word` (EDOPro's form): the name only.
- `text:word` (YGOPRODeck's form): the card text only (for Pendulum monsters, both effects).
- `pend:word`: the Pendulum Effect only (section 6.2).

`-name:word` excludes, like `-word`. Ranking doesn't change: a `name:` word counts toward name ranking, a `text:` word
doesn't.

**UI.** A small three-way switch at the right end of the search box: **Name + text** (default) / **Name** / **Text**.
It applies to the box's plain words and phrases, not to prefixed terms. The box gets narrower by the switch's width.
The choice holds while the editor is open.

## 5. Ban list status

The ban list is the editor's (`_banlists[_banIndex]`), so this filter lives in the editor and not in `CardSearch`, which
knows no lists. It runs after `Find`, as Owned only does.

- **Limit** picker in the More filters panel: Any → Forbidden → Limited → Semi-Limited → Unlimited → On the list (0–2) →
  **Legal here**. It reads `Banlist.Allowed(code, cards)`: 0 / 1 / 2 / 3.
- For a whitelist list (`Banlist.Whitelist`, e.g. Goat), Forbidden reads as **Not legal**, as the grid's under-line does.
- **Legal here** = allowed above 0 and, when the era is limited (`EraLimited`, or a Library cup's `_format`), already
  released in the list's format: the same test the "not released yet" problem uses.
- Typed forms for people who type: `limit:0`, `limit:1`, `limit:2`, `limit:3`, `legal`. The editor removes them from
  the query before `Find` and applies them itself.
- Changing the editor's ban list re-runs the search.

## 6. The smaller ones

### 6.1 OR

- `a or b` (also `OR` and `|`) between two terms matches either. It binds tighter than the AND between terms:
  `lv4 dragon or spellcaster` = Level 4 AND (Dragon OR Spellcaster). Chains work: `a or b or c`.
- Any term can be in an OR, including `-word`, `@name`, `does:` and comparisons.
- `or` at the start or end, or doubled, is ignored. `"or"` in quotes is a plain phrase.
- No parentheses (owner decision 4).

### 6.2 Pendulum Effects

`CardSearch` splits a Pendulum card's text once, when it's built: the part between `[ Pendulum Effect ]` and the
`----` line is the Pendulum Effect, and the rest is the monster text. `pend:` searches the first part. Plain words and
`text:` still search both. Cards without that layout have no Pendulum Effect.

### 6.3 Card looks (campaign)

A **Look** picker in the More filters panel, over your owned copies (`Collection.CopiesOf`, not counting copies at the
Grading Desk): Any → **Foil** (a copy Super Rare or rarer: the booster foil slot's line) → **Slabbed** → **Gem Mint**
(a copy's shown grade is 10) → **Worn** (shown grade 4 or under) → **Alternate art** (the card has another artwork in
the database, any mode). It implies Owned for the first four. In free play the picker offers only Any and Alternate
art. The editor applies it after `Find`, like Limit.

### 6.4 Close names

When a search with plain words finds **no card whose name has all the words**, `CardSearch` also tries close names:

- **Squashed names:** "blueeyes", "darkmagician" and "blue eyes" match "Blue-Eyes" (lowercase, no spaces, hyphens or
  punctuation, on both sides). This also fixes ranking: `blue eyes white` counts as "name starts with" for "Blue-Eyes
  White Dragon".
- **Typos:** each word matches a name word within 1 edit (words of 4–6 letters) or 2 edits (7+); words of 3 letters or
  fewer must match exactly. Swapping two neighbouring letters counts as one edit ("bleu" → "blue").
- Close-name results come after any text matches. The status line says "No card named that; showing close names."
  Only plain words get this, not phrases or prefixed terms.

## 7. The More filters panel

- A **More** button takes the right 60 px of the pickers row (the five pickers narrow from 97 to 85 px each). It shows
  a count when filters in the panel are on ("More · 3").
- It opens a panel over the results grid (1410–1910, 184–934), the same size, on `T.PanelDeep`, with a Close button and
  "Clear these". Top to bottom: Archetype box, Limit and Look pickers, Match all/any, the genre chip groups.
- Results update live behind it (the count updates as you click). Escape or Close shuts it. The controller opens it
  with the button shown in the hint bar (Select), and LB / RB still page the results when it's closed.
- Everything it sets shows in the active filter row and can be removed there with ×. The active row scrolls
  horizontally when it overflows instead of dropping chips past 300 px as it does now.

## 8. Speed

`CardSearch` caches each card's lowercase name, squashed name, text, monster text and Pendulum Effect when it's built.
Close-name matching runs only when the normal search found no name match. Target: a search under 30 ms on the build
machine with every filter on. A harness timing prints the slowest of 50 sample queries.

## 9. Files

| File | What |
|---|---|
| `src/Ygo.Engine/DeckBuilding/CardSearch.cs` | `does:`, `@`, `name:`/`$`/`text:`/`pend:`, OR, close names, cached text, `SetArchetypes` |
| `src/Ygo.Engine/Presentation/CardTags.cs` | Public genre list with tokens and groups; `does:win`; the category fallback |
| `src/Ygo.Engine/DeckBuilding/SeenCards.cs` (new) | The duel observer and `SeenLog` (read, add a duel, cap at 20, write) |
| `unity/Assets/Scripts/DeckEditor.cs` | Mode switch, More button, scope Seen in duels, duel picker, Limit/Look post-filters, scrolling active row |
| `unity/Assets/Scripts/DeckEditor.Filters.cs` (new) | The More filters panel |
| `unity/Assets/Scripts/CardDetail.cs` | Clickable genre and archetype chips in the editor |
| `unity/Assets/Scripts/DuelController.cs` | Subscribe `SeenCards` beside the tally; write the log when a duel ends |
| `src/Ygo.Harness/CardSearchTest.cs` (new) | `--card-search-test` |
| `unity/Assets/Scripts/DuelController.SearchCheck.cs` (new) | `-searchcheck`: the app check |

Builders: engine and harness first (CardSearch, CardTags, SeenCards, the test), then the Unity side.

## 10. Acceptance checks

**`--card-search-test`** (harness, engine only):
- `does:search` finds Sangan and not Pot of Desires (Draw and Banish only); `does:banish lv4` only finds Level 4 monsters; an Effect
  Monster with no categories but the Search role is found by `does:search` through the fallback.
- `@"burning abyss"`, `@burningabyss` and `@burning_abyss` return the same set, all members; `@nosucharchetype` is empty.
- `$dark` finds no card that only has "dark" in its text; `text:dragon` finds no card that only has it in its name;
  `pend:destroy` finds Odd-Eyes Pendulum Dragon and no non-Pendulum card.
- `lv4 dragon or spellcaster` equals the union of `lv4 dragon` and `lv4 spellcaster`; `or dragon` equals `dragon`.
- `blueeyes`, `blue eyes` and `bleu-eyes` all find Blue-Eyes White Dragon; `blue eyes white` ranks it first.
- Every old query in the editor's checks returns what it did before (a fixed list run against the previous results).
- `SeenCards` from hand-built messages: opponent activations, summons and face-up moves are recorded; our own cards,
  face-down Sets and tokens aren't. `SeenLog` keeps 20 duels and 40 viewed cards (newest first, no repeats), reads its
  own file back, and reads empty from a missing or garbled file.
- Timing: the slowest of 50 sample queries, printed; fails over 30 ms.

**`-searchcheck`** (app):
- The More panel opens and closes; picking a genre, an archetype, Limit and Look changes the results and adds chips
  that remove them; Clear these clears only the panel's filters.
- The mode switch changes the results for `dark`.
- Limit Forbidden under a TCG list shows only cards with 0 allowed; Legal here under an era-limited list hides
  unreleased cards.
- After one bot duel, the Seen in duels scope shows the opponent's cards and the duel picker shows that duel; replays
  and the deck tester add nothing.
- Selecting three cards puts them first in Recently viewed, newest first; selecting one again moves it to the front.
- Clicking a genre chip and an archetype chip on the card detail searches for them.
- Layout: nothing in the search panel overlaps at 1920×1080 (the layout check's rules).
- No DP change; `defaults read com.ygocampaign.game` unchanged.

Plus the usual: `--deckbuilding-test`, `--campaign-test`, the campaign and layout app checks.

## Owner decisions (recommendation first)

**Decided (owner, 2026-11-07): accept all seven recommendations.**

1. **Where cards you saw show up.** *Recommend the catalog's Seen in duels scope only* in this pass. Alternative: also
   a "Cards you faced" button on the results screen that opens the catalog on that duel (more direct after a loss, but
   it opens the editor from the results screen, which nothing does today).
2. **Recently viewed cards** (Master Duel and Duel Links' Card History). *Recommend adding it* as a fifth scope,
   "Recently viewed": the last 40 cards you selected in the editor or opened in the reading view, kept in the same
   seen.txt. Alternative: leave it out; Seen in duels covers the main case.
3. **What `@archetype` matches.** *Recommend members only* (precise, and Related already finds cards that name it).
   Alternative: members plus cards whose text quotes the archetype's name ("Burning Abyss" support).
4. **OR grouping.** *Recommend no parentheses:* OR binds tighter than AND, which covers nearly every search.
   Alternative: YGOPRODeck's full `AND / OR / NOT ( )` grammar (more power, a real parser, harder error messages).
5. **The Look filter.** *Recommend building it* as in 6.3 (campaign: Foil, Slabbed, Gem Mint, Worn, Alternate art;
   free play: Alternate art). Alternative: leave it for later; it's about collecting, not finding cards.
6. **Close names.** *Recommend only when nothing matches by name* (6.4). Alternative: always add close names after the
   exact ones (more forgiving, more noise in every search).
7. **How many duels Seen in duels keeps.** *Recommend 20.* Alternatives: 10 (a shorter list), or 50.

## As built (2026-11-08)

Engine, then the Unity side and the harness test in parallel, an independent review, and a fix round for its ten
findings. Results: `--card-search-test` 156 ok (slowest of 50 queries 12.4 ms), `--deckbuilding-test` and `--tags-test`
pass; `-searchcheck` 61 ok, campaign 179, shop 89, layout 51, all 0 FAIL, `defaults read com.ygocampaign.game`
unchanged. Run the app checks with `build/search/checks.sh` (QA build: `-buildout build/search/"Duel Circuit.app"`).

Decided while building (not in the sections above):
- **Old searches.** The 194 old queries in `src/Ygo.Harness/fixtures/card-search-baseline.txt` return the same cards
  (compared as sets: squashed ranking moves a few up, e.g. "blue eyes"). `--card-search-test --write-baseline` rewrites
  it after a card database update. "e-hero" and "d-hero" used to find nothing and now show close names.
- **Squashed names** must start and end at name words ("blueeyes" → Blue-Eyes; "neos" doesn't match "Neo Space"). Name
  only "dark" also finds D/D Ark this way; accepted (the same rule makes "dhero" find D-HERO).
- **Close names** show only when no card in the whole database has all the plain words in its name; Attribute and Type
  words stay filters. They're sorted and filtered apart from the real results and always come last. The trader's
  search never uses them.
- **The box and the filters are searched as two parts** (`Find(box, mode, filters)`), so an OR typed in the box never
  joins a chip or picker, and in Name or Text mode the box's keywords ("fusion", "draw") are plain words.
- **Ban terms:** `-limit:N` and `-legal` exclude; a typed limit outside 0–3 filters nothing. Under a whitelist list
  (Goat) "On the list" means 1–2.
- **Seen in duels:** a duel where the opponent showed nothing isn't written. SeenCards also records a Set card turned
  face-up. seen.txt is replaced atomically and read from its .tmp if the main file is missing.
- **Picked archetypes are ORed** ("or X" in the active row). Clicking a genre or archetype chip on the card detail
  starts a fresh search (Owned only and Sort stay). Recently viewed doesn't reorder while you browse it.
- Not toon / spirit / union / gemini kinds: they would change old results for those words.
