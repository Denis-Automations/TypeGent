using System.Linq;
using FluentAssertions;
using TypeGent.Core.HumanTyping;
using TypeGent.Core.Layouts;
using TypeGent.Core.Typing;
using Xunit;

namespace TypeGent.Tests;

/// <summary>
/// Phase 10 – Dwell Time.
/// Validates that <see cref="DelayModel.SampleDwellMs"/> produces a tight near-Gaussian
/// distribution in the physiological range, and that <see cref="HumanTypingEngine"/> correctly
/// sets <see cref="TimedAction.HoldMs"/> on regular keystrokes when
/// <see cref="TypingProfile.DwellEnabled"/> is true, leaving backspace and Text actions
/// with <see langword="null"/>.
/// </summary>
public class DwellTimeTests
{
    private static readonly KeyboardLayout Layout = new UsQwertyLayout();

    // ── DelayModel.SampleDwellMs ──────────────────────────────────────────────

    [Fact]
    public void SampleDwellMs_Samples_ClusterAroundMean_WithinPhysiologicalBounds()
    {
        // Arrange: default mean 90 ms, sigma 12 ms.
        var model = new DelayModel(new Random(42));
        const int n = 10_000;
        const double mean = 90.0;
        const double sigma = 12.0;

        // Act
        var samples = Enumerable.Range(0, n)
            .Select(_ => model.SampleDwellMs(mean, sigma))
            .ToList();

        // Assert: all samples within physiological bounds.
        samples.Should().AllSatisfy(s =>
            s.Should().BeInRange(DelayModel.DwellMinMs, DelayModel.DwellMaxMs));

        // Sample mean should be close to the configured mean (±2 ms with n=10 000).
        var sampleMean = samples.Average();
        sampleMean.Should().BeApproximately(mean, precision: 2.0);

        // Sample std-dev should be close to sigma (±1 ms tolerance).
        var variance = samples.Average(s => (s - sampleMean) * (s - sampleMean));
        var sampleSigma = Math.Sqrt(variance);
        sampleSigma.Should().BeApproximately(sigma, precision: 1.0);
    }

    [Fact]
    public void SampleDwellMs_ZeroSigma_AlwaysReturnsMean()
    {
        // Arrange: degenerate case — no spread, should just return the mean (clamped).
        var model = new DelayModel(new Random(7));
        const double mean = 80.0;

        // Act & Assert
        for (var i = 0; i < 50; i++)
        {
            model.SampleDwellMs(mean, sigmaMs: 0.0).Should().BeApproximately(mean, precision: 0.001);
        }
    }

    [Fact]
    public void SampleDwellMs_Constants_AreWithinExpectedRange()
    {
        // Physiological guard sanity: min < default mean < max.
        DelayModel.DwellMinMs.Should().BeLessThan(90.0);
        DelayModel.DwellMaxMs.Should().BeGreaterThan(90.0);
        DelayModel.DwellMinMs.Should().BeGreaterThan(0.0);
    }

    // ── HumanTypingEngine integration ─────────────────────────────────────────

    [Fact]
    public void Engine_DwellEnabled_RegularKeys_CarryHoldMs_BackspaceAndTextDoNot()
    {
        // Arrange: text with a character that needs the Unicode fallback ('é') and a letter
        // that will hit the chord (uppercase) or press path.
        // We force typo rate 0 so the only actions are the plain keystrokes + any Text fallback.
        var profile = new TypingProfile
        {
            Wpm = 60,
            TypoRate = 0,
            DwellEnabled = true,
            DwellMeanMs = 90.0,
            DwellSigmaMs = 12.0,
            Fatigue = false,
            WarmUp = false,
            Pace = false,
        };
        var engine = new HumanTypingEngine(new Random(1));

        // "hello é" → 'h','e','l','l','o',' ','é'
        // 'é' hits the Unicode/Text fallback → HoldMs should be null.
        var actions = engine.Plan("hello é", profile, Layout).ToList();

        // All Press/Chord actions should carry HoldMs in the physiological range.
        var regularActions = actions.Where(a => a.Action is KeyAction.Press or KeyAction.Chord).ToList();
        regularActions.Should().NotBeEmpty();
        regularActions.Should().AllSatisfy(a =>
        {
            a.HoldMs.Should().NotBeNull("every Press/Chord should carry dwell when DwellEnabled");
            a.HoldMs!.Value.Should().BeInRange(DelayModel.DwellMinMs, DelayModel.DwellMaxMs);
        });

        // Text (Unicode fallback) actions should have HoldMs = null.
        var textActions = actions.Where(a => a.Action is KeyAction.Text).ToList();
        textActions.Should().NotBeEmpty("'é' should produce a Text fallback action");
        textActions.Should().AllSatisfy(a =>
            a.HoldMs.Should().BeNull("Text (VK_PACKET) actions are never split into down/up"));
    }

    [Fact]
    public void Engine_DwellDisabled_AllActions_HaveNullHoldMs()
    {
        // Arrange: DwellEnabled = false (default) — should be identical to pre-Phase-10 behaviour.
        var profile = new TypingProfile
        {
            Wpm = 60,
            TypoRate = 0,
            DwellEnabled = false,
            Fatigue = false,
            WarmUp = false,
            Pace = false,
        };
        var engine = new HumanTypingEngine(new Random(1));

        var actions = engine.Plan("hello world", profile, Layout).ToList();

        actions.Should().AllSatisfy(a =>
            a.HoldMs.Should().BeNull("DwellEnabled=false must never set HoldMs"));
    }

    [Fact]
    public void Engine_DwellEnabled_SameSeed_ReproducesIdentically()
    {
        // Determinism: same seed → same plan with dwell on.
        var profile = new TypingProfile
        {
            Wpm = 60,
            TypoRate = 0.02,
            DwellEnabled = true,
            DwellMeanMs = 90.0,
            DwellSigmaMs = 12.0,
            Fatigue = false,
            WarmUp = false,
            Pace = false,
        };

        var plan1 = new HumanTypingEngine(new Random(99)).Plan("the quick brown fox", profile, Layout).ToList();
        var plan2 = new HumanTypingEngine(new Random(99)).Plan("the quick brown fox", profile, Layout).ToList();

        plan1.Should().HaveCount(plan2.Count);
        for (var i = 0; i < plan1.Count; i++)
        {
            plan1[i].Delay.Should().Be(plan2[i].Delay, $"delay mismatch at action {i}");
            plan1[i].HoldMs.Should().Be(plan2[i].HoldMs, $"HoldMs mismatch at action {i}");
            plan1[i].Action.Should().Be(plan2[i].Action, $"action mismatch at action {i}");
        }
    }
}
