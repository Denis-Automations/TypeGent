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

        var delays = new DelayModel(_rng, profile.Jitter);
        var errors = new ErrorModel(_rng);
        var baseDelay = profile.BaseDelayMs;
        var backspace = new KeyAction.Press(VirtualKey.Back);

        var prev = '\0';
        var typed = 0;
        var i = 0;

        TypingContext Ctx(char c, char previous, int typedSoFar) => new()
        {
            CurrentChar = c,
            PreviousChar = previous,
            CharsTypedSoFar = typedSoFar,
            NeedsShift = layout.CanMap(c) && layout.NeedsShift(c),
            Fatigue = profile.Fatigue,
        };

        TimedAction Key(char c, double delayMs) =>
            new(TimeSpan.FromMilliseconds(delayMs), layout.ToAction(c));
        TimedAction Back(double delayMs) =>
            new(TimeSpan.FromMilliseconds(delayMs), backspace);

        while (i < text.Length)
        {
            var c = text[i];
            var ctx = Ctx(c, prev, typed);
            var eligible = char.IsLetter(c) && layout.CanMap(c);

            if (eligible && errors.ShouldIntroduceTypo(profile.TypoRate))
            {
                var canTranspose = i + 1 < text.Length
                    && char.IsLetter(text[i + 1]) && layout.CanMap(text[i + 1]);
                var canShiftMistime = char.IsUpper(prev) && char.IsLower(c);

                switch (errors.ChooseKind(canTranspose, canShiftMistime))
                {
                    case TypoKind.AdjacentSlip:
                    {
                        var wrong = errors.AdjacentKey(c);
                        yield return Key(wrong, delays.SampleDelayMs(baseDelay, ctx));
                        yield return Back(errors.ReactionDelayMs());
                        yield return Key(c, delays.SampleDelayMs(baseDelay, ctx) * 0.8);
                        prev = c; typed++; i++;
                        break;
                    }

                    case TypoKind.ShiftMistime:
                    {
                        // Capitalize the letter that should have been lower-case, then fix it.
                        yield return Key(char.ToUpperInvariant(c), delays.SampleDelayMs(baseDelay, ctx));
                        yield return Back(errors.ReactionDelayMs());
                        yield return Key(c, delays.SampleDelayMs(baseDelay, ctx) * 0.8);
                        prev = c; typed++; i++;
                        break;
                    }

                    case TypoKind.Transposition:
                    {
                        var c2 = text[i + 1];
                        // Type the pair in the wrong order...
                        yield return Key(c2, delays.SampleDelayMs(baseDelay, ctx));
                        yield return Key(c, delays.SampleDelayMs(baseDelay, Ctx(c, c2, typed)));
                        // ...notice, delete both...
                        yield return Back(errors.ReactionDelayMs());
                        yield return Back(delays.SampleDelayMs(baseDelay, ctx) * 0.4);
                        // ...and retype correctly.
                        yield return Key(c, delays.SampleDelayMs(baseDelay, ctx) * 0.8);
                        yield return Key(c2, delays.SampleDelayMs(baseDelay, Ctx(c2, c, typed + 1)) * 0.8);
                        prev = c2; typed += 2; i += 2;
                        break;
                    }

                    case TypoKind.RepeatedKey:
                    {
                        yield return Key(c, delays.SampleDelayMs(baseDelay, ctx));
                        var extra = errors.ExtraRepeats();
                        for (var k = 0; k < extra; k++)
                            yield return Key(c, delays.SampleDelayMs(baseDelay, ctx) * 0.5);
                        for (var k = 0; k < extra; k++)
                            yield return Back(k == 0 ? errors.ReactionDelayMs() : delays.SampleDelayMs(baseDelay, ctx) * 0.4);
                        prev = c; typed++; i++;
                        break;
                    }
                }
            }
            else
            {
                yield return Key(c, delays.SampleDelayMs(baseDelay, ctx));
                prev = c; typed++; i++;
            }
        }
    }
}
