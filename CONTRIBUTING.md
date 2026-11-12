# Contributing to Duel Circuit

Everyone is welcome here, beginners too. You don't need to know how to code to help.

Most things here start with an **issue**: a post on the project's GitHub page, for a bug report, an idea or a
question. You need a free GitHub account to write one.

Please be kind to each other. Everyone who takes part follows our [Code of Conduct](CODE_OF_CONDUCT.md).

## Ways to help

| You like to... | You can... |
|---|---|
| Play | Play the game and [report bugs](#report-a-bug). A good bug report helps a lot. |
| Think about games | Suggest ideas: new duelists, events, features. [Open a feature request](https://github.com/fullstackjake/duel-circuit/issues/new/choose). |
| Draw or paint | Make portraits, duel mats or map art. Read the [content rules](#content-rules) first. |
| Write | Write story scenes, dialogue, duelist lines, or improve the guides. |
| Build decks | Suggest decks for duelists, or fix a deck that plays badly. |
| Code | Fix bugs, improve the CPU duelists, add features. Start with [docs/DEVELOPING.md](docs/DEVELOPING.md). |

Not sure where to start? [Open a question](https://github.com/fullstackjake/duel-circuit/issues/new/choose) and say
what you'd like to do. We'll help you find something.

## Report a bug

1. Check the [open issues](https://github.com/fullstackjake/duel-circuit/issues) to see if someone already reported it.
   If so, add what you saw to that issue.
2. If not, [open a bug report](https://github.com/fullstackjake/duel-circuit/issues/new/choose). The form asks for
   everything below.

### What to put in a bug report

- **Your computer:** Windows or Mac.
- **The game's version:** it's at the bottom left of the title screen (for example `v0.2.0`).
- **What happened**, and **what you expected** to happen.
- **The steps** to make it happen again, if you know them.
- **The replay code**, for a bug in a duel (see below).
- **Your `Player.log` file** (see below).

Please don't attach card pictures or screenshots that show a whole card's picture. A screenshot of a menu, or of the
board with small cards, is fine.

### The replay code

A replay code plays a duel back move for move, exactly as it happened. With it, we can see exactly what you saw.

- **During the duel:** click the gear icon (top right), then **Save replay**. The code is copied for you.
- **After the duel:** finished duels are saved for you. In the main menu, open **Replays**, find the duel and click
  **Copy code**.

Then paste the code into your bug report.

### The Player.log file

The game writes a log file while it runs. It helps us see what went wrong.

The game starts a new log each time you open it, so copy the log **before** you open the game again.

| | Log file |
|---|---|
| **Windows** | `%USERPROFILE%\AppData\LocalLow\YgoCampaign\Duel Circuit\Player.log` |
| **Mac** | `~/Library/Logs/YgoCampaign/Duel Circuit/Player.log` |

**On a Mac:**

1. In Finder, choose **Go → Go to Folder** (Shift+Cmd+G).
2. Paste this and press Return:

   ```
   ~/Library/Logs/YgoCampaign/Duel Circuit/
   ```

3. Drag `Player.log` into your bug report.

**On Windows:**

1. Press **Windows key + R**.
2. Paste this and press Enter:

   ```
   %USERPROFILE%\AppData\LocalLow\YgoCampaign\Duel Circuit
   ```

3. Drag `Player.log` into your bug report. Windows may show it as just **Player**.

The log can show your computer's user name in folder paths. You can replace it with `xxx` before you attach it.

## Send a change

Changes go in through a pull request ("PR"): you make the change in your own copy of the project, then ask us to add
it.

### What people usually change

- **The story:** `campaign/story.txt` (the top of the file explains the format).
- **Duelists' lines and play styles:** the `.profile` files in `personalities/`.
- **Decks, portraits, mats and more:** [docs/MODDING.md](docs/MODDING.md) says where each thing lives.
- **The guides:** the `.md` files, like this one.

Building the game needs a Mac with Apple silicon for now. On Windows you can still change decks, characters, story and
guides, and test them in the game you downloaded ([docs/MODDING.md](docs/MODDING.md)).

### Change a file on the GitHub website

You don't need to install anything for a small change, like a story line or a typo.

1. Make a free account at [github.com](https://github.com).
2. On the [Duel Circuit page](https://github.com/fullstackjake/duel-circuit), open the file you want to change.
3. Click the pencil icon (**Edit**). GitHub makes your own copy of the project (a "fork") for you.
4. Make your change.
5. Click **Commit changes...**.
6. In **Extended description**, add this line, with your name and email (why? see [Sign your work](#sign-your-work)):

   ```
   Signed-off-by: Your Name <your-email@example.com>
   ```

7. Click **Propose changes**.
8. Click **Create pull request**.
9. Fill in the short checklist, then click **Create pull request** again. We'll reply in the pull request.

### Already use git?

1. Fork the repository and clone your fork.
2. Make a branch: `git switch -c my-change`
3. Make your change. Keep one topic per pull request.
4. Commit with a sign-off: `git commit -s -m "Short description"`
5. Push: `git push -u origin my-change`
6. Open a pull request against `main` and fill in the checklist.

To build the game, see [docs/BUILDING.md](docs/BUILDING.md). For how the code is laid out and how to test it, see
[docs/DEVELOPING.md](docs/DEVELOPING.md).

## Your contribution's license

When you send a change, you agree it is shared under the same licenses as the rest of the project
([LICENSING.md](LICENSING.md)):

- **Code:** GNU AGPL, version 3 or later, with the [Unity permission](LICENSES/UNITY-PERMISSION.md).
- **Art, story, characters, writing, guides:** CC BY-SA 4.0.

You keep the copyright to your work. We don't ask you to sign anything else.

Only send work you made yourself, or work whose license allows it to be shared this way (then say where it's from
and under which license). Don't send anything copied from Konami or from other games.

### Sign your work

Every commit needs a `Signed-off-by:` line. It says you agree to the
[Developer Certificate of Origin](https://developercertificate.org): in short, that you made the change (or have the
right to send it) and that it can be shared under the project's licenses.

- With git: `git commit -s` adds the line for you.
- On the GitHub website: add the line yourself in **Extended description** (see step 6 above).

Use the name you go by and an email address. GitHub's private "noreply" address is fine.

Forgot it? Run `git commit --amend -s` for your last commit, then `git push --force-with-lease`.

## Content rules

Duel Circuit is a fan game, so we're careful with other people's work.

- **No card pictures.** Don't add card art or screenshots that show a whole card's picture, anywhere: not in the
  code, not in the docs, not in issues.
- **Nothing of Konami's.** No logos, art, music, sound, fonts or text taken from the anime, the manga, the card game
  or the official video games.
- **Original characters only.** No characters from the anime, manga or other games, and no lookalikes.
- **No real people.** Don't use real people's names or faces in the game, including tournament players. (Deck
  builders are credited in [decks/history/SOURCES.md](decks/history/SOURCES.md), never in the game.)
- **AI tools:** if you use AI tools to make art or writing, say so in your pull request. Don't make anything that
  imitates a specific artist: don't prompt with an artist's name, and don't use a model trained to copy one artist.
- **CPUs play at full strength.** To make a duel easier or harder, change which deck a duelist plays. Don't make a CPU
  play worse on purpose.
- **Keep it friendly for all ages.** The game is played by young fans too.

## Questions

[Ask a question](https://github.com/fullstackjake/duel-circuit/issues/new/choose) (choose **Question or help**). There
are no silly questions.
