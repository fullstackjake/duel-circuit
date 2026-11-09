# Duelist panels: what similar games do, and what ours needs (2026-10-27)

The duelist panel (portrait, name, LP, LP bar) is the one piece of the duel screen the player checks every turn. This
looks at how other games handle portraits and health, sets out what our panel has to do for the player, and ends with a
design built from those requirements.

**Sources:** where a source documents something, it's linked. Konami doesn't publish its duel HUD design, so the
notes on Master Duel, Duel Links and Tag Force also draw on how those screens behave in play; those lines are marked
*(observed)*.

## What other games do

### Yu-Gi-Oh! games

| Game | Portrait | Life points | What's worth borrowing |
|------|----------|-------------|------------------------|
| **Duel Links** | Cropped character art during matches, with expression variants and a separate defeat pose for hitting 0 LP ([The Cutting Room Floor](https://new.tcrf.net/Yu-Gi-Oh!_Duel_Links), via search) | A bar with the number *(observed)*; 4,000 LP duels ([Yugipedia](https://yugipedia.com/wiki/Yu-Gi-Oh!_Duel_Links)) | Expressions that change with the duel, and a real defeat moment |
| **Master Duel** | A small player icon; a "Mate" companion beside your LP ([gameplay.tips](https://gameplay.tips/guides/yu-gi-oh-master-duel-duel-screen-info-guide-duel-field-menu-and-icons.html)) | A large number that counts down with a sound and a damage number *(observed)* | The LP roll and sound as the payoff of a hit; the number is the hero |
| **Tag Force** | A bust portrait beside the LP *(observed)* | Number with a gauge *(observed)* | Our game's model: the character is the identity, with reactions |

Reviewers single out Master Duel's sound and summon animations as what makes plays feel substantial ([Cardmarket
Insight](https://insight.cardmarket.com/en/Insight/Articles/First-Impressions-Yu-Gi-Oh-Master-Duel)).

### Other card games

- **Hearthstone:** health is a number on the hero portrait itself; Armor sits just above it as a silver shield, takes
  damage first, and its icon breaks when it runs out ([Hearthstone Wiki: Armor](https://hearthstone.wiki.gg/wiki/Armor)).
  No bar at all: in a card game the exact number matters more than the proportion. The team treated the interface as
  the whole game, since there's no world to explore, so every element earns a deliberate place
  ([GDC 2015](https://gdcvault.com/play/1022036/Hearthstone-How-to-Create-an), [coverage](https://mein-mmo.de/en/hearthstone-weg-zum-megahit063,32034/)).
- **Marvel Snap:** aims for little clutter and clear board-state feedback through icons, color and visual cues
  ([coverage](https://meeplesherald.com/news/marvel-snap-snaps-at-the-old-card-game-design/)), but its menus drew
  criticism as convoluted ([Twinfinite](https://twinfinite.net/2022/07/5-things-marvel-snap-needs-to-be-even-better-than-it-already-is/)).

### Fighting games (the most refined health bars)

- **Damage you can still see:** Street Fighter V showed chip damage as a faded gray "provisional" segment that
  recovers if you avoid further hits; Tekken's gray bar is recoverable health; Dragon Ball FighterZ uses dark blue for
  it; DNF Duel uses white for recoverable and red for permanent loss
  ([Street Fighter Wiki](https://streetfighter.fandom.com/wiki/Health_Meter), [esports.gg](https://esports.gg/guides/tekken-8/health-in-tekken-8-explained),
  [Dragon Ball FighterZ Wiki](https://dragonballfighterz.fandom.com/wiki/Health)). The general pattern (a second bar
  that trails the real one) shows how big the last hit was.
- **Thresholds:** Street Fighter 6's bar turns yellow at 25% health ([Street Fighter Wiki](https://streetfighter.fandom.com/wiki/Health_Meter)):
  one clear "you're in danger" step rather than a gradient.
- **Mirrored, side-colored layout:** player 1 and player 2 each own a side and a color, read at a glance.

### Stylized UI (Persona 5)

- **Hierarchy through size and color:** the selected item grows and gets a splash of blue that contrasts with the
  red, black and white palette; battle actions surround the active character, so the UI itself says whose turn it is
  ([Mechanics of Magic](https://mechanicsofmagic.com/2022/04/23/visual-design-of-games-persona-5/)).
- **Style that still reads:** the team iterated on the angular text boxes specifically to fix readability
  ([Kotaku](https://kotaku.com/-metaphor-refantazio-persona-5-menu-ui-hashino-1851667088)); critics note that bouncing
  menus can make text harder to read ([Siliconera](https://www.siliconera.com/what-makes-the-ui-and-menus-in-persona-games-so-good/)).

### Feedback and accessibility

- **Juice, in moderation:** flash, shake, floating text, sound and particles make a hit feel like a hit (Jonasson and
  Purho, "Juice it or lose it", GDC 2012), but juice for its own sake distracts and can break immersion
  ([summary](https://eastondev.com/blog/en/posts/dev/20260521-game-feedback-feel/)).
- **Never color alone:** critical information shown by color should also have a shape, pattern, icon or text label,
  and players should be able to choose colors where color can't be avoided
  ([Xbox Accessibility Guideline 103](https://devdocs.xbox.com/gaming/accessibility/xbox-accessibility-guidelines/103)).
  A health bar going from green to red is exactly what a deuteranopic player can't see
  ([abratabia](https://abratabia.com/game-accessibility/visual-accessibility.php)).

## What the player needs from the panel

Our duels are Yu-Gi-Oh!: 8,000 LP, damage in hundreds and thousands, one big attack can end it, and players do exact
LP math to find lethal. That shapes the answers.

| # | The player needs to | So the panel must | Today |
|---|---------------------|-------------------|-------|
| 1 | Do exact lethal math | Make the LP number the biggest, clearest thing; the bar only supports it | Number big, bar decorative |
| 2 | Know what just happened | Show how much changed, and whether it was battle damage, an effect, a cost or a gain | Number rolls; no reason shown |
| 3 | See how big the hit was | Keep the lost part visible for a moment (the trailing bar) | Done (the pale damage trail) |
| 4 | Feel danger before it's too late | Clear steps: below half, below a quarter, at 1,000 or less; shown by shape and text as well as color | Red pulse at ≤1,000 only, by color |
| 5 | Know whose turn it is | Light up the active panel and quiet the other one | The turn ring only |
| 6 | Feel the opponent is a person | Expressions that react, a defeat moment | Mood flash, bubbles, "desperate" art when it exists |
| 7 | Follow tag duels | See both partners, and which one is playing | Names only ("Ryo & Tink") |
| 8 | Not be distracted mid-combo | Keep motion short and tied to events; nothing loops loudly | Shake and pulse only on events |
| 9 | Play with reduced motion or color blindness | Every state readable without animation or color | Partly |

## What to borrow

| From | Borrow | Our version |
|------|--------|-------------|
| Hearthstone | Health as a number on the portrait | The LP number sits on the panel's face, large, never behind art |
| Hearthstone | Armor as a separate icon that breaks | Not needed now (no armor in Yu-Gi-Oh!); the same slot can hold a status chip later |
| Fighting games | The trailing damage segment | Keep our trail; make it white so it reads against any side color |
| Street Fighter 6 | One clear threshold color change | Steps at 50%, 25% and ≤1,000, each with its own shape cue |
| Duel Links, Tag Force | Expressions and a defeat pose | Hurt on big hits, desperate at ≤1,000 (art already supported), greyed and cracked at 0 |
| Master Duel | The LP roll and sound | Keep the roll; add a "last change" chip with the amount and its reason |
| Persona 5 | The active character's UI stands out | The active panel brightens and tags "Your turn"; the other dims |
| Accessibility guidelines | Shape and text, not color alone | Danger steps add stripes, an icon and a word; the reason chip has an icon |

## The design (built 2026-10-27)

The structure of the angular panel stays (you liked the direction); what changes is driven by the table above.

1. **The LP bar is eight segments of 1,000 LP.** One segment is one thousand: at a glance you can count "three and a
   half left", which is the Yu-Gi-Oh! way of thinking. A partly full segment shows the remainder.
2. **A "last change" chip** slides out beside the number: `−2500` with a sword (battle), a burst (effect), a coin
   (a cost you paid) or a `+` heart (LP gained). It stays until the next change or about 4 seconds.
3. **The trail is white**, held for a moment, then drained, segment by segment.
4. **Danger steps:**
   - above half: the bar in the side's color;
   - below half: amber, one chevron;
   - a quarter or less: red, diagonal stripes on the bar;
   - 1,000 or less: red stripes pulsing, a "!" icon and the word "Low" on the LP tag.
5. **Turn ownership:** the active panel's name tab is lit and carries "Your turn" or "Their turn"; the other panel dims
   by about a fifth.
6. **Expressions:** hurt (a quick recoil and red flash) on a hit of 2,000 or more; the desperate portrait at 1,000 or
   less when the art exists; at 0 LP the portrait greys and a crack runs across the panel.
7. **Tag duels:** the partner as a small portrait chip on the panel's edge; on a swap the two trade places.
8. **Reduced motion:** no recoil, sweep or pulse; every state still shows through color, stripes, icons and words.

### How it's built

All of it uses pieces the board already has: the polygon shapes and masks from the redesign, the LP roll, the damage
trail, the hit shake and the mood system. New: the segment row (eight small polygons), the change chip (a polygon, an
icon from `Icons` and a text), stripes (a small tiled texture) and the portrait grey and crack (a color multiply and
one line). The reason for an LP change comes from the duel messages: LP costs and LP gains are messages of their own,
and damage is battle damage when it lands during damage calculation, effect damage otherwise.
