using TypeGent.Core.Layouts;

namespace TypeGent.Core.HumanTyping;

/// <summary>
/// Per-character situational input to the <see cref="DelayModel"/>. A default-constructed
/// instance is deliberately "neutral" (no modifiers fire) so timing tests can assert against
/// the bare log-normal.
/// </summary>
public sealed class TypingContext
{
    /// <summary>The character about to be typed.</summary>
    public char CurrentChar { get; init; }

    /// <summary>The previously typed character ('\0' at the start).</summary>
    public char PreviousChar { get; init; }

    /// <summary>How many characters have been typed so far (drives fatigue).</summary>
    public int CharsTypedSoFar { get; init; }

    /// <summary>Whether typing <see cref="CurrentChar"/> requires holding Shift.</summary>
    public bool NeedsShift { get; init; }

    /// <summary>Whether the fatigue modifier is enabled for this run.</summary>
    public bool Fatigue { get; init; } = true;

    /// <summary>
    /// Whether the warm-up ramp is enabled for this run (v2 Phase 1). Defaults to <c>false</c> so a
    /// default-constructed context stays neutral for timing tests.
    /// </summary>
    public bool WarmUp { get; init; }

    /// <summary>
    /// Whether the autocorrelated AR(1) pace envelope is enabled for this run (v2 Phase 2).
    /// Defaults to <c>false</c> so a default-constructed context stays neutral for timing tests.
    /// </summary>
    public bool Pace { get; init; }

    /// <summary>
    /// The active keyboard layout, used by <see cref="DelayModel"/> in v2 Phase 3 to look up
    /// per-key biomechanical metadata for the relationship multiplier. When <see langword="null"/>
    /// the biomechanical modifier is skipped, so tests that don't set it stay neutral.
    /// </summary>
    public KeyboardLayout? Layout { get; init; }

    /// <summary>
    /// Length of the word starting at <see cref="CurrentChar"/> (v2 Phase 4, §2.3). Used when
    /// <see cref="PreviousChar"/> is a space or '\0' to scale the pre-word planning pause by
    /// upcoming word length. Zero means "not at a word boundary" or "no lookahead" — the
    /// legacy flat ×1.5 multiplier is used in that case, keeping all prior tests intact.
    /// </summary>
    public int NextWordLength { get; init; }

    /// <summary>
    /// Whether the upcoming word (of length <see cref="NextWordLength"/>) is in the high-
    /// frequency common-word list (v2 Phase 4). Common words require less planning time
    /// and receive a smaller pre-word pause than rare or unfamiliar words.
    /// Only meaningful when <see cref="NextWordLength"/> > 0.
    /// </summary>
    public bool NextWordIsCommon { get; init; }
}
