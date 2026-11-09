# Going first or second: which decks want which (2026-10-26)

Until the coin toss, you always went first and every CPU always went second. This study measures every deck (79,
including the structure decks and the starters) both ways, so the CPU picks well when it wins the toss and the toss
screen can tell you when your deck wants to go second.

## How it was measured

`--rate-decks` pairs decks at random and alternates who goes first; each deck's wins are now split by first and
second. Two runs:
- **Quick CPUs** (no turn planner, how most early opponents play): 300 rounds, about 150 duels per deck each way.
  Raw: [ratings/first-second-quick-2026-10-26.txt](ratings/first-second-quick-2026-10-26.txt).
- **Planning CPUs** (`--skilled`: skill 1.0 with the turn planner, how the Expert band and later play): 100 rounds,
  about 50 duels per deck each way. Raw: [ratings/first-second-planners-2026-10-26.txt](ratings/first-second-planners-2026-10-26.txt).

## The big finding: going second is better almost everywhere

The average deck wins **4 points more going second** with quick CPUs and **8 more** with planning ones. In the real
game going first is usually the edge. Here the CPUs get less out of a free turn 1 (they build smaller opening boards
than a real player) than out of the sixth card and the first attack. That's worth its own look: better turn-1 setups
for the playbooks would make going first mean what it does in the TCG.

## What the ratings can't tell: how our bots play a deck, not what it wants

A first list took every deck clearly above that average in both runs: seven more decks (Exosister, Danger! Dark World,
ABC, Unchained, Synchron, Blackwing, Six Samurai). Checked against how those decks are built and played, it doesn't
hold. Five of the seven have **no playbook**, so the CPU plays them card by card and never builds the turn-1 board
they're made for (Exosister's Magnifica, ABC-Dragon Buster, Black-Winged Assault Dragon, a Gateway of the Six swarm).
Going first looks bad for them because of the bot, not the deck. Being able to win going second isn't wanting to.

## The decks that go second

Only the decks whose own cards need the opponent's board or a Battle Phase:

| Deck | Quick: first / second | Planners: first / second | Why |
|------|-----------------------|--------------------------|-----|
| Tenpai Dragon | 55% / 67% | 55% / 65% | Clears the board and Synchro Summons in the Battle Phase, which turn 1 doesn't have |
| Mekk-Knight Invoked | 59% / 68% | 49% / 68% | Mekk-Knights summon themselves into columns holding the opponent's cards |
| Mekk-Knight (structure) | 40% / 51% | 33% / 30% | Same mechanic (the planner run's 50 duels are noisy) |

Everything else goes first. Unchained, Danger! Dark World and ABC are flexible: they prefer first but break boards
well when they lose the toss (Unchained takes the opponent's monsters as Link Material; Danger! discards and removal;
ABC's banishing). Exosister, Blackwing, Synchron and Six Samurai want to set up first. Eldlich too (81% / 82% quick,
85% / 79% with planners): its floodgate traps (Skill Drain, Imperial Order, Gozen Match) want to be set before the
opponent moves.

**The follow-up this points at:** playbooks with real turn-1 lines for the decks that have none (Exosister, Unchained,
ABC, Blackwing, Six Samurai). That would make going first pay for them as it does in the TCG, and shrink the general
going-second edge above.

### Done: playbooks for the five

Each now has a playbook that builds the turn-1 board the deck is made for: Exosisters Magnifica (Martha, Pax, Mikailis
and Kaspitell searching on the way), Unchained's D/D/D Wave High King Caesar + Unchained Abomination (Traps Set first,
then destroyed for their summons), ABC-Dragon Buster or Gear Gigant X, Blackwing Armor Master (Black Whirlwind first,
Gale searched), and Legendary Six Samurai - Shi En (Gateway and United up before Kageki brings Kagemusha). Quick CPUs,
300 rounds on three seeds each (about 450 duels per deck each way). Raw (seed 1):
[ratings/five-playbooks-quick-2026-10-26.txt](ratings/five-playbooks-quick-2026-10-26.txt).

| Deck | Going first, before / after | Going second, before / after |
|------|-----------------------------|------------------------------|
| Exosister | 37% / 50% | 48% / 66% |
| Unchained | 53% / 64% | 65% / 75% |
| ABC | 55% / 62% | 57% / 68% |
| Blackwing | 32% / 38% | 34% / 48% |
| Six Samurai | 42% / 50% | 50% / 59% |

Going second rose as much: the playbooks' breakers count there too, and the general going-second edge stays. They all
still go first.

The turn-1 lines turned up CPU mistakes that every deck shares, now fixed:
- Destroying a card of our own as the play (Unchained Twins: "target 1 card you control; destroy it") counted as removal
  with nothing of the opponent's to hit, so it was never played.
- A Spell/Trap's effect from the GY (an Unchained Trap destroyed while Set summons from the Deck) was never used: the
  chain logic had no case for it, and the removal check judged it by the card's other effect.
- A draw or search card the CPU held back as a blunder (Allure of Darkness with one DARK in hand) still counted as
  "play this first", which skipped the deck's playbook for the whole phase.
- Pot of Prosperity's cost banished the best Extra Deck monsters (Magnifica, Mikailis) first; now the ones never summoned.
- Solemn Judgment's "save it for a real threat" check looked for "pay half your LP" and the card says "Pay": it was spent
  on anything (3500 LP to stop Command Knight).
- Removal aimed at the opponent's resolving Normal Spell (Returnia on a chained Terraforming, which resolves anyway).

Playbooks can also pick one effect of a card with several (`ActivateEffect` / `HoldEffect`: Gateway of the Six's
4-counter search, not its +500 ATK) and make a preference conditional (`PreferIf`: Pax searches Stella once Martha is
in hand). Other decks moved by at most 3 points, about the share the five's gains take from them.

The list lives in `Playbooks.SecondDecks` (src/Ygo.Engine/AI/Playbooks/Playbooks.cs) with each deck's reason; the
CPU's toss choice and the toss screen's hint ("Your deck works best going second: ...") read it. Campaign copies count
as their deck ("Mekk-Knight Invoked (3)", "Stall Burn").

## Everything else retested with both orders

| What | Before | Now |
|------|--------|-----|
| Deck ratings, campaign sim, difficulty tests, the editor's bot tests | Already alternated who starts | Unchanged, still valid |
| Blunder audit | Random pairs in both seats | Unchanged, still valid |
| Harness replay and tag tests | Seat 0 always started | Alternate; the tag test checks B1, A1, B2, A2 too |
| In-app checks | You always first | `-gosecond` runs them with the CPU first: all pass (two scenarios built on your turn 1 keep you first) |
| The campaign itself (QA stand-in) | You always first | Re-run from a fresh save with the toss: see the QA report |

## A bug the second-player duels found

Facing Tenpai's Sangenpai Transcendent Dragion ("your opponent's monsters must attack"), the Battle Phase allows
neither Main Phase 2 nor the End Phase. The CPU kept trying to leave anyway and the duel was abandoned after 200
refused answers: 24 of Tenpai's rating duels and 9 of Yubel's. Fixed: a forced attack sends the strongest monster at
the best target. 0 abandoned duels in 4,700 duels after.
