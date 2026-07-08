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

    // Biomechanical metadata for the 26 letter keys (v2 Phase 3, §2.4).
    // Coordinates in key-width units; rows: top=1 (QWERTY row), home=0 (ASDF row),
    // bottom=−1 (ZXCV row). X is measured from the left edge of the keyboard.
    // Standard QWERTY stagger: home row offset +0.25, bottom row offset +0.5 relative to top.
    //   Top row X origin = 0       (Q at x=0)
    //   Home row X origin = 0.25   (A at x=0.25, staggered right by 0.25 from Q)
    //   Bottom row X origin = 0.5  (Z at x=0.5, staggered right by 0.5 from Q)
    private static readonly IReadOnlyDictionary<char, KeyMeta> Metadata =
        new Dictionary<char, KeyMeta>
        {
            // Top row (row 1, Y=1): Q W E R T   Y U I O P
            ['q'] = new(Hand.Left,  Finger.Pinky,  1, 0.0, 1),
            ['w'] = new(Hand.Left,  Finger.Ring,   1, 1.0, 1),
            ['e'] = new(Hand.Left,  Finger.Middle, 1, 2.0, 1),
            ['r'] = new(Hand.Left,  Finger.Index,  1, 3.0, 1),
            ['t'] = new(Hand.Left,  Finger.Index,  1, 4.0, 1),
            ['y'] = new(Hand.Right, Finger.Index,  1, 5.0, 1),
            ['u'] = new(Hand.Right, Finger.Index,  1, 6.0, 1),
            ['i'] = new(Hand.Right, Finger.Middle, 1, 7.0, 1),
            ['o'] = new(Hand.Right, Finger.Ring,   1, 8.0, 1),
            ['p'] = new(Hand.Right, Finger.Pinky,  1, 9.0, 1),

            // Home row (row 0, Y=0): A S D F G   H J K L
            ['a'] = new(Hand.Left,  Finger.Pinky,  0, 0.25, 0),
            ['s'] = new(Hand.Left,  Finger.Ring,   0, 1.25, 0),
            ['d'] = new(Hand.Left,  Finger.Middle, 0, 2.25, 0),
            ['f'] = new(Hand.Left,  Finger.Index,  0, 3.25, 0),
            ['g'] = new(Hand.Left,  Finger.Index,  0, 4.25, 0),
            ['h'] = new(Hand.Right, Finger.Index,  0, 5.25, 0),
            ['j'] = new(Hand.Right, Finger.Index,  0, 6.25, 0),
            ['k'] = new(Hand.Right, Finger.Middle, 0, 7.25, 0),
            ['l'] = new(Hand.Right, Finger.Ring,   0, 8.25, 0),

            // Bottom row (row −1, Y=−1): Z X C V B   N M
            ['z'] = new(Hand.Left,  Finger.Pinky,  -1, 0.5,  -1),
            ['x'] = new(Hand.Left,  Finger.Ring,   -1, 1.5,  -1),
            ['c'] = new(Hand.Left,  Finger.Middle, -1, 2.5,  -1),
            ['v'] = new(Hand.Left,  Finger.Index,  -1, 3.5,  -1),
            ['b'] = new(Hand.Left,  Finger.Index,  -1, 4.5,  -1),
            ['n'] = new(Hand.Right, Finger.Index,  -1, 5.5,  -1),
            ['m'] = new(Hand.Right, Finger.Index,  -1, 6.5,  -1),
        };

    public override string Name => "US QWERTY";

    public override bool CanMap(char c) => Map.ContainsKey(c);

    public override VirtualKey MapChar(char c) =>
        Map.TryGetValue(c, out var entry)
            ? entry.Vk
            : throw new ArgumentOutOfRangeException(
                nameof(c), c, "Character is not on the US QWERTY layout; check CanMap(c) first.");

    public override bool NeedsShift(char c) => Map.TryGetValue(c, out var entry) && entry.Shift;

    public override IEnumerable<char> SupportedChars => Map.Keys;

    /// <summary>
    /// Returns physical metadata for letter keys (case-insensitive). Returns false for digits,
    /// punctuation, and space, which have no biomechanical timing data in this version.
    /// </summary>
    public override bool TryGetMeta(char c, out KeyMeta meta)
    {
        var key = char.ToLowerInvariant(c);
        return Metadata.TryGetValue(key, out meta);
    }

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
