using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.Speed;

namespace BeatInsight.Analysis.Speed;

/// <summary>
/// Equally weighted mean temporal speed of valid sliding triplets. Circle and
/// slider starts are measured; a spinner breaks the measurement. Long positive
/// gaps remain valid and naturally map toward zero.
/// </summary>
public static class GlobalTripletSpeedAnalyzer
{
    public const double SlowIntervalMs = 250;
    public const double FastIntervalMs = 50;
    public const string GapPolicy = "All positive gaps included; mapping saturates at zero";
    public const string SpinnerPolicy = "Triplets containing a spinner are excluded";

    public static GlobalTripletSpeedAnalysis Analyze(Beatmap beatmap, bool includeDiagnostics = false)
    {
        ArgumentNullException.ThrowIfNull(beatmap);
        return Analyze(beatmap.HitObjects, includeDiagnostics);
    }

    public static GlobalTripletSpeedAnalysis Analyze(IReadOnlyList<HitObject> objects, bool includeDiagnostics = false)
    {
        ArgumentNullException.ThrowIfNull(objects);
        int total = Math.Max(0, objects.Count - 2);
        if (total == 0)
        {
            GlobalTripletSpeedAnalysis empty = GlobalTripletSpeedAnalysis.Empty with { HitObjectCount = objects.Count };
            GameplayDebug.GlobalTripletSpeed(empty);
            return empty;
        }

        var intervals = new List<double>(total);
        List<GlobalTripletSpeedDiagnostic>? diagnostics = includeDiagnostics ? new(total) : null;
        double speedSum = 0, consistencySum = 0, intervalSum = 0;
        double min = double.PositiveInfinity, max = double.NegativeInfinity;

        for (int index = 0; index + 2 < objects.Count; index++)
        {
            HitObject first = objects[index], second = objects[index + 1], third = objects[index + 2];
            double delta1 = second.Time - (double)first.Time;
            double delta2 = third.Time - (double)second.Time;
            string reason = ExclusionReason(first, second, third, delta1, delta2);
            if (reason.Length != 0)
            {
                diagnostics?.Add(new(index, index + 1, index + 2, delta1, delta2, 0, 0, 0, false, reason));
                continue;
            }

            double interval = (delta1 + delta2) / 2;
            double localSpeed = IntervalToSpeedPercent(interval);
            double consistency = Math.Min(delta1, delta2) / Math.Max(delta1, delta2);
            intervals.Add(interval);
            intervalSum += interval;
            speedSum += localSpeed;
            consistencySum += consistency;
            min = Math.Min(min, interval);
            max = Math.Max(max, interval);
            diagnostics?.Add(new(index, index + 1, index + 2, delta1, delta2, interval, localSpeed, consistency, true, string.Empty));
        }

        int valid = intervals.Count;
        if (valid == 0)
        {
            var empty = GlobalTripletSpeedAnalysis.Empty with
            {
                TotalTripletCount = total,
                HitObjectCount = objects.Count,
                ExcludedTripletCount = total,
                Diagnostics = diagnostics is null ? Array.Empty<GlobalTripletSpeedDiagnostic>() : diagnostics
            };
            GameplayDebug.GlobalTripletSpeed(empty);
            return empty;
        }

        intervals.Sort();
        double averageInterval = intervalSum / valid;
        var result = new GlobalTripletSpeedAnalysis(
            speedSum / valid, averageInterval, objects.Count, total, valid, total - valid,
            min, max, Median(intervals), 1 - Math.Exp(-valid / 20.0),
            60000 / (averageInterval * 4), consistencySum / valid,
            diagnostics is null ? Array.Empty<GlobalTripletSpeedDiagnostic>() : diagnostics);
        GameplayDebug.GlobalTripletSpeed(result);
        return result;
    }

    public static double IntervalToSpeedPercent(double intervalMs)
    {
        if (!double.IsFinite(intervalMs) || intervalMs <= 0) return 0;
        return 100 * Math.Clamp((SlowIntervalMs - intervalMs) / (SlowIntervalMs - FastIntervalMs), 0, 1);
    }

    private static string ExclusionReason(HitObject first, HitObject second, HitObject third, double delta1, double delta2)
    {
        if (IsSpinner(first) || IsSpinner(second) || IsSpinner(third)) return "SpinnerBreak";
        if (!IsTappingObject(first) || !IsTappingObject(second) || !IsTappingObject(third)) return "UnsupportedObjectType";
        if (!double.IsFinite(delta1) || !double.IsFinite(delta2)) return "NonFiniteTimestamp";
        if (delta1 <= 0 || delta2 <= 0) return "NonIncreasingTimestamp";
        return string.Empty;
    }

    private static bool IsSpinner(HitObject hitObject) => (hitObject.Type & 8) != 0;
    private static bool IsTappingObject(HitObject hitObject) => (hitObject.Type & (1 | 2)) != 0;
    private static double Median(IReadOnlyList<double> sorted) => sorted.Count % 2 == 1
        ? sorted[sorted.Count / 2]
        : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
}
