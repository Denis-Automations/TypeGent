using System;
using System.Collections.ObjectModel;
using System.Linq;
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
/// v2 Phase 12: the raw knobs are now driven by a <see cref="PersonaKind"/> selection mapped to a
/// <c>TypingPersona</c> factory in Core. Users can save, name, switch, and cycle multiple
/// <see cref="NamedProfile"/>s. The Phase A1 "Full realism" toggle survives as a per-profile
/// advanced override that gates the persona's realism layers.
/// </para>
/// <para>
/// Start (button) runs a short countdown so the user can focus their target window, then plans the
/// text with <see cref="HumanTypingEngine"/> and streams it through the <see cref="TypingOrchestrator"/>.
/// The system-wide hotkey (Phase 6) starts typing immediately from the focused window. Stop (and
/// Escape, wired in XAML) cancel the run via a <see cref="CancellationTokenSource"/>. A second
/// hotkey (Ctrl+Shift+P, Phase 12) cycles the selected profile.
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

    /// <summary>
    /// Suppresses change handlers while programmatically loading a profile/persona into the
    /// sliders, so loading doesn't feed back into "user changed the persona" logic.
    /// </summary>
    private bool _loading;

    public MainViewModel(TypingOrchestrator orchestrator, KeyboardLayout layout, HotKeyManager hotKeyManager, ISettingsStore settingsStore)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _hotKeyManager = hotKeyManager ?? throw new ArgumentNullException(nameof(hotKeyManager));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));

        _hotKeyManager.HotKeyPressed += OnHotKeyPressed;
        _hotKeyManager.ProfileCycleRequested += OnProfileCycleRequested;
        _hotKeyManager.RegistrationFailed += OnRegistrationFailed;

        LoadSettings();
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

    /// <summary>
    /// Master realism gate (carried over from Phase A1 as a per-profile advanced override).
    /// When off, the runtime profile disables dwell, rollover, misspellings, pace, and lapses —
    /// the plain atomic Phase 1–8 path — regardless of the selected persona.
    /// </summary>
    [ObservableProperty]
    private bool fullRealism = true;

    [ObservableProperty]
    private PersonaKind selectedPersona = PersonaKind.Average;

    [ObservableProperty]
    private NamedProfile? selectedProfile;

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

    /// <summary>The user's saved named profiles, bound to the profiles dropdown.</summary>
    public ObservableCollection<NamedProfile> Profiles { get; } = new();

    /// <summary>The dropdown options for personas and layouts/hotkeys.</summary>
    public PersonaKind[] PersonaOptions { get; } = Enum.GetValues<PersonaKind>();

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

    // ── Settings load / migrate / seed ─────────────────────────────────────────────

    private void LoadSettings()
    {
        var saved = _settingsStore.Load();
        if (saved is { } s)
        {
            LayoutKind = s.LayoutKind;
            HotKey = s.HotKey;
            Topmost = s.Topmost;

            if (s.Profiles.Count > 0)
            {
                foreach (var p in s.Profiles) Profiles.Add(p);
            }
            else if (s.HasLegacyFlatFields)
            {
                // Pre-Phase-12 flat format → migrate into one Custom profile so nobody loses settings.
                Profiles.Add(MigrateFromFlat(s));
            }
            else
            {
                SeedDefaultProfiles();
            }
        }
        else
        {
            SeedDefaultProfiles();
        }

        var selectedName = saved?.SelectedProfile ?? "";
        var selected = Profiles.FirstOrDefault(p => p.Name == selectedName) ?? Profiles[0];
        SelectedProfile = selected; // triggers OnSelectedProfileChanged → ApplyProfile
    }

    private void SeedDefaultProfiles()
    {
        Profiles.Add(ProfileFromPersona("Hunt & peck", PersonaKind.HuntAndPeck));
        Profiles.Add(ProfileFromPersona("Average", PersonaKind.Average));
        Profiles.Add(ProfileFromPersona("Fast touch-typist", PersonaKind.FastTouchTypist));
        Profiles.Add(ProfileFromPersona("Mobile / autocorrect", PersonaKind.MobileAutocorrect));
    }

    private static NamedProfile MigrateFromFlat(AppSettings s) => new()
    {
        Name = "Custom (imported)",
        Persona = PersonaKind.Custom,
        Wpm = s.Wpm ?? 60,
        Jitter = s.Jitter ?? 0.35,
        TypoRate = s.TypoRate ?? 0.02,
        Fatigue = s.Fatigue ?? true,
        FullRealism = s.FullRealism ?? true,
    };

    /// <summary>Snapshot a persona's slider-visible defaults into a NamedProfile shell.</summary>
    private static NamedProfile ProfileFromPersona(string name, PersonaKind kind)
    {
        var p = ProfileForPersona(kind);
        return new NamedProfile
        {
            Name = name,
            Persona = kind,
            Wpm = p.Wpm,
            Jitter = p.Jitter,
            TypoRate = p.TypoRate,
            Fatigue = p.Fatigue,
            FullRealism = true,
        };
    }

    /// <summary>Map a UI persona to its Core factory profile. Custom falls back to the Average
    /// base; the user's sliders overlay WPM/jitter/typo/fatigue at runtime.</summary>
    private static TypingProfile ProfileForPersona(PersonaKind kind) => kind switch
    {
        PersonaKind.HuntAndPeck => TypingPersona.HuntAndPeck(),
        PersonaKind.Average => TypingPersona.Average(),
        PersonaKind.FastTouchTypist => TypingPersona.FastTouchTypist(),
        PersonaKind.MobileAutocorrect => TypingPersona.MobileAutocorrect(),
        _ => TypingPersona.Average(), // Custom
    };

    private static string FriendlyPersona(PersonaKind kind) => kind switch
    {
        PersonaKind.HuntAndPeck => "Hunt & peck",
        PersonaKind.Average => "Average typist",
        PersonaKind.FastTouchTypist => "Fast touch-typist",
        PersonaKind.MobileAutocorrect => "Mobile / autocorrect",
        PersonaKind.Custom => "Custom",
        _ => kind.ToString(),
    };

    /// <summary>Push a profile's persona + slider values into the bound properties (guarded).</summary>
    private void ApplyProfile(NamedProfile p)
    {
        _loading = true;
        try
        {
            SelectedPersona = p.Persona;
            Wpm = p.Wpm;
            Jitter = p.Jitter;
            TypoRate = p.TypoRate;
            Fatigue = p.Fatigue;
            FullRealism = p.FullRealism;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Profile dropdown selection changed (user pick or programmatic switch).</summary>
    partial void OnSelectedProfileChanged(NamedProfile? value)
    {
        if (value is null || _loading) return;
        ApplyProfile(value);
        Status = $"Loaded profile '{value.Name}'.";
    }

    /// <summary>Persona dropdown selection changed: repopulate sliders from the persona defaults.</summary>
    partial void OnSelectedPersonaChanged(PersonaKind value)
    {
        if (_loading) return;

        if (value == PersonaKind.Custom)
        {
            if (SelectedProfile is { } p) p.Persona = value;
            Status = "Custom persona — set your own values with the sliders.";
            return;
        }

        var persona = ProfileForPersona(value);
        _loading = true;
        try
        {
            Wpm = persona.Wpm;
            Jitter = persona.Jitter;
            TypoRate = persona.TypoRate;
            Fatigue = persona.Fatigue;
            FullRealism = true;
        }
        finally
        {
            _loading = false;
        }

        // Keep the selected profile's stored values in sync with the repopulated sliders so a
        // round-trip (switch away and back) shows the same state, and a later Save is a no-op.
        if (SelectedProfile is { } pp)
        {
            pp.Persona = value;
            pp.Wpm = persona.Wpm;
            pp.Jitter = persona.Jitter;
            pp.TypoRate = persona.TypoRate;
            pp.Fatigue = persona.Fatigue;
            pp.FullRealism = true;
        }
        Status = $"Persona: {FriendlyPersona(value)}.";
    }

    // ── Profile management commands ────────────────────────────────────────────────

    [RelayCommand]
    private void SaveProfile()
    {
        if (SelectedProfile is not { } p) return;
        p.Persona = SelectedPersona;
        p.Wpm = Wpm;
        p.Jitter = Jitter;
        p.TypoRate = TypoRate;
        p.Fatigue = Fatigue;
        p.FullRealism = FullRealism;
        PersistSettings();
        Status = $"Saved profile '{p.Name}'.";
    }

    [RelayCommand]
    private void SaveAsProfile()
    {
        var name = InputDialog.Prompt("Enter a name for this profile:", "Save Profile As…",
            $"Profile {Profiles.Count + 1}");
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();

        if (Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            Status = $"A profile named '{name}' already exists.";
            return;
        }

        var np = new NamedProfile
        {
            Name = name,
            Persona = SelectedPersona,
            Wpm = Wpm,
            Jitter = Jitter,
            TypoRate = TypoRate,
            Fatigue = Fatigue,
            FullRealism = FullRealism,
        };
        Profiles.Add(np);
        SelectedProfile = np;
        PersistSettings();
        Status = $"Saved new profile '{name}'.";
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile is not { } p) return;
        if (Profiles.Count <= 1)
        {
            Status = "Cannot delete the last remaining profile.";
            return;
        }

        var idx = Profiles.IndexOf(p);
        Profiles.RemoveAt(idx);
        var next = Profiles[Math.Min(idx, Profiles.Count - 1)];
        SelectedProfile = next;
        PersistSettings();
        Status = $"Deleted '{p.Name}'; using '{next.Name}'.";
    }

    private void PersistSettings()
    {
        _settingsStore.Save(new AppSettings
        {
            Profiles = Profiles.ToList(),
            SelectedProfile = SelectedProfile?.Name ?? "",
            LayoutKind = LayoutKind,
            HotKey = HotKey,
            Topmost = Topmost,
        });
    }

    // ── Hotkey wiring ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Hook the hotkeys into the window's message pump and register the current selection plus
    /// the profile-cycle hotkey. Called by the View once the HWND is available.
    /// </summary>
    public void InitializeHotKey(IntPtr hwnd)
    {
        _hotKeyManager.Initialize(hwnd);
        _hotKeyManager.Register(HotKey);
        _hotKeyManager.RegisterCycle();
    }

    /// <summary>
    /// Re-register when the user picks a new hotkey in the dropdown.
    /// </summary>
    partial void OnHotKeyChanged(HotKeyKind value)
    {
        if (OwnWindowHandle != IntPtr.Zero)
            _hotKeyManager.Register(value);
    }

    private void OnProfileCycleRequested(object? sender, EventArgs e)
    {
        if (Profiles.Count == 0) return;
        var idx = SelectedProfile is { } cur ? Profiles.IndexOf(cur) : -1;
        SelectedProfile = Profiles[(idx + 1) % Profiles.Count]; // triggers load + status update
    }

    /// <summary>Save settings and release the hotkeys. Called by the View in <c>OnClosed</c>.</summary>
    public void Shutdown()
    {
        PersistSettings();
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
    /// Build the typing plan from the selected persona (overlaid with the user's slider values and
    /// gated by the realism toggle) and stream it through the orchestrator, while a background
    /// monitor watches for focus drift.
    /// </summary>
    private async Task RunTypingAsync(CancellationToken ct)
    {
        _targetWindowHandle = ForegroundWindow.Current;
        Status = "Typing… (Stop or Escape to cancel)";

        // Focus-drift monitor: linked to the typing token so it stops on completion/cancel/stop.
        _focusCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = MonitorFocusAsync(_focusCts.Token);

        // The persona supplies the internal realism knobs; the sliders overlay the user-visible
        // values; FullRealism gates the biometric + cognitive layers as an advanced override.
        var persona = ProfileForPersona(SelectedPersona);
        var profile = new TypingProfile
        {
            Wpm = Wpm,
            Jitter = Jitter,
            TypoRate = TypoRate,
            Fatigue = Fatigue,
            WarmUp = persona.WarmUp,
            Pace = FullRealism ? persona.Pace : false,
            PaceSigma = persona.PaceSigma,
            LapseRate = FullRealism ? persona.LapseRate : 0.0,
            LapseMinMs = persona.LapseMinMs,
            LapseMaxMs = persona.LapseMaxMs,
            MisspellingRate = FullRealism ? persona.MisspellingRate : 0.0,
            AutocorrectEnabled = FullRealism && persona.AutocorrectEnabled,
            DwellEnabled = FullRealism ? persona.DwellEnabled : false,
            DwellMeanMs = persona.DwellMeanMs,
            DwellSigmaMs = persona.DwellSigmaMs,
            RolloverEnabled = FullRealism ? persona.RolloverEnabled : false,
            RolloverProbability = persona.RolloverProbability,
            ErrorMix = persona.ErrorMix,
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
