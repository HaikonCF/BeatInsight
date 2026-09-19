using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.JumpPatterns;

namespace BeatInsight.Analysis.JumpPatterns;

public static partial class JumpPatternAnalyzer
{
    // Geometric template limits only; they are deliberately independent from Sharp Jump.
    private const double MinimumTriangleJumpDistance = 60;
    private const double MinimumTriangleOppositeDistance = 60;
    private const double MinimumTriangleHeight = 30;
    private const double MinimumTriangleAngle = 30;
    private const double MaximumTriangleAngle = 155;
    private const double MinimumTriangleSideBalance = 0.25;
    private const int MinimumTrianglePoints = 2;
    private const int TriangleConfidenceFullLength = 8;

    public static TriangleJumpAnalysis AnalyzeTriangle(IReadOnlyList<HitObject> objects, GameplayProfile profile)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(profile);

        var streamObjects = BuildStreamMask(objects.Count, profile.StreamSequences);
        var candidates = new List<TriangleJumpPoint>();
        for (int i = 1; i + 1 < objects.Count; i++)
        {
            var previous = objects[i - 1];
            var current = objects[i];
            var next = objects[i + 1];
            if ((previous.Type & 1) == 0 || (current.Type & 1) == 0 || (next.Type & 1) == 0
                || streamObjects[i - 1] || streamObjects[i] || streamObjects[i + 1]
                || !GameplayAnalyzer.IsJumpLink(previous, current, out double previousDistance)
                || !GameplayAnalyzer.IsJumpLink(current, next, out double nextDistance)
                || previousDistance < MinimumTriangleJumpDistance || nextDistance < MinimumTriangleJumpDistance)
                continue;

            double oppositeDistance = Distance(previous, next);
            if (oppositeDistance < MinimumTriangleOppositeDistance)
                continue;

            double angle = GameplayAnalyzer.GetTurnAngle(previous, current, next);
            if (angle < MinimumTriangleAngle || angle > MaximumTriangleAngle)
                continue;

            // Twice the triangle area is the cross product magnitude. Dividing by AC yields
            // the altitude from B to AC, avoiding almost-collinear and micro-triangle matches.
            double area2 = Math.Abs(((double)current.X - previous.X) * (next.Y - previous.Y)
                - ((double)current.Y - previous.Y) * (next.X - previous.X));
            double height = area2 / oppositeDistance;
            double sideBalance = Math.Min(previousDistance, Math.Min(nextDistance, oppositeDistance))
                / Math.Max(previousDistance, Math.Max(nextDistance, oppositeDistance));
            if (height < MinimumTriangleHeight || sideBalance < MinimumTriangleSideBalance)
                continue;

            candidates.Add(new(i, current.Time, previous.X, previous.Y, current.X, current.Y,
                next.X, next.Y, previousDistance, nextDistance, oppositeDistance, angle, height, sideBalance));
        }

        var matches = new List<JumpPatternMatch>();
        var retained = new List<TriangleJumpPoint>();
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
            if (pointCount >= MinimumTrianglePoints)
            {
                TriangleJumpPoint first = candidates[start];
                TriangleJumpPoint last = candidates[end];
                int objectCount = pointCount + 2;
                var sequence = candidates.Skip(start).Take(pointCount).ToArray();
                matches.Add(new(JumpPatternType.Triangle, first.ObjectIndex - 1, last.ObjectIndex + 1,
                    objects[first.ObjectIndex - 1].Time, objects[last.ObjectIndex + 1].Time,
                    TriangleConfidence(pointCount, objectCount, sequence.Average(point => point.Height), objects.Count)));
                retained.AddRange(sequence);
                for (int index = first.ObjectIndex - 1; index <= last.ObjectIndex + 1; index++)
                    involved.Add(index);
                longest = Math.Max(longest, objectCount);
            }
            start = end + 1;
        }

        double averageAngle = retained.Count == 0 ? 0 : retained.Average(point => point.Angle);
        double averageHeight = retained.Count == 0 ? 0 : retained.Average(point => point.Height);
        var result = new TriangleJumpAnalysis(objects.Count, involved.Count, longest, averageAngle,
            averageHeight, TriangleConfidence(retained.Count, longest, averageHeight, objects.Count),
            matches.ToArray(), retained.ToArray());
        GameplayDebug.TriangleJump(result, GameplayDebug.TriangleJumpEnabled, GameplayDebug.TriangleJumpDetailedEnabled);
        return result;
    }

    private static double Distance(HitObject left, HitObject right)
    {
        double x = right.X - left.X;
        double y = right.Y - left.Y;
        return Math.Sqrt(x * x + y * y);
    }

    private static double TriangleConfidence(int pointCount, int longest, double averageHeight, int totalObjects)
    {
        if (pointCount == 0 || totalObjects == 0) return 0;
        double frequency = (double)pointCount / Math.Max(1, totalObjects - 2);
        double length = Math.Min(1, (double)longest / TriangleConfidenceFullLength);
        double geometry = Math.Min(1, averageHeight / (MinimumTriangleHeight * 3));
        return Math.Clamp((frequency + length + geometry) / 3, 0, 1);
    }
}
