using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using TypeGent.Core.HumanTyping;
using TypeGent.Core.Layouts;
using TypeGent.Core.Typing;
using Xunit;

namespace TypeGent.Tests;

/// <summary>
/// Phase 11 - Dwell + Flight Decomposition and Rollover.
/// Validates:
///   11.1 - IKI decomposes into near-normal dwell (DU) + UD flight (after KeyUp).
///   11.2 - Rollover fraction matches the configured probability for eligible bigrams.
///   11.3 - Rollover=off -> no pre-expanded KeyDown/KeyUp events; Phase 10 HoldMs path used.
///   11.4 - Same seed -> identical stream with rollover on (determinism).
/// </summary>
public class DwellFlightRolloverTests
{
    private static readonly KeyboardLayout Layout = new UsQwertyLayout();

    private static TypingProfile RolloverProfile(double rolloverProb = 0.6) => new()
    {
        Wpm = 120,
        TypoRate = 0,
        DwellEnabled = true,
        DwellMeanMs = 90.0,
        DwellSigmaMs = 12.0,
        RolloverEnabled = true,
        RolloverProbability = rolloverProb,
        Fatigue = false,
        WarmUp = false,
        Pace = false,
        LapseRate = 0,
    };

    private static List<(KeyAction Action, double DelayMs)> Parse(IEnumerable<TimedAction> stream)
        => stream.Select(a => (a.Action, a.Delay.TotalMilliseconds)).ToList();

    [Fact]
    public void Plan_RolloverEnabled_KeyDown_Delays_AreNonNegative()
    {
        const string text = "the quick brown fox jumps over the lazy dog";
        var actions = Parse(new HumanTypingEngine(new Random(42)).Plan(text, RolloverProfile(), Layout));

        var downDelays = actions.Where(a => a.Action is KeyAction.KeyDown).Select(a => a.DelayMs).ToList();
        downDelays.Should().NotBeEmpty();
        downDelays.Should().AllSatisfy(d => d.Should().BeGreaterThanOrEqualTo(0.0));
    }

    [Fact]
    public void Plan_RolloverEnabled_KeyUp_Delays_AreInDwellRange()
    {
        const string text = "the quick brown fox jumps over the lazy dog";
        var actions = Parse(new HumanTypingEngine(new Random(42)).Plan(text, RolloverProfile(), Layout));

        var upDelays = actions.Where(a => a.Action is KeyAction.KeyUp).Select(a => a.DelayMs).ToList();
        upDelays.Should().NotBeEmpty();
        upDelays.Should().AllSatisfy(d =>
        {
            d.Should().BeGreaterThanOrEqualTo(DelayModel.DwellMinMs);
            d.Should().BeLessThanOrEqualTo(DelayModel.DwellMaxMs);
        });
    }

    [Fact]
    public void Plan_RolloverEnabled_KeyUp_Mean_IsNearConfiguredDwellMean()
    {
        const string text = "the quick brown fox jumps over the lazy dog the quick brown fox jumps over the lazy dog the quick brown fox jumps over the lazy dog the quick brown fox jumps over the lazy dog";
        var actions = Parse(new HumanTypingEngine(new Random(7)).Plan(text, RolloverProfile(), Layout));

        var upDelays = actions.Where(a => a.Action is KeyAction.KeyUp).Select(a => a.DelayMs).ToList();
        upDelays.Average().Should().BeApproximately(90.0, precision: 5.0);
    }

    [Fact]
    public void Plan_RolloverEnabled_RolloverFraction_IsNearConfiguredProbability()
    {
        // Rollover fires only on eligible bigrams (non-same-finger, non-space boundaries).
        // In the pangram, not every consecutive KeyDown pair is eligible — roughly 50–70%
        // of letter bigrams qualify. So the fraction of ALL KeyDown pairs with zero delay
        // will be approximately: rolloverProb × eligibilityRate ≈ 0.6 × 0.55 ≈ 0.33.
        // We assert:
        //   (a) at least some rollover occurred (> 0)
        //   (b) rolled fraction is between 10 % and rolloverProb (upper bound: if all were
        //       eligible the fraction could not exceed rolloverProb)
        const string text = "the quick brown fox jumps over the lazy dog " +
                            "the quick brown fox jumps over the lazy dog " +
                            "the quick brown fox jumps over the lazy dog " +
                            "the quick brown fox jumps over the lazy dog " +
                            "the quick brown fox jumps over the lazy dog";
        const double rolloverProb = 0.6;
        var actions = Parse(new HumanTypingEngine(new Random(99)).Plan(text, RolloverProfile(rolloverProb), Layout));

        var downActions = actions.Where(a => a.Action is KeyAction.KeyDown).ToList();
        downActions.Should().HaveCountGreaterThan(50, "long text should yield many KeyDown events");

        int rolled = 0;
        // Start from index 1: the first KeyDown has no preceding pair.
        for (var j = 1; j < downActions.Count; j++)
        {
            if (downActions[j].DelayMs <= DelayModel.RolloverFlightMs + 0.001)
                rolled++;
        }

        var fraction = (double)rolled / (downActions.Count - 1);

        // Must be non-zero: rollover should actually fire.
        fraction.Should().BeGreaterThan(0.0,
            "rollover probability > 0 so at least some bigrams should roll");

        // Must not exceed rolloverProb: fraction can only approach rolloverProb when ALL
        // bigrams are eligible, which is never the case (spaces, same-finger pairs are ineligible).
        fraction.Should().BeLessThanOrEqualTo(rolloverProb + 0.05,
            "rolled fraction cannot exceed the configured rollover probability");

        // Should be meaningfully above zero — empirically ~0.2–0.4 for pangram × 5 with p=0.6.
        fraction.Should().BeGreaterThan(0.10,
            "enough eligible bigrams in the pangram that rollover fraction should be clearly above 10%");
    }

    [Fact]
    public void Plan_RolloverDisabled_HappyPath_UsesHoldMsNotKeyDown()
    {
        const string text = "the quick brown fox jumps over the lazy dog";
        var profile = new TypingProfile
        {
            Wpm = 120, TypoRate = 0, DwellEnabled = true,
            DwellMeanMs = 90, DwellSigmaMs = 12, RolloverEnabled = false,
            Fatigue = false, WarmUp = false, Pace = false,
        };
        var actions = new HumanTypingEngine(new Random(1)).Plan(text, profile, Layout).ToList();

        actions.Where(a => a.Action is KeyAction.KeyDown).Should().BeEmpty(
            "rollover off -> Phase 10 HoldMs path, no raw KeyDown");
        actions.Where(a => a.Action is KeyAction.Press or KeyAction.Chord)
               .Should().AllSatisfy(a => a.HoldMs.Should().NotBeNull());
    }

    [Fact]
    public void Plan_BothDisabled_LegacyAtomicPath()
    {
        const string text = "the quick brown fox";
        var profile = new TypingProfile
        {
            Wpm = 60, TypoRate = 0, DwellEnabled = false, RolloverEnabled = false,
            Fatigue = false, WarmUp = false, Pace = false,
        };
        var actions = new HumanTypingEngine(new Random(5)).Plan(text, profile, Layout).ToList();

        actions.Should().AllSatisfy(a =>
        {
            a.HoldMs.Should().BeNull();
            a.Action.Should().NotBeOfType<KeyAction.KeyDown>();
            a.Action.Should().NotBeOfType<KeyAction.KeyUp>();
        });
    }

    [Fact]
    public void Plan_RolloverEnabled_SameSeed_ProducesIdenticalStream()
    {
        const string text = "the quick brown fox jumps over the lazy dog";
        var profile = RolloverProfile(0.55);

        var plan1 = new HumanTypingEngine(new Random(1234)).Plan(text, profile, Layout).ToList();
        var plan2 = new HumanTypingEngine(new Random(1234)).Plan(text, profile, Layout).ToList();

        plan1.Should().HaveCount(plan2.Count);
        for (var j = 0; j < plan1.Count; j++)
        {
            plan1[j].Delay.Should().Be(plan2[j].Delay,   $"delay at {j}");
            plan1[j].HoldMs.Should().Be(plan2[j].HoldMs, $"HoldMs at {j}");
            plan1[j].Action.Should().Be(plan2[j].Action,  $"action at {j}");
        }
    }

    [Fact]
    public void Plan_RolloverEnabled_KeyDownKeyUp_AreBalanced()
    {
        const string text = "sphinx of black quartz judge my vow";
        var actions = new HumanTypingEngine(new Random(77)).Plan(text, RolloverProfile(), Layout).ToList();

        var downs = actions.Where(a => a.Action is KeyAction.KeyDown).Select(a => ((KeyAction.KeyDown)a.Action).Key).ToList();
        var ups   = actions.Where(a => a.Action is KeyAction.KeyUp  ).Select(a => ((KeyAction.KeyUp  )a.Action).Key).ToList();

        downs.Should().HaveCount(ups.Count, "each KeyDown must have a matching KeyUp");

        foreach (var grp in downs.GroupBy(v => v))
        {
            var upCount = ups.Count(v => v == grp.Key);
            upCount.Should().Be(grp.Count(), $"VK {grp.Key}: {grp.Count()} down(s) but {upCount} up(s)");
        }
    }
}
