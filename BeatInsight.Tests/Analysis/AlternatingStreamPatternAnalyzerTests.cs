using BeatInsight.Analysis;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Analysis.StreamPatterns;
using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.StreamPatterns;

namespace BeatInsight.Tests.Analysis;

public sealed class AlternatingStreamPatternAnalyzerTests
{
    [Fact]
    public void RunBelowMinimumLength_IsRejected()
    {
        AlternatingStreamPatternSequence run = Assert.Single(
            Analyze(PathFromHeadings(30, -30, 30, -30)).Runs);

        Assert.False(run.Qualified);
        Assert.Equal("TooShort", run.Reason);
    }

    [Fact]
    public void LinearStream_IsRejected()
    {
        AlternatingStreamPatternSequence run = Assert.Single(
            Analyze(PathFromHeadings(0, 0, 0, 0, 0)).Runs);

        Assert.Equal(0, run.ValidAlternatingPivotRatio);
        Assert.Equal("TooFewValidPivots", run.Reason);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void PerfectAlternatingStreamInEitherStartingDirection_IsAccepted(int direction)
    {
        AlternatingStreamPatternSequence sequence = Assert.Single(
            Analyze(Zigzag(6, direction)).Sequences);

        Assert.Equal(4, sequence.ValidAlternatingPivotCount);
        Assert.Equal(3, sequence.AlternationCount);
        Assert.Equal(1, sequence.AlternationRatio, 10);
    }

    [Fact]
    public void ArcIsFlowAndNotAlternating()
    {
        Beatmap map = Map(Curve(6, 30), Repeat(80, 5));

        Assert.Single(StreamPatternAnalyzer.AnalyzeFlowStream(map.HitObjects).Sequences);
        Assert.Empty(StreamPatternAnalyzer.AnalyzeAlternatingStream(map.HitObjects).Sequences);
    }

    [Fact]
    public void ZigzagIsAlternatingAndNotFlow()
    {
        Beatmap map = Map(Zigzag(6), Repeat(80, 5));

        Assert.Single(StreamPatternAnalyzer.AnalyzeAlternatingStream(map.HitObjects).Sequences);
        Assert.Empty(StreamPatternAnalyzer.AnalyzeFlowStream(map.HitObjects).Sequences);
    }

    [Fact]
    public void ReversalHeavyStream_IsRejected()
    {
        AlternatingStreamPatternSequence run = Assert.Single(
            Analyze(PathFromHeadings(0, 180, 0, 180, 0)).Runs);

        Assert.All(run.TurnAngles, angle => Assert.InRange(angle, 179.9, 180));
        Assert.Equal("TooFewValidPivots", run.Reason);
    }

    [Fact]
    public void ANoisyStraightPivotIsTolerated()
    {
        AlternatingStreamPatternSequence sequence = Assert.Single(
            Analyze(PathFromHeadings(0, 30, 0, 30, 30, 0)).Sequences);

        Assert.Equal(5, sequence.PivotCount);
        Assert.Equal(4, sequence.ValidAlternatingPivotCount);
        Assert.Equal(0.8, sequence.ValidAlternatingPivotRatio, 10);
        Assert.Equal(1, sequence.AlternationRatio, 10);
    }

    [Fact]
    public void ValidAlternatingPivotRatioBoundaryIsInclusive()
    {
        IReadOnlyList<(int X, int Y)> positions = PathFromHeadings(0, 30, 0, 30, 30);
        var threshold = new AlternatingStreamPatternOptions(MinimumValidAlternatingPivotRatio: 0.75);
        var above = threshold with { MinimumValidAlternatingPivotRatio = 0.7501 };

        AlternatingStreamPatternSequence accepted = Assert.Single(Analyze(positions, options: threshold).Sequences);
        Assert.Equal(0.75, accepted.ValidAlternatingPivotRatio, 10);
        AlternatingStreamPatternSequence rejected = Assert.Single(Analyze(positions, options: above).Runs);
        Assert.Equal("TooFewValidPivots", rejected.Reason);
    }

    [Fact]
    public void AlternationRatioBoundaryIsInclusive()
    {
        IReadOnlyList<(int X, int Y)> positions = PathFromHeadings(0, 30, 0, 30, 60, 30);
        var threshold = new AlternatingStreamPatternOptions(MinimumAlternationRatio: 0.75);
        var above = threshold with { MinimumAlternationRatio = 0.7501 };

        AlternatingStreamPatternSequence accepted = Assert.Single(Analyze(positions, options: threshold).Sequences);
        Assert.Equal(5, accepted.ValidAlternatingPivotCount);
        Assert.Equal(3, accepted.AlternationCount);
        Assert.Equal(4, accepted.PossibleAlternations);
        Assert.Equal(0.75, accepted.AlternationRatio, 10);
        AlternatingStreamPatternSequence rejected = Assert.Single(Analyze(positions, options: above).Runs);
        Assert.Equal("InsufficientAlternation", rejected.Reason);
    }

    [Fact]
    public void PartialAlternationHasMathematicallyExactRatio()
    {
        AlternatingStreamPatternSequence run = Assert.Single(
            Analyze(PathFromHeadings(0, 30, 0, 30, 60, 30)).Runs);

        Assert.Equal(new[] { 1, -1, 1, 1, -1 },
            run.Pivots.Where(pivot => pivot.IsValidAlternatingPivot).Select(pivot => pivot.TurnSign));
        Assert.Equal(3, run.AlternationCount);
        Assert.Equal(4, run.PossibleAlternations);
        Assert.Equal(0.75, run.AlternationRatio, 10);
        Assert.True(run.Qualified);
    }

    [Fact]
    public void DistanceBelowMinimum_IsRejectedCleanly()
    {
        var positions = Zigzag(6).ToArray();
        positions[3] = positions[2];

        AlternatingStreamPatternSequence run = Assert.Single(Analyze(positions).Runs);

        Assert.False(run.Qualified);
        Assert.Equal("InsufficientSpatialMovement", run.Reason);
    }

    [Theory]
    [InlineData(9, "Short")]
    [InlineData(17, "Long")]
    [InlineData(33, "Deathstream")]
    public void AlternatingCanCoexistWithLengthPatterns(int objectCount, string expectedPattern)
    {
        Beatmap map = Map(Zigzag(objectCount), Repeat(80, objectCount - 1));

        Assert.Single(StreamPatternAnalyzer.AnalyzeAlternatingStream(map.HitObjects).Sequences);
        Assert.Equal(1, expectedPattern switch
        {
            "Short" => StreamPatternAnalyzer.AnalyzeShortStream(map.HitObjects).Sequences.Count,
            "Long" => StreamPatternAnalyzer.AnalyzeLongStream(map.HitObjects).Sequences.Count,
            _ => StreamPatternAnalyzer.AnalyzeDeathstream(map.HitObjects).Sequences.Count
        });
    }

    [Fact]
    public void RegularSpeedZigzagIsSpeedAlternatingAndNotIrregular()
    {
        Beatmap map = Map(Zigzag(7), Repeat(70, 6));

        Assert.Single(StreamPatternAnalyzer.AnalyzeAlternatingStream(map.HitObjects).Sequences);
        Assert.Single(StreamPatternAnalyzer.AnalyzeSpeedStream(map.HitObjects).Sequences);
        Assert.Empty(StreamPatternAnalyzer.AnalyzeIrregularStream(map.HitObjects).Sequences);
    }

    [Fact]
    public void IrregularArcIsIrregularFlowAndNotAlternating()
    {
        Beatmap map = Map(Curve(6, 30), new[] { 80, 105, 78, 118, 82 });

        Assert.Single(StreamPatternAnalyzer.AnalyzeIrregularStream(map.HitObjects).Sequences);
        Assert.Single(StreamPatternAnalyzer.AnalyzeFlowStream(map.HitObjects).Sequences);
        Assert.Empty(StreamPatternAnalyzer.AnalyzeAlternatingStream(map.HitObjects).Sequences);
    }

    [Fact]
    public void StaminaAnalysisRemainsUnchanged()
    {
        Beatmap map = TwoRunMap(Zigzag(12), Zigzag(12, -1), 500);
        string before = System.Text.Json.JsonSerializer.Serialize(
            StreamPatternAnalyzer.AnalyzeStaminaStream(map.HitObjects));

        AlternatingStreamPatternAnalysis alternating =
            StreamPatternAnalyzer.AnalyzeAlternatingStream(map.HitObjects);

        Assert.Equal(2, alternating.AlternatingStreamSequenceCount);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(
            StreamPatternAnalyzer.AnalyzeStaminaStream(map.HitObjects)));
    }

    [Fact]
    public void FlowAnalysisRemainsIndependentAndUnchanged()
    {
        Beatmap map = Map(Curve(7, 25), Repeat(80, 6));
        string before = System.Text.Json.JsonSerializer.Serialize(
            StreamPatternAnalyzer.AnalyzeFlowStream(map.HitObjects));

        _ = StreamPatternAnalyzer.AnalyzeAlternatingStream(map.HitObjects);

        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(
            StreamPatternAnalyzer.AnalyzeFlowStream(map.HitObjects)));
    }

    [Fact]
    public void AlternatingScoreConfidenceAndDescriptiveMetricsAreBounded()
    {
        foreach (AlternatingStreamPatternAnalysis result in new[]
        {
            Analyze(PathFromHeadings(0, 0, 0, 0, 0)),
            Analyze(Zigzag(6)),
            Analyze(PathFromHeadings(0, 30, 0, 30, 30, 0))
        })
        {
            Assert.InRange(result.Confidence, 0, 1);
            Assert.All(result.Runs, run =>
            {
                Assert.InRange(run.AlternatingScore, 0, 1);
                Assert.InRange(run.AlternatingAngleConsistency, 0, 1);
                Assert.InRange(run.SpacingConsistency, 0, 1);
            });
        }
    }

    [Fact]
    public void CoverageCountsUniqueObjectsFromQualifiedRunsOnly()
    {
        Beatmap map = TwoRunMap(Zigzag(6), PathFromHeadings(0, 0, 0, 0, 0), 200);

        AlternatingStreamPatternAnalysis result =
            StreamPatternAnalyzer.AnalyzeAlternatingStream(map.HitObjects);

        Assert.Equal(2, result.RapidRunCount);
        Assert.Equal(1, result.AlternatingStreamSequenceCount);
        Assert.Equal(6, result.AlternatingStreamObjectCount);
        Assert.Equal(0.5, result.Coverage, 10);
    }

    [Fact]
    public void AnalysisDoesNotChangeGameplayIdentityTechRatingUiOrJumpPatterns()
    {
        Beatmap map = Map(Zigzag(17), Repeat(80, 16));
        GameplayProfile profile = GameplayAnalyzer.Analyze(map);
        string profileBefore = System.Text.Json.JsonSerializer.Serialize(profile);
        double rating = map.BeatInsightRating;
        var jumpsBefore = JumpPatternAnalyzer.Analyze(map.HitObjects, profile);

        AlternatingStreamPatternAnalysis first =
            StreamPatternAnalyzer.AnalyzeAlternatingStream(map.HitObjects);
        AlternatingStreamPatternAnalysis second =
            StreamPatternAnalyzer.AnalyzeAlternatingStream(map.HitObjects);

        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(first),
            System.Text.Json.JsonSerializer.Serialize(second));
        Assert.Equal(profileBefore, System.Text.Json.JsonSerializer.Serialize(profile));
        Assert.Equal(rating, map.BeatInsightRating);
        Assert.Equal(jumpsBefore, JumpPatternAnalyzer.Analyze(map.HitObjects, profile));
        Assert.Same(profile, map.GameplayProfile);
    }

    [Fact]
    public void UsesFlowGeometryConventionAndExpectedDefaults()
    {
        var options = new AlternatingStreamPatternOptions();
        AlternatingStreamPatternSequence linear = Assert.Single(
            Analyze(PathFromHeadings(0, 0, 0, 0, 0)).Runs);
        AlternatingStreamPatternSequence reversal = Assert.Single(
            Analyze(PathFromHeadings(0, 180, 0, 180, 0)).Runs);

        Assert.All(linear.TurnAngles, angle => Assert.Equal(0, angle, 10));
        Assert.All(reversal.TurnAngles, angle => Assert.Equal(180, angle, 10));
        Assert.Equal(6, options.MinimumAlternatingStreamObjects);
        Assert.Equal(20, options.MinimumAlternatingTurnAngle);
        Assert.Equal(160, options.MaximumAlternatingTurnAngle);
        Assert.Equal(20, options.MinimumAlternatingLinkDistance);
        Assert.Equal(0.70, options.MinimumValidAlternatingPivotRatio);
        Assert.Equal(0.75, options.MinimumAlternationRatio);
        Assert.False(GameplayDebug.AlternatingStreamPatternEnabled);
        Assert.False(GameplayDebug.AlternatingStreamPatternDetailedEnabled);
        GameplayDebug.AlternatingStreamPattern(Analyze(Zigzag(6)));
    }

    private static AlternatingStreamPatternAnalysis Analyze(
        IReadOnlyList<(int X, int Y)> positions,
        IReadOnlyList<int>? intervals = null,
        AlternatingStreamPatternOptions? options = null) =>
        StreamPatternAnalyzer.AnalyzeAlternatingStream(
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

    private static IReadOnlyList<(int X, int Y)> Zigzag(int objectCount, int direction = 1) =>
        PathFromHeadings(Enumerable.Range(0, objectCount - 1)
            .Select(index => direction * (index % 2 == 0 ? 30.0 : -30.0))
            .ToArray());

    private static IReadOnlyList<(int X, int Y)> Curve(int objectCount, double turnDegrees) =>
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
