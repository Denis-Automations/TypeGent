using TypeGent.App.ViewModels;

namespace TypeGent.App.Settings;

/// <summary>
/// A plain serializable snapshot of the user's preferences (v2 Phase 12). The per-typing
/// knobs live in <see cref="Profiles"/> (a list of <see cref="NamedProfile"/>); window-level
/// settings (layout, hotkey, topmost) sit alongside. Saved to and loaded from
/// <c>settings.json</c> by <see cref="JsonSettingsStore"/>.
/// <para>
/// The nullable legacy fields (<see cref="Wpm"/>, <see cref="Jitter"/>, …) exist only to
/// detect and migrate the pre-Phase-12 flat settings format. They are never written by the
/// current code (<c>JsonIgnoreCondition.WhenWritingNull</c> omits them), so a migrated file
/// becomes clean new format on the next save.
/// </para>
/// </summary>
public sealed class AppSettings
{
    /// <summary>The user's saved named profiles. Always non-empty after load/seed.</summary>
    public List<NamedProfile> Profiles { get; set; } = new();

    /// <summary>The name of the profile to select on startup.</summary>
    public string SelectedProfile { get; set; } = "";

    // ── Window-level settings (not per-profile) ──────────────────────────────────
    public KeyboardLayoutKind LayoutKind { get; set; } = KeyboardLayoutKind.UsQwerty;

    public HotKeyKind HotKey { get; set; } = HotKeyKind.CtrlShiftT;

    public bool Topmost { get; set; }

    // ── Legacy flat fields (migration only — never written) ───────────────────────
    public int? Wpm { get; set; }
    public double? Jitter { get; set; }
    public double? TypoRate { get; set; }
    public bool? Fatigue { get; set; }
    public bool? FullRealism { get; set; }

    /// <summary>True when the loaded file is the pre-Phase-12 flat format awaiting migration.</summary>
    public bool HasLegacyFlatFields =>
        Wpm.HasValue || Jitter.HasValue || TypoRate.HasValue
        || Fatigue.HasValue || FullRealism.HasValue;
}
