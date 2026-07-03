using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using TypeGent.App.ViewModels;

namespace TypeGent.App.Native;

/// <summary>
/// Registers a single system-wide hotkey via <c>user32!RegisterHotKey</c> and surfaces the
/// <c>WM_HOTKEY</c> message as a .NET event (Phase 6). Initialized with the main window's HWND so
/// the OS can post hotkey messages into our message queue; we hook <c>WndProc</c> to raise
/// <see cref="HotKeyPressed"/> on the UI thread.
/// <para>
/// Registration failure (e.g. the binding is already taken by another app) is surfaced via
/// <see cref="RegistrationFailed"/> so the ViewModel can tell the user to rebind.
/// </para>
/// </summary>
public sealed class HotKeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HotKeyId = 9000; // we only ever register one hotkey at a time

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private IntPtr _hwnd;
    private HwndSource? _source;
    private bool _registered;
    private bool _disposed;

    /// <summary>Raised on the UI thread when the registered hotkey is pressed system-wide.</summary>
    public event EventHandler? HotKeyPressed;

    /// <summary>
    /// Raised when <see cref="Register"/> fails (the binding is unavailable); carries a friendly
    /// message with a rebind suggestion.
    /// </summary>
    public event EventHandler<RegistrationFailedEventArgs>? RegistrationFailed;

    /// <summary>Attach to the window's message pump. Call once the HWND is available (OnSourceInitialized).</summary>
    public void Initialize(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);
    }

    /// <summary>
    /// Register <paramref name="kind"/> as the active hotkey, replacing any previous binding.
    /// Returns <c>false</c> (and raises <see cref="RegistrationFailed"/>) when the OS rejects it.
    /// </summary>
    public bool Register(HotKeyKind kind)
    {
        Unregister();

        var (modifiers, vk, label) = Map(kind);
        _registered = RegisterHotKey(_hwnd, HotKeyId, modifiers, vk);

        if (!_registered)
        {
            RegistrationFailed?.Invoke(this, new RegistrationFailedEventArgs(
                $"Could not register the hotkey '{label}' — it may already be in use by another app. " +
                "Pick a different hotkey in the dropdown."));
        }

        return _registered;
    }

    /// <summary>Unregister the current hotkey (no-op if none is registered).</summary>
    public void Unregister()
    {
        if (_registered)
        {
            UnregisterHotKey(_hwnd, HotKeyId);
            _registered = false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // WM_HOTKEY: wParam holds the hotkey id we passed to RegisterHotKey.
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotKeyId)
        {
            HotKeyPressed?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    /// <summary>Map a UI enum value to RegisterHotKey modifiers + virtual-key code, with a display label.</summary>
    private static (uint modifiers, uint vk, string label) Map(HotKeyKind kind) => kind switch
    {
        HotKeyKind.CtrlShiftT => (MOD_CONTROL | MOD_SHIFT, 0x54, "Ctrl+Shift+T"),
        HotKeyKind.CtrlShiftSpace => (MOD_CONTROL | MOD_SHIFT, 0x20, "Ctrl+Shift+Space"),
        HotKeyKind.CtrlAltT => (MOD_CONTROL | MOD_ALT, 0x54, "Ctrl+Alt+T"),
        HotKeyKind.F8 => (0, 0x77, "F8"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown hotkey."),
    };

    public void Dispose()
    {
        if (_disposed) return;
        Unregister();
        _source?.RemoveHook(WndProc);
        _disposed = true;
    }

    /// <summary>Carries a user-facing message explaining why hotkey registration failed.</summary>
    public sealed class RegistrationFailedEventArgs(string message) : EventArgs
    {
        public string Message { get; } = message;
    }
}
