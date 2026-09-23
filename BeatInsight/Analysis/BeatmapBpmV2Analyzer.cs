using BeatInsight.Models;

namespace BeatInsight.Analysis;

public sealed record BeatmapBpmV2(double? PrimaryBpm, double? MinBpm, double? MaxBpm, bool IsVariableBpm)
{
    public static BeatmapBpmV2 Unavailable { get; } = new(null, null, null, false);
}

/// <summary>Descriptive tempo in the playable interval; inherited SV points are never tempos.</summary>
public static class BeatmapBpmV2Analyzer
{
    public static BeatmapBpmV2 Analyze(IReadOnlyList<TimingPoint> timingPoints, double firstObjectTime, double lastObjectEndTime)
    {
        ArgumentNullException.ThrowIfNull(timingPoints);
        if (!double.IsFinite(firstObjectTime) || !double.IsFinite(lastObjectEndTime))
            return BeatmapBpmV2.Unavailable;

        var tempos = timingPoints
            .Where(point => point.Uninherited && double.IsFinite(point.Time)
                && double.IsFinite(point.BeatLength) && point.BeatLength > 0)
            .Select((point, index) => new Tempo(point.Time, Normalize(point.BeatLength), index))
            .Where(point => double.IsFinite(point.BeatLength) && point.BeatLength > 0)
            .OrderBy(point => point.Time)
            .ThenBy(point => point.Index)
            .ToArray();
        if (tempos.Length == 0 || lastObjectEndTime < firstObjectTime)
            return BeatmapBpmV2.Unavailable;

        double start = firstObjectTime;
        double end = lastObjectEndTime;
        double activeBeatLength = double.NaN;
        foreach (Tempo point in tempos)
        {
            if (point.Time > start) break;
            activeBeatLength = point.BeatLength;
        }

        // A zero-duration map uses only the tempo active at the object itself.
        if (end == start)
        {
            if (!double.IsFinite(activeBeatLength)) return BeatmapBpmV2.Unavailable;
            double bpm = 60000 / activeBeatLength;
            return new BeatmapBpmV2(bpm, bpm, bpm, false);
        }

        var durations = new Dictionary<double, (double Duration, double FirstActiveTime)>();
        double segmentStart = start;
        void AddSegment(double segmentEnd)
        {
            if (segmentEnd <= segmentStart) return;
            if (!double.IsFinite(activeBeatLength))
            {
                segmentStart = segmentEnd;
                return;
            }
            if (durations.TryGetValue(activeBeatLength, out var existing))
                durations[activeBeatLength] = (existing.Duration + segmentEnd - segmentStart, existing.FirstActiveTime);
            else
                durations[activeBeatLength] = (segmentEnd - segmentStart, segmentStart);
            segmentStart = segmentEnd;
        }

        foreach (Tempo point in tempos)
        {
            if (point.Time <= start) continue;
            if (point.Time >= end) break;
            AddSegment(point.Time);
            activeBeatLength = point.BeatLength;
        }
        AddSegment(end);

        if (durations.Count == 0) return BeatmapBpmV2.Unavailable;

        double primaryBeatLength = durations
            .OrderByDescending(item => item.Value.Duration)
            .ThenBy(item => item.Value.FirstActiveTime)
            .First().Key;
        double[] values = durations.Keys.Select(length => 60000 / length).ToArray();
        return new BeatmapBpmV2(60000 / primaryBeatLength, values.Min(), values.Max(), durations.Count > 1);
    }

    // Match osu!'s 0.001 ms beat-length grouping so negligible parser noise does not imply a tempo change.
    private static double Normalize(double beatLength) => Math.Round(beatLength * 1000) / 1000;

    private readonly record struct Tempo(double Time, double BeatLength, int Index);
}
