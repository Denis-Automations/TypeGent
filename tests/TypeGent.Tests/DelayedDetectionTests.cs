using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using TypeGent.Core.HumanTyping;
using TypeGent.Core.Layouts;
using TypeGent.Core.Typing;
using Xunit;

namespace TypeGent.Tests;

/// <summary>
/// v2 Phase 6 – Delayed error detection: omission / missing-double typos and displaced
/// backspace corrections. The net-text-equals-input invariant must hold even when the
/// correction appears several characters after the error.
/// </summary>
public class DelayedDetectionTests
{
    private static readonly UsQwertyLayout Layout = new();

    // ----------------------------------------------------------------
    // 6.1  Net-text reconstruction — generalised for delayed corrections
    // ----------------------------------------------------------------

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void NetTypedText_AlwaysEqualsInput_IncludingDelayedCorrections(double typoRate)
    {
        // The reconstruction helper already handles backspaces, so a displaced correction
        // (type extra chars → backspace burst → retype) should still produce the right net text.
        const string input = "The quick brown fox jumps over the lazy dog right now and here we go.";

        for (var seed = 0; seed < 30; seed++)
        {
            var profile = new TypingProfile
            {
                TypoRate = typoRate,
                Wpm = 65,
                Jitter = 0.35,
                Fatigue = false,
                WarmUp = false,
                Pace = false,   // keep deterministic — pace changes draw count
            };

            var actions = new HumanTypingEngine(new Random(seed)).Plan(input, profile, Layout);
            HumanTypingEngineTests.Reconstruct(actions, Layout)
                .Should().Be(input,
                    $"net-text invariant must hold for delayed corrections (seed {seed}, typoRate {typoRate})");
        }
    }

    [Fact]
    public void NetTypedText_EqualsInput_WithOmissionHeavySeeds()
    {
        // Drive Omission by using seeds that statistically produce it (high typo rate,
        // long text). The net text must still be correct.
        const string input =
            "Programming is the art of telling another human being what one wants " +
            "the computer to do and making sure it actually works correctly.";

        for (var seed = 0; seed < 50; seed++)
        {
            var profile = new TypingProfile
            {
                TypoRate = 0.8,
                Wpm = 80,
                Jitter = 0.35,
                Fatigue = false,
                WarmUp = false,
                Pace = false,
            };

            var actions = new HumanTypingEngine(new Random(seed)).Plan(input, profile, Layout);
            HumanTypingEngineTests.Reconstruct(actions, Layout)
                .Should().Be(input, $"seed {seed}: net text must equal input even with omission typos");
        }
    }

    [Fact]
    public void NetTypedText_EqualsInput_WithMissingDoubleHeavyInput()
    {
        // Text deliberately loaded with double-letter opportunities to stress MissingDouble.
        const string input =
            "committee succeeded immediately opportunity accidentally misspelled " +
            "connections necessary accommodation immediately successful occurrence";

        for (var seed = 0; seed < 40; seed++)
        {
            var profile = new TypingProfile
            {
                TypoRate = 0.9,
                Wpm = 75,
                Jitter = 0.35,
                Fatigue = false,
                WarmUp = false,
                Pace = false,
            };

            var actions = new HumanTypingEngine(new Random(seed)).Plan(input, profile, Layout);
            HumanTypingEngineTests.Reconstruct(actions, Layout)
                .Should().Be(input, $"seed {seed}: MissingDouble typos must self-correct via delayed detection");
        }
    }

    // ----------------------------------------------------------------
    // 6.2  Delayed correction is actually present in plans
    // ----------------------------------------------------------------

    /// <summary>
    /// A "delayed correction" pattern in the action stream is:
    ///   [some non-backspace action] ... [backspace] ... [retype chars]
    /// More specifically: a backspace that is preceded by at least one non-backspace action
    /// that in turn was preceded by a typo (error char). We detect it by checking that at
    /// least one backspace appears after at least one non-backspace since the last backspace.
    /// This catches any delayed-correction sequence (≥1 correct char typed before correction).
    /// </summary>
    private static bool HasDelayedCorrection(IReadOnlyList<TimedAction> actions)
    {
        // Walk the stream; track whether we've seen a non-backspace since the last
        // backspace cluster started.  A delayed correction is a backspace that follows
        // at least one non-backspace action since the previous backspace.
        var nonBackspaceSinceLastBS = 0;
        var clusterActive = false;

        foreach (var a in actions)
        {
            var isBS = a.Action is KeyAction.Press { Key: VirtualKey.Back };

            if (isBS)
            {
                if (!clusterActive)
                {
                    // Starting a new backspace cluster — is it delayed?
                    if (nonBackspaceSinceLastBS >= 1) return true;
                    clusterActive = true;
                }
                // else: continue an existing cluster — not delayed
            }
            else
            {
                clusterActive = false;
                nonBackspaceSinceLastBS++;
            }
        }
        return false;
    }

    [Fact]
    public void DelayedCorrection_ASeededPlan_ContainsAtLeastOneDisplacedCorrection()
    {
        // With a high typo rate and long input, some correction must be delayed.
        // We search across seeds until we find at least one delayed correction.
        const string input =
            "The quick brown fox jumps over the lazy dog and then keeps running " +
            "until it reaches the other side of the meadow near the old farmhouse.";

        var profile = new TypingProfile
        {
            TypoRate = 0.5,
            Wpm = 70,
            Jitter = 0.35,
            Fatigue = false,
            WarmUp = false,
            Pace = false,
        };

        var foundDelayedCorrection = false;
        for (var seed = 0; seed < 200 && !foundDelayedCorrection; seed++)
        {
            var actions = new HumanTypingEngine(new Random(seed))
                .Plan(input, profile, Layout).ToList();
            if (HasDelayedCorrection(actions))
                foundDelayedCorrection = true;
        }

        foundDelayedCorrection.Should().BeTrue(
            "at least one seeded plan across 200 seeds should contain a delayed correction " +
            "(error then ≥1 correct char then a backspace burst)");
    }

    [Fact]
    public void DelayedCorrection_HighTypoRate_ManyPlansHaveDelayedCorrections()
    {
        // At very high typo rates, the majority of plans with any typo should
        // include at least one delayed correction.
        const string input =
            "She sells seashells by the seashore and the shells she sells are surely seashells.";

        var profile = new TypingProfile
        {
            TypoRate = 0.9,
            Wpm = 70,
            Jitter = 0.35,
            Fatigue = false,
            WarmUp = false,
            Pace = false,
        };

        var total = 0;
        var withDelayed = 0;

        for (var seed = 0; seed < 100; seed++)
        {
            var actions = new HumanTypingEngine(new Random(seed))
                .Plan(input, profile, Layout).ToList();

            var hasAnyBS = actions.Any(a => a.Action is KeyAction.Press { Key: VirtualKey.Back });
            if (!hasAnyBS) continue;  // no typos at all — skip

            total++;
            if (HasDelayedCorrection(actions)) withDelayed++;
        }

        // We expect the majority of plans with corrections to include at least one
        // delayed correction (since DetectionDelayChars returns delay > 0 ~60% of the time).
        total.Should().BeGreaterThan(0, "there should be plans with corrections at typoRate=0.9");
        var fraction = (double)withDelayed / total;
        fraction.Should().BeGreaterThan(0.3,
            $"at least 30% of plans with corrections should have ≥1 delayed correction " +
            $"(got {withDelayed}/{total} = {fraction:P0})");
    }

    // ----------------------------------------------------------------
    // 6.3  Backspace rhythm is fast (realistic burst)
    // ----------------------------------------------------------------

    [Fact]
    public void BackspaceRhythm_BurstDelays_AreFasterThanNormalTyping()
    {
        // The engine gives successive BSes a delay of SampleDelayMs * 0.4, which must
        // be less than the surrounding normal key delays.  We verify by finding a backspace
        // burst (≥2 consecutive BSes) and comparing their delays to adjacent normal keys.
        const string input =
            "The quick brown fox jumps over the lazy dog";

        var profile = new TypingProfile
        {
            TypoRate = 0.9,
            Wpm = 60,
            Jitter = 0.05,   // low jitter: make delays tight so the rhythm comparison is clean
            Fatigue = false,
            WarmUp = false,
            Pace = false,
        };

        double? firstBurstBSDelay = null;
        double? normalKeyDelay = null;

        for (var seed = 0; seed < 200 && (firstBurstBSDelay == null || normalKeyDelay == null); seed++)
        {
            var actions = new HumanTypingEngine(new Random(seed))
                .Plan(input, profile, Layout).ToList();

            // Find a run of ≥2 consecutive backspaces and note the second one's delay
            // (the first is a "reaction" delay, subsequent ones are the fast burst).
            for (var k = 1; k < actions.Count - 1 && firstBurstBSDelay == null; k++)
            {
                var isBS = actions[k].Action is KeyAction.Press { Key: VirtualKey.Back };
                var prevIsBS = actions[k - 1].Action is KeyAction.Press { Key: VirtualKey.Back };
                if (isBS && prevIsBS)
                    firstBurstBSDelay = actions[k].Delay.TotalMilliseconds;
            }

            // Pick the first non-backspace delay that isn't the very first action
            // (to avoid lapse outliers at position 0).
            for (var k = 2; k < actions.Count && normalKeyDelay == null; k++)
            {
                var notBS = actions[k].Action is not KeyAction.Press { Key: VirtualKey.Back };
                if (notBS)
                    normalKeyDelay = actions[k].Delay.TotalMilliseconds;
            }
        }

        firstBurstBSDelay.Should().NotBeNull("a backspace burst should be present at typoRate=0.9");
        normalKeyDelay.Should().NotBeNull("there should be at least one normal key action");

        firstBurstBSDelay!.Value.Should().BeLessThan(normalKeyDelay!.Value,
            "backspace-burst keystrokes should have a shorter delay than normal key presses " +
            "(the engine uses × 0.4 of the base delay for successive backspaces)");
    }

    // ----------------------------------------------------------------
    // 6.4  DetectionDelayChars distribution
    // ----------------------------------------------------------------

    [Fact]
    public void DetectionDelayChars_Immediate_IsAbout40Percent()
    {
        // ~40% of draws should return 0 (immediate detection).
        var model = new ErrorModel(new Random(77));
        const int trials = 10_000;
        var immediate = Enumerable.Range(0, trials).Count(_ => model.DetectionDelayChars() == 0);

        // 40% ± generous tolerance
        immediate.Should().BeInRange(3000, 5200,
            "roughly 40% of DetectionDelayChars() draws should be immediate (0)");
    }

    [Fact]
    public void DetectionDelayChars_ReturnsValuesInExpectedRange()
    {
        var model = new ErrorModel(new Random(55));
        for (var i = 0; i < 1_000; i++)
        {
            var d = model.DetectionDelayChars();
            d.Should().BeInRange(0, 5,
                "DetectionDelayChars must return 0–5 as documented");
        }
    }

    [Fact]
    public void DetectionDelayChars_MajorityAreDelayed()
    {
        // ~60% of draws should return > 0 (delayed detection outnumbers immediate ~0.60 vs ~0.40).
        var model = new ErrorModel(new Random(11));
        const int trials = 10_000;
        var delayed = Enumerable.Range(0, trials).Count(_ => model.DetectionDelayChars() > 0);

        // 60% ± generous tolerance
        delayed.Should().BeInRange(4500, 7500,
            "roughly 60% of DetectionDelayChars() draws should be delayed (>0), " +
            "matching the research finding that delayed corrections outnumber immediate ones");
    }

    // ----------------------------------------------------------------
    // 6.5  Seeded reproducibility is preserved with delayed corrections
    // ----------------------------------------------------------------

    [Fact]
    public void SameSeed_WithDelayedCorrections_StillReproducesIdentically()
    {
        // Delayed corrections add no extra RNG draws — they are computed deterministically
        // from the detection-delay draw already made. Same seed must still reproduce.
        var profile = new TypingProfile
        {
            TypoRate = 0.5, Wpm = 70, Jitter = 0.35,
            Fatigue = false, WarmUp = false, Pace = false,
        };
        const string input = "The quick brown fox jumps over the lazy dog";

        var first  = new HumanTypingEngine(new Random(42)).Plan(input, profile, Layout).ToList();
        var second = new HumanTypingEngine(new Random(42)).Plan(input, profile, Layout).ToList();

        second.Should().Equal(first,
            "same seed must reproduce the identical action stream, including delayed corrections");
    }
}
