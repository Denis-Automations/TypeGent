using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentAssertions;
using TypeGent.Core.HumanTyping;
using TypeGent.Core.Layouts;
using TypeGent.Core.Typing;
using Xunit;

namespace TypeGent.Tests;

public class HumanTypingEngineTests
{
    private static readonly UsQwertyLayout Layout = new();

    // Reconstruct the net typed text by "executing" the action stream: backspace pops the last
    // char, every other action appends what it produces. Internal so Phase 4+ tests can reuse it.
    internal static string Reconstruct(IEnumerable<TimedAction> actions, KeyboardLayout layout)
    {
        var reverse = new Dictionary<KeyAction, char>();
        foreach (var ch in layout.SupportedChars)
            reverse[layout.ToAction(ch)] = ch;

        var sb = new StringBuilder();
        foreach (var t in actions)
        {
            switch (t.Action)
            {
                case KeyAction.Press p when p.Key == VirtualKey.Back:
                    if (sb.Length > 0) sb.Length--;
                    break;
                case KeyAction.Text text:
                    sb.Append(text.Value);
                    break;
                default:
                    reverse.TryGetValue(t.Action, out var c).Should().BeTrue(
                        $"every non-backspace action should map back to a character, but {t.Action} did not");
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    // Reconstruct net typed text from a stream that may use ANY action variant, including the
    // Phase 9–11 KeyDown/KeyUp (dwell/rollover) path. Press/Chord/Text and backspaces are
    // handled as in Reconstruct; a KeyDown appends the unshifted character for its VK (the
    // rollover happy path only emits KeyDown for unshifted mappable chars) and a KeyUp produces
    // nothing. Used by the Phase A1 realism-on/off profile tests.
    internal static string ReconstructAll(IEnumerable<TimedAction> actions, KeyboardLayout layout)
    {
        var unshifted = new Dictionary<VirtualKey, char>();
        var shifted = new Dictionary<VirtualKey, char>();
        foreach (var ch in layout.SupportedChars)
        {
            var vk = layout.MapChar(ch);
            if (layout.NeedsShift(ch)) shifted[vk] = ch;
            else unshifted[vk] = ch;
        }

        var sb = new StringBuilder();
        foreach (var t in actions)
        {
            switch (t.Action)
            {
                case KeyAction.Press p when p.Key == VirtualKey.Back:
                    if (sb.Length > 0) sb.Length--;
                    break;
                case KeyAction.Press p:
                    unshifted.TryGetValue(p.Key, out var uc).Should().BeTrue(
                        $"Press of VK {p.Key} should map to an unshifted character");
                    sb.Append(uc);
                    break;
                case KeyAction.Chord ch:
                    shifted.TryGetValue(ch.Base, out var sc).Should().BeTrue(
                        $"Chord base VK {ch.Base} should map to a shifted character");
                    sb.Append(sc);
                    break;
                case KeyAction.Text text:
                    sb.Append(text.Value);
                    break;
                case KeyAction.KeyDown kd:
                    unshifted.TryGetValue(kd.Key, out var dc).Should().BeTrue(
                        $"KeyDown of VK {kd.Key} should map to an unshifted character");
                    sb.Append(dc);
                    break;
                case KeyAction.KeyUp:
                    break;
            }
        }

        return sb.ToString();
    }

    [Fact]
    public void HighTypoRate_ProducesBackspaces()
    {
        var engine = new HumanTypingEngine(new Random(42));
        var profile = new TypingProfile { TypoRate = 0.5, Wpm = 60, Jitter = 0.35 };

        var actions = engine.Plan("Hello, World!", profile, Layout).ToList();

        actions.Count(a => a.Action is KeyAction.Press { Key: VirtualKey.Back })
            .Should().BeGreaterThan(0);
    }

    [Fact]
    public void SameSeed_ReproducesIdenticalPlan()
    {
        var profile = new TypingProfile { TypoRate = 0.3, Wpm = 75, Jitter = 0.4 };

        var first = new HumanTypingEngine(new Random(99)).Plan("The quick brown fox.", profile, Layout).ToList();
        var second = new HumanTypingEngine(new Random(99)).Plan("The quick brown fox.", profile, Layout).ToList();

        second.Should().Equal(first);
    }

    [Fact]
    public void SameSeed_WithPace_ReproducesIdenticalPlan()
    {
        // Pace adds a per-call AR(1) Gaussian draw — this proves the draw order stays stable
        // (docs/v2-invariants.md §1) even with the extra RNG consumer active.
        var profile = new TypingProfile { TypoRate = 0.3, Wpm = 75, Jitter = 0.4, Pace = true };

        var first = new HumanTypingEngine(new Random(99)).Plan("The quick brown fox.", profile, Layout).ToList();
        var second = new HumanTypingEngine(new Random(99)).Plan("The quick brown fox.", profile, Layout).ToList();

        second.Should().Equal(first);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void NetTypedText_AlwaysEqualsInput_RegardlessOfTypoRate(double typoRate)
    {
        const string input = "The quick, brown Fox jumps! Over 13 lazy dogs.";
        // A spread of seeds exercises every error kind across runs.
        for (var seed = 0; seed < 25; seed++)
        {
            var profile = new TypingProfile { TypoRate = typoRate, Wpm = 65, Jitter = 0.35 };
            var actions = new HumanTypingEngine(new Random(seed)).Plan(input, profile, Layout);
            Reconstruct(actions, Layout).Should().Be(input, $"seed {seed} at typoRate {typoRate}");
        }
    }

    [Fact]
    public void ZeroTypoRate_TypesOneActionPerChar_NoBackspaces()
    {
        const string input = "Hello, World!";
        var profile = new TypingProfile { TypoRate = 0.0 };

        var actions = new HumanTypingEngine(new Random(1)).Plan(input, profile, Layout).ToList();

        actions.Should().HaveCount(input.Length);
        actions.Any(a => a.Action is KeyAction.Press { Key: VirtualKey.Back }).Should().BeFalse();
    }

    [Fact]
    public void OutOfLayoutChars_StillRoundTripViaFallback()
    {
        const string input = "café — résumé";
        var profile = new TypingProfile { TypoRate = 0.4, Wpm = 60 };

        var actions = new HumanTypingEngine(new Random(7)).Plan(input, profile, Layout);

        Reconstruct(actions, Layout).Should().Be(input);
    }

    // ── Phase A1: runtime realism-on/off profile (mirrors MainViewModel.RunTypingAsync) ──────
    // The shipping app builds its TypingProfile from the Full-realism toggle. These verify the
    // net-text==input invariant under that exact configuration, and that the toggle actually
    // switches the Phase 9–11 down/up path on and off.

    private static TypingProfile AppRealismProfile(bool fullRealism, double misspellingRate) => new()
    {
        Wpm = 60,
        Jitter = 0.35,
        TypoRate = 0.02,
        Fatigue = true,
        WarmUp = true,
        Pace = true,
        LapseRate = 0.005,
        DwellEnabled = fullRealism,
        RolloverEnabled = fullRealism,
        RolloverProbability = 0.55,
        MisspellingRate = misspellingRate,
        AutocorrectEnabled = false,
        DwellMeanMs = 90.0,
        DwellSigmaMs = 12.0,
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NetTypedText_AppRealismProfile_AlwaysEqualsInput(bool fullRealism)
    {
        // Misspelling-rich paragraph so the cognitive path is exercised alongside the
        // biometric down/up layer under the app's default realism-on profile.
        const string input =
            "I believe you should receive the committee report and separate the " +
            "necessary documents because they definitely contain important information. " +
            "The quick brown fox jumps over 13 lazy dogs!";

        for (var seed = 0; seed < 40; seed++)
        {
            var profile = AppRealismProfile(fullRealism, fullRealism ? 0.02 : 0.0);
            var actions = new HumanTypingEngine(new Random(seed)).Plan(input, profile, Layout);
            ReconstructAll(actions, Layout)
                .Should().Be(input,
                    $"net-text invariant must hold with FullRealism={fullRealism}, seed={seed}");
        }
    }

    [Fact]
    public void RealismOnProfile_EmitsKeyDownKeyUpEvents()
    {
        const string input = "the quick brown fox jumps over the lazy dog";
        var actions = new HumanTypingEngine(new Random(42)).Plan(input, AppRealismProfile(true, 0.02), Layout).ToList();

        actions.Any(a => a.Action is KeyAction.KeyDown).Should().BeTrue(
            "Full realism on must activate the Phase 9–11 down/up path");
        actions.Any(a => a.Action is KeyAction.KeyUp).Should().BeTrue();
    }

    [Fact]
    public void RealismOffProfile_UsesAtomicPath_NoKeyDownKeyUp()
    {
        const string input = "the quick brown fox jumps over the lazy dog";
        var actions = new HumanTypingEngine(new Random(42)).Plan(input, AppRealismProfile(false, 0.0), Layout).ToList();

        actions.Any(a => a.Action is KeyAction.KeyDown or KeyAction.KeyUp).Should().BeFalse(
            "Full realism off must use the Phase 1–8 atomic path with no down/up events");
    }

    [Fact]
    public void RealismOnProfile_WithMisspellings_NetTextStillEqualsInput()
    {
        // Bump the misspelling rate to guarantee the cognitive correction path fires while
        // dwell/rollover is active — the retype path emits Press/Chord (HoldMs) mixed with the
        // KeyDown/KeyUp happy path, and the combination must still net-correct.
        const string input =
            "I believe you should receive the committee report and separate the " +
            "necessary documents because they definitely need accommodation.";
        var profile = AppRealismProfile(true, 1.0);

        var anyBackspace = false;
        for (var seed = 0; seed < 50; seed++)
        {
            var actions = new HumanTypingEngine(new Random(seed)).Plan(input, profile, Layout).ToList();
            ReconstructAll(actions, Layout)
                .Should().Be(input, $"combined dwell+rollover+misspelling net-text, seed={seed}");
            if (actions.Any(a => a.Action is KeyAction.Press { Key: VirtualKey.Back }))
                anyBackspace = true;
        }

        anyBackspace.Should().BeTrue(
            "at MisspellingRate=1.0 with many dictionary words, at least one run must correct a misspelling");
    }

    // ── Newline / paragraph-break handling ──────────────────────────────────────────────────
    // '\n' (and the normalized '\r\n' / '\r' forms) must emit a real Enter keypress
    // (KeyAction.Press(VirtualKey.Enter)), never a Unicode VK_PACKET Text fallback. Without this,
    // target apps ignore the newline and run paragraphs together into one continuous line.

    private static int CountEnterPresses(List<TimedAction> actions) =>
        actions.Count(a => a.Action is KeyAction.Press { Key: VirtualKey.Enter });

    private static bool HasUnicodeNewlineFallback(List<TimedAction> actions) =>
        actions.Any(a => a.Action is KeyAction.Text t && (t.Value.Contains('\n') || t.Value.Contains('\r')));

    [Fact]
    public void Newline_Lf_ProducesExactlyOneEnterKey()
    {
        const string input = "a\nb";
        var actions = new HumanTypingEngine(new Random(1)).Plan(input, AppRealismProfile(false, 0.0), Layout).ToList();

        CountEnterPresses(actions).Should().Be(1);
        HasUnicodeNewlineFallback(actions).Should().BeFalse("the newline must be a real Enter, not a VK_PACKET char");
    }

    [Theory]
    [InlineData("a\r\nb")]   // Windows CRLF
    [InlineData("a\nb")]     // Unix LF
    [InlineData("a\rb")]     // legacy lone CR
    public void Newline_AllLineEndings_ProduceExactlyOneEnterKey(string input)
    {
        var actions = new HumanTypingEngine(new Random(1)).Plan(input, AppRealismProfile(false, 0.0), Layout).ToList();

        CountEnterPresses(actions).Should().Be(1, $"the {input.Replace("\r", "\\r").Replace("\n", "\\n")} form must produce a single Enter");
        HasUnicodeNewlineFallback(actions).Should().BeFalse();
    }

    [Theory]
    [InlineData("a\n\nb")]      // LF blank line between paragraphs
    [InlineData("a\r\n\r\nb")]  // CRLF blank line between paragraphs
    public void Newline_BlankLineBetweenParagraphs_ProducesTwoEnterKeys(string input)
    {
        var actions = new HumanTypingEngine(new Random(1)).Plan(input, AppRealismProfile(false, 0.0), Layout).ToList();

        CountEnterPresses(actions).Should().Be(2, "a blank line between paragraphs requires two Enter keypresses");
        HasUnicodeNewlineFallback(actions).Should().BeFalse();
    }

    [Fact]
    public void Newline_FullRealismProfile_ProducesEnterAndFlushesPendingKeyUp()
    {
        // With dwell+rollover active, a newline may interrupt a pending KeyDown/KeyUp pair. The
        // Enter branch must flush the pending KeyUp first, so KeyDown/KeyUp stay balanced and the
        // newline still emits exactly one atomic Enter.
        const string input = "the quick brown fox\njumps over the lazy dog";
        var actions = new HumanTypingEngine(new Random(42)).Plan(input, AppRealismProfile(true, 0.02), Layout).ToList();

        CountEnterPresses(actions).Should().Be(1);
        HasUnicodeNewlineFallback(actions).Should().BeFalse();

        // Every KeyDown must have a matching KeyUp — no key left held across the newline.
        var downs = actions.Count(a => a.Action is KeyAction.KeyDown);
        var ups = actions.Count(a => a.Action is KeyAction.KeyUp);
        downs.Should().Be(ups, "the pending rollover KeyUp must be flushed before the Enter keypress");
    }

    [Fact]
    public void Newline_LeadingAndTrailingNewlines_ProduceEnterKeys()
    {
        const string input = "\nabc\n";
        var actions = new HumanTypingEngine(new Random(1)).Plan(input, AppRealismProfile(false, 0.0), Layout).ToList();

        CountEnterPresses(actions).Should().Be(2, "leading and trailing newlines each produce an Enter");
        HasUnicodeNewlineFallback(actions).Should().BeFalse();
    }

    [Fact]
    public void Newline_NoRngShift_BetweenSeedsReproducesIdenticalPlan()
    {
        // The Enter branch draws from the same RNG stream (SampleDelayMs) as a normal key, so a
        // newline must not perturb the RNG draw order for surrounding characters.
        var profile = AppRealismProfile(false, 0.0);

        var first = new HumanTypingEngine(new Random(99)).Plan("ab\ncd", profile, Layout).ToList();
        var second = new HumanTypingEngine(new Random(99)).Plan("ab\ncd", profile, Layout).ToList();

        second.Should().Equal(first);
    }
}
