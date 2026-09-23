using System.Text.Json;
using BeatInsight.Analysis;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Analysis.StreamPatterns;
using BeatInsight.Analysis.TechPatterns;
using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.TechPatterns;

namespace BeatInsight.Tests.Analysis;

public sealed class ComplexSliderPatternAnalyzerTests
{
    [Fact]
    public void CircleIsIgnored()
    {
        ComplexSliderPatternAnalysis result = Analyze([Circle()]);

        Assert.Empty(result.Sliders);
        Assert.Equal(0, result.TotalSliders);
    }

    [Fact]
    public void SimpleStraightSliderIsNotComplex()
    {
        ComplexSliderPatternOccurrence slider = Only(SimpleSlider());

        Assert.False(slider.Qualified);
        Assert.Equal("TooSimple", slider.Reason);
        Assert.Equal(0, slider.SliderComplexityScore, 10);
    }

    [Fact]
    public void SeveralControlPointsIncreaseScoreWithoutQualifyingStraightGeometry()
    {
        ComplexSliderPatternOccurrence simple = Only(SimpleSlider());
        ComplexSliderPatternOccurrence multiple = Only(StraightMultiPointSlider());

        Assert.True(multiple.SliderComplexityScore > simple.SliderComplexityScore);
        Assert.False(multiple.Qualified);
        Assert.Equal(3, multiple.EffectiveControlPointCount);
    }

    [Fact]
    public void CurvedSliderIncreasesScore()
    {
        ComplexSliderPatternOccurrence simple = Only(SimpleSlider());
        ComplexSliderPatternOccurrence curved = Only(CurvedSlider());

        Assert.True(curved.SliderComplexityScore > simple.SliderComplexityScore);
        Assert.True(curved.MaximumInternalTurnAngle > 35);
    }

    [Fact]
    public void MultipleRepeatsIncreaseScoreButAreNotSufficientAlone()
    {
        ComplexSliderPatternOccurrence simple = Only(SimpleSlider());
        ComplexSliderPatternOccurrence repeated = Only(SimpleSlider(repeatCount: 2));

        Assert.True(repeated.SliderComplexityScore > simple.SliderComplexityScore);
        Assert.Equal(0.5, repeated.RepeatSignal, 10);
        Assert.False(repeated.Qualified);
    }

    [Fact]
    public void CombinedSignalsProduceStrongComplexSlider()
    {
        ComplexSliderPatternOccurrence slider = Only(StrongComplexSlider());

        Assert.True(slider.Qualified);
        Assert.Equal("Qualified", slider.Reason);
        Assert.Equal(1, slider.SliderComplexityScore, 10);
    }

    [Fact]
    public void DegenerateChordDoesNotDivideByZeroOrInventPathSignal()
    {
        HitObject returning = Slider([(50, 50), (0, 0)], pathLength: 120);
        ComplexSliderPatternOccurrence slider = Only(returning);

        Assert.Equal(0, slider.ChordLength);
        Assert.Null(slider.PathToChordRatio);
        Assert.Null(slider.PathSignal);
        Assert.True(double.IsFinite(slider.SliderComplexityScore));
    }

    [Fact]
    public void NearReturnPathToChordRatioIsCapped()
    {
        ComplexSliderPatternOccurrence slider = Only(
            Slider([(50, 50), (1, 0)], pathLength: 10_000));

        Assert.Equal(10, slider.PathToChordRatio);
        Assert.Equal(1, slider.PathSignal);
    }

    [Fact]
    public void ConsecutiveDuplicateControlPointsAreIgnoredForEffectiveCount()
    {
        HitObject duplicated = Slider([(50, 0), (50, 0), (50, 0)], pathLength: 50);
        ComplexSliderPatternOccurrence slider = Only(duplicated);

        Assert.Equal(3, slider.ControlPointCount);
        Assert.Equal(1, slider.EffectiveControlPointCount);
        Assert.Equal(0, slider.SliderComplexityScore, 10);
    }

    [Fact]
    public void EveryScoreAndConfidenceIsBounded()
    {
        ComplexSliderPatternAnalysis result = Analyze([
            SimpleSlider(), CurvedSlider(), StrongComplexSlider(),
            Slider([(1, 0), (0, 0)], pathLength: 1_000_000, repeatCount: 20)
        ]);

        Assert.All(result.Sliders, slider => Assert.InRange(slider.SliderComplexityScore, 0, 1));
        Assert.InRange(result.Confidence, 0, 1);
    }

    [Fact]
    public void CoverageUsesQualifiedSlidersOverAllObjects()
    {
        ComplexSliderPatternAnalysis result = Analyze([Circle(), StrongComplexSlider()]);

        Assert.Equal(1, result.ComplexSliderObjectCount);
        Assert.Equal(0.5, result.Coverage, 10);
    }

    [Fact]
    public void SliderCoverageUsesQualifiedSlidersOverAllSliders()
    {
        ComplexSliderPatternAnalysis result = Analyze([Circle(), SimpleSlider(), StrongComplexSlider()]);

        Assert.Equal(0.5, result.SliderCoverage, 10);
    }

    [Fact]
    public void AverageAndMaximumComplexityUseQualifiedSlidersOnly()
    {
        ComplexSliderPatternAnalysis result = Analyze([
            SimpleSlider(), CurvedSlider(), StrongComplexSlider()
        ]);

        Assert.Equal(0.8125, result.AverageComplexityScore, 10);
        Assert.Equal(1, result.MaximumComplexityScore, 10);
        Assert.Equal(2.5, result.AverageControlPointCount, 10);
        Assert.Equal(1.5, result.AverageRepeatCount, 10);
        Assert.Equal(1.6, result.AveragePathToChordRatio, 10);
    }

    [Fact]
    public void ConfidenceUsesFrozenDescriptiveFormula()
    {
        ComplexSliderPatternAnalysis result = Analyze([Circle(), StrongComplexSlider()]);

        Assert.Equal(0.75, result.Confidence, 10);
    }

    [Fact]
    public void CurveTypeAloneNeverQualifiesSlider()
    {
        ComplexSliderPatternOccurrence perfectCurve = Only(SimpleSlider(curveType: "P"));

        Assert.False(perfectCurve.Qualified);
        Assert.Equal(0, perfectCurve.SliderComplexityScore, 10);
    }

    [Fact]
    public void BeatmapOverloadProvidesReliableDurationWithoutUsingItInScore()
    {
        HitObject slider = StrongComplexSlider();
        var map = Map([slider]);
        map.SliderMultiplier = 1.4;
        map.TimingPoints.Add(new TimingPoint { Time = 0, BeatLength = 500, Uninherited = true });

        ComplexSliderPatternOccurrence withTiming = Assert.Single(
            TechPatternAnalyzer.AnalyzeComplexSlider(map).Sliders);
        ComplexSliderPatternOccurrence withoutTiming = Only(slider);

        Assert.NotNull(withTiming.Duration);
        Assert.Equal(withTiming.StartTime + withTiming.Duration, withTiming.EndTime);
        Assert.Null(withoutTiming.Duration);
        Assert.Null(withoutTiming.EndTime);
        Assert.Equal(withoutTiming.SliderComplexityScore, withTiming.SliderComplexityScore, 10);
    }

    [Fact]
    public void ComplexSliderCanBeDetectedInIsolation()
    {
        ComplexSliderPatternAnalysis result = Analyze([Circle(), StrongComplexSlider(), Circle()]);
        SliderChainPatternAnalysis chain = TechPatternAnalyzer.AnalyzeSliderChain(
            [Circle(), StrongComplexSlider(), Circle()]);

        Assert.Single(result.ComplexSliders);
        Assert.Empty(chain.Sequences);
    }

    [Fact]
    public void SimpleSlidersInChainRemainSimple()
    {
        HitObject[] sliders = [SimpleSlider(0), SimpleSlider(100), SimpleSlider(200)];

        Assert.Single(TechPatternAnalyzer.AnalyzeSliderChain(sliders).Sequences);
        Assert.Empty(Analyze(sliders).ComplexSliders);
    }

    [Fact]
    public void ComplexSliderAndSliderChainCanCoexist()
    {
        HitObject[] sliders = [SimpleSlider(0), StrongComplexSlider(100), SimpleSlider(200)];

        Assert.Single(TechPatternAnalyzer.AnalyzeSliderChain(sliders).Sequences);
        Assert.Single(Analyze(sliders).ComplexSliders);
    }

    [Fact]
    public void RunningComplexSliderDoesNotChangeSliderChainResult()
    {
        HitObject[] objects = [SimpleSlider(0), StrongComplexSlider(100), SimpleSlider(200)];
        string before = JsonSerializer.Serialize(TechPatternAnalyzer.AnalyzeSliderChain(objects));

        _ = Analyze(objects);

        Assert.Equal(before, JsonSerializer.Serialize(TechPatternAnalyzer.AnalyzeSliderChain(objects)));
    }

    [Fact]
    public void RunningComplexSliderDoesNotChangeStreamV1()
    {
        Beatmap map = Map([Circle(0), SimpleSlider(100), Circle(200), StrongComplexSlider(300)]);
        string before = SerializeStreamPatterns(map);

        _ = TechPatternAnalyzer.AnalyzeComplexSlider(map);

        Assert.Equal(before, SerializeStreamPatterns(map));
    }

    [Fact]
    public void RunningComplexSliderDoesNotChangeJumpV1()
    {
        Beatmap map = Map([Circle(0), SimpleSlider(100), Circle(200), StrongComplexSlider(300)]);
        GameplayProfile profile = GameplayAnalyzer.Analyze(map);
        string before = JsonSerializer.Serialize(JumpPatternAnalyzer.Analyze(map.HitObjects, profile));

        _ = TechPatternAnalyzer.AnalyzeComplexSlider(map);

        Assert.Equal(before, JsonSerializer.Serialize(JumpPatternAnalyzer.Analyze(map.HitObjects, profile)));
    }

    [Fact]
    public void HistoricalTechTechV2IdentityRatingAndUiStateRemainUnchanged()
    {
        Beatmap map = Map([SimpleSlider(0), StrongComplexSlider(100), Circle(200)]);
        GameplayProfile profile = GameplayAnalyzer.Analyze(map);
        string profileBefore = JsonSerializer.Serialize(profile);
        double ratingBefore = map.BeatInsightRating;
        string techV2Before = SerializeTechV2(map);
        string historicalBefore = JsonSerializer.Serialize(
            TechPatternRecognitionEngine.FindMatches(map.HitObjects));

        ComplexSliderPatternAnalysis first = TechPatternAnalyzer.AnalyzeComplexSlider(map);
        ComplexSliderPatternAnalysis second = TechPatternAnalyzer.AnalyzeComplexSlider(map);

        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.Equal(profileBefore, JsonSerializer.Serialize(profile));
        Assert.Equal(ratingBefore, map.BeatInsightRating);
        Assert.Equal(techV2Before, SerializeTechV2(map));
        Assert.Equal(historicalBefore, JsonSerializer.Serialize(
            TechPatternRecognitionEngine.FindMatches(map.HitObjects)));
        Assert.Same(profile, map.GameplayProfile);
    }

    [Fact]
    public void DefaultsAndDebugSwitchesMatchComplexSliderV1()
    {
        var options = new ComplexSliderPatternOptions();

        Assert.Equal(3, options.MinimumComplexControlPoints);
        Assert.Equal(1.20, options.MinimumComplexPathToChordRatio);
        Assert.Equal(10.0, options.MaximumPathToChordRatio);
        Assert.Equal(35, options.MinimumInternalTurnAngle);
        Assert.Equal(0.50, options.MinimumSliderComplexityScore);
        Assert.False(GameplayDebug.ComplexSliderPatternEnabled);
        Assert.False(GameplayDebug.ComplexSliderPatternDetailedEnabled);
        GameplayDebug.ComplexSliderPattern(Analyze([StrongComplexSlider()]));
    }

    private static ComplexSliderPatternAnalysis Analyze(IReadOnlyList<HitObject> objects) =>
        TechPatternAnalyzer.AnalyzeComplexSlider(objects);

    private static ComplexSliderPatternOccurrence Only(HitObject slider) =>
        Assert.Single(Analyze([slider]).Sliders);

    private static HitObject Circle(int time = 0) => new()
    {
        Type = 1,
        Time = time,
        X = time / 2,
        Y = time % 120
    };

    private static HitObject SimpleSlider(
        int time = 0,
        int repeatCount = 0,
        string curveType = "L") =>
        Slider([(100, 0)], 100, repeatCount, curveType, time);

    private static HitObject StraightMultiPointSlider(int time = 0) =>
        Slider([(30, 0), (60, 0), (100, 0)], 100, time: time);

    private static HitObject CurvedSlider(int time = 0) =>
        Slider([(50, 100), (100, 0)], 160, time: time);

    private static HitObject StrongComplexSlider(int time = 0) =>
        Slider([(50, 80), (100, -80), (150, 0)], 240, repeatCount: 3, time: time);

    private static HitObject Slider(
        IReadOnlyList<(int X, int Y)> controlPoints,
        double pathLength,
        int repeatCount = 0,
        string curveType = "B",
        int time = 0)
    {
        int slides = repeatCount + 1;
        (int X, int Y) baseEnd = controlPoints.Count == 0 ? (0, 0) : controlPoints[^1];
        (int X, int Y) actualEnd = slides % 2 == 0 ? (0, 0) : baseEnd;
        return new HitObject
        {
            Type = 2,
            Time = time,
            X = 0,
            Y = 0,
            Slides = slides,
            Length = pathLength,
            SliderCurveType = curveType,
            SliderControlPoints = controlPoints
                .Select(point => new SliderControlPoint { X = point.X, Y = point.Y })
                .ToList(),
            SliderEndPosition = new SliderControlPoint { X = actualEnd.X, Y = actualEnd.Y }
        };
    }

    private static Beatmap Map(IEnumerable<HitObject> objects)
    {
        var map = new Beatmap();
        map.HitObjects.AddRange(objects);
        return map;
    }

    private static string SerializeStreamPatterns(Beatmap map) => JsonSerializer.Serialize(new object[]
    {
        StreamPatternAnalyzer.AnalyzeBurst(map.HitObjects),
        StreamPatternAnalyzer.AnalyzeShortStream(map.HitObjects),
        StreamPatternAnalyzer.AnalyzeLongStream(map.HitObjects),
        StreamPatternAnalyzer.AnalyzeDeathstream(map.HitObjects),
        StreamPatternAnalyzer.AnalyzeStaminaStream(map.HitObjects),
        StreamPatternAnalyzer.AnalyzeIrregularStream(map.HitObjects),
        StreamPatternAnalyzer.AnalyzeSpeedStream(map.HitObjects),
        StreamPatternAnalyzer.AnalyzeFlowStream(map.HitObjects),
        StreamPatternAnalyzer.AnalyzeAlternatingStream(map.HitObjects)
    });

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
