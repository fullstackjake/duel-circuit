# Duelist expressions

During a duel the opponent (and a tag partner) reacts on their plate by the LP: a speech bubble with their line,
a colored flash, and a change of face. The bubbles and flashes work for everyone already. The faces need art: three
expressions per character, on top of the normal portrait.

| Expression | When it shows | Look for |
|---|---|---|
| **smug** | they bring out a big monster, or hit you hard | confident grin, raised chin, a pointing or taunting hand |
| **hurt** | they take a big hit (1500+ damage) | wince, eyes squeezed or wide, leaning back, arm up |
| **desperate** | their LP falls to 1000 or less (stays while they're there) | sweating, gritted teeth, intense or panicked eyes |

## What to send

The easiest is **one 2x2 sheet per character**, all four in the **same pose, outfit and framing**:

| top-left | top-right |
|---|---|
| normal | smug |
| **bottom-left** | **bottom-right** |
| hurt | desperate |

Same as the portraits before: no names or text on the sheet, a plain background, and I'll cut them out and line them
up. The sheet's normal face replaces the current portrait so all four line up exactly (the plate shows head and
shoulders, so the head must stay in the same spot in all four).

Already cut out? Name them `<id>_smug.png`, `<id>_hurt.png`, `<id>_desperate.png` (transparent PNG, the same size and
framing as `<id>.png`) and put them in `art/portraits/` (shipped) or `mods/portraits/` (yours only).

Shared prompt to start with:

> Character expression sheet, 2x2 grid, the same anime-style character in all four panels with identical pose,
> outfit, framing and lighting, waist-up, plain light background, no text: top-left neutral, top-right smug confident
> grin, bottom-left hurt wincing, bottom-right desperate sweating with gritted teeth.

Then add the character's look (below).

## Who first

The rival, the mentor and every circuit boss (they already have their own lines in `personalities/<id>.profile`).
Everyone else uses lines that fit their style, and can get faces later.

| Id | Who | Look |
|---|---|---|
| `vera` | Vera, the Vanguard | Athletic young warrior-type in a red jacket with rolled sleeves, a ponytail, and a sword-shaped keychain on her deck box. |
| `old-gus` | Old Gus, the Fortress | Grizzled shop owner in a cardigan with elbow patches, reading glasses on a cord, and a deck box older than most customers. |
| `rookie-ryo` | Ryo, the Rookie | Eager teen in a school uniform with untied shoelaces, a brand-new deck box, and a notebook full of combos. |
| `tink` | Tink, the Tinkerer | Young inventor in a tool belt and oversized goggles, with red, green and yellow wind-up robots peeking from her pockets. |
| `hollis` | Professor Hollis, the Patient | Tweed-jacketed professor with round spectacles, a neat grey beard, and a stack of annotated rulebooks. |
| `burning-abyss` | Nerio Malebranche, of the Ninth Circle | Gaunt poet in a long charcoal coat with ember-red lining, ink-stained fingers, and a battered book of verse under one arm. |
| `altergeist` | Ione Wraith, the Trap Haunter | Pale, hooded figure with a glitching holographic visor and cables trailing from her sleeves like ghostly ribbons. |
| `yummy` | Chef Praline, of the Yummy Way | Bubbly pastry chef in a frilly apron and a tall chef's hat, carrying a tray of tiny desserts. |
| `infernity` | Ash Hollow, the Empty Hand | Hollow-eyed drifter in a torn dark coat with empty pockets turned out, holding nothing at all, on purpose. |
| `gouki` | Coach Gouki, of the Link Ring | Burly wrestling coach in a tracksuit and a championship belt, whistle around his neck, knuckles taped. |
| `maliss` | White Rabbit Maliss, of the Cyberse Wonderland | Girl in a cyber-punk Alice dress, white rabbit ears on a headset, and neon-pink circuit lines on her stockings. |
| `dragon-ruler` | Ignatia Blaze, of the Four Rulers | Fiery-haired adventurer in a four-colored elemental jacket (red, blue, green, brown) with dragon-tooth earrings. |
| `true-draco` | King Barrund, of the True Draco | Massive, bearded king in dragon-scale armor and a crown of horns, who never seems to leave his seat. |
| `unchained` | Abomination Rex, the Unchained | Hulking duelist bound in broken chains, wearing a cracked iron collar and a grin that says he snapped them himself. |
| `danger-dark-world` | Dottie Larkin, of the Dark World | Bright-eyed cryptid hunter in a rain-soaked parka covered in patches, binoculars around her neck and a blurry photo in her pocket. |
| `tenpai-dragon` | Kenta Mizorogi, the Tenpai Rush | Hot-blooded street duelist in a dragon-print bomber jacket with a mahjong tile hanging from a chain. |
| `elfnote` | Linden, the Elfnote Conductor | Slender conductor in a midnight-blue tailcoat, holding a slim baton, with tiny pointed ears and a perfect posture. |
| `spright` | Zia Sparks, of the Level Two | Hyperactive kid in a hoodie with electric-blue streaks, two tiny spark-charms clipped to her bag. |
| `k9-vanquish-soul` | Rafael Lezcano, of the K9 Unit | Sharp detective in a long trench coat with a canine-unit badge and a scarred hound-head emblem on his gloves. |
| `branded-despia` | Corin Vasse, of the Branded | Pale young man with a tattered dragon-scale cloak and one gold-burned arm, carrying himself like a fallen knight. |
| `ishizu-tearlaments` | Kelda Amin, the Archivist | Dark-skinned archivist in her forties: a charcoal blazer over a rust turtleneck, reading glasses pushed up into her braids, a tablet of old tournament records under one arm. |
