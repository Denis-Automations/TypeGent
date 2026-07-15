namespace TypeGent.Core.Typing;

/// <summary>
/// A single thing the backend can do at the keyboard. Variants:
/// <list type="bullet">
///   <item><see cref="Press"/> — tap one key (atomic down+up).</item>
///   <item><see cref="Chord"/> — hold a modifier and tap a base key (e.g. Shift+,).</item>
///   <item><see cref="Text"/> — emit a literal string via the Unicode (VK_PACKET) path,
///   used as the fallback for characters the layout can't express.</item>
///   <item><see cref="KeyDown"/> — press and hold a key without releasing (Phase 9).</item>
///   <item><see cref="KeyUp"/> — release a previously-held key (Phase 9).</item>
/// </list>
/// </summary>
public abstract record KeyAction
{
    private KeyAction() { }

    /// <summary>Tap a single virtual key (atomic down+up).</summary>
    public sealed record Press(VirtualKey Key) : KeyAction;

    /// <summary>Hold <paramref name="Modifier"/> while tapping <paramref name="Base"/>.</summary>
    public sealed record Chord(VirtualKey Modifier, VirtualKey Base) : KeyAction;

    /// <summary>Emit a literal string via the Unicode fallback.</summary>
    public sealed record Text(string Value) : KeyAction;

    /// <summary>
    /// Press and hold <paramref name="Key"/> without releasing it.
    /// Must be paired with a subsequent <see cref="KeyUp"/> on the same key.
    /// Used by the orchestrator when <see cref="TimedAction.HoldMs"/> is set (v2 Phase 9).
    /// </summary>
    public sealed record KeyDown(VirtualKey Key) : KeyAction;

    /// <summary>
    /// Release <paramref name="Key"/> that was previously pressed via <see cref="KeyDown"/>.
    /// Used by the orchestrator when <see cref="TimedAction.HoldMs"/> is set (v2 Phase 9).
    /// </summary>
    public sealed record KeyUp(VirtualKey Key) : KeyAction;
}
