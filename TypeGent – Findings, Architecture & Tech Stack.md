# TypeGent – Findings, Architecture & Tech Stack

> Companion document to `Human-Like Windows Auto Typer – Research & Design Notes.md`.
> This file goes one step further: it critically evaluates the references, validates them by reading the actual repos and Microsoft docs, and commits to a single concrete architecture and tech stack for v1.
>
> **Revised 2026-06-29 per `TypeGent – Approach Review.md`:** runtime moved to **.NET 10 (LTS)**;
> dropped the incorrect "native AOT-ready" claim (WPF can't AOT); v1 ships **US QWERTY only** with a
> **Unicode `VK_PACKET` fallback** (other layouts → v2); timing/error models take a **seedable RNG**;
> log-normal delay is treated as a **median**; `InputSimulatorPlus` license reconciled.

---

## 1. Executive Summary

**Verdict:** Build a single-process **C# / .NET 10 WPF** desktop app for Windows, using **InputSimulatorPlus** as the keyboard backend (SendInput wrapper), a custom **HumanTypingEngine** that combines a log-normal inter-key delay model with a Markov-style adjacency-error model (ported from `Lax3n/HumanTyping`'s design), **US QWERTY virtual-key mapping with a Unicode `VK_PACKET` fallback** for modifier handling, and **`RegisterHotKey` P/Invoke** for a global hotkey to trigger typing into whatever window has focus.

**Why this stack:**
- **InputSimulatorPlus** beats every alternative: it is a tiny, permissively-licensed C# wrapper over `SendInput` with scan-code support, which is exactly what is needed for `Shift+1 → !` to land in the OS input stream the same way a real key would. (It's the only third-party runtime dependency and was last updated in 2022 — owning the ~100-line `SendInput` P/Invoke directly in `TypeGent.Native` is a viable fallback; see §4.)
- **WPF + .NET 10** is the lowest-risk, most-supported choice for a Windows-only utility, and .NET 10 is the current LTS (supported to Nov 2028). MVVM via `CommunityToolkit.Mvvm` keeps the code maintainable. WinForms is older and clunkier; WinUI 3 adds packaging complexity we don't need; Avalonia/MAUI buy cross-platform we explicitly don't want.
- **SendInput** is the *only* reliable API for this on modern Windows. `keybd_event` is legacy, `SendKeys` is text-only (no real keys), and Python solutions (`pyautogui`, `keyboard`) require admin rights on Windows and are inherently slower and less precise.

---

## 2. Critical Evaluation of Each Reference

I read every meaningful URL from the original research. Below is what each is actually good for, and what to ignore.

### 2.1 Keyboard simulation backends

| Project | Stars | License | Usefulness for TypeGent |
|---|---|---|---|
| **kmcnaught/InputSimulatorPlus** | 1 (fork) | MS-PL (permissive — verify `LICENSE.md`) | **Use as-is.** It's a NuGet package (`Install-Package InputSimulatorPlus`) wrapping `SendInput`, with scan-code support. Solves modifier chords cleanly via `SimulateModifiedKeyStroke(modifier, vk)`. Last published 2022; permissive either way (MS-PL/MIT) and fine for our use, but **confirm the exact license in the repo's `LICENSE.md` before distributing**, and note this is the project's only third-party runtime dep (see §4 for the "own the P/Invoke" option). |
| **michaelnoonan/inputsimulator** | 818 | MIT | Reference only — InputSimulatorPlus is its maintained successor with scan-code support, which fixes a class of compatibility issues. |
| **raoyutian/Win32** | 8 | Unlicense | Skip. It's a general-purpose Win32 dump (mouse hooks, system info, Shell APIs). Adds 50+ APIs we don't need. |
| **antelle/keyboard-auto-type** | 20 | MIT | Skip. It's C++17 with CMake. Excellent for cross-platform but useless for a Windows-only C# app — we'd have to ship two runtimes. |
| **myfreeer/sendinput, HydraLM81/sendInput** | low | varies | Skip. Lower-level than we need. InputSimulatorPlus already does the right thing. |
| **Microsoft SendInput docs** | n/a | n/a | **Read this.** Defines `INPUT`, `KEYBDINPUT`, `KEYEVENTF_*` flags. Knowing the underlying call informs how InputSimulatorPlus works and how to handle edge cases like `KEYEVENTF_SCANCODE` for IME-resistant keys. |

**Key technical facts from Microsoft Learn (SendInput):**
- `SendInput` is subject to **UIPI** (User Interface Privilege Isolation). A non-elevated process cannot inject input into a higher-integrity process (e.g., an elevated Notepad). This is a hard OS-level limit, not a library bug.
- Existing keys held by the user can interfere — `GetAsyncKeyState` should be checked before sending chords.
- `SendInput` does **not** reset current keyboard state.
- Events from `SendInput` are serialized into the same stream as user-typed events and are not interspersed, so the target app sees a clean keystroke sequence.

**Decision:** Use **InputSimulatorPlus** (NuGet) and accept the UIPI limitation as a documented constraint in v1.

### 2.2 Human typing behavior models

| Project | Stars | License | What to steal |
|---|---|---|---|
| **Lax3n/HumanTyping** | 72 | MIT | **Heavy inspiration.** The most complete open model. Worth porting the `config.py` parameters and the Markov-state ideas. |
| **UnMars/human_typer** | 7 | MIT | Simple CPM-driven model. Good baseline idea (`average_cpm` as primary speed knob) but no realism beyond that. |
| **FoeXploit/human-typing-simulator** | 3 | MIT | Skip. Trivial — random typos from a symbol set, no real model. Useful as a sanity check but no real ideas. |
| **Pranet-Godavarty/Human-mimic-Auto-Typer** | 1 | Apache-2.0 | **Avoid.** README explicitly states "Absolute System Lockout — All user inputs are blocked/overridden." That's an anti-feature: the user must be able to stop the typer at any time. |
| **ApexXP/Typing-Simulator** | 1 | MIT | UI inspiration only. Tkinter + CustomTkinter. Confirms the minimal feature set: text box, hotkey, speed, dark/light mode. |
| **maxmmmmmmmmmma/AutoTyper-Pro** | 5 | MIT | UI inspiration. CustomTkinter with sliders for typo rate, min/max delay, bias. Packaged as `.exe` — confirms the distribution path is realistic. |
| **jarekj9/paste** | low | varies | Skip. Just types clipboard contents. |
| **AdritoPramanik/TypingBot** | low | varies | Skip. PyAutoGUI wrapper, no model. |

**The Lax3n/HumanTyping model in detail (worth porting the spirit of):**
- **Speed variation**: common words 40% faster, complex words 30% slower. Big-frequency weighted.
- **Bigram acceleration**: frequent digraphs (`th`, `er`, `in`, `re`) typed in rapid bursts.
- **Fatigue**: per-character slowdown factor (e.g., 0.05% per character), cumulative.
- **Natural pauses**: ~250ms at word boundaries, longer at sentences.
- **Error types**: neighbor-key errors (QWERTY adjacency matrix), swap errors (`teh` → `the`), with a probability of late correction (arrow keys to navigate and backspace).
- **State machine**: separates "mental cursor" (where the user thinks they are) from "physical cursor" (actual position) to model proofreading.

**Decision:** Port the *parameters and ideas*, not the Python code. Use a log-normal distribution for inter-key delays (which is what real keystroke timing follows per published keystroke dynamics research) plus digraph/fatigue adjustments. Implement neighbor-key errors via a QWERTY adjacency table; skip swap errors and arrow-key late corrections in v1 to keep scope tight.

### 2.3 UI framework choice (2025 reality)

Sources: Claudio Bernasconi's framework comparison, Avalonia blog, Reddit, Microsoft Learn.

| Framework | Verdict for TypeGent |
|---|---|
| **WinForms** | Skip for new code in 2025. Still works but: designer breaks on complex dialogs, code-behind patterns are messy, customization is limited. Good for internal tools, not for a polished utility. |
| **WPF + .NET 10** | **Pick this.** Mature, MVVM-friendly, well-documented, hot-reload via XAML Live Preview in VS 2022, easy to ship as single-file `.exe`. The "performance" complaint is irrelevant for a utility that types text. (Note: WPF can't be trimmed/AOT'd, so the binary stays 60–150 MB — see §4.) |
| **UWP** | Skip. Dead end. WinUI 3 replaces it. |
| **WinUI 3** | Tempting because "modern" but: requires Windows App SDK packaging, MSIX deployment friction, smaller ecosystem, and the visual gain over WPF is not worth the complexity for a small utility. |
| **.NET MAUI** | Skip. Cross-platform we don't need, still has rough edges, and isn't Windows-native. |
| **Avalonia UI** | Skip. Excellent framework, but only useful if we wanted cross-platform. Adding it would mean a different XAML dialect for no gain. |
| **Blazor Hybrid** | Skip. Embedding a browser in a desktop utility is overkill. |

**Decision:** **WPF on .NET 10**, with **MVVM via CommunityToolkit.Mvvm** (the official `Microsoft Community Toolkit` MVVM source generators are the modern, attribute-driven approach: `[ObservableProperty]`, `[RelayCommand]`).

### 2.4 Global hotkey on Windows

Source: Microsoft TechNet Wiki on WPF global hot keys; Magnus Montin blog.

**There's no managed .NET API.** The standard pattern is:
- P/Invoke `user32!RegisterHotKey(hWnd, id, fsModifiers, vk)` in `OnSourceInitialized`.
- Listen for `WM_HOTKEY` (`0x0312`) via an `HwndSource` hook on the WPF window.
- Unregister in `OnClosed`.

This is ~30 lines of boilerplate. We get it for free with `RegisterHotKey`. Important caveats to document in the UI:
- If the chosen hotkey collides with another app, `RegisterHotKey` returns `false` — show a clear error.
- Registering consumes the hotkey system-wide; the OS will not deliver those keystrokes to the focused app while the typer is armed.

**Decision:** Default hotkey **Ctrl+Shift+T** (commonly unused, memorable), user-configurable in v1.

### 2.5 Keyboard layout detection

Sources: Microsoft Learn on `GetKeyboardLayout`, Stack Overflow accepted answers.

**The reality:**
- `GetKeyboardLayout(0)` returns the layout of the current thread — which for our process is whatever the user picked *for our app*. Not what the target app uses.
- The correct approach is `GetKeyboardLayout(threadIdOfForegroundWindow)`, polled on a timer or via `WM_INPUTLANGCHANGE`.
- `GetKeyboardLayoutName` gives the KLID (e.g., `00000409` for US English).
- The low word of the HKL is the **language identifier** (LANGID), not the layout. To get the actual *layout* you need `KLID` and a lookup against installed layouts.

**For v1 simplification:** Ship a **single hard-coded mapping table for US QWERTY** (covers ~95% of personal-use Windows installs). Don't try to auto-detect the target app's layout in v1 — that's a v2 problem. The mapping is small and stable; once you have one layout, others are mechanical.

**The five-layout matrix was cut from v1 (per the Approach Review).** Those tables exist *only* because we inject per-character VK + Shift chords; building five of them up front is the single biggest complexity in the design and contradicts the "minimal v1" stance. Instead:
- Ship **US QWERTY only** behind the `KeyboardLayout` abstraction, so UK QWERTY / AZERTY / Dvorak / Colemak remain a cheap, self-contained v2 task.
- Add a **Unicode `VK_PACKET` fallback** (`SimulateTextEntry`) for any character the layout can't express. This is layout-agnostic and guarantees correct output for arbitrary input (accents, dashes, emoji) without more tables. Trade-off: `VK_PACKET` is delivered as a Unicode char event rather than a scancode, so the handful of targets that read raw scancodes (mainly DirectInput games — already a non-goal) won't see fallback characters.

**Decision:** Hard-coded mapping table for **US QWERTY only** in v1, plus a Unicode fallback. The UI layout dropdown shows just "US QWERTY" until more tables exist. Other layouts are deferred to v2.

---

## 3. Final Architecture

### 3.1 Solution layout

```
TypeGent.sln
├── src/
│   ├── TypeGent.App/                # WPF UI (MainWindow, ViewModels, Views)
│   │   ├── App.xaml(.cs)
│   │   ├── MainWindow.xaml(.cs)
│   │   ├── ViewModels/
│   │   │   └── MainViewModel.cs     # CommunityToolkit.Mvvm
│   │   ├── Views/                   # (optional code-behind for dialogs)
│   │   └── Native/
│   │       └── HotKeyManager.cs     # P/Invoke RegisterHotKey wrapper
│   ├── TypeGent.Core/               # Domain logic, no UI deps
│   │   ├── HumanTyping/
│   │   │   ├── HumanTypingEngine.cs # Generates (Delay, KeyAction) sequence
│   │   │   ├── TypingProfile.cs     # WPM, jitter, typo rate, fatigue
│   │   │   ├── DelayModel.cs        # Log-normal sampling + bigram/fatigue adjustments
│   │   │   └── ErrorModel.cs        # Neighbor-key table, error probability
│   │   ├── Layouts/
│   │   │   ├── KeyboardLayout.cs    # abstraction (MapChar may return "unmappable")
│   │   │   └── UsQwertyLayout.cs    # v1 ships this one only
│   │   │   # UkQwerty / Azerty / Dvorak / Colemak deferred to v2
│   │   ├── Typing/
│   │   │   ├── KeyAction.cs         # abstract keystroke (press/chord/release)
│   │   │   └── TypingOrchestrator.cs # Walks the sequence, calls keyboard backend, handles cancel
│   │   └── Abstractions/
│   │       └── IKeyboardBackend.cs
│   └── TypeGent.Native/             # SendInput wrapper
│       └── InputSimulatorPlusBackend.cs  # implements IKeyboardBackend, uses InputSimulatorPlus NuGet
└── tests/
    └── TypeGent.Tests/              # xUnit + FluentAssertions
```

### 3.2 Data flow

```
[User pastes text]
   │
   ▼
[MainViewModel: text, WPM, jitter, typoRate, layout, hotkey]
   │
   │  (hotkey pressed OR Start clicked)
   ▼
[TypingOrchestrator.StartAsync(text, profile, layout)]
   │
   │  1. HumanTypingEngine.Plan(text, profile, layout)
   │     → IEnumerable<TimedAction> where TimedAction = (Delay, KeyAction)
   │
   │  2. For each item in sequence:
   │     - await Task.Delay(delay)
   │     - await _backend.ExecuteAsync(action)  ← calls InputSimulatorPlus
   │     - check CancellationToken
   │
   ▼
[Focused window in any app receives keystrokes]
```

### 3.3 Key algorithms

**Inter-key delay (log-normal + modifiers):**

```
baseDelayMs = 60_000 / (WPM * 5)        // 5 chars per word standard
// μ = ln(baseDelayMs) makes baseDelayMs the MEDIAN interval (a log-normal is right-skewed,
// so its mean is ~exp(σ²/2) higher — do NOT assert mean == base). RNG is injected, not new'd.
jittered    = LogNormalSample(rng, μ = ln(baseDelayMs), σ = 0.35)   // ~25% CV
if char is upper or symbol needing Shift: jittered *= 1.15      // Shift penalty
if char follows space:                  jittered *= 1.5       // word boundary
if bigram(prev, curr) ∈ COMMON_BIGRAMS: jittered *= 0.7       // th, er, in, etc.
jittered *= FatigueFactor(charsTyped)   // 1 + 0.0005 * N
jittered  = clamp(jittered, MIN_DELAY_MS, MAX_DELAY_MS)
```

**Error injection (neighbor-key typos, immediate backspace correction):**

```
for each character c at index i:
    if Random() < typoRate * WordDifficultyBoost(i):
        wrong = NeighborKey(c, layout)        // weighted by physical distance
        emit(wrong, delay)
        emit(Backspace, reactionDelay ~ 120-300ms)
        delay *= 0.6                          // user "knows" the next char
    emit(c, jitteredDelay)
```

**Layout table example (US QWERTY, partial):**

```csharp
new KeyboardLayout {
    Name = "US QWERTY",
    Rows = new[] {
        "1234567890-=",
        "qwertyuiop[]",
        "asdfghjkl;'",
        "zxcvbnm,./",
    },
    Shifted = ")!@#$%^&*(" + "_+" + "{}" + ":\" | <>?",
}
```

Lookup: `(char) → (baseVk, needsShift)` via row+column + shift state.

---

## 4. Tech Stack – Committed Choices

| Layer | Choice | Version | Rationale |
|---|---|---|---|
| Runtime | .NET | 10.0 (LTS) | Current LTS, supported to Nov 2028 (.NET 8 LTS ends Nov 2026). Current TFM, single-file publish. **Not** AOT'd — WPF doesn't support Native AOT and trims poorly. |
| Language | C# | 14 | Modern features (collection expressions, primary constructors, etc.). |
| UI | WPF | .NET 10 built-in | Mature, low ceremony, no extra packages. |
| MVVM | `CommunityToolkit.Mvvm` | 8.x | Source generators (`[ObservableProperty]`, `[RelayCommand]`), official Microsoft Toolkit. Versions independently of the runtime; 8.x is current. |
| Keyboard backend | `InputSimulatorPlus` (NuGet) | latest (1.x, 2022) | Permissive license (verify `LICENSE.md`), scan-code + Unicode support, exactly the surface we need. Stale but stable; can be replaced by ~100 lines of in-house P/Invoke in `TypeGent.Native` if needed. |
| Logging | `Microsoft.Extensions.Logging` | 10.x | Standard, pluggable. |
| DI | `Microsoft.Extensions.DependencyInjection` | 10.x | Optional but keeps Core testable. |
| Tests | `xUnit` + `FluentAssertions` + `NSubstitute` | latest | Modern, readable, mockable. Models take an injected seedable `Random` for deterministic tests. |
| Packaging | `dotnet publish` single-file, self-contained, win-x64 | – | Single `.exe` (~60–150 MB; no trimming/AOT for WPF), no installer needed. |

**No additional packages required.** Specifically rejecting:
- ❌ AutoHotkey (interpreter bundled, can't ship as a real app)
- ❌ PyAutoGUI / `keyboard` (Python dep, admin rights needed, slow)
- ❌ WinUI 3 / Windows App SDK (packaging overhead)
- ❌ Avalonia / MAUI (cross-platform we don't want)
- ❌ C++/native SendInput wrapper (already have InputSimulatorPlus)

---

## 5. UI Specification (minimal, complete)

A single window. Nothing more in v1.

```
┌─────────────────────────────────────────────────────┐
│  TypeGent                              [≡]  [—][□][×]│
├─────────────────────────────────────────────────────┤
│  Text to type:                                       │
│  ┌───────────────────────────────────────────────┐  │
│  │                                               │  │
│  │  (multiline TextBox)                          │  │
│  │                                               │  │
│  └───────────────────────────────────────────────┘  │
│  Characters: 0   Estimated time @ 60 WPM: 0:00      │
│                                                      │
│  Speed (WPM):        [────●─────────] 60            │
│  Jitter (σ):         [──────●───────] 0.35          │
│  Typo rate:          [─●─────────────] 0.02          │
│  Fatigue:            [●──────────────] Off           │
│                                                      │
│  Keyboard layout:  [US QWERTY       ▾]               │
│  Hotkey to start:  [Ctrl+Shift+T    ▾]               │
│                                                      │
│         [  Start (Ctrl+Shift+T)  ]   [ Stop ]        │
│                                                      │
│  Status: Idle. Press Ctrl+Shift+T in target window.  │
└─────────────────────────────────────────────────────┘
```

**Window behavior:**
- `Topmost = true` toggle (so user can keep it visible while clicking into another app)
- Always shows current layout detection (read-only) next to dropdown so user can spot mismatches
- Stop button must cancel cleanly even mid-keystroke (CancellationToken checked after every action)
- Emergency stop: `Escape` hotkey always cancels

---

## 6. Non-Goals for v1 (to prevent scope creep)

- ❌ Multiple profiles / profile management
- ❌ Macro recording
- ❌ Scripting language
- ❌ Keyboard layouts other than US QWERTY (UK/AZERTY/Dvorak/Colemak → v2; v1 covers the rest via the Unicode `VK_PACKET` fallback)
- ❌ Auto-detecting the *target* app's layout (only "what my process uses" is auto-detected)
- ❌ Crossing UIPI boundaries (document as a limitation)
- ❌ Game anti-cheat compatibility (some games filter `SendInput`)
- ❌ Linux / macOS support

---

## 7. Development Phases

**Phase 1 – Skeleton (1 session)**
- Solution + projects, NuGets added, MainWindow renders.
- `IKeyboardBackend` interface, `InputSimulatorPlusBackend` implementation.
- Smoke test: type "hello world" into Notepad via a unit test runner that doesn't actually call SendInput (use a fake backend).

**Phase 2 – Layout + Unicode fallback (1 session)**
- Implement `UsQwertyLayout` end-to-end. Verify "Hello, World!" types correctly into Notepad, including the comma and Shift.
- Add the Unicode `VK_PACKET` fallback for characters the layout can't express (verify with e.g. `é`). Other layouts are v2.

**Phase 3 – HumanTypingEngine (1-2 sessions)**
- `DelayModel` with log-normal (base = median) + bigram + fatigue, taking an injected seedable `Random`.
- `ErrorModel` with QWERTY adjacency table, sharing the same injected `Random`.
- Tunable profile bound to ViewModel sliders.

**Phase 4 – UI wiring (1 session)**
- CommunityToolkit.Mvvm ViewModel.
- Bind everything; live "estimated time" recalculation.

**Phase 5 – Hotkey + polish (1 session)**
- `HotKeyManager` P/Invoke.
- `Escape` emergency stop.
- Status bar with live count of chars typed / remaining.

**Phase 6 – Tests + packaging (1 session)**
- xUnit suite for `DelayModel`, `ErrorModel`, layout lookups.
- `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` produces the shipped `.exe`.

---

## 8. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| UIPI blocks injection into elevated apps | Document in UI; show a warning if the focused window is elevated (`GetWindowThreadProcessId` + token check). |
| Target app uses IME (CJK) | v1 disclaimer; document as future work. Sending VK_PACKET is the path but it's per-IME complexity. |
| Log-normal parameters feel off (too fast/slow) | Expose all constants in `TypingProfile`; ship 3 presets ("Cautious", "Average", "Fast") that users can tune from. |
| Hotkey collides with another app | Show a clear error on registration failure; allow rebind. |
| User accidentally types into the TypeGent window itself | On hotkey trigger, capture the foreground window handle *before* showing any modal/tooltip, and refuse if it's our own process. |
| Anti-cheat / anti-bot apps reject `SendInput` | Out of scope. Document. |
| The user wants to cancel mid-type | `CancellationToken` plumbed everywhere; `Escape` is a hard stop. |

---

## 9. Reference URLs (validated)

Verified by reading the actual content during research:

**Backend:**
- https://github.com/kmcnaught/InputSimulatorPlus — chosen library
- https://github.com/michaelnoonan/inputsimulator — reference
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput — official docs

**Human typing models:**
- https://github.com/Lax3n/HumanTyping — primary inspiration (config.py parameters, state machine)
- https://github.com/Lax3n/HumanTyping/blob/master/QUICKSTART.md — usage patterns
- https://github.com/UnMars/human_typer — simpler baseline (CPM parameter)
- https://github.com/FoeXploit/human-typing-simulator — sanity check
- https://github.com/Pranet-Godavarty/Human-mimic-Auto-Typer — anti-reference (don't lock out the user)

**UI inspiration:**
- https://github.com/maxmmmmmmmmmma/AutoTyper-Pro — slider-based config UX
- https://github.com/ApexXP/Typing-Simulator — minimal hotkey+text UI

**UI framework research:**
- https://claudiobernasconi.ch/blog/dotnet-user-interface-frameworks-selection/
- https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/

**Windows integration:**
- https://learn.microsoft.com/en-us/archive/technet-wiki/30568.wpf-implementing-global-hot-keys — `RegisterHotKey` pattern
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getkeyboardlayout — layout detection
- https://stackoverflow.com/questions/44856042/properly-detect-keyboard-layout — accepted pattern for layout-of-foreground-thread

---

## 10. Bottom Line

This is a buildable, well-scoped v1. The hardest parts (SendInput wrapper, layout detection, global hotkey) all have known, well-documented solutions. The "humanization" layer is the only piece where creative modeling matters, and we have a clear reference (Lax3n/HumanTyping) to borrow parameters from. Ship a WPF app with InputSimulatorPlus + a tuned log-normal delay model + QWERTY mapping, then iterate on realism from real-user feedback.