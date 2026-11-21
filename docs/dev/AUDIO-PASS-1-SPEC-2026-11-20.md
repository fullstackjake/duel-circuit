# Audio pass 1: build spec (2026-11-20)

The quick fixes, the mixer and the audio check from
[PRODUCTION-REVIEW-AUDIO-2026-11-20.md](PRODUCTION-REVIEW-AUDIO-2026-11-20.md) (its Q1-Q10, M3 and M6), as one build.
Line numbers are from commit 3b36438. Everything stays synthesized in code: no audio files, nothing downloaded.

**Not in this build:** new music (M1, M2: pass 2), stereo by side (M4), the synthesis polish (M5: band-limited
oscillators, reverb, a small-speaker layer), text blips (B2), sectioned scores (B3). The Settings reorder and the pack
screen rebuild belong to the interface review (its Q7 and M4); this build leaves hooks for both (sections A and H).

**Nobody will hear this build in its checks.** Every check runs muted; each rule below has a check that proves it from
the sound log, the limiter's meters or a rendered file. The owner's listening pass (section "Listening pass") is the
only test of how it sounds.

## What the player gets

| # | Today | After |
|---|---|---|
| A | Three volume rows that cycle off-20...100 and wrap from 100 to Off; Sound effects Off also silences clicks; no master | A sound block: **Master volume**, Sound effects, Music, Interface sounds as sliders in 5% steps (drag, click, < >, arrow keys, d-pad, both ways); Interface sounds independent of Sound effects; **Mute in the background** |
| B | No limiter: big moments can sum past full scale | A limiter on the whole output holds every peak under -1 dBFS |
| C | Opening the menu or Settings in a duel swaps to the menu music; the duel theme restarts on return | The duel theme keeps playing at half volume while paused, and comes back where it was |
| D | Sound carries on behind other windows | Fades out (0.25 s) when another window is in front, back when you return (setting, on by default) |
| E | Coin flight, phases, LP counting, Sets and banishes have no sound of their own; a draw game plays the turn chime | A spinning coin and its landing, a soft phase tick, LP ticks as the counter rolls, a Set tap, an airy banish, a Flip Summon flip, a draw-game chord |
| F | Every chain link plays the same chime and quick links get quieter | Each link a step higher (root, 2nd, 5th, octave); the chain resolving ticks back down |
| G | Duchess Ilse and Vera play the everyday theme; `--boss-music` fails | Their own themes (Nocturne's three semitones up, Rhea's two down) and versus stings |
| H | Every rarity from Ultra up sounds the same; dealing is silent | Secret, Ghost and Starlight reveals add their duel chimes; each card dealt ticks; the music dips under the big reveals |
| I | Win, lose and the other tonal sounds are in C over music in 17 keys | Tuned to the music's key, as the prize chimes already are |
| J | A ringing sound can be re-pitched by a later one; pitch jitter shares Unity's random numbers with the Back Alley's exhibitions | One sound per voice; the jitter has its own seeded generator |
| K | Sound counters only | A sound log in check runs: name, group, volume, pitch, time, played or why not |
| L | Docs promise hover sounds; the roadmap and licensing are behind | Docs match the build (Technical Writer) |
| M | `--export-sounds` writes files; nothing checks them | `--audio-check`: renders everything and checks loudness, peaks, seams, the boss list, the limiter |

## A. The sound settings block (Q1, M3)

### Volume groups

Four groups, each a saved float 0-1 (PlayerPrefs, same keys as today where they exist):

| Group | Key | Default | Scales |
|---|---|---|---|
| Master | `masterVolume` (new) | 1.0 | everything below |
| Sound effects | `sfxVolume` | 0.8 | every effect not in Interface |
| Music | `musicVolume` | 0.5 | themes, the menu loop, the sting, the endings, the rain bed |
| Interface sounds | `uiVolume` | **0.65** (was 0.8) | `click`, `hover`, `error`, and every `Play(..., ui: true)` (the deck editor's change sounds) |

- A voice's volume = call volume × repeat fade × group volume × Master. **Interface no longer multiplies by Sound
  effects** (`Sounds.cs:123,139` today). A group at 0 (or Master at 0) plays nothing and logs `Played = false`,
  `Why = "group off"`.
- Music sources: Master × Music × duck × pause dip × track level × layer gain. `_shot` (sting, endings): Master × Music.
  The rain bed: Master × Music × 0.35 × its level.
- The new Interface default (0.65) keeps a fresh game's clicks where they are today (0.8 × 0.8 = 0.64).

### Old settings load (migration)

- `masterVolume` absent: 1.0. `sfxVolume`, `musicVolume` keep their values (0, 0.2 ... 1.0 sit on the 5% grid).
- One migration, run once in `Sounds.Create` before anything plays, guarded by the int key `audioPrefs` (absent = 1):
  when `audioPrefs` < 2 and `uiVolume` exists, `uiVolume = uiVolume × sfxVolume` (what that player heard); then
  `audioPrefs = 2`. A player who never touched the row has no `uiVolume` key and gets the 0.65 default. The stored
  0.64 shows as "64%" until the slider moves (it then snaps to the 5% grid).
- `musicReactive` is unchanged. `muteInBackground` (new int, default 1).
- The migration is a pure function (`Sounds.MigratePrefs`, interface below) so the check runs it on a dictionary.

### The block

- **One function builds it:** `SettingsSound.Build(RectTransform rows, float y, Action refresh) → float` in a new file
  `SettingsSound.cs`. It draws its rows from `y` down and returns the `y` after its last row. A later reorder (the
  interface review's Q7) moves this one call.
- **What it replaces:** in `MenuScreens.cs` `SettingsScreen.Refresh`, the four rows at lines 602-610 (Sound effects,
  Music, Reactive music, Interface sounds) become `y = SettingsSound.Build(_rows, y, Refresh);`. The heading "SOUND AND
  SCREEN" (line 601) stays above it; Window (line 611) and every row after it keep their order and their look, 208 px
  lower in the scroll (the block is two rows taller). **Nothing else on the screen changes.** `Next` (line 525) goes if
  nothing else uses it.
- **Rows, in this order** (existing order kept, Master added on top, Mute at the bottom):

| Row | Control | Help line (15 px, as today's rows) |
|---|---|---|
| Master volume | slider | "Everything the game plays. Drag, click, or use < > or Left / Right, in 5% steps." |
| Sound effects | slider | "Summons, attacks, damage, packs and other game sounds. Buttons have their own row below." |
| Music | slider | (unchanged) "A calm theme in the menus, a driving one in duels, and a theme of their own for bosses. It dips under negations and big hits." |
| Reactive music | On / Off button (unchanged) | (unchanged) |
| Interface sounds | slider | "Button clicks, hovers and errors, on their own: Sound effects doesn't change them." |
| Mute in the background | On / Off button | "On: the game goes quiet while another window is in front, and comes back when you return." |

- **Row look:** exactly as `Row` draws today (`MenuScreens.cs:537-545`): a 1700 x 94 box in `#1A2030` at the row's `y`,
  the name at (30, 8, 500 x 42) 26 px bold white, the help at (30, 50, 1100 x 40) 15 px `(0.72, 0.76, 0.86)`; rows 104 px
  apart. On/Off rows use `Ui.BoxButton(rt, 1260, 19, 400, 56, value, ..., 20)` as today.
- **Slider rows**, inside the same box (all from the box's top-left):
  - `<` button: `Ui.BoxButton(rt, 1200, 19, 48, 56, "<", ...)`; `>` button: `Ui.BoxButton(rt, 1556, 19, 48, 56, ">", ...)`.
    They step 5% down and up and stop at 0% and 100% (no wrap).
  - A Unity `UnityEngine.UI.Slider` between them: rect (1260, 33, 284, 28); `minValue 0`, `maxValue 20`,
    `wholeNumbers true` (one step = 5%); background 284 x 8 in `#2A3245`, fill in `#FFC940`, handle 24 x 24 white.
    The Slider gives drag, click-to-jump and Left/Right from keys and pad (its `OnMove`); Up/Down still move between rows.
  - Value label at (1612, 19, 80, 56), 20 px white, right-aligned: "80%", or "Off" at 0.
  - While dragging or stepping, the volume applies at once and the label updates in place; the rows are **not** rebuilt
    (`refresh` is not called by sliders). Saved (`PlayerPrefs.Save`) on pointer up, after each `<` `>` or key step, and
    when the screen closes.
  - Preview on release or step: Master and Sound effects play `summon` (as today's row), Interface plays `click`, Music
    none (the music is playing).
- **Hover sound:** buttons only (section E). The slider rows' Selectable is the Slider itself.

## B. The limiter (M3)

**Decided here: a code-only mix, no `AudioMixer` asset.** Unity has no public API to create an `AudioMixer`, its groups
or effects from a script (the mixer controller is internal), so a mixer could only be made by hand in the editor, which
batch builds can't do. The groups are the volume products above; the limiter runs in `OnAudioFilterRead` on the
GameObject that holds the `AudioListener`, which processes the final mix.

- **The algorithm** lives in the engine (`Ygo.Engine.Audio.Limiter`, pure C#, no Unity) so the harness tests the same
  code the game runs:
  - Threshold T = 0.89 (-1.0 dBFS). Lookahead L = round(0.002 × rate) frames (96 at 48 kHz). Release 120 ms.
  - Per frame: x = the largest |sample| over its channels (stereo linked). The frame goes into a delay line of L
    frames. P = the largest x over the last L + 1 frames (a ring buffer with a running maximum; O(1) amortised).
  - Target t = P > T ? T / P : 1. Gain g: when t < g, `g += (t - g) × (1 - exp(-6 / L))` per frame (reaches 99.75% of
    the drop within L); when t ≥ g, `g = t + (g - t) × exp(-1 / (0.120 × rate))`.
  - Output = the delayed frame × g. Safety: any |y| > 0.98 is clamped to ±0.98 and counted in `Clamped`.
  - Meters (since the last reset): `InputPeak`, `OutputPeak`, `MinGain`, `Clamped`, frames processed.
  - No allocation after construction. It reads no clock and no random number.
- **In the game:** `OutputLimiter` (Unity) is added by `Sounds.Create` to the same GameObject as the `AudioListener`,
  after it. Built in `Awake` with `AudioSettings.outputSampleRate` and the speaker mode's channel count. In
  `OnAudioFilterRead(float[] data, int channels)`: `Limiter.Process(data, channels)`, then the background fade (section
  D) multiplies the output, then, when `Sounds.OutputMuted`, `Array.Clear(data)`. The meters are read **before** the
  mute, so muted checks still see real levels.
- **`-mute` changes meaning:** `DuelController.cs:582-583` sets `Sounds.OutputMuted = true` instead of
  `AudioListener.volume = 0`. `Sounds.Silent` (`Sounds.cs:67`) and `DebugState`'s "listening" read `OutputMuted`
  instead of `AudioListener.volume`. A muted run now mixes audio and outputs zeros.
- **How a check proves it with nobody listening:** `-soundcheck` (section K) sets all four volumes to 1 in memory, plays
  the final-blow pile-up (`damage_big` and `impact` at 0 s, `win` at 0.2 s) over the duel theme, waits 0.6 s and reads
  the meters: `InputPeak > 1.0` (the pile-up really overshoots), `OutputPeak ≤ 0.90`, `Clamped == 0`. Offline,
  `--audio-check` runs the same pile-ups through `Limiter` (section M).

## C. Pausing a duel (Q2)

- In `UpdateMusic` (`DuelController.cs:2358-2363`): a duel is **paused** when `_runner != null`, `_board != null`,
  `_board.Winner < 0`, `_menu.IsOpen || _settings.IsOpen`, and none of the other screens in the `menus` list is open.
  Then the music stays `"duel"` and `Sounds.PauseDip(true)`; otherwise `PauseDip(false)` and the choice is as today.
- `PauseDip(true)` moves a music factor to 0.5 over 0.3 s; `false` back to 1 over 0.3 s. The track and its sources are
  never stopped or rebuilt, so it carries on from where it was. The layers keep their targets (the duel is paused, so
  they don't change).
- Leaving the duel from the pause menu (`_runner` null, or the title opens) gives the menu music as today.

## D. Mute in the background (Q3)

- `Sounds` handles `OnApplicationFocus(bool)` (and `OnApplicationPause(true)` as unfocused). Unfocused and
  `MuteInBackground` on: the output gain heads to 0; otherwise to 1. `OutputLimiter` ramps the gain per sample over
  0.25 s (on the audio thread, from a target float set on the main thread).
- `Application.runInBackground` stays true (`DuelController.cs:90`): the duel goes on; only the sound fades.
- Turning the setting off while unfocused can't happen (you're in the game); turning it off restores gain 1 at once.

## E. The silent moments (Q4) and hover (Q1)

New effects in `Sfx.Effects` (engine), from the existing building blocks, mono, peak-normalised as the rest:

```csharp
["coin_spin"] = () => Normalize(Mix(new[] { Swish(1.3, 1200, 2600, 0.2) }.Concat(Enumerable.Range(0, 7).Select(i =>
    Delay(Mix(Tone(3400, 3000, 0.03, Wave.Sine, 0.001, 0.028, 0.5), Noise(0.02, 8000, 6000, 0.001, 0.018, 0.25)), i * 0.186)))
    .ToArray()), 0.4),
["coin_land"] = () => Normalize(Mix(Thud(0.12, 180, 0.8), Tone(2900, 2900, 0.35, Wave.Sine, 0.001, 0.33, 0.35),
    Tone(4350, 4350, 0.2, Wave.Sine, 0.001, 0.18, 0.15)), 0.55),
["phase"] = () => Normalize(Mix(Tone(1046.5, 1046.5, 0.06, Wave.Triangle, 0.002, 0.055, 0.5),
    Tone(1568, 1568, 0.09, Wave.Triangle, 0.002, 0.085, 0.4), 0.05), 0.3),
["set"] = () => Normalize(Mix(Thud(0.07, 220, 0.6), Noise(0.05, 2500, 1200, 0.002, 0.045, 0.5)), 0.45),
["banish"] = () => Normalize(Mix(Swish(0.45, 800, 9000, 0.7), Tone(300, 1200, 0.4, Wave.Sine, 0.02, 0.38, 0.3),
    Shimmer(0.4, 0.05)), 0.6),
["chain_resolve"] = () => Normalize(Tone(1568, 1046.5, 0.07, Wave.Triangle, 0.002, 0.065, 0.5), 0.35),
["draw_game"] = () => Normalize(Echo(Mix(Chord(new[] { 440.0, 587.3, 659.3 }, 1.2, 0, 1.1, 0.2),
    Tone(220, 220, 1.2, Wave.Triangle, 0.01, 1.15, 0.3)), 0.18, 0.3), 0.6),
```

Where they play (Unity), all in the Effects group unless said:

| Moment | Where | Sound |
|---|---|---|
| Coin flight | `CampaignScenes.cs` `Toss()` (433-453): when the flight starts (not under Reduced motion) | `coin_spin` 0.8 |
| Coin lands | same, at line 453, before today's `win`/`turn` | `coin_land` 0.7, then `win`/`turn` as today 0.12 s later (`PlayLater`) |
| Phase banner | `BoardFx.PhaseBanner` (1080-1090), only for the four named phases | `phase` 0.5; Battle Phase 0.8 |
| LP counter rolling | `BoardView.cs` 2547-2549, while `_lpShown` moves and `Fx.Tempo > 0` | `lp_tick` 0.3 every 60 ms per plate; pitch 1.0 going down, 1.25 going up |
| A card Set | `BoardFx.MoveCard` (297-306): to MZone/SZone face-down (`(to.Position & 0xA) != 0`), from Hand or Deck | `set` 0.8 (was `place`) |
| Banished (not destroyed) | same: to Removed, not by destruction | `banish` 0.8 (was `destroy`) |
| Sent to GY (not destroyed) | same | `destroy` at 0.6 (was 0.8) |
| Destroyed on the field | unchanged (`destroy` 0.9 with the shatter) | — |
| Flip Summon | `BoardFx.Summon` (609) | `flip` 0.8, then the summon sound 0.08 s later |
| Draw game | `DuelController.cs:5718` | `draw_game` (dips the music like `win`/`lose`) |
| Button hover | `UiHover.OnPointerEnter` (114-118) and `OnSelect`, only for `Style == HoverStyle.Button` with no `Gate` or an interactable one | `hover` 0.5, Interface group |

- The move rules are one pure function, `BoardFx.MoveSoundFor` (interface below), so the check tests them without a duel.
- **Hover is on by default** (`Sounds.HoverSounds = true`, one line in `Sounds.cs`); the owner flips it there. It obeys
  the 50 ms guard and the repeat fade, so sweeping across a row of buttons doesn't rattle.
- Position changes stay silent (the core's position message isn't parsed into a `DuelEvent`; out of scope).

## F. Chains (Q5)

- `ChainSteps = { 0, 2, 7, 12 }` semitones (root, 2nd, 5th, octave: the prize chimes' rule, so they sit over major and
  minor). Link n plays `activate` at step `ChainSteps[min(n, 4) - 1]`; a link of 0 (no chain) at step 0. The pitch is
  `KeyRatio(root of activate, music key) × 2^(step / 12)`.
- `activate`, `chain_resolve`, `lp_tick` and `phase` are exempt from the repeat fade (they still obey the 50 ms guard).
- **Resolving:** in `BoardFx.ClearChain` (931-950), with n = the badges in the chain and n ≥ 2: for i = 0 .. n-1,
  `chain_resolve` 0.5 at `delay + 0.3 + i × 0.09 × Tempo` (`0.3` is when the badges start shrinking), pitch step
  `ChainSteps[min(n - i, 4) - 1]`: the top link first, stepping down. A one-link "chain" gets no resolve tick.
- None of this adds to the batch's length (`at`): sounds never hold the duel.
- `BoardFx.Sound` gains a pitch: `Sound(float delay, string name, float volume = 1f, float pitch = 1f)`.

## G. Ilse's and Vera's themes (Q6)

- `Sfx.Themes` gains `"ilse"` (Nocturne's theme with `Key` 11 + 3 = 2) and `"vera"` (Rhea's with `Key` 4 - 2 = 2),
  copies made by a helper so their notes stay one source. `BossMusic.ForGuest` already looks themes up by character id
  (`ilse`, `vera`): **no change in `src/Ygo.Engine/Campaign/`**. Their versus screens then play a sting (the existing
  `BossSting` rule).
- Ilse also sits on the Champion's Ladder (`campaign/ladder.txt:24`); `LadderMusic` looks up the id first
  (`DuelController.Ladder.cs:27-30`), so her ladder seat plays her theme too, as the other guests' do (owner decision 9).
- `--boss-music` must exit 0. DEVELOPING.md's "17 themes" becomes 19.

## H. Packs and grading (Q7)

**The hooks the pack-screen rebuild (interface review M4) must keep calling** live in a new `PackSounds.cs`; layout
code never plays a pack sound itself:

- `PackSounds.Opened()`: the tear (today `ShopScreen.cs:1014`).
- `PackSounds.Dealt(int index)`: each card as it's dealt in (`DealIn`, `ShopScreen.cs:1015-1020`, at each card): `draw`
  0.35, pitch `1 + 0.02 × min(index, 8)`.
- `PackSounds.Turned(Rarity rarity, bool hit)`: the one call when a card turns face-up (`Reveal`, today's `flip` at
  `ShopScreen.cs:1054`); it plays everything that card's reveal sounds like, scheduled with `Sounds.PlayLater` from the
  moment it's called. Today's `Sounds.Play(big ? "burst" : "rare", ...)` at `ShopScreen.cs:1084` goes.
- `PackSounds.RevealSounds(Rarity rarity, bool hit)`: the pure list `Turned` plays, for checks:

| Card | Sounds (delay from the turn, volume) |
|---|---|
| any | `flip` 0 s, 0.8 |
| hit, below Ultra Rare | + `rare` 0.26 s, 0.7 |
| Ultra Rare .. Platinum Rare | + `burst` 0.26 s, 1.0 |
| Secret Rare .. Collector's Rare | + `burst` 0.26 s, 1.0; `prize_secret` 0.46 s, 0.85 |
| Ghost Gold Rare, Ghost Rare | + `burst` 0.26 s; `prize_ghost` 0.46 s, 0.85 |
| Quarter Century Secret Rare, Starlight Rare | + `burst` 0.26 s; `prize_starlight` 0.46 s, 0.85 |

  (`hit` is `PackCard.Hit`; a non-hit plays `flip` only, whatever its rarity.)
- `PackSounds.SlabNumber(Rarity rarity, int slab)`: the grading desk's number beat (`GradingDeskScreen.cs:407`): slab 10
  plays `burst` 0.8 and `prize_ping` 0.85 at 0.25 s, plus the rarity's prize chime (as the table) at 0.45 s when the
  rarity has one; below 10, `rare` 0.8 as today. The `flip` at line 399 stays.
- `PackSounds.ChimeFor(Rarity)` returns the prize chime's name or null; `BoardFx.Prize` (1363-1364) may use it instead of
  its own copy of the mapping.
- **Dips:** `burst` and `level_up` dip the music to 0.6 for 1.0 s (a new soft set beside `Important`,
  `Sounds.cs:43`); `draw_game` joins `Important` (0.35 for 0.8 s).

## I. Tuning to the music (Q8)

- Engine table `Sfx.TunedRoots` (name → the pitch class it's built on): `prize_secret`, `prize_ghost`,
  `prize_starlight`, `prize_ping`, `prize_glint` 4; `win`, `lose`, `recover`, `level_up`, `activate`, `phase`,
  `chain_resolve` 0; `turn` 7; `draw_game` 9. Summons, thuds, swishes and noise aren't tuned.
- `Sfx.KeyRatio(int from, int to)` = 2^(s/12), s = `((to - from + 6) mod 12 + 12) mod 12 - 6` (-6 .. +5, the nearest
  way; a tritone goes down), the rule of today's `Sounds.KeyRatio`.
- `Sounds.MusicKey`: a theme's key as today; the menu loop `Sfx.MenuKey` = 9 (A minor: its chords are Am F C G); no
  music playing: -1, and then tuned sounds play at their own pitch (ratio 1). Today it returns 4 (E) for everything
  that isn't a theme.
- A tuned sound's pitch = the call's pitch × `KeyRatio(root, MusicKey)`, with **no random jitter**. Resampling changes
  its length by the same ratio (0.71x to 1.41x); that's accepted.

## J. Voices and determinism (Q9)

- **Voices:** a pool of 16 `AudioSource`s. Each play takes the first source not playing, else the one whose sound
  started earliest; it sets `clip`, `volume`, `pitch` and calls `Play()`. **No `PlayOneShot` on the pool**, so a source
  holds one sound and its pitch never changes while it rings. `_shot` (sting, endings) is unchanged.
- **Jitter:** non-tuned effects take pitch × (0.97 + 0.06 × r), r from a `System.Random` owned by `Sounds`, seeded
  `0x50D5` at `Create`. Never `UnityEngine.Random`: it's shared with `CampaignScreen.Alley.cs:787-789` (the exhibition
  picks), so today every sound played shifts which exhibition shows.
- **Duels stay deterministic:** sound never feeds back into the duel. Nothing in this build reads a sound's state in duel
  logic, `BoardFx` timings (`at`, `Busy`) don't change, and the engine changes are all in `src/Ygo.Engine/Audio/`.
  Recipe + `ResponseLog` replays are untouched; `--replay-test` and `-replaycheck` prove it.

## K. The sound log and `-soundcheck`

- **Log** (check runs only, `DuelController.IsCheckRun`; the last 4,000 entries): every `Sounds.Play` and `PlayLater`
  firing appends `SoundLogEntry` (time, name, group, final volume, final pitch, played, why). Why is `""`, `"group off"`,
  `"within 50 ms"` or `"unknown"`. `Played` counters stay as they are.
- **`-soundcheck <folder>`** (new, `DuelController.SoundCheck.cs`; absolute folder, fresh `-mods`, run with `-mute`).
  Each step fails on today's code:
  1. **Groups:** volumes in memory (Master 1, Effects 0, Interface 1): `click` is logged played, group Interface, volume
     0.6 (Ui's 0.6 call); `summon` not played, "group off". Effects 1, Interface 0: the reverse. Master 0: nothing plays.
  2. **Migration:** `MigratePrefs` on {uiVolume 0.8, sfxVolume 0.8} gives uiVolume 0.64, audioPrefs 2; run again,
     unchanged; on {} gives {audioPrefs 2} only.
  3. **Sliders:** open Settings; the block's rows are, in order, Master volume, Sound effects, Music, Reactive music,
     Interface sounds, Mute in the background; the row after them is Window. `DebugSlider("effects")` reads 80%; `<`
     once gives 75% and the log has `summon` at 0.75 × Master; `>` from 100% stays 100%; a Left key on the focused
     Master slider steps it down 5%; 0 reads "Off". The value is in PlayerPrefs after the step (check builds write their
     own domain: `DuelController.CheckPrefs.cs`). Restore the values the step found.
  4. **Hover:** pointer-enter on a settings button logs `hover` (Interface, 0.5 × the group); on a disabled button and on
     a card tile, nothing.
  5. **Pause:** a CPU-vs-CPU duel (as `-musiccheck` starts its real duel); open the menu for 2 s: the track is still
     `theme:duel:0`, its serial unchanged, `timeSamples` advanced, `PauseDip` 0.5; close it: 1.0 within 0.5 s, same serial.
  6. **Background:** `DebugFocus(false)`: output gain 0 within 0.4 s; `DebugFocus(true)`: 1; with the setting off,
     `DebugFocus(false)` keeps 1.
  7. **Moves and chains (pure):** `MoveSoundFor` gives `set` for hand → SZone face-down, `banish` for field → Removed
     without the destroy reason, `destroy` 0.6 for field → GY without it, `destroy` 0.9 (shatter path) for a destroyed
     field card; `ChainPitchStep(1..5)` = 0, 2, 7, 12, 12; `ResolveSteps(3)` = 7, 2, 0; `ResolveSteps(1)` empty.
  8. **A real duel** at speed 4 to its end: the log has `phase` (at least 4), `lp_tick` (at least 3), `turn`, and every
     `activate` logged with link n ≥ 2 has a higher pitch than the link before it in the same chain (reported, and
     counted; the step passes only if at least one chain of 2+ links happened, else it reruns with the next seed, up to
     3 seeds).
  9. **Coin toss:** `_scenes.ShowCoinToss` with a dummy `done`: `coin_spin`, then `coin_land` about 1.3 s later, then
     `win` or `turn` 0.12 s after that. Under Reduced motion: no `coin_spin`.
  10. **Draw game:** the result sound for winner 2 is `draw_game` (the step calls the same function `AnnounceResult`
      uses).
  11. **Packs (pure):** `RevealSounds` for each table row gives exactly the table; `ChimeFor(StarlightRare)` is
      `prize_starlight`, `ChimeFor(UltraRare)` null; `SlabNumber(SecretRare, 10)` logs `burst`, `prize_ping`, `prize_secret`.
  12. **Tuning:** with `beginner` (key 0) playing, `win` logs pitch 1.000; with `expert` (key 4), 1.260; on the menu loop
      `level_up` 0.841 (-3); `prize_secret` over `beginner` 0.794 (-4); no jitter on any of them (±0.0005).
  13. **Voices:** `prize_secret`, then 20 `click`s in 1 s: the prize's source pitch never changes while it plays and no
      pool source holds two sounds at once (`DebugVoices`).
  14. **Limiter:** section B's pile-up: `InputPeak > 1.0`, `OutputPeak ≤ 0.90`, `Clamped == 0`; then 2 s of the menu loop
      at Music 0.5: `MinGain ≥ 0.999`.
  15. **Jitter source:** 50 plays of `summon` leave `UnityEngine.Random.state` unchanged.
- `-musiccheck` must still pass (its muted steps now go through `OutputMuted`). `-menucheck`'s "sound reaches the
  output" step runs unmuted as today and must still pass (the limiter passes normal levels untouched).

## L. Docs (Q10)

After the build, the Technical Writer (Sonnet): `docs/PLAYING.md` (the sound block, mute in the background, hover),
`docs/DEVELOPING.md` ("Sound and music": new effects, 19 themes, the limiter, `-mute`'s new meaning; the `-soundcheck`
and `--audio-check` rows), `docs/dev/UI-ROADMAP.md` item 22 (2000 LP, seven layers), `LICENSING.md` (a row: "Music and
sound effects: written as code in `src/Ygo.Engine/Audio/`, AGPL-3.0-or-later with the code; no audio files ship"),
`docs/dev/TODO.md`.

## M. `--audio-check` (M6)

`dotnet .../Ygo.Harness.dll --audio-check [--write-baseline]`. Lines read `audio check: ok|FAIL|warn <what>`; exit 1 on
any FAIL. It renders everything in memory (writes nothing unless `--write-baseline`).

**The meter** (`src/Ygo.Harness/Loudness.cs`): ITU BS.1770 K-weighting for any sample rate (the pre-filter high shelf
at 1681.97 Hz, +4.0 dB, Q 0.7072, and the RLB high-pass at 38.14 Hz, Q 0.5003, by the bilinear transform, as libebur128
does); mono weight 1. **Momentary max**: 400 ms windows every 100 ms over the sound padded with 0.5 s of silence;
LUFS = -0.691 + 10 log10(mean square). **Integrated**: 400 ms blocks with 75% overlap, gates -70 LUFS and -10 LU. The
builder confirms it once against ffmpeg (within 0.3 LU): `summon` -15.6 momentary, `menu` -21.2 integrated, the
everyday theme's bed + drums -23.3 integrated (commands under Verification).

| # | Checks (FAIL unless marked warn) | Today's numbers |
|---|---|---|
| 1 | Every effect renders, no NaN or infinity, sample peak ≤ 0.96, first and last samples ≤ 0.01, length 0.01-3.0 s, and two renders are identical (hash) | peaks 0.07-0.95 |
| 2 | Against `src/Ygo.Harness/fixtures/audio-baseline.txt` (name, momentary max, length, peak): loudness ±1.5 LU, length ±2%, peak ±0.02. A name missing from the baseline fails. `--write-baseline` rewrites it | the review's `loud-effects.txt` |
| 3 | Families: `click`, `hover`, `error` ≤ -14 LUFS; `hover` ≤ `click` - 6; `summon_big` ≥ `summon` + 3; `damage_big` ≥ `damage` + 1.5; `phase` ≤ -22; `lp_tick` ≤ -30; `set` ≤ `place` + 1; `chain_resolve` ≤ `activate` - 6; `coin_spin` ≤ -18; `banish` within `destroy` ± 3; `draw_game` -18 .. -12 | summon_big -10.3 / summon -15.6; damage_big -12.9 / damage -15.2; hover -42.8 / click -30.4 |
| 4 | Every theme (19) renders; its seven layers are the same length; no seam jump (`SoundExport`'s rule); loop 20-60 s; everyday mix (bed + drums + lead) integrated -24.0 .. -19.5 LUFS; bed + drums -27.0 .. -22.5; every allowed combination's peak < 0.95; the menu loop integrated -22.5 .. -20.0 | 24.0-49.2 s; -23.2 .. -20.1 with lead; -26.3 .. -23.1 without; menu -21.2 |
| 5 | The boss list: every boss duel has a theme that isn't the everyday one (`SoundExport.Bosses`' rule) | **fails today** (Ilse, Vera) |
| 6 | Every `TunedRoots` name is in `Effects`, roots 0-11; `KeyRatio(4,4)` = 1, `(0,4)` = 2^(4/12), `(0,9)` = 2^(-3/12), `(0,6)` = 2^(-6/12); `ChainSteps` = 0, 2, 7, 12 | — |
| 7 | **Limiter**, at 44.1 kHz and 48 kHz, stereo: four pile-ups (the review's, effects at 1.0: the final blow, the ace landing, the battle, five destroys) each over the everyday theme's bed + drums + lead at 1.0 from bar 3. Before: the final blow ≥ 1.0. After: every output peak ≤ 0.90, `Clamped` 0, gain back ≥ 0.98 within 300 ms of the last input sample over 0.89. A -20 dBFS 1 kHz sine for 1 s comes out equal to the input delayed by L (±1e-5). The menu loop at 1.0: `MinGain` ≥ 0.999. 1,000 calls on 1,024-frame buffers allocate 0 bytes (`GC.GetAllocatedBytesForCurrentThread`) | final blow sums to about 1.73 + 0.45 |
| 8 | warn only (pass 2's M5 turns them into FAILs): the small-speaker table (each heavy sound's momentary max after a 150 Hz high-pass: `damage_big` should come out above `damage`) and the strongest alias of `dp`, `lp_tick`, `click` (Goertzel) | damage_big -19.0 vs damage -17.2; dp alias -25 dB |

The first baseline is written after Part E's new effects exist; its numbers for the old effects must match the review's
`build/qa-review-audio/loud-effects.txt` within 0.3 LU (the meter's check), and the builder says so in the report.

## The interface between the parts

Part E writes these signatures **first** (working bodies; the new effects may start as a short tone), so Part U
compiles against them from the start.

```csharp
// src/Ygo.Engine/Audio/Sfx.cs: Effects gains "coin_spin", "coin_land", "phase", "set", "banish", "chain_resolve",
// "draw_game" (section E). No other public change.

// src/Ygo.Engine/Audio/Sfx.Themes.cs: Themes gains "ilse" and "vera" (section G).

// src/Ygo.Engine/Audio/Sfx.Tuning.cs (new)
public static partial class Sfx
{
    /// <summary>The pitch class each tuned effect is built on (section I).</summary>
    public static readonly IReadOnlyDictionary<string, int> TunedRoots;
    /// <summary>Semitones per chain link, links 1-4 (later links use the last).</summary>
    public static readonly IReadOnlyList<int> ChainSteps;          // 0, 2, 7, 12
    /// <summary>The menu loop's key (A minor).</summary>
    public const int MenuKey = 9;
    /// <summary>The pitch ratio moving a sound built on <paramref name="from"/> to key <paramref name="to"/>, the
    /// nearest way (-6..+5 semitones).</summary>
    public static double KeyRatio(int from, int to);
}

// src/Ygo.Engine/Audio/Limiter.cs (new)
public sealed class Limiter
{
    public Limiter(int sampleRate, int channels, float threshold = 0.89f, float lookaheadMs = 2f, float releaseMs = 120f);
    public int LatencyFrames { get; }
    /// <summary>Limits interleaved samples in place. No allocation; safe on the audio thread.</summary>
    public void Process(float[] data, int channels);
    public void Process(float[] data, int offset, int count, int channels);
    public float InputPeak { get; }
    public float OutputPeak { get; }
    public float MinGain { get; }
    public int Clamped { get; }
    public long Frames { get; }
    public void ResetMeters();
}
```

Part U's public surface (for the checks and the later builds):

```csharp
// unity/Assets/Scripts/Sounds.cs
public enum SoundGroup { Music, Effects, Interface }
public readonly struct SoundLogEntry
{
    public readonly float Time; public readonly string Name; public readonly SoundGroup Group;
    public readonly float Volume; public readonly float Pitch; public readonly bool Played; public readonly string Why;
}
public static float MasterVolume { get; set; }
public static bool MuteInBackground { get; set; }
public static bool HoverSounds = true;                       // the owner's switch for hover sounds
public static bool OutputMuted;                               // -mute: the output is zeroed after the limiter's meters
public static void Play(string name, float volume = 1f, float pitch = 1f, bool ui = false);     // unchanged signature
public static void PlayLater(float delay, string name, float volume = 1f, float pitch = 1f, bool ui = false);
public static void PauseDip(bool on);
public static int MusicKey { get; }                           // -1 when nothing tonal plays
public static IReadOnlyList<SoundLogEntry> Log { get; }
public static void ClearLog();
public static void OverrideVolumes((float master, float music, float effects, float ui)? volumes);  // checks, memory only
public static IDictionary<string, float> MigratePrefs(IDictionary<string, float> prefs);           // pure, section A
public static void DebugFocus(bool focused);
public static float DebugPauseDip { get; }
public static (string track, int serial, int timeSamples) DebugTrack();
public static IReadOnlyList<(string clip, float pitch, bool playing)> DebugVoices();

// unity/Assets/Scripts/OutputLimiter.cs (new)
public sealed class OutputLimiter : MonoBehaviour
{
    public static (float inputPeak, float outputPeak, float minGain, int clamped) Meters { get; }
    public static float OutputGain { get; }
    public static void ResetMeters();
}

// unity/Assets/Scripts/SettingsSound.cs (new)
internal static class SettingsSound
{
    /// <summary>The sound rows (section A) from y down; returns the y after the last row.</summary>
    public static float Build(RectTransform rows, float y, Action refresh);
    public static string DebugSliderText(string group);       // "80%", "Off"; group: master, effects, music, interface
    public static void DebugStep(string group, int steps);    // as the < > buttons
}

// unity/Assets/Scripts/PackSounds.cs (new): the hooks the pack rebuild keeps calling (section H)
public static class PackSounds
{
    public static void Opened();
    public static void Dealt(int index);
    public static void Turned(Rarity rarity, bool hit);
    public static IReadOnlyList<(string name, float delay, float volume)> RevealSounds(Rarity rarity, bool hit);
    public static void SlabNumber(Rarity rarity, int slab);
    public static string? ChimeFor(Rarity rarity);
}

// unity/Assets/Scripts/BoardFx.cs (pure helpers)
internal static (string name, float volume) MoveSoundFor(CardRef from, CardRef to, uint reason);
internal static int ChainPitchStep(int link);
internal static IReadOnlyList<int> ResolveSteps(int links);
```

## Parts and files

Two builders at the same time; no file is shared. Only Part U runs Unity.

### Part E: engine and harness (general-purpose, Opus), about 2.5 agent-days

Owns, and touches nothing else:

- `src/Ygo.Engine/Audio/Sfx.cs` (the `Effects` dictionary only, lines 18-70)
- `src/Ygo.Engine/Audio/Sfx.Themes.cs` (two entries and a copy helper, near lines 64-229)
- new `src/Ygo.Engine/Audio/Sfx.Tuning.cs` and `src/Ygo.Engine/Audio/Limiter.cs`, each with a `.meta` file
  (`fileFormatVersion: 2` and a new random 32-hex `guid`, as `Sfx.Prize.cs.meta`)
- new `src/Ygo.Harness/AudioCheck.cs`, `src/Ygo.Harness/Loudness.cs`, `src/Ygo.Harness/fixtures/audio-baseline.txt`
- `src/Ygo.Harness/Program.cs`: one line, `case "--audio-check": return AudioCheck.Run(args.Skip(i + 1).ToArray());`
  beside `--boss-music` (line 94)
- `src/Ygo.Harness/SoundExport.cs`: only if `AudioCheck` needs `Bosses`' list as a function (make the list part a
  public static method; the command's output stays the same)

Nothing in `src/Ygo.Engine/Campaign/` (the climb's). Harness build: `build/bin/audio1`. Its tests: `--audio-check`
(rows 1-7 pass after the change; row 5 fails before it), `--boss-music` exits 0.

### Part U: Unity (Unity Architect, Opus), about 4.5 agent-days

Owns, and touches nothing else:

| File | What changes |
|---|---|
| `Sounds.cs` | Groups, migration, voices, jitter, log, `PlayLater`, pause dip, focus, tuning, dips, `OutputMuted` (most of the build) |
| new `OutputLimiter.cs` | The limiter component |
| new `SettingsSound.cs` | The sound block |
| new `PackSounds.cs` | The pack and grading hooks |
| new `DuelController.SoundCheck.cs` | `-soundcheck` |
| `MenuScreens.cs` | `SettingsScreen.Refresh` lines 602-610 → one call; `Next` (525) removed if unused |
| `UiHover.cs` | `OnPointerEnter` (114-118) and `OnSelect`: the hover sound |
| `BoardFx.cs` | `Sound` (64-68: pitch), `MoveCard` (297-306), `Summon` (609), `PhaseBanner` (1080-1090), `Spotlight` (1107), `ClearChain` (931-950), the three pure helpers; optionally `Prize` 1363-1364 |
| `BoardView.cs` | The LP roll loop, lines 2547-2549, and one field (`_lpTickNext`) |
| `CampaignScenes.cs` | `ShowCoinToss`'s `Toss()` coroutine only, lines 433-453 |
| `ShopScreen.cs` | `DealIn` (1012-1021) and `Reveal` (1054, 1084): calls to `PackSounds` |
| `GradingDeskScreen.cs` | Line 407: `PackSounds.SlabNumber` |
| `DuelController.cs` | Only these (see below) |

**`DuelController.cs`, exactly:**

| Lines (3b36438) | Method | Change |
|---|---|---|
| 582-583 | `Start` (the `-mute` line) | `Sounds.OutputMuted = true;` instead of `AudioListener.volume = 0f;` |
| after 713 | `Start` (check flags) | two lines: a comment and `if (Arg("-soundcheck") is string soundCheck) StartCoroutine(SoundCheck(soundCheck));` |
| 716 | `Start` (the `checks` array) | append `"-soundcheck"` at the end of the array |
| 2358-2364 | `UpdateMusic` | the pause rule (section C) |
| 5718 | `AnnounceResult` | `draw_game` for a draw, through a small static `ResultSound(int winner, bool youPlay)` placed right after `AnnounceResult` |

Line 716 is one long line every new check edits: if "The climb" adds a check too, the second one to land appends to
the merged line. Check build: `build/qa-audio1`. Unity: Part U only.

### After the build

Code Reviewer (Opus) on both diffs against this spec, ranked findings with `file:line`; one fix round to the same
agents; Evidence Collector (Opus) on a fresh build: reruns everything under Verification, opens the Settings shots at
both sizes, checks that each `-soundcheck` step and each `--audio-check` row fails with the change reverted (in a
scratch copy), and plays awkward inputs (Master 0 with Interface 100; dragging a slider past its ends; focus lost
mid-pile-up; a 6-link chain; a pack of nine Secret Rares). Technical Writer (Sonnet) for section L.

## Traps

- **`OnAudioFilterRead` runs on the audio thread:** no Unity API, no allocation, no locks, no `Debug.Log` inside it.
  Read settings into plain fields on the main thread; meters are plain floats written there and read on the main thread
  (a torn read is harmless). Reset meters with a flag the audio thread acts on.
- **The filter must sit on the `AudioListener`'s GameObject** (`Sounds.Create`, which adds the listener when the scene
  has none). If another listener ever exists, Unity warns and the filter may not see the mix: the check's step 14 is the
  proof.
- **`-mute` used to stop all output at the listener.** Anything else that reads `AudioListener.volume` (grep for it:
  `Sounds.cs:67`, `Sounds.cs:471`) must read `Sounds.OutputMuted`. Muted runs now really mix audio: fine on a Mac, but
  an app check with no audio device would see empty meters (none of ours run that way).
- **The Slider's `OnMove` and arrow navigation:** Left/Right on a focused Slider change its value; the screen's
  `ArrowNav` must not also move the focus sideways. Up/Down move between rows. Check it with the controller test.
- **Rebuilding rows mid-drag kills the drag:** sliders never call `refresh`; only the On/Off buttons do, as today.
- **Settings scroll positions in other checks** (`-menucheck`'s `DebugScroll(0.68f)` for the Duel Presentation rows)
  were measured on today's layout; rows below the block move 208 px. Adjust the fractions if a shot misses its rows.
- **`PlayLater` uses unscaled time** in `Sounds.Update`; a pause (time scale) must not hold it; leaving a screen doesn't
  cancel it (sounds are short).
- **Pitch changes length:** the tuned `win` at +5 semitones is 25% shorter; the duel's end timing doesn't depend on it.
- **The repeat fade** keys on the name: `lp_tick` from both plates at once collides at 50 ms; that's fine.
- **The `Important` dip and the new soft dip** share `_duck`: take the lower level and the later end, as the prize
  branch does now (`Sounds.cs:138`).
- **The menu check counts clips** (`DuelController.cs:4481`: `Sfx.Effects.Count + Sfx.Music.Count + ShowSounds.Count`):
  the new effects change the count by themselves; don't hard-code a number.
- **`Theme` copies:** a shallow copy of a `Theme` with a new `Name` and `Key` is enough (strings are immutable); the
  dictionary is built from an array (`Sfx.Themes.cs:64-229`), so add the copies when building it, not after.
- **Unity imports `src/Ygo.Engine` as a package:** a new `.cs` there without a `.meta` gets one generated by whoever opens
  Unity, which then shows as an untracked change; Part E writes them.
- **Prefs:** check builds write their own preferences domain (`DuelController.CheckPrefs.cs`); still restore the values
  a step changed, so later steps start from the defaults.
- No commit, stash, reset or branch switch; LF endings; no card pictures; no audio files of any kind.

## Verification

Harness, from `build/bin/audio1`:

```bash
dotnet build src/Ygo.Harness -c Release -o build/bin/audio1
H=build/bin/audio1/Ygo.Harness.dll
dotnet $H --audio-check && dotnet $H --boss-music && dotnet $H --replay-test && dotnet $H --games 3
dotnet $H --campaign-test && dotnet $H --texts-check
# the meter against ffmpeg, once:
dotnet $H --export-sounds build/qa-audio1/sounds
ffmpeg -hide_banner -i build/qa-audio1/sounds/summon.wav -af "apad=pad_dur=0.5,ebur128=framelog=info" -f null - 2>&1 | grep -o "M: *-\?[0-9.]*" | sort -k2 -g | tail -1
ffmpeg -hide_banner -i build/qa-audio1/sounds/menu.wav -af ebur128 -f null - 2>&1 | grep -A1 "Integrated loudness"
```

App, from `build/qa-audio1` (a check build: its own bundle id), one at a time, fresh absolute `-mods` each, at 1920x1080
and 1280x720: `-soundcheck` (with `-mute`), `-musiccheck` (with `-mute`), `-menucheck` (**without** `-mute`),
`-replaycheck`, `-shopcheck`, `-campaigncheck`, `-layoutcheck`, `-controlscheck` and the controller test (Settings
sliders), `-clickcheck`. Every screenshot looked at, Settings at both sizes especially. Nothing here touches DP, decks or
ban lists, so `--arbitrage` and `check_history_decks.py` aren't needed.

## Listening pass (for the owner, before it ships)

From the review's checklist, the items this build changes, plus its new sounds (about 10 minutes):

1. **Settings:** drag each slider, step it down with `<` and the Left key; Sound effects at Off with Interface at 100%:
   clicks now play. Master at 50% halves everything.
2. **Hover:** sweep the pointer across the main menu's buttons. Pleasant or chattery? (`Sounds.HoverSounds` turns it off.)
3. **Coin toss:** the spin and the landing before the win/lose chime.
4. **Phases and LP:** the soft phase tick; the tick as LP roll down and up. Too busy at speed 4?
5. **A 3-link chain** (the chain lesson): does it climb and then tick back down?
6. **Set, banish, send to the GY, destroy:** four different sounds now.
7. **Lose on the everyday theme, then win on a boss theme** (the Beginner Circuit's last match): do `lose` and `win`
   sit in the music's key now?
8. **Pause for 3 s mid-duel and resume:** the duel theme dips and carries on.
9. **Final blow at Master, Effects and Music 100%:** no crackle (the limiter) and no audible pumping after it.
10. **Packs:** an Ultra, a Secret, a Ghost or Starlight if you have one: three different reveals. The dealing ticks.
11. **Switch to another app:** the sound fades out, and back when you return.
12. **Ilse and Vera:** their stings and themes (Nocturne and Rhea in a new key: good enough until their own?).
13. **A draw game** if you can reach one (`-autoshot` decks that stall): the new chord.

## Owner decisions (recommendation first)

Decided (owner, 2026-11-20): the audio review's eight, as recommended.

1. **More music:** synthesized themes in a later pass 2 (M1, M2); none in this build. Decided (owner, 2026-11-20).
2. **Pausing a duel:** the duel theme carries on, dipped to 50%. Decided (owner, 2026-11-20).
3. **Mute in the background:** on by default, with a Settings row. Decided (owner, 2026-11-20).
4. **Volume controls:** the mixer with Master and real sliders. Decided (owner, 2026-11-20). Built as a code-only mix
   with a limiter, not an `AudioMixer` asset (section B: Unity can't create one from a script).
5. **Ilse's and Vera's music:** `nocturne` +3 for Ilse and `rhea` -2 for Vera, for now. Decided (owner, 2026-11-20).
6. **Tune effects to the music:** the tonal ones (section I's list). Decided (owner, 2026-11-20).
7. **Character text blips:** later. Decided (owner, 2026-11-20).
8. **What to build first:** this spec, Q1-Q10 plus M3 and M6. Decided (owner, 2026-11-20).

Chosen while writing the spec (built as stated unless the owner says otherwise):

9. **Ilse's ladder seat** plays her new theme too. *Recommended:* yes; guests carry their themes onto the ladder by
   design (`LadderMusic` looks up the id first). *Or:* keep her ladder seat on today's music (a change in
   `DuelController.Ladder.cs`, the climb's neighbourhood).
10. **Hover sound:** on, at half the interface volume, buttons only. *Or:* off (`Sounds.HoverSounds = false`, one line).
11. **Interface default and migration:** default 65%, and a player's saved interface volume becomes what they heard
    (interface × effects), once. *Or:* keep 80% (clicks about 2 dB louder for everyone than today) and no migration.
12. **Chain steps:** root, 2nd, 5th, octave, and the resolve ticks only for chains of two links or more. *Or:* a plain
    rising scale (1, 2, 3, 4, 5), which can clash with minor music on the third.
13. **`-mute` zeroes the output after the limiter** (checks can measure levels muted). *Or:* keep `-mute` at the listener
    and run the limiter step unmuted (one more unmuted app check, which plays sound out loud).
14. **Sending to the GY** keeps `destroy` at a lighter 0.6 rather than a new sound. *Or:* its own clip in pass 2's M5.

## As built (2026-11-21)

Built as specified (sections A to K and M), then one fix round after an independent code review, with the deviations
below. Code: engine and harness: `Audio/Limiter.cs`, `Audio/Sfx.Tuning.cs` (`TunedRoots`, `ChainSteps`, key ratios),
`Audio/Sfx.cs` (seven new effects: `coin_spin`, `coin_land`, `phase`, `set`, `banish`, `chain_resolve`, `draw_game`),
`Audio/Sfx.Themes.cs` (`Transposed`: Ilse and Vera), `AudioCheck.cs`, `Loudness.cs`, `fixtures/audio-baseline.txt`,
`SoundExport.cs` (`BossList`, `BossListOk`); Unity: `Sounds.cs` (groups, `MigratePrefs`, voices, `PauseDip`, the sound
log), `SettingsSound.cs`, `OutputLimiter.cs`, `PackSounds.cs`, `BoardFx.cs` (`MoveSoundFor`, chain pitch, `ClearChain`),
`UiHover.cs`, `DuelController.cs` (`UpdateMusic`), `DuelController.SoundCheck.cs`. Models: spec by a Game Audio Engineer
(Opus), engine by a `general-purpose` agent (Opus), Unity by a Unity Architect (Opus), review by a Code Reviewer (Opus),
docs by a Technical Writer (Sonnet).

Deviations:

1. **The limiter's recovery band is 350 ms.** The release is 120 ms, which can't meet the spec's 300 ms; row 7 allows 350.
2. **A faster attack** (eight time constants over the 2 ms lookahead), so input peaks up to 16 come out at 0.90 or less.
3. **Ilse's and Vera's themes are transposed copies** (`Transposed`: Nocturne +3, Rhea -2), not new tunes. Ilse's ladder
   seat plays hers (decision 9).
4. **A narrower pause rule.** The duel theme carries on at half volume only for Settings opened from the duel's gear, or
   the menu or Settings over a live campaign duel. A free duel under the main menu plays the menu music.
5. **Check runs ignore real window focus** (`_logging`, automated runs only), so a check never fades out. A manual dev
   launch still fades in the background; nothing tested that.
6. **The listener and the limiter sit on a child object** (`Listener`), because a filter on an object that also holds
   AudioSources is taken by them, not the listener.
7. **`banish` plays for any banish** that isn't a destroy. A card sent to the Graveyard keeps `destroy` at 0.6 (decision 14).
8. **Voice stealing spares prize chimes.** The pool is 16 voices, one sound each.
9. **The music restarts after an output-device change** (`AudioConfigurationChanged`): Unity stops every source.
10. **The resolve-tick count uses the highest link activated**, not the badges left, because a resolved Spell takes its
    badge with it.
11. **`-mute` zeroes the output after the limiter** (decision 13), so checks read real levels.

Checks: harness `--audio-check` (rows 1 to 8; row 8 only warns, and its warnings are audio pass 2's targets; run with
`--write-baseline` after a deliberate change to a sound) and `--boss-music` (exits 0 now: its list is shared with row 5);
app `-soundcheck` (51 steps) and `-musiccheck` (20 steps). The Evidence Collector's report is
[EVIDENCE-2026-11-21.md](EVIDENCE-2026-11-21.md).

Open: **nothing has been heard.** Every check runs muted and proves the sounds from the sound log, the limiter's meters
and the music's state. The owner's listening pass (the "Listening pass" list above) is the open item. Also not verified:
a real output-device switch, manual dev launches fading out in the background, and Windows. Audio pass 2 (more music, the
row-8 warnings) and the character text blips are later.

Evidence (2026-11-21): the Evidence Collector tested fresh builds ([EVIDENCE-2026-11-21.md](EVIDENCE-2026-11-21.md)).
Harness: `--audio-check` exits 0 (44 ok, 4 warns, all row 8); `--boss-music` exits 0 (38 boss duels, 35 theme keys); the
final blow's limiter line is `in 1.70 out 0.890, clamped 0`, with the gain back 314 ms after the last input (317 ms at
44.1 kHz). App: all 25 planned runs have 0 FAIL and no exception in any log, with `-soundcheck` at 51 steps. Old audio
preferences, run in the check's own domain, came out as `audioPrefs 2`, `uiVolume 0.25`, `musicVolume 0.33`, `muteInBackground 1`.
Its small findings and weak steps (its sections 5 and 6) are in `TODO.md`. Still unheard: its section 8 repeats the
listening list.
