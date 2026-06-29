using TypeGent.Core.Typing;

namespace TypeGent.Core.Layouts;

/// <summary>
/// Maps characters to the physical key (plus Shift state) that produces them on a given
/// keyboard layout. v1 ships only <see cref="UsQwertyLayout"/>; the abstraction stays so
/// UK QWERTY / AZERTY / Dvorak / Colemak are cheap to add in v2 (see plan.md Phase 8).
/// </summary>
public abstract class KeyboardLayout
{
    /// <summary>Human-readable name shown in the UI (e.g. "US QWERTY").</summary>
    public abstract string Name { get; }

    /// <summary>True if this layout can express <paramref name="c"/> as a key (+ optional Shift).</summary>
    public abstract bool CanMap(char c);

    /// <summary>The virtual key for <paramref name="c"/>. Precondition: <see cref="CanMap"/> is true.</summary>
    public abstract VirtualKey MapChar(char c);

    /// <summary>True if producing <paramref name="c"/> requires holding Shift.</summary>
    public abstract bool NeedsShift(char c);

    /// <summary>Every character this layout can express via the VK-chord path.</summary>
    public abstract IEnumerable<char> SupportedChars { get; }

    /// <summary>
    /// Translate a character into the <see cref="KeyAction"/> that produces it:
    /// a mappable char becomes a <see cref="KeyAction.Press"/> (or a Shift
    /// <see cref="KeyAction.Chord"/>); anything the layout can't express falls back to the
    /// Unicode (VK_PACKET) <see cref="KeyAction.Text"/> path. Shared by the orchestrator
    /// (Phase 3) and the HumanTypingEngine (Phase 4) so both resolve chars identically.
    /// </summary>
    public KeyAction ToAction(char c)
    {
        if (!CanMap(c))
        {
            return new KeyAction.Text(c.ToString());
        }

        var vk = MapChar(c);
        return NeedsShift(c)
            ? new KeyAction.Chord(VirtualKey.Shift, vk)
            : new KeyAction.Press(vk);
    }
}
