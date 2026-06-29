using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WindowsInput;
using WindowsInput.Native;
using TypeGent.Core.Abstractions;
using TypeGent.Core.Typing;

namespace TypeGent.Native;

/// <summary>
/// The production <see cref="IKeyboardBackend"/>: translates <see cref="KeyAction"/>s into
/// real keystrokes via InputSimulatorPlus (SendInput under the hood).
/// <list type="bullet">
///   <item><see cref="KeyAction.Press"/> → <c>Keyboard.KeyPress</c> (single key).</item>
///   <item><see cref="KeyAction.Chord"/> → <c>Keyboard.ModifiedKeyStroke</c> (e.g. Shift+,).</item>
///   <item><see cref="KeyAction.Text"/> → <c>Keyboard.TextEntry</c> (Unicode / VK_PACKET fallback).</item>
/// </list>
/// Our <see cref="VirtualKey"/> values equal the Win32 VK_* codes, which are exactly the
/// numeric values of <see cref="VirtualKeyCode"/>, so the mapping is a plain cast.
/// </summary>
public sealed class InputSimulatorPlusBackend : IKeyboardBackend
{
    private readonly IInputSimulator _sim;
    private readonly ILogger _logger;

    public InputSimulatorPlusBackend(ILogger<InputSimulatorPlusBackend>? logger = null)
        : this(new InputSimulator(), logger)
    {
    }

    // Exposed for testing with a substitute simulator.
    internal InputSimulatorPlusBackend(IInputSimulator sim, ILogger<InputSimulatorPlusBackend>? logger = null)
    {
        _sim = sim ?? throw new ArgumentNullException(nameof(sim));
        _logger = logger ?? NullLogger<InputSimulatorPlusBackend>.Instance;
    }

    public Task ExecuteAsync(KeyAction action, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);
        ct.ThrowIfCancellationRequested();

        switch (action)
        {
            case KeyAction.Press press:
                _sim.Keyboard.KeyPress(ToVk(press.Key));
                break;

            case KeyAction.Chord chord:
                _sim.Keyboard.ModifiedKeyStroke(ToVk(chord.Modifier), ToVk(chord.Base));
                break;

            case KeyAction.Text text:
                if (!string.IsNullOrEmpty(text.Value))
                {
                    _sim.Keyboard.TextEntry(text.Value);
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(action), action, "Unknown KeyAction variant.");
        }

        return Task.CompletedTask;
    }

    private static VirtualKeyCode ToVk(VirtualKey key) => (VirtualKeyCode)(ushort)key;
}
