# Card pictures

Duel Circuit ships with **no card pictures**. This page shows you how to add pictures you already have.

- [Why there are no pictures](#why-there-are-no-pictures)
- [Import from the game](#import-from-the-game)
- [Add pictures by hand](#add-pictures-by-hand)
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

## Import from the game

This is the easy way. You pick the pictures you have, and the game puts them in the right folders for you.

You need one of these:
- A `.zip` file of pictures. Folders inside the zip are fine.
- One or more picture files (`.jpg`, `.jpeg` or `.png`).

Each picture must be named after its card number (the 8-digit passcode on the card, at the bottom left). For example,
`89631139.jpg` is Blue-Eyes White Dragon.

1. In the main menu, choose **Import Cards**. It sits between **Settings** and **About**.
2. Find the section you want. Each section shows how many pictures it holds and the folder they live in.

   | Section | What goes in it | Where it goes |
   |---|---|---|
   | **Card Images** | The normal pictures for your cards | `mods/cards/images` |
   | **High Resolution Card Images** | Bigger pictures, used when a card is shown large. Optional. | `mods/cards/hd` |
   | **Card Back** | The picture on the back of every card | `mods/cards/back.png` or `back.jpg` |

3. Click **Import...** in that section. A file window opens in your Downloads folder.
4. Pick a `.zip`, or one or more pictures, and confirm. (To pick several files, hold **Ctrl** on Windows or **Cmd** on
   a Mac while you click.) For **Card Back**, pick one picture.
5. The game reads your files, then copies the pictures. A bar shows how far it has got. Click **Cancel** to stop. Nothing
   half-copied is left behind.
6. When it's done, a line under the sections tells you what happened, for example "Card Images: 13,585 added, 3 skipped."

Your pictures show at once. You don't need to restart the game. The game copies your files and never changes the
originals.

**If a picture is already there.** For **Card Images** and **High Resolution Card Images**, the game asks once, for
example "1,204 of these pictures already exist." It shows how many are new and how many are already there. Then you
choose:
- **Replace them:** the new pictures take the place of the old ones.
- **Keep mine:** only cards without a picture get one. The pictures you have stay as they are. This is the button
  that is ready when the question opens, so pressing **Enter** never replaces anything.
- **Cancel:** nothing changes.

**The card back** always asks first. It shows the card back you have now next to the new one. Click **Use this back**
to change it, or **Cancel**.

**Good to know:**
- Only files named by card number are taken. The game skips everything else and counts them ("3 skipped"). If it asks
  you the question above, it also says why (for example "notes.txt: not a card number").
- The game checks that each file really is a picture. A file that only looks like one by its name is skipped.
- If the same card shows up twice in one import, the first one is used.
- A picture bigger than 64 MB is skipped.
- A card has one picture per folder. If you replace `89631139.png` with a `.jpg`, the old `.png` is deleted.
- If the game can't read your zip, a box says **"That didn't work"** and tells you why. Click **OK**.
- **Import Cards** is shut while you're in a campaign duel. Finish or concede the duel first.
- **Open folder** opens the section's folder. It's the way to remove a picture: delete the file by hand.

## Add pictures by hand

You can also put the pictures in the folders yourself. The game reads pictures from a folder inside your `mods` folder
(where your save lives).

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
- **In the game:** choose **Import Cards**, then click **Open folder**.
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
sharper.

**The easy way:** use **Import Cards → High Resolution Card Images** (see [Import from the game](#import-from-the-game)).

**By hand:** the game has its own folder for bigger pictures. It is called `hd` and sits next to `images`, inside
`mods/cards`. The game makes it for you and reads it by itself. Put bigger pictures in it, named by card number like
EDOPro's, and start the game. You don't need to add anything to `settings.txt`.

**A folder somewhere else:** if you already have a folder of bigger pictures, named by card number, add it to
`settings.txt`:

```
hd_pics: D:\Pictures\YugiohHD
```

- Add one `hd_pics:` line for each folder. You can have several. They work together with the `hd` folder.
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

**To use your own:** choose **Import Cards**, click **Import...** in the **Card Back** section and pick one picture
(`.jpg`, `.jpeg` or `.png`). The game shows the old back next to the new one. Click **Use this back**. The new back
shows at once. The game keeps one back: importing a `.png` deletes an old `back.jpg`, and the other way round.

**By hand:** name your picture `back.png` or `back.jpg` and put it in the `cards` folder inside `mods` (next to
`images`).

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
