using TypeGent.Core.Layouts;
using TypeGent.Core.Typing;

namespace TypeGent.Core.HumanTyping;

/// <summary>
/// Plans a human-looking keystroke sequence for a piece of text: realistic per-key timing plus
/// occasional mechanical typos that are <em>always corrected immediately</em>, so the net typed
/// text always equals the input. The single injected <see cref="Random"/> is threaded into both
/// the <see cref="DelayModel"/> and <see cref="ErrorModel"/>, so one seed reproduces an entire plan.
/// </summary>
public sealed class HumanTypingEngine
{
    private readonly Random _rng;

    public HumanTypingEngine(Random rng) => _rng = rng ?? throw new ArgumentNullException(nameof(rng));

    /// <summary>
    /// Lazily yield the planned <see cref="TimedAction"/> stream so the orchestrator can start
    /// typing (and cancel) without materializing the whole sequence.
    /// </summary>
    public IEnumerable<TimedAction> Plan(string text, TypingProfile profile, KeyboardLayout layout)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(layout);

        var delays = new DelayModel(_rng, profile.Jitter, profile.LapseRate, profile.LapseMinMs, profile.LapseMaxMs, profile.PaceSigma);
        var errors = new ErrorModel(_rng);
        var baseDelay = profile.BaseDelayMs;
        var backspace = new KeyAction.Press(VirtualKey.Back);

        var prev = '\0';
        var i = 0;
        var retypingUntil = -1;

        // Scan ahead to get the word starting at 'start' (used for pre-word planning pause).
        static (int Length, bool IsCommon) WordInfo(string t, int start, char previous)
        {
            if (previous != ' ' && previous != '\0') return (0, false);
            var j = start;
            while (j < t.Length && char.IsLetter(t[j])) j++;
            var len = j - start;
            if (len == 0) return (0, false);
            var word = t.Substring(start, len).ToLowerInvariant();
            return (len, DelayModel.IsCommonWord(word));
        }

        TypingContext Ctx(char c, char previous, int textIndex)
        {
            var (wordLen, wordIsCommon) = WordInfo(text, textIndex, previous);
            return new()
            {
                CurrentChar = c,
                PreviousChar = previous,
                CharsTypedSoFar = textIndex,
                NeedsShift = layout.CanMap(c) && layout.NeedsShift(c),
                Fatigue = profile.Fatigue,
                WarmUp = profile.WarmUp,
                Pace = profile.Pace,
                Layout = layout,           // v2 Phase 3: biomechanical timing multiplier
                NextWordLength = wordLen,  // v2 Phase 4: pre-word planning pause
                NextWordIsCommon = wordIsCommon,
            };
        }

        TimedAction Key(char c, double delayMs) =>
            new(TimeSpan.FromMilliseconds(delayMs), layout.ToAction(c));
        TimedAction Back(double delayMs) =>
            new(TimeSpan.FromMilliseconds(delayMs), backspace);

        IEnumerable<TimedAction> DelayedCorrection(int delayChars, int errorTextSpan, int backspaceCount, char tempPrev, TypingContext errCtx)
        {
            for (var k = 0; k < delayChars; k++)
            {
                var textIdx = i + errorTextSpan + k;
                var nextC = text[textIdx];
                var nextCtx = Ctx(nextC, tempPrev, textIdx);
                yield return Key(nextC, delays.SampleDelayMs(baseDelay, nextCtx));
                tempPrev = nextC;
            }
            for (var k = 0; k < backspaceCount; k++)
            {
                // First BS: human reaction pause (150–450 ms).
                // Subsequent BSes: fast burst rhythm, but never below 60 ms so the OS/app
                // event queue doesn't drop events (at 110 WPM the raw ×0.4 can reach ~19 ms).
                var bsDelay = k == 0
                    ? errors.ReactionDelayMs()
                    : Math.Max(60.0, delays.SampleDelayMs(baseDelay, errCtx) * 0.4);
                yield return Back(bsDelay);
            }
        }

        while (i < text.Length)
        {
            var c = text[i];
            var ctx = Ctx(c, prev, i);
            var eligible = i >= retypingUntil && char.IsLetter(c) && layout.CanMap(c);

            if (eligible && errors.ShouldIntroduceTypo(profile.TypoRate, delays.CurrentPace))
            {
                var canTranspose = i + 1 < text.Length && char.IsLetter(text[i + 1]) && layout.CanMap(text[i + 1]);
                var canShiftMistime = char.IsUpper(prev) && char.IsLower(c);
                var canMissDouble = i + 1 < text.Length && char.ToLowerInvariant(c) == char.ToLowerInvariant(text[i + 1]);

                var kind = errors.ChooseKind(canTranspose, canShiftMistime, canMissDouble);
                var delay = errors.DetectionDelayChars();

                switch (kind)
                {
                    case TypoKind.AdjacentSlip:
                    {
                        var wrong = errors.AdjacentKey(c, layout);
                        yield return Key(wrong, delays.SampleDelayMs(baseDelay, ctx));
                        
                        var d = Math.Min(delay, text.Length - 1 - i);
                        foreach (var a in DelayedCorrection(d, 1, d + 1, wrong, ctx)) yield return a;
                        
                        retypingUntil = i + 1 + d;
                        break; // leaves i and prev unchanged so main loop re-types
                    }

                    case TypoKind.ShiftMistime:
                    {
                        var wrong = char.ToUpperInvariant(c);
                        yield return Key(wrong, delays.SampleDelayMs(baseDelay, ctx));
                        
                        var d = Math.Min(delay, text.Length - 1 - i);
                        foreach (var a in DelayedCorrection(d, 1, d + 1, wrong, ctx)) yield return a;
                        
                        retypingUntil = i + 1 + d;
                        break;
                    }

                    case TypoKind.Transposition:
                    {
                        var c2 = text[i + 1];
                        yield return Key(c2, delays.SampleDelayMs(baseDelay, ctx));
                        yield return Key(c, delays.SampleDelayMs(baseDelay, Ctx(c, c2, i + 1)));
                        
                        var d = Math.Min(delay, text.Length - 2 - i);
                        foreach (var a in DelayedCorrection(d, 2, d + 2, c, ctx)) yield return a;
                        
                        retypingUntil = i + 2 + d;
                        break;
                    }

                    case TypoKind.RepeatedKey:
                    {
                        yield return Key(c, delays.SampleDelayMs(baseDelay, ctx));
                        var extra = errors.ExtraRepeats();
                        for (var k = 0; k < extra; k++)
                            yield return Key(c, delays.SampleDelayMs(baseDelay, ctx) * 0.5);
                        
                        var d = Math.Min(delay, text.Length - 1 - i);
                        foreach (var a in DelayedCorrection(d, 1, d + extra, c, ctx)) yield return a;
                        
                        retypingUntil = i + 1 + d;
                        prev = c;
                        i++;
                        break;
                    }

                    case TypoKind.Omission:
                    {
                        var d = Math.Min(delay == 0 ? 1 : delay, text.Length - 1 - i);
                        // We skip 'c' entirely. Wait for 'd' chars, backspace 'd' times.
                        foreach (var a in DelayedCorrection(d, 1, d, prev, ctx)) yield return a;
                        
                        retypingUntil = i + 1 + d;
                        break;
                    }

                    case TypoKind.MissingDouble:
                    {
                        // e.g. 'm' 'm' (at i, i+1). User types 'm', skips the second 'm'.
                        yield return Key(c, delays.SampleDelayMs(baseDelay, ctx));
                        
                        var d = Math.Min(delay == 0 ? 1 : delay, text.Length - 2 - i);
                        foreach (var a in DelayedCorrection(d, 2, d + 1, c, ctx)) yield return a;
                        
                        retypingUntil = i + 2 + d;
                        break;
                    }
                }
            }
            else
            {
                yield return Key(c, delays.SampleDelayMs(baseDelay, ctx));
                prev = c;
                i++;
            }
        }
    }
}
