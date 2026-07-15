using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TypeGent.Core.Abstractions;
using TypeGent.Core.Layouts;

namespace TypeGent.Core.Typing;

/// <summary>
/// Drives a sequence of <see cref="TimedAction"/>s against an <see cref="IKeyboardBackend"/>:
/// wait the delay, dispatch the action, repeat — bailing out promptly on cancellation.
/// <para>
/// Phase 9 note: when a <see cref="TimedAction"/> carries a non-null <see cref="TimedAction.HoldMs"/>,
/// the orchestrator expands it into a <see cref="KeyAction.KeyDown"/> → wait hold → <see cref="KeyAction.KeyUp"/>
/// sequence instead of an atomic press. The existing path (HoldMs == null) is unchanged.
/// </para>
/// </summary>
public sealed class TypingOrchestrator(IKeyboardBackend backend, ILogger<TypingOrchestrator>? logger = null)
{
    private readonly IKeyboardBackend _backend = backend
        ?? throw new ArgumentNullException(nameof(backend));
    private readonly ILogger _logger = logger ?? NullLogger<TypingOrchestrator>.Instance;

    /// <summary>
    /// Execute <paramref name="actions"/> in order. The sequence is enumerated lazily so a
    /// streaming engine can be cancelled cleanly mid-text.
    /// </summary>
    public async Task RunAsync(IEnumerable<TimedAction> actions, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actions);

        foreach (var timed in actions)
        {
            ct.ThrowIfCancellationRequested();

            if (timed.Delay > TimeSpan.Zero)
            {
                await Task.Delay(timed.Delay, ct).ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();

            // ── Phase 9: down/up expansion ────────────────────────────────────────────
            // When HoldMs is set, expand Press → KeyDown + hold + KeyUp, and
            // Chord → KeyDown(mod) + KeyDown(base) + hold + KeyUp(base) + KeyUp(mod).
            // Text has no separate down/up concept and always uses the atomic path.
            if (timed.HoldMs.HasValue && timed.Action is not KeyAction.Text)
            {
                var holdSpan = TimeSpan.FromMilliseconds(timed.HoldMs.Value);

                switch (timed.Action)
                {
                    case KeyAction.Press press:
                        _logger.LogTrace("KeyDown {Key}", press.Key);
                        await _backend.ExecuteAsync(new KeyAction.KeyDown(press.Key), ct).ConfigureAwait(false);

                        ct.ThrowIfCancellationRequested();
                        if (holdSpan > TimeSpan.Zero)
                            await Task.Delay(holdSpan, ct).ConfigureAwait(false);

                        ct.ThrowIfCancellationRequested();
                        _logger.LogTrace("KeyUp {Key}", press.Key);
                        await _backend.ExecuteAsync(new KeyAction.KeyUp(press.Key), ct).ConfigureAwait(false);
                        break;

                    case KeyAction.Chord chord:
                        _logger.LogTrace("KeyDown {Mod}, KeyDown {Base}", chord.Modifier, chord.Base);
                        await _backend.ExecuteAsync(new KeyAction.KeyDown(chord.Modifier), ct).ConfigureAwait(false);
                        ct.ThrowIfCancellationRequested();
                        await _backend.ExecuteAsync(new KeyAction.KeyDown(chord.Base), ct).ConfigureAwait(false);

                        ct.ThrowIfCancellationRequested();
                        if (holdSpan > TimeSpan.Zero)
                            await Task.Delay(holdSpan, ct).ConfigureAwait(false);

                        ct.ThrowIfCancellationRequested();
                        _logger.LogTrace("KeyUp {Base}, KeyUp {Mod}", chord.Base, chord.Modifier);
                        await _backend.ExecuteAsync(new KeyAction.KeyUp(chord.Base), ct).ConfigureAwait(false);
                        ct.ThrowIfCancellationRequested();
                        await _backend.ExecuteAsync(new KeyAction.KeyUp(chord.Modifier), ct).ConfigureAwait(false);
                        break;

                    // KeyDown / KeyUp passed directly (e.g. from a future pre-expanded stream)
                    default:
                        _logger.LogTrace("Dispatching {Action}", timed.Action);
                        await _backend.ExecuteAsync(timed.Action, ct).ConfigureAwait(false);
                        break;
                }
            }
            else
            {
                // ── Legacy atomic path (HoldMs == null) ──────────────────────────────
                _logger.LogTrace("Dispatching {Action}", timed.Action);
                await _backend.ExecuteAsync(timed.Action, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Type <paramref name="text"/> through <paramref name="layout"/>, one character at a time,
    /// waiting <paramref name="perCharDelay"/> before each. Each character is resolved to a
    /// <see cref="KeyAction"/> via <see cref="KeyboardLayout.ToAction"/> — the VK-chord path for
    /// characters the layout knows, the Unicode fallback for everything else.
    /// <para>
    /// Phase 3 uses a flat per-character delay; the realistic, varying timing model lands in Phase 4.
    /// </para>
    /// </summary>
    public Task RunTextAsync(string text, KeyboardLayout layout, TimeSpan perCharDelay, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(layout);

        var actions = text.Select(c => new TimedAction(perCharDelay, layout.ToAction(c)));
        return RunAsync(actions, ct);
    }
}
