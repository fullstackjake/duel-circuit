# Modding Duel Circuit

You can change a lot of Duel Circuit without touching its code: add decks, ban lists, portraits, duel mats and map
pictures through your `mods` folder, and write new characters and story scenes as plain text. This guide says where each
thing goes and what the files look like.

- [Rules for mods you share](#rules-for-mods-you-share)
- [The mods folder](#the-mods-folder)
- [Decks](#decks)
- [Extra ban lists](#extra-ban-lists)
- [Portraits and expressions](#portraits-and-expressions)
- [Duel mats](#duel-mats)
- [Map pictures](#map-pictures)
- [Card pictures and art styles](#card-pictures-and-art-styles)
- [settings.txt](#settingstxt)
- [Characters](#characters)
- [Story scenes and other game data](#story-scenes-and-other-game-data)

## Rules for mods you share

What you do on your own computer is up to you. Anything you share with others, or send to the project, follows these
rules:

- **No card art.** Card pictures belong to Konami. Never share them, put them in a mod, or attach them to an issue.
- **Original characters only.** No characters from the anime, manga or other games.
- **No real people.** No real people's names or likenesses, including tournament players. Credit for real decklists
  goes in a sources file (as [decks/history/SOURCES.md](../decks/history/SOURCES.md) does), never in the game.

[CONTRIBUTING.md](../CONTRIBUTING.md) has the full rules for contributions.

## The mods folder

The game creates your `mods` folder the first time it starts. Your save, your decks and your own files all live there.

| Where the game runs | Your `mods` folder |
|---|---|
| Mac app | `~/Library/Application Support/YgoCampaign/YgoCampaign/mods` |
| Windows game | `%USERPROFILE%\AppData\LocalLow\YgoCampaign\Duel Circuit\mods` |
| Unity editor | `mods/` at the top of the project |

To open it on Windows:

1. Press **Windows key + R**.
2. Paste the Windows path from the table and press **Enter**.

To open it on a Mac:

1. In Finder, choose **Go → Go to Folder** (Shift+Cmd+G).
2. Paste the Mac path from the table and press **Return**.

What goes where inside it:

| Path in mods | What it holds | Made by |
|---|---|---|
| `save/profile.txt` | Your campaign save (plain text, with a `.bak` of the last one) | The game |
| `decks/` | Your decks (`.ydk`) | The game and you |
| `replays/` | Saved replays; `replays/recent` keeps the last 30 duels | The game |
| `settings.txt` | Card picture folders | You |
| `cards/images/` | Your card pictures | You, or **Import Cards** in the main menu |
| `cards/hd/` | Bigger (HD) card pictures | You, or **Import Cards** |
| `cards/styles/<style>/` | Other art styles of cards | You |
| `cards/back.png` (or `back.jpg`) | A card back | You, or **Import Cards** |
| `cards/art-styles.txt` | Which style each card shows | The game |
| `lflists/` | Extra ban lists (`.conf`) | You |
| `portraits/` | Portraits and expressions that replace or add to the game's | You |
| `mats/` | Duel mat pictures | You |
| `map/` | Circuit map region pictures | You |

Make any folder that isn't there yet. Close the game before you change files, then start it again to see the change.

For decks, portraits, mats and map pictures, your file wins over the game's file of the same name.

To try a mod without touching your save, start the game with a different `mods` folder. It starts with a fresh save in
that folder.

On Windows, open the `Duel Circuit` folder in File Explorer, type `cmd` in its address bar and press **Enter**. A
Command Prompt opens in that folder. Paste this and press **Enter**:

```
"Duel Circuit.exe" -mods "%USERPROFILE%\Desktop\test-mods"
```

On a Mac, paste this into Terminal and press **Return**:

```bash
"/Applications/Duel Circuit.app/Contents/MacOS/Duel Circuit" -mods "$HOME/Desktop/test-mods"
```

Always give `-mods` a full path, like the ones above. The Mac app ignores a relative one.

## Decks

Decks are `.ydk` files, the same format EDOPro and most deck builders use, so decks from those work here too.

- **Your decks** go in `mods/decks/`. They show up in Free Duel and the deck editor. Only files directly in that folder
  are read, not subfolders.
- **Replacing a built-in deck:** a deck in `mods/decks/` with the same file name as a built-in one (for example
  `warriors.ydk`) replaces it in the game. Saving a built-in deck in the editor does exactly this.
- **Importing:** the deck editor also takes a YDKE link or `.ydk` text pasted from the clipboard (**Import**).

A `.ydk` file is a list of card ids (the card's 8-digit passcode, the number EDOPro uses), one per line, under `#main`, `#extra` and `!side`:

```
#created by Duel Circuit
#about Lord of D. and the Flute call Blue-Eyes from your hand.
#main
89631139
89631139
89631139
#extra
!side
```

Lines starting with `#` are notes. EDOPro ignores them, and Duel Circuit reads a few:

| Line | What it does |
|---|---|
| `#about <text>` | How the deck works, shown in the deck editor |
| `#era <year>`, `#tier <text>`, `#why <text>` | A historic deck's story, shown in the deck editor |
| `#banlist <list name>` | The ban list the editor picks when it opens the deck, e.g. `#banlist TCG April 2005` |
| `#starters <id> <id>...` | Cards you marked as starters for the opening-hand odds |
| `#campaign` | A campaign deck (built from your collection); the game writes it |

**Built-in decks** live in the project's `decks/` folder: the starter decks and other built-in decks directly in `decks/`, the historic
tournament decks in `decks/history/`, the shop's structure decks in `decks/structure/`, and the Back Alley's in
`decks/alley/` and `decks/headliners/`.

**Recipes.** If you work from your copy of the project from GitHub, you can write decks by card name instead of id
(needs Python 3 and [BUILDING.md](BUILDING.md) steps 1 and 2). A recipe file (see `decks/recipes/starters.txt`) has `deck: <path>`, any `#note` lines, then `main:`, `extra:`
and `side:` sections of `<copies> <card name>` lines. This turns it into `.ydk` files and checks every name:

```bash
python3 tools/build_deck.py decks/recipes/starters.txt
```

It reports unknown names and Main Decks outside 40 to 60 cards, and writes nothing if anything is wrong.

## Extra ban lists

Put `.conf` files in `mods/lflists/` (make the folder). They use EDOPro's ban list format, so EDOPro's own lists work.
Your lists appear in the deck editor's ban list choice next to the game's.

```
#[My lists]
!My Format 2026
#Forbidden
55144522 0 --Pot of Greed
#Limited
70368879 1 --Upstart Goblin
#Semi-Limited
12580477 2 --Raigeki
```

- `!<name>` starts a list. One file can hold several lists.
- Each card line is `<card id> <copies allowed>`: 0 Forbidden, 1 Limited, 2 Semi-Limited. Anything after the number
  is a comment.
- A line `$whitelist` makes it a whitelist: only the listed cards are allowed.
- Lines starting with `#` are comments.

## Portraits and expressions

Each duelist has a portrait: a PNG with a transparent background, named after the character's id (the file name of
their profile, see [Characters](#characters)). The game's are in the project's `art/portraits/`. They're trimmed to the
figure, so their sizes vary: most duelists' portraits are about 300 x 350 pixels (from 290 to 540 wide and 310 to 620
tall), and the Back Alley regulars' faces are smaller, about 180 x 140. Use a similar size so yours sits the same way.

To replace one, or give a portrait to a character who has none, put `<character id>.png` in `mods/portraits/`. For
example, `mods/portraits/vera.png` replaces Vera's.

Portraits show in story scenes, on versus screens, on the duel board's player panels, and in the campaign's lists.

**Expressions.** During a duel a duelist's face changes for a moment when something big happens. Put these next to
the portrait (same size, same framing):

| File | When it shows |
|---|---|
| `<id>_smug.png` | They bring out a big monster or hit you hard |
| `<id>_hurt.png` | They take a big hit |
| `<id>_desperate.png` | Their LP falls to 1000 or less |

A character without one keeps their normal portrait. [docs/dev/EXPRESSIONS.md](dev/EXPRESSIONS.md) says what to draw.

## Duel mats

The duel mat is the picture behind the field. Each side has its own. Put a PNG or JPG in `mods/mats/`, named after
who it's for:

| File | Whose side |
|---|---|
| `<character id>.png` | That character's side, whatever deck they play |
| `<deck name>.png` | Anyone playing that deck (the `.ydk` file name) |
| `you.png` | Your side |

A picture replaces the mat the game paints in code. [docs/dev/MATS.md](dev/MATS.md) has the size (2048 x 768) and the
look of each boss's mat. The painted signature mats (colors and a motif per boss) are listed in `campaign/mats.txt`.

## Map pictures

The campaign's circuit map has one region per difficulty band. Each is painted in the game, and the project ships a
picture for each in `art/map/`. To replace one, put `<band>.png` or `<band>.jpg` in `mods/map/`. The band names are:

```
beginner  novice  intermediate  advanced  expert  master  gauntlet
```

[docs/dev/MAP.md](dev/MAP.md) describes each region.

## Card pictures and art styles

The game ships no card pictures. Adding your own, using an EDOPro install's pictures, HD picture folders, art styles
(`mods/cards/styles/`) and a card back are all in [CARD-PICTURES.md](CARD-PICTURES.md).

Card pictures are for your own computer only: never put them in a mod you share.

## settings.txt

`mods/settings.txt` holds the card picture folders. Every other setting is changed in the game's **Settings** screen
and saved by the game itself.

```
# Lines starting with # are comments. "~" means your home folder.
edopro_pics: ~/ProjectIgnis/pics
hd_pics: ~/Pictures/card-pictures-hd
hd_pics: /Volumes/External/more-pictures
```

| Line | What it does |
|---|---|
| `edopro_pics: <folder>` | An EDOPro install's `pics` folder. Without it, the game looks for EDOPro in your home folder (`~/ProjectIgnis`, `~/Applications/ProjectIgnis`, `~/Downloads/ProjectIgnis`, `~/Games/ProjectIgnis`) and in `/Applications/ProjectIgnis` |
| `hd_pics: <folder>` | A folder of bigger card pictures. Add one line per folder |

[CARD-PICTURES.md](CARD-PICTURES.md) explains how the game picks between folders.

## Characters

Characters are part of the game's data, not your `mods` folder. There are two ways to change them:

- **No building needed:** edit the copy inside the game you downloaded. The characters are in this folder:

  | | Characters folder |
  |---|---|
  | Windows | `Duel Circuit\Duel Circuit_Data\StreamingAssets\personalities\` |
  | Mac | `Duel Circuit.app/Contents/Resources/Data/StreamingAssets/personalities/` |

  The next version of the game replaces these files, so keep a copy of your changes. On a Mac, right-click the app and
  choose **Show Package Contents** to get inside it. Changing files inside the app may make macOS say the app is
  damaged; [BUILDING.md's Troubleshooting](BUILDING.md#troubleshooting) has a Terminal line that fixes that.
- **From the project's source:** change them in your copy of the project from GitHub and play them in the Unity editor
  or a build of your own ([BUILDING.md](BUILDING.md)).

Each character is one `.profile` file in `personalities/`. The file name is the character's id (`vera.profile` is
`vera`). Here is Vera's, shortened:

```
# Relentless beatdown. Attacks into set cards and trades happily.
name: Vera
title: the Vanguard
aggression: 0.95
caution: 0.05
patience: 0.3
spending: 0.75
skill: 0.85
decks: warriors, classic
intro: I run traps. I just never wait for them. Hit me if you can.
rematch: Round two. Same plan: hit you until you fall.
win: Pressure wins duels. Remember that.
lose: You held the line. Next time I hit harder.
partner: Partners! Good. I hit first. You hit second. Nobody waits.
look: Athletic young warrior-type in a red jacket with rolled sleeves.
ladder: 5
signature: Gearfried the Swordmaster
summon: Here comes the hammer!
strike: Feel that? That's pressure.
hit: Ha! Is that all you've got?
danger: I'm not done. Not even close.
```

Each line is `key: value`. A `#` starts a comment, so don't use `#` in a line of dialogue. Keys the game doesn't know
are skipped.

| Key | What it is |
|---|---|
| `name`, `title` | Shown as "Vera, the Vanguard" |
| `decks` | Their own decks, by `.ydk` file name, separated by commas (Free Duel offers these first). A character with none of their decks in the game doesn't appear |
| `intro` | What they say before a duel |
| `rematch` | What they say the second time you meet them in the same circuit (optional) |
| `win`, `lose` | What they say after they win or lose |
| `partner` | What they say the first time they're your tag partner (optional) |
| `summon`, `strike`, `hit`, `danger` | Duel lines: a big summon, hitting hard, taking a hit, falling to 1000 LP. Repeat a key for more lines; one is picked each time |
| `look` | What they look like (for the character list and portrait artists) |
| `ladder` | Their place in the campaign order. Leave it out (or 0) for a character outside the campaign: they're open in Free Duel from the start |
| `signature` | Their signature card's name: your gift the first time you beat them |
| `aggression`, `caution`, `patience`, `spending`, `skill` | How they play: numbers from 0 to 1 (below) |

How the five numbers change their play:

| Number | Low | High |
|---|---|---|
| `aggression` | Only attacks clear wins, summons defensively | Swings into set monsters, takes even trades |
| `caution` | Ignores your set Spell/Traps | Plays around them, sets monsters |
| `patience` | Uses disruption on your first play | Saves it for your strongest play |
| `spending` | Hoards removal and Extra Deck plays | Spends them freely |
| `skill` | Often makes random choices | Always plays its best line |

Personality changes *how* a character plays; `skill` changes *how well*. At `skill` 0.5 or more they use their deck's
playbook (its combo knowledge, where it has one); at 0.9 or more they also plan their turns ahead.

A new character with a deck and no `ladder` line shows up in Free Duel. Putting them into the campaign means editing
`campaign/circuits.txt` too. The top of that file explains its format.

To see how a character plays against the standard CPU duelist, run the harness from your copy of the project from
GitHub (needs the harness: [BUILDING.md](BUILDING.md) steps 1–4, Mac only):

```bash
dotnet build/bin/Ygo.Harness/Release/net7.0/Ygo.Harness.dll --styles --games 300
```

It plays every character against the balanced CPU and prints, for each, win rate, attacks per turn, monster sets,
disruption used, Extra Deck summons and game length.

## Story scenes and other game data

These are game data too, so you edit them the same two ways as characters. In the downloaded game, the `campaign/`
files are in the `campaign` folder next to `personalities` (the same updates replace them, so keep a copy). The
`art/` pictures are easiest to change in your copy of the project from GitHub.

| File | What it holds |
|---|---|
| `campaign/story.txt` | The story scenes: when each plays, where, and who says what. The top of the file explains the format |
| `campaign/circuits.txt` | The circuits, their matches and the Tag Cups. The top of the file explains the format |
| `campaign/guests.txt` | The special guests |
| `campaign/mats.txt` | The painted signature mats |
| `campaign/shop.txt` | What the shop's people say |
| `art/backdrops/<place>.png` | A picture for a story scene's place, instead of the one painted in code (none ship yet) |
| `art/coin/you.png`, `them.png` | The coin toss's two faces |

A story line is `<speaker>: <text>`, where the speaker is a character id, `partner`, or `-` for narration:

```
scene: my-first-scene
when: level:5
old-gus: Level five already? The regulars are talking about you.
vera: Talking. Not worried.
```

After editing the story, check that every scene's trigger and speaker exist (needs the harness: [BUILDING.md](BUILDING.md)
steps 1–4, Mac only):

```bash
dotnet build/bin/Ygo.Harness/Release/net7.0/Ygo.Harness.dll --campaign-test
```

It prints its checks and ends with `All campaign checks passed.` (or how many failed, with `FAIL` beside each).
