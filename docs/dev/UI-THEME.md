# UI theme: roles and conversion rules

`unity/Assets/Scripts/UiTheme.cs` holds every color role used by the shell screens: Campaign, Shop, Trader,
Results, Versus (and the other scenes in `CampaignScenes.cs`) and the deck editor's frame. It has two themes:

- **`UiTheme.MainStreet`**: today's colors. Converting a screen to it must not change a pixel you can see.
- **`UiTheme.BackAlley`**: the alley tokens from POST-GAME-PLAN step 8.

`UiTheme.Current` (default `MainStreet`) picks the theme. Setting it raises `UiTheme.Changed`. Screens rebuild
on their next Open / Refresh.

## Roles

"Was" lists every old value that maps to the role. Values marked ≈ differ by at most 5/255 per channel for
surfaces, or 10/255 for text. Those differences can't be seen.

### Surfaces

| Role | MainStreet | BackAlley | Was / typical uses |
|---|---|---|---|
| `Base` | #121620 | #0D0B0A | screen roots; ≈ #141821 (editor root), ≈ #11151F (scene header band) |
| `Deep` | #0D1017 | #080706 | 90 px top bars; ≈ #0B0E15 (Versus / Results / toss background) |
| `Panel` | #1A1F2B | #1A1614 | detail panes, editor side columns, Locker / New game panels, editor charts |
| `PanelDeep` | #161B27 | #161210 | Versus sides, Results cards, guest / lesson panels, editor modals and deck browser |
| `Well` | #0E1118 | #090807 | inputs, card slots, XP track, trader face |
| `Inset` | #232A38 | #241E1A | Locker tiles; ≈ #232B3D (editor pickers), ≈ #232A3A (Results pip off) |
| `RowA` | #20273A | #221C18 | even rows, match rows not won |
| `RowB` | #1C2232 | #1C1714 | odd rows, starter tiles; ≈ #1E2535 (offer / deck-browser rows), ≈ #1C2130 (final round locked) |
| `RowSelected` | #2E3A55 | #3D2E25 | selected set / card / deck |
| `RowWon` | #232B1E | #1F2619 | won match row |
| `RowChampion` | #2B2534 | #2A1F2C | Champion's table row |
| `RowPlayable` | #2B2716 | #2C2414 | playable final round |
| `GoldWash` | #3A3220 | #3A2F1C | chosen poster / mat tile |
| `Unlock` | #3A2F12 | #3A2C10 | Results unlock rows |

### Controls, overlays, stripes

| Role | MainStreet | BackAlley | Was / typical uses |
|---|---|---|---|
| `Button` | #2A3448 | #2E2622 | BoxButton default, Scout, Back, Locker tab off |
| `ButtonOff` | #1C2333 | #1A1613 | tab not picked; ≈ #1E2433 (disabled button, editor Lerp base) |
| `TabOn` | #3A4A66 | #4A3A30 | picked tab; ≈ #3A4A6A |
| `ButtonDone` | #24402C | #22381F | passed-lesson button |
| `Primary` | #B8432F | #B8432F | the one red action per screen |
| `Chip` | #3D6FB5 | #4F6470 | editor active filter chip |
| `ImportChip` | #C8622F | #C8622F | trader OCG chip |
| `Highlight` | #FFC940 | #FFC940 | selection frames, ghost frame, gold stripe, armed button, starter ribbon fill, Versus edge |
| `Focus` | rgba(.35,.6,1,.6) | rgba(.88,.69,.44,.6) | editor card-modal outline |
| `Scrim` | black | black | modal dims; keep the call site's alpha |
| `Glass` | rgb(.03,.04,.07) | #070605 | pack-opening backdrop, map name backing; ≈ rgb(.04,.05,.08) strip. Keep the alpha |
| `GlassSoft` | rgb(.06,.08,.12) | #100D0B | pack info box; ≈ rgb(.05,.06,.09) hand overlay, ≈ rgb(.055,.07,.125) reveal inner. Keep the alpha |
| `Divider` | white @ .15 | #3FE0D0 @ .55 | section rules (neon in the alley) |
| `NoticeBar` | #3A2E52 | #3A2438 | editor save-as-new bar |
| `WarnBar` | #4A3D1C | #4A3A18 | editor recovery bar |
| `Brass` | #B8862B | #B8862B | guest / lesson stripes |
| `Locked` | #3A4050 | #3A332E | locked guest stripe |
| `GoodStripe` | #3FA56B | #3FA56B | passed-lesson stripe |
| `PipOn` | #2F5A35 | #2E5230 | won Results pip |
| `Meter` | #4CAF6A | #4CAF6A | XP bar fill |
| `Placeholder` | #13203A | #2A201A | guest initial tile |

### Text

| Role | MainStreet | BackAlley | Was / typical uses |
|---|---|---|---|
| `Text` | white | #EDE6DA | labels, titles, button text |
| `TextBright` | rgb(.88,.9,.95) | #E6DED1 | ≈ #E2E7F0, ≈ rgb(.92,.94,.98): card text, scouting tips |
| `TextBody` | rgb(.85,.87,.92) | #DDD5C8 | ≈ (.86,.88,.93), (.85,.88,.94) |
| `TextSoft` | rgb(.8,.83,.9) | #D0C7B8 | status bar, help, blurbs; ≈ (.8,.82,.9), (.78,.82,.9), (.82,.85,.92), (.8,.84,.92) |
| `TextMuted` | rgb(.75,.78,.86) | #C2B8A8 | subtitles; ≈ (.75,.78,.85) |
| `TextFaint` | rgb(.7,.74,.82) | #B3A999 | row second lines; ≈ (.72,.76,.84), (.7,.72,.8) |
| `TextQuiet` | rgb(.62,.67,.77) | #A0968A | tags, bond, guest decks; ≈ (.62,.66,.75), (.65,.7,.8), (.6,.63,.7) |
| `TextDim` | #8A93A6 | #8E8476 | rich-text meta gray, tab off label, LOCKED |
| `Caption` | white @ .5 | #EDE6DA @ .55 | small caps captions; also white @ .55 |
| `OnAccent` | #161B27 | #120F0D | dark text on gold fills |
| `Quote` | #F2E6B8 | #F2E6B8 | spoken quotes |
| `Intro` | rgb(.85,.85,.7) | #D9D2B0 | italic match intros |

### Signs and meaning

| Role | MainStreet | BackAlley | Typical uses |
|---|---|---|---|
| `Sign` | #FFC940 | #FF4FA3 | the screen's name plate only: CAMPAIGN, CARD SHOP, CARD TRADER, scene banner titles |
| `Gold` | #FFC940 | #FFC940 | DP, prices, prizes, "Cleared", in-panel headings, results headline on a win |
| `GoldSoft` | #FFD86B | #FFD86B | tokens, trader header, cup / final names, wishlist, unlocks, cup paths |
| `LibraryGold` | #E6C27A | #E6C27A | Library cup title |
| `Good` | #7BD88F | #7BD88F | won / ready / (new) / EXP; ≈ #7FE0A0 (scouting) |
| `Warn` | #E3A21A | #E3A21A | limited, ahead of band |
| `Bad` | #FF8A8A | #FF8A8A | errors, can't, New game label |
| `BadStrong` | #D9433F | #D9433F | Forbidden / not legal text |
| `BadWarm` | #FF8A6A | #FF8A6A | lesson "Not quite" |
| `Info` | #7FB2FF | #7FB2FF | scouting report heading |
| `InfoSoft` | #9FD8FF | #9FD8FF | "own N" |
| `Violet` | #B98CFF | #B98CFF | Champion's table |
| `VioletSoft` | #D7A6FF | #D7A6FF | scouting "Build against it" |
| `Import` | #FF9E6B | #FF9E6B | "OCG import" |

`UiTheme.NeonCyan` (#3FE0D0) is there for alley-only signage. Neon never goes on a button, a row or the board.

## Conversion rules

1. Take `var T = UiTheme.Current;` at the top of each Build / Refresh / Show method. Never cache a theme in a
   `static readonly` field.
2. **Colors:** replace `Ui.Hex("#FFC940")` with `T.Gold` (or `T.Sign` / `T.Highlight`, depending on use: see rule 5).
   Replace `new Color(0.8f, 0.83f, 0.9f)` with `T.TextSoft` and `Color.white` (text) with `T.Text`. Use the tables.
   A `Color.white` that tints art (portraits, posters, mat images) stays `Color.white`.
3. **Rich text:** replace `<color=#FFC940>` with `<color={T.GoldHex}>`. The `…Hex` properties cover Gold, GoldSoft, Good, Warn,
   Bad, BadStrong, TextDim, TextSoft, Info, InfoSoft, Violet, VioletSoft and Import. For any other role use
   `T.Tag(T.Role)`, or `T.Paint("text", T.Good)`. Strings in `const` fields (e.g. Trader's `AheadTag`) become
   properties or methods.
4. **Alpha:** `new Color(1,1,1,0.4f)` becomes `T.Text.WithAlpha(0.4f)`, and `new Color(0,0,0,0.75f)` becomes
   `T.Scrim.WithAlpha(0.75f)`. Glass keeps its alpha too: `T.Glass.WithAlpha(0.72f)`. The extension is in
   `ColorAlpha`. The screens' own private `WithAlpha(c, a)` still works.
5. **Gold has three roles:** `Sign` for the screen's name plate, `Highlight` for frames, fills and stripes, and
   `Gold` for currency and gold text. Pick by use, not by hex.
6. **Button fills:** a hex string passed as a parameter (e.g. `Button(..., color: "#B8432F")` in
   `CampaignScenes`) becomes a `Color` parameter (`T.Primary`, `T.Button`). `Ui.BoxButton` and `Ui.Button`
   still paint #2A3448 / rgb(.22,.25,.32). Set `.targetGraphic.color = T.Button` after creating one, or
   change the helpers in Ui.cs to read `UiTheme.Current.Button`.
7. **Keep checks and their colors on the same role.** For example, `DeckEditor` line ~1714 finds the browser
   headings by `t.color == Ui.Hex("#FFC940")`. Compare against `T.Gold` there too.
8. **Leave these alone:** card faces, rarity / grade / foil colors (`CardFoil`, `Grades.Color`,
   `ShopScreen.RarityColor`), limit and ban-pass badges on cards (#D9433F / #E3A21A / #C9A227 on card views),
   pack-reveal tags (#E8433F / #B8860B / #2E8B57), deck-editor data colors (`ChipColors`, the split and level
   charts, `KindColor`, drag-zone tints), the duel board and the Versus side colors (#4C8DFF you / #FF5A5A them),
   the circuit map's `Regions` palette and node art, the coin, and the Library's materials (shelf, binder plates,
   final plaque and pips).
9. **Convert whole screens, never half.** After converting, MainStreet must look identical.
