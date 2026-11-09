# Permission request to edo9300 and Project Ignis (ready to post)

**Status: edo9300 answered in edo9300/ygopro-core#179 (2026-11-08; recorded in THIRD_PARTY_NOTICES.md). Project Ignis ticket opened on Discord, waiting.** Owner decision 3 (docs/dev/README-PLAN-2026-11-08.md). Two pieces:

1. **A GitHub issue on edo9300/ygopro-core**, the main question (he holds the rules engine).
   Post it at https://github.com/edo9300/ygopro-core/issues/new: paste the title and the body below.
2. **A short message to Project Ignis** for the card scripts, card database and ban lists: a staff ticket in
   **#modmail-reports** on their Discord server (invite: https://discord.gg/ygopro-percy, from the server's
   #welcome-and-rules; claim and verify your Discord account first), or a DM to @ProjectIgnisYGO on X. Paste the link
   to the issue into it.

When an answer comes, record it in THIRD_PARTY_NOTICES.md ("Status: the rules engine inside a Unity build"), with a
link to the issue or a copy of the message, and update LICENSING.md and LICENSES/UNITY-PERMISSION.md to match.

---

## 1. GitHub issue (edo9300/ygopro-core)

**Title:** Permission to ship ygopro-core in a free, open-source Unity fan game (Duel Circuit)

**Body:**

Hi edo9300,

I'm Jake Joseph, the developer of **Duel Circuit**, a free, single-player Yu-Gi-Oh! fan game: you climb a campaign of
CPU duelists who play real decks from 2004 to 2026. ygopro-core is its rules engine and Project Ignis's CardScripts run
every card. It wouldn't exist without your work, so first of all: thank you.

Before I make the project public, I'd like to ask your permission for one thing.

**The project**

- Free and non-commercial: no price, no ads, no donations.
- Open source: my own code is AGPL-3.0-or-later, like ygopro-core. Art and story are CC BY-SA 4.0.
- Offline and single-player. No online play.
- It ships **no card pictures**. Players can point it at their own picture folders, for example an EDOPro install.
- The repository will be public at https://github.com/fullstackjake/duel-circuit. It's private until I have your
  answer, but I'm happy to add you to it if you'd like to look first.

**What I'm asking**

The game is made with Unity, and each build ships ygopro-core as a library (`libocgcore.dylib` for macOS,
`ocgcore.dll` for Windows), built from commit `baa3881` with three small patches. The game's C# code calls it through
the C API.

Unity's runtime isn't open source, so a strict reading of the AGPL could say ygopro-core can't be shipped inside a Unity
build without your permission. My own code carries an extra permission for this (AGPL section 7), but I can't give one
for yours. So:

**May I ship ygopro-core, with my patches, inside Duel Circuit's Unity builds?** ygopro-core stays under the AGPL, and
every release includes its full source with the patches applied.

**What I'll do in return**

- Credit you by name in the game's About screen, the README and the third-party notices, with links to the repository.
- Keep the patches public. Two add optional duel flags (`DUEL_SECOND_PLAYER_STARTS`: the second player takes the first
  turn; `DUEL_SHUFFLE_AFTER_DECK_LOOK`: shuffle the Deck after an effect looks through it), and one orders effect sets by
  creation instead of memory address, so replays play back exactly. You're welcome to any of them upstream; I can open
  a pull request if you'd like.
- Attach the patched core's source to every release.
- Change or remove anything you're not comfortable with.

If the answer is no, or you'd like it done differently, that's completely fine: tell me and I'll adjust.

I'm asking Project Ignis separately about CardScripts, BabelCDB and LFLists.

Thank you for your time, and for everything you've built.

Jake Joseph ([@fullstackjake](https://github.com/fullstackjake))

---

## 2. Message to Project Ignis (Discord or X)

Hi Project Ignis team! I'm Jake Joseph, making **Duel Circuit**, a free, offline, single-player Yu-Gi-Oh! fan game
built on ygopro-core and your CardScripts. It's open source (AGPL-3.0), non-commercial, and ships no card pictures.

Before I make it public, I'd like your OK on two things:

1. Shipping **CardScripts** (unchanged, as Lua files) inside the game's Unity builds. Unity's runtime isn't open
   source, so I'd rather ask than assume.
2. Including data from **BabelCDB** (converted into the game's own format) and **LFLists** (copied as they are) in the
   builds. Neither repository states a license.

You'd be credited by name in the game, the README and the notices, and the full source stays available. If you'd
rather I do anything differently, I'll change it. I've asked edo9300 about ygopro-core here: https://github.com/edo9300/ygopro-core/issues/179

Thank you for EDOPro and everything around it!
