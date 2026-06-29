namespace TypeGent.Core.Typing;

/// <summary>
/// A <see cref="KeyAction"/> paired with the delay to wait <em>before</em> performing it.
/// The HumanTypingEngine (Phase 4) produces a stream of these; the orchestrator waits the
/// delay, then dispatches the action.
/// </summary>
/// <param name="Delay">How long to wait before performing <paramref name="Action"/>.</param>
/// <param name="Action">The action to perform.</param>
public sealed record TimedAction(TimeSpan Delay, KeyAction Action);
