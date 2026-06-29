namespace TypeGent.App.ViewModels;

/// <summary>
/// Keyboard layout choices shown in the UI dropdown. v1 ships only <see cref="UsQwerty"/>;
/// the others are deferred to v2 (see plan.md Phase 8), so this enum has a single member for now.
/// </summary>
public enum KeyboardLayoutKind
{
    UsQwerty,
}

/// <summary>
/// Hotkey choices shown in the UI dropdown. The selection is bound and persisted in the
/// ViewModel in Phase 5; actually registering the system-wide hotkey lands in Phase 6
/// (<c>HotKeyManager</c>), so for now this only drives the dropdown.
/// </summary>
public enum HotKeyKind
{
    CtrlShiftT,
    CtrlShiftSpace,
    CtrlAltT,
    F8,
}
