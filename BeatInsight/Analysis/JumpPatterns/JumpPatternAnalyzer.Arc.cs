using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.JumpPatterns;

namespace BeatInsight.Analysis.JumpPatterns;

public static partial class JumpPatternAnalyzer
{
    private const double MinimumArcTurnAngle = 20;
    private const double MaximumArcTurnAngle = 110;
    private const double MinimumArcJumpDistance = 60;
    private const int MinimumArcPoints = 3;
    private const double MinimumArcAngleConsistency = .50;
    private const int ArcConfidenceFullLength = 10;

    public static ArcJumpAnalysis AnalyzeArc(IReadOnlyList<HitObject> objects, GameplayProfile profile)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(profile);

        var streamObjects = BuildStreamMask(objects.Count, profile.StreamSequences);
        var candidates = new List<ArcJumpPoint>();
        for (int i = 1; i + 1 < objects.Count; i++)
        {
            HitObject previous = objects[i - 1];
            HitObject current = objects[i];
            HitObject next = objects[i + 1];
            if ((previous.Type & 1) == 0 || (current.Type & 1) == 0 || (next.Type & 1) == 0
                || streamObjects[i - 1] || streamObjects[i] || streamObjects[i + 1]
                || !GameplayAnalyzer.IsJumpLink(previous, current, out double previousDistance)
                || !GameplayAnalyzer.IsJumpLink(current, next, out double nextDistance)
                || previousDistance < MinimumArcJumpDistance || nextDistance < MinimumArcJumpDistance)
                continue;

            double angle = GameplayAnalyzer.GetTurnAngle(previous, current, next);
            if (angle < MinimumArcTurnAngle || angle > MaximumArcTurnAngle)
                continue;

            double sign = SignedTurn(previous, current, next, previousDistance, nextDistance);
            ArcTurnDirection direction = sign > 0 ? ArcTurnDirection.Left
                : sign < 0 ? ArcTurnDirection.Right : ArcTurnDirection.Neutral;
            if (direction == ArcTurnDirection.Neutral)
                continue;

            candidates.Add(new(i, current.Time, previous.X, previous.Y, current.X, current.Y,
                next.X, next.Y, angle, previousDistance, nextDistance, direction));
        }

        var matches = new List<JumpPatternMatch>();
        var retained = new List<ArcJumpPoint>();
        var involved = new HashSet<int>();
        var angleConsistencies = new List<double>();
        var distanceConsistencies = new List<double>();
        var cumulativeTurns = new List<double>();
        int longest = 0;
        int leftSequences = 0;
        int rightSequences = 0;
        int start = 0;
        while (start < candidates.Count)
        {
            int end = start;
            while (end + 1 < candidates.Count
                && candidates[end + 1].ObjectIndex == candidates[end].ObjectIndex + 1
                && candidates[end + 1].TurnDirection == candidates[end].TurnDirection)
                end++;

            int pointCount = end - start + 1;
            ArcJumpPoint[] sequence = candidates.Skip(start).Take(pointCount).ToArray();
            double angleConsistency = Consistency(sequence.Select(point => point.Angle));
            if (pointCount >= MinimumArcPoints && angleConsistency >= MinimumArcAngleConsistency)
            {
                ArcJumpPoint first = sequence[0];
                ArcJumpPoint last = sequence[^1];
                int objectCount = pointCount + 2;
                double distanceConsistency = Consistency(sequence.SelectMany(point => new[]
                    { point.PreviousDistance, point.NextDistance }));
                double cumulativeTurn = sequence.Sum(point => point.Angle);
                matches.Add(new(JumpPatternType.Arc, first.ObjectIndex - 1, last.ObjectIndex + 1,
                    objects[first.ObjectIndex - 1].Time, objects[last.ObjectIndex + 1].Time,
                    ArcConfidence(pointCount, objectCount, angleConsistency, distanceConsistency, cumulativeTurn,
                        objects.Count)));
                retained.AddRange(sequence);
                for (int index = first.ObjectIndex - 1; index <= last.ObjectIndex + 1; index++)
                    involved.Add(index);
                longest = Math.Max(longest, objectCount);
                angleConsistencies.Add(angleConsistency);
                distanceConsistencies.Add(distanceConsistency);
                cumulativeTurns.Add(cumulativeTurn);
                if (first.TurnDirection == ArcTurnDirection.Left) leftSequences++;
                else rightSequences++;
            }
            start = end + 1;
        }

        double averageTurn = retained.Count == 0 ? 0 : retained.Average(point => point.Angle);
        double minTurn = retained.Count == 0 ? 0 : retained.Min(point => point.Angle);
        double maxTurn = retained.Count == 0 ? 0 : retained.Max(point => point.Angle);
        double averageDistance = retained.Count == 0 ? 0 : retained.Average(point =>
            (point.PreviousDistance + point.NextDistance) / 2);
        var result = new ArcJumpAnalysis(objects.Count, involved.Count, longest, averageTurn, minTurn, maxTurn,
            angleConsistencies.Count == 0 ? 0 : angleConsistencies.Average(), averageDistance,
            distanceConsistencies.Count == 0 ? 0 : distanceConsistencies.Average(),
            cumulativeTurns.Count == 0 ? 0 : cumulativeTurns.Average(), leftSequences, rightSequences,
            ArcConfidence(retained.Count, longest,
                angleConsistencies.Count == 0 ? 0 : angleConsistencies.Average(),
                distanceConsistencies.Count == 0 ? 0 : distanceConsistencies.Average(),
                cumulativeTurns.Count == 0 ? 0 : cumulativeTurns.Average(), objects.Count),
            matches.ToArray(), retained.ToArray());
        GameplayDebug.ArcJump(result, GameplayDebug.ArcJumpEnabled, GameplayDebug.ArcJumpDetailedEnabled);
        return result;
    }

    private static double ArcConfidence(int pointCount, int longest, double angleConsistency,
        double distanceConsistency, double cumulativeTurn, int totalObjects)
    {
        if (pointCount == 0 || totalObjects == 0) return 0;
        double frequency = (double)pointCount / Math.Max(1, totalObjects - 2);
        double length = Math.Min(1, (double)longest / ArcConfidenceFullLength);
        double curvature = Math.Clamp(cumulativeTurn / 180, 0, 1);
        return Math.Clamp((frequency + length + angleConsistency + distanceConsistency + curvature) / 5, 0, 1);
    }
}
