# TypeGent v2 – Phased Build Plan

A step-by-step roadmap for the **v2** feature set, picking up where the shipped v1 left off. Same
structure as `plan.md`: every phase has a goal, implementation notes, a self-check, success
criteria, and a definition of done — so each is independently verifiable before moving on.

> Companion to:
> - `plan.md` (the v1 build — Phases 0–7 shipped, Phase 8 listed deferred items)
> - `Humanlike Typing – Enhancement Research.md` (the keystroke-dynamics research these phases
>   implement; section refs like "§2.1" point there)
>
> **Ordering (per user):** the human-like typing techniques come first, sequenced **easy → hard**
> (Phases 1–12), then the remaining deferred features — additional keyboard layouts, layout
> auto-detect, IME, tunable-curve UI, telemetry (Phases 13–17).
>
> **What carries over from `plan.md` Phase 8:** delayed-detection typos (→ Phase 6),
> cognitive/linguistic errors (→ Phase 7), inverse-distance neighbour weighting (→ Phase 5),
> corpus/Markov bigrams (→ Phase 8), tunable curves (→ Phase 16), multiple saved profiles
> (→ Phase 12 personas), plus additional layouts / auto-detect / IME / telemetry (Phases 13–17).

---

## Phase 0 – Ground Rules (invariants carried from v1)

Everything below must hold the contracts v1 established, or it isn't shippable.

- **Single injected `Random`.** Any new randomness draws from the one `Random` threaded through
  `HumanTypingEngine` → `DelayModel` / `ErrorModel`. Never `new Random()` inside a model. The RNG
  **draw order must stay stable** so a fixed seed still reproduces an entire plan (the seeded tests
  depend on it — adding a draw in the wrong place silently breaks reproducibility).
- **Net typed text == input.** Every error is still net-corrected. Phase 6 (delayed detection) is
  the only phase that stresses this; it must *generalize* the reconstruction test, not drop the
  invariant.
- **US QWERTY only remains the v1 default.** New layouts (Phase 13) sit behind the existing
  `KeyboardLayout` abstraction and don't regress US QWERTY.
- **Verify against a controlled target, not Notepad.** The Windows 11 Notepad fast-burst limitation
  (plan.md Phase 4 note) still applies; timing/event-rate work (esp. Phases 9–11) must be verified
  against a target that doesn't drop rapid `SendInput` bursts.
- **One branch per phase**, merged only after its self-check passes.

**Success criteria (all must pass):**

- [x] A `docs/v2-invariants.md` (or a section in the README) records the four contracts above.
- [x] The existing 34 v1 tests stay green at the end of every phase (no regression gate).

---

## Phase 1 – Timing quick wins (Tier 1 bundle)

**Goal:** Land the five cheap, high-value timing refinements from research Tier 1 — all inside
`DelayModel` / `TypingContext` / `TypingProfile`, no new action types, no backend change.

### 1.1 Expand the bigram table (fast + slow sets)
`DelayModel.CommonBigrams` grows from 14 → ~50 top English bigrams (×0.7), and gains an optional
*slow* set of rare/awkward pairs (×1.1–1.2). Pure data. (§1.1)

### 1.2 Punctuation & sentence-boundary pauses
Add boundary multipliers keyed on `ctx.PreviousChar`: `, ; :` → ×1.8; `. ! ?` → ×2.5–4;
`\n` (paragraph) → ×5+. Scales by boundary rank. (§1.2)

### 1.3 Warm-up ramp
Add an inverse-of-fatigue early multiplier that decays to 1.0, e.g.
`×(1 + 0.20·exp(-CharsTypedSoFar/40))`. Reuses `CharsTypedSoFar`. (§1.3)

### 1.4 Shifted log-normal (physiological floor)
Replace the hard `MinDelayMs = 20` clamp with a 3-parameter shifted log-normal:
`floor + exp(μ + σ·Z)`, `floor ≈ 45 ms`, μ set so the median still tracks the WPM base. Adjust
`DelayModel_Median_TracksBaseDelay`. (§1.4)

### 1.5 Attention lapses
With small per-char probability (0.3–0.8%) inject a one-off 1.5–4 s pause, text-independent, drawn
from the RNG. (§1.5)

### 1.6 Self-check for Phase 1
- Type a 200-char paragraph into a controlled target; pace visibly pauses at `.`/`,`, starts
  slightly slow, and occasionally stalls (lapse).
- Unit tests: median still ≈ base after the shifted-log-normal change; a seeded plan reproduces
  identically; boundary multipliers apply for representative punctuation.

**Success criteria (all must pass):**

- [x] Bigram table has ~50 fast entries (+ optional slow set); a comma/period/newline each lengthen
      the following delay; warm-up shortens delays over the first ~40 chars; a shifted log-normal
      replaces the 20 ms clamp with a ~45 ms floor; lapses fire at the configured rate.
- [x] `DelayModel` median-tracks-base test still passes; seeded reproducibility test still passes.
- [x] All v1 tests green.

**Definition of done:** Tier-1 timing refinements merged; timing looks visibly more natural on a
200-char paragraph; determinism and median invariants intact.

---

## Phase 2 – Autocorrelated pace & burst-and-pause (Tier 2 core)

**Goal:** Kill the i.i.d. "machine" tell. Real IKIs are positively autocorrelated (Hurst
0.5 < H < 1) — pace drifts in runs. This is the single highest realism-per-line change. (§2.1, §2.2)

### 2.1 AR(1) pace state
`DelayModel` becomes stateful (or the engine threads a `pace` scalar through `TypingContext`):
`pace_t = 0.9·pace_{t-1} + 0.1·N(1, small)`, then `delay *= pace_t`. This subsumes warm-up/fatigue
into one slow-varying envelope. **Guard the RNG draw order** so seeded plans still reproduce.

### 2.2 Burst-and-pause (mostly emergent)
With AR(1) drift (2.1) + boundary pauses (1.2) in place, bursts are the low-variance runs between
injected pauses. Optionally define a pause threshold ≈ 4× median IKI to make bursts explicit for a
future preview/telemetry view.

### 2.3 Self-check for Phase 2
- Log IKIs for a 500-char run; the lag-1 autocorrelation is clearly positive (not ~0 as with i.i.d.).
- Seeded plan still reproduces byte-for-byte identically.

**Success criteria (all must pass):**

- [x] Measured lag-1 autocorrelation of inter-key delays is significantly > 0 with pace enabled,
      ≈ 0 with it disabled.
- [x] Same seed → identical plan (draw-order stability preserved).
- [x] All prior tests green.

**Definition of done:** Pace is autocorrelated and tunable; the "randomly rerolled every key" signature
is gone; determinism preserved.

---

## Phase 3 – Biomechanical bigram timing (layout metadata)

**Goal:** Make key-to-key variation reflect the fingers involved — the biggest *believability* jump.
Measured means: hand-alternation ≈ 114 ms (fastest), same-hand-different-finger ≈ 131 ms, same-finger
≈ 157 ms (slowest). (§2.4)

### 3.1 Layout metadata
Annotate `UsQwertyLayout` keys with `(hand, finger, row, x, y)`. Expose via `KeyboardLayout` so other
layouts (Phase 13) can supply their own.

### 3.2 Relationship multiplier
In `DelayModel`, derive a multiplier from prev→current key: alternating-hand = 1.0 (baseline),
same-hand-different-finger ≈ 1.15×, same-finger ≈ 1.35×, double-letter ≈ 1.35×, plus a small term
∝ Euclidean key distance (row jumps).

### 3.3 Self-check for Phase 3
- Unit test: same-finger digraph (e.g. `ed`→same finger cases) samples slower than an alternating-hand
  digraph at equal base delay, over many seeds (compare medians).

**Success criteria (all must pass):**

- [x] Layout exposes per-key hand/finger/row/position metadata for US QWERTY.
- [x] Same-finger and same-hand bigrams are measurably slower than alternating-hand bigrams; distance
      adds latency for row jumps.
- [x] All prior tests green.

**Definition of done:** Per-bigram timing is driven by real biomechanics via layout metadata, not guesswork.

---

## Phase 4 – Pre-word planning latency

**Goal:** Replace the flat ×1.5 post-space multiplier with a planning pause scaled by the *upcoming*
word's length and (optionally) rarity. (§2.3)

### 4.1 Lookahead
The engine already scans char-by-char; add a peek to the next word boundary to get the upcoming word's
length. Longer word → bigger pre-word pause.

### 4.2 Optional word-frequency weighting
A small common-word set (or frequency list) → rare words get a larger planning pause; common short words
get a smaller one.

### 4.3 Self-check for Phase 4
- Delays after a space are larger before long/rare words than before short/common ones (measured over
  a mixed paragraph).

**Success criteria (all must pass):**

- [x] Post-space delay scales with next-word length (and rarity if the list ships).
- [x] Determinism preserved; all prior tests green.

**Definition of done:** Word-boundary pauses reflect planning load, not a constant multiplier.

---

## Phase 5 – Error realism (mix, speed-coupling, inverse-distance)

**Goal:** Make the *when* and *what* of typos match measured data. Substitution dominates (~10× the
others); error probability rises with speed. Includes the `plan.md` Phase 8 inverse-distance
neighbour-weighting item. (§2.6)

### 5.1 Re-weight the error mix
`ErrorModel.ChooseKind` weights move toward the measured mix (substitution-dominant: sub ≈ 3.5%, insert
≈ 0.25%, omit ≈ 0.17%, transpose ≈ 0.07% for young adults) rather than hand-picked values.

### 5.2 Speed-coupled typo rate
Multiply the per-char typo probability by the current pace (from Phase 2) — faster bursts produce more
errors (speed–accuracy tradeoff).

### 5.3 Inverse-distance neighbour weighting
`ErrorModel.AdjacentKey` weights closer physical neighbours higher (uses the Phase 3 key positions),
replacing v1's uniform pick. *(plan.md Phase 8 item.)*

### 5.4 Self-check for Phase 5
- Over many seeds, substitution ≫ other kinds; typo count rises when WPM/pace rises; adjacent picks bias
  toward nearer keys.

**Success criteria (all must pass):**

- [x] Error-kind distribution matches the target mix within tolerance over N seeds.
- [x] Higher pace yields a higher measured typo count.
- [x] Adjacent-key substitution is distance-weighted.
- [x] Net typed text still == input; all prior tests green.

**Definition of done:** Error frequency, kind, and target-key selection are data-driven and speed-coupled.

---

## Phase 6 – Delayed error detection (omission / missing-double)

**Goal:** Add corrections that are noticed *several characters later* — backspace burst back to the
error, then retype. Unlocks the `plan.md` Phase 8 omission / missing-double typos (`wat`→`want`,
`commitee`), which are delayed-detection by nature. In research, delayed corrections outnumber
immediate ones (~0.63 vs ~0.40 per sentence). (§2.5)

### 6.1 Delayed-correction state machine
The engine's per-char `switch` gains a mode: emit N more correct chars after the error, then a backspace
burst (its own fast rhythm) back to the error position, then retype. Net text still == input, but the
correction is displaced.

### 6.2 Omission / missing-double typos
With delayed detection available, add the two deferred kinds: dropped char and dropped double letter,
each corrected on later detection.

### 6.3 Self-check for Phase 6
- Reconstruct net text from the action stream (backspaces pop) across many seeds × typo rates 0/0.5/1.0
  — must equal input, *including* displaced corrections.
- A seeded plan contains at least one delayed correction (error, then ≥1 correct char, then a backspace
  burst).

**Success criteria (all must pass):**

- [x] Net-text-equals-input reconstruction test **generalized** to tolerate delayed corrections and green.
- [x] Omission and missing-double kinds emit and self-correct.
- [x] Backspace bursts type at a realistic (fast, repeated-key) rhythm.
- [x] All prior tests green.

**Definition of done:** Corrections can be immediate or delayed; omission/double typos ship; the
net-text invariant holds under displaced corrections.

---

## Phase 7 – Cognitive / linguistic errors (misspellings + autocorrect)

**Goal:** Move beyond finger-mechanics to *knowledge* errors: learned misspellings (`recieve`) and an
optional autocorrect simulation. *(plan.md Phase 8 item — needs a dictionary/word list.)*

### 7.1 Misspelling dictionary
Ship a small curated common-misspellings map (`receive`→`recieve`, etc.). With low probability, type the
misspelling, then correct it (immediate or delayed per Phase 6).

### 7.2 Autocorrect simulation (optional)
Simulate an autocorrect pass that fixes a slip a beat after it's typed (distinct from human backspacing —
a bulk replace of the word).

### 7.3 Self-check for Phase 7
- A seeded run over text containing a known-tricky word occasionally produces the misspelling-then-fix
  sequence; net text == input.

**Success criteria (all must pass):**

- [x] Misspelling dictionary drives occasional cognitive errors, all net-corrected.
- [x] Autocorrect simulation (if shipped) is toggleable and distinct from manual correction.
- [x] All prior tests green.

**Definition of done:** Cognitive misspellings (and optional autocorrect) join the mechanical typos, all
respecting net-text == input.

---

## Phase 8 – Corpus-driven / Markov bigram timing

**Goal:** Replace the hard-coded common-bigrams list with tables **learned from a corpus** — a proper
bigram (and optionally trigram/Markov) timing/frequency model. *(plan.md Phase 8 item.)*

### 8.1 Corpus-derived bigram frequencies
Build a bigram frequency table from a bundled corpus; map frequency → timing multiplier (frequent pairs
faster) rather than the ~50 hand-listed pairs from Phase 1.

### 8.2 Optional Markov structure
Extend to trigrams / a small Markov model if it measurably improves realism; keep it a static shipped
asset (no runtime corpus dependency).

### 8.3 Self-check for Phase 8
- Bigram multipliers derived from the table reproduce (roughly) the Phase 1 fast set and add many more;
  timing on natural text is smoother than the hand-listed version.

**Success criteria (all must pass):**

- [x] Bigram timing comes from a corpus-derived table shipped as a static asset.
- [x] Determinism and net-text invariants intact; all prior tests green.

**Definition of done:** Data-driven bigram (Markov) timing supersedes the hand-coded list.

---

## Phase 9 – Down/Up event model (architectural fork)

**Goal:** The prerequisite for all keystroke-*biometric* realism (Phases 10–11). Move from "one press
per key" to independently scheduled **key-down** and **key-up** events. Do this only if defeating
keystroke biometrics is a goal — visual realism is already covered by Phases 1–8.

### 9.1 Action-model change
Extend `KeyAction`/`TimedAction` (or add a parallel event type) so a keystroke carries a **hold
duration**, and the orchestrator can schedule down and up separately.

### 9.2 Backend change
`InputSimulatorPlusBackend` emits `SimulateKeyDown` / `SimulateKeyUp` (InputSimulator exposes both)
instead of only `SimulateKeyPress`. Preserve the Unicode `VK_PACKET` fallback path.

### 9.3 Self-check for Phase 9
- A single key round-trips as a discrete down then up with the intended hold gap into a controlled target.
- Orchestrator cancellation still halts within one event.

**Success criteria (all must pass):**

- [x] Keystrokes emit as separate down/up events with a schedulable hold; VK_PACKET fallback unaffected.
- [x] Cancellation (Stop/Escape) still halts promptly.
- [x] All prior tests migrated/green.

**Definition of done:** The engine can express and the backend can deliver independent key-down/key-up
events — the foundation for dwell/flight/rollover.

---

## Phase 10 – Dwell time (key hold duration)

**Goal:** Model how long each key is physically held (~60–120 ms), near-Gaussian and weakly key-dependent —
a primary keystroke-biometric feature. (§3.1)

### 10.1 Dwell sampling
Sample hold time per key from a near-normal distribution (small σ), feed it into the Phase 9 hold slot.

### 10.2 Self-check for Phase 10
- Measured hold times cluster ~60–120 ms with low skew; distinct from the between-key gaps.

**Success criteria (all must pass):**

- [x] Per-key dwell is sampled from a tight near-normal distribution and delivered by the backend.
- [x] Determinism preserved; all prior tests green.

**Definition of done:** Keys are held for a realistic, near-Gaussian dwell time.

---

## Phase 11 – Dwell + flight decomposition & rollover

**Goal:** Split the inter-key interval into `dwell(N) + flight(N→N+1)`, with flight the heavy-tailed
(log-normal) part; then add **rollover** — fast typists press the next key before releasing the previous
(40–70% overlap → negative flight). (§3.2, §3.3)

### 11.1 Decomposition
Derive down/up timestamps from a near-normal dwell + a log-normal flight so that down-down, up-up, down-up,
up-down latencies are all realistic.

### 11.2 Rollover (negative flight)
For fast personas, with 0.4–0.7 probability on easy alternating bigrams, overlap events (next down before
prior up).

### 11.3 Self-check for Phase 11
- With rollover on, a measurable fraction of keystrokes overlap (negative flight); with it off, none do.
- All four latency families (dd/uu/du/ud) have plausible distributions.

**Success criteria (all must pass):**

- [x] IKI decomposes into dwell + flight; flight is heavy-tailed, dwell near-normal.
- [x] Rollover produces the configured overlap fraction for fast personas.
- [x] All prior tests green.

**Definition of done:** Timing is expressed as dwell + flight with realistic rollover — full
keystroke-biometric realism.

---

## Phase 12 – Persona presets & multiple saved profiles

**Goal:** Bundle all the knobs (Phases 1–11) into coherent personas, and let users save/switch multiple
named profiles. Combines research §3.4 with the `plan.md` Phase 8 "multiple profiles" item.

### 12.1 Personas
A `TypingPersona` sets `TypingProfile` + the new tables in one pick: e.g. hunt-and-peck (slow, high
substitution, big pre-word pauses, little rollover), average ~52 WPM, fast touch-typist 100+ WPM (high
rollover, tight σ, low error), mobile/elderly (omission-skewed). Seed error mixes from the research
demographics.

### 12.2 Saved profiles
Extend the v1 `JsonSettingsStore` to persist multiple named profiles under `%AppData%\TypeGent\`; add a
profile dropdown and an optional hotkey to cycle profiles.

### 12.3 Self-check for Phase 12
- Selecting a persona visibly changes pace/error character; profiles round-trip across restarts; the
  profile-switch hotkey works.

**Success criteria (all must pass):**

- [ ] At least 3 personas ship and are visibly distinct.
- [ ] Multiple named profiles save/load; a hotkey cycles them.
- [ ] All prior tests green.

**Definition of done:** One-click personas and persistent multi-profile support ship.

---

## Phase 13 – Additional keyboard layouts

**Goal:** Add UK QWERTY, AZERTY, Dvorak, Colemak behind the existing `KeyboardLayout` abstraction —
mechanical translations of the US QWERTY table, each also supplying Phase 3 biomechanical metadata.
*(plan.md Phase 8 item.)*

### 13.1 Layout tables
One `KeyboardLayout` subclass per layout (VK map, shift map, supported chars, and hand/finger/position
metadata). US QWERTY stays the default.

### 13.2 UI
Populate the layout dropdown (v1 shipped it US-QWERTY-only).

### 13.3 Self-check for Phase 13
- Each layout types a representative string correctly (incl. shifted symbols) into a controlled target;
  unit tests spot-check ~20 chars per layout.

**Success criteria (all must pass):**

- [ ] UK QWERTY, AZERTY, Dvorak, Colemak each type correctly with shift variants and Unicode fallback.
- [ ] Layout dropdown lists all shipped layouts; US QWERTY unchanged.
- [ ] Per-layout unit tests green; all prior tests green.

**Definition of done:** Four additional layouts ship behind the abstraction with biomechanical metadata.

---

## Phase 14 – Auto-detect the target app's keyboard layout

**Goal:** Detect the foreground window's active input layout and select the matching `KeyboardLayout`
automatically, instead of relying on the user's dropdown pick. *(plan.md Phase 8 item.)*

### 14.1 Detection
On hotkey/Start, read the target thread's keyboard layout (`GetKeyboardLayout` via the foreground
window's thread id) and map it to a shipped `KeyboardLayout`; fall back to the user pick if unknown.

### 14.2 Self-check for Phase 14
- Switching the OS layout and targeting an app auto-selects the right layout; unknown layouts fall back
  gracefully with a status message.

**Success criteria (all must pass):**

- [ ] The active target layout is detected and matched when supported; graceful fallback otherwise.
- [ ] User can still override; all prior tests green.

**Definition of done:** Layout selection can follow the target app automatically.

---

## Phase 15 – IME support (CJK, Thai, etc.)

**Goal:** Type non-Latin scripts via the Unicode `VK_PACKET` path (and/or IME composition) — CJK, Thai,
and similar. *(plan.md Phase 8 item.)*

### 15.1 Approach
Route unmapped scripts through `SimulateTextEntry` (VK_PACKET) as v1 already does for accented chars, and
evaluate whether true IME composition (sending composition strings) is needed for target apps that don't
accept VK_PACKET. Document the DirectInput/raw-input caveat.

### 15.2 Self-check for Phase 15
- Representative CJK/Thai strings appear correctly in a Unicode-aware target.

**Success criteria (all must pass):**

- [ ] CJK/Thai text types correctly via the fallback (and/or IME) path.
- [ ] Caveats documented; all prior tests green.

**Definition of done:** Non-Latin scripts type correctly in Unicode-aware targets.

---

## Phase 16 – Tunable delay-curve UI

**Goal:** Let users *draw* / shape the inter-key delay distribution in the UI instead of only setting
scalar jitter. *(plan.md Phase 8 item.)*

### 16.1 Curve editor
A WPF control to shape the timing distribution (e.g. adjust σ, skew, boundary-pause magnitudes, or a
freehand curve) bound into `TypingProfile`; persisted via the settings store.

### 16.2 Self-check for Phase 16
- Editing the curve visibly changes typed pace; the shape persists across restarts.

**Success criteria (all must pass):**

- [ ] Users can shape the delay distribution and see it reflected in typing.
- [ ] The curve persists; all prior tests green.

**Definition of done:** The delay distribution is user-tunable beyond a single jitter slider.

---

## Phase 17 – Telemetry (opt-in only)

**Goal:** Optional, explicitly opt-in telemetry — never phoning home silently. *(plan.md Phase 8 item.)*

### 17.1 Approach
Opt-in toggle (default off), transparent about what's collected (e.g. anonymized realism metrics, crash
info), local-first with clear disclosure; no data leaves the machine without consent.

### 17.2 Self-check for Phase 17
- With telemetry off (default), nothing is sent; with it on, only the disclosed data is sent, after
  explicit consent.

**Success criteria (all must pass):**

- [ ] Telemetry is off by default and requires explicit opt-in.
- [ ] Collected fields are disclosed; nothing sent when off.
- [ ] All prior tests green.

**Definition of done:** Opt-in telemetry ships with clear disclosure and an off-by-default posture.

---

## Summary Table

| Phase | Theme | What you'll have at the end |
|---|---|---|
| 0 | Invariants | v1 contracts documented and gated (single RNG, net-text==input, controlled-target verify) |
| 1 | Timing quick wins | Bigger bigram table, punctuation/sentence pauses, warm-up, shifted log-normal, lapses |
| 2 | Autocorrelated pace | Pace drifts in runs (Hurst-like); i.i.d. tell gone |
| 3 | Biomechanical bigrams | Hand/finger/distance-driven per-key timing via layout metadata |
| 4 | Pre-word planning | Post-space pause scales with next-word length/rarity |
| 5 | Error realism | Substitution-dominant mix, speed-coupled rate, inverse-distance neighbours |
| 6 | Delayed detection | Displaced corrections; omission/missing-double typos |
| 7 | Cognitive errors | Misspelling dictionary + optional autocorrect sim |
| 8 | Corpus/Markov bigrams | Data-driven bigram (Markov) timing table |
| 9 | Down/up event model | Independent key-down/key-up with hold (architectural fork) |
| 10 | Dwell time | Near-Gaussian key hold durations |
| 11 | Dwell+flight+rollover | Full biometric timing incl. negative-flight rollover |
| 12 | Personas & profiles | One-click personas + multiple saved profiles + switch hotkey |
| 13 | More layouts | UK QWERTY, AZERTY, Dvorak, Colemak |
| 14 | Layout auto-detect | Follows the target app's active layout |
| 15 | IME support | CJK/Thai via VK_PACKET / IME |
| 16 | Tunable curves | User-shaped delay distribution in the UI |
| 17 | Telemetry | Opt-in, off by default, fully disclosed |
