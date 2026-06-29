using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using TypeGent.Core.Abstractions;
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
}
