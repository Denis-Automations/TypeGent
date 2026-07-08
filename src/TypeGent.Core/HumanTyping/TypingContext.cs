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
}
