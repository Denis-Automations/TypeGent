# TypeGent v2 – Invariants (Ground Rules)

The four contracts v1 established. **Every v2 phase must keep them, or it isn't shippable.** These
are not suggestions — they are the load-bearing assumptions the test suite and the human-likeness
guarantees depend on. New code is built *on top* of them, never around them.

Companion to `planv2.md` (Phase 0). Each section below names the contract, the code that enforces it,
the test that guards it, and what future phases need to watch for.

---

## 1. Single injected `Random` (deterministic, stable draw order)

**Contract.** All randomness flows from one `Random` instance threaded through
`HumanTypingEngine` → `DelayModel` / `ErrorModel`. Never `new Random()` inside a model. The RNG
**draw order must stay stable** so a fixed seed reproduces an entire plan byte-for-byte — the seeded
tests depend on it. Adding a draw in the wrong place silently breaks reproducibility.

**Enforcing code.**
- `HumanTypingEngine(Random rng)` — the single entry point; the same `_rng` is passed into both
  `new DelayModel(_rng, …)` and `new ErrorModel(_rng)`. (`src/TypeGent.Core/HumanTyping/HumanTypingEngine.cs`)
- `DelayModel` and `ErrorModel` both store the injected `_rng` and draw only from it.
- `DelayModel.NextGaussian()` deliberately consumes **two** uniforms per call with no cached spare,
  so the consumption order is trivially predictable — a future optimization that caches a Box–Muller
  spare would *change the draw order* and break determinism. Don't do that without re-running the
  reproducibility test. (`src/TypeGent.Core/HumanTyping/DelayModel.cs`)

**Guarding test.** `HumanTypingEngineTests.SameSeed_ReproducesIdenticalPlan` — two engines built
from `new Random(99)` must produce identical action lists.

**What v2 phases must watch.** Phase 2 (AR(1) pace) makes `DelayModel` *stateful* — the pace scalar
is drawn from the RNG, so it is a new consumer. Insert its draw at a stable, documented point in the
per-character sequence and keep it there. Phase 5 (speed-coupled typo rate) and Phase 10 (dwell)
add further draws; each must slot into a fixed position in the draw order. When in doubt: re-run
`SameSeed_ReproducesIdenticalPlan` after every change that touches the RNG.

---

## 2. Net typed text == input

**Contract.** Every error is net-corrected — the reconstructed text from the action stream
(backspace pops the last char) always equals the input. Phase 6 (delayed detection) is the only
phase that stresses this; it must **generalize** the reconstruction test to tolerate displaced
corrections, never drop the invariant.

**Enforcing code.**
- `HumanTypingEngine.Plan` — each typo branch emits the wrong keystrokes **and** the full
  correction (backspaces + retype) before advancing `i`, so the cursor always lands back on the
  correct character. (`src/TypeGent.Core/HumanTyping/HumanTypingEngine.cs`)
- Corrections are plain `KeyAction.Press(VirtualKey.Back)` inside the existing `TimedAction`
  stream — no separate action type.

**Guarding tests.**
- `HumanTypingEngineTests.NetTypedText_AlwaysEqualsInput_RegardlessOfTypoRate` — 25 seeds × typo
  rates 0 / 0.5 / 1.0; reconstructs via the `Reconstruct` helper and asserts equality.
- `HumanTypingEngineTests.OutOfLayoutChars_StillRoundTripViaFallback` — the invariant holds even
  for `VK_PACKET` fallback characters.

**What v2 phases must watch.** Phase 6 introduces delayed corrections (emit N correct chars, then a
backspace burst back to the error, then retype). The reconstruction test must be **generalized** to
handle this — backspace still pops, so the *math* is unchanged, but the test's expectations about
"immediate" correction no longer hold and need widening. Phase 7 (cognitive errors / autocorrect)
adds a bulk word-replace path that must also preserve net text. Any new error kind ships with a
net-text assertion.

---

## 3. US QWERTY remains the default; new layouts sit behind the abstraction

**Contract.** US QWERTY is the v1 default and stays the default in v2. New layouts (Phase 13) are
added as `KeyboardLayout` subclasses and must not regress US QWERTY behavior.

**Enforcing code.**
- `KeyboardLayout` (abstract) — `CanMap`, `MapChar`, `NeedsShift`, `SupportedChars`, and the shared
  `ToAction` (VK-chord for mappable chars, `KeyAction.Text` / `VK_PACKET` fallback otherwise).
  (`src/TypeGent.Core/Layouts/KeyboardLayout.cs`)
- `UsQwertyLayout` — the only v1 layout; the UI dropdown ships it as the sole entry until Phase 13.
  (`src/TypeGent.Core/Layouts/UsQwertyLayout.cs`)

**Guarding tests.** `UsQwertyLayoutTests` — representative characters incl. the real-comma rule and
out-of-layout fallback routing.

**What v2 phases must watch.** Phase 13 adds UK QWERTY, AZERTY, Dvorak, Colemak as subclasses,
each also supplying the Phase 3 biomechanical metadata (hand/finger/row/position). US QWERTY's
existing mappings and tests must stay untouched. Phase 14 (auto-detect) selects a layout from the
target window but must fall back to the user pick — never silently change the default.

---

## 4. Verify against a controlled target, not Notepad

**Contract.** The Windows 11 Notepad control can drop or scramble *extremely rapid* `SendInput`
bursts (observed only in an aggressive harness at 110 WPM + 35% typo rate; at the human default of
~200 ms/char it behaves correctly). This is a Notepad-side limitation, not an engine bug — the
engine's keystroke stream is provably correct via the reconstruction test. Timing- and event-rate
work — **especially Phases 9–11** (down/up events, dwell, flight/rollover, which push rapid
back-to-back events) — must be verified against a target that doesn't drop bursts.

**Enforcing practice.**
- The `Reconstruct` test proves the *logical* correctness of the stream independent of any target.
- Manual / end-to-end verification of timing-sensitive phases uses a controlled target (e.g. a
  custom input-receiving test window, or a target known not to coalesce rapid `SendInput`), not
  Windows 11 Notepad. Notepad remains fine for everyday manual smoke tests at human pacing.

**What v2 phases must watch.** Phases 9–11 schedule independent down/up events with short holds and
negative-flight rollover — the very pattern Notepad coalesces. Validate those against a controlled
target and record the result in the phase's self-check, not against Notepad alone.

---

## Process rule: one branch per phase

One git branch per phase, merged only after that phase's self-check passes (carried from v1
`plan.md` Phase 0). No phase is merged on assumption.

---

## Regression gate

The v1 test suite (34 tests across `DelayModel`, `ErrorModel`, `UsQwertyLayout`,
`TypingOrchestrator`, and `HumanTypingEngine`) must stay green at the end of **every** phase. Run
before merging:

```powershell
dotnet test
```

If a v2 phase legitimately changes an existing test's expectations (Phase 6 generalizing the
reconstruction test is the expected case), the test is *updated and kept green* — never deleted to
make the suite pass.
