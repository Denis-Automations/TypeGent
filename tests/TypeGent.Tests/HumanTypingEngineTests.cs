using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentAssertions;
using TypeGent.Core.HumanTyping;
using TypeGent.Core.Layouts;
using TypeGent.Core.Typing;
using Xunit;

namespace TypeGent.Tests;

public class HumanTypingEngineTests
{
    private static readonly UsQwertyLayout Layout = new();

    // Reconstruct the net typed text by "executing" the action stream: backspace pops the last
    // char, every other action appends what it produces. Internal so Phase 4+ tests can reuse it.
    internal static string Reconstruct(IEnumerable<TimedAction> actions, KeyboardLayout layout)
    {
        var reverse = new Dictionary<KeyAction, char>();
        foreach (var ch in layout.SupportedChars)
            reverse[layout.ToAction(ch)] = ch;

        var sb = new StringBuilder();
        foreach (var t in actions)
        {
            switch (t.Action)
            {
                case KeyAction.Press p when p.Key == VirtualKey.Back:
                    if (sb.Length > 0) sb.Length--;
                    break;
                case KeyAction.Text text:
                    sb.Append(text.Value);
                    break;
                default:
                    reverse.TryGetValue(t.Action, out var c).Should().BeTrue(
                        $"every non-backspace action should map back to a character, but {t.Action} did not");
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    [Fact]
    public void HighTypoRate_ProducesBackspaces()
    {
        var engine = new HumanTypingEngine(new Random(42));
        var profile = new TypingProfile { TypoRate = 0.5, Wpm = 60, Jitter = 0.35 };

        var actions = engine.Plan("Hello, World!", profile, Layout).ToList();

        actions.Count(a => a.Action is KeyAction.Press { Key: VirtualKey.Back })
            .Should().BeGreaterThan(0);
    }

    [Fact]
    public void SameSeed_ReproducesIdenticalPlan()
    {
        var profile = new TypingProfile { TypoRate = 0.3, Wpm = 75, Jitter = 0.4 };

        var first = new HumanTypingEngine(new Random(99)).Plan("The quick brown fox.", profile, Layout).ToList();
        var second = new HumanTypingEngine(new Random(99)).Plan("The quick brown fox.", profile, Layout).ToList();

        second.Should().Equal(first);
    }

    [Fact]
    public void SameSeed_WithPace_ReproducesIdenticalPlan()
    {
        // Pace adds a per-call AR(1) Gaussian draw — this proves the draw order stays stable
        // (docs/v2-invariants.md §1) even with the extra RNG consumer active.
        var profile = new TypingProfile { TypoRate = 0.3, Wpm = 75, Jitter = 0.4, Pace = true };

        var first = new HumanTypingEngine(new Random(99)).Plan("The quick brown fox.", profile, Layout).ToList();
        var second = new HumanTypingEngine(new Random(99)).Plan("The quick brown fox.", profile, Layout).ToList();

        second.Should().Equal(first);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void NetTypedText_AlwaysEqualsInput_RegardlessOfTypoRate(double typoRate)
    {
        const string input = "The quick, brown Fox jumps! Over 13 lazy dogs.";
        // A spread of seeds exercises every error kind across runs.
        for (var seed = 0; seed < 25; seed++)
        {
            var profile = new TypingProfile { TypoRate = typoRate, Wpm = 65, Jitter = 0.35 };
            var actions = new HumanTypingEngine(new Random(seed)).Plan(input, profile, Layout);
            Reconstruct(actions, Layout).Should().Be(input, $"seed {seed} at typoRate {typoRate}");
        }
    }

    [Fact]
    public void ZeroTypoRate_TypesOneActionPerChar_NoBackspaces()
    {
        const string input = "Hello, World!";
        var profile = new TypingProfile { TypoRate = 0.0 };

        var actions = new HumanTypingEngine(new Random(1)).Plan(input, profile, Layout).ToList();

        actions.Should().HaveCount(input.Length);
        actions.Any(a => a.Action is KeyAction.Press { Key: VirtualKey.Back }).Should().BeFalse();
    }

    [Fact]
    public void OutOfLayoutChars_StillRoundTripViaFallback()
    {
        const string input = "café — résumé";
        var profile = new TypingProfile { TypoRate = 0.4, Wpm = 60 };

        var actions = new HumanTypingEngine(new Random(7)).Plan(input, profile, Layout);

        Reconstruct(actions, Layout).Should().Be(input);
    }
}
