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

    // High-frequency English words that need less pre-word planning time (v2 Phase 4, §2.3).
    // Covers ~65 % of tokens in typical running text. Case-insensitive comparison.
    private static readonly HashSet<string> CommonWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Top function words / pronouns
        "i", "a", "an", "the", "and", "or", "but", "nor", "so", "yet",
        "in", "on", "at", "to", "of", "for", "by", "as", "up", "out",
        "if", "no", "not", "is", "am", "are", "was", "were", "be", "been",
        "do", "did", "has", "had", "will", "can", "may", "let", "get", "got",
        "he", "she", "it", "we", "you", "they", "me", "him", "her", "us",
        "my", "his", "its", "our", "your", "one", "all", "who", "than",
        // Very high-frequency verbs
        "say", "said", "see", "saw", "come", "came", "go", "went", "make",
        "made", "know", "knew", "take", "took", "give", "gave", "look",
        "want", "use", "find", "tell", "ask", "seem", "feel", "try", "call",
        "keep", "put", "set", "run", "ran", "hold", "move", "show", "send",
        "read", "lose", "pass", "stop", "turn", "open", "help", "play",
        "live", "need", "work", "like", "think", "have",
        // Very high-frequency adjectives / adverbs
        "very", "well", "just", "now", "how", "new", "old", "few", "big",
        "good", "bad", "own", "long", "more", "most", "much", "many",
        "such", "even", "still", "too", "also", "only", "once", "never",
        "back", "off", "away", "down", "here", "there", "then", "when",
        "why", "each", "both", "same", "last", "next", "high", "free",
        "real", "true", "full", "sure", "right", "left", "near", "far",
        // Very high-frequency nouns
        "man", "men", "day", "way", "two", "eye", "lot", "end", "bit",
        "time", "year", "part", "word", "face", "life", "home", "hand",
        "side", "body", "head", "door", "room", "line", "city", "fact",
        "idea", "case", "name", "form", "kind", "mind", "size", "role",
        // Common connectives / discourse words
        "that", "this", "what", "with", "from", "they", "some", "into",
        "over", "after", "about", "which", "their", "there", "would", "could",
        "should", "these", "those", "other", "where", "while", "being",
        "through", "before", "around", "without", "another", "because",
        "always", "really", "quite", "every", "maybe", "again", "often",
        "almost", "early", "short", "small", "great", "first", "large",
        "thing", "point", "place", "world", "people", "state", "night",
        "water", "story", "power", "money", "group", "begin", "under",
        "might", "along", "whose", "though", "since", "either", "within",
        "little", "anyone", "number", "second", "better", "something",
        "nothing", "someone", "everyone", "together", "however", "already",
        "perhaps", "between", "certain", "different", "anything",
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
        delay *= BoundaryMultiplier(ctx);                               // word/punct/sentence (§1.2 + §2.3)
        delay *= BigramMultiplier(ctx.PreviousChar, ctx.CurrentChar);             // familiarity (§1.1)
        if (ctx.Fatigue) delay *= 1.0 + 0.0005 * ctx.CharsTypedSoFar;             // fatigue
        if (ctx.WarmUp) delay *= 1.0 + 0.20 * Math.Exp(-ctx.CharsTypedSoFar / 40.0); // warm-up ramp (§1.3)
        delay *= BiomechanicalMultiplier(ctx.PreviousChar, ctx.CurrentChar, ctx.Layout); // hand/finger/distance (§2.4)

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

    /// <summary>
    /// Returns true if <paramref name="word"/> (lowercase) is in the high-frequency common-word
    /// list used by the pre-word planning-pause formula (v2 Phase 4).
    /// </summary>
    public static bool IsCommonWord(string word) => CommonWords.Contains(word);

    private static double BoundaryMultiplier(TypingContext ctx)
    {
        var prev = ctx.PreviousChar;
        if (prev == ' ')  return PlanningPauseMultiplier(ctx);          // word boundary (§2.3)
        if (prev == ',' || prev == ';' || prev == ':') return 1.8;      // light punctuation pause
        if (prev == '.' || prev == '!' || prev == '?') return 3.0;      // sentence boundary
        if (prev == '\n' || prev == '\r') return 5.0;                   // paragraph / line break
        return 1.0;
    }

    /// <summary>
    /// Planning-pause multiplier for the character that immediately follows a space (v2 Phase 4,
    /// §2.3). Scales with the upcoming word's length and rarity:
    /// <code>
    ///   multiplier = 1.0 + 0.06 × wordLength  [+0.3 if rare]  clamped to [1.0, 3.5]
    /// </code>
    /// Examples: "the" (3, common) → ×1.18 | "quick" (5, rare) → ×1.60 | 16-letter rare → ×2.26.
    /// When <see cref="TypingContext.NextWordLength"/> is 0 (no lookahead supplied) the legacy
    /// flat ×1.5 is used so existing tests that don't set word-context stay green.
    /// </summary>
    private static double PlanningPauseMultiplier(TypingContext ctx)
    {
        if (ctx.NextWordLength == 0) return 1.5;      // legacy flat — no lookahead info
        var mult = 1.0 + 0.06 * ctx.NextWordLength;  // length-driven component
        if (!ctx.NextWordIsCommon) mult += 0.3;       // rarity penalty for unfamiliar words
        return Math.Clamp(mult, 1.0, 3.5);
    }

    private static double BiomechanicalMultiplier(char prev, char cur, Layouts.KeyboardLayout? layout)
    {
        // No layout or no metadata for either key → skip (preserves draw order — no RNG draw).
        if (layout is null) return 1.0;
        if (prev == '\0') return 1.0;
        if (!layout.TryGetMeta(prev, out var pm)) return 1.0;
        if (!layout.TryGetMeta(cur,  out var cm)) return 1.0;

        // Double-letter: same physical key struck twice.
        if (char.ToLowerInvariant(prev) == char.ToLowerInvariant(cur)) return 1.38;

        // Relationship multiplier (hand/finger). Measured means from §2.4:
        //   Alternating hand  ≈ 114 ms → ×1.00 (baseline)
        //   Same hand, diff finger ≈ 131 ms → ×1.15
        //   Same finger       ≈ 157 ms → ×1.38
        double mult;
        if (pm.Hand != cm.Hand)
            mult = 1.00;                             // hand alternation — fastest
        else if (pm.Finger != cm.Finger)
            mult = 1.15;                             // same hand, different finger
        else
            mult = 1.38;                             // same finger — slowest

        // Distance term: up to +8% for keys far apart (row jumps, long reaches).
        // Euclidean distance in key-width units; reference distance = 1 key width.
        var dx = pm.X - cm.X;
        var dy = pm.Y - cm.Y;
        var dist = Math.Sqrt(dx * dx + dy * dy);
        mult += 0.04 * Math.Min(dist, 2.0);         // caps at 2 key-widths to avoid outliers

        return mult;
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
