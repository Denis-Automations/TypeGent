namespace TypeGent.Core.HumanTyping;

/// <summary>
/// Samples a human-looking inter-key delay. The core is a log-normal whose <em>median</em>
/// equals the WPM-derived base delay (μ = ln(base), σ = jitter), then context modifiers and a
/// clamp are applied.
/// <para>
/// The RNG is injected so a fixed seed reproduces a whole plan; never <c>new Random()</c> here.
/// </para>
/// </summary>
public sealed class DelayModel
{
    public const double MinDelayMs = 20.0;
    public const double MaxDelayMs = 2000.0;

    // Common English bigrams type faster than average.
    private static readonly HashSet<string> CommonBigrams = new(StringComparer.Ordinal)
    {
        "th", "er", "in", "re", "on", "an", "at", "en", "ed", "or", "te", "ng", "is", "ti",
    };

    private readonly Random _rng;
    private readonly double _jitter;

    public DelayModel(Random rng, double jitter = 0.35)
    {
        _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        _jitter = jitter;
    }

    /// <summary>
    /// Sample the delay (ms) before typing the character described by <paramref name="ctx"/>,
    /// given the median <paramref name="baseDelayMs"/>.
    /// </summary>
    public double SampleDelayMs(double baseDelayMs, TypingContext ctx)
    {
        if (baseDelayMs <= 0) baseDelayMs = 1;

        // Log-normal with median == baseDelayMs.
        var mu = Math.Log(baseDelayMs);
        var delay = Math.Exp(mu + _jitter * NextGaussian());

        if (ctx.NeedsShift) delay *= 1.15;                                   // shift reach
        if (ctx.PreviousChar == ' ') delay *= 1.5;                           // word boundary
        if (IsCommonBigram(ctx.PreviousChar, ctx.CurrentChar)) delay *= 0.7; // practiced pair
        if (ctx.Fatigue) delay *= 1.0 + 0.0005 * ctx.CharsTypedSoFar;        // fatigue

        return Math.Clamp(delay, MinDelayMs, MaxDelayMs);
    }

    private static bool IsCommonBigram(char prev, char cur)
    {
        if (prev == '\0') return false;
        var pair = new string(new[] { char.ToLowerInvariant(prev), char.ToLowerInvariant(cur) });
        return CommonBigrams.Contains(pair);
    }

    // Standard normal via Box–Muller (draws two uniforms per call; no cached spare so the
    // RNG-consumption order stays trivially deterministic).
    private double NextGaussian()
    {
        var u1 = 1.0 - _rng.NextDouble();
        var u2 = 1.0 - _rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
