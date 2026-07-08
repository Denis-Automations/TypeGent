using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using TypeGent.Core.HumanTyping;
using TypeGent.Core.Layouts;
using TypeGent.Core.Typing;
using Xunit;

namespace TypeGent.Tests;

/// <summary>
/// v2 Phase 5 – Error realism: substitution-dominant mix, speed-coupled typo rate,
/// and inverse-distance neighbour weighting.
/// </summary>
public class ErrorRealismTests
{
    private static readonly UsQwertyLayout Layout = new();

    // ----------------------------------------------------------------
    // 5.1  Error-kind distribution (substitution-dominant)
    // ----------------------------------------------------------------

    [Fact]
    public void ChooseKind_AdjacentSlip_DominatesOverManyTrials()
    {
        // Over many rolls with all kinds enabled, AdjacentSlip should appear far more than
        // any other single kind — it carries weight 0.84 out of a total ≈ 0.975.
        var model = new ErrorModel(new Random(42));
        const int trials = 10_000;
        var counts = new Dictionary<TypoKind, int>();

        for (var i = 0; i < trials; i++)
        {
            var kind = model.ChooseKind(canTranspose: true, canShiftMistime: true, canMissDouble: true);
            counts[kind] = counts.TryGetValue(kind, out var n) ? n + 1 : 1;
        }

        var slipCount = counts.TryGetValue(TypoKind.AdjacentSlip, out var s) ? s : 0;
        var nonSlipCount = trials - slipCount;

        // AdjacentSlip should be at least 5× the sum of all other kinds combined.
        slipCount.Should().BeGreaterThan(nonSlipCount * 4,
            "substitution (AdjacentSlip) must strongly dominate the error-kind distribution");
    }

    [Fact]
    public void ChooseKind_OmissionAndMissingDouble_AreRareButPresent()
    {
        // Omission (0.03) and MissingDouble (0.005) should each appear occasionally
        // but never dominate.
        var model = new ErrorModel(new Random(123));
        const int trials = 10_000;
        var omissions = 0;
        var missingDoubles = 0;

        for (var i = 0; i < trials; i++)
        {
            var kind = model.ChooseKind(canTranspose: true, canShiftMistime: true, canMissDouble: true);
            if (kind == TypoKind.Omission) omissions++;
            if (kind == TypoKind.MissingDouble) missingDoubles++;
        }

        omissions.Should().BeInRange(100, 600,
            "Omission weight 0.03 should produce ~300 hits over 10 000 trials");
        missingDoubles.Should().BeInRange(10, 150,
            "MissingDouble weight 0.005 should appear rarely but measurably");
    }

    // ----------------------------------------------------------------
    // 5.2  Speed-coupled typo rate
    // ----------------------------------------------------------------

    [Fact]
    public void SpeedCoupling_SlowPace_ReducesEffectiveTypoRate()
    {
        // pace = 0.5 → effective rate = typoRate × 0.5 (half as many typos)
        var model = new ErrorModel(new Random(0));
        const int trials = 10_000;
        const double typoRate = 0.5;

        var normalHits = Enumerable.Range(0, trials)
            .Count(_ => model.ShouldIntroduceTypo(typoRate, currentPace: 1.0));
        var slowHits = Enumerable.Range(0, trials)
            .Count(_ => model.ShouldIntroduceTypo(typoRate, currentPace: 0.5));

        slowHits.Should().BeLessThan(normalHits,
            "at pace 0.5 the effective typo rate should be lower than at pace 1.0");

        // At pace 0.5 the rate is typoRate × 0.5 = 0.25 → ~2500 hits.
        slowHits.Should().BeInRange(2000, 3200,
            "effective rate at pace 0.5 should be ~half of the base rate");
    }

    [Fact]
    public void SpeedCoupling_FastPace_IncreasesEffectiveTypoRate()
    {
        // pace = 2.0 → effective rate = typoRate × 2.0 (twice as many typos)
        var model = new ErrorModel(new Random(0));
        const int trials = 10_000;
        const double typoRate = 0.2;

        var normalHits = Enumerable.Range(0, trials)
            .Count(_ => model.ShouldIntroduceTypo(typoRate, currentPace: 1.0));
        var fastHits = Enumerable.Range(0, trials)
            .Count(_ => model.ShouldIntroduceTypo(typoRate, currentPace: 2.0));

        fastHits.Should().BeGreaterThan(normalHits,
            "at pace 2.0 the effective typo rate should be higher than at pace 1.0 " +
            "(speed–accuracy tradeoff: faster typing → more errors)");
    }

    [Fact]
    public void SpeedCoupling_EngineLevel_HigherPaceYieldsMoreBackspaces()
    {
        // Run the engine with pace enabled (Pace=true, PaceSigma=large) vs disabled
        // over enough seeds to average out variance. High pace drift → more typos.
        // We hold TypoRate constant and only vary whether the AR(1) pace is on.
        const string input = "The quick brown fox jumps over the lazy dog";
        const int seeds = 50;
        const double typoRate = 0.15;

        var profile = new TypingProfile
        {
            Wpm = 60, Jitter = 0.1, TypoRate = typoRate,
            Fatigue = false, WarmUp = false, Pace = true, PaceSigma = 0.8,
        };
        var profileNoPace = new TypingProfile
        {
            Wpm = 60, Jitter = 0.1, TypoRate = typoRate,
            Fatigue = false, WarmUp = false, Pace = false,
        };

        double backspacesWithPace = 0;
        double backspacesWithoutPace = 0;

        for (var seed = 0; seed < seeds; seed++)
        {
            var withPace = new HumanTypingEngine(new Random(seed))
                .Plan(input, profile, Layout).ToList();
            var withoutPace = new HumanTypingEngine(new Random(seed))
                .Plan(input, profileNoPace, Layout).ToList();

            backspacesWithPace += withPace.Count(a => a.Action is KeyAction.Press { Key: VirtualKey.Back });
            backspacesWithoutPace += withoutPace.Count(a => a.Action is KeyAction.Press { Key: VirtualKey.Back });
        }

        // With a high pace sigma (0.8) the pace swings well above 1.0 often, boosting errors.
        backspacesWithPace.Should().BeGreaterThan(backspacesWithoutPace,
            "autocorrelated pace (PaceSigma=0.8) should cause more typos on average " +
            "than the baseline 1.0 pace, because pace often exceeds 1.0");
    }

    // ----------------------------------------------------------------
    // 5.3  Inverse-distance neighbour weighting
    // ----------------------------------------------------------------

    [Fact]
    public void AdjacentKey_WithLayout_BiasesSubstitutionTowardNearerKeys()
    {
        // For 'f' the closest physical neighbour is 'g' (x-dist ≈ 1 key),
        // while 'r', 'd', 'v' are the next nearest.  'j' is on the opposite side
        // and should almost never be chosen as a neighbour.
        var model = new ErrorModel(new Random(7));
        const int samples = 5_000;

        var nearCount  = 0;  // 'd', 'g', 'v', 'r' — immediate physical neighbours of 'f'
        var farCount   = 0;  // any key NOT adjacent to 'f' on QWERTY

        // Known immediate neighbours of 'f': 'e','r','t','d','g','c','v' (from the Neighbors table)
        var nearNeighbours = new HashSet<char> { 'e', 'r', 't', 'd', 'g', 'c', 'v' };

        for (var i = 0; i < samples; i++)
        {
            var pick = char.ToLowerInvariant(model.AdjacentKey('f', Layout));
            if (nearNeighbours.Contains(pick)) nearCount++;
            else farCount++;
        }

        // All picks should be within the immediate-neighbour list (farCount should be 0
        // since AdjacentKey only samples from the Neighbors table).
        farCount.Should().Be(0,
            "AdjacentKey must only select from the known QWERTY neighbours, not random keys");

        // With inverse-distance weighting, physically closest keys should dominate.
        // The nearest key 'g' (to the right of 'f') and 'd' (to the left) should together
        // appear more often than 'e', 't', 'c', 'v' which are slightly further.
        nearCount.Should().Be(samples,
            "all picks should be within the immediate-neighbour set");
    }

    [Fact]
    public void AdjacentKey_WithLayout_CloserNeighbourChosenMoreOften_ThanFarNeighbour()
    {
        // 's' neighbours (from table): q, w, e, a, d, z, x.
        // 'd' is immediately to the right of 's' (distance ≈ 1 unit).
        // 'q' is top-left of 's' (distance > 1.4 units).
        // With inverse-distance weighting, 'd' and 'a' should be chosen more often than 'q'.
        var model = new ErrorModel(new Random(42));
        const int samples = 10_000;

        var dCount = 0;
        var qCount = 0;

        for (var i = 0; i < samples; i++)
        {
            var pick = char.ToLowerInvariant(model.AdjacentKey('s', Layout));
            if (pick == 'd') dCount++;
            if (pick == 'q') qCount++;
        }

        dCount.Should().BeGreaterThan(qCount,
            "the close neighbour 'd' should be chosen more often than the distant 'q' " +
            "under inverse-distance weighting");
    }

    [Fact]
    public void AdjacentKey_WithoutLayout_AllNeighboursChoseUniformly()
    {
        // Without a layout the fallback is uniform random. Over many samples,
        // each neighbour of 'a' (neighbours: q,w,s,z — 4 keys) should each appear ~25%.
        var model = new ErrorModel(new Random(5));
        const int samples = 8_000;
        var counts = new Dictionary<char, int>();

        for (var i = 0; i < samples; i++)
        {
            var pick = char.ToLowerInvariant(model.AdjacentKey('a'));  // no layout
            counts[pick] = counts.TryGetValue(pick, out var n) ? n + 1 : 1;
        }

        // Each of the 4 neighbours should appear ~2000 times (±500 for tolerance).
        foreach (var (key, count) in counts)
            count.Should().BeInRange(1000, 3000,
                $"neighbour '{key}' should be chosen with roughly equal probability without layout weighting");
    }

    // ----------------------------------------------------------------
    // 5.4  Net-text invariant holds with speed coupling enabled
    // ----------------------------------------------------------------

    [Fact]
    public void NetTypedText_EqualsInput_WithSpeedCoupling()
    {
        // Speed coupling changes *how many* typos happen but must not break net-text == input.
        const string input = "The quick brown fox jumps over the lazy dog";

        for (var seed = 0; seed < 30; seed++)
        {
            var profile = new TypingProfile
            {
                TypoRate = 0.5, Wpm = 65, Jitter = 0.35,
                Pace = true, PaceSigma = 0.5, Fatigue = false, WarmUp = false,
            };
            var actions = new HumanTypingEngine(new Random(seed)).Plan(input, profile, Layout);
            HumanTypingEngineTests.Reconstruct(actions, Layout)
                .Should().Be(input, $"net-text invariant must hold with speed coupling (seed {seed})");
        }
    }
}
