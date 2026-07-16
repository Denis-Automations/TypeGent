# InputProbe — Keystroke Verification Harness

A dev/test tool for verifying that TypeGent's Phase 9–11 keystroke-biometric layer
(down/up events, dwell, flight/rollover) is **delivered correctly** to a target window at
typing speed. This satisfies invariant §4 (`docs/v2-invariants.md`): Phases 9–11 must be
verified against a controlled target that does not coalesce rapid `SendInput` bursts — not
Windows 11 Notepad, which can drop or scramble aggressive bursts.

InputProbe is **not shipped** with the main app (`publish/`). It lives in `tools/` for
manual verification only.

---

## What it does

- Captures every `PreviewKeyDown` / `PreviewKeyUp` (WPF tunneling events, closest to the raw
  `WM_KEYDOWN`/`WM_KEYUP` messages) with a high-resolution `Stopwatch` timestamp.
- Logs each event in real time: `index, VK, direction, time_ms`.
- Computes live statistics:
  - **Dwell** (key hold time = up − down per key, FIFO matched by VK): min/max/mean/stddev,
    and the fraction within [30, 200] ms (the model's physiological range).
  - **Flight** (inter-key gap = next down − prev up): min/max/mean/stddev, plus the fraction
    of **negative** flights (true overlap / rollover) and near-zero flights.
- Exports the full event log to CSV (`index, vk, vk_name, event, t_ms`) for offline analysis.

---

## Verification procedure (Phase A3 / invariant §4)

1. **Build and launch InputProbe:**
   ```
   dotnet run --project tools/InputProbe
   ```
2. **Launch TypeGent** (`dotnet run --project src/TypeGent.App`).
3. **Configure TypeGent:**
   - Set WPM to 100+ (fast enough to stress event delivery).
   - Ensure **Full keystroke realism** is ON (dwell + rollover + misspellings).
   - Paste a fixed pangram, e.g.:
     `the quick brown fox jumps over the lazy dog`
4. **Click Start in TypeGent** (3-second countdown) and immediately focus the InputProbe
   window. Alternatively, focus InputProbe and press the TypeGent hotkey.
5. **After typing completes**, click **Save CSV…** in InputProbe and save the file.
6. **Inspect the stats panel** and/or the CSV:

### Expected results (with Full realism ON, 100+ WPM)

| Metric | Expected | Why |
|---|---|---|
| KeyDown / KeyUp counts | Equal (balanced) | Every key down has a matching up |
| Dwell mean | ~60–120 ms | Near-Gaussian, centered on configured 90 ms (Phase 10) |
| Dwell in [30, 200] ms | ~100% | Clamped to physiological range (DwellMin/DwellMax) |
| Flight min | **Negative** (after Phase A4) | True rollover: next key down before prev key up |
| Negative flight fraction | > 0 (measurable) | Rollover fires on ~40–70% of eligible bigrams (Phase 11) |
| No dropped/coalesced events | KeyDown count == expected key count | WPF does not coalesce rapid SendInput |

### Expected results (with Full realism OFF)

| Metric | Expected |
|---|---|
| No KeyDown/KeyUp events | Phase 1–8 atomic path (Press with hold) |
| Or KeyDown/KeyUp if dwell-only (rollover off) | Sequential, no negative flight |

---

## CSV format

```
index,vk,vk_name,event,t_ms
1,84,T,DOWN,0.000
2,84,T,UP,95.234
3,72,H,DOWN,45.678
...
```

Use the CSV to compute dwell/flight distributions in a spreadsheet or script for a
permanent record. The `t_ms` column is milliseconds since the first captured event.
