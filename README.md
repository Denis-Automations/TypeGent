<p align="center">
  <img src="assets/logo.png" alt="TypeGent logo" width="220" />
</p>

<h1 align="center">TypeGent</h1>

<p align="center">A human-like auto-typer for Windows.</p>

---

TypeGent types text into whatever window you have focused, character-by-character, using OS-level
keyboard input (`SendInput`) — with realistic human timing and occasional self-correcting typos —
instead of pasting it instantly. The result reads like a real person at the keyboard, not a paste.

- **Stack:** C# / .NET 10, WPF (MVVM via CommunityToolkit.Mvvm), `SendInput` via InputSimulatorPlus.
- **Target OS:** Windows 10 1809+ / Windows 11 (x64).
- **Status:** 🚧 **v2 in progress.** The v2 engine is implemented and unit-verified (222 tests
  green); human-like keystroke realism (dwell, rollover, misspellings), cognitive errors,
  personas, and multi-profile support are live in the app. Ships as a single self-contained `.exe`
  (no prerequisites on the target machine).

## Features

- **Human-like timing** — inter-keystroke delays are drawn from a log-normal distribution around
  your target WPM, with pauses at word/punctuation/sentence boundaries, corpus-driven bigram
  familiarity timing, biomechanical hand/finger/distance modifiers, a warm-up ramp, autocorrelated
  pace drift, gentle fatigue over long passages, and occasional attention lapses. It never types
  like a metronome.
- **Self-correcting typos** — an optional, tunable typo rate introduces realistic mechanical slips
  (adjacent-key hits, transpositions, doubled letters, mistimed shifts, omissions, missed doubles)
  that are caught via **delayed detection** (a few keystrokes pass before the error is noticed and
  backspaced) or immediate correction. The net typed text always equals your input, exactly.
- **Cognitive misspellings** — besides mechanical typos, learned dictionary misspellings (e.g.
  "recieve", "seperate") are typed and then corrected, either by human backspacing or by a simulated
  autocorrect bulk-replace — modeling real-word spelling habits, not just key slips.
- **Keystroke realism — dwell & rollover** — each keystroke is delivered as a separate key-down /
  key-up pair with a near-Gaussian key-hold (dwell) duration, and fast touch-typist personas overlap
  keys (the next key goes down before the previous is released — genuine negative flight), as seen in
  real biometric typing.
- **Personas & saved profiles** — one-click persona presets (hunt & peck, average typist, fast
  touch-typist, mobile/autocorrect) bundle all the realism knobs into coherent characters; save,
  name, and switch multiple profiles, and cycle them with a global hotkey (Ctrl+Shift+P).
- **US QWERTY + Unicode fallback** — common characters are injected as genuine virtual-key + Shift
  chords (indistinguishable from physical keystrokes); anything outside the layout (accents, em
  dashes, emoji) falls back to Unicode `VK_PACKET` entry so arbitrary text still comes out correct.
- **System-wide hotkey** — trigger typing into any focused app with a configurable global hotkey.
- **Safety guards** — refuses to type into its own window, warns if focus drifts off the target
  mid-run, and detects elevated (admin) targets that a non-elevated process can't reach.
- **Live UI** — text box with live character count and an `≈` time estimate; sliders for speed,
  jitter and typo rate; fatigue and keystroke-realism toggles; persona, profile, layout and hotkey
  dropdowns; Start/Stop (both **Stop** and **Escape** cancel within one keystroke).
- **Settings persistence** — your saved profiles and window settings are stored in
  `%AppData%\TypeGent\settings.json` and restored on the next launch (typed text is never persisted).

## Requirements

- **Build:** the [**.NET 10 SDK**](https://dotnet.microsoft.com/download/dotnet/10.0) is the only
  requirement. Visual Studio is optional (VS Code + the `dotnet` CLI is sufficient).
- **Run the published `.exe`:** nothing — the binary is self-contained and bundles the .NET runtime.

## Project layout

```
src/
  TypeGent.Core/    Timing + error models, layout abstraction, typing orchestrator (no UI, no P/Invoke)
  TypeGent.Native/  IKeyboardBackend implementation over InputSimulatorPlus (SendInput)
  TypeGent.App/     WPF app: MVVM view model, XAML UI, hotkey, elevation/focus guards, settings
tests/
  TypeGent.Tests/   xUnit suite (delay/error models, layout, orchestrator, engine)
tools/
  publish.ps1       One-command single-file publish
  Generate-Icon.ps1 Icon generation helper
assets/
  logo.png          App logo (used in this README)
```

The App talks to the keyboard only through the `IKeyboardBackend` abstraction in Core, so the
`SendInput` dependency is isolated in `TypeGent.Native` and swappable.

## Build & run (development)

```powershell
dotnet build                          # compile all projects (0 warnings, 0 errors)
dotnet test                           # run the unit test suite (222 tests, all green)
dotnet run --project src/TypeGent.App # launch the WPF window
```

## Build the distributable `.exe`

Produce a single-file, self-contained `win-x64` executable:

```powershell
powershell -ExecutionPolicy Bypass -File tools\publish.ps1
```

This runs `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` and
writes the result to `publish\TypeGent.App.exe`.

- **Output:** a single `publish\TypeGent.App.exe`, roughly **62 MB**, with **no sidecar files** —
  the .NET runtime, native WPF libraries, and the app icon are all bundled into the one executable,
  and `.pdb` debug symbols are excluded from the Release publish.
- **Size note:** WPF does not support Native AOT and trims poorly, so a self-contained WPF binary is
  expected to land in the ~60–150 MB range. If size matters more than portability, drop
  `--self-contained` for a framework-dependent build (requires the .NET 10 Desktop Runtime on the
  target machine).

Copy `publish\TypeGent.App.exe` to any Windows 10 1809+ / Windows 11 x64 machine and run it — no
install step required.

## Usage

1. Launch TypeGent (run the `.exe`, or `dotnet run --project src/TypeGent.App`).
2. Type or paste your text into the text box and set the profile (speed, jitter, typo rate, etc.).
3. Trigger typing one of two ways:
   - **Start button** — begins a short 3-2-1 countdown; click into your target window before it ends.
   - **Global hotkey** — focus your target window and press the configured hotkey; typing starts
     immediately.
4. Watch the text appear in the target app with human-like pacing and the occasional self-corrected
   typo. Press **Stop** or **Escape** to halt within one keystroke.

**Notes & limits:**

- TypeGent won't type into its own window — click into the target app first.
- To type into an app running **as administrator**, run TypeGent as administrator too (Windows
  blocks input from a lower-privilege process to a higher one).
- Games that read raw scancodes (DirectInput/raw input) may ignore Unicode-fallback characters;
  game compatibility is a non-goal.

## Testing

```powershell
dotnet test                                     # 222 tests
dotnet test --collect:"XPlat Code Coverage"     # with line coverage on TypeGent.Core
```

The suite covers the log-normal delay model (median/shape/clamping, warm-up, pace, lapses), the
dwell/flight/rollover keystroke-biometric layer, the error model (typo-kind selection, adjacency,
speed coupling, cognitive misspellings, autocorrect), the US QWERTY layout (representative
characters incl. the real-comma rule and Unicode fallback routing), orchestrator dispatch order and
cancellation, persona presets, and end-to-end engine planning — including the guarantee that
reconstructed net text always equals the input across many seeds and typo rates.

## Roadmap

v2 is in progress. **Shipped in v2:** human-like keystroke realism (dwell, rollover, negative
flight), cognitive misspellings + autocorrect, delayed and immediate typo correction, corpus-driven
bigram timing, biomechanical timing modifiers, pre-word planning pauses, attention lapses, and
persona presets with multiple saved profiles (cycle hotkey included).

**Deferred to the next version:** additional keyboard layouts (UK QWERTY, AZERTY, Dvorak, Colemak),
auto-detecting the target's layout, IME/CJK support, a tunable delay-curve editor, and opt-in
telemetry. The phased v2 build plan lives in `planv2.md` and the remediation/completion plan in
[`planv2-remediation-and-completion.md`](planv2-remediation-and-completion.md); the four invariants
every v2 phase must preserve are documented in
[`docs/v2-invariants.md`](docs/v2-invariants.md) (single injected RNG, net text == input, US QWERTY
default, controlled-target verification).
