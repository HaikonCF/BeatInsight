using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.JumpPatterns;

namespace BeatInsight.Analysis.JumpPatterns;

public static partial class JumpPatternAnalyzer
{
    // ReversalTurnDegrees is shared with Sharp's existing diagnostic convention.
    private const double MinimumReversalJumpDistance = 60;
    private const int MinimumReversalPoints = 2;
    private const int ReversalConfidenceFullLength = 8;

    public static ReversalJumpAnalysis AnalyzeReversal(IReadOnlyList<HitObject> objects,
        GameplayProfile profile)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(profile);

        var streamObjects = BuildStreamMask(objects.Count, profile.StreamSequences);
        var candidates = new List<ReversalJumpPoint>();
        for (int i = 1; i + 1 < objects.Count; i++)
        {
            HitObject previous = objects[i - 1];
            HitObject current = objects[i];
            HitObject next = objects[i + 1];
            if ((previous.Type & 1) == 0 || (current.Type & 1) == 0 || (next.Type & 1) == 0
                || streamObjects[i - 1] || streamObjects[i] || streamObjects[i + 1]
                || !GameplayAnalyzer.IsJumpLink(previous, current, out double previousDistance)
                || !GameplayAnalyzer.IsJumpLink(current, next, out double nextDistance)
                || previousDistance < MinimumReversalJumpDistance || nextDistance < MinimumReversalJumpDistance)
                continue;

            double angle = GameplayAnalyzer.GetTurnAngle(previous, current, next);
            if (angle < ReversalTurnDegrees)
                continue;

            double returnDistance = Distance(previous, next);
            candidates.Add(new(i, current.Time, previous.X, previous.Y, current.X, current.Y,
                next.X, next.Y, angle, previousDistance, nextDistance, returnDistance));
        }

        var matches = new List<JumpPatternMatch>();
        var retained = new List<ReversalJumpPoint>();
        var involved = new HashSet<int>();
        int longest = 0;
        int start = 0;
        while (start < candidates.Count)
        {
            int end = start;
            while (end + 1 < candidates.Count
                && candidates[end + 1].ObjectIndex == candidates[end].ObjectIndex + 1)
                end++;

            int pointCount = end - start + 1;
            if (pointCount >= MinimumReversalPoints)
            {
                ReversalJumpPoint[] sequence = candidates.Skip(start).Take(pointCount).ToArray();
                ReversalJumpPoint first = sequence[0];
                ReversalJumpPoint last = sequence[^1];
                int objectCount = pointCount + 2;
                double averageAngle = sequence.Average(point => point.Angle);
                double distanceConsistency = DistanceConsistency(sequence);
                double averageReturnDistance = sequence.Average(point => point.ReturnDistance);
                double averageJumpDistance = sequence.Average(point =>
                    (point.PreviousDistance + point.NextDistance) / 2);
                matches.Add(new(JumpPatternType.Reversal, first.ObjectIndex - 1, last.ObjectIndex + 1,
                    objects[first.ObjectIndex - 1].Time, objects[last.ObjectIndex + 1].Time,
                    ReversalConfidence(pointCount, objectCount, averageAngle, distanceConsistency,
                        averageReturnDistance, averageJumpDistance, objects.Count)));
                retained.AddRange(sequence);
                for (int index = first.ObjectIndex - 1; index <= last.ObjectIndex + 1; index++)
                    involved.Add(index);
                longest = Math.Max(longest, objectCount);
            }
            start = end + 1;
        }

        double averageAngleResult = retained.Count == 0 ? 0 : retained.Average(point => point.Angle);
        double minimumAngle = retained.Count == 0 ? 0 : retained.Min(point => point.Angle);
        double maximumAngle = retained.Count == 0 ? 0 : retained.Max(point => point.Angle);
        double averageJumpDistanceResult = retained.Count == 0 ? 0 : retained.Average(point =>
            (point.PreviousDistance + point.NextDistance) / 2);
        double averageReturnDistanceResult = retained.Count == 0 ? 0 : retained.Average(point => point.ReturnDistance);
        var result = new ReversalJumpAnalysis(objects.Count, involved.Count, longest,
            averageAngleResult, minimumAngle, maximumAngle, averageJumpDistanceResult,
            DistanceConsistency(retained), averageReturnDistanceResult,
            ReversalConfidence(retained.Count, longest, averageAngleResult, DistanceConsistency(retained),
                averageReturnDistanceResult, averageJumpDistanceResult, objects.Count),
            matches.ToArray(), retained.ToArray());
        GameplayDebug.ReversalJump(result, GameplayDebug.ReversalJumpEnabled,
            GameplayDebug.ReversalJumpDetailedEnabled);
        return result;
    }

    private static double DistanceConsistency(IReadOnlyList<ReversalJumpPoint> points)
    {
        if (points.Count == 0) return 0;
        double minimum = points.Min(point => Math.Min(point.PreviousDistance, point.NextDistance));
        double maximum = points.Max(point => Math.Max(point.PreviousDistance, point.NextDistance));
        return maximum <= 0 ? 0 : Math.Clamp(minimum / maximum, 0, 1);
    }

    private static double ReversalConfidence(int pointCount, int longest, double averageAngle,
        double distanceConsistency, double averageReturnDistance, double averageJumpDistance, int totalObjects)
    {
        if (pointCount == 0 || totalObjects == 0) return 0;
        double frequency = (double)pointCount / Math.Max(1, totalObjects - 2);
        double length = Math.Min(1, (double)longest / ReversalConfidenceFullLength);
        double intensity = Math.Clamp((averageAngle - ReversalTurnDegrees) / (180 - ReversalTurnDegrees), 0, 1);
        double returnCloseness = averageJumpDistance <= 0 ? 0 : Math.Clamp(1 - averageReturnDistance / averageJumpDistance, 0, 1);
        return Math.Clamp((frequency + length + intensity + distanceConsistency + returnCloseness) / 5, 0, 1);
    }
}
