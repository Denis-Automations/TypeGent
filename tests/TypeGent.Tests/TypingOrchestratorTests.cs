using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using TypeGent.Core.Abstractions;
using TypeGent.Core.Layouts;
using TypeGent.Core.Typing;
using Xunit;

namespace TypeGent.Tests;

public class TypingOrchestratorTests
{
    [Fact]
    public async Task RunAsync_TwoPresses_CallsBackendTwiceWithCorrectVirtualKeys()
    {
        // Arrange: a fake backend that records the actions it receives.
        var backend = Substitute.For<IKeyboardBackend>();
        var received = new List<KeyAction>();
        backend
            .ExecuteAsync(Arg.Do<KeyAction>(received.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var orchestrator = new TypingOrchestrator(backend);

        // Zero delays keep the test fast; Phase 2 has no timing model.
        var actions = new[]
        {
            new TimedAction(TimeSpan.Zero, new KeyAction.Press(VirtualKey.H)),
            new TimedAction(TimeSpan.Zero, new KeyAction.Press(VirtualKey.I)),
        };

        // Act
        await orchestrator.RunAsync(actions, CancellationToken.None);

        // Assert: exactly two dispatches, in order, with the right VK codes.
        await backend.Received(2).ExecuteAsync(Arg.Any<KeyAction>(), Arg.Any<CancellationToken>());
        received.Should().Equal(
            new KeyAction.Press(VirtualKey.H),
            new KeyAction.Press(VirtualKey.I));
    }

    [Fact]
    public async Task RunAsync_Cancellation_StopsPromptly()
    {
        var backend = Substitute.For<IKeyboardBackend>();
        backend.ExecuteAsync(Arg.Any<KeyAction>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var orchestrator = new TypingOrchestrator(backend);
        using var cts = new CancellationTokenSource();

        var actions = Enumerable.Range(0, 100)
            .Select(_ => new TimedAction(TimeSpan.FromMilliseconds(50), new KeyAction.Press(VirtualKey.A)));

        cts.CancelAfter(TimeSpan.FromMilliseconds(80));
        await Assert.ThrowsAsync<TaskCanceledException>(() => orchestrator.RunAsync(actions, cts.Token));

        // The backend should have been called at most a couple of times before cancellation fired.
        await backend.ReceivedWithAnyArgs(1).ExecuteAsync(default!, default);
    }
    [Fact]
    public async Task RunTextAsync_RoutesVkChordAndUnicodeFallback_InOrder()
    {
        var backend = Substitute.For<IKeyboardBackend>();
        var received = new List<KeyAction>();
        backend
            .ExecuteAsync(Arg.Do<KeyAction>(received.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var orchestrator = new TypingOrchestrator(backend);

        // "H," exercises the Shift chord and the unshifted comma; 'é' hits the Unicode fallback.
        await orchestrator.RunTextAsync("H,é", new UsQwertyLayout(), TimeSpan.Zero, CancellationToken.None);

        received.Should().Equal(
            new KeyAction.Chord(VirtualKey.Shift, VirtualKey.H),
            new KeyAction.Press(VirtualKey.OEM_COMMA),
            new KeyAction.Text("é"));
    }
}
