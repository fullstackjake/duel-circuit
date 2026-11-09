# Mekk-Knight Invoked: how the deck plays

The analysis behind the CPU's playbook for this deck (`src/Ygo.Engine/AI/Playbooks/MekkKnightPlaybook.cs`), which
your stand-in in QA play and any CPU piloting the deck both use.

## The engines

**Invoked (the boss maker).** Terraforming finds Magical Meltdown; Meltdown adds Aleister the Invoker; Aleister's
Normal Summon adds Invocation. Invocation fuses Aleister with any LIGHT monster (every Mekk-Knight is LIGHT) into
**Invoked Mechaba**, which negates a card once per turn. Under Meltdown the Fusion can't be negated or answered.
Next turn Invocation shuffles itself back from the GY to return the banished Aleister, so the line repeats. When
Invocation can banish materials from the GY, that's what it should use (the field and hand stay intact).

**Mekk-Knights (free bodies).** Each Special Summons itself from the hand into a column holding 2 or more cards.
- Blue Sky searches as many other Mekk-Knights as the opponent has cards in its column: summon it into their busiest
  column.
- Purple Nightfall banishes a Mekk-Knight until your next Standby Phase to search another.
- Red Moon and Yellow Star destroy an opponent's monster or Spell/Trap in their own column (never yours).
- Indigo Eclipse moves a Mekk-Knight to line up those columns; Green Horizon gets one back from the GY on attack.

**World Legacy (support).** Key returns a banished Mekk-Knight; Secret revives a Level 5+ (a Mekk-Knight or Mechaba);
Succession revives into a Link's zone; Monstrosity calls two Level 9s. Their column-negation clauses protect the
board.

## The bosses, in order

1. **Invoked Mechaba** (Aleister + a Mekk-Knight). Elysium later, from an Invoked + an Extra Deck monster (best from
   the GY).
2. **Black Luster Soldier - Soldier of Chaos** from three spare monsters with different names. Use a Level 7+
   Mekk-Knight (Purple Nightfall, Indigo Eclipse, Red Moon, Yellow Star) among them: it then can't be targeted or
   destroyed by the opponent's effects.
3. **True King of All Calamities**: World Legacy Monstrosity on Mechaba calls Knightmare Incarnation Idlee and World
   Legacy Guardragon Mardark (both differ from Mechaba in Type and Attribute); those two make True King. Mechaba stays.
4. **Mekk-Knight Crusadia Avramax** last, from two spare Extra Deck monsters (Morning Star, Lib, Unicorn, a spare
   Invoked): it draws every attack and can't be targeted. It needs a lot on the field, so it's rare.

**Never spend a boss as material** for something lesser (Black Luster Soldier into Knightmare Unicorn, Mechaba into
a Link-2). The exception is a boss that's stuck: one a card like Nightmare Wheel or Spellbinding Circle targets
(it can't attack, or is negated). A lock on everything, like Swords of Revealing Light, isn't that: it ends.

Morning Star (a World Legacy search) is for when there aren't three bodies for the Soldier. Pinpoint Landing goes
down before the Mekk-Knights land, for a card each time one arrives alone.

## Removal and answers

Gameciel takes an opponent's monster away (it's Tributed). Super Polymerization fuses with the opponent's monster
too, and nothing can respond. Raigeki, Dark Hole and Lightning Storm clear boards (Lightning Storm needs no face-up
cards of yours). Mechaba, Infinite Impermanence, Ash Blossom and Red Reboot are the interruptions; keep Ash in hand,
don't Set it as a monster.

## What the CPU now does with it

- Builds the bosses in that order, keeps them, and puts the Soldier together from Level 7+ Mekk-Knights.
- Never uses a removal effect on its own card (Red Moon on its own monster, MST on its own Spell/Trap).
- Summons Aleister the Invoker for Invocation. (From the night of 2026-10-25 until the next day a hand-trap rule
  wrongly kept him in hand: Mekk-Knight beat Snake-Eye only 9-10% of the time then.)
- In 100-game tests against Snake-Eye with a skilled player: 25% without the playbook, 28% with (2026-10-26; an earlier
  measurement gave 26% to 36%, so the playbook's edge is a few points, within the noise of 100 games). Even against
  Fire King Snake-Eye and Yubel, and slightly fewer quick wins against weak decks (Gadgets 88% to 82%): fewer,
  bigger monsters.
