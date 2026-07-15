namespace TypeGent.Core.Typing;

/// <summary>
/// A <see cref="KeyAction"/> paired with the delay to wait <em>before</em> performing it,
/// and an optional hold duration for the key (v2 Phase 9).
/// <para>
/// When <see cref="HoldMs"/> is <see langword="null"/> (the default) the orchestrator performs
/// the action atomically — this is the pre-Phase-9 behaviour and is the path used by all
/// existing code. When <see cref="HoldMs"/> is set and the action is a <see cref="KeyAction.Press"/>
/// or <see cref="KeyAction.Chord"/>, the orchestrator expands the action into a
/// <see cref="KeyAction.KeyDown"/> → wait <see cref="HoldMs"/> ms → <see cref="KeyAction.KeyUp"/>
/// sequence. <see cref="KeyAction.Text"/> ignores <see cref="HoldMs"/> (Unicode path has no
/// separate down/up concept).
/// </para>
/// </summary>
/// <param name="Delay">How long to wait before performing <paramref name="Action"/>.</param>
/// <param name="Action">The action to perform.</param>
/// <param name="HoldMs">
/// Optional key-hold (dwell) duration in milliseconds (v2 Phase 9). <see langword="null"/>
/// means use the legacy atomic press. Set by <c>DelayModel</c> starting in Phase 10.
/// </param>
public sealed record TimedAction(TimeSpan Delay, KeyAction Action, double? HoldMs = null);
