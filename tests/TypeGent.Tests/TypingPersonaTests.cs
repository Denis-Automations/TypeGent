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
/// v2 Phase 12 – Persona presets: each <see cref="TypingPersona"/> factory returns a coherent,
/// visibly distinct <see cref="TypingProfile"/>, the optional <see cref="ErrorMix"/> skews the
/// error mix without disturbing the seeded draw order, and every persona keeps the net-text
/// invariant and determinism intact.
/// </summary>
public class TypingPersonaTests
{
    private static readonly UsQwertyLayout Layout = new();

    private static readonly string Sample =
        "I believe you should receive the committee report and separate the necessary " +
        "documents because they definitely contain important information about the environment.";

    // ----------------------------------------------------------------
    // 12.1a  ErrorMix default reproduces the measured mix exactly
    // ----------------------------------------------------------------

    [Fact]
    public void ErrorMix_Default_MatchesMeasuredMix()
    {
        var d = ErrorMix.Default;
        d.AdjacentSlip.Should().Be(0.84);
        d.RepeatedKey.Should().Be(0.05);
        d.Omission.Should().Be(0.03);
        d.Transposition.Should().Be(0.015);
        d.ShiftMistime.Should().Be(0.06);
        d.MissingDouble.Should().Be(0.005);
    }

    [Fact]
    public void ErrorMix_Null_ProducesSamePlan_AsDefaultMix()
    {
        // A profile that leaves ErrorMix null must plan identically to one that explicitly sets
        // ErrorMix.Default — the default-parameter path must not perturb the RNG draw order.
        static TypingProfile Make(ErrorMix? mix) => new()
        {
            TypoRate = 0.5, Wpm = 65, Jitter = 0.35,
            Fatigue = false, WarmUp = false, Pace = false,
            ErrorMix = mix,
        };

        for (var seed = 0; seed < 20; seed++)
        {
            var withNull    = new HumanTypingEngine(new Random(seed)).Plan(Sample, Make(null), Layout).ToList();
            var withDefault = new HumanTypingEngine(new Random(seed)).Plan(Sample, Make(ErrorMix.Default), Layout).ToList();

            withDefault.Should().Equal(withNull,
                "ErrorMix=null must be equivalent to ErrorMix.Default (seed {0})", seed);
        }
    }

    // ----------------------------------------------------------------
    // 12.1b  Persona values match their stated character
    // ----------------------------------------------------------------

    [Fact]
    public void HuntAndPeck_MatchesStatedCharacter()
    {
        var p = TypingPersona.HuntAndPeck();
        p.Wpm.Should().Be(25);
        p.TypoRate.Should().BeGreaterThan(0.04);
        p.DwellEnabled.Should().BeTrue();
        p.RolloverEnabled.Should().BeFalse("hunt-and-peck never overlaps keys");
        p.DwellSigmaMs.Should().BeGreaterThan(15, "wide key-hold variance");
        p.LapseRate.Should().BeGreaterThan(0, "frequent attention lapses");
    }

    [Fact]
    public void Average_MatchesStatedCharacter()
    {
        var p = TypingPersona.Average();
        p.Wpm.Should().Be(52);
        p.DwellEnabled.Should().BeTrue();
        p.RolloverEnabled.Should().BeTrue();
        p.RolloverProbability.Should().Be(0.40);
        p.DwellMeanMs.Should().Be(90.0);
        p.AutocorrectEnabled.Should().BeFalse();
        p.ErrorMix.Should().BeNull();
    }

    [Fact]
    public void FastTouchTypist_MatchesStatedCharacter()
    {
        var p = TypingPersona.FastTouchTypist();
        p.Wpm.Should().BeGreaterThanOrEqualTo(100);
        p.TypoRate.Should().BeLessThan(0.02, "fast but accurate");
        p.RolloverEnabled.Should().BeTrue();
        p.RolloverProbability.Should().Be(0.70);
        p.DwellSigmaMs.Should().BeLessThan(10, "tight key-hold variance");
    }

    [Fact]
    public void MobileAutocorrect_MatchesStatedCharacter()
    {
        var p = TypingPersona.MobileAutocorrect();
        p.AutocorrectEnabled.Should().BeTrue("the defining feature of the mobile persona");
        p.RolloverEnabled.Should().BeFalse();
        p.ErrorMix.Should().NotBeNull("mobile persona skews the error mix");
        p.ErrorMix!.Omission.Should().BeGreaterThan(ErrorMix.Default.Omission,
            "mobile errors lean toward omissions / missed taps");
    }

    // ----------------------------------------------------------------
    // 12.1c  Personas are visibly distinct
    // ----------------------------------------------------------------

    [Fact]
    public void Personas_AreVisiblyDistinct()
    {
        var all = new[] { TypingPersona.HuntAndPeck(), TypingPersona.Average(),
                          TypingPersona.FastTouchTypist(), TypingPersona.MobileAutocorrect() };

        // Speeds are all different.
        all.Select(p => p.Wpm).Distinct().Count().Should().Be(4, "each persona types at a distinct speed");

        // Rollover splits them: hunt-and-peck & mobile off; average & fast on.
        TypingPersona.HuntAndPeck().RolloverEnabled.Should().BeFalse();
        TypingPersona.MobileAutocorrect().RolloverEnabled.Should().BeFalse();
        TypingPersona.Average().RolloverEnabled.Should().BeTrue();
        TypingPersona.FastTouchTypist().RolloverEnabled.Should().BeTrue();
        TypingPersona.FastTouchTypist().RolloverProbability
            .Should().BeGreaterThan(TypingPersona.Average().RolloverProbability);

        // Only the mobile persona autocorrects.
        all.Count(p => p.AutocorrectEnabled).Should().Be(1);

        // Only the mobile persona customises the error mix.
        all.Count(p => p.ErrorMix is not null).Should().Be(1);
    }

    // ----------------------------------------------------------------
    // 12.1d  Net-text invariant holds for every persona
    // ----------------------------------------------------------------

    public static IEnumerable<object[]> AllPersonas => new[]
    {
        new object[] { "HuntAndPeck", TypingPersona.HuntAndPeck() },
        new object[] { "Average", TypingPersona.Average() },
        new object[] { "FastTouchTypist", TypingPersona.FastTouchTypist() },
        new object[] { "MobileAutocorrect", TypingPersona.MobileAutocorrect() },
    };

    [Theory]
    [MemberData(nameof(AllPersonas))]
    public void Persona_NetTypedText_AlwaysEqualsInput(string name, TypingProfile persona)
    {
        for (var seed = 0; seed < 25; seed++)
        {
            var actions = new HumanTypingEngine(new Random(seed)).Plan(Sample, persona, Layout);
            HumanTypingEngineTests.ReconstructAll(actions, Layout)
                .Should().Be(Sample, $"net-text invariant must hold for the {name} persona (seed {seed})");
        }
    }

    [Theory]
    [MemberData(nameof(AllPersonas))]
    public void Persona_SeededPlan_IsDeterministic(string name, TypingProfile persona)
    {
        var first  = new HumanTypingEngine(new Random(2024)).Plan(Sample, persona, Layout).ToList();
        var second = new HumanTypingEngine(new Random(2024)).Plan(Sample, persona, Layout).ToList();

        second.Should().Equal(first, "same seed must reproduce the identical plan for the {0} persona", name);
    }

    // ----------------------------------------------------------------
    // 12.1e  Mobile error mix actually produces more omissions
    // ----------------------------------------------------------------

    [Fact]
    public void MobileErrorMix_ProducesMoreOmissionsThanDefault()
    {
        var mobile = TypingPersona.MobileAutocorrect().ErrorMix!;
        const int trials = 20_000;

        int CountOmissions(ErrorMix mix)
        {
            var model = new ErrorModel(new Random(7));
            var n = 0;
            for (var i = 0; i < trials; i++)
                if (model.ChooseKind(canTranspose: true, canShiftMistime: true, canMissDouble: true, mix)
                    == TypoKind.Omission)
                    n++;
            return n;
        }

        var mobileOmissions = CountOmissions(mobile);
        var defaultOmissions = CountOmissions(ErrorMix.Default);

        mobileOmissions.Should().BeGreaterThan(defaultOmissions * 3,
            "the mobile mix (Omission weight 0.18) must produce far more omissions than the default (0.03)");
    }
}
