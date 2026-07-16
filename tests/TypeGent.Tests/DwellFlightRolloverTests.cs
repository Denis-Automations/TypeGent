using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using TypeGent.Core.HumanTyping;
using TypeGent.Core.Layouts;
using TypeGent.Core.Typing;
using Xunit;

namespace TypeGent.Tests;

/// <summary>
/// Phase 11 / A4 - Dwell + Flight Decomposition and Rollover (true negative flight).
/// Validates:
///   11.1 - IKI decomposes into near-normal dwell (DU) + UD flight (after KeyUp).
///   A4.1 - Rollover produces genuine overlap: KeyDown(N+1) precedes KeyUp(N) in the stream.
///   A4.2 - Rollover fraction (true overlaps) matches the configured probability for eligible bigrams.
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

    // Compute cumulative timestamps (ms) from the action stream's relative delays.
    private static List<(KeyAction Action, double TimeMs)> Timestamps(
        IEnumerable<TimedAction> stream)
    {
        var result = new List<(KeyAction, double)>();
        var t = 0.0;
        foreach (var a in stream)
        {
            t += a.Delay.TotalMilliseconds;
            result.Add((a.Action, t));
        }
        return result;
    }

    // Returns true if the stream contains overlapping keys: a KeyDown(B) that occurs while
    // another key A (A != B) is still held — i.e. KeyDown(B) precedes KeyUp(A) in stream order.
    private static bool HasOverlap(IEnumerable<TimedAction> stream)
    {
        var held = new HashSet<VirtualKey>();
        foreach (var t in stream)
        {
            switch (t.Action)
            {
                case KeyAction.KeyDown kd:
                    if (held.Any(k => k != kd.Key))
                        return true;
                    held.Add(kd.Key);
                    break;
                case KeyAction.KeyUp ku:
                    held.Remove(ku.Key);
                    break;
            }
        }
        return false;
    }

    // Count the fraction of consecutive KeyDown pairs (by stream order) where the second
    // KeyDown's stream index precedes the first key's matching KeyUp index — a true overlap.
    private static double OverlapFraction(IEnumerable<TimedAction> stream)
    {
        var downs = new List<(VirtualKey Key, int Index)>();
        var ups = new List<(VirtualKey Key, int Index)>();
        var idx = 0;
        foreach (var t in stream)
        {
            switch (t.Action)
            {
                case KeyAction.KeyDown kd:
                    downs.Add((kd.Key, idx));
                    break;
                case KeyAction.KeyUp ku:
                    ups.Add((ku.Key, idx));
                    break;
            }
            idx++;
        }

        if (downs.Count < 2) return 0.0;

        int overlaps = 0;
        int pairs = downs.Count - 1;
        for (var j = 1; j < downs.Count; j++)
        {
            var prevKey = downs[j - 1].Key;
            var prevDownIdx = downs[j - 1].Index;
            var curDownIdx = downs[j].Index;

            // Find the KeyUp of the previous key (first up with matching key after its KeyDown).
            var prevUpIdx = ups.First(u => u.Key == prevKey && u.Index > prevDownIdx).Index;

            if (curDownIdx < prevUpIdx)
                overlaps++;
        }

        return (double)overlaps / pairs;
    }

    // Compute per-key actual dwell (up_time - down_time) from timestamped stream.
    // Matches each KeyDown to its corresponding KeyUp (FIFO per key).
    private static List<double> PerKeyDwell(List<(KeyAction Action, double TimeMs)> ts)
    {
        var pending = new Dictionary<VirtualKey, Queue<double>>();
        var dwells = new List<double>();
        foreach (var (action, time) in ts)
        {
            switch (action)
            {
                case KeyAction.KeyDown kd:
                    if (!pending.ContainsKey(kd.Key))
                        pending[kd.Key] = new Queue<double>();
                    pending[kd.Key].Enqueue(time);
                    break;
                case KeyAction.KeyUp ku:
                    if (pending.TryGetValue(ku.Key, out var q) && q.Count > 0)
                    {
                        var downTime = q.Dequeue();
                        dwells.Add(time - downTime);
                    }
                    break;
            }
        }
        return dwells;
    }

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
    public void Plan_RolloverEnabled_KeyUp_Delays_AreInValidRange()
    {
        // After A4, KeyUp delays are either a dwell (non-rolled keys: 30-200 ms) or an
        // overlap (rolled keys: 8-25 ms). Both must fall within [OverlapMinMs, DwellMaxMs].
        const string text = "the quick brown fox jumps over the lazy dog";
        var actions = Parse(new HumanTypingEngine(new Random(42)).Plan(text, RolloverProfile(), Layout));

        var upDelays = actions.Where(a => a.Action is KeyAction.KeyUp).Select(a => a.DelayMs).ToList();
        upDelays.Should().NotBeEmpty();
        upDelays.Should().AllSatisfy(d =>
        {
            d.Should().BeGreaterThanOrEqualTo(DelayModel.OverlapMinMs,
                "a KeyUp delay is either an overlap (>=8 ms) or a dwell (>=30 ms)");
            d.Should().BeLessThanOrEqualTo(DelayModel.DwellMaxMs);
        });
    }

    [Fact]
    public void Plan_RolloverEnabled_PerKeyDwell_Mean_IsNearConfiguredDwellMean()
    {
        // After A4, a rolled key's actual dwell = sampled_dwell + overlap (slightly inflated).
        // The average across all keys should still be near the configured 90 ms mean.
        const string text = "the quick brown fox jumps over the lazy dog " +
                            "the quick brown fox jumps over the lazy dog " +
                            "the quick brown fox jumps over the lazy dog " +
                            "the quick brown fox jumps over the lazy dog";
        var ts = Timestamps(new HumanTypingEngine(new Random(7)).Plan(text, RolloverProfile(), Layout));

        var dwells = PerKeyDwell(ts);
        dwells.Should().NotBeEmpty();
        dwells.Average().Should().BeApproximately(90.0, precision: 15.0,
            "average per-key dwell should be near the configured 90 ms mean (rolled keys are " +
            "inflated by ~16 ms overlap but non-rolled keys are exact)");
    }

    [Fact]
    public void Plan_RolloverEnabled_RolloverFraction_IsNearConfiguredProbability()
    {
        // Rollover fires only on eligible bigrams (non-same-finger, non-space boundaries).
        // In the pangram, roughly 50-70% of letter bigrams qualify. So the fraction of ALL
        // consecutive KeyDown pairs with true overlap will be approximately:
        //   rolloverProb x eligibilityRate ~ 0.6 x 0.55 ~ 0.33.
        const string text = "the quick brown fox jumps over the lazy dog " +
                            "the quick brown fox jumps over the lazy dog " +
                            "the quick brown fox jumps over the lazy dog " +
                            "the quick brown fox jumps over the lazy dog " +
                            "the quick brown fox jumps over the lazy dog";
        const double rolloverProb = 0.6;
        var actions = new HumanTypingEngine(new Random(99))
            .Plan(text, RolloverProfile(rolloverProb), Layout).ToList();

        var downs = actions.Where(a => a.Action is KeyAction.KeyDown).ToList();
        downs.Should().HaveCountGreaterThan(50, "long text should yield many KeyDown events");

        var fraction = OverlapFraction(actions);

        // Must be non-zero: rollover should actually fire (true overlaps, not zero-gaps).
        fraction.Should().BeGreaterThan(0.0,
            "rollover probability > 0 so at least some bigrams should overlap");

        // Must not exceed rolloverProb: fraction can only approach rolloverProb when ALL
        // bigrams are eligible, which is never the case (spaces, same-finger pairs are ineligible).
        fraction.Should().BeLessThanOrEqualTo(rolloverProb + 0.05,
            "overlap fraction cannot exceed the configured rollover probability");

        // Should be meaningfully above zero.
        fraction.Should().BeGreaterThan(0.10,
            "enough eligible bigrams in the pangram that overlap fraction should be clearly above 10%");
    }

    [Fact]
    public void Plan_RolloverEnabled_ProducesNegativeFlight()
    {
        // The core A4 assertion: when rollover fires, the stream contains KeyDown(N+1)
        // BEFORE KeyUp(N) — genuine overlap (negative flight), not a zero-gap approximation.
        const string text = "the quick brown fox jumps over the lazy dog " +
                            "the quick brown fox jumps over the lazy dog " +
                            "the quick brown fox jumps over the lazy dog";
        var actions = new HumanTypingEngine(new Random(42))
            .Plan(text, RolloverProfile(0.8), Layout).ToList();

        HasOverlap(actions).Should().BeTrue(
            "with rollover enabled and high probability, the stream must contain " +
            "overlapping keys (KeyDown before a prior KeyUp) — true negative flight");
    }

    [Fact]
    public void Plan_RolloverEnabled_ProbabilityZero_NoNegativeFlight()
    {
        // With rollover enabled but probability zero, ShouldRollover never fires, so no
        // KeyDown should precede a prior KeyUp — no overlap, no negative flight.
        const string text = "the quick brown fox jumps over the lazy dog";
        var actions = new HumanTypingEngine(new Random(42))
            .Plan(text, RolloverProfile(0.0), Layout).ToList();

        HasOverlap(actions).Should().BeFalse(
            "with rollover probability 0, no key should overlap another — " +
            "all KeyDown/KeyUp pairs should be sequential");
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
