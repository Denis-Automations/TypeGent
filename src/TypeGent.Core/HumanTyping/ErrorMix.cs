namespace TypeGent.Core.HumanTyping;

/// <summary>
/// The relative weights of each mechanical-typo kind, used by
/// <see cref="ErrorModel.ChooseKind"/>. v2 Phase 12 introduces this so a
/// <see cref="TypingPersona"/> can skew the error mix (e.g. the mobile persona leans
/// toward omissions) without touching the engine. Weights are relative probabilities;
/// <see cref="ErrorModel"/> normalises by their sum, so they need not sum to 1.
/// <para>
/// <see cref="Default"/> reproduces the measured substitution-dominant mix (v2 Phase 5/6)
/// exactly, so profiles that leave <see cref="TypingProfile.ErrorMix"/> null keep their
/// stable RNG draw order and identical results.
/// </para>
/// </summary>
public sealed record ErrorMix
{
    /// <summary>Hitting a physically adjacent key instead of the intended one (substitution).</summary>
    public double AdjacentSlip { get; init; } = 0.84;

    /// <summary>Striking a key one or two extra times.</summary>
    public double RepeatedKey { get; init; } = 0.05;

    /// <summary>Skip the intended key entirely (always corrected via delayed detection).</summary>
    public double Omission { get; init; } = 0.03;

    /// <summary>Swap the order of the current and next letter.</summary>
    public double Transposition { get; init; } = 0.015;

    /// <summary>Shift held a beat too long, capitalising a letter that should be lower-case.</summary>
    public double ShiftMistime { get; init; } = 0.06;

    /// <summary>Type a double-letter pair only once.</summary>
    public double MissingDouble { get; init; } = 0.005;

    /// <summary>The measured substitution-dominant mix (v2 Phase 5/6). Identical to the
    /// pre-Phase-12 hardcoded weights.</summary>
    public static ErrorMix Default { get; } = new();
}
