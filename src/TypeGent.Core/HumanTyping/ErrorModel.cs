namespace TypeGent.Core.HumanTyping;

/// <summary>The kind of mechanical typo to inject. All are corrected immediately.</summary>
public enum TypoKind
{
    /// <summary>Hit a physically adjacent key instead of the intended one.</summary>
    AdjacentSlip,

    /// <summary>Shift held a beat too long, capitalizing a letter that should be lower-case.</summary>
    ShiftMistime,

    /// <summary>Swap the order of the current and next letter.</summary>
    Transposition,

    /// <summary>Type the key one or two extra times.</summary>
    RepeatedKey,

    /// <summary>Skip the intended key entirely (v2 Phase 6). Always corrected via delayed detection.</summary>
    Omission,

    /// <summary>Type a double-letter pair only once (e.g. 'commitee' instead of 'committee') (v2 Phase 6).</summary>
    MissingDouble,

    /// <summary>
    /// Type a learned cognitive misspelling of an entire word, then correct it
    /// (immediate or delayed). The misspelling comes from <see cref="MisspellingDictionary"/>
    /// rather than a physical adjacency table (v2 Phase 7).
    /// </summary>
    CognitiveMisspelling,
}

/// <summary>
/// Decides whether and how to introduce a mechanical typo, using the same injected RNG as the
/// <see cref="DelayModel"/> (so one seed reproduces an entire plan). Mechanical typos (Phases
/// 5–6) and cognitive misspellings (Phase 7) share this model; all are net-corrected so the
/// invariant <c>net typed text == input</c> always holds.
/// </summary>
public sealed class ErrorModel
{
    private readonly Random _rng;

    public ErrorModel(Random rng) => _rng = rng ?? throw new ArgumentNullException(nameof(rng));

    /// <summary>
    /// Roll for a typo at the current character (v2 Phase 5: speed-coupled rate).
    /// Faster pace (<paramref name="currentPace"/> &gt; 1) raises the effective probability —
    /// this models the speed–accuracy tradeoff: typing faster produces more errors.
    /// The effective rate is capped at 0.45 so pace spikes can't produce pathologically
    /// frequent typos that overwhelm the correction machinery.
    /// </summary>
    public bool ShouldIntroduceTypo(double typoRate, double currentPace = 1.0)
        => _rng.NextDouble() < Math.Min(0.45, typoRate * Math.Max(0.1, currentPace));

    /// <summary>Pick a typo kind, weighted, among those applicable at this position.</summary>
    public TypoKind ChooseKind(bool canTranspose, bool canShiftMistime, bool canMissDouble)
    {
        // Measured mix (v2 Phase 5 & 6): substitution dominates.
        // Omission and MissingDouble are less frequent than slips, but they happen.
        var weights = new List<(TypoKind Kind, double W)>
        {
            (TypoKind.AdjacentSlip, 0.84),
            (TypoKind.RepeatedKey, 0.05),
            (TypoKind.Omission, 0.03),
        };
        if (canTranspose) weights.Add((TypoKind.Transposition, 0.015));
        if (canShiftMistime) weights.Add((TypoKind.ShiftMistime, 0.06));
        if (canMissDouble) weights.Add((TypoKind.MissingDouble, 0.005));

        var total = weights.Sum(x => x.W);
        var r = _rng.NextDouble() * total;
        var acc = 0.0;
        foreach (var (kind, w) in weights)
        {
            acc += w;
            if (r <= acc) return kind;
        }

        return TypoKind.AdjacentSlip;
    }

    /// <summary>
    /// A physically adjacent key to <paramref name="intended"/>, preserving case. Falls back to
    /// the intended character if it has no known neighbors. Uses inverse-distance weighting
    /// if layout metadata is available (v2 Phase 5).
    /// </summary>
    public char AdjacentKey(char intended, Layouts.KeyboardLayout? layout = null)
    {
        var lower = char.ToLowerInvariant(intended);
        if (!Neighbors.TryGetValue(lower, out var ns) || ns.Length == 0) return intended;

        char pick;
        if (layout != null && layout.TryGetMeta(intended, out var center))
        {
            var weights = new double[ns.Length];
            var totalWeight = 0.0;
            for (var i = 0; i < ns.Length; i++)
            {
                if (layout.TryGetMeta(ns[i], out var nm))
                {
                    var dx = center.X - nm.X;
                    var dy = center.Y - nm.Y;
                    var dist = Math.Sqrt(dx * dx + dy * dy);
                    var w = 1.0 / (dist + 0.01);
                    weights[i] = w;
                    totalWeight += w;
                }
                else
                {
                    weights[i] = 1.0;
                    totalWeight += 1.0;
                }
            }

            var r = _rng.NextDouble() * totalWeight;
            var acc = 0.0;
            pick = ns[0];
            for (var i = 0; i < ns.Length; i++)
            {
                acc += weights[i];
                if (r <= acc)
                {
                    pick = ns[i];
                    break;
                }
            }
        }
        else
        {
            pick = ns[_rng.Next(ns.Length)];
        }

        return char.IsUpper(intended) ? char.ToUpperInvariant(pick) : pick;
    }

    /// <summary>Milliseconds to "notice" a typo before correcting it.</summary>
    public int ReactionDelayMs() => (int)(150 + _rng.NextDouble() * 300);

    /// <summary>How many extra times a repeated key fires (mostly 1, sometimes 2).</summary>
    public int ExtraRepeats() => _rng.NextDouble() < 0.8 ? 1 : 2;

    /// <summary>
    /// Roll for a cognitive misspelling at the start of a word (v2 Phase 7).
    /// The misspelling rate is independent of — and additive with — the mechanical typo rate,
    /// but shares the same RNG so the draw order is stable.
    /// </summary>
    public bool ShouldApplyMisspelling(double misspellingRate)
        => misspellingRate > 0 && _rng.NextDouble() < misspellingRate;

    /// <summary>
    /// The delay (ms) between the last character of an autocorrected word and the bulk-replace
    /// action — simulates the brief gap while the autocorrect system applies (v2 Phase 7).
    /// Typically 50–250 ms, deliberately faster than human backspacing.
    /// </summary>
    public int AutocorrectDelayMs() => (int)(50 + _rng.NextDouble() * 200);

    /// <summary>
    /// How many characters the typist types *after* the error before noticing it (v2 Phase 6).
    /// Roughly 40% immediate (0), 60% delayed.
    /// </summary>
    public int DetectionDelayChars()
    {
        var r = _rng.NextDouble();
        if (r < 0.40) return 0; // immediate
        if (r < 0.70) return 1;
        if (r < 0.85) return 2;
        if (r < 0.95) return 3;
        if (r < 0.99) return 4;
        return 5;
    }

    // QWERTY physical neighbors per letter. v1 picks uniformly among immediate neighbors; true
    // inverse-distance weighting is a v2 refinement (plan.md Phase 8).
    private static readonly IReadOnlyDictionary<char, string> Neighbors = new Dictionary<char, string>
    {
        ['q'] = "was",     ['w'] = "qeasd",   ['e'] = "wrsdf",  ['r'] = "etdfg",
        ['t'] = "ryfgh",   ['y'] = "tughj",   ['u'] = "yihjk",  ['i'] = "uojkl",
        ['o'] = "ipkl",    ['p'] = "ol",
        ['a'] = "qwsz",    ['s'] = "qweadzx", ['d'] = "wersfxc", ['f'] = "ertdgcv",
        ['g'] = "rtyfhvb", ['h'] = "tyugjbn",  ['j'] = "yuihknm", ['k'] = "uiojlm",
        ['l'] = "iopk",
        ['z'] = "asx",     ['x'] = "sdzc",    ['c'] = "dfxv",   ['v'] = "fgcb",
        ['b'] = "ghvn",    ['n'] = "hjbm",    ['m'] = "jkn",
    };
}
