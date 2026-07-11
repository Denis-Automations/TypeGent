using System;
using System.Linq;
using FluentAssertions;
using TypeGent.Core.HumanTyping;
using TypeGent.Core.Layouts;
using Xunit;

namespace TypeGent.Tests;

/// <summary>
/// v2 Phase 4 – Pre-word planning latency.
/// Verifies that the post-space delay scales with upcoming word length and rarity, that the
/// legacy flat ×1.5 is preserved when no lookahead data is supplied, and that determinism
/// is unaffected.
/// </summary>
public class PreWordPlanningTests
{
    private static readonly UsQwertyLayout Layout = new();

    // -----------------------------------------------------------------------
    // Formula / DelayModel unit tests (controlled contexts, no engine)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Helper: sample 10 000 delays at a word boundary with a fixed seed and return the median.
    /// Low jitter so the planning multiplier is clearly visible above noise.
    /// </summary>
    private static double MedianSpaceDelay(int wordLen, bool isCommon, int seed = 42)
    {
        var model = new DelayModel(new Random(seed), jitter: 0.05);
        var samples = Enumerable.Range(0, 10_000)
            .Select(_ => model.SampleDelayMs(200, new TypingContext
            {
                PreviousChar = ' ',
                CurrentChar = 'x',
                Fatigue = false,
                NextWordLength = wordLen,
                NextWordIsCommon = isCommon,
            }))
            .OrderBy(d => d)
            .ToList();
        return samples[samples.Count / 2];
    }

    [Fact]
    public void PlanningPause_LongerWord_LargerDelay()
    {
        // 3-letter rare vs 12-letter rare — only length differs.
        var shortWord = MedianSpaceDelay(wordLen: 3,  isCommon: false);
        var longWord  = MedianSpaceDelay(wordLen: 12, isCommon: false);

        longWord.Should().BeGreaterThan(shortWord,
            "a longer word requires more planning time and should lengthen the pre-word pause");
    }

    [Fact]
    public void PlanningPause_RareWord_LargerThanCommonWord_SameLength()
    {
        // 5-letter common vs 5-letter rare — only rarity differs.
        var common = MedianSpaceDelay(wordLen: 5, isCommon: true);
        var rare   = MedianSpaceDelay(wordLen: 5, isCommon: false);

        rare.Should().BeGreaterThan(common,
            "an uncommon word should add a rarity penalty (+0.3×) on top of the length term");
    }

    [Fact]
    public void PlanningPause_LongRareWord_LargerThan_ShortCommonWord()
    {
        // The composite effect: short common ("the", 3) vs long rare ("incomprehensible", 16).
        var shortCommon = MedianSpaceDelay(wordLen: 3,  isCommon: true);
        var longRare    = MedianSpaceDelay(wordLen: 16, isCommon: false);

        longRare.Should().BeGreaterThan(shortCommon);

        // Exact multiplier ratio: (1.0 + 0.06×16 + 0.3) / (1.0 + 0.06×3) = 2.26 / 1.18 ≈ 1.915
        // Both samples use same seed → same Gaussian draw, so ratio equals multiplier ratio.
        (longRare / shortCommon).Should().BeApproximately(2.26 / 1.18, 0.01);
    }

    [Fact]
    public void PlanningPause_ExactMultiplier_LengthFormula()
    {
        // Validate the formula directly. Same seed, same base → ratio == multiplier ratio.
        // 7-letter rare: 1.0 + 0.06*7 + 0.3 = 1.72
        // 7-letter common: 1.0 + 0.06*7 = 1.42
        var common = MedianSpaceDelay(wordLen: 7, isCommon: true);
        var rare   = MedianSpaceDelay(wordLen: 7, isCommon: false);

        (rare / common).Should().BeApproximately(1.72 / 1.42, 0.01);
    }

    [Fact]
    public void PlanningPause_Clamps_At_MaxMultiplier()
    {
        // A very long rare word (e.g. 50 letters) should not exceed the ×3.5 ceiling.
        // Formula without clamp: 1.0 + 0.06*50 + 0.3 = 4.30 → clamped to 3.5.
        // Formula for 16-letter rare: 1.0 + 0.06*16 + 0.3 = 2.26 (not clamped).
        var clamped   = MedianSpaceDelay(wordLen: 50, isCommon: false);
        var unclamped = MedianSpaceDelay(wordLen: 16, isCommon: false);

        // The clamped (50-letter) delay should not scale linearly beyond the 3.5 ceiling.
        // Ratio should be 3.5 / 2.26 ≈ 1.549, not 4.30 / 2.26 ≈ 1.903.
        (clamped / unclamped).Should().BeApproximately(3.5 / 2.26, 0.01);
    }

    [Fact]
    public void PlanningPause_ZeroWordLength_UsesLegacyFlat150()
    {
        // When NextWordLength == 0 (no lookahead), the space multiplier must remain ×1.5
        // so every prior test and user that doesn't set word-context stays unaffected.
        // Use prev='\0' as neutral so BigramTable returns exactly 1.0 (v2 Phase 8: the corpus
        // table gives non-1.0 multipliers to letter pairs like "ax"). The space case uses a
        // non-letter prev (' ') so its bigram mult is also 1.0, keeping the ratio exactly ×1.5.
        var neutral  = new DelayModel(new Random(7), jitter: 0.05).SampleDelayMs(200,
            new TypingContext { PreviousChar = '\0', CurrentChar = 'x', Fatigue = false });
        var noLook   = new DelayModel(new Random(7), jitter: 0.05).SampleDelayMs(200,
            new TypingContext { PreviousChar = ' ', CurrentChar = 'x', Fatigue = false,
                                NextWordLength = 0 });

        noLook.Should().BeApproximately(neutral * 1.5, 1e-6,
            "zero NextWordLength must preserve the legacy flat ×1.5 space multiplier");
    }

    // -----------------------------------------------------------------------
    // DelayModel.IsCommonWord spot-checks
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("the",   true)]
    [InlineData("and",   true)]
    [InlineData("have",  true)]
    [InlineData("THE",   true)]   // case-insensitive
    [InlineData("people",true)]
    [InlineData("something", true)]
    [InlineData("incomprehensible", false)]
    [InlineData("phosphorescent",   false)]
    [InlineData("xylophone",        false)]
    public void IsCommonWord_MatchesExpected(string word, bool expected)
    {
        DelayModel.IsCommonWord(word).Should().Be(expected);
    }

    // -----------------------------------------------------------------------
    // Engine integration: word lookahead wired end-to-end
    // -----------------------------------------------------------------------

    /// <summary>
    /// Measure the median delay at the first character of the second word in a two-word
    /// sentence (e.g. "a quick") across many seeds with a very low jitter so the planning
    /// signal dominates.
    /// </summary>
    private static double MedianEngineSpaceDelay(string text, int targetActionIndex,
        int samples = 2000)
    {
        var profile = new TypingProfile
        {
            Wpm = 60, Jitter = 0.05, TypoRate = 0.0, Fatigue = false, WarmUp = false,
        };
        var delays = new double[samples];
        for (var s = 0; s < samples; s++)
        {
            var actions = new HumanTypingEngine(new Random(s))
                .Plan(text, profile, Layout).ToList();
            delays[s] = actions[targetActionIndex].Delay.TotalMilliseconds;
        }
        Array.Sort(delays);
        return delays[samples / 2];
    }

    [Fact]
    public void Engine_DelayAfterSpace_LargerBeforeLongRareWord_ThanShortCommonWord()
    {
        // "a the" → 'a'[0] ' '[1] 't'[2]  — 't' follows space before "the" (3 letters, common)
        // "a incomprehensible" → 'a'[0] ' '[1] 'i'[2] — follows space before 16-letter rare word
        var delayBeforeCommon = MedianEngineSpaceDelay("a the", targetActionIndex: 2);
        var delayBeforeRare   = MedianEngineSpaceDelay("a incomprehensible", targetActionIndex: 2);

        delayBeforeRare.Should().BeGreaterThan(delayBeforeCommon,
            "engine should produce a longer pre-word pause before a long/rare word than a short/common one");
    }

    [Fact]
    public void Engine_DelayAfterSpace_ScalesWithWordLength()
    {
        // "a cat" (3 letters, rare) vs "a behavior" (8 letters, rare)
        var shortDelay = MedianEngineSpaceDelay("a cat",      targetActionIndex: 2);
        var longDelay  = MedianEngineSpaceDelay("a behavior", targetActionIndex: 2);

        longDelay.Should().BeGreaterThan(shortDelay,
            "pre-word pause should grow with word length (all else equal)");
    }

    [Fact]
    public void Engine_SameSeed_StillReproducesIdenticalPlan_WithPlanningPause()
    {
        var profile = new TypingProfile { TypoRate = 0.3, Wpm = 75, Jitter = 0.4 };
        var first  = new HumanTypingEngine(new Random(99)).Plan("The quick brown fox.", profile, Layout).ToList();
        var second = new HumanTypingEngine(new Random(99)).Plan("The quick brown fox.", profile, Layout).ToList();

        second.Should().Equal(first, "planning pause is deterministic — same seed must reproduce identically");
    }

    [Fact]
    public void Engine_NetTypedText_StillEqualsInput_WithPlanningPause()
    {
        const string input = "The quick, brown Fox jumps! Over lazy dogs.";
        for (var seed = 0; seed < 20; seed++)
        {
            var profile = new TypingProfile { TypoRate = 0.5, Wpm = 65, Jitter = 0.35 };
            var actions = new HumanTypingEngine(new Random(seed)).Plan(input, profile, Layout);
            HumanTypingEngineTests.Reconstruct(actions, Layout).Should().Be(input,
                $"net-text invariant must hold (seed {seed})");
        }
    }
}
