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
        // Fixed seed -> deterministic. A log-normal is right-skewed, so assert on the MEDIAN
        // (which equals the base), NOT the mean (which is ~6% higher at sigma = 0.35).
        var model = new DelayModel(new Random(1234));
        var samples = Enumerable.Range(0, 10_000)
            .Select(_ => model.SampleDelayMs(100, new TypingContext())) // neutral: no modifiers
            .OrderBy(d => d)
            .ToList();

        var median = samples[samples.Count / 2];

        median.Should().BeApproximately(100, 5);
        samples.Average().Should().BeGreaterThan(median); // skewed right
        samples.Should().AllSatisfy(d => d.Should().BeInRange(20, 2000));
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
}
