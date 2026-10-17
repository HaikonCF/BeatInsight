using BeatInsight.Analysis.Fusion;
using BeatInsight.Analysis.Identity;
using BeatInsight.Analysis.Sections;
using BeatInsight.Analysis.SkillPressure;
using BeatInsight.Models;
using BeatInsight.Models.Fusion;
using BeatInsight.Models.Identity;
using BeatInsight.Models.Sections;
using BeatInsight.Models.SkillPressure;

namespace BeatInsight.Tests.Analysis;

public sealed class GameplayIdentityV3AnalyzerTests
{
    [Fact]
    public void EmptyInputProducesSparseLowConfidenceMixedIdentity()
    {
        GameplayIdentityV3Profile result = Analyze(Input());
        Assert.Equal(GameplayStructureKind.ClassicMixed, result.PrimaryStructure);
        Assert.Null(result.SecondaryStructure);
        Assert.Empty(result.MainSkills);
        Assert.Empty(result.MainPatterns);
        Assert.Contains(GameplayIdentityV3DiagnosticFlag.SparseMap, result.Flags);
        Assert.Contains(GameplayIdentityV3DiagnosticFlag.LowConfidence, result.Flags);
    }

    [Theory]
    [InlineData(85, 10, 5, GameplayStructureKind.Jump)]
    [InlineData(10, 85, 5, GameplayStructureKind.Stream)]
    [InlineData(10, 5, 85, GameplayStructureKind.Tech)]
    public void StrongStructuralSignalSelectsOnlyStructuralPrimary(
        double jump, double stream, double tech, GameplayStructureKind expected)
    {
        GameplayIdentityV3Profile result = Analyze(Input(100, jump, stream, tech, sections: Sections(expected, expected)));
        Assert.Equal(expected, result.PrimaryStructure);
        Assert.DoesNotContain(result.PrimaryStructure.ToString(), new[] { "Aim", "Speed", "Reading", "Stamina" });
    }

    [Fact]
    public void CloseSignificantSignalsProduceClassicMixed()
    {
        GameplayIdentityV3Profile result = Analyze(Input(100, 70, 66, 10,
            sections: Sections(GameplayStructureKind.Jump, GameplayStructureKind.Stream)));
        Assert.Equal(GameplayStructureKind.ClassicMixed, result.PrimaryStructure);
        Assert.Equal(GameplayStructureKind.Jump, result.SecondaryStructure);
    }

    [Theory]
    [InlineData(90, 5, 5, GameplayStructureKind.Jump)]
    [InlineData(5, 90, 5, GameplayStructureKind.Stream)]
    [InlineData(5, 5, 90, GameplayStructureKind.Tech)]
    public void DominantStructureIsNotMadeMixedByManyNeutralSections(
        double jump, double stream, double tech, GameplayStructureKind expected)
    {
        GameplayIdentityV3Profile result = Analyze(Input(100, jump, stream, tech,
            sections: Sections(expected,
                GameplayStructureKind.ClassicMixed, GameplayStructureKind.ClassicMixed,
                GameplayStructureKind.ClassicMixed, GameplayStructureKind.ClassicMixed)));

        Assert.Equal(expected, result.PrimaryStructure);
        Assert.Equal(1, result.SectionContext.RepresentedStructuralFamilyCount);
        Assert.Equal(0, result.SectionContext.RealStructuralDiversity);
    }

    [Fact]
    public void RealJumpStreamSectionDiversityProducesClassicMixed()
    {
        GameplayIdentityV3Profile result = Analyze(Input(100, 70, 66, 5,
            sections: Sections(GameplayStructureKind.Jump, GameplayStructureKind.Stream,
                GameplayStructureKind.ClassicMixed)));

        Assert.Equal(GameplayStructureKind.ClassicMixed, result.PrimaryStructure);
        Assert.Equal(2, result.SectionContext.RepresentedStructuralFamilyCount);
    }

    [Fact]
    public void RealJumpTechSectionDiversityCanProduceClassicMixedDespiteWideScoreMargin()
    {
        GameplayIdentityV3Profile result = Analyze(Input(100, 75, 5, 35,
            sections: Sections(GameplayStructureKind.Jump, GameplayStructureKind.Jump,
                GameplayStructureKind.Tech, GameplayStructureKind.ClassicMixed)));

        Assert.Equal(GameplayStructureKind.ClassicMixed, result.PrimaryStructure);
        Assert.Equal(2, result.SectionContext.RepresentedStructuralFamilyCount);
    }

    [Fact]
    public void MarginAboveTwentyWithOnlyOneRepresentedFamilyIsNotMixed()
    {
        GameplayIdentityV3Profile result = Analyze(Input(100, 90, 10, 5,
            sections: Sections(GameplayStructureKind.Jump,
                GameplayStructureKind.ClassicMixed, GameplayStructureKind.ClassicMixed,
                GameplayStructureKind.ClassicMixed, GameplayStructureKind.ClassicMixed)));

        Assert.True(result.JumpStructureScore - result.StreamStructureScore > 20);
        Assert.Equal(1, result.SectionContext.RepresentedStructuralFamilyCount);
        Assert.Equal(GameplayStructureKind.Jump, result.PrimaryStructure);
    }

    [Fact]
    public void NeutralSectionsAloneDoNotCreateStructuralDiversity()
    {
        GameplayIdentityV3Profile result = Analyze(Input(100, 90, 10, 5,
            sections: Sections(GameplayStructureKind.ClassicMixed, GameplayStructureKind.ClassicMixed)));

        Assert.Equal(0, result.SectionContext.RepresentedStructuralFamilyCount);
        Assert.Equal(0, result.SectionContext.RealStructuralDiversity);
        Assert.Equal(GameplayStructureKind.Jump, result.PrimaryStructure);
    }

    [Fact]
    public void TwoFamiliesAtFifteenPercentAreRepresentedInclusively()
    {
        GameplayStructureKind[] structures =
        [
            GameplayStructureKind.Jump, GameplayStructureKind.Jump, GameplayStructureKind.Jump,
            GameplayStructureKind.Stream, GameplayStructureKind.Stream, GameplayStructureKind.Stream,
            GameplayStructureKind.ClassicMixed, GameplayStructureKind.ClassicMixed,
            GameplayStructureKind.ClassicMixed, GameplayStructureKind.ClassicMixed,
            GameplayStructureKind.ClassicMixed, GameplayStructureKind.ClassicMixed,
            GameplayStructureKind.ClassicMixed, GameplayStructureKind.ClassicMixed,
            GameplayStructureKind.ClassicMixed, GameplayStructureKind.ClassicMixed,
            GameplayStructureKind.ClassicMixed, GameplayStructureKind.ClassicMixed,
            GameplayStructureKind.ClassicMixed, GameplayStructureKind.ClassicMixed
        ];
        GameplayIdentityV3Profile result = Analyze(Input(100, 80, 20, 5, sections: Sections(structures)));

        Assert.Equal(.15, result.SectionContext.JumpSectionRatio, precision: 10);
        Assert.Equal(.15, result.SectionContext.StreamSectionRatio, precision: 10);
        Assert.Equal(2, result.SectionContext.RepresentedStructuralFamilyCount);
        Assert.Equal(GameplayStructureKind.ClassicMixed, result.PrimaryStructure);
    }

    [Fact]
    public void StructureScoresKeepTheirDocumentedFormulaAfterMixedCalibration()
    {
        GameplayIdentityV3Profile result = Analyze(Input(100, 50, 40, 30,
            sections: Sections(GameplayStructureKind.Jump, GameplayStructureKind.ClassicMixed)));

        Assert.Equal(42.5, result.JumpStructureScore, precision: 10);
        Assert.Equal(24, result.StreamStructureScore, precision: 10);
        Assert.Equal(18, result.TechStructureScore, precision: 10);
    }

    [Fact]
    public void JumpWithWeakStreamHasNoSecondary()
    {
        GameplayIdentityV3Profile result = Analyze(Input(100, 90, 20, 5,
            sections: Sections(GameplayStructureKind.Jump, GameplayStructureKind.Jump)));
        Assert.Equal(GameplayStructureKind.Jump, result.PrimaryStructure);
        Assert.Null(result.SecondaryStructure);
    }

    [Fact]
    public void StreamWithSignificantTechSelectsTechSecondary()
    {
        GameplayIdentityV3Profile result = Analyze(Input(100, 5, 90, 75,
            sections: Sections(GameplayStructureKind.Stream, GameplayStructureKind.Stream)));
        Assert.Equal(GameplayStructureKind.Stream, result.PrimaryStructure);
        Assert.Equal(GameplayStructureKind.Tech, result.SecondaryStructure);
    }

    [Fact]
    public void StructureFormulaUsesGlobalSectionsAndPatternsWithDocumentedWeights()
    {
        GameplayIdentityV3PatternInput pattern = new("Jump", FusionPatternFamily.Jump, 1, 1, 1);
        GameplayIdentityV3Profile result = Analyze(Input(100, 50, 0, 0,
            patterns: [pattern], sections: Sections(GameplayStructureKind.Jump)));
        Assert.Equal(69.31818181818181, result.JumpStructureScore, precision: 10);
    }

    [Fact]
    public void MainSkillsAreRankedThresholdedAndLimitedToThree()
    {
        SkillPressureProfile pressure = Pressure(
            stamina: Metric(80), endurance: Metric(75), finger: Metric(70), burst: Metric(65),
            aim: Metric(20), reading: Metric(10), rhythm: Metric(5));
        GameplayIdentityV3Profile result = Analyze(Input(100, 80, 10, 5, pressure));
        Assert.Equal(new[] { "Stamina", "Endurance", "FingerControl" }, result.MainSkills.Select(skill => skill.Name));
    }

    [Fact]
    public void WeakSkillsAreExcluded()
    {
        GameplayIdentityV3Profile result = Analyze(Input(100, 80, 10, 5,
            Pressure(stamina: Metric(29.99), aim: Metric(10))));
        Assert.Empty(result.MainSkills);
    }

    [Fact]
    public void PrecisionUnavailableIsExcluded()
    {
        SkillPressureProfile pressure = Pressure(stamina: Metric(50), precision: SkillPressureMetric.Unavailable("No geometry"));
        GameplayIdentityV3Profile result = Analyze(Input(100, 80, 10, 5, pressure));
        Assert.DoesNotContain(result.MainSkills, skill => skill.Name == "Precision");
    }

    [Fact]
    public void EnduranceAndRhythmKeepPartialStatus()
    {
        GameplayIdentityV3Profile result = Analyze(Input(100, 80, 10, 5,
            Pressure(endurance: Metric(80), rhythm: Metric(70))));
        Assert.All(result.MainSkills, skill => Assert.Equal(GameplayIdentityV3EvidenceStatus.Partial, skill.Status));
    }

    [Fact]
    public void UnreliableStrongSkillIsMarkedPartial()
    {
        GameplayIdentityV3Profile result = Analyze(Input(100, 80, 10, 5,
            Pressure(aim: Metric(80, confidence: .3, reliable: false))));
        Assert.Equal(GameplayIdentityV3EvidenceStatus.Partial, Assert.Single(result.MainSkills).Status);
    }

    [Fact]
    public void PatternsAreRankedAcrossFamiliesAndFamilyIsPreserved()
    {
        GameplayIdentityV3PatternInput[] patterns =
        [
            new("JumpStrong", FusionPatternFamily.Jump, .9, .9, .9),
            new("StreamMedium", FusionPatternFamily.Stream, .6, .6, .6),
            new("TechWeak", FusionPatternFamily.Tech, .1, .1, .1)
        ];
        GameplayIdentityV3Profile result = Analyze(Input(100, 80, 10, 5, patterns: patterns));
        Assert.Equal("JumpStrong", result.MainPatterns[0].Name);
        Assert.Equal(FusionPatternFamily.Jump, result.MainPatterns[0].Family);
        Assert.DoesNotContain(result.MainPatterns, pattern => pattern.Name == "TechWeak");
    }

    [Fact]
    public void MainPatternsAreLimitedToFive()
    {
        GameplayIdentityV3PatternInput[] patterns = Enumerable.Range(0, 8)
            .Select(index => new GameplayIdentityV3PatternInput($"P{index}", FusionPatternFamily.Jump, .8, .8, .8)).ToArray();
        Assert.Equal(5, Analyze(Input(100, 80, 10, 5, patterns: patterns)).MainPatterns.Count);
    }

    [Fact]
    public void AllPatternsPreservesSummariesOutsideMainPatternSelection()
    {
        GameplayIdentityV3Profile result = Analyze(Input(100, 20, 20, 90,
            patterns:
            [
                new("TechStrong", FusionPatternFamily.Tech, 1, 1, 1),
                new("JumpDetected", FusionPatternFamily.Jump, .45, .45, .45),
                new("StreamDetected", FusionPatternFamily.Stream, .30, .30, .30)
            ]));

        Assert.Equal(["TechStrong"], result.MainPatterns.Select(pattern => pattern.Name));
        Assert.Equal(
            ["TechStrong", "JumpDetected", "StreamDetected"],
            result.AllPatterns.Select(pattern => pattern.Name));
    }

    [Fact]
    public void OneTwoRemainsAvailableWhenNotSelectedAsMainPattern()
    {
        GameplayIdentityV3Profile result = Analyze(Input(100, 80, 10, 60,
            patterns:
            [
                new("TechStrong", FusionPatternFamily.Tech, 1, 1, 1),
                new("OneTwo", FusionPatternFamily.Jump, .20, .20)
            ]));

        Assert.DoesNotContain(result.MainPatterns, pattern => pattern.Name == "OneTwo");
        GameplayIdentityV3Pattern oneTwo = Assert.Single(
            result.AllPatterns, pattern => pattern.Name == "OneTwo");
        Assert.Equal(FusionPatternFamily.Jump, oneTwo.Family);
        Assert.True(oneTwo.Coverage > 0);
    }

    [Fact]
    public void NoSignificantPatternProducesEmptySelection()
    {
        GameplayIdentityV3Profile result = Analyze(Input(100, 80, 10, 5,
            patterns: [new("Weak", FusionPatternFamily.Jump, .399, .399, .399)]));

        Assert.Empty(result.MainPatterns);
    }

    [Fact]
    public void OneStrongPatternProducesOneSelection()
    {
        GameplayIdentityV3Profile result = Analyze(Input(100, 80, 10, 5,
            patterns: [new("Strong", FusionPatternFamily.Jump, .8, .8, .8)]));

        Assert.Equal("Strong", Assert.Single(result.MainPatterns).Name);
    }

    [Fact]
    public void TwoStrongPatternsProduceTwoSelections()
    {
        GameplayIdentityV3Profile result = Analyze(Input(100, 80, 10, 5,
            patterns:
            [
                new("First", FusionPatternFamily.Jump, .9, .9, .9),
                new("Second", FusionPatternFamily.Stream, .7, .7, .7)
            ]));

        Assert.Equal(2, result.MainPatterns.Count);
    }

    [Fact]
    public void SeveralStrongPatternsAreRetainedUpToFive()
    {
        GameplayIdentityV3PatternInput[] patterns = Enumerable.Range(0, 5)
            .Select(index => new GameplayIdentityV3PatternInput($"P{index}", FusionPatternFamily.Jump, .8 - index * .02, .8 - index * .02, .8 - index * .02))
            .ToArray();

        Assert.Equal(5, Analyze(Input(100, 80, 10, 5, patterns: patterns)).MainPatterns.Count);
    }

    [Fact]
    public void AbsolutePatternCutoffExcludesStrengthBelowFortyPercent()
    {
        GameplayIdentityV3Profile result = Analyze(Input(100, 80, 10, 5,
            patterns:
            [
                new("Kept", FusionPatternFamily.Jump, .8, .8, .8),
                new("Removed", FusionPatternFamily.Stream, .399, .399, .399)
            ]));

        Assert.DoesNotContain(result.MainPatterns, pattern => pattern.Name == "Removed");
    }

    [Fact]
    public void RelativePatternCutoffExcludesPatternBelowFiftyFivePercentOfStrongest()
    {
        GameplayIdentityV3Profile result = Analyze(Input(100, 80, 10, 5,
            patterns:
            [
                new("Strongest", FusionPatternFamily.Jump, .9, .9, .9),
                new("AbsoluteOnly", FusionPatternFamily.Stream, .49, .49, .49)
            ]));

        Assert.Equal("Strongest", Assert.Single(result.MainPatterns).Name);
    }

    [Fact]
    public void TopPatternAboveAbsoluteMinimumIsAlwaysRetained()
    {
        GameplayIdentityV3Profile result = Analyze(Input(100, 80, 10, 5,
            patterns: [new("Top", FusionPatternFamily.Tech, .4, .4, .4)]));

        Assert.Equal("Top", Assert.Single(result.MainPatterns).Name);
    }

    [Fact]
    public void PatternRankingIsUnchangedBySelectionCutoff()
    {
        GameplayIdentityV3Profile result = Analyze(Input(100, 80, 10, 5,
            patterns:
            [
                new("Third", FusionPatternFamily.Tech, .6, .6, .6),
                new("First", FusionPatternFamily.Jump, .8, .8, .8),
                new("Second", FusionPatternFamily.Stream, .7, .7, .7)
            ]));

        Assert.Equal(new[] { "First", "Second", "Third" }, result.MainPatterns.Select(pattern => pattern.Name));
    }

    [Fact]
    public void PatternStrengthFormulaAndFamilyAreUnchanged()
    {
        GameplayIdentityV3Profile result = Analyze(Input(100, 80, 10, 5,
            patterns: [new("Weighted", FusionPatternFamily.Tech, .8, .6, .4)]));
        GameplayIdentityV3Pattern pattern = Assert.Single(result.MainPatterns);

        Assert.Equal(66, pattern.Score, precision: 10);
        Assert.Equal(FusionPatternFamily.Tech, pattern.Family);
    }

    [Fact]
    public void MissingIntensityRenormalizesAvailablePatternWeights()
    {
        GameplayIdentityV3Profile result = Analyze(Input(100, 80, 10, 5,
            patterns: [new("NoIntensity", FusionPatternFamily.Jump, .8, .8)]));
        Assert.Equal(80, Assert.Single(result.MainPatterns).Score, precision: 10);
    }

    [Fact]
    public void SectionRatiosAndTimelineCompressionAreExact()
    {
        SectionAnalysisResult sections = Sections(
            GameplayStructureKind.Jump, GameplayStructureKind.Jump,
            GameplayStructureKind.Stream, GameplayStructureKind.Stream,
            GameplayStructureKind.Tech);
        GameplayIdentityV3SectionContext context = Analyze(Input(100, 80, 10, 5, sections: sections)).SectionContext;
        Assert.Equal(.4, context.JumpSectionRatio, precision: 10);
        Assert.Equal(.4, context.StreamSectionRatio, precision: 10);
        Assert.Equal(.2, context.TechSectionRatio, precision: 10);
        Assert.Equal(3, context.RepresentedStructuralFamilyCount);
        Assert.Equal(.6, context.RealStructuralDiversity, precision: 10);
        Assert.Equal(new[] { GameplayStructureKind.Jump, GameplayStructureKind.Stream, GameplayStructureKind.Tech }, context.CompressedStructureTimeline);
    }

    [Fact]
    public void SectionTransitionAndSpikeCountsArePreserved()
    {
        SectionAnalysisResult sections = SectionsWithDiagnostics();
        GameplayIdentityV3SectionContext context = Analyze(Input(100, 80, 10, 5, sections: sections)).SectionContext;
        Assert.Equal(1, context.GameplayTransitionCount);
        Assert.Equal(1, context.DifficultySpikeCount);
    }

    [Fact]
    public void AllScoresAndConfidencesRemainBoundedAndFinite()
    {
        GameplayIdentityV3Profile result = Analyze(Input(100, double.PositiveInfinity, double.NaN, -100,
            Pressure(stamina: Metric(200), aim: Metric(90)),
            [new("Bad", FusionPatternFamily.Tech, double.NaN, double.PositiveInfinity, -10)],
            Sections(GameplayStructureKind.Jump)));
        foreach (double value in new[]
                 {
                     result.JumpStructureScore, result.StreamStructureScore, result.TechStructureScore,
                     result.ClassicMixedScore, result.StructureConfidence, result.SkillConfidence,
                     result.PatternConfidence, result.GlobalIdentityConfidence
                 })
        {
            Assert.True(double.IsFinite(value));
        }
        Assert.InRange(result.GlobalIdentityConfidence, 0, 1);
    }

    [Fact]
    public void StrongDominanceHasHigherStructureConfidenceThanContradiction()
    {
        GameplayIdentityV3Profile aligned = Analyze(Input(200, 90, 5, 5,
            sections: Sections(GameplayStructureKind.Jump, GameplayStructureKind.Jump)));
        GameplayIdentityV3Profile contradicted = Analyze(Input(200, 90, 5, 5,
            sections: Sections(GameplayStructureKind.Stream, GameplayStructureKind.Stream)));
        Assert.True(aligned.StructureConfidence > contradicted.StructureConfidence);
    }

    [Fact]
    public void StrongSecondaryAndMixedDiagnosticsAreDescriptive()
    {
        GameplayIdentityV3Profile result = Analyze(Input(200, 75, 70, 5,
            sections: Sections(GameplayStructureKind.Jump, GameplayStructureKind.Stream, GameplayStructureKind.ClassicMixed)));
        Assert.Equal(GameplayStructureKind.ClassicMixed, result.PrimaryStructure);
        Assert.Contains(GameplayIdentityV3DiagnosticFlag.HighlyMixedStructure, result.Flags);
    }

    [Fact]
    public void PatternMismatchFlagDoesNotChangePrimary()
    {
        GameplayIdentityV3Profile result = Analyze(Input(200, 90, 5, 5,
            patterns: [new("TechStrong", FusionPatternFamily.Tech, 1, 1, 1)],
            sections: Sections(GameplayStructureKind.Jump, GameplayStructureKind.Jump)));
        Assert.Equal(GameplayStructureKind.Jump, result.PrimaryStructure);
        Assert.Contains(GameplayIdentityV3DiagnosticFlag.PatternStructureMismatch, result.Flags);
    }

    [Fact]
    public void SkillMismatchFlagDoesNotChangePrimary()
    {
        GameplayIdentityV3Profile result = Analyze(Input(200, 90, 5, 5,
            Pressure(stamina: Metric(90)), sections: Sections(GameplayStructureKind.Jump)));
        Assert.Equal(GameplayStructureKind.Jump, result.PrimaryStructure);
        Assert.Contains(GameplayIdentityV3DiagnosticFlag.SkillStructureMismatch, result.Flags);
    }

    [Fact]
    public void AnalysisIsDeterministic()
    {
        GameplayIdentityV3Input input = Input(100, 80, 30, 10,
            Pressure(stamina: Metric(70), aim: Metric(60)),
            [new("Jump", FusionPatternFamily.Jump, .7, .8, .6)],
            Sections(GameplayStructureKind.Jump, GameplayStructureKind.Jump));
        Assert.Equal(Snapshot(Analyze(input)), Snapshot(Analyze(input)));
    }

    [Fact]
    public void RealAdapterDoesNotMutateHistoricalIdentityRatingFusionOrSectionAnalysis()
    {
        Beatmap map = FixtureLoader.Load("Vickeblanka - Black Rover (TV Size) (Sotarks) [Extreme].osu");
        string primary = map.GameplayProfile.Identity.Primary;
        string secondary = map.GameplayProfile.Identity.Secondary;
        double rating = map.BeatInsightRating;
        string fusion = FusionSnapshot(GameplayFusionAnalyzer.Analyze(map));
        string sections = SectionSnapshot(SectionAnalysisV2Analyzer.Analyze(map));
        string pressure = PressureSnapshot(SkillPressureAnalyzer.Analyze(map));

        GameplayIdentityV3Profile result = GameplayIdentityV3Analyzer.Analyze(map);

        Assert.NotNull(result);
        Assert.Equal(primary, map.GameplayProfile.Identity.Primary);
        Assert.Equal(secondary, map.GameplayProfile.Identity.Secondary);
        Assert.Equal(rating, map.BeatInsightRating);
        Assert.Equal(fusion, FusionSnapshot(GameplayFusionAnalyzer.Analyze(map)));
        Assert.Equal(sections, SectionSnapshot(SectionAnalysisV2Analyzer.Analyze(map)));
        Assert.Equal(pressure, PressureSnapshot(SkillPressureAnalyzer.Analyze(map)));
    }

    private static GameplayIdentityV3Profile Analyze(GameplayIdentityV3Input input) => GameplayIdentityV3Analyzer.Analyze(input);

    private static GameplayIdentityV3Input Input(
        int objects = 0,
        double jump = 0,
        double stream = 0,
        double tech = 0,
        SkillPressureProfile? pressure = null,
        IReadOnlyList<GameplayIdentityV3PatternInput>? patterns = null,
        SectionAnalysisResult? sections = null) => new(
            objects, jump, stream, tech,
            pressure ?? Pressure(),
            patterns ?? Array.Empty<GameplayIdentityV3PatternInput>(),
            sections ?? Sections());

    private static SkillPressureMetric Metric(double score, double confidence = .9, bool reliable = true) =>
        new(Math.Clamp(score, 0, 100), confidence, true, reliable, new Dictionary<string, double>(), string.Empty);

    private static SkillPressureProfile Pressure(
        SkillPressureMetric? stamina = null,
        SkillPressureMetric? endurance = null,
        SkillPressureMetric? finger = null,
        SkillPressureMetric? burst = null,
        SkillPressureMetric? aim = null,
        SkillPressureMetric? precision = null,
        SkillPressureMetric? reading = null,
        SkillPressureMetric? rhythm = null)
    {
        SkillPressureMetric unavailable = SkillPressureMetric.Unavailable("Unavailable");
        SkillPressureMetric[] metrics =
        [
            stamina ?? unavailable, endurance ?? unavailable, finger ?? unavailable, burst ?? unavailable,
            aim ?? unavailable, precision ?? unavailable, reading ?? unavailable, rhythm ?? unavailable
        ];
        SkillPressureMetric[] available = metrics.Where(metric => metric.Available).ToArray();
        return new(metrics[0], metrics[1], metrics[2], metrics[3], metrics[4], metrics[5], metrics[6], metrics[7],
            available.Length == 0 ? 0 : available.Average(metric => metric.Confidence), available.Length, []);
    }

    private static SectionAnalysisResult Sections(params GameplayStructureKind[] structures)
    {
        GameplaySectionV2Analysis[] sections = structures.Select((structure, index) => Section(structure, index)).ToArray();
        return new(
            sections, sections.Length == 0 ? 0 : 20, sections.Length == 0 ? 0 : 20,
            sections.Length == 0 ? null : 0, 0, 0, structures, Enumerable.Repeat(20d, sections.Length).ToArray(),
            Array.Empty<SectionPatternTimelinePoint>(), Array.Empty<SectionSkillTimelinePoint>(),
            sections.Length == 0 ? 0 : .8, TimeSpan.Zero);
    }

    private static SectionAnalysisResult SectionsWithDiagnostics()
    {
        GameplaySectionV2Analysis first = Section(GameplayStructureKind.Jump, 0);
        GameplaySectionV2Analysis second = Section(GameplayStructureKind.Stream, 1) with
        {
            Spike = new SectionDifficultySpike(true, 20, 30, 2.5, .8, ["Aim"]),
            Transition = new SectionGameplayTransition(true, GameplayStructureKind.Jump, GameplayStructureKind.Stream, .5, .4, .3, .55)
        };
        return new([first, second], 35, 50, 1, 1, 1,
            [GameplayStructureKind.Jump, GameplayStructureKind.Stream], [20, 50],
            Array.Empty<SectionPatternTimelinePoint>(), Array.Empty<SectionSkillTimelinePoint>(), .8, TimeSpan.Zero);
    }

    private static GameplaySectionV2Analysis Section(GameplayStructureKind structure, int index)
    {
        double jump = structure == GameplayStructureKind.Jump ? 80 : 5;
        double stream = structure == GameplayStructureKind.Stream ? 80 : 5;
        double tech = structure == GameplayStructureKind.Tech ? 80 : 5;
        return new(index, index * 5000, (index + 1) * 5000, Array.Empty<int>(), 0, 0, 0, 0, 0,
            jump, stream, tech, 0, structure, null, new SectionLocalSkills(0, 0, 0, 0), Pressure(),
            SectionPatternFamilyV2.Empty, SectionPatternFamilyV2.Empty, SectionPatternFamilyV2.Empty,
            20, .8, SectionDifficultySpike.None, SectionGameplayTransition.First(structure), structure.ToString());
    }

    private static string Snapshot(GameplayIdentityV3Profile profile) => string.Join('|',
        profile.JumpStructureScore, profile.StreamStructureScore, profile.TechStructureScore, profile.ClassicMixedScore,
        profile.PrimaryStructure, profile.SecondaryStructure, profile.StructureConfidence, profile.SkillConfidence,
        profile.PatternConfidence, profile.GlobalIdentityConfidence,
        string.Join(',', profile.MainSkills), string.Join(',', profile.MainPatterns),
        string.Join(',', profile.AllPatterns), string.Join(',', profile.Flags));

    private static string FusionSnapshot(BeatInsight.Models.Fusion.GameplayFusionProfile profile) => string.Join('|',
        profile.FinalAim, profile.FinalSpeed, profile.FinalReading, profile.FinalTech,
        profile.DominantStructure, profile.SecondaryStructure, profile.Confidence);
    private static string SectionSnapshot(SectionAnalysisResult result) => string.Join('|',
        result.SectionCount, result.AverageLocalDifficulty, result.MaximumLocalDifficulty,
        result.DifficultySpikeCount, result.GameplayTransitionCount, result.GlobalConfidence,
        string.Join(',', result.DominantStructureTimeline), string.Join(',', result.LocalDifficultyTimeline));
    private static string PressureSnapshot(SkillPressureProfile profile) => string.Join('|',
        profile.Stamina.Score, profile.Endurance.Score, profile.FingerControl.Score, profile.BurstControl.Score,
        profile.AimControl.Score, profile.Precision.Score, profile.ReadingPressure.Score,
        profile.RhythmComplexity.Score, profile.Confidence);
}
