namespace TypeGent.Core.Typing;

/// <summary>
/// A single thing the backend can do at the keyboard. Variants:
/// <list type="bullet">
///   <item><see cref="Press"/> — tap one key.</item>
///   <item><see cref="Chord"/> — hold a modifier and tap a base key (e.g. Shift+,).</item>
///   <item><see cref="Text"/> — emit a literal string via the Unicode (VK_PACKET) path,
///   used as the fallback for characters the layout can't express.</item>
/// </list>
/// </summary>
public abstract record KeyAction
{
    private KeyAction() { }

    /// <summary>Tap a single virtual key.</summary>
    public sealed record Press(VirtualKey Key) : KeyAction;

    /// <summary>Hold <paramref name="Modifier"/> while tapping <paramref name="Base"/>.</summary>
    public sealed record Chord(VirtualKey Modifier, VirtualKey Base) : KeyAction;

    /// <summary>Emit a literal string via the Unicode fallback.</summary>
    public sealed record Text(string Value) : KeyAction;
}
