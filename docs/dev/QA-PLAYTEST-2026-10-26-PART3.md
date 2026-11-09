# QA playtest, part 3: the new campaign features (2026-10-26)

A fresh Mekk-Knight Invoked save with the default settings (the historic challenge off), played by the QA stand-in
through the real screens, as in parts 1 and 2 ([part 2](QA-PLAYTEST-2026-10-26-PART2.md)). On top of the circuits,
Tag Cups, lessons and shop, it now exercises what was built after part 2:
- **Card Tokens of any era**, priced by rarity, up to three trades a visit (modern staples first);
- **DP for a Card Token and a Ban Pass slot** whenever DP allows;
- **Tag Cup partners picked like a player would**: Kit every third tag match, else the partner with the strongest bond,
  bringing a deck other than their first when the bond allows it; checked against the duel that starts (partner,
  deck and skill);
- **the special guests** as they unlock, one on one and in tag;
- **Save as new**: once per run, a second deck (Recipe, or Build when no recipe is ready) saved over most of the old
  one, a circuit duel with it, then back with the campaign's deck picker.

## Results

**Run 1** (fresh save, 45 duels): **36 won, 9 lost**, 3,423 decisions through the prompt panel (none behind it),
**0 issues**. Through the Underdog Open, Trap Masters Cup and Small Engines Invitational into the Intermediate band:
8 circuits and 2 Tag Cups, Level 15. **Runs 2-4** (continuing that save, 30 duels in all) for the second deck and the
fixes below; the last of them **0 issues**. Every in-app check and harness test passes on the final build.

| Feature | What happened | |
|---------|---------------|-|
| Tokens of any era | 19 trades: Ash Blossom (2017), Infinite Impermanence and Called by the Grave (2018), Crossout Designator (2021), Triple Tactics Talent (2020) in a 2005-2011 shop; 3 cost 2 tokens (Indigo Eclipse, Invocation, World Legacy's Secret). Tokens always went down by the price | ✓ |
| DP for tokens | 4 bought, at 1,000, 1,500, 2,000 and 2,500 DP | ✓ |
| DP for Ban Pass slots | 2 bought (4,000 and 8,000 DP): 5 passes for every deck | ✓ |
| The era rule | The Mekk-Knight deck stayed whole at the Underdog Open: one Ban Pass on Raigeki (1 of 4) and it won the circuit | ✓ |
| Partners and bonds | 8 picks. Kit 3 times with your deck; Captain Brask (3 wins) bringing Gadgets; Old Gus (4 wins) bringing Zombies, his third deck. Each duel had the picked partner, deck and skill | ✓ |
| Guests | Ines unlocked by the lessons, beaten one on one and in tag; Nocturne by the Novice band, beaten both ways; Rhea by the 6th tag win, lost both ways (twice each) | ✓ |
| Save as new | Build, Save asked, saved as "Mekk-Knight Invoked (new)" and made the campaign deck; the old one kept; a circuit duel with the new deck; the deck picker back to the old one | ✓ after fixes 2 and 3 |

*(Screenshot guests-screen removed 2026-11-08: it showed official card art.)*

## Bugs found and fixed

### 1. Aleister the Invoker was treated as a hand trap (high: AI)

The rule from part 1 that keeps hand traps like Ash Blossom in hand also caught **Aleister the Invoker**: he has a
hand effect too, though his job is the Normal Summon that fetches Invocation. Any Mekk-Knight player not using the
turn planner held Aleister forever ("playbook step skipped: Aleister the Invoker is a hand trap"). That was your
stand-in in every tag duel (it opened with Magical Meltdown, then passed) and every CPU below planning skill. Rhea's
team won on turn 2 twice that way.

**Fixed:** a monster that does its work when Summoned ("When this card is Normal Summoned") isn't a hand trap. Only
Aleister changes. Mekk-Knight against Snake-Eye, 100 duels, skilled player: **9% without the playbook / 10% with**
before, **25% / 28%** after.

### 2. The versus screen undid your deck choice (high)

The circuit versus screen remembered the deck from the previous circuit duel, and starting the duel made that your
campaign deck again. So picking another deck under YOUR DECK on the campaign screen, or Save as new, was silently
undone at the next circuit duel. **Fixed:** each versus screen starts from the deck you play now; only a loaner you
picked there carries over (and the deck arrows keep their pick).

### 3. Build kept the old deck's name and story (medium)

Build replaced the cards but kept the old deck's title, story and combo starters, so the new deck was shown as
"Mekk-Knight Invoked" next to the real one (Kit "with Mekk-Knight Invoked" was really the Build deck). **Fixed:** like
Recipe, Build drops them; the opening-hand panel asks you to mark new starters.

*(Screenshot save-as-new removed 2026-11-08: it showed official card art.)*

### 4. Small things

| Where | What | Fixed |
|-------|------|-------|
| Deck names | "Mekk-Knight Invoked (new)" showed as "Mekk Knight Invoked (new)": the file-slug-to-words rule ("stall-burn" to "Stall Burn") ran on names you typed | Only lowercase slugs are reworded |
| Guests | A deck ready for your current circuit (TCG April 2015) was refused because the last circuit cleared (TCG March 2011) forbids Black Luster Soldier - Envoy of the Beginning | Guest duels take a deck ready for either |

*(Screenshot partner-bond removed 2026-11-08: it showed official card art.)*

## For you to decide

- **Rhea arrives too early.** Six tag wins come by the end of the Novice band, but her Thunder Dragon deck is one of the
  strongest in the game (Elo 1728, 9th of 78): she won all four duels, twice on turn 2 or 4. Options: unlock her
  at 10 or 12 tag wins, or give her a mid deck until the Advanced band. **Decided: she now unlocks at 12 tag wins.**
  Nocturne (HAT, 1580) after the Novice band
  felt right; Ines (Goat Control) after the lessons was a fair first guest.
- **Kit and your only deck.** With one campaign deck, Kit played a copy of yours. **Decided and done:** Kit plays
  one of your other campaign decks, or Kit's own Synchrons while you have only the one you play.
- **Guest portraits.** The five new characters show an initial; they need pictures in `art/portraits/`.
- **DP.** 24,860 DP left after 4 tokens and 2 pass slots by Level 15: still plenty, but the token price climbs
  500 each time, so it will absorb more later.

*(Screenshot trader-dp removed 2026-11-08: it showed official card art.)*

## Running it again

`"Duel Circuit" -mods <empty folder> -pics <your card pictures> -qaplay <out> -qagames 45 -qafull` plays a fresh
campaign with all of the above; add `-qacontinue` to pick up the save in `-mods` (it makes the second deck first).
