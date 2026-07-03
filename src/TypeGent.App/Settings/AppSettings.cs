using TypeGent.App.ViewModels;

namespace TypeGent.App.Settings;

/// <summary>
/// A plain serializable snapshot of the user's preferences. The fields mirror the
/// <see cref="MainViewModel"/> tunable properties; <c>Text</c> is deliberately excluded — the
/// text box is document content, not a setting. Saved to and loaded from <c>settings.json</c> by
/// <see cref="JsonSettingsStore"/>.
/// </summary>
public sealed class AppSettings
{
    public int Wpm { get; set; } = 60;
    public double Jitter { get; set; } = 0.35;
    public double TypoRate { get; set; } = 0.02;
    public bool Fatigue { get; set; } = true;
    public KeyboardLayoutKind LayoutKind { get; set; } = KeyboardLayoutKind.UsQwerty;
    public HotKeyKind HotKey { get; set; } = HotKeyKind.CtrlShiftT;
    public bool Topmost { get; set; }
}
