# TypeGent

A human-like auto-typer for Windows. It types text into the focused window character-by-character with realistic timing (and optional typos + corrections) using OS-level keyboard input, rather than pasting it instantly.

- **Stack:** C# / .NET 10, WPF (MVVM via CommunityToolkit.Mvvm), `SendInput` via InputSimulatorPlus.
- **Target:** Windows 10 1809+ / Windows 11.
- **Status:** Phase 5 complete. Full WPF UI — text box, **WPM / jitter / typo-rate sliders, fatigue + always-on-top toggles, layout/hotkey dropdowns**, a live character count and `≈` time estimate, and **Start / Stop** (Stop and `Escape` cancel cleanly). Types real keystrokes (US QWERTY + Unicode fallback) into the focused window with **human-like timing and self-correcting typos**. The system-wide global hotkey + elevated-target handling are Phase 6.

## Documents

| File | Purpose |
|---|---|
| [`plan.md`](plan.md) | Phased build plan with per-phase success criteria and self-checks. |
| [`TypeGent – Findings, Architecture & Tech Stack.md`](TypeGent%20%E2%80%93%20Findings%2C%20Architecture%20%26%20Tech%20Stack.md) | Committed architecture and tech-stack decisions. |
| [`TypeGent – Approach Review.md`](TypeGent%20%E2%80%93%20Approach%20Review.md) | Independent review that drove the current decisions. |
| [`Human-Like Windows Auto Typer – Research & Design Notes.md`](Human-Like%20Windows%20Auto%20Typer%20%E2%80%93%20Research%20%26%20Design%20Notes.md) | Original research log (kept for provenance). |

## Build & run

```powershell
dotnet build                          # compile all projects (should be 0 warnings, 0 errors)
dotnet test                           # run the unit test suite (should be all green)
dotnet run --project src\TypeGent.App # launch the WPF window
```

Only the **.NET 10 SDK** is required to build and run; Visual Studio is optional.

## Test it yourself (Phase 5)

The app now has its **real UI**: a text box, sliders for **Speed (WPM)**, **Jitter (σ)** and
**Typo rate**, **Fatigue** and **Keep window on top** toggles, **Keyboard layout** + **Hotkey**
dropdowns, a live **character count** and **≈ estimated time**, and **Start / Stop** buttons. The
character count and time estimate update as you type or drag the WPM slider.

Because the system-wide hotkey is still Phase 6, **Start** uses a 3-second countdown so you can
focus your target window first, then types the text box contents into **whatever window is in the
foreground** using the human engine with the profile you set. The final text always matches the input.

### Manual smoke test — type into Notepad

1. Open **Notepad** (`notepad.exe`) and leave it open.
2. From the project folder, launch TypeGent:
   ```powershell
   dotnet run --project src\TypeGent.App
   ```
3. Type or paste some text. Watch **Characters** and **≈ estimated time** update live; drag **Speed (WPM)** and the estimate changes immediately.
4. Set the sliders to taste (e.g. raise **Typo rate** to see more corrections).
5. Click **Start**. The status shows a countdown: *"Switch to your target window… typing in 3 / 2 / 1"*.
6. **During the countdown, click into the Notepad window** so it has focus.
7. Watch the text appear in Notepad. The status returns to **Done.**

**What to verify:**

- The text appears in **Notepad**, not back in TypeGent's text box. (If you *don't* switch focus,
  TypeGent detects its own window is in front and shows *"Click into the target app first…"* instead
  of typing into itself — no crash.)
- **Stop** (or pressing **Escape** while TypeGent is focused) halts typing within one keystroke.
- **The pace varies like a person** — slightly slower after spaces, faster on common letter pairs,
  not a metronome.
- **Occasional typos that fix themselves** — you'll see a wrong key (or a
  swapped/doubled/wrongly-capitalized letter) appear, get backspaced, and corrected. **The final
  text always matches the input** — try a long paragraph and compare.
- Capitals, a real comma `,` (not `<`), and out-of-layout characters like `—`/`é` (Unicode fallback)
  all still come out correctly.

### Things that won't work yet (by design)

- **No global hotkey** — Start uses a countdown button; the system-wide hotkey (and a true global
  `Escape`) land in **Phase 6**. The hotkey dropdown is bound but not yet registered.
- **Other keyboard layouts** — the dropdown lists **US QWERTY** only; UK/AZERTY/Dvorak/Colemak are v2.
- **Elevated targets:** a normal (non-admin) TypeGent can't type into an app running **as
  Administrator** (e.g. an elevated Notepad). That's a Windows security boundary; explicit handling
  comes in **Phase 6**.
- **Games** that read raw scancodes (DirectInput/raw input) may ignore the Unicode-fallback
  characters. Game compatibility is a non-goal.

### Automated tests

```powershell
dotnet test
```

Covers the orchestrator dispatch order, the US QWERTY mapping for representative characters
(`a Z ! 5 [ ; ,`), the real-comma rule, and out-of-layout characters routing to the Unicode fallback.
