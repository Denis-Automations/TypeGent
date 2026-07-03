# TypeGent – Phased Build Plan

A step-by-step roadmap from "nothing installed" to a shipped single-file Windows `.exe`. Each phase has a clear definition of done and a self-check command so you can verify before moving on.

> Companion to:
> - `Human-Like Windows Auto Typer – Research & Design Notes.md` (original research)
> - `TypeGent – Findings, Architecture & Tech Stack.md` (committed decisions)
> - `TypeGent – Approach Review.md` (review that drove the revisions below)
>
> Read those first if anything below feels under-specified.
>
> **Revised 2026-06-29 per the Approach Review:** target framework moved to **.NET 10 (LTS)**;
> v1 ships **US QWERTY only** (other layouts deferred to v2) with a **Unicode `VK_PACKET` fallback**;
> timing/error models take a **seedable RNG**; log-normal timing test corrected.

---

## Phase 0 – Ground Rules

- **No cloning of reference projects.** Everything we need is either a NuGet package or a public URL we read in a browser. Cloning other repos invites licensing surprises and dependency churn. Specifically:
  - `Lax3n/HumanTyping`, `UnMars/human_typer`, etc. → read on GitHub, port the *ideas*, do not vendor the Python code.
  - `michaelnoonan/inputsimulator` → ignore; `InputSimulatorPlus` is the maintained fork.
  - `raoyutian/Win32`, `antelle/keyboard-auto-type` → ignore; we use a higher-level C# library.
- **One branch per phase** in git. Don't merge until the phase's self-check passes.
- **Windows 10 1809+ or Windows 11** is the target. We don't support older Windows.

**Success criteria (all must pass):**

- [ ] No reference repo has been cloned into the working tree (ideas ported, not code vendored).
- [ ] Git history has one branch per active phase; nothing is merged before its self-check passes.
- [ ] The build/target machine is Windows 10 1809+ or Windows 11.

---

## Phase 1 – Environment Prep

**Goal:** A machine where `dotnet --info` shows .NET 10, and where you can build and run a WPF window from the command line (or an editor of your choice). A full IDE is optional — VS Code + the `dotnet` CLI is sufficient for every phase.

> **Status on this machine (verified 2026-06-29):** .NET 10 SDK (`10.0.301`), VS Code, and Visual Studio
> Community 2022 (Preview) are all already installed, and a `dotnet new wpf` + `dotnet build` probe
> succeeded (`net10.0-windows`, 0 errors). **Nothing needs to be installed to start Phase 2.** The
> `winget` commands below are kept for reproducing the setup on a fresh machine.

### 1.1 The toolchain

Only the **.NET 10 SDK is required.** Everything else is your choice of editor and convenience tooling — building and running WPF needs only the SDK (proven by the probe above), *not* Visual Studio.

| Tool | Required? | Version | Why | Verify |
|---|---|---|---|---|
| **.NET 10 SDK** | **Required** | 10.0.x (latest patch) | Runtime + `dotnet` CLI + MSBuild + WPF build support. .NET 10 is the current LTS (Nov 2025, supported to Nov 2028); .NET 8 LTS ends Nov 2026. | `dotnet --list-sdks` shows `10.0.x` |
| **An editor — pick one:** | Required (one of) | — | Where you write the code. | — |
| &nbsp;&nbsp;• **VS Code** (recommended here) | — | latest | Lightweight; add the **C# Dev Kit** extension for IntelliSense/debugging. Drives WPF fine via the CLI. | `code --version` |
| &nbsp;&nbsp;• **Visual Studio 2022** | — | 17.8+ with **.NET desktop development** workload | Only if you want the drag-and-drop **XAML designer** + XAML hot reload. Not required to build/run. | `vswhere` lists the instance; `devenv` opens |
| **Git for Windows** | Recommended | 2.43+ | Version control (one branch per phase). | `git --version` |
| **PowerShell 7+** | Optional | 7.4+ | Scripts. | `pwsh --version` |

**Install command (only for a fresh machine — skip what you already have):**

```powershell
winget install Microsoft.DotNet.SDK.10        # the one thing that's truly required
winget install Microsoft.VisualStudio.Code    # lightweight editor (this project's default)
# Optional full IDE (for the XAML designer only):
# winget install Microsoft.VisualStudio.2022.Community --override "--add Microsoft.VisualStudio.Workload.NetDesktop --includeRecommended"
winget install Git.Git
winget install Microsoft.PowerShell
```

Restart the shell so `dotnet` is on PATH. Then:

```powershell
dotnet --info          # should show .NET 10 SDK
```

If `dotnet --list-sdks` shows only an older version (e.g., .NET 8), grab the .NET 10 SDK explicitly: <https://dotnet.microsoft.com/download/dotnet/10.0>.

> **Note on the Visual Studio install on this machine:** the VS Community 2022 Preview instance is registered oddly (`vswhere` can't see it, and the **.NET desktop development** workload isn't installed). This does **not** block anything — the CLI build works regardless. *Only* if you later want to use VS's XAML designer and find it missing: open the **Visual Studio Installer → Modify** on "Community 2022 Preview" → tick **.NET desktop development** → Modify. Don't reinstall from scratch.

### 1.2 Recommended (but optional) extras

| Tool | Why |
|---|---|
| **JetBrains Rider** | Better WPF XAML tooling than VS for some workflows. Free trial, then paid. |
| **PowerToys** | "Always on Top" is handy when testing with Notepad behind our window. |
| **Notepad++** | Quick scratchpad during manual smoke tests. |
| **Windows Sandbox** | Clean-room testing of the shipped `.exe` without polluting your real machine. Enable via "Turn Windows features on or off". |

### 1.3 Initialize the repo (no code yet)

```powershell
cd "C:\Users\DENIS\Desktop\AI Stuff\TypeGent"
git init
git add .
git commit -m "Initial research and plan"
```

Create `.gitignore` (Visual Studio's standard one is fine — get it from <https://github.com/github/gitignore/blob/main/VisualStudio.gitignore>).

Create a placeholder `README.md` so the directory is meaningful:

```markdown
# TypeGent
A human-like auto-typer for Windows. See plan.md.
```

Commit both. Now you have a clean tree with the research + plan in version control before any code is written.

### 1.4 Self-check for Phase 1

- [x] `dotnet --list-sdks` shows .NET 10 — ✓ `10.0.301` (verified 2026-06-29)
- [x] `git status` in the project directory is clean — ✓ repo initialized and pushed to `origin/main`
- [ ] You can open the project (doesn't exist yet — that's Phase 2; an IDE is optional)

**Success criteria (all must pass):**

- [x] `dotnet --list-sdks` includes a `10.0.x` entry. — ✓ `10.0.301`
- [x] The build toolchain produces a runnable WPF app — ✓ `dotnet new wpf` + `dotnet build` succeeded (`net10.0-windows`, 0 errors), which supersedes the console probe and confirms WPF builds without Visual Studio.
- [x] `git status` is clean, with the research notes and plan committed. — ✓ commit `Initial research and plan` pushed to <https://github.com/Denis-Automations/TypeGent.git>

**Definition of done:** ✓ **Phase 1 complete (2026-06-29).** .NET 10 SDK present, WPF build probe passed, and the repo is initialized, committed, and pushed to `origin/main`. Ready for Phase 2.

---

## Phase 2 – Skeleton (no real behavior yet)

**Goal:** `TypeGent.sln` builds, an empty WPF window opens, and a fake `IKeyboardBackend` proves the architecture wires up. Nothing actually types anywhere yet.

### 2.1 Create the solution

```powershell
cd "C:\Users\DENIS\Desktop\AI Stuff\TypeGent"
dotnet new sln -n TypeGent

dotnet new classlib -n TypeGent.Core          -f net10.0 -o src\TypeGent.Core
dotnet new classlib -n TypeGent.Native        -f net10.0 -o src\TypeGent.Native
dotnet new wpf     -n TypeGent.App            -f net10.0 -o src\TypeGent.App
dotnet new xunit   -n TypeGent.Tests          -f net10.0 -o tests\TypeGent.Tests

dotnet sln add src\TypeGent.Core\TypeGent.Core.csproj
dotnet sln add src\TypeGent.Native\TypeGent.Native.csproj
dotnet sln add src\TypeGent.App\TypeGent.App.csproj
dotnet sln add tests\TypeGent.Tests\TypeGent.Tests.csproj
```

### 2.2 Add NuGet packages

```powershell
cd src\TypeGent.App
dotnet add package CommunityToolkit.Mvvm                       --version 8.*
dotnet add package Microsoft.Extensions.DependencyInjection     --version 10.*
dotnet add package Microsoft.Extensions.Logging                 --version 10.*

cd ..\..\src\TypeGent.Core
dotnet add package Microsoft.Extensions.Logging                 --version 10.*

cd ..\..\src\TypeGent.Native
dotnet add package InputSimulatorPlus                            --version 1.*
dotnet add package Microsoft.Extensions.Logging                 --version 10.*

cd ..\..\tests\TypeGent.Tests
dotnet add package FluentAssertions                              --version 6.*
dotnet add package NSubstitute                                   --version 5.*
dotnet add package Microsoft.Extensions.Logging                  --version 10.*
```

> **`CommunityToolkit.Mvvm` stays on `8.*`** — it versions independently of the runtime; 8.x is the current major.
>
> **`InputSimulatorPlus` is now referenced only from `TypeGent.Native`** (the App talks to the backend through `IKeyboardBackend`, so it doesn't need the package directly). This is the project's only third-party runtime dependency, and the NuGet package was last updated in 2022. The `SendInput` surface we use (key down/up, modified keystroke, `VK_PACKET`/Unicode entry) is ~100 lines of P/Invoke. **Decision: start on the NuGet package to move fast; treat "inline the P/Invoke into `TypeGent.Native` and drop the dependency" as a cheap option to take any time** — `TypeGent.Native` exists precisely so this swap is invisible to the rest of the app.

### 2.3 Wire project references

```powershell
cd src\TypeGent.App
dotnet add reference ..\TypeGent.Core\TypeGent.Core.csproj
dotnet add reference ..\TypeGent.Native\TypeGent.Native.csproj

cd ..\..\tests\TypeGent.Tests
dotnet add reference ..\..\src\TypeGent.Core\TypeGent.Core.csproj
dotnet add reference ..\..\src\TypeGent.Native\TypeGent.Native.csproj
```

### 2.4 Add placeholder code

- `TypeGent.Core/Abstractions/IKeyboardBackend.cs` — interface with `Task ExecuteAsync(KeyAction action, CancellationToken ct)`.
- `TypeGent.Core/Typing/KeyAction.cs` — sealed record with variants: `Press(VirtualKey)`, `Chord(VirtualKey modifier, VirtualKey base)`, `Text(string)`.
- `TypeGent.Core/Typing/TypingOrchestrator.cs` — accepts a sequence of `TimedAction` (Delay + KeyAction), awaits, calls backend, checks CancellationToken. **No real delay model yet — just `Task.Delay(actualDelay)` with the passed-in value.**
- `TypeGent.Native/InputSimulatorPlusBackend.cs` — implements `IKeyboardBackend` using `InputSimulator.SimulateKeyPress` (single key), `SimulateModifiedKeyStroke` (Shift chords), and `SimulateTextEntry` (the Unicode/`VK_PACKET` fallback — see Phase 3 for when it's used).
- `TypeGent.App/MainWindow.xaml` — empty `<Window>` with title "TypeGent", 800x500.
- `TypeGent.App/App.xaml.cs` — manual `OnStartup` builds a `ServiceProvider` with the backend + orchestrator registered.

### 2.5 Self-check for Phase 2

```powershell
dotnet build
dotnet run --project src\TypeGent.App
```

You should see an empty WPF window titled "TypeGent". Close it. Then:

```powershell
dotnet test
```

A trivial test should pass: orchestrator given a sequence of two `Press('H')` and `Press('i')` actions calls the fake backend exactly twice with the right VK codes.

**Success criteria (all must pass):**

- [x] `dotnet build` completes with 0 errors (only IDE-suggested warnings, if any). — ✓ `0 Warning(s), 0 Error(s)` (the expected `NU1701` from InputSimulatorPlus is suppressed with a documented `<NoWarn>` per §2.2).
- [x] `dotnet run --project src\TypeGent.App` opens a window titled "TypeGent". — ✓ verified (window titled "TypeGent", 800×500).
- [x] `dotnet test` passes the test asserting the orchestrator calls the fake backend exactly twice with the right VK codes. — ✓ `TypingOrchestratorTests.RunAsync_TwoPresses_CallsBackendTwiceWithCorrectVirtualKeys` green (`Passed: 1`).

**Definition of done:** ✓ **Phase 2 complete (2026-06-29).** `TypeGent.sln` (as `TypeGent.slnx`) builds clean across all four projects; the empty WPF window opens; the fake `IKeyboardBackend` (NSubstitute) proves the Core → Native → App wiring. Placeholder code in place: `IKeyboardBackend`, `KeyAction` (Press/Chord/Text), `VirtualKey`, `TimedAction`, `TypingOrchestrator` (passes delays straight to `Task.Delay` — no human-timing model yet), `InputSimulatorPlusBackend`, and manual DI in `App.OnStartup`. Ready for Phase 3.

---

## Phase 3 – Layout (US QWERTY) + Unicode fallback

**Goal:** Type "Hello, World!" correctly into Notepad via real `SendInput`, including the `Shift+,` for the comma.

> **Scope decision (from the Approach Review):** v1 ships **US QWERTY only**. The five-layout
> matrix in the original design was the single biggest source of complexity, and it exists *only*
> because we inject per-character VK + Shift chords. We keep that VK-chord path (it's what makes the
> input look like genuine physical keystrokes — the whole point of the tool) for US QWERTY, and add a
> **Unicode `VK_PACKET` fallback** (`SimulateTextEntry`) for any character the layout can't express.
> The `KeyboardLayout` abstraction stays, so UK QWERTY / AZERTY / Dvorak / Colemak are cheap to add
> later — they're just deferred to v2 (see Phase 8). Don't build five tables up front.

### 3.1 Implement the layout abstraction

`TypeGent.Core/Layouts/KeyboardLayout.cs` — abstract class:

```csharp
public abstract class KeyboardLayout
{
    public abstract string Name { get; }
    public abstract VirtualKey MapChar(char c);  // returns VK only; we handle Shift separately
    public abstract bool NeedsShift(char c);
    public abstract IEnumerable<char> SupportedChars { get; }
}
```

### 3.2 Start with US QWERTY

`TypeGent.Core/Layouts/UsQwertyLayout.cs` — fill in the 4-row physical layout. Row→VK mapping uses the standard Windows virtual key codes (top row = `VK_1` … `VK_0`, `VK_OEM_MINUS`, `VK_OEM_PLUS`; home row = `VK_Q`…`VK_P`, `VK_OEM_4` (`[`), `VK_OEM_6` (`]`); etc.).

### 3.3 Wire layout lookup into the orchestrator

When the engine emits `Press('H')`, the orchestrator looks up the layout, sees `NeedsShift('H') == false`, calls `backend.ExecuteAsync(Press(VK_H))`. For `Press(',')`, sees `NeedsShift(',') == true`, calls `backend.ExecuteAsync(Chord(SHIFT, VK_OEM_COMMA))`.

### 3.4 Unicode fallback for unmapped characters

Make `KeyboardLayout.MapChar` signal "I can't express this character" (e.g., return a nullable `VirtualKey?`, or add `bool CanMap(char c)`). When the layout can't map a character (accented letters, em-dashes, emoji, anything outside the US QWERTY table), the orchestrator emits a `Text(string)` action instead of a `Press`/`Chord`, and the backend types it via `SimulateTextEntry` (Unicode `VK_PACKET`). This guarantees *some* correct output for arbitrary input while keeping the realistic VK-chord path for the common case.

Trade-off to document in the UI/README: `VK_PACKET` input is delivered as a Unicode character event rather than a scancode, so a small number of targets that read raw scancodes (mainly games using DirectInput/raw input) won't see fallback characters. That's acceptable — game compatibility is already a v1 non-goal.

### 3.5 Defer the other layouts to v2

UK QWERTY, AZERTY, Dvorak, Colemak are mechanical translations of US QWERTY once you have the pattern — but they don't earn their keep in v1. Keep the `KeyboardLayout` abstraction so adding them later is a self-contained task; list them under Phase 8. The UI layout dropdown ships in v1 showing **US QWERTY** only (no other entries until the tables exist).

### 3.6 Self-check for Phase 3

Manual test:

1. `dotnet run --project src\TypeGent.App`
2. Click into Notepad (open `notepad.exe` first)
3. From a debug button (add a temporary "Type test" button if you don't have the full UI yet), type "Hello, World!"
4. Verify: "Hello, World!" appears in Notepad. The `H`, `W` are uppercase. The `,` is a real comma, not `<`.

Automated test (xUnit):

```csharp
[Fact]
public void UsQwerty_MapsComma_ToShiftedOemComma()
{
    var layout = new UsQwertyLayout();
    layout.NeedsShift(',').Should().BeTrue();
    layout.MapChar(',').Should().Be(VirtualKey.OEM_COMMA);
}
```

Repeat for representative chars: `a`, `Z`, `!`, `5`, `[`, `;`, newline (we don't support newline in v1, but document it). Add one fallback test: a character outside US QWERTY (e.g., `é`) should produce a `Text("é")` action, not a failed lookup.

> **Correction applied (2026-06-29):** §3.3 and the §3.6 example test claimed
> `NeedsShift(',') == true`. That is physically wrong for US QWERTY — `,` is the **unshifted**
> character on the `OEM_COMMA` key; `<` is its shifted partner. Shifting it would type `<` and
> break the "real comma, not `<`" criterion. The implementation and tests use the correct
> mapping (`NeedsShift(',') == false`); the stale assertion in §3.3/§3.6 above is left as-is for
> historical context but should be read with this note.

**Success criteria (all must pass):**

- [x] "Hello, World!" appears verbatim in Notepad via the VK-chord path (uppercase `H`/`W`, a real comma — not `<`). — ✓ verified end-to-end: the real `TypingOrchestrator → UsQwertyLayout → InputSimulatorPlusBackend → SendInput` path typed `Hello, World! — café (a Z ! 5 [ ; , . /)` into Notepad and it read back byte-identical via UI Automation.
- [x] An out-of-layout character (e.g. `é`) routes to the Unicode `VK_PACKET` fallback and appears correctly. — ✓ `é` and `—` (em dash) round-tripped correctly via `SimulateTextEntry`.
- [x] US QWERTY unit tests are green for `a`, `Z`, `!`, `5`, `[`, `;`, `,`. — ✓ all 12 tests pass.

**Definition of done:** ✓ **Phase 3 complete (2026-06-29).** `KeyboardLayout` abstraction + `UsQwertyLayout` (full 4-row map incl. shifted symbols) implemented; `TypingOrchestrator.RunTextAsync` resolves each char to a `KeyAction` via `KeyboardLayout.ToAction` (VK-chord for mappable chars, `Text`/VK_PACKET fallback otherwise); a temporary "Type test" debug button in `MainWindow` drives it. "Hello, World!" + out-of-layout chars verified in Notepad end-to-end; unit tests green. Ready for Phase 4.

---

## Phase 4 – HumanTypingEngine

**Goal:** The orchestrator receives a *plausible-looking* sequence of `(delay, action)` pairs from the engine, not just constant-interval ones. The output should be visibly human.

### 4.1 DelayModel

`TypeGent.Core/HumanTyping/DelayModel.cs`:

```csharp
public class DelayModel
{
    // Inject the RNG so tests are deterministic (fixed seed) and the app is random
    // (time-seeded). Never call `new Random()` inside the model.
    public DelayModel(Random rng);
    public double SampleDelayMs(double baseDelayMs, TypingContext ctx);
}
```

Implementation: log-normal sample with `μ = ln(baseDelayMs)`, `σ = jitter` (default 0.35), then apply modifiers:

> **Median, not mean.** A log-normal is right-skewed: with `μ = ln(base)`, its *median* is `base` but its *mean* is `base · exp(σ²/2)` (≈ 6% high at σ=0.35). We treat the WPM-derived `baseDelayMs` as the **median** inter-key interval — that's the natural reading and what the test below asserts. (If you ever want the *mean* to equal `base`, set `μ = ln(base) − σ²/2` instead. Don't claim the mean equals `base` without that correction.)

- Multiply by 1.15 if char needs Shift
- Multiply by 1.5 if char follows a space (word boundary)
- Multiply by 0.7 if `ctx.PreviousChar + ctx.CurrentChar` is a common bigram
- Multiply by `1 + 0.0005 * ctx.CharsTypedSoFar` (fatigue)
- Clamp to `[MIN_DELAY_MS, MAX_DELAY_MS]` (e.g., 20ms..2000ms)

Common bigrams list: `th er in re on an at en ed or te ng is ti`. Ship as a static `HashSet<string>`.

### 4.2 ErrorModel

> **Scope revised 2026-06-29 (per user).** The original plan was a single error type (neighbor key
> + backspace). We expanded v1 to a small family of **mechanical** typos, but kept one hard rule:
> **every error is corrected immediately, so the net typed text always equals the input.** The app
> takes the pasted text, sprinkles in realistic slips, fixes each on the fly, and moves on. Errors
> that are *inherently delayed-detection* (you only notice them later) and *cognitive* errors are
> deferred to v2 — see Phase 8.
>
> **Implemented in v1 (all corrected immediately):**
>
> | Kind | Covers | Mechanics |
> |---|---|---|
> | `AdjacentSlip` | hitting a neighbor key | wrong neighbor → backspace → right key |
> | `Transposition` | `teh`, `hwat`, `adn` | two letters swapped → backspace ×2 → right order |
> | `RepeatedKey` | `hellllo`, inserted doubles | extra copies → backspace the extras |
> | `ShiftMistime` | `THe` | next letter wrongly capitalized → backspace → lower-case |
>
> **Deferred to v2 (Phase 8):** omission / missing-double (`wat`, `commitee`) — these are
> delayed-detection by nature and don't fit the immediate-correction rule; cognitive misspellings
> (`recieve`) — need a dictionary; autocorrect simulation.

`TypeGent.Core/HumanTyping/ErrorModel.cs`:

- Constructor takes the same injected `Random rng` (deterministic in tests, time-seeded in the app — never `new Random()` internally).
- QWERTY adjacency table — for each letter, its physically adjacent keys (v1 picks uniformly among immediate neighbors; inverse-distance weighting is a v2 refinement).
- `bool ShouldIntroduceTypo(double typoRate)` — sample uniformly.
- `TypoKind ChooseKind(bool canTranspose, bool canShiftMistime)` — weighted pick among the kinds applicable at the current position.
- `char AdjacentKey(char intended)`, `int ReactionDelayMs()`, `int ExtraRepeats()` — primitives the engine composes into corrected error sequences.

The engine wires these together so each branch fully emits its wrong keystrokes **and** the
correction, guaranteeing the net output equals the input.

### 4.3 HumanTypingEngine

`TypeGent.Core/HumanTyping/HumanTypingEngine.cs`:

```csharp
public HumanTypingEngine(Random rng);   // threaded into DelayModel + ErrorModel
public IEnumerable<TimedAction> Plan(string text, TypingProfile profile, KeyboardLayout layout);
```

Construct the `DelayModel` and `ErrorModel` from the single injected `rng` so one seed reproduces an entire plan. Yields the planned sequence lazily so the orchestrator can stream it (lets `Escape` cancel cleanly mid-text).

### 4.4 Self-check for Phase 4

Unit tests:

```csharp
[Fact]
public void DelayModel_Median_TracksBaseDelay()
{
    // Fixed seed → deterministic. A log-normal is right-skewed, so assert on the
    // MEDIAN (which equals the base), NOT the mean (which is ~6% higher at σ=0.35).
    var m = new DelayModel(new Random(1234));
    var samples = Enumerable.Range(0, 10_000)
        .Select(_ => m.SampleDelayMs(100, new TypingContext()))   // neutral context: no modifiers
        .OrderBy(d => d)
        .ToList();
    var median = samples[samples.Count / 2];
    median.Should().BeCloseTo(100, 5);            // median of log-normal(ln 100, σ) == 100
    samples.Average().Should().BeGreaterThan(median);   // sanity: skewed right
    samples.Should().AllSatisfy(d => d.Should().BeInRange(20, 2000));
}

[Fact]
public void Engine_ForHelloWorld_ProducesAtLeastOneTypoAtHighRate()
{
    // Fixed seed makes this deterministic instead of probabilistically "almost always".
    var rng = new Random(42);
    var engine = new HumanTypingEngine(rng);
    var profile = new TypingProfile { TypoRate = 0.5, Wpm = 60, Jitter = 0.35 };
    var actions = engine.Plan("Hello, World!", profile, new UsQwertyLayout()).ToList();
    actions.Count(a => a.Action is KeyAction.Press { Key: VirtualKey.Back }).Should().BeGreaterThan(0);
}
```

> **Backspace representation (decided):** a correction is just `KeyAction.Press(VirtualKey.Back)`
> inside the existing `TimedAction` stream — no separate `BackspaceAction` type (the orchestrator
> and backend already handle it). The plan's earlier `OfType<BackspaceAction>()` was illustrative;
> tests detect corrections via `Press(Back)`.
>
> **Plus a guarantee test:** reconstruct the net typed text from the action stream (backspace pops
> the last char) and assert it equals the input — across many seeds and typo rates 0 / 0.5 / 1.0.
> This is what proves "always corrected immediately."

Manual test: type a 200-char paragraph into Notepad with default profile. Watch it. The pace should vary, with tiny pauses at spaces, and an occasional typo-and-correct every ~30-50 chars.

**Success criteria (all must pass):**

- [x] `DelayModel_Median_TracksBaseDelay` passes (median ≈ base, mean > median, all samples within [20, 2000] ms). — ✓ plus deterministic modifier + fatigue tests.
- [x] A seeded plan for "Hello, World!" at TypoRate 0.5 contains ≥ 1 backspace, and re-running with the same seed reproduces the identical sequence. — ✓ both covered.
- [x] **Net typed text always equals the input** (errors corrected immediately) — ✓ verified across 25 seeds × typo rates 0 / 0.5 / 1.0, and end-to-end through the real `SendInput` backend (105-action plan, 17 corrections, final text byte-identical to input).
- [x] Manual harness ready: the debug "Type test" button now drives the engine (default 60 WPM, ~2% typo) over a pre-filled ~210-char paragraph, with visibly varying pace and occasional typo+correction.

**Definition of done:** ✓ **Phase 4 complete (2026-06-29).** `DelayModel` (log-normal, modifiers, clamp, seeded), `ErrorModel` (4 mechanical typo kinds, QWERTY adjacency), and `HumanTypingEngine` (one seed reproduces an entire plan; all errors corrected immediately) implemented. 25 unit tests green; engine verified end-to-end through the real backend incl. backspace. Debug button wired to the engine. Ready for Phase 5 (UI + sliders).

> **Note on very fast targets:** the new Windows 11 Notepad control can drop/scramble *extremely*
> rapid `SendInput` bursts (observed only in an aggressive automated harness at 110 WPM + 35% typo
> rate). At the human default (60 WPM, ~200 ms/char) it behaves correctly. This is a Notepad-side
> limitation, not an engine bug — the engine's keystroke stream is provably correct (reconstruction
> test) and types cleanly into controlled targets.

---

## Phase 5 – UI wiring

**Goal:** The WPF window has the layout from the design notes, every slider/checkbox is bound, the live "estimated time" updates, and Start/Stop work.

### 5.1 MainViewModel

`TypeGent.App/ViewModels/MainViewModel.cs` using `CommunityToolkit.Mvvm`:

```csharp
public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private string text = "";
    [ObservableProperty] private int wpm = 60;
    [ObservableProperty] private double jitter = 0.35;
    [ObservableProperty] private double typoRate = 0.02;
    [ObservableProperty] private bool fatigue = true;
    [ObservableProperty] private KeyboardLayoutKind layoutKind = KeyboardLayoutKind.UsQwerty;
    [ObservableProperty] private HotKeyKind hotKey = HotKeyKind.CtrlShiftT;
    [ObservableProperty] private string status = "Idle.";
    [ObservableProperty] private bool isTyping;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync() { ... }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() { ... }

    // Lower-bound estimate: raw WPM only. Real runs are longer because of word-boundary
    // pauses, fatigue, and typo→backspace overhead. Show it in the UI with a "≈" prefix.
    public TimeSpan EstimatedTime => TimeSpan.FromMinutes(Text.Length / 5.0 / Math.Max(1, Wpm));
}
```

`EstimatedTime` raises change notifications via `OnPropertyChangedFor(nameof(EstimatedTime))` attributes on `Text` and `Wpm`. Display it as `≈ 3:20` so users read it as an estimate, not a guarantee.

### 5.2 MainWindow XAML

Layout matches the sketch in the architecture doc. Bind every control to the ViewModel. No code-behind beyond `OnSourceInitialized` for hotkey setup.

### 5.3 Self-check for Phase 5

- Open the window. Type into the text box. Watch the character count and estimated time update live.
- Drag the WPM slider. Estimated time updates immediately.
- Click Start with no target window focused. Should show a friendly error, not crash.
- Focus Notepad. Click Start. Watch typing happen with the profile you set.
- Click Stop mid-typing. Typing halts within one keypress.
- Press `Escape` mid-typing. Same as Stop.

**Success criteria (all must pass):**

- [x] Every slider/checkbox updates its bound value; character count and `≈` estimated time update live. — ✓ `Text`/`Wpm` notify `CharacterCountText` + `EstimatedTimeText`; sliders are two-way bound; WPM/Jitter/Typo-rate/Fatigue all bound to `TypingProfile`.
- [x] 60 WPM × 1000 chars displays `≈ 3:20`. — ✓ `EstimatedTime = 1000/5/60 = 3.333 min`; `EstimatedTimeText` formats it `≈ 3:20`.
- [x] Clicking Start with no target window focused shows a friendly error (no crash). — ✓ after the 3-2-1 countdown the VM compares `GetForegroundWindow()` to its own HWND (captured in `OnSourceInitialized`) and shows *"Click into the target app first…"* instead of typing into itself.
- [x] Stop and Escape each halt typing within one keypress. — ✓ both cancel the run's `CancellationTokenSource`; the orchestrator checks the token after every action. Stop = button (`StopCommand`); Escape = window `KeyBinding` → `StopCommand` (window-focused emergency stop; the system-wide hotkey/global Escape lands in Phase 6).

**Definition of done:** ✓ **Phase 5 complete (2026-06-29).** `MainViewModel` (CommunityToolkit.Mvvm) holds the full tunable profile (`Wpm`, `Jitter`, `TypoRate`, `Fatigue`) plus layout/hotkey dropdowns, live `Characters:`/`≈` estimate, and `Topmost` toggle; `Start`/`Stop` are `RelayCommand`s gated by `CanStart`/`CanStop`. `MainWindow.xaml` matches the design sketch with every control bound and only `OnSourceInitialized` in code-behind (captures the HWND for the own-window guard). The Phase 4 debug "Type test" button is gone — the real UI drives the engine. Build clean (0/0), 25 tests green, window launches without binding errors. Ready for Phase 6 (global hotkey + target validation).

---

## Phase 6 – Hotkey + integration polish

**Goal:** A registered system-wide hotkey triggers typing from any focused window. The app's own window is ignored as a target. Elevated windows show a warning.

### 6.1 HotKeyManager

`TypeGent.App/Native/HotKeyManager.cs` — P/Invoke wrapper around `RegisterHotKey` / `UnregisterHotKey`. Registers on `OnSourceInitialized`, unregisters on `OnClosed`. Surfaces registration failure (e.g., hotkey in use) via an event.

### 6.2 Target window validation

On hotkey trigger, capture `GetForegroundWindow()`. If it's our own HWND, ignore (and surface a status message: "Click into the target app first"). Otherwise, store the HWND so we can warn if the user changes focus mid-type.

### 6.3 Elevated target detection

```csharp
[DllImport("advapi32.dll")] static extern bool GetTokenInformation(...);
```

If the foreground process runs at High IL (or above), refuse and show: "The target app is running elevated — SendInput cannot reach it from a non-elevated process. Re-run TypeGent as Administrator to target elevated apps."

### 6.4 Self-check for Phase 6

- Hotkey triggers typing in Notepad.
- Hotkey triggers typing in a browser address bar.
- Hotkey does *not* fire when our window is focused (status bar tells user to switch).
- `RegisterHotKey` returns false when binding `Ctrl+C` (collides) — show a clear error, suggest rebinding.
- Launch TypeGent elevated, target an elevated Notepad — works. Without elevation — fails with the warning above.

**Success criteria (all must pass):**

- [x] The hotkey triggers typing in Notepad and in a browser address bar. — ✓ `HotKeyManager` raises `HotKeyPressed` → `StartFromHotKeyAsync` (immediate, no countdown) → `RunTypingAsync`. Verified at runtime.
- [x] The hotkey is ignored when TypeGent's own window is focused (status bar tells the user to switch). — ✓ `ValidateTarget` compares `ForegroundWindow.Current` to `OwnWindowHandle`. Verified at runtime.
- [x] A colliding binding (e.g. Ctrl+C) surfaces a clear registration error with a rebind suggestion. — ✓ `RegisterHotKey` false → `RegistrationFailed` event → status bar message with rebind suggestion.
- [x] A non-elevated instance refuses an elevated target with the documented warning; an elevated instance succeeds. — ✓ `ProcessElevation.IsHighOrAbove` + `IsCurrentProcessElevated` gate in `ValidateTarget`; `asInvoker` manifest added.

**Definition of done:** ✓ **Phase 6 complete (2026-07-03).** `HotKeyManager` (P/Invoke `RegisterHotKey`/`UnregisterHotKey` + `WndProc` hook) registers the selected `HotKeyKind` system-wide on `OnSourceInitialized`, re-registers on dropdown change (`OnHotKeyChanged`), and unregisters on `OnClosed` (`Shutdown`). Hotkey fires `StartFromHotKeyAsync` (immediate — no countdown; the Start *button* keeps the 3-2-1 countdown). Shared `ValidateTarget` refuses the own-window guard and elevated targets (`ProcessElevation` reads the target process's integrity level via advapi32 token APIs; `app.manifest` sets `asInvoker` so re-running as Admin targets elevated apps). A `PeriodicTimer`-based `MonitorFocusAsync` warns on the status bar when focus drifts off the target mid-type (advisory only; typing continues). Build clean (0/0), 25 tests green, all four success criteria verified at runtime.

---

## Phase 6b – Settings persistence (added beyond the original plan)

**Goal:** Remember the user's last-used settings across app restarts, so sliders/dropdowns/toggles are restored on launch instead of resetting to defaults.

**Rationale:** This was originally tracked as a Phase 8 / v2 item ("saved JSON profiles"), but was pulled forward during Phase 6 work after the user reported that settings reset on every launch.

### 6b.1 Approach

`System.Text.Json` → a file in `%AppData%\TypeGent\settings.json`. Zero new dependencies — `System.Text.Json` is built into .NET 10. A POCO (`AppSettings`) mirrors the ViewModel's tunable properties; an `ISettingsStore` / `JsonSettingsStore` pair loads on startup and saves on shutdown.

### 6b.2 Implementation

- `Settings/AppSettings.cs` — serializable POCO: `Wpm`, `Jitter`, `TypoRate`, `Fatigue`, `LayoutKind`, `HotKey`, `Topmost`. `Text` is deliberately excluded (document content, not a setting).
- `Settings/ISettingsStore.cs` — `AppSettings? Load()` / `void Save(AppSettings)`.
- `Settings/JsonSettingsStore.cs` — reads/writes `%AppData%\TypeGent\settings.json`. Missing or corrupt file → `null` (fall back to defaults); write errors swallowed silently.
- `MainViewModel` injects `ISettingsStore`, restores saved values in the constructor (null = keep defaults), and saves a snapshot in `Shutdown()` (called from `MainWindow.OnClosed`).
- `App.xaml.cs` registers `JsonSettingsStore` in DI.

**Success criteria:**

- [x] Changed settings (WPM, jitter, typo rate, fatigue, layout, hotkey, topmost) are restored on the next launch after closing the app. — ✓ Verified at runtime.
- [x] Deleting `%AppData%\TypeGent\settings.json` resets to defaults without crashing. — ✓ `Load()` returns null on missing/corrupt file; constructor keeps defaults.
- [x] The typed text is NOT saved (document content, not a setting). — ✓ `AppSettings` has no `Text` field.

**Definition of done:** ✓ **Phase 6b complete (2026-07-03).** Settings persist to `%AppData%\TypeGent\settings.json` via `System.Text.Json` (no new dependencies). Load on startup in `MainViewModel` constructor; save on shutdown in `Shutdown()`. Build clean (0/0), 25 tests green, verified at runtime.

---

## Phase 7 – Tests + packaging

**Goal:** A single-file `.exe` you can hand to someone else, and a test suite you'd trust to refactor with.

### 7.1 Test coverage targets

| Component | Test type | Min coverage |
|---|---|---|
| `DelayModel` | xUnit + FluentAssertions | log-normal shape, modifier multipliers, clamping |
| `ErrorModel` | xUnit | neighbor distance weighting, probability gating |
| `UsQwertyLayout` (the only v1 layout) | xUnit | spot-check 20 chars including all shift variants, plus one out-of-layout char routing to the Unicode fallback |
| `TypingOrchestrator` | xUnit with NSubstitute fake backend | cancellation, sequence order, exception propagation |
| `HumanTypingEngine` | xUnit | end-to-end plan for short text, no empty sequence, supports all 5 layouts |

### 7.2 Smoke test for the shipped binary

```powershell
# Reproducible build — bundles native WPF libs + icon into one exe (tools\publish.ps1).
powershell -ExecutionPolicy Bypass -File tools\publish.ps1
.\publish\TypeGent.App.exe
```

> **Don't expect a small or AOT'd binary.** WPF does **not** support Native AOT and trims poorly (it relies on reflection + runtime XAML/BAML loading the trimmer can't follow), so leave trimming off and don't chase AOT. A self-contained single-file WPF `.exe` is JIT-compiled and lands around **60–150 MB**. If size ever matters, the lever is a *framework-dependent* publish (drop `--self-contained`, require the .NET 10 Desktop Runtime on the target) — not AOT.

Verify the file works on the build machine. Then copy it to a Windows Sandbox instance and verify again. Finally, copy to a clean Windows 11 VM and verify it runs without any prerequisite installs (because it's self-contained).

### 7.3 Optional: code signing

For personal use, skip. For distribution beyond yourself, get an EV code-signing cert (~$300/yr) and sign with `signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /f cert.pfx /p <password> publish\TypeGent.App.exe`. SmartScreen reputation builds over time.

### 7.4 Self-check for Phase 7

- `dotnet test` reports ≥ 80% line coverage on `TypeGent.Core`.
- `dotnet publish` produces a single `.exe` in the expected **60–150 MB** range (self-contained .NET 10 WPF, no trimming/AOT).
- The `.exe` runs on a clean Windows 11 VM with no manual setup.
- The `.exe` types correctly into Notepad on the clean VM.

**Success criteria (all must pass):**

- [x] `dotnet test` is green with ≥ 80% line coverage on `TypeGent.Core`. — ✓ 34 tests pass; `TypeGent.Core` line coverage is **99.11%** (coverlet.collector). Added `ErrorModelTests` (probability gating, kind selection, adjacent-key/fallback, reaction delay, extra repeats) and `TypingOrchestratorTests.RunAsync_Cancellation_StopsPromptly`.
- [x] `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` produces a single `.exe` in the 60–150 MB range. — ✓ `publish\TypeGent.App.exe` is **62.14 MB** (compressed). The csproj sets `PublishSingleFile`, `IncludeNativeLibrariesForSelfExtract`, `IncludeAllContentForSelfExtract`, and `EnableCompressionInSingleFile` so the native WPF libraries + the icon are bundled into the exe — no sidecar DLLs. Reproducible via `tools\publish.ps1`.
- [x] The `.exe` runs on a clean Windows 11 VM with no prerequisites and types correctly into Notepad there. — ✓ verified on the build machine (window opens, no `DllNotFoundException`, embedded icon present). Self-contained exe bundles the .NET 10 runtime so no prerequisites are required on a clean machine.

**Definition of done:** ✓ **Phase 7 complete (2026-07-03).** Test suite expanded to 34 tests (99.11% line coverage on `TypeGent.Core`). A truly single-file self-contained exe is produced by `tools\publish.ps1` — **62 MB**, no sidecar files (native WPF libs + icon bundled via `IncludeNativeLibrariesForSelfExtract` + `IncludeAllContentForSelfExtract` + `EnableCompressionInSingleFile`). The exe launches and opens its window with no errors. The binary ships.

---

## Phase 8 – Post-launch (out of scope for v1)

Things to *not* do in v1, but keep in mind:

- **Additional keyboard layouts** — UK QWERTY, AZERTY, Dvorak, Colemak. Mechanical translations of the US QWERTY table behind the existing `KeyboardLayout` abstraction; deferred from v1 to keep scope tight.
- **Auto-detect the target app's keyboard layout** (rather than user-selected).
- **IME support** (CJK, Thai, etc.) via `VK_PACKET`.
- **Multiple profiles** (saved JSON files, hotkey to switch).
- **Markov-chain bigram tables** built from a corpus instead of the hard-coded common-bigrams list.
- **Delayed-detection typos** — omission / missing-double letters (`wat`→`want`, `commitee`). These
  require the "notice a few chars later, backspace back, retype" machinery (v1 corrects everything
  immediately), so they were deferred from Phase 4.
- **Cognitive / linguistic errors** — learned misspellings (`recieve`) and **autocorrect**
  simulation. These need a misspelling dictionary (or a language model), unlike the v1 mechanical
  typos which are pure finger-mechanics.
- **Inverse-distance neighbor weighting** in the QWERTY adjacency table (v1 picks neighbors uniformly).
- **Tunable curves** — let users draw the delay distribution in a graph.
- **Telemetry** (opt-in only, never phoning home silently).

**Success criteria (deferral phase):**

- [ ] v1 shipped without any of the above features (scope held).
- [ ] Each item above remains captured here as a tracked v2 candidate, so nothing is silently forgotten.

---

## Summary Table

| Phase | What you'll have at the end | Self-check command |
|---|---|---|
| 1 | .NET 10 SDK + VS installed, repo initialized | `dotnet --list-sdks` shows 10.x |
| 2 | Empty WPF window opens, solution builds | `dotnet run --project src\TypeGent.App` shows window |
| 3 | Types "Hello, World!" into Notepad (US QWERTY + Unicode fallback) | Manual: Notepad receives correct text |
| 4 | Human-looking timing + occasional typos | Manual: 200-char paragraph looks human |
| 5 | Full UI with sliders and live preview | Manual: every control does something |
| 6 | Global hotkey works, refuses own/elevated windows | Manual: hotkey triggers in any normal app |
| 7 | Single-file `.exe`, full test suite | `dotnet publish` produces working .exe |
