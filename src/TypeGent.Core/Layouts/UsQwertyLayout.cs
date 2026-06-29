using TypeGent.Core.Typing;

namespace TypeGent.Core.Layouts;

/// <summary>
/// The US QWERTY physical layout — the only layout shipped in v1. Each supported character
/// maps to a virtual key plus whether Shift is held. Letters and digits reuse the fact that
/// the VK_* codes equal their ASCII upper-case code points; punctuation lives on the OEM keys.
/// <para>
/// Characters not in the table (accents, em-dashes, emoji, newline, tab) return
/// <see cref="CanMap"/> == false and are routed to the Unicode fallback by
/// <see cref="KeyboardLayout.ToAction"/>. Multi-line input ('\n', '\t') is out of scope for v1.
/// </para>
/// </summary>
public sealed class UsQwertyLayout : KeyboardLayout
{
    private static readonly IReadOnlyDictionary<char, (VirtualKey Vk, bool Shift)> Map = Build();

    public override string Name => "US QWERTY";

    public override bool CanMap(char c) => Map.ContainsKey(c);

    public override VirtualKey MapChar(char c) =>
        Map.TryGetValue(c, out var entry)
            ? entry.Vk
            : throw new ArgumentOutOfRangeException(
                nameof(c), c, "Character is not on the US QWERTY layout; check CanMap(c) first.");

    public override bool NeedsShift(char c) => Map.TryGetValue(c, out var entry) && entry.Shift;

    public override IEnumerable<char> SupportedChars => Map.Keys;

    private static Dictionary<char, (VirtualKey, bool)> Build()
    {
        var m = new Dictionary<char, (VirtualKey, bool)>();

        // Letters: lower-case unshifted, upper-case shifted; both use the same VK
        // (VK_A..VK_Z == 'A'..'Z').
        for (char c = 'a'; c <= 'z'; c++) m[c] = ((VirtualKey)char.ToUpperInvariant(c), false);
        for (char c = 'A'; c <= 'Z'; c++) m[c] = ((VirtualKey)c, true);

        // Number row, unshifted digits (VK_0..VK_9 == '0'..'9').
        for (char c = '0'; c <= '9'; c++) m[c] = ((VirtualKey)c, false);

        // Number row, shifted symbols.
        m['!'] = (VirtualKey.D1, true);
        m['@'] = (VirtualKey.D2, true);
        m['#'] = (VirtualKey.D3, true);
        m['$'] = (VirtualKey.D4, true);
        m['%'] = (VirtualKey.D5, true);
        m['^'] = (VirtualKey.D6, true);
        m['&'] = (VirtualKey.D7, true);
        m['*'] = (VirtualKey.D8, true);
        m['('] = (VirtualKey.D9, true);
        m[')'] = (VirtualKey.D0, true);

        // OEM punctuation keys: unshifted character, then its shifted partner.
        m[';'] = (VirtualKey.OEM_1, false); m[':'] = (VirtualKey.OEM_1, true);
        m['='] = (VirtualKey.OEM_PLUS, false); m['+'] = (VirtualKey.OEM_PLUS, true);
        m[','] = (VirtualKey.OEM_COMMA, false); m['<'] = (VirtualKey.OEM_COMMA, true);
        m['-'] = (VirtualKey.OEM_MINUS, false); m['_'] = (VirtualKey.OEM_MINUS, true);
        m['.'] = (VirtualKey.OEM_PERIOD, false); m['>'] = (VirtualKey.OEM_PERIOD, true);
        m['/'] = (VirtualKey.OEM_2, false); m['?'] = (VirtualKey.OEM_2, true);
        m['`'] = (VirtualKey.OEM_3, false); m['~'] = (VirtualKey.OEM_3, true);
        m['['] = (VirtualKey.OEM_4, false); m['{'] = (VirtualKey.OEM_4, true);
        m['\\'] = (VirtualKey.OEM_5, false); m['|'] = (VirtualKey.OEM_5, true);
        m[']'] = (VirtualKey.OEM_6, false); m['}'] = (VirtualKey.OEM_6, true);
        m['\''] = (VirtualKey.OEM_7, false); m['"'] = (VirtualKey.OEM_7, true);

        // Space bar.
        m[' '] = (VirtualKey.Space, false);

        return m;
    }
}
