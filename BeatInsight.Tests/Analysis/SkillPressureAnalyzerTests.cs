using BeatInsight.Analysis.Fusion;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Analysis.SkillPressure;
using BeatInsight.Analysis.StreamPatterns;
using BeatInsight.Analysis.TechPatterns;
using BeatInsight.Models;
using BeatInsight.Models.SkillPressure;

namespace BeatInsight.Tests.Analysis;

public sealed class SkillPressureAnalyzerTests
{
    [Fact]
    public void IsolatedRapidRunDoesNotCreateHighStamina()
    {
        SkillPressureProfile result = Analyze(new()
        {
            ObjectCount = 20, StaminaCoverage = .2, StaminaActiveRatio = .8,
            RapidObjectVolume = .2, LongRapidSupport = .8
        });
        Assert.InRange(result.Stamina.Score!.Value, 0, 35);
    }

    [Fact]
    public void RepeatedRapidRunsWithLowRecoveryRaiseStamina()
    {
        SkillPressureProfile weak = Analyze(new() { ObjectCount = 100, StaminaCoverage = .2, StaminaRepetition = .1 });
        SkillPressureProfile strong = Analyze(new()
        {
            ObjectCount = 100, StaminaCoverage = .8, StaminaRepetition = .9,
            StaminaActiveRatio = .9, StaminaRecoveryPressure = .9, RapidObjectVolume = .9,
            LongRapidSupport = .7
        });
        Assert.True(strong.Stamina.Score > weak.Stamina.Score);
        Assert.True(strong.Stamina.Score >= 75);
    }

    [Fact]
    public void LargeRecoveryReducesStamina()
    {
        SkillPressureInput basis = new()
        {
            ObjectCount = 100, StaminaCoverage = .7, StaminaRepetition = .8,
            StaminaActiveRatio = .8, RapidObjectVolume = .8
        };
        Assert.True(Analyze(basis with { StaminaRecoveryPressure = .9 }).Stamina.Score
            > Analyze(basis with { StaminaRecoveryPressure = .1 }).Stamina.Score);
    }

    [Fact]
    public void EnduranceSeparatesShortDenseFromLongRepeatedPressure()
    {
        SkillPressureProfile shortDense = Analyze(new()
        {
            ObjectCount = 200, DurationSeconds = 45, GlobalPressureCoverage = .9,
            PressureSectionSpread = .3, SustainedPressure = .5
        });
        SkillPressureProfile longRepeated = Analyze(new()
        {
            ObjectCount = 700, DurationSeconds = 300, GlobalPressureCoverage = .75,
            PressureSectionSpread = .9, SustainedPressure = .8
        });
        Assert.True(longRepeated.Endurance.Score > shortDense.Endurance.Score);
    }

    [Fact]
    public void LongWeakMapDoesNotAutomaticallyHaveHighEndurance()
    {
        SkillPressureProfile result = Analyze(new() { ObjectCount = 500, DurationSeconds = 420, GlobalPressureCoverage = .05 });
        Assert.InRange(result.Endurance.Score!.Value, 0, 35);
    }

    [Fact]
    public void StaminaAndEnduranceCanDiverge()
    {
        SkillPressureProfile result = Analyze(new()
        {
            ObjectCount = 80, DurationSeconds = 35, StaminaCoverage = .9, StaminaRepetition = .9,
            StaminaActiveRatio = .9, StaminaRecoveryPressure = .9, RapidObjectVolume = .9,
            GlobalPressureCoverage = .2, PressureSectionSpread = .1
        });
        Assert.True(result.Stamina.Score - result.Endurance.Score > 25);
    }

    [Fact]
    public void RegularTimingHasNoFingerControlPressure()
    {
        Assert.Equal(0, Analyze(new() { ObjectCount = 100 }).FingerControl.Score);
    }

    [Fact]
    public void SustainedIrregularityAndAlternationRaiseFingerControl()
    {
        SkillPressureProfile result = Analyze(new()
        {
            ObjectCount = 100, IrregularStrength = .9, AlternatingStrength = .8,
            RhythmTransitionStrength = .7, TemporalBreadth = 1
        });
        Assert.True(result.FingerControl.Score >= 75);
    }

    [Fact]
    public void SingleIrregularSpikeRemainsLow()
    {
        Assert.InRange(Analyze(new() { ObjectCount = 100, IrregularStrength = .1 }).FingerControl.Score!.Value, 0, 20);
    }

    [Fact]
    public void RepeatedVariedBurstsOutscoreIsolatedBurst()
    {
        SkillPressureProfile isolated = Analyze(new()
        {
            ObjectCount = 100, BurstCoverage = .08, BurstIntensity = .8, BurstLength = .2
        });
        SkillPressureProfile repeated = Analyze(new()
        {
            ObjectCount = 100, BurstCoverage = .6, BurstRepetition = .9,
            BurstIntensity = .8, BurstLength = .7, BurstVariation = .6
        });
        Assert.True(repeated.BurstControl.Score - isolated.BurstControl.Score > 40);
    }

    [Fact]
    public void SpeedAloneDoesNotCreateBurstControl()
    {
        Assert.Equal(0, Analyze(new() { ObjectCount = 100, BaseSpeed = 100 }).BurstControl.Score);
    }

    [Fact]
    public void LongNonBurstSequenceDoesNotCreateBurstControl()
    {
        Assert.Equal(0, Analyze(new() { ObjectCount = 100, LongRapidSupport = 1 }).BurstControl.Score);
    }

    [Fact]
    public void DirectionalPatternsRaiseAimControl()
    {
        SkillPressureProfile straight = Analyze(new() { ObjectCount = 100, BaseAim = 80 });
        SkillPressureProfile controlled = Analyze(new()
        {
            ObjectCount = 100, BaseAim = 60, AimAngleControl = .9,
            JumpControlPatternStrength = .9, SpatialAwkwardStrength = .8,
            SliderDirectionStrength = .7
        });
        Assert.True(controlled.AimControl.Score - straight.AimControl.Score > 50);
    }

    [Fact]
    public void HighAimCanHaveLowAimControl()
    {
        Assert.InRange(Analyze(new() { ObjectCount = 100, BaseAim = 100 }).AimControl.Score!.Value, 0, 20);
    }

    [Fact]
    public void PrecisionIsUnavailableWithoutReliableRelativeGeometry()
    {
        SkillPressureMetric precision = Analyze(new() { ObjectCount = 100, BaseAim = 100 }).Precision;
        Assert.False(precision.Available);
        Assert.Null(precision.Score);
    }

    [Fact]
    public void PrecisionCanBeConsumedWhenFutureReliableGeometryExists()
    {
        SkillPressureMetric precision = Analyze(new()
        {
            ObjectCount = 100, PrecisionGeometryAvailable = true,
            PrecisionGeometryStrength = .8, PrecisionDemandCoverage = .6
        }).Precision;
        Assert.True(precision.Available);
        Assert.InRange(precision.Score!.Value, 60, 80);
    }

    [Fact]
    public void ReadingPressureCombinesVisualLoadAndTargetedPatterns()
    {
        SkillPressureProfile simple = Analyze(new() { ObjectCount = 100, BaseReading = 60 });
        SkillPressureProfile complex = Analyze(new()
        {
            ObjectCount = 100, BaseReading = 60, ReadingDensity = .8,
            ReadingClutter = .8, ReadingAmbiguity = .8, ReadingNovelty = .6,
            ReadingPatternStrength = .8
        });
        Assert.True(complex.ReadingPressure.Score > simple.ReadingPressure.Score);
        Assert.NotEqual(complex.ReadingPressure.Score, complex.ReadingPressure.Signals["ReadContext"] * 100);
    }

    [Fact]
    public void ConstantRhythmHasZeroComplexity()
    {
        Assert.Equal(0, Analyze(new() { ObjectCount = 100 }).RhythmComplexity.Score);
    }

    [Fact]
    public void MultiRegimeTemporalEvidenceRaisesRhythmComplexity()
    {
        SkillPressureProfile result = Analyze(new()
        {
            ObjectCount = 100, RhythmTransitionStrength = .9,
            IrregularStrength = .8, AlternatingStrength = .5, TemporalBreadth = 1
        });
        Assert.True(result.RhythmComplexity.Score >= 75);
    }

    [Fact]
    public void FingerControlAndRhythmComplexityCanDiverge()
    {
        SkillPressureProfile result = Analyze(new()
        {
            ObjectCount = 100, AlternatingStrength = 1, RhythmTransitionStrength = .1
        });
        Assert.NotEqual(result.FingerControl.Score, result.RhythmComplexity.Score);
    }

    [Fact]
    public void ScoresAndConfidencesAreBoundedAndFinite()
    {
        SkillPressureProfile result = Analyze(AllStrong() with
        {
            BaseAim = double.PositiveInfinity, ReadingDensity = double.NaN
        });
        foreach (SkillPressureMetric metric in Metrics(result).Where(metric => metric.Available))
        {
            Assert.InRange(metric.Score!.Value, 0, 100);
            Assert.InRange(metric.Confidence, 0, 1);
            Assert.True(double.IsFinite(metric.Score.Value));
            Assert.True(double.IsFinite(metric.Confidence));
        }
        Assert.InRange(result.Confidence, 0, 1);
    }

    [Fact]
    public void EmptyMapHasNoAvailableSkills()
    {
        SkillPressureProfile result = Analyze(new());
        Assert.Equal(0, result.AvailableSkillCount);
        Assert.All(Metrics(result), metric => Assert.Null(metric.Score));
    }

    [Fact]
    public void VerySmallMapLeavesRhythmUnavailable()
    {
        SkillPressureProfile result = Analyze(new() { ObjectCount = 3 });
        Assert.Null(result.RhythmComplexity.Score);
        Assert.False(result.RhythmComplexity.Available);
    }

    [Fact]
    public void AllStrongSignalsRemainStableAndOrderIndependent()
    {
        SkillPressureInput input = AllStrong();
        Assert.Equal(SkillSnapshot(Analyze(input)), SkillSnapshot(Analyze(input)));
        Assert.All(Metrics(Analyze(input)).Where(metric => metric.Available), metric => Assert.InRange(metric.Score!.Value, 80, 100));
    }

    [Fact]
    public void RealAdapterDoesNotChangeIdentityRatingFusionPatternsOrTechV2()
    {
        Beatmap map = FixtureLoader.Load("Exit This Earth's Atmosphere [Primordial Nucleosynthesis].osu");
        string primary = map.GameplayProfile.Identity.Primary;
        string secondary = map.GameplayProfile.Identity.Secondary;
        double rating = map.BeatInsightRating;
        double techV2 = map.GameplayProfile.TechV2Presence;
        string fusionBefore = FusionSnapshot(GameplayFusionAnalyzer.Analyze(map));
        string patternsBefore = PatternSnapshot(map);

        _ = SkillPressureAnalyzer.Analyze(map);

        Assert.Equal(primary, map.GameplayProfile.Identity.Primary);
        Assert.Equal(secondary, map.GameplayProfile.Identity.Secondary);
        Assert.Equal(rating, map.BeatInsightRating);
        Assert.Equal(techV2, map.GameplayProfile.TechV2Presence);
        Assert.Equal(fusionBefore, FusionSnapshot(GameplayFusionAnalyzer.Analyze(map)));
        Assert.Equal(patternsBefore, PatternSnapshot(map));
    }

    private static SkillPressureProfile Analyze(SkillPressureInput input) => SkillPressureAnalyzer.Analyze(input);

    private static SkillPressureInput AllStrong() => new()
    {
        ObjectCount = 1000, DurationSeconds = 360, BaseAim = 100, BaseSpeed = 100, BaseReading = 100,
        StaminaCoverage = 1, StaminaRepetition = 1, StaminaActiveRatio = 1, StaminaRecoveryPressure = 1,
        RapidObjectVolume = 1, LongRapidSupport = 1, GlobalPressureCoverage = 1, PressureSectionSpread = 1,
        SustainedPressure = 1, IrregularStrength = 1, AlternatingStrength = 1, RhythmTransitionStrength = 1,
        TemporalBreadth = 1, BurstCoverage = 1, BurstRepetition = 1, BurstIntensity = 1, BurstLength = 1,
        BurstVariation = 1, AimAngleControl = 1, JumpControlPatternStrength = 1, SpatialAwkwardStrength = 1,
        SliderDirectionStrength = 1, ReadingDensity = 1, ReadingClutter = 1, ReadingAmbiguity = 1,
        ReadingNovelty = 1, ReadingPatternStrength = 1, PrecisionGeometryAvailable = true,
        PrecisionGeometryStrength = 1, PrecisionDemandCoverage = 1
    };

    private static IEnumerable<SkillPressureMetric> Metrics(SkillPressureProfile profile) =>
    [
        profile.Stamina, profile.Endurance, profile.FingerControl, profile.BurstControl,
        profile.AimControl, profile.Precision, profile.ReadingPressure, profile.RhythmComplexity
    ];

    private static string PatternSnapshot(Beatmap map)
    {
        IReadOnlyList<HitObject> objects = map.HitObjects;
        return string.Join('|',
            JumpPatternAnalyzer.Analyze(objects, map.GameplayProfile).Count,
            StreamPatternAnalyzer.AnalyzeStaminaStream(objects).Coverage,
            StreamPatternAnalyzer.AnalyzeIrregularStream(objects).Coverage,
            TechPatternAnalyzer.AnalyzeRhythmTransition(objects).Coverage,
            TechPatternAnalyzer.AnalyzeSpatialAwkwardTransition(objects).Coverage);
    }

    private static string SkillSnapshot(SkillPressureProfile profile) => string.Join('|',
        Metrics(profile).Select(metric => $"{metric.Score:R}:{metric.Confidence:R}:{metric.Available}:{metric.Reliable}"));

    private static string FusionSnapshot(BeatInsight.Models.Fusion.GameplayFusionProfile profile) => string.Join('|',
        profile.JumpStructureScore, profile.StreamStructureScore, profile.TechStructureScore,
        profile.JumpPatternSupport, profile.StreamPatternSupport, profile.TechPatternSupport,
        profile.FinalAim, profile.FinalSpeed, profile.FinalReading, profile.FinalTech,
        profile.DominantStructure, profile.SecondaryStructure, profile.Confidence,
        string.Join(',', profile.Flags));
}
