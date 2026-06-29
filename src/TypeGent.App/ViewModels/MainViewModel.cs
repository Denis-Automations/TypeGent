using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TypeGent.App.Native;
using TypeGent.Core.HumanTyping;
using TypeGent.Core.Layouts;
using TypeGent.Core.Typing;

namespace TypeGent.App.ViewModels;

/// <summary>
/// The single ViewModel behind <c>MainWindow</c>. Holds the tunable typing profile (bound to the
/// sliders/checkboxes), exposes the live character count + estimated time, and drives Start/Stop.
/// <para>
/// Start runs a short countdown so the user can focus their target window, then plans the text
/// with <see cref="HumanTypingEngine"/> and streams it through the <see cref="TypingOrchestrator"/>.
/// Stop (and Escape, wired in XAML) cancel the run via a <see cref="CancellationTokenSource"/>.
/// </para>
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly TypingOrchestrator _orchestrator;
    private readonly KeyboardLayout _layout;
    private CancellationTokenSource? _cts;

    public MainViewModel(TypingOrchestrator orchestrator, KeyboardLayout layout)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
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
    private string status = "Idle. Click Start, then focus your target window during the countdown.";

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

            // Refuse to type into ourselves — the user forgot to focus their target.
            if (OwnWindowHandle != IntPtr.Zero && ForegroundWindow.Current == OwnWindowHandle)
            {
                Status = "Click into the target app first — TypeGent won't type into its own window.";
                return;
            }

            Status = "Typing… (Stop or Escape to cancel)";

            var profile = new TypingProfile
            {
                Wpm = Wpm,
                Jitter = Jitter,
                TypoRate = TypoRate,
                Fatigue = Fatigue,
            };

            // Time-seeded RNG in the app (deterministic seeds are for tests). v1 has one layout.
            var engine = new HumanTypingEngine(new Random());
            var actions = engine.Plan(Text, profile, _layout);
            await _orchestrator.RunAsync(actions, ct);

            Status = "Done.";
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
            IsTyping = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        _cts?.Cancel();
        Status = "Stopping…";
    }
}
