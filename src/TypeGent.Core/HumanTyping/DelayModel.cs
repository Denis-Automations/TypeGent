namespace TypeGent.Core.HumanTyping;

/// <summary>
/// Samples a human-looking inter-key delay. The core is a <em>shifted</em> log-normal whose median
/// equals the WPM-derived base delay: <c>FloorMs + exp(ln(base − FloorMs) + σ·Z)</c>, so a
/// physiological floor (~45 ms) replaces v1's hard 20 ms clamp. Context modifiers (Shift reach,
/// word/punctuation/sentence boundaries, bigram familiarity, fatigue, warm-up) scale the result,
/// then an optional attention lapse can add a multi-second stall.
/// <para>
/// The RNG is injected so a fixed seed reproduces a whole plan; never <c>new Random()</c> here.
/// The lapse draw is the only new RNG consumer in v2 Phase 1; it sits at a fixed point at the end
/// of <see cref="SampleDelayMs"/> and is skipped entirely when the rate is 0, so existing seeded
/// plans keep their draw order (see <c>docs/v2-invariants.md</c> §1).
/// </para>
/// </summary>
public sealed class DelayModel
{
    /// <summary>Physiological floor (ms): the minimum possible inter-key delay (v2 Phase 1).</summary>
    public const double FloorMs = 45.0;

    public const double MaxDelayMs = 2000.0;

    // Top ~50 English bigrams type faster than average (×0.7). Grown from v1's 14 (v2 Phase 1, §1.1).
    private static readonly HashSet<string> FastBigrams = new(StringComparer.Ordinal)
    {
        "th", "he", "in", "er", "an", "re", "on", "at", "en", "nd",
        "ti", "es", "or", "te", "of", "ed", "is", "it", "al", "ar",
        "st", "to", "nt", "ng", "se", "ha", "as", "ou", "io", "le",
        "ve", "co", "me", "de", "hi", "ri", "ea", "ra", "ce", "li",
        "ch", "ll", "be", "ma", "el", "ta", "la", "na", "ot", "so",
    };

    // Rare / awkward bigrams type slower than average (×1.15). v2 Phase 1 slow set (§1.1).
    private static readonly HashSet<string> SlowBigrams = new(StringComparer.Ordinal)
    {
        "qz", "zq", "jq", "qj", "zx", "xz", "qx", "xq", "jx", "xj",
        "vq", "qv", "bv", "vb", "pq", "qp", "gq", "qg", "mx", "xm",
    };

    private readonly Random _rng;
    private readonly double _jitter;
    private readonly double _lapseRate;
    private readonly double _lapseMinMs;
    private readonly double _lapseMaxMs;
    private readonly double _paceSigma;

    // AR(1) pace state (v2 Phase 2, §2.1). Carries across calls within one plan so IKIs are
    // positively autocorrelated. Starts at 1.0 (no drift); a fresh DelayModel per Plan() resets it.
    private double _pace = 1.0;
    private const double PacePersistence = 0.9;

    public DelayModel(Random rng, double jitter = 0.35, double lapseRate = 0.0,
        double lapseMinMs = 1500, double lapseMaxMs = 4000, double paceSigma = 0.3)
    {
        _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        _jitter = jitter;
        _lapseRate = lapseRate;
        _lapseMinMs = lapseMinMs;
        _lapseMaxMs = lapseMaxMs;
        _paceSigma = paceSigma;
    }

    /// <summary>
    /// Sample the delay (ms) before typing the character described by <paramref name="ctx"/>,
    /// given the median <paramref name="baseDelayMs"/>.
    /// </summary>
    public double SampleDelayMs(double baseDelayMs, TypingContext ctx)
    {
        if (baseDelayMs <= FloorMs) baseDelayMs = FloorMs + 1;

        // Shifted log-normal: median == baseDelayMs, with an irreducible motor floor (§1.4).
        var mu = Math.Log(baseDelayMs - FloorMs);
        var delay = FloorMs + Math.Exp(mu + _jitter * NextGaussian());

        // Deterministic context modifiers (no RNG draws — order-independent).
        if (ctx.NeedsShift) delay *= 1.15;                                        // shift reach
        delay *= BoundaryMultiplier(ctx.PreviousChar);                            // word/punct/sentence (§1.2)
        delay *= BigramMultiplier(ctx.PreviousChar, ctx.CurrentChar);             // familiarity (§1.1)
        if (ctx.Fatigue) delay *= 1.0 + 0.0005 * ctx.CharsTypedSoFar;             // fatigue
        if (ctx.WarmUp) delay *= 1.0 + 0.20 * Math.Exp(-ctx.CharsTypedSoFar / 40.0); // warm-up ramp (§1.3)

        // AR(1) autocorrelated pace (§2.1): pace drifts in slow runs so IKIs are positively
        // autocorrelated instead of i.i.d. Stateful — _pace carries across calls within a plan.
        // Skipped when ctx.Pace is false so seeded plans keep a stable RNG draw order. Draw order
        // per call (all features on): [log-normal Gaussian] → [pace Gaussian] → [lapse roll].
        if (ctx.Pace)
        {
            _pace = PacePersistence * _pace + (1 - PacePersistence) * (1.0 + _paceSigma * NextGaussian());
            delay *= _pace;
        }

        delay = Math.Clamp(delay, FloorMs, MaxDelayMs);

        // Attention lapse (§1.5): skipped when the rate is 0 so seeded plans keep a stable draw order.
        if (_lapseRate > 0 && _rng.NextDouble() < _lapseRate)
            delay += _lapseMinMs + _rng.NextDouble() * (_lapseMaxMs - _lapseMinMs);

        return delay;
    }

    private static double BoundaryMultiplier(char prev)
    {
        if (prev == ' ') return 1.5;                                  // word boundary (kept from v1)
        if (prev == ',' || prev == ';' || prev == ':') return 1.8;    // light punctuation pause
        if (prev == '.' || prev == '!' || prev == '?') return 3.0;    // sentence boundary
        if (prev == '\n' || prev == '\r') return 5.0;                 // paragraph / line break
        return 1.0;
    }

    private static double BigramMultiplier(char prev, char cur)
    {
        if (prev == '\0') return 1.0;
        var pair = new string(new[] { char.ToLowerInvariant(prev), char.ToLowerInvariant(cur) });
        if (FastBigrams.Contains(pair)) return 0.7;
        if (SlowBigrams.Contains(pair)) return 1.15;
        return 1.0;
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
