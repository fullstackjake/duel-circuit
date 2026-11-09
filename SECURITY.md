# Security

Duel Circuit is an offline, single-player game. The game's own code makes no network requests. It has no accounts
and keeps no personal data.

The main risk is files people share with each other: deck files (`.ydk`), deck and replay codes, and mod files
(pictures, portraits, mats, ban lists and settings in the `mods` folder). A shared file should never be able to crash
the game in a harmful way, run code, or read or write files outside the game's own folders. Only use mod files from
people you trust.

## Supported versions

Only the newest release gets fixes.

## Report a problem privately

If you find a security problem, please don't open a public issue.

1. Go to the [Security tab](https://github.com/fullstackjake/duel-circuit/security) of this repository.
2. Click **Report a vulnerability** (GitHub's private vulnerability reporting).
3. Tell us what you found, how to make it happen, and which version you tested.

Only the maintainer can see the report. We'll answer there, fix the problem, and credit you if you'd like.

For anything that isn't a security problem, open a normal issue.
