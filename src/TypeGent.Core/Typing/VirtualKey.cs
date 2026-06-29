namespace TypeGent.Core.Typing;

/// <summary>
/// Windows virtual-key codes (the subset TypeGent injects). Values match the
/// Win32 VK_* constants so they can be passed straight to SendInput-based backends.
/// Letter and digit codes intentionally equal their ASCII upper-case code points.
/// </summary>
public enum VirtualKey : ushort
{
    // Letters (VK_A..VK_Z == 'A'..'Z')
    A = 0x41, B = 0x42, C = 0x43, D = 0x44, E = 0x45, F = 0x46, G = 0x47,
    H = 0x48, I = 0x49, J = 0x4A, K = 0x4B, L = 0x4C, M = 0x4D, N = 0x4E,
    O = 0x4F, P = 0x50, Q = 0x51, R = 0x52, S = 0x53, T = 0x54, U = 0x55,
    V = 0x56, W = 0x57, X = 0x58, Y = 0x59, Z = 0x5A,

    // Top-row digits (VK_0..VK_9 == '0'..'9')
    D0 = 0x30, D1 = 0x31, D2 = 0x32, D3 = 0x33, D4 = 0x34,
    D5 = 0x35, D6 = 0x36, D7 = 0x37, D8 = 0x38, D9 = 0x39,

    // Whitespace / control
    Space = 0x20,
    Tab = 0x09,
    Enter = 0x0D,
    Back = 0x08,

    // Modifiers
    Shift = 0x10,
    Control = 0x11,
    Menu = 0x12, // Alt

    // OEM punctuation keys (US QWERTY positions)
    OEM_1 = 0xBA,      // ; :
    OEM_PLUS = 0xBB,   // = +
    OEM_COMMA = 0xBC,  // , <
    OEM_MINUS = 0xBD,  // - _
    OEM_PERIOD = 0xBE, // . >
    OEM_2 = 0xBF,      // / ?
    OEM_3 = 0xC0,      // ` ~
    OEM_4 = 0xDB,      // [ {
    OEM_5 = 0xDC,      // \ |
    OEM_6 = 0xDD,      // ] }
    OEM_7 = 0xDE,      // ' "
}
