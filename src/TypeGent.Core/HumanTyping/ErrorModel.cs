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
}

/// <summary>
/// Decides whether and how to introduce a mechanical typo, using the same injected RNG as the
/// <see cref="DelayModel"/> (so one seed reproduces an entire plan). Cognitive/spelling errors
/// and delayed-detection errors (omission, missing double letters) are deferred to v2 — see
/// plan.md Phase 8.
/// </summary>
public sealed class ErrorModel
{
    private readonly Random _rng;

    public ErrorModel(Random rng) => _rng = rng ?? throw new ArgumentNullException(nameof(rng));

    /// <summary>Roll for a typo at the current character.</summary>
    public bool ShouldIntroduceTypo(double typoRate) => _rng.NextDouble() < typoRate;

    /// <summary>Pick a typo kind, weighted, among those applicable at this position.</summary>
    public TypoKind ChooseKind(bool canTranspose, bool canShiftMistime)
    {
        var weights = new List<(TypoKind Kind, double W)>
        {
            (TypoKind.AdjacentSlip, 0.50),
            (TypoKind.RepeatedKey, 0.15),
        };
        if (canTranspose) weights.Add((TypoKind.Transposition, 0.20));
        if (canShiftMistime) weights.Add((TypoKind.ShiftMistime, 0.15));

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
    /// the intended character if it has no known neighbors (so the caller always gets a char).
    /// </summary>
    public char AdjacentKey(char intended)
    {
        var lower = char.ToLowerInvariant(intended);
        if (!Neighbors.TryGetValue(lower, out var ns) || ns.Length == 0) return intended;

        var pick = ns[_rng.Next(ns.Length)];
        return char.IsUpper(intended) ? char.ToUpperInvariant(pick) : pick;
    }

    /// <summary>Milliseconds to "notice" a typo before correcting it.</summary>
    public int ReactionDelayMs() => (int)(150 + _rng.NextDouble() * 300);

    /// <summary>How many extra times a repeated key fires (mostly 1, sometimes 2).</summary>
    public int ExtraRepeats() => _rng.NextDouble() < 0.8 ? 1 : 2;

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
