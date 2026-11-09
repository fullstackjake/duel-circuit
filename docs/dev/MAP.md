# Circuit map art

The campaign map is a climb through seven regions, one per difficulty band, from the local card shop at the bottom to
the Gauntlet at the top. The circuits sit on a winding gold path over each region, each one a round node with the
boss's face. Today each region is painted in code; a picture replaces it.

## The story the map tells

You start in a small-town card shop and play your way up through the dueling world: across town, out to the region,
into the big tournament halls, up to the pro league, and finally to a tower at the top of the world where the four
most dominant decks in history wait. Each region is a step up in stakes and in spectacle, and each is tied to its
own era of the game (the Novice region holds 2010s decks, Master holds today's).

## Size and rules

- **Size:** 1600 x 1200 (4:3, landscape). The game crops the top and bottom to fit each region, so keep the middle
  third of the height the most interesting, and don't put anything that matters in the top or bottom 10%.
- **The path goes on top.** Gold lines and round portrait nodes zigzag left and right over it, with labels under each
  node, so keep it **mid-to-dark and not too busy**, with no single bright spot in the middle. Atmosphere over detail.
- **Seen from above at an angle** (a "bird's-eye fantasy map"), so a path climbing the picture feels natural.
- **No text, logos, real card art or real Yu-Gi-Oh! monsters.** No people in focus (the bosses' faces are the
  people on the map).
- Bottom edge = where you enter the region, top edge = where you leave it; bottom of one region meets the top of the
  one below, so a slightly darker band along the top and bottom edges blends them.

Save them as `art/map/<band>.png` (shipped with the game; rebuild the app) or `mods/map/<band>.png` (yours only):
`beginner`, `novice`, `intermediate`, `advanced`, `expert`, `master`, `gauntlet`.

Shared style, to start every prompt with:

> Fantasy game world map region, bird's-eye view at a 45-degree angle, painterly anime-style background art like a
> JRPG overworld, rich atmosphere and soft lighting, mid-to-dark tones with no bright spot in the center, details
> toward the edges, landscape 4:3, no text, no logos, no characters, no UI.

## The seven regions

| File | Region and its circuits | Prompt (after the shared style) |
|---|---|---|
| `beginner` | **Beginner:** the local shop, its weekly tournament, the town tournament and the regional qualifier. Warm and welcoming, where everyone starts. | A cozy small town at golden-hour dusk, seen from above: a corner card shop with warm glowing windows and an awning, cobblestone streets, string lights, a small park and a town hall in the distance at the top. Warm amber and brown tones. |
| `novice` | **Novice:** the first regional events (2010 to 2018 decks: Frog FTK, Six Samurai, Spellbooks, Burning Abyss, Monarchs, Altergeist). Leaving home. | A green river valley at the edge of town: a winding road leaving the houses behind, an old stone bridge, forest on both sides, a convention tent village with banners in the clearing ahead. Fresh green and teal tones, early evening. |
| `intermediate` | **Intermediate:** the Synchro, Xyz and Link eras at a coastal city's regional hall. Bigger stages. | A coastal city at blue hour seen from above: harbor with boats, a lighthouse, a big glass-domed exhibition hall on the waterfront with spotlights, waves catching moonlight. Deep blue and cyan tones. |
| `advanced` | **Advanced:** championship halls of the golden age and control kings (Dragon Rulers, True Draco, Unchained). Heat and pressure. | A volcanic highland with a grand stone colosseum built into the mountainside, lava rivers glowing far below, ember-filled air, banners on the colosseum walls. Fiery red and orange tones against dark rock. |
| `expert` | **Expert series:** shadows and storms, rituals and fire, the new wave. Mysterious and arcane. | A floating island archipelago in a violet storm at night: ancient ritual stone circles on the islands, glowing runes, lightning in purple clouds, mist pouring off the island edges. Violet and indigo tones. |
| `master` | **Master league:** the modern pro circuit (Link wars, Graveyard kings, YCS Houston 2026). Prestige and spectacle. | A gleaming futuristic arena city above the clouds at sunset: a huge golden stadium with light beams, sky bridges and holographic banners (no words), floating platforms. Gold and white tones with deep shadows. |
| `gauntlet` | **The Tier 0 Gauntlet:** the four most dominant decks in history. The final climb. | The top of the world at night: a lone black obsidian tower on a jagged peak above a sea of clouds, crimson light pouring from its top, a red moon, shattered stone steps spiraling up to it. Black and crimson tones, ominous and epic. |

## Tips

- Generate a few of each and pick the one with the calmest middle; the game dims nothing, so a very bright picture
  makes the labels hard to read.
- If one region comes out too busy, a slight darkening or blur before saving helps a lot.
- They don't have to match perfectly, but the same shared style keeps them feeling like one world.
