using BeatInsight.Analysis;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Analysis.StreamPatterns;
using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.StreamPatterns;

namespace BeatInsight.Tests.Analysis;

public sealed class FlowStreamPatternAnalyzerTests
{
    [Fact]
    public void RunBelowMinimumLength_IsRejected()
    {
        FlowStreamPatternSequence run = Assert.Single(Analyze(Path(5, 30), Repeat(80, 4)).Runs);

        Assert.False(run.Qualified);
        Assert.Equal("TooShort", run.Reason);
    }

    [Fact]
    public void PerfectlyLinearStream_IsRejected()
    {
        FlowStreamPatternSequence run = Assert.Single(Analyze(PathFromHeadings(0, 0, 0, 0, 0)).Runs);

        Assert.Equal(0, run.FlowPivotRatio);
        Assert.Equal("TooFewFlowPivots", run.Reason);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(-30)]
    public void RegularCurveInEitherDirection_IsAccepted(double turnDegrees)
    {
        FlowStreamPatternSequence sequence = Assert.Single(Analyze(Path(6, turnDegrees)).Sequences);

        Assert.Equal(1, sequence.FlowPivotRatio, 10);
        Assert.Equal(1, sequence.TurnDirectionConsistency, 10);
        Assert.Equal(Math.Sign(turnDegrees), sequence.DominantTurnDirection);
    }

    [Fact]
    public void AlternatingLeftRightZigzag_IsRejected()
    {
        FlowStreamPatternSequence run = Assert.Single(
            Analyze(PathFromHeadings(30, -30, 30, -30, 30)).Runs);

        Assert.Equal(1, run.FlowPivotRatio, 10);
        Assert.Equal(0.5, run.TurnDirectionConsistency, 10);
        Assert.Equal("InconsistentTurnDirection", run.Reason);
    }

    [Fact]
    public void ReversalHeavyStream_IsRejected()
    {
        FlowStreamPatternSequence run = Assert.Single(
            Analyze(PathFromHeadings(0, 180, 0, 180, 0)).Runs);

        Assert.All(run.TurnAngles, angle => Assert.InRange(angle, 179.9, 180));
        Assert.Equal("TooFewFlowPivots", run.Reason);
    }

    [Fact]
    public void ANoisyStraightPivotIsTolerated()
    {
        FlowStreamPatternSequence sequence = Assert.Single(
            Analyze(PathFromHeadings(0, 30, 60, 60, 90, 120)).Sequences);

        Assert.Equal(5, sequence.PivotCount);
        Assert.Equal(4, sequence.FlowPivotCount);
        Assert.Equal(0.8, sequence.FlowPivotRatio, 10);
    }

    [Fact]
    public void FlowPivotRatioBoundaryIsInclusive()
    {
        var threshold = new FlowStreamPatternOptions(MinimumFlowPivotRatio: 0.8);
        var above = threshold with { MinimumFlowPivotRatio = 0.8001 };
        IReadOnlyList<(int X, int Y)> positions = PathFromHeadings(0, 30, 60, 60, 90, 120);

        Assert.Single(Analyze(positions, options: threshold).Sequences);
        FlowStreamPatternSequence rejected = Assert.Single(Analyze(positions, options: above).Runs);
        Assert.Equal("TooFewFlowPivots", rejected.Reason);
    }

    [Fact]
    public void TurnDirectionConsistencyBoundaryIsInclusive()
    {
        IReadOnlyList<(int X, int Y)> positions = PathFromHeadings(0, 30, 60, 90, 60);
        var threshold = new FlowStreamPatternOptions(MinimumTurnDirectionConsistency: 0.75);
        var above = threshold with { MinimumTurnDirectionConsistency = 0.7501 };

        FlowStreamPatternSequence accepted = Assert.Single(Analyze(positions, options: threshold).Sequences);
        Assert.Equal(0.75, accepted.TurnDirectionConsistency, 10);
        FlowStreamPatternSequence rejected = Assert.Single(Analyze(positions, options: above).Runs);
        Assert.Equal("InconsistentTurnDirection", rejected.Reason);
    }

    [Fact]
    public void NearZeroDistanceDoesNotCreateFalseFlow()
    {
        var positions = Path(6, 30).ToArray();
        positions[3] = positions[2];

        FlowStreamPatternSequence run = Assert.Single(Analyze(positions).Runs);

        Assert.False(run.Qualified);
        Assert.Equal("InsufficientSpatialMovement", run.Reason);
    }

    [Fact]
    public void MinimumLinkDistanceBoundaryAt18PixelsIsInclusive()
    {
        IReadOnlyList<(int X, int Y)> positions =
        [
            (0, 0),
            (18, 0),
            (18, 18),
            (0, 18),
            (0, 0),
            (18, 0)
        ];

        FlowStreamPatternSequence sequence = Assert.Single(Analyze(positions).Sequences);

        Assert.Equal(18, sequence.MinimumDistance, 10);
        Assert.All(sequence.Distances, distance => Assert.True(distance >= 18));
    }

    [Fact]
    public void LinkJustBelow18PixelsIsRejected()
    {
        IReadOnlyList<(int X, int Y)> positions =
        [
            (0, 0),
            (16, 8),
            (8, 24),
            (-8, 16),
            (0, 0),
            (16, 8)
        ];

        FlowStreamPatternSequence run = Assert.Single(Analyze(positions).Runs);

        Assert.InRange(run.MinimumDistance, 17.88, 17.90);
        Assert.False(run.Qualified);
        Assert.Equal("InsufficientSpatialMovement", run.Reason);
    }

    [Fact]
    public void ValidatedReignOfFearCompactArcRemainsAcceptedAtDefaultDistance()
    {
        IReadOnlyList<(int X, int Y)> positions =
        [
            (130, 104),
            (131, 84),
            (138, 67),
            (151, 53),
            (168, 43),
            (186, 40),
            (205, 44),
            (222, 53)
        ];

        FlowStreamPatternSequence sequence = Assert.Single(Analyze(positions).Sequences);

        Assert.InRange(sequence.MinimumDistance, 18.24, 18.26);
        Assert.Equal(1, sequence.FlowPivotRatio, 10);
        Assert.Equal(1, sequence.TurnDirectionConsistency, 10);
        Assert.InRange(sequence.CurvatureConsistency, 0.74, 0.76);
    }

    [Theory]
    [InlineData(9, "Short")]
    [InlineData(17, "Long")]
    [InlineData(33, "Deathstream")]
    public void FlowCanCoexistWithLengthPatterns(int objectCount, string expectedPattern)
    {
        Beatmap map = Map(Path(objectCount, 20), Repeat(80, objectCount - 1));

        Assert.Single(StreamPatternAnalyzer.AnalyzeFlowStream(map.HitObjects).Sequences);
        Assert.Equal(1, expectedPattern switch
        {
            "Short" => StreamPatternAnalyzer.AnalyzeShortStream(map.HitObjects).Sequences.Count,
            "Long" => StreamPatternAnalyzer.AnalyzeLongStream(map.HitObjects).Sequences.Count,
            _ => StreamPatternAnalyzer.AnalyzeDeathstream(map.HitObjects).Sequences.Count
        });
    }

    [Fact]
    public void FlowCanCoexistWithSpeedAndIrregularStreams()
    {
        Beatmap speed = Map(Path(7, 25), Repeat(75, 6));
        Beatmap irregular = Map(Path(8, 25), new[] { 70, 95, 68, 98, 72, 96, 69 });

        Assert.Single(StreamPatternAnalyzer.AnalyzeFlowStream(speed.HitObjects).Sequences);
        Assert.Single(StreamPatternAnalyzer.AnalyzeSpeedStream(speed.HitObjects).Sequences);
        Assert.Single(StreamPatternAnalyzer.AnalyzeFlowStream(irregular.HitObjects).Sequences);
        Assert.Single(StreamPatternAnalyzer.AnalyzeIrregularStream(irregular.HitObjects).Sequences);
    }

    [Fact]
    public void StaminaAnalysisRemainsUnchanged()
    {
        Beatmap map = TwoRunMap(Path(12, 20), Path(12, -20), 500);
        string before = System.Text.Json.JsonSerializer.Serialize(
            StreamPatternAnalyzer.AnalyzeStaminaStream(map.HitObjects));

        FlowStreamPatternAnalysis flow = StreamPatternAnalyzer.AnalyzeFlowStream(map.HitObjects);

        Assert.Equal(2, flow.FlowStreamSequenceCount);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(
            StreamPatternAnalyzer.AnalyzeStaminaStream(map.HitObjects)));
    }

    [Fact]
    public void FlowScoreConfidenceAndDescriptiveMetricsAreBounded()
    {
        foreach (FlowStreamPatternAnalysis result in new[]
        {
            Analyze(PathFromHeadings(0, 0, 0, 0, 0)),
            Analyze(Path(6, 30)),
            Analyze(PathFromHeadings(0, 30, 60, 60, 90, 120))
        })
        {
            Assert.InRange(result.Confidence, 0, 1);
            Assert.All(result.Runs, run =>
            {
                Assert.InRange(run.FlowScore, 0, 1);
                Assert.InRange(run.SpacingConsistency, 0, 1);
                Assert.InRange(run.CurvatureConsistency, 0, 1);
            });
        }
    }

    [Fact]
    public void CoverageCountsUniqueObjectsFromQualifiedRunsOnly()
    {
        Beatmap map = TwoRunMap(Path(6, 30), PathFromHeadings(0, 0, 0, 0, 0), 200);

        FlowStreamPatternAnalysis result = StreamPatternAnalyzer.AnalyzeFlowStream(map.HitObjects);

        Assert.Equal(2, result.RapidRunCount);
        Assert.Equal(1, result.FlowStreamSequenceCount);
        Assert.Equal(6, result.FlowStreamObjectCount);
        Assert.Equal(0.5, result.Coverage, 10);
    }

    [Fact]
    public void AnalysisDoesNotChangeGameplayIdentityTechRatingUiOrJumpPatterns()
    {
        Beatmap map = Map(Path(17, 20), Repeat(80, 16));
        GameplayProfile profile = GameplayAnalyzer.Analyze(map);
        string profileBefore = System.Text.Json.JsonSerializer.Serialize(profile);
        double rating = map.BeatInsightRating;
        var jumpsBefore = JumpPatternAnalyzer.Analyze(map.HitObjects, profile);

        FlowStreamPatternAnalysis first = StreamPatternAnalyzer.AnalyzeFlowStream(map.HitObjects);
        FlowStreamPatternAnalysis second = StreamPatternAnalyzer.AnalyzeFlowStream(map.HitObjects);

        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(first),
            System.Text.Json.JsonSerializer.Serialize(second));
        Assert.Equal(profileBefore, System.Text.Json.JsonSerializer.Serialize(profile));
        Assert.Equal(rating, map.BeatInsightRating);
        Assert.Equal(jumpsBefore, JumpPatternAnalyzer.Analyze(map.HitObjects, profile));
        Assert.Same(profile, map.GameplayProfile);
    }

    [Fact]
    public void UsesExistingAngleConventionAndExpectedDefaults()
    {
        var options = new FlowStreamPatternOptions();
        FlowStreamPatternSequence linear = Assert.Single(Analyze(PathFromHeadings(0, 0, 0, 0, 0)).Runs);
        FlowStreamPatternSequence reversal = Assert.Single(Analyze(PathFromHeadings(0, 180, 0, 180, 0)).Runs);

        Assert.All(linear.TurnAngles, angle => Assert.Equal(0, angle, 10));
        Assert.All(reversal.TurnAngles, angle => Assert.Equal(180, angle, 10));
        Assert.Equal(6, options.MinimumFlowStreamObjects);
        Assert.Equal(15, options.MinimumFlowTurnAngle);
        Assert.Equal(110, options.MaximumFlowTurnAngle);
        Assert.Equal(18, options.MinimumFlowLinkDistance);
        Assert.Equal(0.70, options.MinimumFlowPivotRatio);
        Assert.Equal(0.75, options.MinimumTurnDirectionConsistency);
        Assert.False(GameplayDebug.FlowStreamPatternEnabled);
        Assert.False(GameplayDebug.FlowStreamPatternDetailedEnabled);
        GameplayDebug.FlowStreamPattern(Analyze(Path(6, 30)));
    }

    private static FlowStreamPatternAnalysis Analyze(
        IReadOnlyList<(int X, int Y)> positions,
        IReadOnlyList<int>? intervals = null,
        FlowStreamPatternOptions? options = null) =>
        StreamPatternAnalyzer.AnalyzeFlowStream(
            Map(positions, intervals ?? Repeat(80, positions.Count - 1)).HitObjects,
            options);

    private static Beatmap Map(IReadOnlyList<(int X, int Y)> positions, IReadOnlyList<int> intervals)
    {
        var map = new Beatmap();
        AddRun(map, positions, intervals, 0);
        return map;
    }

    private static Beatmap TwoRunMap(
        IReadOnlyList<(int X, int Y)> first,
        IReadOnlyList<(int X, int Y)> second,
        int gap)
    {
        var map = new Beatmap();
        int time = AddRun(map, first, Repeat(80, first.Count - 1), 0);
        AddRun(map, second, Repeat(80, second.Count - 1), time + gap);
        return map;
    }

    private static int AddRun(
        Beatmap map,
        IReadOnlyList<(int X, int Y)> positions,
        IReadOnlyList<int> intervals,
        int startTime)
    {
        int time = startTime;
        map.HitObjects.Add(new HitObject { Type = 1, Time = time, X = positions[0].X, Y = positions[0].Y });
        for (int index = 1; index < positions.Count; index++)
        {
            time += intervals[index - 1];
            map.HitObjects.Add(new HitObject
            {
                Type = 1,
                Time = time,
                X = positions[index].X,
                Y = positions[index].Y
            });
        }
        return time;
    }

    private static IReadOnlyList<(int X, int Y)> Path(int objectCount, double turnDegrees) =>
        PathFromHeadings(Enumerable.Range(0, objectCount - 1)
            .Select(index => index * turnDegrees)
            .ToArray());

    private static IReadOnlyList<(int X, int Y)> PathFromHeadings(params double[] headings)
    {
        var positions = new List<(int X, int Y)> { (0, 0) };
        double x = 0;
        double y = 0;
        foreach (double heading in headings)
        {
            double radians = heading * Math.PI / 180;
            x += 50 * Math.Cos(radians);
            y += 50 * Math.Sin(radians);
            positions.Add(((int)Math.Round(x), (int)Math.Round(y)));
        }
        return positions;
    }

    private static int[] Repeat(int value, int count) => Enumerable.Repeat(value, count).ToArray();
}
