# DRAFT: permission request to edo9300 and Project Ignis

**Status: draft, not sent.** For the owner to edit and send (owner decision 3, docs/dev/README-PLAN-2026-11-08.md).
Where to send it: the contact each project prefers (Project Ignis asks for questions on their Discord; edo9300 through
the ygopro-core repository or Discord). When the answer comes, record it in THIRD_PARTY_NOTICES.md ("Status: the
rules engine inside a Unity build") and keep the reply.

Before sending, fill in: the public repository link (if it's public by then) and your sign-off name.

---

**Subject:** Permission to ship ygopro-core in a free Unity fan game (Duel Circuit)

Hi edo9300, hi Project Ignis team,

I'm the developer of **Duel Circuit**, a free, single-player Yu-Gi-Oh! fan game. You climb a campaign of CPU
duelists who play real decks from 2004 to 2026. It runs on your work: ygopro-core is the rules engine, and
CardScripts runs every card. It would not exist without you, so first of all: thank you.

I'd like to ask your permission for one thing before I make the project public.

**The project**

- Free and non-commercial: no price, no ads, no donations. It will never be sold.
- Open source: our own code is AGPL-3.0-or-later, the same license as your work. Art and story are CC BY-SA 4.0.
- Offline and single-player; no online play.
- It ships **no card pictures**. Players can point it at their own picture folders, for example an EDOPro install.
- Source: [repository link]

**What I'm asking**

The game is made with Unity. Each build ships:

- ygopro-core, built as a library (`libocgcore.dylib` for macOS and `ocgcore.dll` for Windows), from commit
  `baa3881` with three small patches;
- CardScripts, unchanged, as Lua source files.

Unity's runtime is not open source, so a strict reading of the AGPL could say the core and scripts can't be shipped
inside a Unity build without your permission. Our own code carries an extra permission for this (AGPL section 7), but
I can't give one for your code. So I'm asking:

1. **May I ship ygopro-core (with our patches) and CardScripts inside Duel Circuit's Unity builds?** Everything stays
   under the AGPL, and the full source of the core, the patches and the scripts stays available.
2. **Do you mind if the builds include data from BabelCDB and LFLists?** The card database is converted into the
   game's own format; the ban lists are copied as they are. Neither repository states a license, so I'd rather ask.

**What I'll do in return**

- Credit you by name in the game, the README and THIRD_PARTY_NOTICES.md, with links to your repositories.
- Publish our patches (they're already in the repository, in `patches/ygopro-core/`). They add two optional duel flags
  (the second player takes the first turn; shuffle a Deck after an effect looks through it) and order effect sets by
  creation instead of memory address, so replays play back exactly. You're welcome to any of them upstream if
  they're useful.
- Keep the source of every shipped build available, including a source archive with each release.
- Change or remove anything you're not comfortable with.

If the answer is no, or you'd like it done differently, that's completely fine: please tell me and I'll adjust.

Thank you for your time, and for everything you've built.

Best regards,
[Your name]
Duel Circuit
