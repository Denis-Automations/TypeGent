using System;
using System.Runtime.InteropServices;

namespace TypeGent.App.Native;

/// <summary>
/// Thin wrapper over <c>user32!GetForegroundWindow</c>. Phase 5 uses it for one check only:
/// after the Start countdown, make sure the foreground window isn't TypeGent's own window, so
/// we show a friendly "click into the target app first" message instead of typing into ourselves.
/// The full target-window validation (elevation, focus-change detection) arrives in Phase 6.
/// </summary>
internal static class ForegroundWindow
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    /// <summary>The handle of the window the user is currently focused on, OS-wide.</summary>
    public static IntPtr Current => GetForegroundWindow();
}
