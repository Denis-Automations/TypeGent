namespace TypeGent.Core.HumanTyping;

/// <summary>
/// The user-tunable knobs that shape a typing run. Pure data; the models read it.
/// </summary>
public sealed class TypingProfile
{
    /// <summary>Words per minute (1 word == 5 chars by convention).</summary>
    public int Wpm { get; init; } = 60;

    /// <summary>Log-normal sigma for inter-key timing — higher = more variable pace.</summary>
    public double Jitter { get; init; } = 0.35;

    /// <summary>Probability per eligible character of introducing a (self-corrected) typo.</summary>
    public double TypoRate { get; init; } = 0.02;

    /// <summary>Whether typing gradually slows as more characters are typed.</summary>
    public bool Fatigue { get; init; } = true;

    /// <summary>
    /// Whether an early warm-up ramp makes the first ~40 chars slightly slower, decaying to 1.0
    /// (v2 Phase 1). Deterministic (no RNG draw), so it is on by default without affecting
    /// seeded reproducibility.
    /// </summary>
    public bool WarmUp { get; init; } = true;

    /// <summary>
    /// Whether inter-key pace drifts autocorrelated (AR(1)) across the run — bursts of faster and
    /// slower typing instead of i.i.d. rerolls every key (v2 Phase 2). Defaults to <c>false</c> so
    /// seeded plans keep a stable RNG draw order; the app enables it.
    /// </summary>
    public bool Pace { get; init; }

    /// <summary>The standard deviation of the AR(1) pace innovation (v2 Phase 2). Higher = driftier.</summary>
    public double PaceSigma { get; init; } = 0.3;

    /// <summary>
    /// Per-character probability of an attention lapse — a one-off 1.5–4 s stall (v2 Phase 1).
    /// Defaults to 0 so seeded plans keep a stable RNG draw order; the app enables it.
    /// </summary>
    public double LapseRate { get; init; } = 0.0;

    /// <summary>Minimum lapse duration in ms (v2 Phase 1).</summary>
    public double LapseMinMs { get; init; } = 1500;

    /// <summary>Maximum lapse duration in ms (v2 Phase 1).</summary>
    public double LapseMaxMs { get; init; } = 4000;

    /// <summary>
    /// Per-word probability of attempting a cognitive misspelling from the
    /// <see cref="MisspellingDictionary"/> (v2 Phase 7). The misspelling is always
    /// corrected (immediate or delayed per Phase 6). Defaults to 0 so existing seeded
    /// plans keep their RNG draw order — the app enables it.
    /// </summary>
    public double MisspellingRate { get; init; } = 0.0;

    /// <summary>
    /// Whether to simulate an autocorrect pass that bulk-replaces the mistyped word a beat
    /// after the final character is typed — distinct from manual backspacing (v2 Phase 7).
    /// When <see langword="false"/> (default) the engine uses human backspacing for all
    /// corrections; when <see langword="true"/> a fast autocorrect action replaces the word.
    /// </summary>
    public bool AutocorrectEnabled { get; init; } = false;

    /// <summary>
    /// Whether to sample a near-Gaussian key-hold (dwell) duration for each keystroke and
    /// deliver it as a separate key-down / key-up pair (v2 Phase 10). Requires the Phase 9
    /// down/up event model. Defaults to <see langword="false"/> so existing seeded plans keep
    /// their RNG draw order — the app enables it.
    /// </summary>
    public bool DwellEnabled { get; init; } = false;

    /// <summary>
    /// Mean key-hold (dwell) duration in milliseconds (v2 Phase 10).
    /// Research median for skilled typists is ~90 ms (PMC8606350).
    /// </summary>
    public double DwellMeanMs { get; init; } = 90.0;

    /// <summary>
    /// Standard deviation of the dwell distribution in milliseconds (v2 Phase 10).
    /// Dwell is notably more Gaussian (less skewed) than flight time; σ ≈ 12 ms is typical.
    /// </summary>
    public double DwellSigmaMs { get; init; } = 12.0;

    /// <summary>
    /// The median inter-key interval implied by <see cref="Wpm"/>: 60000 / (Wpm * 5).
    /// At 60 WPM this is 200 ms.
    /// </summary>
    public double BaseDelayMs => 60_000.0 / (Math.Max(1, Wpm) * 5.0);
}
