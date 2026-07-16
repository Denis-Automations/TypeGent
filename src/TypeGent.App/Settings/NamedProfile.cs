using TypeGent.App.ViewModels;

namespace TypeGent.App.Settings;

/// <summary>
/// A user-named, persistable typing profile (v2 Phase 12). Stores the persona archetype
/// plus the user-visible slider/checkbox values. The persona (via <c>TypingPersona</c> in
/// Core) supplies the internal realism knobs (dwell, rollover, misspellings, pace, lapses);
/// the values here are what the UI binds and what gets round-tripped to
/// <c>settings.json</c>.
/// </summary>
public sealed class NamedProfile
{
    public string Name { get; set; } = "";

    public PersonaKind Persona { get; set; } = PersonaKind.Average;

    public int Wpm { get; set; } = 60;

    public double Jitter { get; set; } = 0.35;

    public double TypoRate { get; set; } = 0.02;

    public bool Fatigue { get; set; } = true;

    /// <summary>
    /// Master realism gate (carried over from Phase A1 as an advanced override). When off,
    /// the runtime profile disables dwell, rollover, misspellings, pace, and lapses
    /// regardless of the persona — the plain atomic Phase 1–8 path.
    /// </summary>
    public bool FullRealism { get; set; } = true;
}
