# Persona walkthrough: the first hour (2026-10-30)

The agency-agents **Persona Walkthrough** agent (`~/.claude/agents/design-persona-walkthrough.md`, adapted from web
pages to a game's first hour) played through 49 screenshots of the real build, in play order. The screenshots come from a
12-duel `-qaplay -qafull` run on 15aa5bd (0 issues). It played them as two newcomers. **This is a simulation, not user data:**
the findings are hypotheses for a real playtest.

- **Dana, 32:** played Yu-Gi-Oh as a kid (2004-2008) and quit; nostalgic, afraid modern Yu-Gi-Oh is
  incomprehensible; has 45 minutes a night.
- **Sam, 26:** plays Hearthstone and Marvel Snap, has never played Yu-Gi-Oh; impatient with text; quits if lost in the
  first 10 minutes.

## Verdicts

| | Confidence | Clarity | Relevance | Keeps playing past the first hour? | Most likely quit point |
|---|---|---|---|---|---|
| Dana | 7 | 5 | 8 | Maybe | End of night one, during the chain lessons' jargon ("Link 3 resolved first, negating Ash at link 2") |
| Sam | 6 | 4 | 6 | Probably not | His first unguided turn: a hand of Mekk-Knight combo cards with no hint |

The first 18 screens work for both: the warm mentor, the short guided duel, a clear Duel button. The hour falls apart
where guidance stops (first circuit duel), unlocks pile up (the first circuit's results), and modern jargon arrives
(lessons, deck editor).

**Fixed the same day** (quick wins, before the night's playtest):
- Finding 1, in part: Gus's "always" line.
- 6: the Lessons count.
- 8: Kit's lines and the versus layout.
- 9: the starter screen and the Era mode name.

Still open: the rest of 1, plus 2-5, 7 and 10.

## Findings, ranked (checked against the screenshots and code)

| # | Finding | Who | Checked | Fix | Effort |
|---|---|---|---|---|---|
| 1 | Guidance stops after the first duel. Gus says "The green bar up top **always** says what to do next", but that bar exists only in lessons; in circuit duels playable cards glow gold, with no text hint. | Both | **Yes**: the line is in `ChainLessons.cs`; the stills are taken at turn changes, so they can't show prompts in the middle of a turn | A hint line through the Beginner Circuit (toggle in settings), or at least fix Gus's "always" | Medium (line: quick) |
| 2 | Rules are taught over a black screen before the board appears; the Extra Monster Zones are never explained | Both | Yes | Teach on the live board with highlights; one line on the middle zones | Medium |
| 3 | Five unlocks land at once on the first circuit's results (level, recipe, Card Token, new circuit, new sets); the Card Trader has no button in the campaign bar | Both | Yes (the campaign bar is Guests, Lessons, Recipe Book, Card Shop, New game, Menu) | One unlock at a time, each with its button; Trader in the bar | Quick-medium |
| 4 | The deck editor shows every advanced tool at once (ban list, Ban Passes, "Play 100 games", Hand trap and Negate filters) | Both | Yes | A simple default editor, with an "Advanced" toggle | Big |
| 5 | The guest Ines is open from the start with no skill rating or warning, and a loss says only "Patience. It's always patience." | Both | Yes: no skill line on the guests screen | A skill rating, "Recommended after: Novice band", one line on what beat you plus a lesson to try (tools, not a weaker Ines) | Medium |
| 6 | The difficulty rises at the Rookie Cup while Lessons has no badge (the Recipe Book has one) | Both | Partly | "Lessons (3 new)" badge; an offer card before the cup | Quick |
| 7 | "New game" sits in the main bar | Both | **Mostly not an issue**: it opens its own screen of options before anything resets | Optional: move it under Menu | Quick |
| 8 | Kit says the save name aloud: "I play Mekk-Knight Invoked Evolved (new)"; her quote "No spare? I brought my Synchrons." plays while she's using your deck; the tag versus screen cuts your deck name off under the arrows, and the quote runs into the hint line | Both | **Yes** (log line, `48-g11-b-versus.png`) | Strip "(new)" in dialogue, choose her quote based on where the deck came from, wrap the deck line, move the quote clear of the hint | Quick |
| 9 | Starter deck screen: the Mekk-Knight blurb is all jargon (hand traps, Kaijus, Super Polymerization) yet says "The early circuits will be easy", which lures Sam onto the hardest deck to pilot; "Historic challenge (era-limited cards)" reads as a challenge, not Dana's comfort mode | Disagree | Yes | Story before the deck pick; plain blurbs with a complexity rating; mark a "Best first deck"; rename the toggle "Classic cards only" | Quick |
| 10 | The lessons say "Link 3 resolved first, negating Ash at link 2" (Link is also a monster type; Ash is never introduced); Sam never opens them; nothing covers Extra Deck summons | Disagree | Yes | "1st/2nd/3rd card in the chain" with a visual stack and full card names; an Extra Deck lesson; a short in-duel version the first time a chain happens | Medium |

