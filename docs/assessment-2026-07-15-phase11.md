# TypeGent v2 — Alignment Assessment (through Phase 11)

**Date:** 2026-07-15
**Scope:** Whole app vs. `planv2.md` (Phases 0–11) and `docs/v2-invariants.md`.
**Verdict:** The engine work is real, well-tested, and faithful to the plan. **But the shipping app
does not actually turn on the last several phases (7, 9, 10, 11).** They live in `TypeGent.Core` and
are covered by unit tests, yet the running WPF app never enables them — so the headline v2
deliverables (keystroke-biometric dwell/flight/rollover, cognitive misspellings) are dark at runtime.

---

## Snapshot

- **Tests:** `dotnet test` → **195 passed / 0 failed** (up from the v1 baseline of 34). Regression
  gate is intact.
- **Build:** clean; `obj/`,`bin/` are correctly untracked in git.
- **Phase coverage in code:** Phases 0–11 are all implemented in Core with dedicated test suites
  (`BiomechanicalTimingTests`, `PreWordPlanningTests`, `ErrorRealismTests`, `DelayedDetectionTests`,
  `CognitiveErrorTests`, `CorpusBigramTests`, `DownUpEventModelTests`, `DwellTimeTests`,
  `DwellFlightRolloverTests`, …).
- **Determinism discipline:** Excellent. Every new RNG consumer (pace, lapse, dwell, rollover,
  misspelling) is gated behind a flag that defaults **off**, with the draw-order impact documented
  inline and in `docs/v2-invariants.md §1`. This is the single hardest thing to keep right across 11
  phases, and it has been kept right.

---

## What's working / aligned

1. **Invariants are documented and genuinely enforced.** `docs/v2-invariants.md` records all four
   v1 contracts; the code honors them. Net-text==input is guarded across 25 seeds × 3 typo rates,
   including the Phase 6 *delayed*-correction generalization and the VK_PACKET fallback path.
2. **Research → code traceability is faithful.** Each phase maps cleanly to its research section
   (§1.1–§3.3): corpus bigram table (Phase 8), biomechanical hand/finger/distance multiplier
   (Phase 3), speed-coupled substitution-dominant error mix (Phase 5), delayed detection (Phase 6).
   The numbers in the code match the plan's targets.
3. **Layered architecture held up.** The down/up fork (Phase 9) was added as an *optional* expansion
   in `TimedAction.HoldMs` + orchestrator, so the legacy atomic path is untouched when the flag is
   off. `KeyAction.KeyDown/KeyUp` flow cleanly through to `InputSimulatorPlusBackend`.
4. **Honest self-documentation.** Where a technique is an approximation (rollover), the code says so
   rather than overclaiming. Good engineering hygiene.

---

## What's off / needs attention

### 1. 🔴 CRITICAL — the app never enables Phases 7, 9, 10, 11
`MainViewModel.RunTypingAsync` (`src/TypeGent.App/ViewModels/MainViewModel.cs:275`) builds the only
runtime `TypingProfile`:

```csharp
var profile = new TypingProfile
{
    Wpm = Wpm, Jitter = Jitter, TypoRate = TypoRate, Fatigue = Fatigue,
    WarmUp = true, Pace = true, LapseRate = 0.005,
};
```

It leaves `DwellEnabled=false`, `RolloverEnabled=false`, `MisspellingRate=0`,
`AutocorrectEnabled=false` at their defaults. Consequences:

- **Phase 10 (dwell), Phase 11 (flight/rollover), and therefore Phase 9's down/up model never
  execute in the real app.** The user gets Phase 1–8 timing/error realism, but *zero* of the
  keystroke-biometric layer — which is the stated purpose of Phases 9–11.
- **Phase 7 cognitive misspellings never fire** (`MisspellingRate` stays 0).
- Multiple code comments assert *"Defaults to false … the app enables it."* That statement is now
  **inaccurate** for dwell, rollover, and misspelling — nothing in the app enables them.

This is the core of "is everything aligned to the goal": the *code* is aligned, but the *product*
silently ships without the four most recent phases. Phases marked done-by-unit-test are not
delivered end-to-end.

**Recommendation:** enable these in the runtime profile with sensible defaults (even before the
Phase 12 persona UI lands), or explicitly document that Phases 9–11 are staged-but-gated pending
Phase 12. At minimum, correct the "the app enables it" comments so they don't mislead.

### 2. 🟠 Phase 9–11 controlled-target verification likely not done in-app
`v2-invariants.md §4` explicitly names Phases 9–11 as the ones that **must** be verified against a
controlled (non-Notepad) target, because the unit test only proves *logical* stream correctness, not
event-rate delivery. Since the app never enables dwell/rollover, that end-to-end check can't have
happened through the app. The phase self-checks that claim "verified" should say against *what*
target. Worth an explicit manual verification before calling 9–11 truly done.

### 3. 🟠 Phase 11 rollover is a zero-gap approximation, not true negative flight
The plan's Phase 11.2 goal is overlap — "next key pressed *before* the previous is released
(→ negative flight)." The implementation instead emits `KeyUp(prev)` then `KeyDown(cur)` with a
**~0 ms** gap (`RolloverFlightMs = 0.0`), i.e. sequential and never overlapping. It's honestly
documented as "the closest approximation without event reordering," and the test measures the
zero-delay fraction — so the suite passes — but a biometric analyzer would see **no** true key
overlap. Against a keystroke-biometric detector (the explicit motivation for this phase), zero-gap
and negative-gap are distinguishable. Flag Phase 11.2 as **partially** met, or implement real
overlap by reordering the down of key N+1 ahead of the up of key N in the emitted stream.

### 4. 🟡 `MisspellingDictionary` data smells (minor, non-blocking)
- **Duplicate keys**, silently overwritten (indexer init, so no crash): `"occurrence"` (→ ends as
  `"occurrance"`, first `"occurence"` entry dead) and `"stationary"` (both `"stationery"`). Dedupe.
- Several entries are **wrong-word homophones**, not misspellings: `accept↔except`,
  `affect↔effect`, `weather`/`whether` both → `"wether"`. Net-text==input still holds (they're
  corrected), but they'll produce semantically odd "type the wrong word, then fix it" sequences.
  Decide if that's intended for Phase 7 or should be pruned to true orthographic misspellings.
- No-op guard (`quite`, `resistance`) is handled correctly by `TryGet`.

### 5. 🟡 README is stale relative to v2
`README.md` still says **"v1 complete … 34 unit tests,"** describes typos as
**"immediately backspaces and fixes each one"** (Phase 6 added *delayed* correction), and files all
of Phases 1–11 under **"Roadmap (v2 candidates)"** as out of scope. Fine while v2 is mid-flight, but
it no longer reflects the codebase. Refresh when v2 reaches a shippable milestone.

---

## Suggested next actions (in priority order)

1. **Wire Phases 7/9/10/11 into `RunTypingAsync`** (or a default persona), so the work is actually
   exercised — then do the controlled-target verification §4 requires. *(Unblocks the real goal.)*
2. **Decide on true rollover overlap** vs. accepting the documented zero-gap approximation; update
   the Phase 11 success criterion wording either way.
3. **Dedupe + sanity-pass `MisspellingDictionary`** (drop or intentionally keep the homophones).
4. **Fix the "the app enables it" comments** in `TypingProfile`/`DelayModel`/`HumanTypingEngine`.
5. **Refresh `README.md`** to reflect v2 progress and the 195-test suite.

Phase 12 (personas + saved profiles) is the natural home for items 1 and 4 — the persona presets are
where all these knobs get turned on coherently. If Phase 12 is imminent, the acceptable interim is to
explicitly note in `planv2.md` that Phases 9–11 are "implemented & unit-verified, runtime-gated
pending Phase 12," rather than leaving the misleading "the app enables it" comments in place.
