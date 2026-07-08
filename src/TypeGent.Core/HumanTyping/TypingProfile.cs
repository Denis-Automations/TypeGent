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
    /// The median inter-key interval implied by <see cref="Wpm"/>: 60000 / (Wpm * 5).
    /// At 60 WPM this is 200 ms.
    /// </summary>
    public double BaseDelayMs => 60_000.0 / (Math.Max(1, Wpm) * 5.0);
}
