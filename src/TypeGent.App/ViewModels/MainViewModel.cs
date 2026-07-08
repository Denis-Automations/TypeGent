using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TypeGent.App.Native;
using TypeGent.App.Settings;
using TypeGent.Core.HumanTyping;
using TypeGent.Core.Layouts;
using TypeGent.Core.Typing;

namespace TypeGent.App.ViewModels;

/// <summary>
/// The single ViewModel behind <c>MainWindow</c>. Holds the tunable typing profile (bound to the
/// sliders/checkboxes), exposes the live character count + estimated time, and drives Start/Stop.
/// <para>
/// Start (button) runs a short countdown so the user can focus their target window, then plans the
/// text with <see cref="HumanTypingEngine"/> and streams it through the <see cref="TypingOrchestrator"/>.
/// The system-wide hotkey (Phase 6) starts typing immediately from the focused window. Stop (and
/// Escape, wired in XAML) cancel the run via a <see cref="CancellationTokenSource"/>.
/// </para>
/// <para>
/// Target validation (Phase 6): typing is refused into TypeGent's own window and into elevated apps
/// when TypeGent itself isn't elevated. A focus-drift monitor warns the user (status bar only) if the
/// foreground window changes mid-type.
/// </para>
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly TypingOrchestrator _orchestrator;
    private readonly KeyboardLayout _layout;
    private readonly HotKeyManager _hotKeyManager;
    private readonly ISettingsStore _settingsStore;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _focusCts;
    private IntPtr _targetWindowHandle;

    public MainViewModel(TypingOrchestrator orchestrator, KeyboardLayout layout, HotKeyManager hotKeyManager, ISettingsStore settingsStore)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _hotKeyManager = hotKeyManager ?? throw new ArgumentNullException(nameof(hotKeyManager));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));

        _hotKeyManager.HotKeyPressed += OnHotKeyPressed;
        _hotKeyManager.RegistrationFailed += OnRegistrationFailed;

        // Restore previously saved settings; null (first run / corrupt file) keeps the defaults.
        if (_settingsStore.Load() is { } saved)
        {
            Wpm = saved.Wpm;
            Jitter = saved.Jitter;
            TypoRate = saved.TypoRate;
            Fatigue = saved.Fatigue;
            LayoutKind = saved.LayoutKind;
            HotKey = saved.HotKey;
            Topmost = saved.Topmost;
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyPropertyChangedFor(nameof(CharacterCountText))]
    [NotifyPropertyChangedFor(nameof(EstimatedTimeText))]
    private string text = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstimatedTimeText))]
    private int wpm = 60;

    [ObservableProperty]
    private double jitter = 0.35;

    [ObservableProperty]
    private double typoRate = 0.02;

    [ObservableProperty]
    private bool fatigue = true;

    [ObservableProperty]
    private KeyboardLayoutKind layoutKind = KeyboardLayoutKind.UsQwerty;

    [ObservableProperty]
    private HotKeyKind hotKey = HotKeyKind.CtrlShiftT;

    [ObservableProperty]
    private bool topmost;

    [ObservableProperty]
    private string status = "Idle. Click Start (or press your hotkey) to begin typing.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool isTyping;

    /// <summary>The dropdown options — single-membered in v1, but data-bound so v2 just grows the enum.</summary>
    public KeyboardLayoutKind[] LayoutOptions { get; } = Enum.GetValues<KeyboardLayoutKind>();

    public HotKeyKind[] HotKeyOptions { get; } = Enum.GetValues<HotKeyKind>();

    /// <summary>TypeGent's own window handle, set by the View in <c>OnSourceInitialized</c>.</summary>
    public IntPtr OwnWindowHandle { get; set; }

    public string CharacterCountText => $"Characters: {Text.Length}";

    /// <summary>
    /// A lower-bound estimate from raw WPM only (5 chars/word). Real runs are longer because of
    /// word-boundary pauses, fatigue, and typo→backspace overhead — hence the "≈" prefix.
    /// </summary>
    public TimeSpan EstimatedTime => TimeSpan.FromMinutes(Text.Length / 5.0 / Math.Max(1, Wpm));

    public string EstimatedTimeText => $"≈ {(int)EstimatedTime.TotalMinutes}:{EstimatedTime.Seconds:00}";

    /// <summary>
    /// Hook the hotkey into the window's message pump and register the current selection. Called by
    /// the View once the HWND is available (<c>OnSourceInitialized</c>).
    /// </summary>
    public void InitializeHotKey(IntPtr hwnd)
    {
        _hotKeyManager.Initialize(hwnd);
        _hotKeyManager.Register(HotKey);
    }

    /// <summary>
    /// Re-register when the user picks a new hotkey in the dropdown.
    /// </summary>
    partial void OnHotKeyChanged(HotKeyKind value)
    {
        if (OwnWindowHandle != IntPtr.Zero)
            _hotKeyManager.Register(value);
    }

    /// <summary>Save settings and release the hotkey. Called by the View in <c>OnClosed</c>.</summary>
    public void Shutdown()
    {
        _settingsStore.Save(new AppSettings
        {
            Wpm = Wpm,
            Jitter = Jitter,
            TypoRate = TypoRate,
            Fatigue = Fatigue,
            LayoutKind = LayoutKind,
            HotKey = HotKey,
            Topmost = Topmost,
        });
        _hotKeyManager.Dispose();
    }

    private void OnHotKeyPressed(object? sender, EventArgs e)
    {
        // Fire-and-forget on the UI thread; StartFromHotKeyAsync guards itself via CanStart.
        _ = StartFromHotKeyAsync();
    }

    private void OnRegistrationFailed(object? sender, HotKeyManager.RegistrationFailedEventArgs e)
        => Status = e.Message;

    private bool CanStart() => !IsTyping && !string.IsNullOrEmpty(Text);

    private bool CanStop() => IsTyping;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        IsTyping = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            for (var i = 3; i >= 1; i--)
            {
                Status = $"Switch to your target window… typing in {i}";
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }

            if (!ValidateTarget(out var error))
            {
                Status = error;
                return;
            }

            await RunTypingAsync(ct);
        }
        catch (OperationCanceledException)
        {
            Status = "Stopped.";
        }
        catch (Exception ex)
        {
            Status = "Error: " + ex.Message;
        }
        finally
        {
            CleanupRun();
        }
    }

    /// <summary>
    /// Hotkey entry point: no countdown — the user pressed the hotkey from inside their target app,
    /// so typing should start immediately.
    /// </summary>
    private async Task StartFromHotKeyAsync()
    {
        if (!CanStart())
            return;

        IsTyping = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            if (!ValidateTarget(out var error))
            {
                Status = error;
                return;
            }

            await RunTypingAsync(ct);
        }
        catch (OperationCanceledException)
        {
            Status = "Stopped.";
        }
        catch (Exception ex)
        {
            Status = "Error: " + ex.Message;
        }
        finally
        {
            CleanupRun();
        }
    }

    /// <summary>
    /// Shared target validation: refuse our own window, and refuse an elevated target when TypeGent
    /// itself isn't elevated. Returns <c>false</c> with a user-facing message in <paramref name="error"/>.
    /// </summary>
    private bool ValidateTarget(out string error)
    {
        var fg = ForegroundWindow.Current;

        if (OwnWindowHandle != IntPtr.Zero && fg == OwnWindowHandle)
        {
            error = "Click into the target app first as TypeGent won't type into its own window.";
            return false;
        }

        if (ProcessElevation.IsHighOrAbove(fg) && !ProcessElevation.IsCurrentProcessElevated())
        {
            error = "The target app is running as admin. TypeGent must run as admin for it to type to your target input field.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Build the typing plan and stream it through the orchestrator, while a background monitor
    /// watches for focus drift.
    /// </summary>
    private async Task RunTypingAsync(CancellationToken ct)
    {
        _targetWindowHandle = ForegroundWindow.Current;
        Status = "Typing… (Stop or Escape to cancel)";

        // Focus-drift monitor: linked to the typing token so it stops on completion/cancel/stop.
        _focusCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = MonitorFocusAsync(_focusCts.Token);

        var profile = new TypingProfile
        {
            Wpm = Wpm,
            Jitter = Jitter,
            TypoRate = TypoRate,
            Fatigue = Fatigue,
            WarmUp = true,
            Pace = true,
            LapseRate = 0.005,
        };

        // Time-seeded RNG in the app (deterministic seeds are for tests). v1 has one layout.
        var engine = new HumanTypingEngine(new Random());
        var actions = engine.Plan(Text, profile, _layout);
        await _orchestrator.RunAsync(actions, ct);

        Status = "Done.";
    }

    /// <summary>
    /// Warn (status bar only) if the foreground window drifts off the target mid-type. Typing
    /// continues — this is an advisory, not a stop — matching plan §6.2.
    /// </summary>
    private async Task MonitorFocusAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                var fg = ForegroundWindow.Current;
                if (fg != _targetWindowHandle && fg != OwnWindowHandle)
                {
                    Status = "Warning: focus left the target window therefore typing continues into the focused app.";
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when typing ends or is stopped.
        }
    }

    private void CleanupRun()
    {
        IsTyping = false;
        _focusCts?.Cancel();
        _focusCts?.Dispose();
        _focusCts = null;
        _cts?.Dispose();
        _cts = null;
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        _cts?.Cancel();
        Status = "Stopping…";
    }
}
