using FluentAssertions;
using TypeGent.Core.HumanTyping;
using TypeGent.Core.Layouts;
using Xunit;

namespace TypeGent.Tests;

/// <summary>
/// v2 Phase 3 – Layout metadata accuracy and biomechanical timing multiplier.
/// </summary>
public class BiomechanicalTimingTests
{
    private static readonly UsQwertyLayout Layout = new();

    // ----------------------------------------------------------------
    // 3.1  Layout metadata spot-checks
    // ----------------------------------------------------------------

    [Theory]
    // Home row: A (left pinky), F (left index), J (right index), L (right ring)
    [InlineData('a', Hand.Left,  Finger.Pinky,  0)]
    [InlineData('s', Hand.Left,  Finger.Ring,   0)]
    [InlineData('d', Hand.Left,  Finger.Middle, 0)]
    [InlineData('f', Hand.Left,  Finger.Index,  0)]
    [InlineData('j', Hand.Right, Finger.Index,  0)]
    [InlineData('k', Hand.Right, Finger.Middle, 0)]
    [InlineData('l', Hand.Right, Finger.Ring,   0)]
    // Top row
    [InlineData('q', Hand.Left,  Finger.Pinky,  1)]
    [InlineData('e', Hand.Left,  Finger.Middle, 1)]
    [InlineData('r', Hand.Left,  Finger.Index,  1)]
    [InlineData('y', Hand.Right, Finger.Index,  1)]
    [InlineData('p', Hand.Right, Finger.Pinky,  1)]
    // Bottom row
    [InlineData('z', Hand.Left,  Finger.Pinky, -1)]
    [InlineData('c', Hand.Left,  Finger.Middle,-1)]
    [InlineData('n', Hand.Right, Finger.Index, -1)]
    [InlineData('m', Hand.Right, Finger.Index, -1)]
    public void TryGetMeta_ReturnsCorrectHandFingerRow(char c, Hand expectedHand, Finger expectedFinger, int expectedRow)
    {
        Layout.TryGetMeta(c, out var meta).Should().BeTrue();
        meta.Hand.Should().Be(expectedHand);
        meta.Finger.Should().Be(expectedFinger);
        meta.Row.Should().Be(expectedRow);
    }

    [Fact]
    public void TryGetMeta_CaseInsensitive_BothCasesReturnSameMeta()
    {
        Layout.TryGetMeta('a', out var lower).Should().BeTrue();
        Layout.TryGetMeta('A', out var upper).Should().BeTrue();
        upper.Should().Be(lower);
    }

    [Theory]
    [InlineData('1')]    // digit — no biomechanical data
    [InlineData(' ')]    // space bar — not letter-keyed
    [InlineData(',')]    // punctuation
    [InlineData('é')]    // out-of-layout
    public void TryGetMeta_ReturnsFalseForNonLetterKeys(char c)
    {
        Layout.TryGetMeta(c, out _).Should().BeFalse();
    }

    // ----------------------------------------------------------------
    // 3.2  Biomechanical multiplier ordering (§2.4 self-check)
    // ----------------------------------------------------------------

    /// <summary>
    /// Measures the median sampled delay for a digraph by running many seeds.
    /// All context modifiers except biomechanical are held neutral (no fatigue,
    /// no warm-up, no pace, base delay 200 ms, neutral bigram "az" replaced by
    /// the target pair).
    /// </summary>
    private static double MedianDelayForDigraph(char prev, char cur, int samples = 10_000)
    {
        var model = new DelayModel(new Random(42), jitter: 0.10);
        var delays = new double[samples];
        for (var i = 0; i < samples; i++)
            delays[i] = model.SampleDelayMs(200, new TypingContext
            {
                PreviousChar = prev,
                CurrentChar = cur,
                Fatigue = false,
                WarmUp = false,
                Pace = false,
                Layout = Layout,
            });
        Array.Sort(delays);
        return delays[samples / 2];
    }

    [Fact]
    public void BiomechanicalMultiplier_AlternatingHand_FasterThanSameHandDifferentFinger()
    {
        // e→j : left-middle → right-index (alternating hand)
        var altHand = MedianDelayForDigraph('e', 'j');
        // e→d : left-middle → left-middle-adjacent … no — pick same-hand, diff-finger
        // d→f : left-middle → left-index (same hand, different finger)
        var sameHandDiffFinger = MedianDelayForDigraph('d', 'f');

        altHand.Should().BeLessThan(sameHandDiffFinger,
            "alternating-hand digraphs should be faster than same-hand different-finger digraphs");
    }

    [Fact]
    public void BiomechanicalMultiplier_SameHandDifferentFinger_FasterThanSameFinger()
    {
        // d→f : left-middle → left-index (same hand, different finger) — bio ×1.15
        // d→d : double letter (left-middle → left-middle, same finger) — bio ×1.38
        // Both bigrams are infrequent in English (corpus mult ~0.95–0.98), so the
        // biomechanical ordering (1.15 < 1.38) dominates the corpus-driven offset (v2 Phase 8).
        var sameHandDiffFinger = MedianDelayForDigraph('d', 'f');
        var sameFinger         = MedianDelayForDigraph('d', 'd');

        sameHandDiffFinger.Should().BeLessThan(sameFinger,
            "same-hand-different-finger digraphs should be faster than same-finger (double-letter) digraphs");
    }

    [Fact]
    public void BiomechanicalMultiplier_SameFinger_SlowerThanAlternatingHand()
    {
        // e→n : left-middle → right-index (alternating hand, bio ×1.00).
        //       'en' is a very common English bigram (freq ~3.44) → corpus mult ~0.655 → total ~0.655.
        // d→d : double letter (left-middle → left-middle, same finger, bio ×1.38).
        //       'dd' is a rare bigram (freq ~0.15) → corpus mult ~0.949 → total ~1.309.
        // 0.655 ≪ 1.309: the alternating-hand pair is clearly faster despite any corpus offset (v2 Phase 8).
        var altHand   = MedianDelayForDigraph('e', 'n');
        var sameFinger = MedianDelayForDigraph('d', 'd');

        altHand.Should().BeLessThan(sameFinger,
            "alternating-hand digraphs should be measurably faster than same-finger digraphs");
    }

    [Fact]
    public void BiomechanicalMultiplier_DoubleLetter_AppliesSameFingerPenalty()
    {
        // "ee" — same physical key struck twice
        var doubleLetter = MedianDelayForDigraph('e', 'e');
        // e→j — alternating hand (baseline)
        var altHand = MedianDelayForDigraph('e', 'j');

        doubleLetter.Should().BeGreaterThan(altHand,
            "double-letter digraphs should be slower than alternating-hand digraphs");
    }

    [Fact]
    public void BiomechanicalMultiplier_RowJump_AddsDistancePenalty()
    {
        // q→z : left-pinky, both left-pinky, but different rows (top vs bottom → large Y distance)
        var rowJump = MedianDelayForDigraph('q', 'z');
        // q→a : left-pinky → left-pinky adjacent row (top → home, smaller distance)
        var adjacentRow = MedianDelayForDigraph('q', 'a');

        // Both are same-hand same-finger; the row jump (q→z crosses two rows) should be slower.
        rowJump.Should().BeGreaterThan(adjacentRow,
            "a two-row jump should add more distance penalty than a one-row adjacent move");
    }

    [Fact]
    public void BiomechanicalMultiplier_NoLayout_LeavesDelayUnchanged()
    {
        // Without a layout the biomechanical multiplier should not fire (returns 1.0).
        var withLayout = new DelayModel(new Random(7), jitter: 0.10).SampleDelayMs(200, new TypingContext
        {
            PreviousChar = 'r', CurrentChar = 't', Fatigue = false, Layout = Layout,
        });
        var withoutLayout = new DelayModel(new Random(7), jitter: 0.10).SampleDelayMs(200, new TypingContext
        {
            PreviousChar = 'r', CurrentChar = 't', Fatigue = false, Layout = null,
        });

        // With layout: same-finger → multiplier > 1.0 → delay larger
        withLayout.Should().BeGreaterThan(withoutLayout,
            "supplying a layout for a same-finger digraph should produce a larger delay than no layout");
    }

    [Fact]
    public void BiomechanicalMultiplier_DoesNotAffectSeedReproducibility()
    {
        // Adding a layout must not change the RNG draw order (no RNG in the biomechanical path).
        var profileWithLayout = new TypingProfile { TypoRate = 0.2, Wpm = 75, Jitter = 0.4 };
        var layout = new UsQwertyLayout();

        var first  = new HumanTypingEngine(new Random(99)).Plan("The quick brown fox.", profileWithLayout, layout).ToList();
        var second = new HumanTypingEngine(new Random(99)).Plan("The quick brown fox.", profileWithLayout, layout).ToList();

        second.Should().Equal(first, "same seed + same layout must reproduce identically");
    }
}
