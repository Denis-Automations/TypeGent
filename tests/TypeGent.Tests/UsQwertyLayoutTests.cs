using FluentAssertions;
using TypeGent.Core.Layouts;
using TypeGent.Core.Typing;
using Xunit;

namespace TypeGent.Tests;

public class UsQwertyLayoutTests
{
    private readonly UsQwertyLayout _layout = new();

    [Theory]
    [InlineData('a', VirtualKey.A, false)]
    [InlineData('Z', VirtualKey.Z, true)]
    [InlineData('!', VirtualKey.D1, true)]
    [InlineData('5', VirtualKey.D5, false)]
    [InlineData('[', VirtualKey.OEM_4, false)]
    [InlineData(';', VirtualKey.OEM_1, false)]
    [InlineData(',', VirtualKey.OEM_COMMA, false)]
    public void MapsRepresentativeChars(char c, VirtualKey expectedVk, bool expectedShift)
    {
        _layout.CanMap(c).Should().BeTrue();
        _layout.MapChar(c).Should().Be(expectedVk);
        _layout.NeedsShift(c).Should().Be(expectedShift);
    }

    [Fact]
    public void Comma_IsUnshiftedOemComma()
    {
        // NOTE: this intentionally CORRECTS the example in plan.md §3.6, which asserted
        // NeedsShift(',') == true. On a physical US QWERTY keyboard ',' is the UNSHIFTED
        // character on the OEM_COMMA key — '<' is its shifted partner. Shifting it would
        // type '<' and break the "real comma, not <" success criterion.
        _layout.NeedsShift(',').Should().BeFalse();
        _layout.MapChar(',').Should().Be(VirtualKey.OEM_COMMA);
    }

    [Fact]
    public void OutOfLayoutChar_RoutesToUnicodeFallback()
    {
        _layout.CanMap('é').Should().BeFalse();
        _layout.ToAction('é').Should().Be(new KeyAction.Text("é"));
    }

    [Fact]
    public void HelloWorldActions_UseVkChordPath_WithRealComma()
    {
        // Spot-check the action each character resolves to.
        _layout.ToAction('H').Should().Be(new KeyAction.Chord(VirtualKey.Shift, VirtualKey.H));
        _layout.ToAction('e').Should().Be(new KeyAction.Press(VirtualKey.E));
        _layout.ToAction(' ').Should().Be(new KeyAction.Press(VirtualKey.Space));
        _layout.ToAction(',').Should().Be(new KeyAction.Press(VirtualKey.OEM_COMMA));
        _layout.ToAction('!').Should().Be(new KeyAction.Chord(VirtualKey.Shift, VirtualKey.D1));
    }
}
