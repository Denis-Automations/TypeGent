using System.Linq;
using FluentAssertions;
using TypeGent.Core.HumanTyping;
using Xunit;

namespace TypeGent.Tests;

public class ErrorModelTests
{
    [Theory]
    [InlineData(0.0, 0, 0)]
    [InlineData(1.0, 100, 0)]
    [InlineData(0.5, 50, 20)]
    public void ShouldIntroduceTypo_ProbabilityGates(double typoRate, int expected, int tolerance)
    {
        var model = new ErrorModel(new Random(0));
        var hits = Enumerable.Range(0, 100).Count(_ => model.ShouldIntroduceTypo(typoRate));

        if (tolerance == 0)
        {
            hits.Should().Be(expected);
        }
        else
        {
            hits.Should().BeInRange(expected - tolerance, expected + tolerance);
        }
    }

    [Fact]
    public void ChooseKind_OnlyReturnsApplicableKinds()
    {
        var model = new ErrorModel(new Random(123));

        for (var i = 0; i < 100; i++)
        {
            var kind = model.ChooseKind(canTranspose: false, canShiftMistime: false, canMissDouble: false);
            kind.Should().BeOneOf(TypoKind.AdjacentSlip, TypoKind.RepeatedKey, TypoKind.Omission);
        }

        for (var i = 0; i < 100; i++)
        {
            var kind = model.ChooseKind(canTranspose: true, canShiftMistime: true, canMissDouble: true);
            kind.Should().BeOneOf(
                TypoKind.AdjacentSlip,
                TypoKind.RepeatedKey,
                TypoKind.Transposition,
                TypoKind.ShiftMistime,
                TypoKind.Omission,
                TypoKind.MissingDouble);
        }
    }

    [Fact]
    public void AdjacentKey_PreservesCaseAndStaysWithinKnownNeighbors()
    {
        var model = new ErrorModel(new Random(42));

        for (var i = 0; i < 50; i++)
        {
            model.AdjacentKey('A').Should().BeOneOf('W', 'Q', 'S', 'Z');
            model.AdjacentKey('a').Should().BeOneOf('w', 'q', 's', 'z');
        }
    }

    [Fact]
    public void AdjacentKey_FallsBackToIntended_WhenNoNeighbors()
    {
        var model = new ErrorModel(new Random(42));

        // '1' is not a letter and has no QWERTY neighbor table entry.
        model.AdjacentKey('1').Should().Be('1');
    }

    [Fact]
    public void ReactionDelayMs_IsWithinDocumentedRange()
    {
        var model = new ErrorModel(new Random(7));

        for (var i = 0; i < 100; i++)
        {
            model.ReactionDelayMs().Should().BeInRange(150, 449);
        }
    }

    [Fact]
    public void ExtraRepeats_ReturnsOneOrTwo()
    {
        var model = new ErrorModel(new Random(99));
        var values = Enumerable.Range(0, 200).Select(_ => model.ExtraRepeats()).Distinct();
        values.Should().BeSubsetOf(new[] { 1, 2 });
    }
}
