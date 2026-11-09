# Duel mats

Each half of the duel field has its own mat. The rival (Vera) and every circuit's final opponent have a **signature
mat** painted in code: their own colors and a motif, listed in [`campaign/mats.txt`](../../campaign/mats.txt). Everyone
else gets a plain mat in their deck's main Attribute color.

A picture always wins over the painted mat. The game looks for `<character id>.png` (or `.jpg`; a deck name works too):

1. `mods/mats/` (yours: never committed)
2. `art/mats/` in the repo (shipped with the game, like the portraits; only art you made and may share)

## Making a mat picture

- **Size:** 2048 x 768 (about 8:3). It is stretched over half the field, about 1380 x 515 on screen.
- **Direction:** draw it as its owner sees it, sitting at the bottom edge. The **top edge meets the middle of the
  board**. On the opponent's side the game turns it around, like their cards.
- **Keep it quiet:** cards sit on top in two rows across the middle band, so keep that band low in detail and
  contrast; put the big shapes toward the corners and the edges. The game dims pictures to 75%.
- **No text, logos, card art or real Yu-Gi-Oh! monsters.** Use original motifs only.
- Save it as `art/mats/<id>.png`. It shows up the next time a duel starts (rebuild the app for `art/mats/`).

Shared style to start every prompt with:

> Wide 8:3 tabletop duel playmat, top-down, painterly fantasy illustration, dark and low contrast in the middle
> band, details toward the edges and corners, soft glow, no text, no logos, no characters, no cards.

## Pictures so far

In `art/mats/` (AI-generated art supplied for the project): `vera`, `burning-abyss`, `altergeist`, `infernity`,
`gouki`, `maliss`, `dragon-ruler`, `true-draco`, `unchained`, `branded-despia`. Still painted in code: `rookie-ryo`,
`tink`, `hollis`, `yummy`, `danger-dark-world`, `tenpai-dragon`, `elfnote`, `spright`, `k9-vanquish-soul`,
`ishizu-tearlaments`.

## Prompts

| Id | Who | Prompt (after the shared style) |
|---|---|---|
| `vera` | Vera | Crimson battle banners and crossed sword silhouettes on a scorched red field, golden sunburst rising from the top edge. |
| `rookie-ryo` | Ryo | Deep-blue school notebook paper with faint ruled lines, doodled stars and arrows in light blue chalk. |
| `tink` | Tink | Workshop bench of copper and brass gears, springs and blueprints, warm amber lamplight. |
| `hollis` | Professor Hollis | Old teal library desk, an engraved arcane ring of runes, open annotated books at the corners. |
| `burning-abyss` | Nerio Malebranche | Descending circles of a burning underworld, ember-red flames licking up from the bottom edge, charcoal smoke. |
| `altergeist` | Ione Wraith | Haunted violet static and glitching scanlines, ghostly pink cables drifting like ribbons. |
| `yummy` | Chef Praline | Pastel pink patisserie counter, sprinkles and tiny dessert silhouettes at the edges, sugar sparkles. |
| `infernity` | Ash Hollow | An empty black void, thin red cracks spreading from the center, nothing else at all. |
| `gouki` | Coach Gouki | Top-down wrestling ring in orange and brown, three ropes around the edge, corner turnbuckles. |
| `maliss` | White Rabbit Maliss | Neon-pink circuit board traces on a dark crimson wonderland floor, playing-card suit shapes, a pocket watch. |
| `dragon-ruler` | Ignatia Blaze | Overlapping dragon scales in four elemental tints (fire, water, wind, earth), one per corner. |
| `true-draco` | King Barrund | A golden royal seal with interlocked triangles on dark gold stone, horned crown motifs around it. |
| `unchained` | Abomination Rex | Purple dungeon flagstones crossed by two snapped iron chains, cracked stone. |
| `danger-dark-world` | Dottie Larkin | Misty green swamp at night, fog banks, a blurry long-necked silhouette in the far corner. |
| `tenpai-dragon` | Kenta Mizorogi | Golden-red dragon flames along the bottom edge, scattered mahjong tile shapes (blank faces). |
| `elfnote` | Linden | Emerald concert hall floor, a curving five-line music staff with floating notes, soft stage glow. |
| `spright` | Zia Sparks | Electric-blue night sky with crackling small sparks and four-pointed stars. |
| `k9-vanquish-soul` | Rafael Lezcano | Navy police-precinct floor, yellow-and-black hazard stripes on the far edge, chevrons pointing to the middle. |
| `branded-despia` | Corin Vasse | A burned golden brand-sigil in a ring on dark wine-red stone, pale ash drifting. |
| `ishizu-tearlaments` | Kelda Amin | Teal tidal waves washing over scattered archive pages and index cards, faint ruled lines on the paper. |

Your own mat: `you.png` (or your deck's name, for one mat per deck).

## Era mats

Once you clear a Library cup you can buy that format's mat for your side of the board (seat 0). Like the signature
mats they are painted in code from [`campaign/mats.txt`](../../campaign/mats.txt) (the "Era mats" section), keyed
`era-<cup slug>`; a picture at `art/mats/<id>.png` (or `mods/mats/`) would replace one. Each motif is drawn on its
own, with no card art or real monsters, and stays faint in the middle band. `eclipse`, `halos` and `arrows` were
added for these mats.

| Id | Look | Intent |
|---|---|---|
| `era-goat-cup` | `eclipse`, old gold on tournament-felt green | 2005: the Chaos era, light and dark meeting in an eclipse over a classic card table. |
| `era-teledad-cup` | `void`, acid lime cracks in olive-black | 2009: Dark Armed Dragon's darkness split by psychic-teleport lightning. |
| `era-synchro-peak-cup` | `halos`, pale mint-white rings on slate | 2012: Synchro peak, stars streaming through a row of tuning rings. |
| `era-rabbits-rulers-cup` | `scales`, pale bronze on umber-taupe | 2013: Dragon Ruler hide and Dino-Rabbit fossil earth. |
| `era-hat-shaddoll-cup` | `gears`, bone-white on mulberry dusk | 2014: Qliphort and HAT machinery under Shaddoll's twilight. |
| `era-nekroz-pepe-cup` | `runes`, ice white on indigo | 2016: a frozen ritual circle for Nekroz (and the Pendulum stage beyond it). |
| `era-abc-to-spyral-cup` | `circuit`, jade signal traces on gunmetal | 2017: spy gadgets and union mechs, all wiring and signal. |
| `era-link-dawn-cup` | `arrows`, signal orange on petrol blue | 2019: the Link dawn, a square marker with eight arrows and chevron trails out to the edges. |
| `era-toss-eldlich-cup` | `sparkles`, cursed-gold glints on tomb black | 2021: Eldlich's golden hoard glinting in the dark. |
| `era-tearlaments-cup` | `mist`, aqua tears through violet fog | 2022: Tearlaments' sorrowful sea mist. |
| `era-kashtira-snake-eye-cup` | `flames`, orange fire in a violet night | 2024: Snake-Eye's fire rising through Kashtira's banished dark. |
| `era-ryzeal-cup` | `rays`, warm white on dusky rose | 2025: the newest format rising like a dawn. |
