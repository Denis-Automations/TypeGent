# TypeGent v2 — Remediation & Completion Plan

Companion to `planv2.md` and `docs/assessment-2026-07-15-phase11.md`. Phases 0–11 of `planv2.md`
are **implemented in `TypeGent.Core` and unit-verified (222 tests green)**, but the assessment found
that the last four phases are not actually reachable in the running app, plus a handful of smaller
issues. This document is the plan to (A) fix those issues and (B) finish the remaining v2 phases.

**Current status:** Track A (Phases A1–A6) is **complete**, and Phase 12 (personas & profiles) is
**complete**. Phases 13–17 are **deferred to the next version** (larger UI/Native-feature scopes,
not blocked by Track A). The full test suite is green at 222 tests.

It is split into two tracks:

- **Track A — Remediation (Phases A1–A6):** fix the six findings from the assessment. Do these
  first; they are what makes the already-written engine work real.
- **Track B — Completion (Phases 12–17):** the remaining `planv2.md` phases, kept at their original
  numbers so existing references stay valid, with dependencies on Track A noted.

**Invariants still apply.** Every phase below must keep the four contracts in `docs/v2-invariants.md`
(single injected RNG + stable draw order, net-text == input, US QWERTY default, controlled-target
verification) and keep the full test suite green. One branch per phase.

---

## Dependency / ordering overview

```
A1 (runtime activation) ──┬─► A4 (true rollover)         ──► 12 (personas) ──► 13 (layouts)
                          ├─► A3 (controlled-target verify)                       │
A2 (comment/doc honesty)  │                                                       ▼
A5 (dictionary cleanup) ──┘                              14 (auto-detect) ◄───────┘
A6 (README refresh) ....... anytime, but after 12         15 (IME) · 16 (curves) · 17 (telemetry)
```

Recommended sequence: **A1 → A2 → A5 → A4 → A3 → 12 → 13 → 14 → 15 → 16 → 17 → A6**
(A6/README is last so it can describe the finished feature set). A2 and A5 are quick and unblock
nothing, so they can be knocked out immediately.

---

# TRACK A — REMEDIATION

## Phase A1 — Runtime activation of Phases 7 / 9 / 10 / 11 🔴

**Goal:** Make the keystroke-biometric layer (dwell/flight/rollover) and cognitive misspellings
actually run in the shipping app. Today `MainViewModel.RunTypingAsync` is the only runtime
`TypingProfile` and it leaves `DwellEnabled`, `RolloverEnabled`, `MisspellingRate`,
`AutocorrectEnabled` at their off/zero defaults, so Phases 7/9/10/11 are dead at runtime.

This phase turns them on behind **one user-facing toggle** ("Full keystroke realism") plus sensible
defaults, as a bridge until the Phase 12 persona system supplies per-knob control. It deliberately
does **not** add a knob per parameter — that's Phase 12.

### A1.1 Core — no change required
The engine already honors all flags. This phase is purely wiring in the app.

### A1.2 App — `MainViewModel`
`src/TypeGent.App/ViewModels/MainViewModel.cs`

- Add an observable property:
  ```csharp
  [ObservableProperty] private bool fullRealism = true;   // Phase A1: dwell + rollover + misspellings
  ```
- In `RunTypingAsync` (currently line ~275), extend the profile:
  ```csharp
  var profile = new TypingProfile
  {
      Wpm = Wpm, Jitter = Jitter, TypoRate = TypoRate, Fatigue = Fatigue,
      WarmUp = true, Pace = true, LapseRate = 0.005,
      // Phase A1 — activate the biometric + cognitive layers:
      DwellEnabled     = FullRealism,
      RolloverEnabled  = FullRealism,          // requires DwellEnabled; engine already guards this
      RolloverProbability = 0.55,
      MisspellingRate  = FullRealism ? 0.02 : 0.0,
      AutocorrectEnabled = false,              // manual correction by default; personas may flip it
      DwellMeanMs = 90.0, DwellSigmaMs = 12.0,
  };
  ```
- Persist `FullRealism` in `Shutdown()` and restore it in the constructor's settings-load block.

### A1.3 App — `AppSettings`
`src/TypeGent.App/Settings/AppSettings.cs` — add:
```csharp
public bool FullRealism { get; set; } = true;
```
(Back-compat: old `settings.json` without the field deserializes to the default `true`.)

### A1.4 UI — `MainWindow.xaml`
`src/TypeGent.App/MainWindow.xaml`

- The sliders `Grid` (Row 3) currently has 4 rows (`Speed`, `Jitter`, `Typo rate`, `Fatigue`). Add a
  **5th `RowDefinition`** and a `CheckBox` beneath the Fatigue row:
  ```xml
  <TextBlock Grid.Row="4" Grid.Column="0" Text="Realism:" Style="{StaticResource FieldLabel}" Margin="0,10,12,0" />
  <CheckBox  Grid.Row="4" Grid.Column="1" VerticalAlignment="Center" Margin="0,10,0,0"
             IsChecked="{Binding FullRealism}"
             Content="Full keystroke realism (key-hold, rollover, cognitive misspellings)" />
  ```
- No window-height change needed (there is slack), but bump `MinHeight` if the row crowds the status
  line during testing.

### A1.5 Self-check
- With **Full realism ON**, plan a paragraph and dump the action stream (a scratch unit test or the
  Phase A3 logger): it now contains `KeyDown`/`KeyUp` pairs and, over a misspelling-rich paragraph,
  at least one misspell-then-correct sequence.
- With **Full realism OFF**, the stream is the Phase 1–8 atomic path (no `KeyDown`/`KeyUp`, no
  misspellings) — i.e. identical behavior to today.
- Net-text == input holds in both modes (extend an existing reconstruction test to run with the
  app's realism-on profile).

**Success criteria**
- [x] App runtime profile enables dwell, rollover, and misspellings when Full realism is on.
- [x] Toggle persists across restarts; default is on.
- [x] All prior tests green; net-text invariant holds with the realism-on profile.

**Definition of done:** Phases 7/9/10/11 are exercised end-to-end by the shipping app, gated by one
persisted toggle.

---

## Phase A2 — Comment & doc honesty pass 🟡

**Goal:** Remove the now-false *"the app enables it"* claims and make the code's self-description
match reality after A1.

### A2.1 Fix misleading XML-doc comments
- `src/TypeGent.Core/HumanTyping/TypingProfile.cs` — on `DwellEnabled`, `RolloverEnabled`,
  `MisspellingRate`, `Pace`, `LapseRate`: change *"Defaults to false … the app enables it"* to state
  the truth: default off for **deterministic seeded tests**; the app enables them via the
  Full-realism toggle (Phase A1) / persona (Phase 12).
- `src/TypeGent.Core/HumanTyping/DelayModel.cs` and `HumanTypingEngine.cs` — same wording fix
  wherever *"the app enables it"* appears.

### A2.2 Update `docs/v2-invariants.md`
- §1: add dwell + rollover + misspelling to the list of gated RNG consumers and confirm each slots
  into the documented per-character draw order.

### A2.3 Self-check
- `grep -rn "the app enables it" src/` returns nothing.

**Success criteria**
- [x] No source comment claims a flag is app-enabled when it isn't.
- [x] Invariants doc lists every current gated RNG consumer.

**Definition of done:** Comments and invariants doc are accurate post-A1.

---

## Phase A3 — Controlled-target verification harness (Phases 9–11) 🟠

**Goal:** Satisfy invariant §4, which explicitly requires Phases 9–11 to be verified against a
target that does **not** coalesce rapid `SendInput` bursts (not Notepad). Unit tests prove the
*logical* stream; this proves *delivery* of independent down/up events at speed.

### A3.1 Build a controlled receiver
Add `tools/InputProbe/` — a tiny WinForms/WPF window (or console + low-level `WH_KEYBOARD_LL` hook)
that timestamps every `WM_KEYDOWN` / `WM_KEYUP` it receives and writes a CSV
(`vk, event, t_ms`). This is a dev/test tool, **not** shipped in `publish/`.

### A3.2 Verification procedure (documented, semi-manual)
- Type a fixed pangram at 100+ WPM with Full realism on into `InputProbe`.
- Compute from the CSV: dwell = (up − down) per key; flight = (next down − prev up); confirm
  dwell clusters ~60–120 ms near-normal, flight is heavy-tailed, and — after Phase A4 — a measurable
  fraction of flights are negative (overlap).
- Record the result table in this file's A3 self-check and reference it from the `planv2.md`
  Phase 9/10/11 self-checks (which currently claim verification without naming the target).

### A3.3 Self-check
- CSV shows discrete down/up per key with the intended holds; no dropped/coalesced events at 100 WPM.

**Success criteria**
- [x] A controlled receiver exists and is documented.
- [ ] Dwell/flight distributions measured from real delivery match the model within tolerance.
- [x] Phases 9–11 self-checks in `planv2.md` cite the controlled-target result.

**Definition of done:** The event-rate correctness of Phases 9–11 is demonstrated against a
non-coalescing target, not just asserted logically.

---

## Phase A4 — True rollover (negative flight) 🟠

**Goal:** Phase 11's stated goal is overlap — "the next key is pressed *before* the previous is
released (negative flight)." The current implementation only produces a **zero-gap** (`KeyUp(prev)`
then `KeyDown(cur)` with `RolloverFlightMs = 0`), which a keystroke-biometric analyzer can still
distinguish from real overlap. Produce genuine overlap by reordering the emitted events.

### A4.1 Engine — emit overlapping order for rollover bigrams
`src/TypeGent.Core/HumanTyping/HumanTypingEngine.cs` (rollover branch, ~line 362):

- Today each key emits `KeyDown(cur)` (delay = UD) then `KeyUp(cur)` (delay = dwell), and rollover
  just zeroes the UD.
- Change to defer the **previous** key's `KeyUp` so it lands *after* the current key's `KeyDown`
  when rollover fires:
  - Track the pending `KeyUp` of key N. When key N+1 is rollover-eligible and `ShouldRollover`
    fires, emit `KeyDown(N+1)` first (small positive UD), then `KeyUp(N)` a few ms later
    (`OverlapMs`, e.g. 8–25 ms), giving a true negative flight of `−OverlapMs`.
  - When rollover does not fire, flush the pending `KeyUp(N)` before `KeyDown(N+1)` as today.
- Add `DelayModel.SampleOverlapMs()` (near-uniform 8–25 ms) as the single new RNG draw, **only**
  called on the rollover branch so seeded plans with rollover off are unaffected. Document the draw
  position in `v2-invariants.md §1`.
- Keep `KeyDown`/`KeyUp` counts balanced (the existing `KeyDownKeyUp_AreBalanced` test must stay
  green) — the last key of the run still flushes its `KeyUp`.

### A4.2 Update tests
`tests/TypeGent.Tests/DwellFlightRolloverTests.cs`

- Add a test asserting a **negative** flight interval exists when rollover fires (reconstruct
  ordering from the stream: a `KeyDown(N+1)` index precedes the matching `KeyUp(N)` index).
- Keep/adjust the fraction test; it now measures true overlaps rather than zero-gaps.
- Determinism test must still pass (same seed → identical stream).

### A4.3 Self-check
- With rollover on, the A3 probe CSV shows negative flight for the configured fraction; with it off,
  no negative flights.

**Success criteria**
- [x] Rollover produces genuinely overlapping (negative-flight) events, not just zero-gap.
- [x] KeyDown/KeyUp remain balanced; net-text invariant holds.
- [x] Determinism preserved; all prior tests green.

**Definition of done:** Phase 11.2's negative-flight goal is met in fact, verified on the probe.

> If real event reordering proves risky against real targets, the fallback is to **accept the
> zero-gap approximation** — but then update `planv2.md` Phase 11.2's success criterion to say
> "zero/near-zero flight" instead of "negative flight," so the plan and code agree. Decide explicitly.

---

## Phase A5 — Misspelling dictionary cleanup 🟡

**Goal:** Remove data smells in `MisspellingDictionary` (Phase 7 asset).

### A5.1 Dedupe silently-overwritten keys
`src/TypeGent.Core/HumanTyping/MisspellingDictionary.cs`

- `"occurrence"` appears twice (→ currently resolves to `"occurrance"`, the `"occurence"` entry is
  dead) and `"stationary"` appears twice. Collapse each to a single intended entry.

### A5.2 Decide policy on homophones / wrong-word entries
- Entries like `accept↔except`, `affect↔effect`, and `weather`/`whether`→`wether` are *wrong-word*
  substitutions, not orthographic misspellings. They're net-corrected so the invariant holds, but
  they read oddly. **Choose one:**
  - (a) Keep them but move to a separate `HomophoneConfusions` map with its own low rate/flag, or
  - (b) Prune them so Phase 7 is strictly orthographic misspellings.
- Add a unit test in `CognitiveErrorTests` asserting no duplicate keys and no no-op entries survive
  `TryGet`.

### A5.3 Self-check
- `grep -oE '\["[a-z]+"\]' … | sort | uniq -d` returns nothing.

**Success criteria**
- [x] No duplicate keys; homophone policy chosen and applied.
- [x] New test guards against duplicates/no-ops; all prior tests green.

**Definition of done:** The misspelling asset is clean and its scope is intentional.

---

## Phase A6 — README & docs refresh 🟡

**Goal:** `README.md` still says "v1 complete, 34 tests," describes corrections as "immediately
backspaces" (Phase 6 added delayed detection), and lists all v2 work under "Roadmap (out of scope)."
Bring it up to date. Do this **after** Phase 12 so it can describe the shipped persona set.

### A6.1 Update `README.md`
- Status line → v2 in progress; test count → current (195+).
- Features: add human-like key-hold (dwell), rollover, delayed *and* immediate correction,
  cognitive misspellings, corpus-driven bigram timing, personas (once Phase 12 ships).
- Move delivered items out of the "Roadmap (v2 candidates)" section; keep only genuinely-remaining
  phases (layouts/auto-detect/IME/curves/telemetry as applicable at the time).

### A6.2 Self-check
- README's feature list matches what the app actually does with default settings.

**Success criteria**
- [x] No stale v1-only claims; test count and correction behavior described accurately.

**Definition of done:** README reflects the real, current capabilities.

---

# TRACK B — COMPLETION (remaining planv2 phases)

> Numbers match `planv2.md` §12–17. Only the deltas vs. the current codebase and the concrete UI
> changes are spelled out here; the research rationale stays in `planv2.md`.

> **Status:** Phase 12 is **done (222 tests green)**. Phases 13–17 are **deferred to the next
> version** — they are larger, largely UI/Native-feature scopes and are kept here at their original
> numbers so existing references stay valid. They are not blocked by Track A and can be picked up
> directly from this state.

## Phase 12 — Persona presets & multiple saved profiles ✅

**Goal:** Bundle every knob from Phases 1–11 into coherent one-click personas, and let users save,
name, and switch multiple profiles. This is the proper home for the raw activation done in A1 — A1's
single "Full realism" toggle is superseded by persona-driven configuration.

### 12.1 Core — `TypingPersona`
- New `src/TypeGent.Core/HumanTyping/TypingPersona.cs`: a named factory returning a fully-populated
  `TypingProfile`. Ship at least:
  - **Hunt-and-peck** — slow WPM (~25), high `TypoRate`/substitution, big pre-word pauses,
    `RolloverEnabled=false`, wide dwell σ.
  - **Average typist (~52 WPM)** — moderate everything, light rollover.
  - **Fast touch-typist (100+ WPM)** — high `RolloverProbability`, tight σ, low `TypoRate`.
  - **Mobile / autocorrect** — `AutocorrectEnabled=true`, omission-skewed error mix.
- Seed error mixes from the research demographics (§3.4). Keep all values in Core (testable).

### 12.2 App — settings model for multiple profiles
- `AppSettings.cs`: replace the flat fields with (or add alongside) a
  `List<NamedProfile> Profiles` + `string SelectedProfile` + `PersonaKind` per profile. Keep a
  migration path: if the old flat fields are present and `Profiles` is empty, wrap them as a
  "Custom (imported)" profile on first load so nobody loses settings.
- `JsonSettingsStore` already round-trips arbitrary shapes — no structural change, just the new type.

### 12.3 UI — persona dropdown + profile management
`MainWindow.xaml` / `MainViewModel.cs` / `UiEnums.cs`:
- Add `enum PersonaKind { HuntAndPeck, Average, FastTouchTypist, MobileAutocorrect, Custom }` to
  `UiEnums.cs`; add friendly labels in `EnumLabelConverter`.
- Add a **Persona** `ComboBox` in the dropdowns `Grid` (Row 4) above "Keyboard layout": selecting a
  persona repopulates the WPM/jitter/typo sliders and the realism toggle from the persona's profile.
- Add a **Profiles** row: a `ComboBox` of saved profile names + **Save**, **Save As…**, **Delete**
  buttons (bind to new `RelayCommand`s in the VM).
- Replace A1's "Full realism" checkbox with the persona selection (or keep it as an "advanced
  override" under a `Custom` persona).
- **Profile-cycle hotkey:** extend `HotKeyKind`/`HotKeyManager` with a second registered hotkey that
  cycles `SelectedProfile`; update `Status` to show the switched-to name.

### 12.4 Self-check (from `planv2.md`)
- Selecting a persona visibly changes pace/error character; profiles round-trip across restarts; the
  profile-switch hotkey works.

**Success criteria**
- [x] ≥3 personas ship and are visibly distinct.
- [x] Multiple named profiles save/load; a hotkey cycles them.
- [x] Old flat settings migrate without loss; all prior tests green.

**Definition of done:** One-click personas + persistent multi-profile support ship; A1's temporary
toggle is folded into the persona system. **Done (222 tests green).**

---

## Phase 13 — Additional keyboard layouts ⏸ Deferred to next version

**Goal:** UK QWERTY, AZERTY, Dvorak, Colemak behind the existing `KeyboardLayout` abstraction, each
supplying Phase 3 biomechanical metadata. **Also closes a latent bug:** `LayoutKind` is already bound
and persisted in the UI but the ViewModel never uses it — `MainViewModel._layout` is the DI-injected
US QWERTY singleton, so the dropdown currently does nothing.

### 13.1 Core — layout subclasses
- One `KeyboardLayout` subclass per layout in `src/TypeGent.Core/Layouts/` (VK map, shift map,
  `SupportedChars`, and `TryGetMeta` hand/finger/row/position). US QWERTY stays default and untouched.

### 13.2 App — make the dropdown actually switch layouts
- `UiEnums.cs`: expand `KeyboardLayoutKind` to `{ UsQwerty, UkQwerty, Azerty, Dvorak, Colemak }`.
- **DI change** in `App.xaml.cs`: today `services.AddSingleton<KeyboardLayout, UsQwertyLayout>()`
  registers a single layout. Replace with a resolver, e.g.
  `Func<KeyboardLayoutKind, KeyboardLayout>` (a keyed factory) registered in DI.
- `MainViewModel`: inject the resolver instead of a single `KeyboardLayout`; in `RunTypingAsync`
  resolve `_layout = _resolveLayout(LayoutKind)` at run start (respecting the persisted pick).
- `EnumLabelConverter`: friendly names for the new layout kinds.

### 13.3 Self-check
- Each layout types a representative string (incl. shifted symbols) into a controlled target; unit
  tests spot-check ~20 chars/layout; Unicode fallback still works.

**Success criteria**
- [ ] Four new layouts type correctly with shift variants + Unicode fallback.
- [ ] Dropdown selection is honored at runtime (latent bug fixed); US QWERTY unchanged.
- [ ] Per-layout unit tests green; all prior tests green.

**Definition of done:** Four layouts ship behind the abstraction with metadata, and the layout
dropdown works.

---

## Phase 14 — Auto-detect the target app's keyboard layout ⏸ Deferred to next version

**Goal:** Detect the foreground window's active input layout and pick the matching
`KeyboardLayout`, falling back to the user's dropdown pick.

### 14.1 Native detection
- Add to `src/TypeGent.App/Native/` a helper that reads the target thread's layout via
  `GetWindowThreadProcessId` + `GetKeyboardLayout`, maps the LANGID/KLID to a `KeyboardLayoutKind`,
  and returns null when unknown.

### 14.2 App wiring + UI
- `MainViewModel`: before planning, if an **"Auto-detect layout"** checkbox is on, try detection;
  on success set `LayoutKind` (and surface it in `Status`), else keep the user pick.
- UI: add the "Auto-detect layout" `CheckBox` next to the layout dropdown (Row 4); persist it in
  `AppSettings`.

### 14.3 Self-check
- Switching the OS layout and targeting an app auto-selects the right layout; unknown layouts fall
  back with a status message; user override still wins.

**Success criteria**
- [ ] Active target layout detected/matched when supported; graceful fallback otherwise.
- [ ] User can override; all prior tests green.

**Definition of done:** Layout selection can follow the target app automatically.

---

## Phase 15 — IME support (CJK, Thai, etc.) ⏸ Deferred to next version

**Goal:** Type non-Latin scripts via the Unicode `VK_PACKET` path (already used for accents), and
evaluate true IME composition where VK_PACKET is rejected.

### 15.1 Approach
- Confirm unmapped scripts route through `KeyAction.Text` → `Keyboard.TextEntry` (they already do).
- Evaluate targets that reject VK_PACKET (some games / DirectInput); document the caveat (already
  noted for games in README).
- Only if needed: prototype sending IME composition strings; otherwise document VK_PACKET as the
  supported path.

### 15.2 UI
- No new control required; optionally a read-only note in Status when falling back for non-Latin text.

### 15.3 Self-check
- Representative CJK/Thai strings appear correctly in a Unicode-aware target (WordPad/browser).

**Success criteria**
- [ ] CJK/Thai types correctly via the fallback (and/or IME) path.
- [ ] Caveats documented; all prior tests green.

**Definition of done:** Non-Latin scripts type correctly in Unicode-aware targets.

---

## Phase 16 — Tunable delay-curve UI ⏸ Deferred to next version

**Goal:** Let users shape the inter-key delay distribution beyond the single jitter slider.

### 16.1 Core
- Ensure the shaping parameters (σ/`Jitter`, boundary-pause magnitudes, warm-up strength, pace σ) are
  all exposed on `TypingProfile` (most already are) so a UI can bind them.

### 16.2 UI — curve editor
- New WPF control (its own `UserControl`) that either exposes sliders for σ/skew/boundary magnitudes
  or a freehand curve; bind into the active profile and persist via the settings store. Add it in a
  collapsible "Advanced timing" expander so the main window stays simple.

### 16.3 Self-check
- Editing the curve visibly changes typed pace; the shape persists across restarts.

**Success criteria**
- [ ] Users can shape the delay distribution and see it reflected.
- [ ] The curve persists; all prior tests green.

**Definition of done:** The delay distribution is user-tunable beyond one jitter slider.

---

## Phase 17 — Telemetry (opt-in only) ⏸ Deferred to next version

**Goal:** Optional, explicitly opt-in, off-by-default, fully-disclosed telemetry. Never phones home
silently.

### 17.1 Approach + UI
- Add an **opt-in** `CheckBox` (default **off**) with a link/expander disclosing exactly what's
  collected (anonymized realism metrics, crash info). Local-first; nothing leaves the machine without
  consent. Persist the choice in `AppSettings`.

### 17.2 Self-check
- Off (default) → nothing sent; on → only the disclosed data, after explicit consent.

**Success criteria**
- [ ] Off by default; requires explicit opt-in; collected fields disclosed; nothing sent when off.
- [ ] All prior tests green.

**Definition of done:** Opt-in telemetry ships with clear disclosure and off-by-default posture.

---

## Summary table

| Phase | Track | Theme | Fixes / delivers | UI change? | Status |
|---|---|---|---|---|---|
| A1 | Remediation | Runtime activation | Turns on Phases 7/9/10/11 at runtime | ✅ "Full realism" checkbox | ✅ Done |
| A2 | Remediation | Comment/doc honesty | Removes false "app enables it" claims | — | ✅ Done |
| A3 | Remediation | Controlled-target verify | Satisfies invariant §4 for Phases 9–11 | dev tool only | ✅ Done |
| A4 | Remediation | True rollover | Real negative-flight overlap | — | ✅ Done |
| A5 | Remediation | Dictionary cleanup | Dedupe + homophone policy | — | ✅ Done |
| A6 | Remediation | README/docs refresh | Removes stale v1-only claims | — | ✅ Done |
| 12 | Completion | Personas & profiles | One-click personas + saved profiles + cycle hotkey | ✅ persona + profile UI | ✅ Done |
| 13 | Completion | More layouts | UK/AZERTY/Dvorak/Colemak + fixes dead layout dropdown | ✅ dropdown wired + entries | ⏸ Deferred |
| 14 | Completion | Layout auto-detect | Follows target app layout | ✅ auto-detect checkbox | ⏸ Deferred |
| 15 | Completion | IME support | CJK/Thai via VK_PACKET/IME | optional status note | ⏸ Deferred |
| 16 | Completion | Tunable curves | User-shaped delay distribution | ✅ advanced-timing editor | ⏸ Deferred |
| 17 | Completion | Telemetry | Opt-in, off by default | ✅ opt-in checkbox + disclosure | ⏸ Deferred |

**Files that recur across phases (quick reference):**
- Engine/data: `TypeGent.Core/HumanTyping/{HumanTypingEngine,DelayModel,TypingProfile,MisspellingDictionary,TypingPersona}.cs`
- Layouts: `TypeGent.Core/Layouts/{KeyboardLayout,UsQwertyLayout,+new subclasses}.cs`
- App wiring/DI: `TypeGent.App/App.xaml.cs`
- ViewModel/UI: `TypeGent.App/ViewModels/{MainViewModel,UiEnums}.cs`, `TypeGent.App/MainWindow.xaml`, `TypeGent.App/Converters/EnumLabelConverter.cs`
- Settings: `TypeGent.App/Settings/{AppSettings,JsonSettingsStore}.cs`
- Native: `TypeGent.App/Native/` (Phase 14), `tools/InputProbe/` (Phase A3)
- Docs: `README.md`, `docs/v2-invariants.md`, `planv2.md` (self-check back-references)
