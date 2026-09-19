using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.JumpPatterns;

namespace BeatInsight.Analysis.JumpPatterns;

public static partial class JumpPatternAnalyzer
{
    // This band is intentionally disjoint from Sharp (>= 120°), leaving 116°-119° neutral.
    private const double MinimumWideAngle = 60;
    private const double MaximumWideAngle = 115;
    private const double MinimumWideJumpDistance = 60;
    private const int MinimumWidePoints = 2;
    private const int WideConfidenceFullLength = 8;

    public static WideAngleJumpAnalysis AnalyzeWideAngle(IReadOnlyList<HitObject> objects,
        GameplayProfile profile)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(profile);

        var streamObjects = BuildStreamMask(objects.Count, profile.StreamSequences);
        var candidates = new List<WideAngleJumpPoint>();
        for (int i = 1; i + 1 < objects.Count; i++)
        {
            HitObject previous = objects[i - 1];
            HitObject current = objects[i];
            HitObject next = objects[i + 1];
            if ((previous.Type & 1) == 0 || (current.Type & 1) == 0 || (next.Type & 1) == 0
                || streamObjects[i - 1] || streamObjects[i] || streamObjects[i + 1]
                || !GameplayAnalyzer.IsJumpLink(previous, current, out double previousDistance)
                || !GameplayAnalyzer.IsJumpLink(current, next, out double nextDistance)
                || previousDistance < MinimumWideJumpDistance || nextDistance < MinimumWideJumpDistance)
                continue;

            double angle = GameplayAnalyzer.GetTurnAngle(previous, current, next);
            if (angle < MinimumWideAngle || angle > MaximumWideAngle)
                continue;

            candidates.Add(new(i, current.Time, previous.X, previous.Y, current.X, current.Y,
                next.X, next.Y, angle, previousDistance, nextDistance));
        }

        var matches = new List<JumpPatternMatch>();
        var retained = new List<WideAngleJumpPoint>();
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
            if (pointCount >= MinimumWidePoints)
            {
                WideAngleJumpPoint[] sequence = candidates.Skip(start).Take(pointCount).ToArray();
                WideAngleJumpPoint first = sequence[0];
                WideAngleJumpPoint last = sequence[^1];
                int objectCount = pointCount + 2;
                double averageAngle = sequence.Average(point => point.Angle);
                matches.Add(new(JumpPatternType.WideAngle, first.ObjectIndex - 1, last.ObjectIndex + 1,
                    objects[first.ObjectIndex - 1].Time, objects[last.ObjectIndex + 1].Time,
                    WideConfidence(pointCount, objectCount, averageAngle,
                        sequence.Min(point => point.Angle), sequence.Max(point => point.Angle), objects.Count)));
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
        double averageDistance = retained.Count == 0 ? 0 : retained.Average(point =>
            (point.PreviousDistance + point.NextDistance) / 2);
        var result = new WideAngleJumpAnalysis(objects.Count, involved.Count, longest,
            averageAngleResult, minimumAngle, maximumAngle, averageDistance,
            WideConfidence(retained.Count, longest, averageAngleResult, minimumAngle, maximumAngle, objects.Count),
            matches.ToArray(), retained.ToArray());
        GameplayDebug.WideAngleJump(result, GameplayDebug.WideAngleJumpEnabled,
            GameplayDebug.WideAngleJumpDetailedEnabled);
        return result;
    }

    private static double WideConfidence(int pointCount, int longest, double averageAngle,
        double minimumAngle, double maximumAngle, int totalObjects)
    {
        if (pointCount == 0 || totalObjects == 0) return 0;
        double frequency = (double)pointCount / Math.Max(1, totalObjects - 2);
        double length = Math.Min(1, (double)longest / WideConfidenceFullLength);
        double center = (MinimumWideAngle + MaximumWideAngle) / 2;
        double halfWidth = (MaximumWideAngle - MinimumWideAngle) / 2;
        double centerCloseness = Math.Clamp(1 - Math.Abs(averageAngle - center) / halfWidth, 0, 1);
        double coherence = Math.Clamp(1 - (maximumAngle - minimumAngle)
            / (MaximumWideAngle - MinimumWideAngle), 0, 1);
        return Math.Clamp((frequency + length + centerCloseness + coherence) / 4, 0, 1);
    }
}
