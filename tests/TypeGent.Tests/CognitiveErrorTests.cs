using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentAssertions;
using TypeGent.Core.HumanTyping;
using TypeGent.Core.Layouts;
using TypeGent.Core.Typing;
using Xunit;

namespace TypeGent.Tests;

/// <summary>
/// v2 Phase 7 – Cognitive / linguistic errors: misspelling dictionary and autocorrect
/// simulation. The net-text-equals-input invariant must hold for every scenario.
/// </summary>
public class CognitiveErrorTests
{
    private static readonly UsQwertyLayout Layout = new();

    // Helper that builds the net text from an action stream, honouring
    // both KeyAction.Text (used by autocorrect) and backspaces.
    private static string Reconstruct(IEnumerable<TimedAction> actions)
    {
        var sb = new StringBuilder();
        var reverseMap = new Dictionary<KeyAction, char>();
        foreach (var ch in Layout.SupportedChars)
            reverseMap[Layout.ToAction(ch)] = ch;

        foreach (var t in actions)
        {
            switch (t.Action)
            {
                case KeyAction.Press p when p.Key == VirtualKey.Back:
                    if (sb.Length > 0) sb.Length--;
                    break;
                case KeyAction.Text txt:
                    sb.Append(txt.Value);
                    break;
                default:
                    if (reverseMap.TryGetValue(t.Action, out var c))
                        sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    // ----------------------------------------------------------------
    // 7.1  MisspellingDictionary unit tests
    // ----------------------------------------------------------------

    [Fact]
    public void MisspellingDictionary_ContainsKnownEntries()
    {
        // Spot-check a handful of classic entries.
        MisspellingDictionary.TryGet("receive",    out var m1).Should().BeTrue();
        m1.Should().Be("recieve");

        MisspellingDictionary.TryGet("believe",    out var m2).Should().BeTrue();
        m2.Should().Be("beleive");

        MisspellingDictionary.TryGet("separate",   out var m3).Should().BeTrue();
        m3.Should().Be("seperate");

        MisspellingDictionary.TryGet("definitely", out var m4).Should().BeTrue();
        m4.Should().Be("definately");

        MisspellingDictionary.TryGet("committee",  out var m5).Should().BeTrue();
        m5.Should().Be("commitee");
    }

    [Fact]
    public void MisspellingDictionary_ReturnsFalse_ForUnknownWord()
    {
        MisspellingDictionary.TryGet("xyzzy",    out _).Should().BeFalse();
        MisspellingDictionary.TryGet("algorithm", out _).Should().BeFalse();
    }

    [Fact]
    public void MisspellingDictionary_IsCaseInsensitive()
    {
        // TryGet should match regardless of caller's casing.
        MisspellingDictionary.TryGet("Receive",   out var m1).Should().BeTrue();
        MisspellingDictionary.TryGet("RECEIVE",   out var m2).Should().BeTrue();
        m1.Should().Be("recieve");
        m2.Should().Be("recieve");
    }

    [Fact]
    public void MisspellingDictionary_HasAtLeastThirtyEntries()
    {
        // Ensure the dictionary ships with enough entries to be meaningful.
        MisspellingDictionary.Entries.Count.Should().BeGreaterThanOrEqualTo(30,
            "the dictionary should ship with at least 30 curated misspelling pairs");
    }

    // ----------------------------------------------------------------
    // 7.1b  Phase A5 — dictionary cleanliness guards
    // ----------------------------------------------------------------

    [Fact]
    public void MisspellingDictionary_HasNoNoOpEntries()
    {
        // No entry should map a word to itself — those are dead data (TryGet filters them,
        // but they shouldn't exist in the first place after the A5 cleanup).
        var noOps = MisspellingDictionary.Entries
            .Where(kv => string.Equals(kv.Key, kv.Value, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .ToList();

        noOps.Should().BeEmpty("no entry should map a word to itself (a no-op)");
    }

    [Fact]
    public void MisspellingDictionary_DuplicateKeys_AreDeduplicatedToCanonicalEntry()
    {
        // "occurrence" previously appeared twice (occurence / occurrance); the second silently
        // overwrote the first. After A5 a single canonical entry must remain.
        MisspellingDictionary.TryGet("occurrence", out var occ).Should().BeTrue();
        occ.Should().Be("occurence",
            "the duplicate 'occurrence' entry is collapsed to one canonical misspelling");
    }

    [Fact]
    public void MisspellingDictionary_ExcludesWrongWordHomophones()
    {
        // Phase A5: wrong-word homophone substitutions are pruned — Phase 7 is strictly
        // orthographic misspellings, so these must no longer resolve.
        foreach (var word in new[] { "accept", "except", "affect", "effect",
                                     "principal", "stationary", "compliment",
                                     "discrete", "ensure", "weather", "whether" })
        {
            MisspellingDictionary.TryGet(word, out _).Should().BeFalse(
                $"'{word}' is a wrong-word homophone, not an orthographic misspelling, and must not be in the dictionary");
        }
    }

    [Fact]
    public void MisspellingDictionary_RetainsOrthographicMisspellings()
    {
        // Sanity check that the A5 cleanup kept the core orthographic entries intact.
        MisspellingDictionary.TryGet("receive",    out var r).Should().BeTrue();   r.Should().Be("recieve");
        MisspellingDictionary.TryGet("separate",   out var s).Should().BeTrue();   s.Should().Be("seperate");
        MisspellingDictionary.TryGet("definitely", out var d).Should().BeTrue();   d.Should().Be("definately");
        MisspellingDictionary.TryGet("committee",  out var c).Should().BeTrue();   c.Should().Be("commitee");
        MisspellingDictionary.TryGet("occurrence", out var o).Should().BeTrue();   o.Should().Be("occurence");
    }

    // ----------------------------------------------------------------
    // 7.2  Net-text invariant with misspellings (human backspace mode)
    // ----------------------------------------------------------------

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.3)]
    [InlineData(1.0)]
    public void NetTypedText_WithMisspellings_HumanCorrection_AlwaysEqualsInput(double misspellingRate)
    {
        // Text containing several words in the misspelling dictionary so the dictionary
        // can be hit at a realistic rate.
        const string input =
            "I believe you should receive the committee report and separate the " +
            "necessary documents because they definitely contain important information.";

        for (var seed = 0; seed < 30; seed++)
        {
            var profile = new TypingProfile
            {
                Wpm             = 65,
                Jitter          = 0.35,
                TypoRate        = 0.0,   // disable mechanical typos to isolate cognitive errors
                MisspellingRate = misspellingRate,
                AutocorrectEnabled = false,
                Fatigue  = false,
                WarmUp   = false,
                Pace     = false,
            };

            var actions = new HumanTypingEngine(new Random(seed)).Plan(input, profile, Layout);
            Reconstruct(actions)
                .Should().Be(input,
                    $"net-text invariant must hold with misspellingRate={misspellingRate}, seed={seed}");
        }
    }

    // ----------------------------------------------------------------
    // 7.3  Net-text invariant with autocorrect simulation
    // ----------------------------------------------------------------

    [Theory]
    [InlineData(0.3)]
    [InlineData(1.0)]
    public void NetTypedText_WithAutocorrect_AlwaysEqualsInput(double misspellingRate)
    {
        const string input =
            "believe receive separate definitely necessary committee occurrence " +
            "environment accommodate recommend embarrass millennium parallel";

        for (var seed = 0; seed < 30; seed++)
        {
            var profile = new TypingProfile
            {
                Wpm             = 65,
                Jitter          = 0.35,
                TypoRate        = 0.0,
                MisspellingRate = misspellingRate,
                AutocorrectEnabled = true,
                Fatigue  = false,
                WarmUp   = false,
                Pace     = false,
            };

            var actions = new HumanTypingEngine(new Random(seed)).Plan(input, profile, Layout);
            Reconstruct(actions)
                .Should().Be(input,
                    $"net-text invariant must hold with autocorrect and misspellingRate={misspellingRate}, seed={seed}");
        }
    }

    // ----------------------------------------------------------------
    // 7.4  Misspelling actually fires in some seeded runs
    // ----------------------------------------------------------------

    [Fact]
    public void MisspellingSequence_OccursInSomeSeeds()
    {
        // With a high rate and input full of dictionary words, at least one run should
        // contain a misspelling-then-backspace pattern in the action stream.
        const string input =
            "I believe you should receive the committee report and separate the " +
            "necessary documents because they definitely need accommodation and " +
            "recommend the occurrence of embarrassing parallel environment changes.";

        var profile = new TypingProfile
        {
            Wpm             = 65,
            Jitter          = 0.35,
            TypoRate        = 0.0,
            MisspellingRate = 1.0,  // fire every time a dict word is encountered
            AutocorrectEnabled = false,
            Fatigue  = false,
            WarmUp   = false,
            Pace     = false,
        };

        // A "misspelling+correction" sequence in human mode is recognisable because
        // there will be a run of backspaces (≥1) at some point in the stream.
        var foundBS = false;
        for (var seed = 0; seed < 50 && !foundBS; seed++)
        {
            var actions = new HumanTypingEngine(new Random(seed))
                .Plan(input, profile, Layout).ToList();
            if (actions.Any(a => a.Action is KeyAction.Press { Key: VirtualKey.Back }))
                foundBS = true;
        }

        foundBS.Should().BeTrue(
            "at rate=1.0 with many dictionary words, at least one run must contain backspaces " +
            "(misspelling correction)");
    }

    [Fact]
    public void AutocorrectSequence_OccursInSomeSeeds_AsTextAction()
    {
        // With autocorrect enabled, the stream should contain at least one KeyAction.Text
        // that equals the corrected word (the autocorrect bulk-replace).
        const string input =
            "believe receive separate definitely necessary committee " +
            "occurrence environment accommodate recommend embarrass";

        var profile = new TypingProfile
        {
            Wpm             = 65,
            Jitter          = 0.35,
            TypoRate        = 0.0,
            MisspellingRate = 1.0,
            AutocorrectEnabled = true,
            Fatigue  = false,
            WarmUp   = false,
            Pace     = false,
        };

        var foundTextAction = false;
        for (var seed = 0; seed < 50 && !foundTextAction; seed++)
        {
            var actions = new HumanTypingEngine(new Random(seed))
                .Plan(input, profile, Layout).ToList();
            // An autocorrect fires as a KeyAction.Text whose value is the correct word.
            if (actions.Any(a => a.Action is KeyAction.Text txt && txt.Value.Length > 1))
                foundTextAction = true;
        }

        foundTextAction.Should().BeTrue(
            "with AutocorrectEnabled=true and rate=1.0, at least one run must contain " +
            "a KeyAction.Text bulk-replace action (the autocorrect simulation)");
    }

    // ----------------------------------------------------------------
    // 7.5  Autocorrect is distinct from human backspacing
    // ----------------------------------------------------------------

    [Fact]
    public void AutocorrectSequence_HasFasterCorrectionThanHumanBackspace()
    {
        // AutocorrectEnabled=true → first BS is autocorrect delay (50–250 ms).
        // AutocorrectEnabled=false → first BS is reaction delay (150–450 ms).
        // Over many seeds the autocorrect first-BS should average lower.
        const string input = "believe receive separate definitely necessary committee";
        const int seeds = 100;

        double SumFirstBS(bool autocorrect)
        {
            var total = 0.0;
            var count = 0;
            for (var seed = 0; seed < seeds; seed++)
            {
                var profile = new TypingProfile
                {
                    Wpm = 60, Jitter = 0.05, TypoRate = 0.0,
                    MisspellingRate = 1.0,
                    AutocorrectEnabled = autocorrect,
                    Fatigue = false, WarmUp = false, Pace = false,
                };
                var actions = new HumanTypingEngine(new Random(seed))
                    .Plan(input, profile, Layout).ToList();
                var firstBS = actions.FirstOrDefault(a =>
                    a.Action is KeyAction.Press { Key: VirtualKey.Back });
                if (firstBS != default)
                {
                    total += firstBS.Delay.TotalMilliseconds;
                    count++;
                }
            }
            return count > 0 ? total / count : double.MaxValue;
        }

        var avgHuman = SumFirstBS(false);
        var avgAC    = SumFirstBS(true);

        avgAC.Should().BeLessThan(avgHuman,
            "autocorrect correction should start faster (50–250 ms) than human backspacing (150–450 ms)");
    }

    // ----------------------------------------------------------------
    // 7.6  MisspellingRate = 0 does NOT change existing seeded plans
    // ----------------------------------------------------------------

    [Fact]
    public void MisspellingRate_Zero_DoesNotAffectSeededPlan()
    {
        // With MisspellingRate=0, no extra RNG draws happen → identical plan to the
        // profile with no cognitive settings at all.
        const string input = "The quick brown fox jumps over the lazy dog.";

        var profileBase = new TypingProfile
        {
            TypoRate = 0.3, Wpm = 65, Jitter = 0.35,
            Fatigue = false, WarmUp = false, Pace = false,
        };
        var profileWithZeroRate = new TypingProfile
        {
            TypoRate = 0.3, Wpm = 65, Jitter = 0.35,
            MisspellingRate = 0.0,   // explicitly off
            AutocorrectEnabled = false,
            Fatigue = false, WarmUp = false, Pace = false,
        };

        var planBase   = new HumanTypingEngine(new Random(42)).Plan(input, profileBase, Layout).ToList();
        var planZero   = new HumanTypingEngine(new Random(42)).Plan(input, profileWithZeroRate, Layout).ToList();

        planZero.Should().Equal(planBase,
            "MisspellingRate=0 must not inject any extra RNG draws — the plan must be identical");
    }

    // ----------------------------------------------------------------
    // 7.7  Seeded reproducibility with misspellings
    // ----------------------------------------------------------------

    [Fact]
    public void SameSeed_WithMisspellings_StillReproducesIdentically()
    {
        const string input =
            "I believe you should receive the committee and separate the necessary " +
            "documents that definitely need accommodation and environment.";

        var profile = new TypingProfile
        {
            Wpm = 65, Jitter = 0.35, TypoRate = 0.1,
            MisspellingRate = 0.5,
            AutocorrectEnabled = false,
            Fatigue = false, WarmUp = false, Pace = false,
        };

        var first  = new HumanTypingEngine(new Random(77)).Plan(input, profile, Layout).ToList();
        var second = new HumanTypingEngine(new Random(77)).Plan(input, profile, Layout).ToList();

        second.Should().Equal(first,
            "same seed must reproduce the identical action stream even with cognitive misspellings");
    }

    [Fact]
    public void SameSeed_WithAutocorrect_StillReproducesIdentically()
    {
        const string input = "believe receive separate definitely necessary committee occurrence";

        var profile = new TypingProfile
        {
            Wpm = 65, Jitter = 0.35, TypoRate = 0.0,
            MisspellingRate = 0.8,
            AutocorrectEnabled = true,
            Fatigue = false, WarmUp = false, Pace = false,
        };

        var first  = new HumanTypingEngine(new Random(13)).Plan(input, profile, Layout).ToList();
        var second = new HumanTypingEngine(new Random(13)).Plan(input, profile, Layout).ToList();

        second.Should().Equal(first,
            "same seed must reproduce the identical action stream even with autocorrect simulation");
    }

    // ----------------------------------------------------------------
    // 7.8  Combined mechanical + cognitive errors still satisfy net-text
    // ----------------------------------------------------------------

    [Fact]
    public void NetTypedText_CombinedMechanicalAndCognitiveErrors_AlwaysEqualsInput()
    {
        const string input =
            "I believe you should receive the committee report and separate the " +
            "necessary documents because they definitely contain important information.";

        for (var seed = 0; seed < 30; seed++)
        {
            var profile = new TypingProfile
            {
                Wpm             = 65,
                Jitter          = 0.35,
                TypoRate        = 0.15,
                MisspellingRate = 0.4,
                AutocorrectEnabled = false,
                Fatigue  = false,
                WarmUp   = false,
                Pace     = false,
            };

            var actions = new HumanTypingEngine(new Random(seed)).Plan(input, profile, Layout);
            Reconstruct(actions)
                .Should().Be(input,
                    $"net-text invariant must hold with combined mechanical + cognitive errors, seed={seed}");
        }
    }

    // ----------------------------------------------------------------
    // 7.9  ErrorModel helper unit tests
    // ----------------------------------------------------------------

    [Fact]
    public void ShouldApplyMisspelling_AtRateZero_NeverFires()
    {
        var model = new ErrorModel(new Random(1));
        for (var i = 0; i < 1_000; i++)
            model.ShouldApplyMisspelling(0.0).Should().BeFalse("rate=0 must never fire");
    }

    [Fact]
    public void ShouldApplyMisspelling_AtRateOne_AlwaysFires()
    {
        var model = new ErrorModel(new Random(2));
        for (var i = 0; i < 100; i++)
            model.ShouldApplyMisspelling(1.0).Should().BeTrue("rate=1 must always fire");
    }

    [Fact]
    public void AutocorrectDelayMs_IsInExpectedRange()
    {
        var model = new ErrorModel(new Random(3));
        for (var i = 0; i < 1_000; i++)
        {
            var d = model.AutocorrectDelayMs();
            d.Should().BeInRange(50, 250,
                "autocorrect delay must be 50–250 ms (faster than human backspacing)");
        }
    }

    [Fact]
    public void AutocorrectDelayMs_IsDistinctFromReactionDelay()
    {
        // Human reaction: 150–450 ms. Autocorrect: 50–250 ms.
        // The maximum autocorrect delay (250) is less than the minimum human delay (150)… well,
        // actually they overlap (150–250). So we verify average autocorrect < average human.
        var model1 = new ErrorModel(new Random(5));
        var model2 = new ErrorModel(new Random(5));
        const int n = 1_000;

        var avgAC    = Enumerable.Range(0, n).Average(_ => model1.AutocorrectDelayMs());
        var avgHuman = Enumerable.Range(0, n).Average(_ => model2.ReactionDelayMs());

        avgAC.Should().BeLessThan(avgHuman,
            "average autocorrect delay should be lower than average human reaction delay");
    }
}
