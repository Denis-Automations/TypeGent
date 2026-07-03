using System;
using System.Runtime.InteropServices;

namespace TypeGent.App.Native;

/// <summary>
/// Thin wrapper over <c>user32!GetForegroundWindow</c>. Used by <see cref="MainViewModel"/> to
/// capture the target window on start (and refuse typing into TypeGent's own window), by
/// <see cref="ProcessElevation"/> to read the target's integrity level, and by the focus-drift
/// monitor that warns the user if focus leaves the target mid-type.
/// </summary>
internal static class ForegroundWindow
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    /// <summary>The handle of the window the user is currently focused on, OS-wide.</summary>
    public static IntPtr Current => GetForegroundWindow();
}
