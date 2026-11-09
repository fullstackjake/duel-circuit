# Spec (2026-11-06): Duchess Ilse as a guest, and three story hooks

Owner decision: [STORY-REVIEW-2026-11-06.md](STORY-REVIEW-2026-11-06.md), "The owner's answers" 1, 7, 9 and 12. Build plan:
[NEXT-BUILD-2026-11-06.md](NEXT-BUILD-2026-11-06.md), chunk B2. The scene text belongs to the Narrative Designer (W1/W2). This
spec names the triggers, scene ids and fields they fill in. Values marked `[PLACEHOLDER]` are hypotheses. Tuning is parked,
so her four decks play exactly as they are now, at `skill: 1.0`.

## 1. Ilse's guest entry

**guests.txt** (the 5th entry, after Seraph):
```
guest: ilse synchron, zombies, gadgets, modern
unlock: final
blurb: The name at the top of Gus's corkboard. She hasn't sat down across from anyone in years.   # W2 may reword
prize: 10000   # [PLACEHOLDER] above Seraph's 8000, equal to the Every Era final's finalprize
```
- **Grammar** (update the header): `guest: <character> <deck>[, <deck>...] [+ <partner character> <partner deck>]`.
  Both the partner and the deck list are optional. `Guest.Decks` is the list and `Guest.Deck` stays `Decks[0]`, so the
  existing readers (`DuelController.Qa.cs`, `WallStudy`) compile unchanged. Add `Guest.HasTag`, true only when a `+ partner`
  parsed. Without a partner, `IsTagUnlocked` is always false and nothing reads `Partner`.
- **`unlock: final`** means `Library.FinalCleared(profile)`: all six Every Era rounds are in `LibraryWins`. Rationale: it
  depends on save state, not on story text. A save already past the final unlocks at once, and New Game+ doesn't touch the
  Library, so she stays unlocked in NG+. `GuestList` gets an optional `Library` (`Load(path, roster, library)`). With no
  Library loaded, `final` stays locked. In `CampaignState`, load the Library before the guests (today the guests load
  first, at line 225, and the Library at line 229). `CardUnlocks.MeetCircuit` maps `final` to the last circuit (the Tier 0
  Gauntlet). Today it would fall through to circuit 0.
- **Her rules:** a `final` guest's duels follow the last circuit's rules (`GuestRules` returns the last index for her).
  Without this, an NG+ save would face her 2026 decks under the Beginner list. The existing `RulesFor` fallback to the
  current circuit's rules still applies.
- **While locked**, the guests screen shows the row as all guests do: `???`, the `?` tile, and the blurb (so the blurb must
  not name her). Status line, from `RuleText("final")`:
  - Library not open: `Locked: Win the Every Era final`
  - Cups left: `Locked: Win the Every Era final (Format Library: 7 of 12 cups cleared)`
  - All cups cleared: `Locked: Win the Every Era final (2 of 6 rounds won)`
- **Format: one deck per duel, rotating on wins.** She plays `Decks[w % 4]`, where `w` is `MatchWins["guest:ilse"]`.
  A loss or a draw doesn't advance the rotation, so you have to beat each deck in turn ("Show me none of it was luck").
  There's no new save field and no series state. NG+ clears MatchWins, so the rotation restarts at Synchron along with the
  first-win prize. Add `GuestList.DeckFor(guest, profile)` and use it in every place the flow reads `g.Deck` for one on one:
  the versus screen, its showcase and scouts, `StartGuestDuel`, the coin toss, and the guests screen's Scout. Scouting
  shows only the next deck. Rows:
  - Decks line: `Plays four decks, one a duel: Synchron, Zombies, Gadgets, Modern · no tag duel`
  - Status: `First win: +10,000 DP · next: Synchron (1 of 4)`, then `Beaten 2x: she can partner you in tag duels · next: Gadgets (3 of 4)`
- **No tag duel.** Her scene line is "This one's only between us". `GuestRow.HasTag = false` hides the Tag button and
  doesn't register `Tag:ilse`.
- **Five rows must fit.** The rows sit at `150 + i*200` with a height of 180, so a fifth row runs off the screen and under
  Back (y 960). With more than 4 guests, use a step of 160 and a height of 150, and move the decks and status lines up.
  Everything must clear Back at 1920x1080.
- **First-win prize:** `prize:` paid through the existing `GuestList.Record` → `Circuits.PayDuel` (the ledger line
  `Ledger.Guest`). This is no new DP path. Rematches pay `WinDp` like every other guest. It's paid once per cycle, as with
  Ines, whose lessons unlock also persists through NG+.
- **Tag Cup partner once beaten: yes, at no cost.** `TagPartnerChoices` already adds beaten guests. She brings her own
  decks through `PartnerDeckChoices`.
- **`beat:ilse` and her gift:** add `signature: Mirror Force` to `ilse.profile` (a data field, so it's the builder's line).
  The `beat:` event exists only through the signature gift (`CardUnlocks.Bonds`: sig 0 means no grant and no event).
  Mirror Force is in three of her four decks, is no one else's signature, isn't on the no-pass list, is unlimited on the
  TCG September 2026 list, and its 2002 printing passes the Gauntlet's 2026 era. It's a card from no year, which suits her.
  The bond 3 trade, the bond 5 rarest gift and the 15% first-cycle drops follow automatically. Tell W2 that `{card}` is a
  new copy, not the signed card on the board.
- **Music and portrait:** `art/portraits/ilse.png` exists. Music stays `BossMusic.ForGuest`, which plays the everyday
  theme unless an `ilse` theme exists. No new assets.
- **Old saves:** there's no format change. A save with every final key unlocks her. A save from before the Library
  existed keeps her locked until it clears the final.

## 2. Ilse's scene triggers (W2 writes the scenes)

| Scene id | Trigger | When it plays | Place |
|---|---|---|---|
| `ilse-table` | **new `guest:<character>`** | Duel pressed on a guest's row, before the versus screen, once (only if a scene for that guest exists) | default shop-floor |
| `gift-ilse` | `beat:ilse` | on the results of the first win, like every `gift-*` scene | **`corkboard-top`** (new) |

- **`guest:<character>`**, generic for any guest. In `GuestVersus`, before the deck-readiness check:
  `if (!cycled && _story.Pending(profile, new[]{"guest:"+id}).Count > 0) { PlayStory(..., () => GuestVersus(g, tag)); return; }`.
  This copies the alley's `headliner:<id>` (`DuelController.Alley.cs:110`). `StoryPlaces.Default` falls through to
  shop-floor.
- **The corkboard needs a little code.** `StoryBackdrops.MainStreet.Corkboard()` is static and paints the old gold-signed
  card at the top (600, 82) with a newer card under it (612, 248). It doesn't depend on the profile and draws no names. So
  the reorder in `every-era-corkboard` ("above mine") can stay text only, but `gift-ilse` ("under yours") would contradict
  the painting. Add one place, `StoryPlaces.CorkboardTop = "corkboard-top"`, to `Known`, plus a `case` in
  `StoryBackdrops.cs`. It is `Corkboard(swapped: true)`: the plain card at the top, the gold-signed card (and its two gold
  strokes) in the lower slot. About 10 lines. The backdrop check paints every `Known` place, so it covers the new one.
  W2 sets `place: corkboard-top` on `gift-ilse`.

## 3. Three story hooks

**`firstloss`** (scene id `first-loss`; W1 writes it, Gus at shop-floor):
- **What counts:** a loss (`winner == 1`, not a draw) in a **one-on-one circuit match** or a **guest duel**. That includes
  Gus's Rookie Cup match and loaner losses. It excludes Gus's practice "First duel" and the lessons (they're teaching and
  aren't recorded), Tag Cups (team duels that `Losses` doesn't count), the Library, the Every Era rounds, the Locals and
  the alley (it has `antelose`).
- **Rule:** `Story.LossEvents(profile, winner)` returns `["firstloss"]` when `winner == 1` and `profile.Losses.Values.Sum() == 1`
  after the record, meaning this is the save's first recorded loss of any kind. Add it in `BeginCampaignResults` and
  `BeginGuestResults`. It plays on the results like `beat:` scenes. `Seen` makes it once ever, and `Losses` survives NG+.
- **Saves that already lost: they never see it** (their sum is already above 1). Gus's "new faces" line would be wrong in
  the Master band. This needs no migration.
- Accepted edge: if the first loss is a duel forfeited by `LiveDuels.Settle` on load, the scene is skipped, because no
  results screen plays.

**`finale`** (scene id `finale`; W1 writes it, `place: alley`, staged per A5):
- **Rule:** `Story.FinaleDue(profile, alleyData)` is true when `Seen` has `every-era-corkboard` and `alley-closing`, plus
  `nl-invitational-win` **only while the No Limits table is ready**
  (`alleyData.Tables.Any(t => t.Tier == AlleyTier.NoLimits && t.Ready)`), and `finale` is unseen.
- **Recommend firing on the two strands now.** Every build ships No Limits `ready: no`, so waiting would leave finale as
  dead content. The No Limits strand switches on by itself the day the table ships. **W1 must keep No Limits and the
  Invitational out of the finale's text**, because a save may see it before either exists.
- **Where it's raised:** next to the heldcard catch-up in `CampaignScreen.Alley.cs` `AlleyOnOpen`, which runs on every
  campaign-screen Open, through a `CampaignState.FinaleDue` property. A fresh run gets it on the first Open after the last
  strand's scene has been read. A save that is already past every strand gets it on its first Open after this ships, once.
  Every later Open raises nothing.

**`partner:` profile field and `{partnerline}`:**
- `Personality.PartnerLine` is read from `partner: <one line>` in `*.profile`. Every character who can partner gets one
  from W1, Ilse's from W2. These are text lines, so they belong to the writers.
- `Story.Fill` replaces `{partnerline}` **first**, so the line itself may use `{partnerdeck}` and `{you}`. Add
  `partnerline` to `Story.Placeholders`.
- **Fallback: the line is skipped.** `Story.Shows(line, partner)` is false when the text contains `{partnerline}` and the
  partner has no line. `StoryScreen.Play` filters its queue with it, next to `ShownIn`. W1 writes `first-partner` with
  `partner: {partnerline}` as its own line.

## 4. Checks

**`--campaign-test` (CampaignTest.cs)**. First update the existing checks the change would break. Lines 445-447 and 466
must skip `PartnerDeck` when `!HasTag`, and the partner lookup at line 485 needs the same guard. At lines 520-524,
"openAlone" must give the veteran the final's keys, and the tag checks must exclude `!HasTag` guests. `TriggerOk` must
accept `guest:<guest id>`, `firstloss` and `finale`. Then add these checks:
1. Ilse parses with 4 decks (each file exists), no partner and `unlock: final`. The veteran save (every circuit cleared,
   the lessons passed, 12 tag wins) still has her locked. Add every final key and she's unlocked, and still unlocked after
   `NewGamePlus()`. Her tag duel never opens.
2. Rotation: Synchron at 0 wins, a loss and a draw keep it, a win moves to Zombies, 4 wins wrap back to Synchron.
3. Prize once, then `WinDp`, on her first win (no tag prize). The first-win grants give Mirror Force with event
   `beat:ilse`. `MeetCircuit("ilse")` is the last circuit. `SignatureProblems` is empty.
4. `firstloss`: a new save's first circuit loss raises it and the second loss doesn't. A guest loss on a new save raises
   it. A draw doesn't. A save with earlier `Losses` doesn't.
5. `finale`: not due with any one strand. With both strands and No Limits not ready, it's due. Once seen, it's not due.
   With a test `AlleyData` whose No Limits table is ready, it needs all three.
6. `{partnerline}` fills in (with `{partnerdeck}` inside it). A character without one has that line skipped.
   `partner:` parses. Print, as info, the characters who can partner you but have no line.
7. Scenes exist: `ilse-table` (`guest:ilse`), `gift-ilse` (`beat:ilse`, `place: corkboard-top`), `first-loss`, `finale`.
   This fails until W1/W2 land, which is intended for the combined build.

**App checks:**
- **final** (`DuelController.FinalCheck.cs`), after the corkboard scene:
  - Finale isn't raised yet.
  - On the guests screen, Ilse is unlocked, there's no `Tag:ilse`, all five rows clear Back (screenshot).
  - `Duel:ilse` plays `ilse-table`. The versus screen shows Synchron and Tier 0 Gauntlet rules.
  - A win through the guest results path shows the prize line and the Mirror Force gift, then `gift-ilse` on
    `corkboard-top`. The row then says "next: Zombies" and she appears in `TagPartnerChoices`.
  - Then mark `alley-closing` seen, Open: `finale` plays once, and a relaunch plays nothing.
  - Report each scene as seen.
- **campaign** (DuelController.cs, the Ines guest step at line ~3650): conceding to Ines on that save plays `first-loss`
  when it's the save's first loss. Read it through before "Continue goes back to the guests". A second concede plays
  nothing.
- **alley**: Nyx's Table cleared and `alley-closing` read, with the Library strand undone: `finale` is not raised.
- **theme/backdrop**: covered by the `Known` loop (`corkboard-top`). **layout**: if the layout check visits the guests
  screen, assert five rows at 1280x720. The other checks need no change.

## 5. Builder file list (B2)

- **Engine:**
  - `src/Ygo.Engine/Campaign/Guests.cs`
  - `Story.cs` (the triggers' doc comment, `LossEvents`, `FinaleDue`, `Shows`, `{partnerline}`, `CorkboardTop`)
  - `CardUnlocks.cs` (`MeetCircuit` for `final`; register all of a guest's `Decks`)
  - `src/Ygo.Engine/AI/Personality.cs`
- **Data:**
  - `campaign/guests.txt` (entry and header)
  - `personalities/ilse.profile`: the `signature:` line only
- **App:**
  - `CampaignState.cs` (load order) and `CampaignState.Alley.cs` (`FinaleDue`)
  - `DuelController.Guests.cs`
  - `DuelController.cs` (one line in `BeginCampaignResults`, plus the campaign check step)
  - `CampaignScenes.cs` (guest rows)
  - `CampaignScreen.Alley.cs` (the catch-up). U1 owns only `CampaignScreen.cs`'s header.
  - `StoryScreen.cs`
  - `StoryBackdrops.cs`, `StoryBackdrops.MainStreet.cs`
- **Checks:** `src/Ygo.Harness/CampaignTest.cs`, `DuelController.FinalCheck.cs`, `DuelController.AlleyCheck.cs`.
- **Not the builder's:** `campaign/story.txt` (including its header's trigger list, which W1/W2 update with `guest:`,
  `firstloss`, `finale`, `{partnerline}`), and every text line in `personalities/*.profile`, including `partner:` lines and
  Ilse's blurb wording if W2 wants it.

## The owner's answers (2026-11-06)

All three as recommended: the finale plays on the Library and alley strands now and waits for No Limits only once that
table is ready; Ilse's signature is Mirror Force; her first-win prize is 10,000 DP, paid again each New Game+ cycle.
