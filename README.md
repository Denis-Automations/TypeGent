# TypeGent

A human-like auto-typer for Windows. It types text into the focused window character-by-character with realistic timing (and optional typos + corrections) using OS-level keyboard input, rather than pasting it instantly.

- **Stack:** C# / .NET 10, WPF (MVVM via CommunityToolkit.Mvvm), `SendInput` via InputSimulatorPlus.
- **Target:** Windows 10 1809+ / Windows 11.
- **Status:** Phase 3 complete. Types real keystrokes (US QWERTY + Unicode fallback) into the focused window via a temporary debug button. No human-timing model or real UI yet — those are Phases 4–5.

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

## Test it yourself (Phase 3)

Right now the app is a **debug harness**, not the finished tool: one text box, a **Type test**
button, and a status line. Clicking the button counts down 3 seconds (so you can switch focus to
your target), then types the text box contents into **whatever window is in the foreground** using
real OS keystrokes.

### Manual smoke test — type into Notepad

1. Open **Notepad** (`notepad.exe`) and leave it open.
2. From the project folder, launch TypeGent:
   ```powershell
   dotnet run --project src\TypeGent.App
   ```
3. The text box is pre-filled with `Hello, World! — café`. Leave it or edit it.
4. Click **Type test**. The status shows a countdown: *"Switch to your target window… typing in 3 / 2 / 1"*.
5. **During the countdown, click into the Notepad window** so it has focus.
6. Watch the text appear in Notepad. The status returns to **Done.**

**What to verify:**

- The text appears in **Notepad**, not back in TypeGent's text box. (If it lands in TypeGent, you
  didn't switch focus in time — try again.)
- Capitals are correct: `H` and `W` are upper-case (typed as Shift chords).
- The comma is a **real comma `,`** — *not* `<`. This is the key correctness check for the layout.
- The out-of-layout characters **`—` (em dash)** and **`é`** appear correctly — these route through
  the Unicode (`VK_PACKET`) fallback rather than the physical-key path.
- Try your own text in the box (punctuation, digits, symbols like `! @ # [ ] ; / ?`) and confirm it
  all comes out verbatim.

### Things that won't work yet (by design)

- **Timing is flat** (~40 ms/char). Human-like varying pace + typos arrive in **Phase 4**.
- **No global hotkey / real UI** — you must use the debug button. Those land in **Phases 5–6**.
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
