# Story review (2026-11-06): recommendations

Two reviewers read the whole story overnight, read-only: a Narrative Designer on the player-facing writing and a
Narratologist on structure. Their full findings, with scene ids and suggested rewrites, are in
[story-review/WRITING-2026-11-06.md](story-review/WRITING-2026-11-06.md) and
[story-review/STRUCTURE-2026-11-06.md](story-review/STRUCTURE-2026-11-06.md). This page merges them. Nothing is changed
yet. Every recommendation keeps the owner rules (original characters, no real players' names, CPUs at full strength) and
NO-LIMITS-STORY's continuity list.

## What works

- **The through-line holds.** Each strand gives you a new name that answers its question: champion, a name above Gus's,
  Upstairs then Midnight, then Local. Every strand comes home upstairs to Gus.
- **One idea runs through all four parts:** a card means something when it's played and lost fair. The alley's ante and
  No Limits extend it rather than dilute it.
- **Strong pieces:** the held-card payoff (the best in the game), the No Limits pilots' voices, Moth, Baz, Nocturne,
  Penny.

## The problems both reviewers found

1. **Nyx's Table is out of date.** Nyx names "the Emperor, the crow", but seat 2 is now Thunder Dragon (the difficulty
   pass), and Volta Kask sits there without a word.
2. **The end chair breaks in wave 2.** Junia's lines in `nl-first-visit`, `nl-junia` and `nl-junia-beat` say the chair is
   hers, which turns false once Remy is ready and closes; `nl-remy-beat` can play before Junia's summer ends. Both
   reviewers wrote chair-neutral rewrites.
3. **Small continuity slips:** `alley-closing` puts you at the top even while Ilara holds the Midnight Open; Moth says
   "home" (against his own rule); Lux, a wave-2 pilot, speaks in a wave-1 scene; `nl-invitational` says "half signed"
   though nobody signs; `nl-restored` changes rooms inside one scene.

## Writing (Narrative Designer)

- **About 45 scenes repeat a line the player read a second earlier:** 15 of 21 gift scenes open with the lose line the
  results just showed; every headliner and No Limits entrance scene ends with its versus intro; most beat scenes repeat
  the lose line. Rewrite them, and add the rule to story.txt's header.
- **The generic partner scene** plays the same three lines for every new partner without their own scene.
- **Borrowed tics:** Kit's "...Mostly." in other mouths, Baz's "Somebody X!" in Imogen's and Mika's, "Rude." from four
  characters; two pairs of characters share a deck or a joke (Ribbit Ruiz and Mass Driver Moss, Yata Mags and Baron
  Corvus).
- **Pacing:** up to about 28 lines before your first alley duel; seven lines over about 105 characters; a few muddled or
  wrong lines (a Kashtira mechanic line among them).
- **Cast:** Mika and Moth speak before they're introduced; Rhea and Seraph are still never mentioned; Ryo, Tink and Brask
  vanish after the first band; the nameless regulars' win and lose pools have only three lines each.

## Structure (Narratologist)

- **Ilse is the last threshold and is never tested.** Her name stays above yours, she has duel lines for a duel that never
  happens, and her best line has no setup. Recommended: Ilse as a guest after the final, a setup line in `first-stamp`,
  and a `graveyard-kings-clear` scene.
- **Key beats are optional or off-screen:** Gus fixing the back door is only a random footer; Kit's motive plays only if
  you lose a card of your own, and Kit has no introduction; Ines has no `beat:` scene; Vera's rival arc never gets its last
  match; Junia drops "Local" without giving you a new word.
- **Scenes can't check what has played.** Story scenes have no `after:`/`before:` gates (shop lines do), so lines that
  depend on the post-game's order can't be fixed. A small change in Story.cs.
- **Still open from the last review:** no scene reacts to a loss ("lose and learn" is never shown); two promises point at
  an unbuilt Champion's Ladder; New Game+ has no story; 10 of 21 circuits are silent.

## Recommended order (merged, value for effort)

1. Fix Nyx's Table, the end-chair lines and the small continuity slips (text only).
2. Rewrite the scenes that repeat the versus intro or the lose line (text only).
3. Cut the generic partner scene to one neutral line; introduce Mika and Moth; fix the borrowed tics, Moth's "home", the
   long and muddled lines (text only).
4. Kit's introduction and motive; Gus's back door on screen; `gift-ines`; Junia's last word (text, a few new scenes).
5. Plant the Duchess on the critical path and set up Ilse's confession (text).
6. Widen the regulars' win and lose pools to about eight lines; Penny's post-loss pools (text).
7. `after:`/`before:` gates for story scenes (small code).
8. Ilse as a post-final guest (a feature: a deck, a guest entry, scenes).

Items 1-6 are writing only and can be done in one pass by the Narrative Designer, checked by `--campaign-test`'s story
checks.

## Decisions for the owner (recommendation first)

1. **Ilse duels:** a guest after the Every Era final. *Or she stays a name on the board.*
2. **The end chair:** keep the decided order (Remy closes, Junia wildcards) and ship the chair-neutral text. *Or Junia
   holds the chair until she's beaten.*
3. **The Champion's Ladder:** commit to it as where Ilse's and Junia's promises lead. *Or soften both lines now.*
4. **Repeated lines:** fix them in the text. *Or stop the results screen showing the lose line when a scene follows.*
5. **Volta Kask at Nyx's Table:** acknowledge him in the scene.
6. **Ribbit Ruiz:** retitle him so he isn't a second "Mass Driver". *Or keep both.*
7. **Partners:** a one-line partner field in each profile, used by the generic partner scene.
8. **Vera's last match:** a rematch as a guest on a trap deck.
9. **A first-loss scene** (`firstloss`), so "lose and learn" is shown once.
10. **Cameos:** one-liners for the early cast (Ryo at the first Locals).
11. **The key-holder before Nyx:** stays unanswered in the game, answered privately in the bible.
12. **A three-strand finale:** Gus at the top of the back step, Baz finally naming him.

## The owner's answers (2026-11-06)

All twelve taken as recommended:
1. **Ilse duels:** a guest after the Every Era final (built now; spec [ILSE-GUEST-SPEC-2026-11-06.md](ILSE-GUEST-SPEC-2026-11-06.md)).
2. **The end chair:** Remy closes, Junia wildcards; Junia's lines rewritten chair-neutral.
3. **The Champion's Ladder:** committed to. Ilse's "ladder" and Junia's "other doors" stay as written and lead there; it
   is a planned feature (TODO), not built now.
4. **Repeated lines:** fixed in the text, and the rule goes in story.txt's header.
5. Volta Kask acknowledged at Nyx's Table. 6. Ribbit Ruiz retitled. 7. A one-line `partner:` field in each profile for the
   generic partner scene. 8. Vera's rematch as a guest on a trap-waiting warriors build: later, not in this pass.
9. A `firstloss` scene. 10. Early-cast cameos. 11. The key-holder before Nyx stays unanswered in the game, answered in a
   private bible. 12. The three-strand finale, Gus at the top of the back step, Baz naming him.

What's being built: [NEXT-BUILD-2026-11-06.md](NEXT-BUILD-2026-11-06.md).
