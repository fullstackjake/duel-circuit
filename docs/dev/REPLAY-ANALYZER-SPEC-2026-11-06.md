# Spec (2026-11-06): the replay analyzer, v1

Owner's goal: "analyze replays, play by play, card by card, to see if the CPU is playing efficiently and to catch
misplays." An HTML report first (an in-app viewer later), for app duels and harness CPU-vs-CPU runs equally. Harness
only; the game and the replay format don't change. The analyzer never changes how any CPU plays (section 3c).

## 1. Commands

```
--analyze-replay <file|folder> [--out dir] [--side cpu|both|0|1] [--fast] [--as-played] [--pics [dir]] [--jobs N]
```

- **A file:** `*.replay` or a `.txt` holding a `YGOR1:` code (the headliner clips in `campaign/replays`). It writes
  `<out>/<name>.html` and prints one line per flag (`turn 5  high  missed lethal: ...`).
- **A folder** (recursive): one page per replay, plus `index.html` and `analysis.json`. The index has a table of replays
  (result, decisions, agreement %, flags by severity) and **the 50 most suspicious decisions across the folder**, ranked
  by severity then regret (section 2), each one linked to its row. It also groups flags by kind and by character.
- `--out` defaults to `build/analysis/<yyyy-MM-dd-HHmm>/`. It refuses any folder inside the input. To read the owner's
  save, the command line points at `~/Library/.../mods/replays` and the analyzer only reads it.
- `--side`: `cpu` (default) gives every CPU seat a second opinion, which means both seats in a harness run, and in an
  app duel the opponents plus your tag partner. `both` adds your seat, judged by the full-strength stand-in (the QA
  advisor's style: the top ladder character with your deck's playbook). `0` or `1` picks one team.
- `--fast`: the second opinion without the planner (the bot's heuristics and playbook only). `--as-played`: rebuild each
  CPU at its recorded skill instead of full strength (a fidelity check: a current-version replay should agree 100%).
- Exit codes: `0` analyzed (flags don't fail the run), `1` bad arguments or no replays, `2` some replay couldn't be
  played back (listed in the index).

**Keeping harness replays (opt-in):** `--keep-replays <dir> [--keep flagged|stalls|all] [--keep-max 200]` on
`--audit` (where `--out` stays as an alias), `--circuit-study`, `--wall-study`, `--alley-study`, `--peak-check` and
`--round-robin`. `flagged` (the default) runs the BlunderAudit watchers alongside and keeps the games with a finding.
`stalls` keeps prompt-cap stops, so you analyze them rather than re-tracing with `PEAK_REPLAY`/`ENDLESS_REPLAY`. Next
to each replay goes a sidecar, `<name>.replay.bot`: one line per seat
(`seat 1: character=<id> skill=0.92 seed=<n> playbook=<name> planner=on`, plus the knobs of a runtime-built personality
such as an alley regular). Without a sidecar (app replays), the analyzer uses the app's rules (section 4).

## 2. The per-replay page

Self-contained: inline CSS and JS, the data as one embedded JSON block, no CDN. It opens from `file://`.

- **Header:** title, date, decks, result and reason, turns, format version, mats, tag partners. It says which seats get
  a second opinion, and why not when one doesn't. A banner warns about drift (recorded with N cards, now M) and names
  the turn where playback diverged, if it did.
- **Body, turn by turn:** the narrator's play-by-play lines (`DuelNarrator`), with a row for each **decision** in
  between. Each row shows:
  - who decided (the active member in tag), the phase, and the prompt in words ("Main Phase 1 menu", "Chain to
    Raigeki?", "Pick 1 card to discard"). It lists **the options offered with readable card names and where they sit**,
    and marks **the one played**. Rejected answers that the core retried show as "retried".
  - **the second opinion:** the bot's pick and its source (planner, a playbook rule by name, tax bait, heuristics, or a
    random fallback). Then a **scores table** for the options: the heuristic score each one got, the planner's rollout
    score (plain / interrupted / risk / final) where it ran, and each option's **regret** (best score minus its own, in
    the planner's board points: a card in hand is 350, 1 LP is 1). Chunk 1 shows the bot's own reason line. Chunk 2
    adds the scores.
  - **flags with a severity.** **high:** missed lethal, lost to own card, decked out, or a regret of 2000 or more (or one
    that changes a win or a loss). **medium:** self damage of 1000 or more, a negated own effect, own effect cost more,
    removal wasted on a resolving card, or a regret of 800 to 2000. **low:** a downgraded monster, a risky summon and then
    a loss, a regret of 400 to 800 (the planner's own override margin), or a disagreement with no score to back it.
    **info:** disagreements on minor prompts (position, zone, order) and a risky summon that still won. The watchers'
    findings attach to the last decision before the message that raised them.
- **Board panel** (click a row): the board as it stood at that prompt, **with nothing hidden**. It shows both hands, all
  monster, Spell/Trap, field and Extra Monster zones (face-down cards revealed and marked), both Graveyards, banished
  cards (face-down ones marked), face-up Extra Deck cards, deck counts, LP, and the chain with its targets.
- **Filters:** flagged only, minimum severity, side, prompt kind, "hide minor" (forced or single-option prompts, which
  are on by default), and card-name search. Keys `n`/`p` jump between flags.
- **Pictures:** off by default. `--pics [dir]` adds thumbnails as `file://` links to the EDOPro `pics` folder, found the
  way the app finds it (`CardArt.cs`: `mods/settings.txt edopro_pics`, then `~/ProjectIgnis/pics` and its siblings). A
  missing picture leaves the text alone.
- **No real names:** the page uses the replay's names (original characters, and "You"). It shows file names only, never
  absolute paths, so the home folder never appears.

## 3. How it works

**a. Full information.** It plays the recorded responses back as `--show-replay` does: the `Replay.Recipe`, then
`YataLock.Attach`, then a `DuelRunner` whose seats are `AnalyzedSeat` wrappers. Each wrapper takes the next recorded
answer from the shared `ReplayFeed`. At each prompt, before it answers, the wrapper snapshots `BoardState.Refresh(duel)`
(full core queries: hidden cards included), the LP from the messages, and the chain from `ChainTracker` and
`DuelContext.Chain`. One `DuelContext` observes the whole duel, as in the app, so the shadows read the hints and the
history it keeps. The runner gets a `ResponseLog` too, so at every prompt the log holds exactly the answers before it,
which is what the planner replays. If the feed runs out before the recorded end, or the turn or winner differs, that
is a divergence: the banner names the turn and the rows stop there.

**b. The second opinion: a shadow that follows the record.** Each analyzed duelist gets a fresh `SmartDuelist` with that
character's personality and deck, its playbook (`Playbooks.ForDeck`, a new instance), and a `TurnPlanner` over the
shared `ResponseLog` (none with `--fast`). Skill is 1.0 unless `--as-played`. It is built as the app builds it: seed
`Settings.Seed * 4 + salt` (0/1 leads, 2/3 tag partners), or the seed in the sidecar. At each of its prompts (attempt 0
only), the shadow answers for real. The wrapper compares that answer with the recorded one: selections as sets, menus by
action and index. The recorded answer goes to the duel either way. **When they agree,** the shadow's memory is exactly
right. **When they disagree,** the row is flagged and the shadow is replaced by a fresh one before the next prompt. A
fresh bot's memory is nearly all per phase or per turn (actions this phase, tries, the attack it meant, chained-this-turn
counts), so it is right again from the next turn. Until that turn ends, its opinions on that phase's follow-ups (the
target or material of a play it wouldn't have made) are marked "fresh memory" and capped at low. The bot's existing log
line (the `log` argument: "(planned) X (+620 over the usual play)", "[rule]") gives the source and reason with no AI
change.

**c. AI hooks (chunk 2, read-only).** No snapshot or restore, and no change to any decision path:
- `src/Ygo.Engine/AI/DecisionNotes.cs` (new): small record types, and a sink interface.
- `SmartDuelist.cs`: a `Notes` property, null by default. It gets a callback in `IdleCore.Consider` (every option scored,
  plus the blunder that rejected it), in the `Chain` option loop, in the `BattleCore` attacker and target benefits, and
  in `Rank` for selections. It also gets a public `CurrentExposure(me)` that wraps the private `Exposure`. Every call is
  `Notes?.Invoke(values already computed)`. They touch neither `_rng` nor any field.
- `TurnPlanner.cs`: a `Scored` callback in `Decide` for each candidate (label, response, follow-up target, plain,
  interrupted, risk, final score, usual or not, chosen or not). It also gets a public
  `ScoreOptions(prompt, me, options, exposure)`, which runs the same candidate loop with no margin. `Decide` and
  `ScoreOptions` share one extracted loop with the same early outs and timing stats.
- Not touched: `.Sense`, `.Taxers`, `TurnPlanner.Combo.cs` (combo search is off by default), `DuelContext.cs`,
  `Personality.cs`, and the playbooks.

The planner's own `Decide` covers the Main Phase 1, Battle and Main Phase 1 selection decisions. For a disagreement
anywhere else in the seat's own turn (Main Phase 2, its own chains, other selections), the analyzer calls `ScoreOptions`
on just [played, shadow's pick]. That's 2-4 rollouts. **Opponent-turn decisions** (chain responses, hand traps) get the
bot view and the watchers but **no rollout score**. The rollouts' turn player is a public-cards-only responder, so it
can't play its turn faithfully.

**d. Watchers.** `BlunderAudit.Watcher`, `Finding` and `Spy` become `internal`. `Finding` gains the message ordinal, so
each one can be attached to a decision. That edit is harness only.

## 4. Which replays work

- **Formats 1-5** all load (`Replay.Read`). 2 adds tag, 3-4 add looks (cosmetic), 5 adds mats (shown in the header).
  Lessons and shows record nothing.
- **Characters:** the id is looked up in `personalities/`. An alley regular (`alley-regular`) is rebuilt the way
  `BackAlley.PersonalityOf` does it: the knobs of the roster character whose first deck is the recorded deck, under the
  handle. Penny is played as herself. **Decks:** the playbook comes from the deck name, with a ` (loaner)` suffix
  stripped, or else from the `decks/**/*.ydk` file whose main and extra decks match by card count. Era-table duels
  replay exactly, because the replay records the codes that were played (`PlayTexts`).
- **Tag:** four duelists, routed by `TagDuel.Seats` to `AnalyzedSeat` wrappers. Your CPU partner counts as a CPU. The
  board shows the active member's hand; the waiting partner's hand shows only if the core reports it.
- **Unknown character, or a skill that can't be rebuilt:** the second opinion is off for that seat, with the reason in
  the header. Everything else still renders: full information, prompts, choices and watchers. `--stand-in` uses the
  balanced personality anyway.
- **Drift:** a card-count mismatch gives a warning, and a divergence stops the rows at that turn (section 3a).

## 5. Performance

Measured on this machine: a planner decision averages **203 ms** (158 decisions in 4 expert-vs-expert duels, slowest
852 ms), and the audit plays a game with both seats thinking in about **8.4 CPU-seconds**. Plain playback takes about
0.1 s. The shadow costs what the original CPU's thinking cost, plus 2-4 rollouts per disagreement. Rollouts replay the
duel from the start, so long duels cost more.

| Mode | Per replay (one core) | 30 app replays, 10 cores |
|---|---|---|
| `--side cpu` | about 5-20 s per CPU seat | about 1-2 min |
| `--side both` | about 2-2.5 times that (you disagree with the bot more often) | about 3-4 min |
| `--fast` | under 1 s | a few seconds |

Replays run in parallel (`--jobs`, default the processor count), each one single-threaded and deterministic. A page is
about 0.5-1 MB for a 500-decision duel: card data is stored once and each snapshot as compact arrays.

## 6. Checks: `--analyze-test`

1. **Structure:** it analyzes `campaign/replays/return-rook.txt` and a fixed-seed tag duel. Each page has one row per
   prompt and a snapshot for every row. The JSON parses. There are no `http` URLs and no absolute paths, and the turn
   count and winner match the replay.
2. **A known flag:** a seeded CPU duel where a `Saboteur` wrapper ends the Battle Phase at the first lethal-on-empty-field
   menu. It searches seeds 1-200 for one. The page must hold a **high "missed lethal"** row on that decision with
   "the bot disagrees" set, and from chunk 2, a regret that changes the result.
3. **Fidelity:** a current-version harness game analyzed `--as-played` must agree on 100% of its CPU decisions.
4. **Play is unchanged (chunk 2):** 8 seeded planner duels played with the `Notes`/`Scored` sinks attached and with them
   off must have byte-identical response logs. `--replay-test` and `--battle-misplay-test` must still pass.

## 7. Build plan

**Chunk 1: harness only. It touches no AI file, so it can start before the misplay fix lands.**
New files: `src/Ygo.Harness/ReplayAnalyzer.cs` (the command, playback, `AnalyzedSeat`, shadows, flags, batch),
`ReplayAnalyzer.Page.cs` (the page and index writer), `ReplayAnalyzer.Text.cs` (prompts and answers in words; the
`PromptTrace.Describe` logic moves here and PromptTrace calls it), `ReplayKeeper.cs` (`--keep-replays` and the sidecar),
and `ReplayAnalyzerTest.cs`. Edits: `Program.cs` (flags), `BlunderAudit.cs` (section 3d), the studies named in section 1
(an optional keeper), and `docs/dev/TODO.md`.

**Chunk 2: after the misplay fix lands in `src/Ygo.Engine/AI/**`.** The section 3c hooks (`DecisionNotes.cs`,
`SmartDuelist.cs`, `TurnPlanner.cs`), the scores table and regret severities on the page, and checks 2 (regret) and 4.
Unity isn't touched. `analysis.json` is the bridge to the in-app viewer later.

## 8. Owner decisions (recommendation first)

1. **The second opinion at full strength** (skill 1.0, with the planner) for every CPU, even characters recorded below
   it, with `--as-played` for exact reproduction. The other option: the recorded skill by default.
2. **Pictures:** none by default, with `--pics` linking the EDOPro folder. The other option: embedded thumbnails (pages
   of 5-10 MB, but they work on any machine).
3. **Harness keeping:** opt-in `--keep-replays` with `--keep flagged` and a cap of 200. The other option: `all`.
4. **Opponent-turn decisions:** v1 gives them the bot view and the watchers without rollout scores. The other option: a
   later "hindsight" mode where rollouts know the opponent's real hand, labeled as hindsight.
5. **Your seat (`--side both`)** is judged by the top ladder character playing your deck. The other option: a
   mirror of your deck's own character, when it has one.

## The owner's answers (2026-11-06)

All as recommended: the second opinion at full strength (skill 1.0 with the planner; `--as-played` for the recorded
skill); pictures off, `--pics` links the EDOPro folder; harness keeping defaults to `--keep flagged` with a cap of 200;
v1 gives opponent-turn decisions no rollout scores, and `--side both` judges your seat by the top ladder character
playing your deck. Build v1: chunk 1 now, chunk 2 after the misplay fix lands.
