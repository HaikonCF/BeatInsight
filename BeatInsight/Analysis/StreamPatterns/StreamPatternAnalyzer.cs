using BeatInsight.Models;
using BeatInsight.Models.StreamPatterns;

namespace BeatInsight.Analysis.StreamPatterns;

/// <summary>
/// Entry point for descriptive temporal Stream-family patterns.
/// It consumes existing hitobject timing and does not mutate GameplayProfile or Beatmap.
/// </summary>
public static partial class StreamPatternAnalyzer
{
    public static BurstPatternAnalysis AnalyzeBurst(
        IReadOnlyList<HitObject> objects,
        BurstPatternOptions? options = null) =>
        AnalyzeBurstCore(objects, options ?? new BurstPatternOptions());

    public static ShortStreamPatternAnalysis AnalyzeShortStream(
        IReadOnlyList<HitObject> objects,
        ShortStreamPatternOptions? options = null) =>
        AnalyzeShortStreamCore(objects, options ?? new ShortStreamPatternOptions());

    public static LongStreamPatternAnalysis AnalyzeLongStream(
        IReadOnlyList<HitObject> objects,
        LongStreamPatternOptions? options = null) =>
        AnalyzeLongStreamCore(objects, options ?? new LongStreamPatternOptions());

    public static DeathstreamPatternAnalysis AnalyzeDeathstream(
        IReadOnlyList<HitObject> objects,
        DeathstreamPatternOptions? options = null) =>
        AnalyzeDeathstreamCore(objects, options ?? new DeathstreamPatternOptions());

    public static StaminaStreamPatternAnalysis AnalyzeStaminaStream(
        IReadOnlyList<HitObject> objects,
        StaminaStreamPatternOptions? options = null) =>
        AnalyzeStaminaStreamCore(objects, options ?? new StaminaStreamPatternOptions());

    public static IrregularStreamPatternAnalysis AnalyzeIrregularStream(
        IReadOnlyList<HitObject> objects,
        IrregularStreamPatternOptions? options = null) =>
        AnalyzeIrregularStreamCore(objects, options ?? new IrregularStreamPatternOptions());

    public static SpeedStreamPatternAnalysis AnalyzeSpeedStream(
        IReadOnlyList<HitObject> objects,
        SpeedStreamPatternOptions? options = null) =>
        AnalyzeSpeedStreamCore(objects, options ?? new SpeedStreamPatternOptions());

    public static FlowStreamPatternAnalysis AnalyzeFlowStream(
        IReadOnlyList<HitObject> objects,
        FlowStreamPatternOptions? options = null) =>
        AnalyzeFlowStreamCore(objects, options ?? new FlowStreamPatternOptions());

    public static AlternatingStreamPatternAnalysis AnalyzeAlternatingStream(
        IReadOnlyList<HitObject> objects,
        AlternatingStreamPatternOptions? options = null) =>
        AnalyzeAlternatingStreamCore(objects, options ?? new AlternatingStreamPatternOptions());

    private static IReadOnlyList<RapidStreamRun> SegmentRapidCircleRuns(
        IReadOnlyList<HitObject> objects)
    {
        var runs = new List<RapidStreamRun>();
        int start = 0;
        while (start < objects.Count)
        {
            if (!IsCircle(objects[start]))
            {
                start++;
                continue;
            }

            int end = start;
            while (end + 1 < objects.Count
                && IsCircle(objects[end + 1])
                && GameplayAnalyzer.IsStreamTimingInterval(objects[end + 1].Time - objects[end].Time))
            {
                end++;
            }

            if (end > start)
            {
                double[] intervals = Enumerable.Range(start + 1, end - start)
                    .Select(index => (double)(objects[index].Time - objects[index - 1].Time))
                    .ToArray();
                runs.Add(new(start, end, objects[start].Time, objects[end].Time, intervals));
            }

            // Preserve each complete maximal run for exclusive length classification.
            start = end + 1;
        }

        return runs;
    }

    private static bool IsCircle(HitObject hitObject) => (hitObject.Type & 1) == 1;

    private sealed record RapidStreamRun(
        int StartIndex,
        int EndIndex,
        int StartTime,
        int EndTime,
        IReadOnlyList<double> Intervals)
    {
        public int ObjectCount => EndIndex - StartIndex + 1;
        public double Duration => EndTime - StartTime;
        public double AverageInterval => Intervals.Count == 0 ? 0 : Intervals.Average();
        public double IntervalConsistency => Intervals.Count == 0
            ? 0
            : Intervals.Min() / Intervals.Max();
    }
}
