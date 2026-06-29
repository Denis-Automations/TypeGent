# TypeGent

A human-like auto-typer for Windows. It types text into the focused window character-by-character with realistic timing (and optional typos + corrections) using OS-level keyboard input, rather than pasting it instantly.

- **Stack:** C# / .NET 10, WPF (MVVM via CommunityToolkit.Mvvm), `SendInput` via InputSimulatorPlus.
- **Target:** Windows 10 1809+ / Windows 11.
- **Status:** Pre-code planning. Phase 1 (environment prep) complete.

## Documents

| File | Purpose |
|---|---|
| [`plan.md`](plan.md) | Phased build plan with per-phase success criteria and self-checks. |
| [`TypeGent – Findings, Architecture & Tech Stack.md`](TypeGent%20%E2%80%93%20Findings%2C%20Architecture%20%26%20Tech%20Stack.md) | Committed architecture and tech-stack decisions. |
| [`TypeGent – Approach Review.md`](TypeGent%20%E2%80%93%20Approach%20Review.md) | Independent review that drove the current decisions. |
| [`Human-Like Windows Auto Typer – Research & Design Notes.md`](Human-Like%20Windows%20Auto%20Typer%20%E2%80%93%20Research%20%26%20Design%20Notes.md) | Original research log (kept for provenance). |

## Build (once code exists)

```powershell
dotnet build
dotnet test
dotnet run --project src\TypeGent.App
```

Only the **.NET 10 SDK** is required to build and run; Visual Studio is optional.
