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
    /// The median inter-key interval implied by <see cref="Wpm"/>: 60000 / (Wpm * 5).
    /// At 60 WPM this is 200 ms.
    /// </summary>
    public double BaseDelayMs => 60_000.0 / (Math.Max(1, Wpm) * 5.0);
}
