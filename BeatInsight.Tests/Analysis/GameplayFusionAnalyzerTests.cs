using BeatInsight.Analysis.Fusion;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Analysis.StreamPatterns;
using BeatInsight.Analysis.TechPatterns;
using BeatInsight.Models;
using BeatInsight.Models.Fusion;

namespace BeatInsight.Tests.Analysis;

public sealed class GameplayFusionAnalyzerTests
{
    [Fact]
    public void AllScoresRemainInZeroToOneHundred()
    {
        GameplayFusionProfile result = Analyze(
            aim: -10, speed: 150, reading: double.NaN, tech: 110, stamina: -1,
            jump: 2, stream: -2, techStructure: double.PositiveInfinity,
            Evidence("Jump", FusionPatternFamily.Jump, 2, 2, 2));

        double[] scores =
        [
            result.JumpStructureScore, result.StreamStructureScore, result.TechStructureScore,
            result.MixedStructureScore, result.AimScore, result.SpeedScore, result.ReadingScore,
            result.TechScore, result.StaminaScore!.Value, result.FinalAim, result.FinalSpeed,
            result.FinalReading, result.FinalTech, result.FinalStamina!.Value
        ];
        Assert.All(scores, value => Assert.InRange(value, 0, 100));
    }

    [Fact]
    public void AllSupportsRemainInZeroToOne()
    {
        GameplayFusionProfile result = Analyze(
            evidence:
            [
                Evidence("Jump", FusionPatternFamily.Jump, 2, -1, 4),
                Evidence("Stream", FusionPatternFamily.Stream, 4, 4, 4, reading: 3, stamina: 3),
                Evidence("Tech", FusionPatternFamily.Tech, 3, 3, 3, reading: 3)
            ]);

        Assert.All(
            new[] { result.JumpPatternSupport, result.StreamPatternSupport, result.TechPatternSupport,
                result.ReadingPatternSupport, result.StaminaPatternSupport, result.Confidence },
            value => Assert.InRange(value, 0, 1));
    }

    [Fact]
    public void NoPatternsLeaveFinalSkillsEqualToBaseSkills()
    {
        GameplayFusionProfile result = Analyze(aim: 30, speed: 40, reading: 50, tech: 60, stamina: 70);

        Assert.Equal(30, result.FinalAim);
        Assert.Equal(40, result.FinalSpeed);
        Assert.Equal(50, result.FinalReading);
        Assert.Equal(60, result.FinalTech);
        Assert.Equal(70, result.FinalStamina);
    }

    [Fact]
    public void MaximumSupportNeverAddsMoreThanTenPercent()
    {
        GameplayFusionPatternEvidence[] evidence = Enumerable.Range(0, 13)
            .Select(index => Evidence($"J{index}", FusionPatternFamily.Jump, 1, 1, 1))
            .Concat(Enumerable.Range(0, 9).Select(index => Evidence($"S{index}", FusionPatternFamily.Stream, 1, 1, 1)))
            .Concat(Enumerable.Range(0, 7).Select(index => Evidence($"T{index}", FusionPatternFamily.Tech, 1, 1, 1)))
            .ToArray();

        GameplayFusionProfile result = Analyze(aim: 50, speed: 50, tech: 50, evidence: evidence);

        Assert.InRange(result.FinalAim, 50, 55.0000001);
        Assert.InRange(result.FinalSpeed, 50, 55.0000001);
        Assert.InRange(result.FinalTech, 50, 55.0000001);
    }

    [Fact]
    public void StrongPatternCannotCreateHighSkillFromWeakBase()
    {
        GameplayFusionProfile result = Analyze(
            aim: 5,
            evidence: [Evidence("Strong Jump", FusionPatternFamily.Jump, 1, 1, 1)]);

        Assert.InRange(result.FinalAim, 5, 5.5);
    }

    [Fact]
    public void JumpPatternsReinforceAimOnly()
    {
        GameplayFusionProfile result = Analyze(
            aim: 50, speed: 50, reading: 50, tech: 50,
            evidence: [Evidence("Jump", FusionPatternFamily.Jump, .9, .9, .9)]);

        Assert.True(result.FinalAim > result.AimScore);
        Assert.Equal(result.SpeedScore, result.FinalSpeed);
        Assert.Equal(result.ReadingScore, result.FinalReading);
        Assert.Equal(result.TechScore, result.FinalTech);
    }

    [Fact]
    public void StreamPatternsReinforceSpeedOnlyByDefault()
    {
        GameplayFusionProfile result = Analyze(
            aim: 50, speed: 50, reading: 50, tech: 50,
            evidence: [Evidence("Speed Stream", FusionPatternFamily.Stream, .9, .9, .9)]);

        Assert.True(result.FinalSpeed > result.SpeedScore);
        Assert.Equal(result.AimScore, result.FinalAim);
        Assert.Equal(result.ReadingScore, result.FinalReading);
        Assert.Equal(result.TechScore, result.FinalTech);
    }

    [Fact]
    public void TechPatternsReinforceTechOnlyByDefault()
    {
        GameplayFusionProfile result = Analyze(
            aim: 50, speed: 50, reading: 50, tech: 50,
            evidence: [Evidence("Overlap", FusionPatternFamily.Tech, .9, .9, .9)]);

        Assert.True(result.FinalTech > result.TechScore);
        Assert.Equal(result.AimScore, result.FinalAim);
        Assert.Equal(result.SpeedScore, result.FinalSpeed);
        Assert.Equal(result.ReadingScore, result.FinalReading);
    }

    [Fact]
    public void ReadingBonusIsTargetedAndCappedAtTwoPercent()
    {
        GameplayFusionProfile generic = Analyze(
            reading: 50,
            evidence: [Evidence("Overlap", FusionPatternFamily.Tech, 1, 1, 1)]);
        GameplayFusionProfile targeted = Analyze(
            reading: 50,
            evidence: [Evidence("Rhythm", FusionPatternFamily.Tech, 1, 1, 1, reading: 1)]);

        Assert.Equal(50, generic.FinalReading);
        Assert.InRange(targeted.FinalReading, 50, 51);
        Assert.True(targeted.FinalReading > generic.FinalReading);
    }

    [Fact]
    public void StaminaIsFusedOnlyWhenBaseScoreExists()
    {
        GameplayFusionPatternEvidence stamina = Evidence(
            "Stamina", FusionPatternFamily.Stream, 1, 1, 1, stamina: 1);

        GameplayFusionProfile available = Analyze(stamina: 50, evidence: [stamina]);
        GameplayFusionProfile unavailable = Analyze(stamina: null, evidence: [stamina]);

        Assert.InRange(available.FinalStamina!.Value, 50, 55.0000001);
        Assert.Null(unavailable.StaminaScore);
        Assert.Null(unavailable.FinalStamina);
    }

    [Fact]
    public void DominantAndSecondaryCanNeverBeSkillNames()
    {
        GameplayFusionProfile result = Analyze(jump: .8, stream: .4, techStructure: .2);

        Assert.Contains(result.DominantStructure, Enum.GetValues<GameplayStructureKind>());
        Assert.True(result.SecondaryStructure is null
            || Enum.GetValues<GameplayStructureKind>().Contains(result.SecondaryStructure.Value));
    }

    [Fact]
    public void HighestSeparatedJumpStructureIsDominant()
    {
        Assert.Equal(GameplayStructureKind.Jump, Analyze(jump: .8, stream: .3, techStructure: .2).DominantStructure);
    }

    [Fact]
    public void HighestSeparatedStreamStructureIsDominant()
    {
        Assert.Equal(GameplayStructureKind.Stream, Analyze(jump: .2, stream: .8, techStructure: .3).DominantStructure);
    }

    [Fact]
    public void HighestSeparatedTechStructureIsDominant()
    {
        Assert.Equal(GameplayStructureKind.Tech, Analyze(jump: .2, stream: .3, techStructure: .8).DominantStructure);
    }

    [Fact]
    public void CloseStructureScoresProduceMixedDiagnostic()
    {
        GameplayFusionProfile result = Analyze(jump: .60, stream: .57, techStructure: .2);

        Assert.Equal(GameplayStructureKind.ClassicMixed, result.DominantStructure);
        Assert.Contains(GameplayFusionDiagnosticFlag.MixedStructure, result.Flags);
    }

    [Fact]
    public void ConfidenceIsAlwaysBounded()
    {
        Assert.InRange(Analyze(jump: 1, stream: -1, techStructure: 5).Confidence, 0, 1);
        Assert.InRange(Analyze().Confidence, 0, 1);
    }

    [Fact]
    public void IncoherenceFlagsAreDescriptiveOnly()
    {
        GameplayFusionProfile result = Analyze(
            aim: 10, speed: 10, reading: 80, tech: 10,
            jump: .8, stream: .8, techStructure: .8,
            evidence:
            [
                Evidence("Tech", FusionPatternFamily.Tech, 1, 1, 1),
                Evidence("Jump", FusionPatternFamily.Jump, 1, 1, 1),
                Evidence("Stream", FusionPatternFamily.Stream, 1, 1, 1)
            ]);

        Assert.Contains(GameplayFusionDiagnosticFlag.HighTechPatternsLowTechSkill, result.Flags);
        Assert.Contains(GameplayFusionDiagnosticFlag.HighStreamStructureLowSpeed, result.Flags);
        Assert.Contains(GameplayFusionDiagnosticFlag.HighJumpStructureLowAim, result.Flags);
        Assert.Contains(GameplayFusionDiagnosticFlag.HighReadingWithoutReadingPatterns, result.Flags);
    }

    [Fact]
    public void StructureAndOtherFamiliesDoNotDoubleCountIntoFinalTech()
    {
        GameplayFusionProfile withoutPatterns = Analyze(tech: 50, techStructure: 1);
        GameplayFusionProfile jumpOnly = Analyze(
            tech: 50,
            techStructure: 1,
            evidence: [Evidence("Jump", FusionPatternFamily.Jump, 1, 1, 1)]);

        Assert.Equal(50, withoutPatterns.FinalTech);
        Assert.Equal(50, jumpOnly.FinalTech);
    }

    [Fact]
    public void AbsentPatternFamilyHasZeroSupport()
    {
        GameplayFusionProfile result = Analyze(
            evidence: [Evidence("Jump", FusionPatternFamily.Jump, .8, .8, .8)]);

        Assert.True(result.JumpPatternSupport > 0);
        Assert.Equal(0, result.StreamPatternSupport);
        Assert.Equal(0, result.TechPatternSupport);
    }

    [Fact]
    public void SeveralMicroSignalsDoNotArtificiallySaturateSupport()
    {
        GameplayFusionPatternEvidence[] micros = Enumerable.Range(0, 7)
            .Select(index => Evidence($"T{index}", FusionPatternFamily.Tech, .01, .01, .01))
            .ToArray();

        Assert.InRange(
            GameplayFusionAnalyzer.CalculatePatternSupport(micros, FusionPatternFamily.Tech),
            0,
            .15);
    }

    [Fact]
    public void OneStrongPatternOutweighsSeveralMicroSignals()
    {
        GameplayFusionPatternEvidence strong = Evidence("Strong", FusionPatternFamily.Tech, .9, .9, .9);
        GameplayFusionPatternEvidence[] micros = Enumerable.Range(0, 7)
            .Select(index => Evidence($"T{index}", FusionPatternFamily.Tech, .05, .05, .05))
            .ToArray();

        double strongSupport = GameplayFusionAnalyzer.CalculatePatternSupport([strong], FusionPatternFamily.Tech);
        double microSupport = GameplayFusionAnalyzer.CalculatePatternSupport(micros, FusionPatternFamily.Tech);
        Assert.True(strongSupport > microSupport);
    }

    [Fact]
    public void RealAdapterDoesNotChangeIdentityOrBeatInsightRating()
    {
        Beatmap map = FixtureLoader.Load("Exit This Earth's Atmosphere [Primordial Nucleosynthesis].osu");
        string primary = map.GameplayProfile.Identity.Primary;
        string secondary = map.GameplayProfile.Identity.Secondary;
        string pattern = map.GameplayProfile.Identity.Pattern;
        double rating = map.BeatInsightRating;

        _ = GameplayFusionAnalyzer.Analyze(map);

        Assert.Equal(primary, map.GameplayProfile.Identity.Primary);
        Assert.Equal(secondary, map.GameplayProfile.Identity.Secondary);
        Assert.Equal(pattern, map.GameplayProfile.Identity.Pattern);
        Assert.Equal(rating, map.BeatInsightRating);
    }

    [Fact]
    public void RealAdapterDoesNotChangeJumpPatterns()
    {
        Beatmap map = FixtureLoader.Load("Vickeblanka - Black Rover (TV Size) (Sotarks) [Extreme].osu");
        string[] before = JumpSnapshot(map);

        _ = GameplayFusionAnalyzer.Analyze(map);

        Assert.Equal(before, JumpSnapshot(map));
    }

    [Fact]
    public void RealAdapterDoesNotChangeStreamPatterns()
    {
        Beatmap map = FixtureLoader.Load("FREEDOM DiVE [FrenZ's Insane].osu");
        string before = StreamSnapshot(map);

        _ = GameplayFusionAnalyzer.Analyze(map);

        Assert.Equal(before, StreamSnapshot(map));
    }

    [Fact]
    public void RealAdapterDoesNotChangeTechPatternsOrTechV2()
    {
        Beatmap map = FixtureLoader.Load("Exit This Earth's Atmosphere [Primordial Nucleosynthesis].osu");
        string before = TechSnapshot(map);
        double techV2 = map.GameplayProfile.TechV2Presence;

        _ = GameplayFusionAnalyzer.Analyze(map);

        Assert.Equal(before, TechSnapshot(map));
        Assert.Equal(techV2, map.GameplayProfile.TechV2Presence);
    }

    private static GameplayFusionProfile Analyze(
        double aim = 40,
        double speed = 40,
        double reading = 40,
        double tech = 40,
        double? stamina = null,
        double jump = .4,
        double stream = .3,
        double techStructure = .2,
        params GameplayFusionPatternEvidence[] evidence) => GameplayFusionAnalyzer.Analyze(new GameplayFusionInput(
            aim,
            speed,
            reading,
            tech,
            stamina,
            jump,
            stream,
            techStructure,
            evidence));

    private static GameplayFusionPatternEvidence Evidence(
        string name,
        FusionPatternFamily family,
        double coverage,
        double confidence,
        double? intensity,
        double reading = 0,
        double stamina = 0) => new(name, family, coverage, confidence, intensity, reading, stamina);

    private static string[] JumpSnapshot(Beatmap map) => JumpPatternAnalyzer
        .Analyze(map.HitObjects, map.GameplayProfile)
        .Select(match => $"{match.Type}:{match.StartObjectIndex}:{match.EndObjectIndex}:{match.Confidence:R}")
        .ToArray();

    private static string StreamSnapshot(Beatmap map)
    {
        IReadOnlyList<HitObject> objects = map.HitObjects;
        return string.Join('|',
            StreamPatternAnalyzer.AnalyzeBurst(objects).Coverage,
            StreamPatternAnalyzer.AnalyzeShortStream(objects).Coverage,
            StreamPatternAnalyzer.AnalyzeLongStream(objects).Coverage,
            StreamPatternAnalyzer.AnalyzeDeathstream(objects).Coverage,
            StreamPatternAnalyzer.AnalyzeStaminaStream(objects).Coverage,
            StreamPatternAnalyzer.AnalyzeIrregularStream(objects).Coverage,
            StreamPatternAnalyzer.AnalyzeSpeedStream(objects).Coverage,
            StreamPatternAnalyzer.AnalyzeFlowStream(objects).Coverage,
            StreamPatternAnalyzer.AnalyzeAlternatingStream(objects).Coverage);
    }

    private static string TechSnapshot(Beatmap map)
    {
        IReadOnlyList<HitObject> objects = map.HitObjects;
        return string.Join('|',
            TechPatternAnalyzer.AnalyzeSliderChain(objects).Coverage,
            TechPatternAnalyzer.AnalyzeComplexSlider(map).Coverage,
            TechPatternAnalyzer.AnalyzeSliderDirectionChange(map).Coverage,
            TechPatternAnalyzer.AnalyzeCircleSliderTransition(objects).Coverage,
            TechPatternAnalyzer.AnalyzeRhythmTransition(objects).Coverage,
            TechPatternAnalyzer.AnalyzeSpatialAwkwardTransition(objects).Coverage,
            TechPatternAnalyzer.AnalyzeOverlap(objects).Coverage);
    }
}
