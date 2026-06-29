# TypeGent – Approach Review

> Independent technical review of the three planning documents
> (`Human-Like Windows Auto Typer – Research & Design Notes.md`, `TypeGent – Findings, Architecture & Tech Stack.md`, `plan.md`)
> performed before any code was written. Reviewed: 2026-06-29.

---

## Verdict (TL;DR)

**Yes — the overall approach is sound, well-scoped, and buildable.** The stack (C# desktop app + a `SendInput` wrapper + a custom timing/error model + a global hotkey) is the correct shape for this problem, the reference evaluation is unusually thorough, and the phased plan with per-phase self-checks is exactly how a one-person utility should be built. You could start coding from this and ship.

That said, several decisions are **slightly out of date or over-scoped for a v1**, and there are a few **concrete technical errors** in the plan that will bite you when you write the tests. None of them change the architecture; they're refinements. The most important three:

1. **Pin .NET 10, not .NET 8.** .NET 8 LTS ends **10 Nov 2026** — about five months from now. Starting a brand-new project on it today is starting on a runtime that's almost out of support.
2. **Cut the 5 keyboard layouts from v1.** They are the single biggest source of complexity in the design, they exist only because of the VK-chord injection choice, and they contradict the doc's own "minimal v1 / layout is a v2 problem" stance.
3. **Fix the log-normal test (and the claim behind it).** The Phase 4 assertion that the mean equals the base delay is mathematically wrong; a log-normal is right-skewed.

Details and the full list below.

---

## What's genuinely good (keep it)

- **Right backend category.** `SendInput` is the correct and only reliable injection API on modern Windows. The docs correctly identify the UIPI/integrity-level limitation as an OS constraint, not a bug — most projects discover this the hard way.
- **Right UI framework for the goal.** WPF on .NET for a Windows-only utility is the low-risk, well-supported choice. The rejection of WinUI 3 / MAUI / Avalonia is correctly reasoned: they all buy things you explicitly don't want.
- **Clean, testable separation.** `Core` (no UI deps) / `Native` (injection) / `App` (WPF) / `Tests`. This is what makes the `IKeyboardBackend` fake-able and the timing model unit-testable. Slightly heavy for a single-exe tool, but defensible.
- **Cancellation is designed in from the start.** `CancellationToken` plumbed through the orchestrator + `Escape` hard-stop, and an explicit anti-reference (the project that "locks out all user input") — correctly flagged as an anti-feature. Good instinct.
- **Phased plan with self-checks and definitions of done.** Each phase is independently verifiable. This is the right way to build it.
- **Scope discipline.** The "Non-Goals for v1" list (IME, profiles, macro recording, target-layout auto-detection) is realistic and prevents the usual scope creep.

---

## Findings — fix before / while coding

### F1. Runtime is near end-of-life — target .NET 10 (LTS)
**Severity: High (easy fix).**
The tech-stack table pins **.NET 8.0 (LTS)** with rationale "LTS until Nov 2026." That date is now ~5 months away. **.NET 10 shipped Nov 2025 as the current LTS and is supported through Nov 2028.** ([dotnet support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core), [endoflife.date/dotnet](https://endoflife.date/dotnet))

*Recommendation:* Target `net10.0-windows` (or `net10.0`). It's a one-line `TargetFramework` change with no architectural impact, and you get C# 14 + three more years of support. Bump all the `Microsoft.Extensions.*` packages to the 10.x line to match.

### F2. "Native AOT-ready" is incorrect for a WPF app
**Severity: Medium (corrects a false expectation).**
The tech-stack table says ".NET 8 … single-file publish, native AOT-ready." **WPF does not support Native AOT and trims poorly** — it relies on reflection and runtime XAML/BAML loading that the trimmer can't follow. ([Native AOT overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/))

This matters because it sets a wrong expectation about the shipped binary. Your self-contained single-file `.exe` will be **~60–150 MB and JIT-compiled**, not a small AOT binary, and aggressive trimming is off the table. The plan's own Phase 7 estimate ("< 80 MB") is in the right ballpark — just drop the AOT claim so nobody chases it later.

*Recommendation:* Keep single-file + self-contained; delete "native AOT-ready"; expect 60–150 MB. If size ever matters, framework-dependent publish (require the .NET Desktop Runtime) is the lever, not AOT.

### F3. The five keyboard layouts are over-scope for v1 — and partly self-inflicted
**Severity: Medium-High (scope + architecture).**
This is the design's biggest unnecessary cost. Two things are worth separating:

- **Why the layout tables exist at all.** They're needed *only because* you chose to inject each character as a virtual-key + Shift chord (`Shift+,` → `,`). The alternative — `SendInput` with `KEYEVENTF_UNICODE` (a.k.a. `VK_PACKET`, what `SimulateTextEntry` does) — injects the Unicode codepoint directly and is **layout-agnostic**: no mapping tables, correct on AZERTY/Dvorak/QWERTY alike, with zero per-layout code.
- **Why you might still want VK chords.** Raw-input/DirectInput consumers (mainly **games**) read scancodes and ignore `WM_CHAR`, so Unicode injection won't register there. *But* the docs already list "game anti-cheat compatibility" as a non-goal and note anti-cheat filters `SendInput` regardless — so the primary justification for VK chords is contradicted by your own scope.

So for the v1 target (browsers, editors, chat, text fields), Unicode injection would deliver the actual product — human-paced typing into the focused window — with dramatically less code. The "looks human" win comes almost entirely from **timing**, not from how the keycode is delivered; both injection methods set the `LLKHF_INJECTED` flag anyway, so neither is meaningfully "more real" to software that checks.

*Recommendation (pick one):*
- **Lean v1:** ship Unicode injection for the text path and **US QWERTY only** for the VK-chord path (needed for a few control keys + the "physical key" realism story). Drop UK/AZERTY/Dvorak/Colemak to v2 — exactly where the doc already says target-layout work belongs. This removes four hand-built tables from the critical path.
- **If you keep VK chords as the main path:** still ship **US QWERTY only** in v1. Adding the other four is "mechanical" per the plan, which is precisely why it can wait until someone actually needs them.

Either way, keep `KeyboardLayout` as the abstraction so adding layouts later is cheap — just don't build five up front.

### F4. The log-normal timing test is mathematically wrong
**Severity: Medium (it will mislead you about correctness).**
`plan.md` Phase 4:

```csharp
samples.Mean().Should().BeCloseTo(100, 10);   // log-normal is unbiased
```

A log-normal with `μ = ln(100)`, `σ = 0.35` has:
- **median** = `exp(μ)` = **100**
- **mean** = `exp(μ + σ²/2)` = `100 · exp(0.06125)` ≈ **106.3**

So the *mean* is ~106, not 100 — the distribution is right-skewed, and the comment "log-normal is unbiased" is false. With these numbers the assertion passes only by luck (106.3 is within 100 ± 10); raise σ, or apply the Shift/word-boundary/fatigue multipliers the same test feeds in, and it flips to failing for the wrong reason.

*Recommendation:* Decide what the knob means and test that, not the wrong moment.
- If WPM should be the **median** key interval, sample `exp(Normal(ln(base), σ))` and assert on the **median** (`samples.Order().ElementAt(5000)`), not the mean.
- If WPM should be the **mean**, set `μ = ln(base) − σ²/2` so the mean lands on `base`, then assert the mean.
Pick one explicitly and write the comment to match. Also: the modifier multipliers (×1.15, ×1.5, ×0.7, fatigue) shift the aggregate, so any "mean ≈ base" test must use a neutral context or account for them.

### F5. Timing precision: `Task.Delay` is coarse and drifts
**Severity: Low-Medium.**
The orchestrator loops `await Task.Delay(d)` then injects. On Windows the default timer granularity is ~15.6 ms, so any delay under ~16 ms is unreliable and all delays carry ±~15 ms jitter plus scheduling drift. At 60 WPM (base 200 ms) this is invisible and arguably *adds* realism; for fast profiles or tight bigram bursts (your ×0.7) it becomes a real chunk of the interval, and cumulative drift means a 1000-char run finishes meaningfully later than `EstimatedTime` predicts.

*Recommendation:* Acceptable for v1 — just be aware. If timing fidelity matters, compute each keystroke's *target absolute time* and sleep to that (`delay = targetTime − now`) to kill cumulative drift, and/or raise timer resolution with `timeBeginPeriod(1)` while a typing run is active (and restore it after). Don't over-engineer this in v1.

### F6. No seedable RNG → flaky / unrepeatable tests
**Severity: Low-Medium (testability).**
`DelayModel` and `ErrorModel` are inherently random, but nothing in the design injects the random source. Phase 4's "produces at least one typo at TypoRate=0.5" is a non-deterministic test (fine in practice — `0.5^13` is negligible — but it's a smell), and you can't reproduce a "that run looked weird" report without a seed.

*Recommendation:* Inject `Random` (or a small `IRandomSource`) into the models. Tests pass a fixed seed for determinism; the app passes a time-seeded instance. Cheap now, painful to retrofit.

---

## Findings — minor / factual cleanups

### F7. License is stated inconsistently — verify before committing
The chosen backend `InputSimulatorPlus` is listed as **MS-PL** in §2.1's table and **MIT** in the executive summary and tech-stack table. These can't both be right. The actual `LICENSE.md` on the repo should be checked and the docs made consistent. ([repo LICENSE](https://github.com/kmcnaught/InputSimulatorPlus/blob/master/LICENSE.md)) Either license is fine for your use; the inconsistency just shouldn't survive into the README.

### F8. The chosen dependency is stale — consider owning the P/Invoke
`kmcnaught/InputSimulatorPlus` on NuGet was **last updated in 2022** (~4 years stale). It still works (`SendInput`'s surface hasn't changed), but the whole stack rests on an unmaintained 1-developer fork. Note there are newer-maintained relatives (`H.InputSimulator`, `InputSimulatorPlus2`) if you want a live dependency. ([NuGet](https://www.nuget.org/packages/InputSimulatorPlus))

More to the point: **you already have a `TypeGent.Native` project**, and the slice of `SendInput` you actually need (key down/up, modified keystroke, Unicode entry) is ~100 lines of P/Invoke. Owning it removes the supply-chain risk entirely and gives you direct control over scancode vs. Unicode flags (relevant to F3).

*Recommendation:* Start with the NuGet package to move fast (Phase 2), but treat "inline the `SendInput` P/Invoke into `TypeGent.Native`" as a cheap, low-risk option you can take any time. Given the project is already structured for it, I'd lean toward owning it.

### F9. `EstimatedTime` ignores typos and pauses
`Text.Length / 5 / WPM` is correct for raw WPM but ignores typo→backspace overhead, word-boundary pauses, and fatigue, so the displayed estimate will read low (the real run takes longer). Fine for a label — just don't be surprised when a run overshoots, and consider calling it "≈" in the UI (the mock already does).

### F10. Star counts are noise; maintenance/recency is the real signal
The reference tables lean on GitHub star counts ("1 star fork," "818 stars") as a quality proxy. Stars don't tell you if a SendInput wrapper works — last-commit date, open-issue character, and whether it sets the flags you need do. The evaluations are still directionally right; just don't let "more stars" drive a dependency decision (see F8).

---

## Use-case / ethics note (worth one paragraph, not a blocker)

This is a legitimate dual-use desktop utility — typing into apps that block paste, accessibility, UI testing, demos, and filling fields that reject clipboard input are all fine reasons to build it. Be aware that some framing in the research ("undetectable," "bypass," and the GhostType references) points at uses that may violate a platform's Terms of Service or an exam/proctoring/anti-cheat policy. None of that changes the engineering, and the design's "don't lock out the user / always cancellable" stance is the right ethical default. Just keep the README honest about intended use and don't market "undetectable."

---

## Recommended changes, in priority order

1. **Re-target .NET 10 (LTS).** One line; do it before Phase 1. *(F1)*
2. **Drop AOT language; set size expectations to 60–150 MB.** *(F2)*
3. **Ship US QWERTY only in v1**; keep the `KeyboardLayout` abstraction, defer the other four. Decide Unicode-injection vs VK-chord as the primary text path and write down why. *(F3)*
4. **Fix the log-normal test + its premise** before you trust any timing test. *(F4)*
5. **Inject a seedable RNG** into `DelayModel`/`ErrorModel`. *(F6)*
6. **Reconcile the license** in the docs; **decide NuGet vs. owning the P/Invoke** (lean: own it, since `TypeGent.Native` already exists). *(F7, F8)*
7. Optional polish: drift-corrected timing (F5), "≈" on the estimate (F9).

None of these require re-architecting. The plan is good — these make it current, smaller to ship, and correct in the places the tests would otherwise lie to you.

---

## Sources
- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) · [endoflife.date/dotnet](https://endoflife.date/dotnet)
- [Native AOT deployment overview (WPF/trimming limitations)](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [InputSimulatorPlus on NuGet](https://www.nuget.org/packages/InputSimulatorPlus) · [repo LICENSE](https://github.com/kmcnaught/InputSimulatorPlus/blob/master/LICENSE.md)
