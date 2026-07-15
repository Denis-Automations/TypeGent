using TypeGent.Core.Layouts;
using TypeGent.Core.Typing;

namespace TypeGent.Core.HumanTyping;

/// <summary>
/// Plans a human-looking keystroke sequence for a piece of text: realistic per-key timing plus
/// occasional mechanical typos and cognitive misspellings, all net-corrected so the net typed
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
        var dwellEnabled = profile.DwellEnabled;
        var dwellMean = profile.DwellMeanMs;
        var dwellSigma = profile.DwellSigmaMs;

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

        // Build a TimedAction for a regular character keystroke.
        // When DwellEnabled, the action carries a near-Gaussian HoldMs so the orchestrator
        // splits it into KeyDown → wait → KeyUp (Phase 9/10). Text (Unicode fallback) and
        // backspace actions always use HoldMs = null (legacy atomic path).
        TimedAction Key(char c, double delayMs)
        {
            var action = layout.ToAction(c);
            var hold = dwellEnabled && action is not KeyAction.Text
                ? delays.SampleDwellMs(dwellMean, dwellSigma)
                : (double?)null;
            return new TimedAction(TimeSpan.FromMilliseconds(delayMs), action, hold);
        }

        // Backspace: no dwell — mechanical repeat key, always atomic.
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

        // ── v2 Phase 7: cognitive misspelling ────────────────────────────────────────
        // Yields a complete misspelling-then-correction sequence for the word at text[i..i+wordLen).
        // Two correction modes:
        //   AutocorrectEnabled=true  → a fast autocorrect bulk-replace (KeyAction.Text) replaces
        //                              the mistyped word. Distinct from human backspacing.
        //   AutocorrectEnabled=false → human backspace burst erases the misspelled word; the
        //                              correct word is then retyped via the existing key path.
        // In both cases net typed text == input when the caller advances i by wordLen.
        IEnumerable<TimedAction> CognitiveMisspellingSequence(
            string misspelling, string correctWord, TypingContext firstCtx)
        {
            // 1. Type the misspelled form character by character.
            for (var m = 0; m < misspelling.Length; m++)
            {
                var mc = misspelling[m];
                var mCtx = m == 0
                    ? firstCtx
                    : new TypingContext
                    {
                        CurrentChar    = mc,
                        PreviousChar   = misspelling[m - 1],
                        CharsTypedSoFar = i + m,
                        NeedsShift     = layout.CanMap(mc) && layout.NeedsShift(mc),
                        Fatigue        = profile.Fatigue,
                        WarmUp         = profile.WarmUp,
                        Pace           = profile.Pace,
                        Layout         = layout,
                    };
                if (layout.CanMap(mc))
                    yield return Key(mc, delays.SampleDelayMs(baseDelay, mCtx));
                else
                    yield return new TimedAction(
                        TimeSpan.FromMilliseconds(delays.SampleDelayMs(baseDelay, mCtx)),
                        new KeyAction.Text(mc.ToString()));
            }

            // 2. Correction: autocorrect or human backspacing.
            if (profile.AutocorrectEnabled)
            {
                // Autocorrect: short pause then the system bulk-replaces the misspelled word.
                // We model this as fast backspaces × misspelling.Length followed by a single
                // Text action with the correct word — visually distinct from human backspacing.
                var acDelay = errors.AutocorrectDelayMs();
                for (var k = 0; k < misspelling.Length; k++)
                    yield return Back(k == 0 ? acDelay : Math.Max(30.0, acDelay / misspelling.Length));
                // Insert the correct word in one shot (simulates IME/autocorrect text injection).
                yield return new TimedAction(
                    TimeSpan.FromMilliseconds(Math.Max(30.0, acDelay / 2.0)),
                    new KeyAction.Text(correctWord));
            }
            else
            {
                // Human correction: reaction pause → backspace burst → retype correct word.
                for (var k = 0; k < misspelling.Length; k++)
                {
                    var bsDelay = k == 0
                        ? errors.ReactionDelayMs()
                        : Math.Max(60.0, delays.SampleDelayMs(baseDelay, firstCtx) * 0.4);
                    yield return Back(bsDelay);
                }
                // Retype the correct word character by character.
                for (var r = 0; r < correctWord.Length; r++)
                {
                    var rc = correctWord[r];
                    var rCtx = new TypingContext
                    {
                        CurrentChar    = rc,
                        PreviousChar   = r == 0 ? prev : correctWord[r - 1],
                        CharsTypedSoFar = i + r,
                        NeedsShift     = layout.CanMap(rc) && layout.NeedsShift(rc),
                        Fatigue        = profile.Fatigue,
                        WarmUp         = profile.WarmUp,
                        Pace           = profile.Pace,
                        Layout         = layout,
                    };
                    if (layout.CanMap(rc))
                        yield return Key(rc, delays.SampleDelayMs(baseDelay, rCtx));
                    else
                        yield return new TimedAction(
                            TimeSpan.FromMilliseconds(delays.SampleDelayMs(baseDelay, rCtx)),
                            new KeyAction.Text(rc.ToString()));
                }
            }
        }

        while (i < text.Length)
        {
            var c = text[i];
            var ctx = Ctx(c, prev, i);

            // ── v2 Phase 7: cognitive misspelling check (word-boundary, before per-char typos) ──
            // Fire once per word start when the whole word is in the misspelling dictionary and we
            // haven't already handled it (retypingUntil guard). The check consumes one RNG draw;
            // when MisspellingRate==0 no draw is made so existing seeded plans are unaffected.
            var atWordStart = (prev == ' ' || prev == '\0') && char.IsLetter(c);
            if (atWordStart && i >= retypingUntil && profile.MisspellingRate > 0)
            {
                // Extract the full word at the current position.
                var wordEnd = i;
                while (wordEnd < text.Length && char.IsLetter(text[wordEnd])) wordEnd++;
                var wordLen = wordEnd - i;
                var wordText = text.Substring(i, wordLen);

                if (MisspellingDictionary.TryGet(wordText, out var misspelling)
                    && errors.ShouldApplyMisspelling(profile.MisspellingRate))
                {
                    // Preserve the original case pattern: if the first char is uppercase, match it.
                    var correctedMisspelling = char.IsUpper(c)
                        ? char.ToUpperInvariant(misspelling[0]) + misspelling.Substring(1)
                        : misspelling;

                    foreach (var a in CognitiveMisspellingSequence(correctedMisspelling, wordText, ctx))
                        yield return a;

                    // Advance past the entire word — the correct form has been output.
                    prev = wordText[wordLen - 1];
                    i += wordLen;
                    retypingUntil = i; // no further typo injection until here
                    continue;
                }
            }

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
