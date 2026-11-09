# The last circuits' decks: turn order, combos, card timing (2026-10-27)

The decks of the last two circuits, Master League: Houston 2026 (Mitsurugi, Light and Darkness, Branded Despia) and
the Tier 0 Gauntlet (Eldlich, K9 Vanquish Soul, Tearlaments, Ishizu Tearlaments), are the seven strongest decks in the
game by our ratings. This combines what the decks are built to do (guides and event coverage, linked at the end) with
our own measurements of how the CPU plays them, and says where the two agree and where they don't.

## Going first or second

Three measurements:
- **Against the whole field** (79 decks, 2026-10-26; [quick](../../ratings/first-second-quick-2026-10-26.txt) and
  [planning CPUs](../../ratings/first-second-planners-2026-10-26.txt)).
- **Against each other** (new: `--round-robin`, each pair 12 times with the first turn alternating, every side played
  like the late CPUs: skill 1.0, the turn planner and the deck's playbook). Raw: `build/round-robin-late.txt`.
- **What the guides say** about each deck.

| Deck | Field, planners: first / second | Each other: first / second | Guides |
|------|---------------------------------|----------------------------|--------|
| Ishizu Tearlaments | 85% / 90% | 52% / 69% | Strong either way; it interrupts on your turn |
| Branded Despia | 77% / 93% | 44% / 72% | First, building a Fusion board; second it breaks boards with Branded Fusion |
| Tearlaments | 78% / 93% | 50% / 66% | Can win on its own going first; Kaleido-Heart interrupts on your turn |
| Mitsurugi | 79% / 84% | 44% / 58% | First: one Ryzeal Mitsurugi player reports 77% first, 40% second |
| Light and Darkness | 86% / 86% | 36% / 66% | Control and grind; its bosses are hardest to answer once set up |
| K9 Vanquish Soul | 73% / 85% | 19% / 52% | Either: one player reports 92% first, 85% second |
| Eldlich | 85% / 79% | 25% / 41% | Mixed; floodgate traps are at their best set before you move |

**Where we disagree with the guides:** in our game every one of these decks wins more going second, by 14 to 33
points against the others. Real players mostly want to go first with them. The 2026-10-26 study found the reason:
our CPUs build smaller turn-1 boards than a real player does, so the sixth card and the first attack are worth more
than a free setup turn. A deck that wins mostly on the opponent's turn (K9 Vanquish Soul's quick effects, Light and
Darkness's grind) shows the biggest gap.

**What it means:**
- **For you, the player:** against these CPUs, go second when you win the toss. Against every one of them, going
  second won more (from 47% against Ishizu Tearlaments to 80% against K9 Vanquish Soul). Their scouting reports now
  say so.
- **For the CPUs:** they still choose to go first with these decks, as the real decks would. Switching them to second
  would make them stronger in our game right now (permitted by the difficulty rule), but it hides the real gap. The
  lasting fix is better turn-1 boards in their playbooks, which is #2 on the saved list.

## The decks

### Mitsurugi (Houston)
- **Our list** is a Mitsurugi-Chaos hybrid: one each of Habakiri, Saji, Aramasa and Murakumo, with Black Chaos, Dark
  Magician and the Diabellstar package. Kusanagi, Futsu no Mitama and Great Purification are in its side deck only.
- **The engine** (the official guide): every Mitsurugi Ritual Monster comes back when Tributed, so each Ritual pays
  for the next. Habakiri calls Saji, Saji finds Mitsurugi Ritual, Aramasa finds Ame no Murakumo, Kusanagi returns the
  Ritual. The full line ends on Habakiri, Murakumo and Futsu no Mitama with a set Trap.
- **Card timing:** Murakumo's wipe happens on summon, once per turn. Its best use is on the opponent's turn: Tribute
  it with a set Mitsurugi Prayers or Great Purification and bring it back to wipe their board.
- **Where our CPU differs:**
  - It plays Mitsurugi Ritual on turn 1 even into an empty board, which spends Murakumo's wipe on nothing.
  - It only uses Mitsurugi Prayers on its own turn, which is the opposite of the guide's timing.
  - Both are worth changing (recommendation 1).

### Light and Darkness (Houston; Seraph's deck)
- **The engine:**
  - Ragged Records of Rites finds Celtic Mystic, which draws three and discards two.
  - The discards are the payoff: Skull Archfiend of Chaos sent to the GY finds a Ritual Monster, and Griffoh sets Mind
    Shuffle.
  - Light and Darkness Ritual then summons Black Chaos or the Soldier, more than once a turn.
- **Card timing:**
  - Discard Skull Archfiend and Griffoh to Celtic Mystic, never the Ritual pieces. Our playbook already does this.
  - Mind Shuffle is the key card; one write-up puts the core list's odds of opening with access to it at about half.
- **What it means for you:** Black Chaos and the Soldier of Light and Darkness ignore your activated effects unless they
  target them. A board wipe does nothing to them; Infinite Impermanence, Effect Veiler or battle do. That's now in its
  scouting report.

### Branded Despia (Houston)
- **The engine:** Branded Fusion makes Albion from the Deck, which fuses again into Mirrorjade or Lubellion. The Albaz
  Fusions sent to the GY pay off at the End Phase.
- **Card timing:** the guides disagree. Older guides open with Branded Fusion. A newer one builds a "manual" Fusion
  with Blazing Cartesia and Albaz first and keeps Branded Fusion as a backup or a board breaker going second.
- **Our CPU** opens with Branded Fusion whenever it has it. At 44% first and 72% second against the field above,
  testing the Cartesia-first line is worthwhile (recommendation 2).

### Eldlich (Gauntlet)
- **The engine:** a Trap deck that grinds. Eldlich sends any card on the field to the GY and comes back from the GY.
  Two Level 10s make a Rail Cannon for the finish.
- **Card timing:**
  - Gozen Match and Skill Drain are floodgates, best set before the opponent acts.
  - Cursed Eldland searches every turn.
- **Turn order:** our numbers are the weakest of the seven against this field, 25% first and 41% second. The guides
  are split. Against the whole campaign it does slightly better first (85% / 79%), which is where its going-first
  choice came from.

### K9 Vanquish Soul (Gauntlet)
- **The engine:**
  - Razen and Dr. Mad Love search when Normal Summoned.
  - Heavy Borger and Caesar Valius summon themselves by returning a Vanquish Soul to the hand, which searches again
    later.
  - Revealed Attributes power quick effects on your turn, and K9-66a Jokul brings two Level 5s for K9-17 "Ripper".
- **Turn order:** the biggest gap of the seven (19% first, 52% second). Its strength is interaction on the opponent's
  turn, which going first wastes while our CPU's turn-1 board stays small.

### Tearlaments and Ishizu Tearlaments (Gauntlet)
- **The engine:** every Tearlaments sent to the GY by an effect Fusion Summons, so milling is the plan. The Ishizu
  build adds Kelbek and Agido, which mill five from both Decks.
- **Card timing:** the guides stress making Kaleido-Heart and Kitkallos on the opponent's turn to interrupt and
  search. Our playbook prefers Rulkallos, then Kaleido-Heart, then Kitkallos for the GY Fusion.
- **What beats them:** banishing (D.D. Crow, Macro Cosmos, Dimension Shifter, the Bystials). It's also the main reason
  Bystials appear in almost every deck here.

## Recommendations

1. **Mitsurugi playbook:** *Measured, no change kept.*
   - Prayers on the opponent's turn was built on 2026-10-27 (c472e84): Mitsurugi 55% to 56%.
   - Holding Mitsurugi Ritual until they have monsters, instead of on turn 1 into an empty board (2026-10-29,
     `--playbook-ab` against Branded Despia, Light and Darkness, Eldlich, K9 Vanquish Soul, Tearlaments and Ishizu
     Tearlaments, 144 duels, same seeds): 51% either way (74 of 144). Turn 1's Ritual wastes Murakumo's wipe but puts
     Murakumo out for the Prayers wipe on their turn; the two cancel out. The current play stays.
2. **Branded playbook:** *Not possible with our list.* Blazing Cartesia only summons itself from the hand while Fallen of
   Albaz is on the field or in the GY, so it can't come before Branded Fusion unless Albaz is Normal Summoned first.
   Our list is 60 cards with one Albaz and one Branded Fusion: about 1 opening hand in 1,000 holds Albaz, Cartesia and
   Branded Fusion or Aluber (0.1%). A route for it wouldn't change results, so none was written.
3. **Turn order for these CPUs:** keep going first (authentic), or switch to second (what wins in our game today).
   It's a design choice for you; better turn-1 boards would make it moot.
4. **Better turn-1 boards** for these seven decks first, since they're the ones the player meets last.

## Sources

- [Konami: LD26 Mitsurugi guide](https://www.yugioh-card.com/eu/ld26-mitsurugi-guide/)
- [Yu-Gi-Oh! Organization: Chaos Ritual four ways](https://ygorganization.com/cds_chaosritualx4)
- [Konami: YCS Houston 2026 Top 8 pairings with deck types](https://yugiohblog.konami.com/2026/ycs/ycs-houston-top-8-pairings-with-deck-types-advanced/)
- [Master Duel Meta: Ryzeal Mitsurugi, July 2026](https://www.masterduelmeta.com/top-decks/master-i/july-2026/ryzeal-mitsurugi/le-saint-karma/8kPRO/)
- [Master Duel Meta: Branded Despia notes and combos](https://www.masterduelmeta.com/top-decks/master-i/december-2023/branded-despia/pulover/8gt0x/)
- [YGOPRODeck: Theater of the Damned (Branded)](https://ygoprodeck.com/deck/theater-of-the-damned-726162)
- [Master Duel Meta: Vanquish Soul K9, March 2026](https://www.masterduelmeta.com/top-decks/master-i/march-2026/vanquish-soul-k9/prag-c/DNEjJ/)
- [Konami: YCS Top 8 feature match (Vanquish Soul K9)](https://yugiohblog.konami.com/2025/ycs/top-8-feature-match-matthew-vanden-heuvel-vs-kassim-hakim/)
- [Master Duel Meta: Eldlich, March 2024](https://www.masterduelmeta.com/top-decks/master-i/march-2024/eldlich/mattdawg42/2eOD3)
- [YGOPRODeck: Eldlich Zombies](https://ygoprodeck.com/eldlich-zombies/)
- [Yugipedia: Tearlaments](https://yugipedia.com/wiki/Tearlaments)
- [YGOPRODeck: Invoked Elysium (Ishizu Tearlaments)](https://ygoprodeck.com/deck/invoked-elysium-tearlaments-ishizu-bystial-fiendsmith-563087)
