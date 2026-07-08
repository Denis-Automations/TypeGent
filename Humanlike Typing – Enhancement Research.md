# TypeGent – Human-Like Typing: Enhancement Research

Techniques to push the `HumanTypingEngine` beyond its current four knobs (speed, jitter, typo
rate, fatigue) toward genuinely human keystroke dynamics. Ordered **easy → hard** by
implementation cost, with a concrete integration note against the code we actually have.

> Companion to `plan.md` (Phase 4 built the current engine). Grounded in keystroke-dynamics
> research — chiefly the Aalto **136-million-keystroke** study (Dhakal et al., CHI 2018), the
> **Typoist** error-simulation paper (arXiv 2502.03560), and free-text IKI distribution work
> (PMC8606350). Sources are linked inline.

---

## Where we are today

What the engine (`src/TypeGent.Core/HumanTyping/`) already does — this is a solid base, and
several items below are refinements of it rather than net-new systems:

| Aspect | Current implementation | File |
|---|---|---|
| Inter-key timing | Log-normal, **median = WPM base delay**, σ = jitter (Box–Muller) | `DelayModel.cs` |
| Timing modifiers | ×1.15 shift, ×1.5 after space, ×0.7 for 14 common bigrams, fatigue drift | `DelayModel.cs:43-46` |
| Fatigue | `×(1 + 0.0005·charsTyped)` — monotonic slowdown | `DelayModel.cs:46` |
| Errors | 4 mechanical typos (adjacent slip, shift-mistime, transposition, repeated key) | `ErrorModel.cs` |
| Correction | **Always immediate**: wrong key → backspace → right key, net text == input | `HumanTypingEngine.cs:57-113` |
| Determinism | One injected `Random` seeds the whole plan | `HumanTypingEngine.cs:16` |
| Output | `IEnumerable<TimedAction>` = `(TimeSpan delay, KeyAction)` — **one press per key** | `Typing/TimedAction` |

**Two structural facts that shape everything below:**

1. **We only emit key *presses*, not down/up events.** `TimedAction` carries a single delay
   *before* a `KeyAction`. There is no concept of how long a key is *held*. Anything involving
   dwell time, flight time, or rollover (Tiers noted below) requires extending the action model
   and the `InputSimulatorPlusBackend` to emit separate key-down / key-up events. This is the one
   big architectural fork on this list.
2. **Timing is i.i.d.** Each `SampleDelayMs` call is independent. Real typing rhythm is
   *autocorrelated* — slow keys cluster, fast keys cluster. Adding one piece of state fixes this
   cheaply and is one of the highest realism-per-line wins.

---

## Tier 1 — Easy wins (hours, no architecture change)

These all live inside `DelayModel` / `TypingContext` / `TypingProfile` and cost a handful of lines
each. They need no new action types and no backend change.

### 1.1 Expand the bigram table (and split fast/slow)
**Models:** practiced letter pairs are faster; the current list is only 14 entries.
**Now:** `CommonBigrams` has 14 pairs → ×0.7. **Do:** grow to the top ~50 English bigrams, and
optionally add a *slow* set (rare/awkward pairs) with a ×1.1–1.2 penalty. This is pure data.
**Effort:** trivial. **Source:** [Aalto 136M](https://userinterfaces.aalto.fi/136Mkeystrokes/) —
digraph identity is a top predictor of IKI.

### 1.2 Punctuation & sentence-boundary pauses
**Models:** humans pause *after* `. , ; : ! ?` and much longer at sentence/paragraph ends
(cognitive boundary, not motor).
**Now:** the only boundary modifier is `PreviousChar == ' '` → ×1.5. **Do:** in `DelayModel`, add:
`prev is ',' ';' ':'` → ×1.8; `prev is '.' '!' '?'` → ×2.5–4; `prev == '\n'` (paragraph) → ×5+.
Scale by boundary rank: intra-word < word < punctuation < sentence < paragraph.
**Effort:** trivial (a few `if`s reading `ctx.PreviousChar`). **Source:**
[Springer keystroke log](https://link.springer.com/article/10.1007/s11145-019-09953-8) — the
30 ms–2 s (transcription) vs >2 s (planning) two-tier pause rule.

### 1.3 Warm-up ramp
**Models:** typists start ~15–25% slower and speed up over the first ~30–60 s / N keystrokes
(≈5–10% WPM gain once warm).
**Now:** we model fatigue (gets slower) but not warm-up (starts slow). **Do:** add an *inverse*
early-multiplier that decays to 1.0, e.g. `×(1 + 0.20·exp(-charsTyped/40))`. Reuses the existing
`CharsTypedSoFar`. **Effort:** trivial (one line, mirror of the fatigue line).
**Source:** [TypeTest warm-ups](https://typetest.io/blog/posts/2024-09-16-typing-warm-ups-prep-your-fingers-for-speed.html).

### 1.4 A physiological floor via a shifted log-normal
**Models:** no human keystroke gap is ~20 ms of "fast log-normal tail"; there is a hard motor
floor (~40–60 ms) and the *shape* above it is skewed.
**Now:** `MinDelayMs = 20` is a hard clamp that piles probability mass at the floor. **Do:** use a
3-parameter shifted log-normal: `floor + exp(μ + σ·Z)` with `floor ≈ 45 ms`, and set μ so the
*median* still tracks the WPM base. Cleaner tail than clamping. **Effort:** small (change the
sampling line + adjust the median test in `DelayModel_Median_TracksBaseDelay`).
**Source:** [PMC8606350](https://pmc.ncbi.nlm.nih.gov/articles/PMC8606350/) (log-normal /
log-logistic best-fit; shifted forms more accurate).

### 1.5 Occasional attention lapses
**Models:** rare, text-independent multi-second pauses ("looked away", "lost the thread").
**Now:** none. **Do:** with small per-char probability (e.g. 0.3–0.8%), inject a one-off large
pause (1.5–4 s) drawn from the RNG, independent of the character. Poisson-ish arrivals.
**Effort:** trivial (one `if` in `DelayModel` or the engine loop). **Source:**
[Keystroke Dynamics Authentication (arXiv 0911.3304)](https://arxiv.org/pdf/0911.3304) — "pauses
for an unknown reason."

---

## Tier 2 — Medium (one new state variable or small table)

### 2.1 Autocorrelated pace (AR(1) speed drift) — *highest priority on this list*
**Models:** the single biggest gap vs. real typing. Human IKIs are **positively autocorrelated**
(Hurst 0.5 < H < 1) — you speed up and slow down in *runs*, you don't reroll independently every
key. Right now our pace looks "random" in the tell-tale i.i.d. way.
**Do:** carry a slow multiplier across keys: `pace_t = 0.9·pace_{t-1} + 0.1·N(1, small)`, then
`delay *= pace_t`. This is the session-scale version of §1.3/fatigue and unifies them.
**Effort:** medium — `DelayModel` becomes *stateful* (or the engine threads `pace` through the
context). Watch determinism: the RNG draw order must stay stable so seeded tests still reproduce.
**Source:** [Hurst exponent](https://en.wikipedia.org/wiki/Hurst_exponent),
[Long-range correlation in skilled performance (Frontiers)](https://www.frontiersin.org/journals/psychology/articles/10.3389/fpsyg.2014.01030/full).

### 2.2 Burst-and-pause structure
**Models:** typing is fast bursts (low variance) separated by pauses at planning boundaries.
**Do:** falls out mostly for free once §2.1 (drift) + §1.2 (boundary pauses) + §2.3 (pre-word
planning) are in — a "burst" is just the low-σ run between two injected pauses. Optionally define a
pause threshold ≈ 4× median IKI to make bursts explicit. **Effort:** medium (mostly emergent).
**Source:** [Springer keystroke log](https://link.springer.com/article/10.1007/s11145-019-09953-8).

### 2.3 Pre-word planning latency (scaled by word rarity/length)
**Models:** the gap between the space and the first letter of the next word carries next-word
planning; **rare/long words get a longer pre-word pause**.
**Now:** we add a flat ×1.5 after any space. **Do:** make the post-space multiplier depend on the
upcoming word's length and (optionally) frequency — long/rare word → bigger pause, common short
word → smaller. Needs lookahead to the next word boundary (engine already scans char-by-char) and,
for frequency, a small common-word set. **Effort:** medium (lookahead + optional word list).
**Source:** [Springer keystroke log](https://link.springer.com/article/10.1007/s11145-019-09953-8).

### 2.4 Biomechanical bigram timing (hand/finger/distance) — *biggest believability jump*
**Models:** IKI depends on the *fingers* involved. Measured means for skilled typists:
hand-alternation ≈ **114 ms** (fastest), same-hand different-finger ≈ **131 ms**, same-finger ≈
**157 ms** (slowest — the classic same-finger digraph penalty). Repeats (double letters) are slow
too.
**Do:** annotate the existing `UsQwertyLayout` with per-key **(hand, finger, row, x/y)** metadata,
then in `DelayModel` apply a multiplier from the previous→current key relationship:
alternating-hand = 1.0 (baseline), same-hand-diff-finger ≈ **1.15×**, same-finger ≈ **1.35×**,
plus a small term ∝ Euclidean key distance for row jumps. This replaces guesswork with the real
driver of key-to-key variation. **Effort:** medium — the layout metadata table is the work; the
multiplier is a few lines. **Source:**
[Aalto 136M / Cambridge](https://www.cam.ac.uk/research/news/what-makes-a-faster-typist),
[finger-alternation IKI data (ResearchGate)](https://www.researchgate.net/figure/The-impact-of-finger-alternation-for-inter-key-intervals-between-different-bigram-types_fig4_338913960).

### 2.5 Delayed error detection (v2 typo family from plan.md Phase 8)
**Models:** real correction is often **not** immediate — the error is noticed several chars later,
then a backspace burst walks back and retypes. In one study delayed corrections (~0.63/sentence)
*outnumbered* immediate ones (~0.40/sentence). This also unlocks the deferred **omission** and
**missing-double** typos (`wat`→`want`, `commitee`), which are delayed-detection *by nature*.
**Now:** every typo is corrected on the very next action (`HumanTypingEngine.cs`), which is the one
hard invariant. **Do:** add a mode where the engine types N more correct chars, *then* emits a
backspace burst back to the error and retypes — net text still equals input, but the correction is
displaced. **Effort:** medium–hard — the engine's tidy per-char switch becomes a small state
machine, and the "net text == input" reconstruction test must be generalized to tolerate delayed
corrections. **Source:** [Typoist (arXiv 2502.03560)](https://arxiv.org/abs/2502.03560);
already tracked in `plan.md` Phase 8.

### 2.6 Realistic error mix + speed coupling
**Models:** **substitution dominates** on modern keyboards (~10× the others): substitution ≈ 3.5%,
insertion ≈ 0.25%, omission ≈ 0.17%, transposition ≈ 0.07% (young adults). And error probability
**rises with speed** (speed–accuracy tradeoff).
**Now:** `ChooseKind` weights are hand-picked (0.50 slip / 0.20 transpose / 0.15 each) and typo
rate is a flat constant. **Do:** re-weight toward the measured mix, and multiply the per-char typo
probability by the current pace (faster burst from §2.1 → more errors). Also cheap: inverse-distance
neighbour weighting for `AdjacentKey` (closer neighbours more likely) — already a noted Phase 8
refinement. **Effort:** small–medium. **Source:**
[Typoist (2502.03560)](https://arxiv.org/abs/2502.03560),
[Cambridge/Aalto](https://www.cam.ac.uk/research/news/what-makes-a-faster-typist).

---

## Tier 3 — Hard (new action model / backend change)

These require emitting **separate key-down and key-up events**, which `TimedAction` +
`InputSimulatorPlusBackend` do not do today. `InputSimulator` exposes `SimulateKeyDown` /
`SimulateKeyUp`, so the backend can support it — but `KeyAction`/`TimedAction`/`TypingOrchestrator`
and every timing test would need to move from "press" to "down + hold + up." Do this only if
keystroke-biometric realism is a goal.

### 3.1 Dwell time (key hold duration)
**Models:** how long a key is physically held — ~**60–120 ms**, and notably **more Gaussian** (less
skewed) than the between-key flight time. Keystroke-biometric systems key on this.
**Do:** add a `HoldMs` to the action model; sample dwell from a near-normal per-key distribution;
backend emits down, waits `HoldMs`, emits up. **Effort:** hard (action-model + backend + orchestrator
+ tests). **Source:** [PMC8606350](https://pmc.ncbi.nlm.nih.gov/articles/PMC8606350/),
[keystroke metrics diagram](https://www.researchgate.net/figure/Keystroke-metrics-latency-interval-dwell-time-and-flight-time-Generally-typing_fig1_221247068).

### 3.2 Dwell + flight decomposition
**Models:** `IKI(down→down) = dwell(key N) + flight(N→N+1)`. Flight is the heavy-tailed,
cognitively-loaded part (≈6 nats of info vs ≈4.5 for dwell); dwell is tighter/near-normal.
**Do:** once §3.1 exists, split the current single delay into dwell (normal) + flight (log-normal),
and derive down/up timestamps. This yields realistic down-down, up-up, down-up and up-down latencies
that biometric tooling checks. **Effort:** hard. **Source:**
[PMC8606350](https://pmc.ncbi.nlm.nih.gov/articles/PMC8606350/).

### 3.3 Key rollover / overlap (negative flight)
**Models:** fast typists press the next key *before releasing* the previous — for the fastest,
**40–70%** of keystrokes overlap, i.e. **flight time goes negative**. It's a strong speed correlate.
**Do:** requires §3.1/§3.2 (independent down/up scheduling). For fast personas, with 0.4–0.7
probability on easy alternating bigrams, overlap the events. **Effort:** hard (only possible with the
event model). **Source:** [Cambridge/Aalto](https://www.cam.ac.uk/research/news/what-makes-a-faster-typist).

### 3.4 Persona presets
**Models:** coherent bundles of the parameters above — "hunt-and-peck" (slow, high substitution, big
pre-word pauses, little rollover), "average 52 WPM", "fast touch-typist 100+ WPM" (high rollover,
tight σ, low error), "mobile/elderly" (omission-skewed). The research gives per-demographic error
mixes to seed these.
**Do:** a `TypingPersona` that sets `TypingProfile` + the new tables in one pick. Not hard *per se*,
but only meaningful once Tiers 1–2 exist. **Effort:** small once the knobs exist; listed here because
it depends on them. **Source:** [Typoist demographic error rates (2502.03560)](https://arxiv.org/abs/2502.03560).

---

## Recommended order (realism per unit effort)

1. **§2.1 Autocorrelated pace** — kills the i.i.d. "machine" tell; one state variable.
2. **§2.4 Biomechanical bigram timing** — biggest believability jump; needs layout metadata.
3. **§1.2 punctuation pauses + §1.3 warm-up + §1.5 lapses** — cheap polish, do them together.
4. **§1.1 bigger bigram table + §1.4 shifted log-normal** — data + shape cleanup.
5. **§2.6 realistic error mix / speed coupling**, then **§2.5 delayed detection** (unlocks the
   Phase 8 omission/double typos).
6. **§3.x dwell / flight / rollover + §3.4 personas** — only if keystroke-biometric realism matters;
   this is the architectural fork (down/up event model).

**Guardrails carried from the current design:**
- Keep the **single injected `Random`** contract — any new randomness must draw from it, and the
  draw *order* must stay stable so seeded plans remain reproducible (the existing tests depend on it).
- Preserve **net-typed-text == input**. §2.5 (delayed correction) is the only item that stresses this;
  generalize the reconstruction test rather than dropping the invariant.
- The **Windows 11 Notepad fast-burst limitation** (plan.md Phase 4 note) still applies — dwell/rollover
  work will push event rates higher, so re-verify against a controlled target, not Notepad.

## Key sources
- [Observations on Typing from 136 Million Keystrokes — Dhakal et al., CHI 2018](https://userinterfaces.aalto.fi/136Mkeystrokes/) · [Cambridge summary](https://www.cam.ac.uk/research/news/what-makes-a-faster-typist)
- [On the shape of timings distributions in free-text keystroke dynamics (PMC8606350)](https://pmc.ncbi.nlm.nih.gov/articles/PMC8606350/)
- [Typoist: Simulating Errors in Touchscreen Typing (arXiv 2502.03560)](https://arxiv.org/abs/2502.03560)
- [An Agent-Based Modeling Approach to Free-Text Keyboard Dynamics (arXiv 2505.05015)](https://arxiv.org/pdf/2505.05015)
- [Understanding the keystroke log (Springer)](https://link.springer.com/article/10.1007/s11145-019-09953-8)
- [Finger-alternation IKI data (ResearchGate)](https://www.researchgate.net/figure/The-impact-of-finger-alternation-for-inter-key-intervals-between-different-bigram-types_fig4_338913960)
- [Long-range correlation in skilled performance (Frontiers)](https://www.frontiersin.org/journals/psychology/articles/10.3389/fpsyg.2014.01030/full) · [Hurst exponent](https://en.wikipedia.org/wiki/Hurst_exponent)
- [MacKenzie & Soukoreff — KSPC / error metrics (ACM)](https://dl.acm.org/doi/10.1145/1753326.1753329)
- [Attention lapses — Keystroke Dynamics Authentication (arXiv 0911.3304)](https://arxiv.org/pdf/0911.3304)
