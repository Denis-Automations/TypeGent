namespace TypeGent.Core.HumanTyping;

/// <summary>
/// Named, one-click typing presets (v2 Phase 12). Each method returns a fully-populated
/// <see cref="TypingProfile"/> bundling every knob from Phases 1–11 into a coherent,
/// research-grounded character. The app maps its UI persona selector to these factories;
/// all persona values live here in Core so they are unit-testable and the engine stays
/// free of persona-specific logic.
/// <para>
/// A persona sets the <em>baseline</em> realism layers (dwell, rollover, misspellings,
/// pace, lapses). The app overlays the user's WPM/jitter/typo/fatigue slider values and a
/// master realism gate on top, so a saved profile can carry the persona's internal timing
/// while still reflecting the user's slider tweaks.
/// </para>
/// <para>
/// Research anchors (see <c>docs/assessment-…</c> and <c>planv2.md</c> §3): skilled-typist
/// dwell median ≈ 90 ms (PMC8606350); 40–70 % of fast bigrams overlap; average adult typing
/// ≈ 40 WPM with hunt-and-peck ≈ 25 WPM; mobile thumb-typing is slower, more error-prone,
/// and autocorrect-assisted.
/// </para>
/// </summary>
public static class TypingPersona
{
    /// <summary>
    /// Slow two-finger typist: ~25 WPM, high error rate, large timing variance, long
    /// variable key holds, no rollover (keys are released before the next is found), and
    /// frequent attention lapses from looking at the keyboard.
    /// </summary>
    public static TypingProfile HuntAndPeck() => new()
    {
        Wpm = 25,
        Jitter = 0.45,
        TypoRate = 0.06,
        Fatigue = true,
        WarmUp = true,
        Pace = true,
        PaceSigma = 0.35,
        LapseRate = 0.01,
        MisspellingRate = 0.03,
        AutocorrectEnabled = false,
        DwellEnabled = true,
        DwellMeanMs = 110.0,
        DwellSigmaMs = 25.0,
        RolloverEnabled = false,
        RolloverProbability = 0.0,
    };

    /// <summary>
    /// A typical touch-typist: ~52 WPM, moderate error rate and variance, light rollover
    /// on eligible bigrams, standard ~90 ms dwell. The sensible middle ground.
    /// </summary>
    public static TypingProfile Average() => new()
    {
        Wpm = 52,
        Jitter = 0.35,
        TypoRate = 0.025,
        Fatigue = true,
        WarmUp = true,
        Pace = true,
        PaceSigma = 0.30,
        LapseRate = 0.005,
        MisspellingRate = 0.02,
        AutocorrectEnabled = false,
        DwellEnabled = true,
        DwellMeanMs = 90.0,
        DwellSigmaMs = 12.0,
        RolloverEnabled = true,
        RolloverProbability = 0.40,
    };

    /// <summary>
    /// Fast skilled touch-typist: 110+ WPM, tight timing, low error rate, short dwell with
    /// small variance, and frequent key overlap (high rollover probability) as seen in
    /// expert typists.
    /// </summary>
    public static TypingProfile FastTouchTypist() => new()
    {
        Wpm = 110,
        Jitter = 0.22,
        TypoRate = 0.012,
        Fatigue = true,
        WarmUp = true,
        Pace = true,
        PaceSigma = 0.25,
        LapseRate = 0.002,
        MisspellingRate = 0.01,
        AutocorrectEnabled = false,
        DwellEnabled = true,
        DwellMeanMs = 80.0,
        DwellSigmaMs = 8.0,
        RolloverEnabled = true,
        RolloverProbability = 0.70,
    };

    /// <summary>
    /// Mobile / autocorrect thumb-typist: slower, high mechanical error rate skewed toward
    /// omissions and missed doubles (fat-finger taps), no rollover, and an autocorrect pass
    /// that bulk-replaces mistyped words instead of human backspacing.
    /// </summary>
    public static TypingProfile MobileAutocorrect() => new()
    {
        Wpm = 38,
        Jitter = 0.40,
        TypoRate = 0.08,
        Fatigue = true,
        WarmUp = true,
        Pace = true,
        PaceSigma = 0.30,
        LapseRate = 0.005,
        MisspellingRate = 0.03,
        AutocorrectEnabled = true,
        DwellEnabled = true,
        DwellMeanMs = 95.0,
        DwellSigmaMs = 18.0,
        RolloverEnabled = false,
        RolloverProbability = 0.0,
        ErrorMix = new ErrorMix
        {
            AdjacentSlip = 0.55,
            RepeatedKey = 0.08,
            Omission = 0.18,
            Transposition = 0.02,
            ShiftMistime = 0.04,
            MissingDouble = 0.08,
        },
    };
}
