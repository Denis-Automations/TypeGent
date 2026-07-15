using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using TypeGent.Core.Abstractions;
using TypeGent.Core.Typing;
using Xunit;

namespace TypeGent.Tests;

/// <summary>
/// Phase 9 – Down/Up Event Model.
/// Verifies that the orchestrator correctly expands a <see cref="TimedAction"/> that carries
/// a <see cref="TimedAction.HoldMs"/> value into separate <see cref="KeyAction.KeyDown"/> /
/// <see cref="KeyAction.KeyUp"/> events, while leaving the legacy atomic-press path untouched.
/// </summary>
public class DownUpEventModelTests
{
    // ── helper ────────────────────────────────────────────────────────────────

    private static (TypingOrchestrator orchestrator, List<KeyAction> received) MakeOrchestrator()
    {
        var backend = Substitute.For<IKeyboardBackend>();
        var received = new List<KeyAction>();
        backend
            .ExecuteAsync(Arg.Do<KeyAction>(received.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        return (new TypingOrchestrator(backend), received);
    }

    // ── 9.1 : Press + HoldMs → KeyDown then KeyUp ─────────────────────────────

    [Fact]
    public async Task RunAsync_PressWithHoldMs_EmitsKeyDownThenKeyUp()
    {
        // Arrange
        var (orchestrator, received) = MakeOrchestrator();

        // A single 'A' Press with a zero hold (keeps the test instant but exercises the path).
        var actions = new[]
        {
            new TimedAction(TimeSpan.Zero, new KeyAction.Press(VirtualKey.A), HoldMs: 0.0),
        };

        // Act
        await orchestrator.RunAsync(actions, CancellationToken.None);

        // Assert: two events — KeyDown then KeyUp — not a single Press.
        received.Should().HaveCount(2);
        received[0].Should().BeOfType<KeyAction.KeyDown>()
            .Which.Key.Should().Be(VirtualKey.A);
        received[1].Should().BeOfType<KeyAction.KeyUp>()
            .Which.Key.Should().Be(VirtualKey.A);
    }

    // ── 9.2 : Press without HoldMs → legacy atomic Press ─────────────────────

    [Fact]
    public async Task RunAsync_PressWithoutHoldMs_UsesAtomicPress()
    {
        // Arrange
        var (orchestrator, received) = MakeOrchestrator();

        var actions = new[]
        {
            // HoldMs omitted (null) — should go through the legacy path.
            new TimedAction(TimeSpan.Zero, new KeyAction.Press(VirtualKey.A)),
        };

        // Act
        await orchestrator.RunAsync(actions, CancellationToken.None);

        // Assert: exactly one event, still a Press (not split).
        received.Should().HaveCount(1);
        received[0].Should().BeOfType<KeyAction.Press>()
            .Which.Key.Should().Be(VirtualKey.A);
    }

    // ── 9.3 : Chord + HoldMs → mod-down, base-down, hold, base-up, mod-up ────

    [Fact]
    public async Task RunAsync_ChordWithHoldMs_EmitsModDownBaseDownHoldBaseUpModUp()
    {
        // Arrange
        var (orchestrator, received) = MakeOrchestrator();

        var actions = new[]
        {
            new TimedAction(
                TimeSpan.Zero,
                new KeyAction.Chord(VirtualKey.Shift, VirtualKey.A),
                HoldMs: 0.0),
        };

        // Act
        await orchestrator.RunAsync(actions, CancellationToken.None);

        // Assert: 4 events in order — Shift↓ A↓ A↑ Shift↑
        received.Should().HaveCount(4);
        received[0].Should().BeOfType<KeyAction.KeyDown>()
            .Which.Key.Should().Be(VirtualKey.Shift);
        received[1].Should().BeOfType<KeyAction.KeyDown>()
            .Which.Key.Should().Be(VirtualKey.A);
        received[2].Should().BeOfType<KeyAction.KeyUp>()
            .Which.Key.Should().Be(VirtualKey.A);
        received[3].Should().BeOfType<KeyAction.KeyUp>()
            .Which.Key.Should().Be(VirtualKey.Shift);
    }

    // ── 9.4 : Text + HoldMs → still uses atomic TextEntry (no down/up) ───────

    [Fact]
    public async Task RunAsync_TextWithHoldMs_UsesAtomicTextEntry()
    {
        // Arrange
        var (orchestrator, received) = MakeOrchestrator();

        var actions = new[]
        {
            new TimedAction(TimeSpan.Zero, new KeyAction.Text("é"), HoldMs: 50.0),
        };

        // Act
        await orchestrator.RunAsync(actions, CancellationToken.None);

        // Assert: Text has no separate down/up concept — exactly one Text action dispatched.
        received.Should().HaveCount(1);
        received[0].Should().BeOfType<KeyAction.Text>()
            .Which.Value.Should().Be("é");
    }

    // ── 9.5 : Cancellation during hold still throws promptly ─────────────────

    [Fact]
    public async Task RunAsync_CancelledDuringHold_ThrowsTaskCanceledException()
    {
        // Arrange
        var backend = Substitute.For<IKeyboardBackend>();
        backend.ExecuteAsync(Arg.Any<KeyAction>(), Arg.Any<CancellationToken>())
               .Returns(Task.CompletedTask);
        var orchestrator = new TypingOrchestrator(backend);
        using var cts = new CancellationTokenSource();

        // One action with a long hold — we cancel mid-hold.
        var actions = new[]
        {
            new TimedAction(TimeSpan.Zero, new KeyAction.Press(VirtualKey.A), HoldMs: 30_000.0),
        };

        cts.CancelAfter(TimeSpan.FromMilliseconds(80));

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(
            () => orchestrator.RunAsync(actions, cts.Token));
    }
}
