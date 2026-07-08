using System.Linq;
using FluentAssertions;
using TypeGent.Core.HumanTyping;
using Xunit;

namespace TypeGent.Tests;

public class DelayModelTests
{
    [Fact]
    public void Median_TracksBaseDelay()
    {
        // Fixed seed -> deterministic. A (shifted) log-normal is right-skewed, so assert on the
        // MEDIAN (which equals the base), NOT the mean (which is ~6% higher at sigma = 0.35).
        // v2 Phase 1: the shifted log-normal adds a 45 ms floor, so the floor is the lower bound
        // instead of v1's 20 ms clamp, and the median is still floor + (base - floor) == base.
        var model = new DelayModel(new Random(1234));
        var samples = Enumerable.Range(0, 10_000)
            .Select(_ => model.SampleDelayMs(100, new TypingContext())) // neutral: no modifiers
            .OrderBy(d => d)
            .ToList();

        var median = samples[samples.Count / 2];

        median.Should().BeApproximately(100, 5);
        samples.Average().Should().BeGreaterThan(median); // skewed right
        samples.Should().AllSatisfy(d => d.Should().BeInRange(DelayModel.FloorMs, DelayModel.MaxDelayMs));
    }

    [Theory]
    [InlineData(false, ' ', '\0', 'x', 1.0)]     // neutral
    [InlineData(true, '\0', '\0', 'x', 1.15)]    // needs shift
    [InlineData(false, ' ', ' ', 'x', 1.5)]      // follows a space
    [InlineData(false, '\0', 't', 'h', 0.7)]     // common bigram "th"
    public void Modifiers_ScaleTheSampleDeterministically(
        bool needsShift, char ignored, char prev, char cur, double expectedFactor)
    {
        _ = ignored;
        // Same seed -> same underlying Gaussian draw, so the only difference is the modifier.
        var neutral = new DelayModel(new Random(7)).SampleDelayMs(100, new TypingContext());
        var modified = new DelayModel(new Random(7)).SampleDelayMs(100, new TypingContext
        {
            NeedsShift = needsShift,
            PreviousChar = prev,
            CurrentChar = cur,
            Fatigue = false,
        });

        modified.Should().BeApproximately(neutral * expectedFactor, 1e-6);
    }

    [Fact]
    public void Fatigue_SlowsTypingOverTime()
    {
        var early = new DelayModel(new Random(3)).SampleDelayMs(100,
            new TypingContext { CharsTypedSoFar = 0, Fatigue = true });
        var late = new DelayModel(new Random(3)).SampleDelayMs(100,
            new TypingContext { CharsTypedSoFar = 1000, Fatigue = true });

        late.Should().BeApproximately(early * (1.0 + 0.0005 * 1000), 1e-6);
    }

    [Fact]
    public void WarmUp_RampShortensEarlyDelays()
    {
        // Same seed -> same Gaussian draw; only the warm-up factor differs. The ramp is
        // 1 + 0.20·exp(-CharsTypedSoFar/40): 1.20 at 0, decaying toward 1.0. Fatigue is disabled
        // to isolate the warm-up effect (fatigue also scales with CharsTypedSoFar).
        var early = new DelayModel(new Random(11)).SampleDelayMs(100,
            new TypingContext { CharsTypedSoFar = 0, WarmUp = true, Fatigue = false });
        var late = new DelayModel(new Random(11)).SampleDelayMs(100,
            new TypingContext { CharsTypedSoFar = 100, WarmUp = true, Fatigue = false });

        early.Should().BeGreaterThan(late);
        var expectedRatio = (1.0 + 0.20 * Math.Exp(-100.0 / 40.0)) / (1.0 + 0.20 * Math.Exp(0));
        late.Should().BeApproximately(early * expectedRatio, 1e-6);
    }

    [Theory]
    [InlineData(' ', 1.5)]
    [InlineData(',', 1.8)]
    [InlineData(';', 1.8)]
    [InlineData(':', 1.8)]
    [InlineData('.', 3.0)]
    [InlineData('!', 3.0)]
    [InlineData('?', 3.0)]
    [InlineData('\n', 5.0)]
    public void BoundaryMultiplier_LengthensDelayAfterBoundary(char prev, double factor)
    {
        // "az" is in neither the fast nor the slow set, so it is a clean neutral reference.
        var neutral = new DelayModel(new Random(7)).SampleDelayMs(100,
            new TypingContext { PreviousChar = 'a', CurrentChar = 'z' });
        var afterBoundary = new DelayModel(new Random(7)).SampleDelayMs(100,
            new TypingContext { PreviousChar = prev, CurrentChar = 'z' });

        afterBoundary.Should().BeApproximately(neutral * factor, 1e-6);
    }

    [Theory]
    [InlineData("th", 0.7)]
    [InlineData("he", 0.7)]
    [InlineData("in", 0.7)]
    [InlineData("st", 0.7)]
    [InlineData("of", 0.7)]
    [InlineData("qx", 1.15)]
    public void BigramTable_FastAndSlowSetsApply(string pair, double factor)
    {
        var neutral = new DelayModel(new Random(7)).SampleDelayMs(100,
            new TypingContext { PreviousChar = 'a', CurrentChar = 'z' });
        var bigram = new DelayModel(new Random(7)).SampleDelayMs(100,
            new TypingContext { PreviousChar = pair[0], CurrentChar = pair[1] });

        bigram.Should().BeApproximately(neutral * factor, 1e-6);
    }

    [Fact]
    public void LapseRate_One_AddsLongPauseToEverySample()
    {
        var model = new DelayModel(new Random(5), 0.35, lapseRate: 1.0, lapseMinMs: 1500, lapseMaxMs: 4000);
        var samples = Enumerable.Range(0, 100)
            .Select(_ => model.SampleDelayMs(100, new TypingContext())).ToList();

        samples.Should().AllSatisfy(d => d.Should().BeGreaterThanOrEqualTo(1500));
    }

    [Fact]
    public void LapseRate_Zero_NeverLapses()
    {
        var model = new DelayModel(new Random(5), 0.35, lapseRate: 0.0);
        var samples = Enumerable.Range(0, 2000)
            .Select(_ => model.SampleDelayMs(100, new TypingContext())).ToList();

        samples.Should().AllSatisfy(d => d.Should().BeLessThan(1500));
    }

    [Fact]
    public void LapseRate_FiresAtConfiguredRate()
    {
        var model = new DelayModel(new Random(123), 0.35, lapseRate: 0.05, lapseMinMs: 1500, lapseMaxMs: 4000);
        var samples = Enumerable.Range(0, 4000)
            .Select(_ => model.SampleDelayMs(100, new TypingContext())).ToList();

        var lapses = samples.Count(d => d >= 1500);
        // 5% of 4000 = 200; generous binomial tolerance.
        lapses.Should().BeInRange(120, 320);
    }

    // --- v2 Phase 2: autocorrelated pace (AR(1)) ---

    private static double Lag1Autocorrelation(IReadOnlyList<double> xs)
    {
        var n = xs.Count;
        if (n < 2) return 0;
        var mean = xs.Average();
        double num = 0;
        for (var i = 0; i < n - 1; i++)
            num += (xs[i] - mean) * (xs[i + 1] - mean);
        double den = 0;
        for (var i = 0; i < n; i++)
            den += (xs[i] - mean) * (xs[i] - mean);
        return den == 0 ? 0 : num / den;
    }

    [Fact]
    public void Pace_WhenEnabled_InducesPositiveLag1Autocorrelation()
    {
        // Low jitter so the AR(1) pace signal is clearly visible above per-key noise. Neutral
        // context (no fatigue/warm-up) so pace is the ONLY source of autocorrelation.
        var model = new DelayModel(new Random(1234), jitter: 0.1, paceSigma: 0.3);
        var ctx = new TypingContext { Pace = true, Fatigue = false };

        var samples = Enumerable.Range(0, 5000)
            .Select(_ => model.SampleDelayMs(100, ctx)).ToList();

        var r1 = Lag1Autocorrelation(samples);
        r1.Should().BeGreaterThan(0.15, "AR(1) pace should induce clearly positive lag-1 autocorrelation");
    }

    [Fact]
    public void Pace_WhenDisabled_HasNearZeroAutocorrelation()
    {
        var model = new DelayModel(new Random(1234), jitter: 0.1, paceSigma: 0.3);
        var ctx = new TypingContext { Pace = false, Fatigue = false };

        var samples = Enumerable.Range(0, 5000)
            .Select(_ => model.SampleDelayMs(100, ctx)).ToList();

        var r1 = Lag1Autocorrelation(samples);
        r1.Should().BeInRange(-0.10, 0.10, "i.i.d. log-normal samples should have ~0 autocorrelation");
    }
}
