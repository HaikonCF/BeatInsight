using System.Text.Json;
using BeatInsight.Analysis;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Analysis.TechPatterns;
using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.TechPatterns;

namespace BeatInsight.Tests.Analysis;

public sealed class SpatialAwkwardTransitionPatternAnalyzerTests
{
    [Fact]
    public void InsufficientContextIsRejected()
    {
        SpatialAwkwardTransitionPatternAnalysis result = Analyze(
            Circle(0, 0, 0), Circle(120, 0, 100));

        Assert.Empty(result.Transitions);
        Assert.All(result.Occurrences, occurrence =>
            Assert.Equal("InsufficientContext", occurrence.Reason));
    }

    [Fact]
    public void SpinnerBreaksLocalContext()
    {
        HitObject spinner = Circle(0, 0, 100);
        spinner.Type = 8;

        SpatialAwkwardTransitionPatternAnalysis result = Analyze(
            Circle(-120, 0, 0), spinner, Circle(-120, 0, 200));

        Assert.Empty(result.Transitions);
        Assert.Equal("IntervalBreak", Pivot(result, 1).Reason);
    }

    [Fact]
    public void NegativeGapBreaksLocalContext()
    {
        SpatialAwkwardTransitionPatternOccurrence occurrence = Pivot(Analyze(
            Circle(-120, 0, 100), Circle(0, 0, 99), Circle(-120, 0, 200)), 1);

        Assert.Equal("IntervalBreak", occurrence.Reason);
    }

    [Fact]
    public void GapAboveOneSecondBreaksLocalContext()
    {
        SpatialAwkwardTransitionPatternOccurrence occurrence = Pivot(Analyze(
            Circle(-120, 0, 0), Circle(0, 0, 1001), Circle(-120, 0, 1101)), 1);

        Assert.Equal("IntervalBreak", occurrence.Reason);
    }

    [Fact]
    public void OneSecondGapBoundaryRemainsValid()
    {
        SpatialAwkwardTransitionPatternOccurrence occurrence = Assert.Single(Analyze(
            Circle(-120, 0, 0), Circle(0, 0, 1000), Circle(-120, 0, 2000)).Transitions);

        Assert.Equal("Qualified", occurrence.Reason);
    }

    [Fact]
    public void MicroDisplacementsAreBelowDistanceRelevance()
    {
        SpatialAwkwardTransitionPatternOccurrence occurrence = Pivot(Analyze(
            Circle(-10, 0, 0), Circle(0, 0, 100), Circle(-10, 0, 200)), 1);

        Assert.Equal("BelowDistanceRelevance", occurrence.Reason);
    }

    [Fact]
    public void LargeStraightDistanceHasOnlyOneActiveSignal()
    {
        SpatialAwkwardTransitionPatternOccurrence occurrence = Pivot(Analyze(
            Circle(-300, 0, 0), Circle(0, 0, 100), Circle(300, 0, 200)), 1);

        Assert.Equal(1, occurrence.DistanceSignal);
        Assert.Equal(0, occurrence.TurnSignal);
        Assert.Equal(0, occurrence.AsymmetrySignal);
        Assert.Equal("InsufficientCombinedSignals", occurrence.Reason);
    }

    [Fact]
    public void LargeTurnWithWeakDistancesStaysBelowDistanceRelevance()
    {
        SpatialAwkwardTransitionPatternOccurrence occurrence = Pivot(Analyze(
            Circle(-40, 0, 0), Circle(0, 0, 100), Circle(-40, 0, 200)), 1);

        Assert.Equal(180, occurrence.TurnAngle, 8);
        Assert.Equal("BelowDistanceRelevance", occurrence.Reason);
    }

    [Fact]
    public void CombinedDistanceTurnAndAsymmetryQualify()
    {
        SpatialAwkwardTransitionPatternOccurrence occurrence = Assert.Single(Analyze(
            Circle(-180, 0, 0), Circle(0, 0, 100), Circle(-42, 42, 200)).Transitions);

        Assert.True(occurrence.DistanceSignal > 0);
        Assert.True(occurrence.TurnSignal > 0);
        Assert.True(occurrence.AsymmetrySignal > 0);
    }

    [Fact]
    public void ScoreExactlyOneHalfIsInclusive()
    {
        SpatialAwkwardTransitionPatternOccurrence occurrence = Assert.Single(Analyze(
            Circle(-120, 0, 0), Circle(0, 0, 100), Circle(-120, 0, 200)).Transitions);

        Assert.Equal(0.5, occurrence.DistanceSignal!.Value, 10);
        Assert.Equal(1, occurrence.TurnSignal);
        Assert.Equal(0, occurrence.AsymmetrySignal);
        Assert.Equal(0.5, occurrence.SpatialAwkwardScore, 10);
    }

    [Fact]
    public void ScoreJustBelowOneHalfIsRejected()
    {
        SpatialAwkwardTransitionPatternOccurrence occurrence = Pivot(Analyze(
            Circle(-119, 0, 0), Circle(0, 0, 100), Circle(-119, 0, 200)), 1);

        Assert.True(occurrence.SpatialAwkwardScore < 0.5);
        Assert.Equal("BelowAwkwardThreshold", occurrence.Reason);
    }

    [Fact]
    public void OneExtremeSignalDoesNotQualify()
    {
        SpatialAwkwardTransitionPatternOccurrence occurrence = Pivot(Analyze(
            Circle(-1000, 0, 0), Circle(0, 0, 100), Circle(1000, 0, 200)), 1);

        Assert.Equal(1, occurrence.DistanceSignal);
        Assert.Equal("InsufficientCombinedSignals", occurrence.Reason);
    }

    [Fact]
    public void TwoActiveSignalsCanQualify()
    {
        SpatialAwkwardTransitionPatternOccurrence occurrence = Assert.Single(Analyze(
            Circle(-120, 0, 0), Circle(0, 0, 100), Circle(-120, 0, 200)).Transitions);

        Assert.Equal(2, ActiveSignalCount(occurrence));
    }

    [Fact]
    public void EqualDistancesHaveZeroAsymmetry()
    {
        SpatialAwkwardTransitionPatternOccurrence occurrence = Pivot(Analyze(
            Circle(-120, 0, 0), Circle(0, 0, 100), Circle(-120, 0, 200)), 1);

        Assert.Equal(1, occurrence.DistanceRatio, 10);
        Assert.Equal(0, occurrence.Asymmetry, 10);
        Assert.Equal(0, occurrence.AsymmetrySignal);
    }

    [Fact]
    public void StrongAsymmetryIsMeasured()
    {
        SpatialAwkwardTransitionPatternOccurrence occurrence = Pivot(Analyze(
            Circle(-180, 0, 0), Circle(0, 0, 100), Circle(-30, 0, 200)), 1);

        Assert.Equal(1.0 / 6, occurrence.DistanceRatio, 10);
        Assert.Equal(5.0 / 6, occurrence.Asymmetry, 10);
        Assert.Equal(1, occurrence.AsymmetrySignal);
    }

    [Fact]
    public void RightAngleStartsWithZeroTurnSignal()
    {
        SpatialAwkwardTransitionPatternOccurrence occurrence = Pivot(Analyze(
            Circle(-120, 0, 0), Circle(0, 0, 100), Circle(0, 120, 200)), 1);

        Assert.Equal(90, occurrence.TurnAngle, 8);
        Assert.Equal(0, occurrence.TurnSignal);
    }

    [Fact]
    public void AngleJustBelowNinetyWithScoreAboveThresholdIsRejected()
    {
        SpatialAwkwardTransitionPatternOccurrence occurrence = Pivot(Analyze(
            Circle(-1_000_000, 0, 0), Circle(0, 0, 100), Circle(9, 50_000, 200)), 1);

        Assert.InRange(occurrence.TurnAngle, 89.98, 89.99);
        Assert.True(occurrence.SpatialAwkwardScore >= 0.50);
        Assert.False(occurrence.Qualified);
        Assert.Equal("BelowAwkwardThreshold", occurrence.Reason);
    }

    [Fact]
    public void AngleExactlyNinetyWithScoreAtLeastThresholdIsAccepted()
    {
        SpatialAwkwardTransitionPatternOccurrence occurrence = Assert.Single(Analyze(
            Circle(-1_000_000, 0, 0), Circle(0, 0, 100), Circle(0, 50_000, 200)).Transitions);

        Assert.Equal(90, occurrence.TurnAngle, 8);
        Assert.True(occurrence.SpatialAwkwardScore >= 0.50);
        Assert.Equal("Qualified", occurrence.Reason);
    }

    [Fact]
    public void AngleAboveNinetyWithScoreAtLeastThresholdIsAccepted()
    {
        SpatialAwkwardTransitionPatternOccurrence occurrence = Assert.Single(Analyze(
            Circle(-1_000_000, 0, 0), Circle(0, 0, 100), Circle(-9, 50_000, 200)).Transitions);

        Assert.InRange(occurrence.TurnAngle, 90.01, 90.02);
        Assert.True(occurrence.SpatialAwkwardScore >= 0.50);
    }

    [Fact]
    public void DistanceAndAsymmetrySignalsCannotQualifyBelowNinetyDegrees()
    {
        SpatialAwkwardTransitionPatternOccurrence occurrence = Pivot(Analyze(
            Circle(-1_000_000, 0, 0), Circle(0, 0, 100), Circle(50_000, 1_000, 200)), 1);

        Assert.True(occurrence.DistanceSignal > 0);
        Assert.True(occurrence.AsymmetrySignal > 0);
        Assert.True(occurrence.SpatialAwkwardScore >= 0.50);
        Assert.True(occurrence.TurnAngle < 90);
        Assert.False(occurrence.Qualified);
    }

    [Fact]
    public void StrongAwkwardCaseRemainsQualified()
    {
        SpatialAwkwardTransitionPatternOccurrence occurrence = Assert.Single(Analyze(
            Circle(-300, 0, 0), Circle(0, 0, 100), Circle(-60, 0, 200)).Transitions);

        Assert.Equal(1, occurrence.DistanceSignal);
        Assert.Equal(1, occurrence.TurnSignal);
        Assert.Equal(1, occurrence.AsymmetrySignal);
        Assert.Equal(1, occurrence.SpatialAwkwardScore);
    }

    [Fact]
    public void TopSyntheticStrongCaseIsUnchanged()
    {
        SpatialAwkwardTransitionPatternOccurrence occurrence = Assert.Single(Analyze(
            Circle(-10_000, 0, 0), Circle(0, 0, 100), Circle(-1, 0, 200)).Transitions);

        Assert.Equal(180, occurrence.TurnAngle, 8);
        Assert.Equal(1, occurrence.DistanceSignal);
        Assert.Equal(1, occurrence.TurnSignal);
        Assert.Equal(1, occurrence.AsymmetrySignal);
        Assert.Equal(1, occurrence.SpatialAwkwardScore);
    }

    [Fact]
    public void CoverageExcludesRemovedBelowNinetyFalsePositive()
    {
        SpatialAwkwardTransitionPatternAnalysis result = Analyze(
            Circle(-1_000_000, 0, 0), Circle(0, 0, 100), Circle(50_000, 1_000, 200));

        Assert.Empty(result.Transitions);
        Assert.Equal(0, result.SpatialAwkwardObjectCount);
        Assert.Equal(0, result.Coverage);
    }

    [Fact]
    public void SamePivotKeepsItsExistingScore()
    {
        SpatialAwkwardTransitionPatternOccurrence occurrence = Pivot(Analyze(
            Circle(-1_000_000, 0, 0), Circle(0, 0, 100), Circle(0, 50_000, 200)), 1);

        Assert.Equal(2.0 / 3, occurrence.SpatialAwkwardScore, 10);
    }

    [Fact]
    public void SamePivotKeepsItsExistingSignals()
    {
        SpatialAwkwardTransitionPatternOccurrence occurrence = Pivot(Analyze(
            Circle(-1_000_000, 0, 0), Circle(0, 0, 100), Circle(0, 50_000, 200)), 1);

        Assert.Equal(1, occurrence.DistanceSignal);
        Assert.Equal(0, occurrence.TurnSignal);
        Assert.Equal(1, occurrence.AsymmetrySignal);
    }

    [Fact]
    public void ReversalHasMaximumTurnSignal()
    {
        SpatialAwkwardTransitionPatternOccurrence occurrence = Pivot(Analyze(
            Circle(-120, 0, 0), Circle(0, 0, 100), Circle(-120, 0, 200)), 1);

        Assert.Equal(180, occurrence.TurnAngle, 8);
        Assert.Equal(1, occurrence.TurnSignal);
    }

    [Fact]
    public void DistanceSignalIsClamped()
    {
        SpatialAwkwardTransitionPatternOccurrence occurrence = Pivot(Analyze(
            Circle(-10_000, 0, 0), Circle(0, 0, 100), Circle(-10_000, 0, 200)), 1);

        Assert.Equal(1, occurrence.DistanceSignal);
    }

    [Fact]
    public void TurnSignalIsClamped()
    {
        SpatialAwkwardTransitionPatternOccurrence occurrence = Pivot(Analyze(
            Circle(-120, 0, 0), Circle(0, 0, 100), Circle(-120, 0, 200)), 1);

        Assert.InRange(occurrence.TurnSignal!.Value, 0, 1);
        Assert.Equal(1, occurrence.TurnSignal);
    }

    [Fact]
    public void AsymmetrySignalIsClamped()
    {
        SpatialAwkwardTransitionPatternOccurrence occurrence = Pivot(Analyze(
            Circle(-600, 0, 0), Circle(0, 0, 100), Circle(-60, 0, 200)), 1);

        Assert.Equal(1, occurrence.AsymmetrySignal);
    }

    [Fact]
    public void ScoresAndConfidenceAreBounded()
    {
        SpatialAwkwardTransitionPatternAnalysis result = Analyze(
            Circle(-10_000, 0, 0), Circle(0, 0, 100), Circle(-1, 0, 200));

        Assert.All(result.Occurrences, occurrence =>
            Assert.InRange(occurrence.SpatialAwkwardScore, 0, 1));
        Assert.InRange(result.Confidence, 0, 1);
    }

    [Fact]
    public void CoverageUsesUniqueObjectsAfterDeduplication()
    {
        SpatialAwkwardTransitionPatternAnalysis result = Analyze(
            Circle(-120, 0, 0), Circle(0, 0, 100),
            Circle(-120, 0, 200), Circle(0, 0, 300));

        Assert.Single(result.Transitions);
        Assert.Equal(3, result.SpatialAwkwardObjectCount);
        Assert.Equal(0.75, result.Coverage, 10);
    }

    [Fact]
    public void AdjacentQualifiedPivotsBecomeOneEvent()
    {
        SpatialAwkwardTransitionPatternAnalysis result = Analyze(
            Circle(-120, 0, 0), Circle(0, 0, 100),
            Circle(-120, 0, 200), Circle(0, 0, 300));

        SpatialAwkwardTransitionPatternOccurrence transition = Assert.Single(result.Transitions);
        Assert.Equal(1, transition.PivotIndex);
    }

    [Fact]
    public void TwoSeparatedZonesRemainTwoEvents()
    {
        SpatialAwkwardTransitionPatternAnalysis result = Analyze(
            Circle(-120, 0, 0), Circle(0, 0, 100), Circle(-120, 0, 200),
            Circle(1000, 0, 1301), Circle(1120, 0, 1401), Circle(1000, 0, 1501));

        Assert.Equal(new[] { 1, 4 }, result.Transitions.Select(transition => transition.PivotIndex));
    }

    [Fact]
    public void SpatialAwkwardCanCoexistWithSharpJump()
    {
        Beatmap map = Map(
            Circle(0, 0, 0), Circle(120, 0, 200),
            Circle(0, 0, 400), Circle(120, 0, 600));

        Assert.Single(TechPatternAnalyzer.AnalyzeSpatialAwkwardTransition(map.HitObjects).Transitions);
        Assert.NotEmpty(JumpPatternAnalyzer.AnalyzeSharp(
            map.HitObjects, GameplayAnalyzer.Analyze(map)).Matches);
    }

    [Fact]
    public void SpatialAwkwardCanCoexistWithReversalJump()
    {
        Beatmap map = Map(
            Circle(0, 0, 0), Circle(120, 0, 200),
            Circle(0, 0, 400), Circle(120, 0, 600));

        Assert.Single(TechPatternAnalyzer.AnalyzeSpatialAwkwardTransition(map.HitObjects).Transitions);
        Assert.NotEmpty(JumpPatternAnalyzer.AnalyzeReversal(
            map.HitObjects, GameplayAnalyzer.Analyze(map)).Matches);
    }

    [Fact]
    public void SpatialAwkwardCanCoexistWithSliderDirectionChange()
    {
        Beatmap map = Map(
            Circle(-120, 0, 0), Slider(0, 0, 120, 0, 100), Circle(0, 0, 200));

        Assert.Single(TechPatternAnalyzer.AnalyzeSpatialAwkwardTransition(map.HitObjects).Transitions);
        Assert.NotEmpty(TechPatternAnalyzer.AnalyzeSliderDirectionChange(map.HitObjects).QualifiedOccurrences);
    }

    [Fact]
    public void SpatialAwkwardCanCoexistWithCircleSliderTransition()
    {
        Beatmap map = Map(
            Circle(-120, 0, 0), Slider(0, 0, 120, 0, 100),
            Circle(0, 0, 200), Slider(-120, 0, 0, 0, 300));

        Assert.NotEmpty(TechPatternAnalyzer.AnalyzeSpatialAwkwardTransition(map.HitObjects).Transitions);
        Assert.Single(TechPatternAnalyzer.AnalyzeCircleSliderTransition(map.HitObjects).Sequences);
    }

    [Fact]
    public void SpatialAwkwardCanCoexistWithRhythmTransition()
    {
        Beatmap map = Map(
            Circle(-120, 0, 0), Circle(0, 0, 100), Circle(-120, 0, 200),
            Circle(0, 0, 250), Circle(-120, 0, 300));

        Assert.NotEmpty(TechPatternAnalyzer.AnalyzeSpatialAwkwardTransition(map.HitObjects).Transitions);
        Assert.Single(TechPatternAnalyzer.AnalyzeRhythmTransition(map.HitObjects).Transitions);
    }

    [Fact]
    public void AnalysisDoesNotMutateTechV2IdentityRatingOrUiState()
    {
        Beatmap map = Map(
            Circle(-120, 0, 0), Circle(0, 0, 100), Circle(-120, 0, 200));
        GameplayProfile profile = GameplayAnalyzer.Analyze(map);
        string profileBefore = JsonSerializer.Serialize(profile);
        string techV2Before = SerializeTechV2(map);
        string historicalBefore = JsonSerializer.Serialize(
            TechPatternRecognitionEngine.FindMatches(map.HitObjects));
        double ratingBefore = map.BeatInsightRating;

        SpatialAwkwardTransitionPatternAnalysis first =
            TechPatternAnalyzer.AnalyzeSpatialAwkwardTransition(map.HitObjects);
        SpatialAwkwardTransitionPatternAnalysis second =
            TechPatternAnalyzer.AnalyzeSpatialAwkwardTransition(map.HitObjects);

        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.Equal(profileBefore, JsonSerializer.Serialize(profile));
        Assert.Equal(techV2Before, SerializeTechV2(map));
        Assert.Equal(historicalBefore, JsonSerializer.Serialize(
            TechPatternRecognitionEngine.FindMatches(map.HitObjects)));
        Assert.Equal(ratingBefore, map.BeatInsightRating);
        Assert.Same(profile, map.GameplayProfile);
    }

    [Fact]
    public void SliderFinalPositionDrivesOutgoingTransitionGeometry()
    {
        SpatialAwkwardTransitionPatternOccurrence occurrence = Assert.Single(Analyze(
            Circle(-120, 0, 0), Slider(0, 0, 120, 0, 100), Circle(0, 0, 200)).Transitions);

        Assert.Equal(120, occurrence.IncomingDistance, 8);
        Assert.Equal(120, occurrence.OutgoingDistance, 8);
        Assert.Equal(180, occurrence.TurnAngle, 8);
    }

    [Fact]
    public void ZeroLengthTransitionHasUnreliableGeometry()
    {
        SpatialAwkwardTransitionPatternOccurrence occurrence = Pivot(Analyze(
            Circle(0, 0, 0), Circle(0, 0, 100), Circle(-120, 0, 200)), 1);

        Assert.Equal("UnreliableGeometry", occurrence.Reason);
        Assert.Null(occurrence.DistanceSignal);
    }

    [Fact]
    public void DefaultsAggregatesAndDebugSwitchesMatchV1()
    {
        var options = new SpatialAwkwardTransitionPatternOptions();
        SpatialAwkwardTransitionPatternAnalysis result = Analyze(
            Circle(-120, 0, 0), Circle(0, 0, 100), Circle(-120, 0, 200));
        SpatialAwkwardTransitionPatternOccurrence transition = Assert.Single(result.Transitions);

        Assert.Equal(1, options.SpatialWindowSize);
        Assert.Equal(60, options.MinimumRelevantDistance);
        Assert.Equal(90, options.MinimumAwkwardTurnAngle);
        Assert.Equal(0.50, options.MinimumSpatialAwkwardScore);
        Assert.Equal(1000, options.MaximumSpatialTransitionGap);
        Assert.Equal(120, result.AverageIncomingDistance, 8);
        Assert.Equal(120, result.AverageOutgoingDistance, 8);
        Assert.Equal(180, result.AverageTurnAngle, 8);
        Assert.Equal(0, result.AverageAsymmetry, 8);
        Assert.Equal(0.5, result.AverageSpatialAwkwardScore, 10);
        Assert.Equal(0.5, result.MaximumSpatialAwkwardScore, 10);
        Assert.Equal((0.5 + 1 + 0.5) / 3, result.Confidence, 10);
        Assert.False(GameplayDebug.SpatialAwkwardTransitionPatternEnabled);
        Assert.False(GameplayDebug.SpatialAwkwardTransitionPatternDetailedEnabled);
        GameplayDebug.SpatialAwkwardTransitionPattern(result);
        Assert.Equal("Qualified", transition.Reason);
    }

    [Fact]
    public void OccurrenceReasonsStayWithinV1Taxonomy()
    {
        string[] allowedReasons =
        [
            "InsufficientContext",
            "IntervalBreak",
            "UnreliableGeometry",
            "BelowDistanceRelevance",
            "BelowAwkwardThreshold",
            "InsufficientCombinedSignals",
            "Qualified"
        ];

        SpatialAwkwardTransitionPatternAnalysis result = Analyze(
            Circle(-120, 0, 0), Circle(0, 0, 100), Circle(-119, 0, 200),
            Circle(181, 0, 300));

        Assert.All(result.Occurrences, occurrence =>
            Assert.Contains(occurrence.Reason, allowedReasons));
    }

    private static SpatialAwkwardTransitionPatternAnalysis Analyze(params HitObject[] objects) =>
        TechPatternAnalyzer.AnalyzeSpatialAwkwardTransition(objects);

    private static SpatialAwkwardTransitionPatternOccurrence Pivot(
        SpatialAwkwardTransitionPatternAnalysis result,
        int pivotIndex) =>
        Assert.Single(result.Occurrences, occurrence => occurrence.PivotIndex == pivotIndex);

    private static int ActiveSignalCount(SpatialAwkwardTransitionPatternOccurrence occurrence) =>
        new[] { occurrence.DistanceSignal, occurrence.TurnSignal, occurrence.AsymmetrySignal }
            .Count(signal => signal > 0);

    private static Beatmap Map(params HitObject[] objects) => new()
    {
        CS = 4,
        SliderMultiplier = 1.4,
        TimingPoints = [new() { Time = 0, BeatLength = 500, Uninherited = true }],
        HitObjects = objects.ToList()
    };

    private static HitObject Circle(int x, int y, int time) => new()
    {
        Type = 1,
        X = x,
        Y = y,
        Time = time
    };

    private static HitObject Slider(
        int startX,
        int startY,
        int endX,
        int endY,
        int time) => new()
    {
        Type = 2,
        X = startX,
        Y = startY,
        Time = time,
        Slides = 1,
        Length = Math.Sqrt(Math.Pow(endX - startX, 2) + Math.Pow(endY - startY, 2)),
        SliderCurveType = "L",
        SliderControlPoints = [new SliderControlPoint { X = endX, Y = endY }],
        SliderEndPosition = new SliderControlPoint { X = endX, Y = endY }
    };

    private static string SerializeTechV2(Beatmap map) => JsonSerializer.Serialize(
        TechDetectorV2Experimental.Analyze(
            map.HitObjects,
            GameplayAnalyzer.IsSpinner,
            GameplayAnalyzer.IsSlider,
            GameplayAnalyzer.IsComplexSlider,
            GameplayAnalyzer.GetObjectStartPosition,
            GameplayAnalyzer.GetObjectEndPosition,
            GameplayAnalyzer.GetTechTurnAngle));
}
