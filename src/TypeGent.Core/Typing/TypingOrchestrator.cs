using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TypeGent.Core.Abstractions;
using TypeGent.Core.Layouts;

namespace TypeGent.Core.Typing;

/// <summary>
/// Drives a sequence of <see cref="TimedAction"/>s against an <see cref="IKeyboardBackend"/>:
/// wait the delay, dispatch the action, repeat — bailing out promptly on cancellation.
/// <para>
/// Phase 2 note: there is no human-timing model yet. The orchestrator simply waits the
/// delay carried by each <see cref="TimedAction"/>. The HumanTypingEngine (Phase 4) is what
/// will produce realistic delays for it to consume.
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

            _logger.LogTrace("Dispatching {Action}", timed.Action);
            await _backend.ExecuteAsync(timed.Action, ct).ConfigureAwait(false);
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
