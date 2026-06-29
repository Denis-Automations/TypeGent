using TypeGent.Core.Typing;

namespace TypeGent.Core.Abstractions;

/// <summary>
/// The seam between the typing logic and the OS. Implementations turn a
/// <see cref="KeyAction"/> into real (or fake) keystrokes. The production
/// implementation lives in TypeGent.Native; tests substitute a fake.
/// </summary>
public interface IKeyboardBackend
{
    /// <summary>Perform <paramref name="action"/>, honoring cancellation.</summary>
    Task ExecuteAsync(KeyAction action, CancellationToken ct);
}
