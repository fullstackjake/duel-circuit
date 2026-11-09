# Card pictures

Duel Circuit ships with **no card pictures**. This page shows you how to add pictures you already have.

- [Why there are no pictures](#why-there-are-no-pictures)
- [Add your own pictures](#add-your-own-pictures)
- [Use EDOPro's pictures](#use-edopros-pictures)
- [Bigger (HD) pictures](#bigger-hd-pictures)
- [Art styles](#art-styles)
- [The card back](#the-card-back)
- [The picture cache](#the-picture-cache)
- [Please keep pictures private](#please-keep-pictures-private)

## Why there are no pictures

Card pictures are © Konami. We can't include them, so the game doesn't.

Without pictures, every card shows its name, stats and text in a clean card frame. You can play the whole game this
way. Pictures only change how cards look.

## Add your own pictures

The game reads pictures from a folder inside your `mods` folder (where your save lives).

| | Your pictures folder |
|---|---|
| **Windows** | `%USERPROFILE%\AppData\LocalLow\YgoCampaign\Duel Circuit\mods\cards\images` |
| **Mac** | `~/Library/Application Support/YgoCampaign/YgoCampaign/mods/cards/images` |

The game makes this folder the first time it starts.

1. Close the game.
2. Name each picture after its card number (the 8-digit passcode on the card, at the bottom left). For example, `89631139.jpg` is Blue-Eyes White Dragon.
3. Put the pictures in your pictures folder. `.jpg` and `.png` both work.
4. Start the game.
5. Open **Settings** and find **Card pictures**. It shows how many pictures the game found, and which folders it read.

**Good to know:**
- Any picture folder that works with EDOPro works here too, because EDOPro names pictures the same way.
- Alternate artworks use the base card's picture when they don't have their own.
- A card without a picture keeps its text look.
- Face-down cards never show their picture, so nothing gives away a hidden card.

**Opening the folder:**
- **Windows:** press **Windows key + R**, paste the folder path from the table above, and press **Enter**.
- **Mac:** in Finder, choose **Go → Go to Folder** (Shift+Cmd+G), paste the folder path, and press **Return**.

## Use EDOPro's pictures

If you have EDOPro (Project Ignis) installed, the game can use the pictures EDOPro has already downloaded.
You don't need to copy anything.

### Mac: usually automatic

The game looks for EDOPro in these places and uses the first one it finds:

```
~/ProjectIgnis
~/Applications/ProjectIgnis
~/Downloads/ProjectIgnis
~/Games/ProjectIgnis
/Applications/ProjectIgnis
```

If your EDOPro is somewhere else, tell the game where it is (the next section works on a Mac too).

### Windows: tell the game where EDOPro is

On Windows, the game finds EDOPro by itself in a `ProjectIgnis` folder in your user folder (for example
`C:\Users\you\ProjectIgnis`), or in its Downloads, Games or Applications folder. If yours is anywhere else, add one
line to a settings file:

1. Close the game.
2. Find EDOPro's `pics` folder. It's inside your EDOPro (ProjectIgnis) folder.
3. Click the address bar in File Explorer and copy the folder's path, for example `D:\Games\ProjectIgnis\pics`.
4. Open your `mods` folder: press **Windows key + R**, paste `%USERPROFILE%\AppData\LocalLow\YgoCampaign\Duel Circuit\mods`,
   and press **Enter**.
5. Open `settings.txt` in Notepad. If there's no such file, make one: right-click → **New → Text Document**, and name it `settings.txt`.
6. Add this line, with your own path after the colon:

   ```
   edopro_pics: D:\Games\ProjectIgnis\pics
   ```

7. Save the file and start the game.
8. Check **Settings → Card pictures**: EDOPro's folder should be in the list.

**Tips:**
- Don't put quotes around the path.
- If Windows hides file endings, make sure the file is called `settings.txt`, not `settings.txt.txt`.
- On a Mac the line looks like this: `edopro_pics: /path/to/ProjectIgnis/pics`. You can start it with `~` for your home folder.

## Bigger (HD) pictures

EDOPro's pictures are small (177 × 254 pixels), and the game shows cards larger than that. Bigger pictures look much
sharper. If you have a folder of bigger pictures, named by card number like EDOPro's, add it to `settings.txt`:

```
hd_pics: D:\Pictures\YugiohHD
```

- Add one `hd_pics:` line for each folder. You can have several.
- On a Mac, use a Mac path, like `hd_pics: ~/Pictures/YugiohHD`.
- When a card has a picture in more than one folder, **the biggest picture wins**.
- So if you want your own picture of a card to be used instead of an HD one, make yours at least as big.
- The game never downloads pictures. You add them yourself.

## Art styles

Some cards have other looks with the same card number, like extended art or manga-style art. You can keep these as
**styles**:

1. In your `mods` folder, open `cards`, and make a folder called `styles` if it isn't there.
2. Inside `styles`, make one folder per style, for example `extended` and `manga`. The folder name is the style's name.
3. Put the pictures in, named by card number as usual: `mods/cards/styles/extended/89631139.jpg`.
4. Start the game.

How to use them:
- A card with pictures in more than one style gets an **Art** button on its picture (for example "Art: Extended ▸"),
  in the deck editor and in the big card view. Click it for the next style; right-click for the previous one.
- **Settings → Prefer style** picks a style for every card that has one, unless you chose differently for that card.
- Your choices are remembered and show everywhere: in duels, in your hand, in the deck editor and in packs.
- A picture that isn't card-shaped gets borders instead of being stretched.

## The card back

Face-down cards, decks and unopened packs use a card back. The game picks one in this order:

1. Your own: `mods/cards/back.png` (or `back.jpg`).
2. EDOPro's card back, if the game found EDOPro.
3. The game's own back: dark blue (navy), with a gold border and a gold star.

## The picture cache

To load big pictures quickly, the game keeps ready-made copies in a cache folder. Deleting it is always safe: the
game simply makes the copies again.

| | Cache folder |
|---|---|
| **Windows** | `%USERPROFILE%\AppData\LocalLow\YgoCampaign\Duel Circuit\card-art-cache` |
| **Mac** | `~/Library/Caches/com.ygocampaign.game/card-art` |

The game keeps the cache to about 1.3 GB at most and removes the oldest copies first.
The first time you open a big picture it can take a moment. After that it's fast.

## Please keep pictures private

- Card pictures are © Konami. Use them only on your own computer, for yourself.
- Never share or upload them, and never add them to this project.
- Please don't attach card pictures to bug reports. A replay code shows us everything we need.
