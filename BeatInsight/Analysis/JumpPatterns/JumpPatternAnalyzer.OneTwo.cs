using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.JumpPatterns;

namespace BeatInsight.Analysis.JumpPatterns;

public static partial class JumpPatternAnalyzer
{
    // A 1-2 template is an alternating chain, not a generic high-angle Jump.
    private const double MinimumOneTwoJumpDistance = 60;
    private const double MinimumOneTwoTurnDegrees = 35;
    private const double NearReversalTurnDegrees = 170;
    // Three observed pivots (five objects) avoids promoting a short Sharp zigzag
    // before the alternating motif has had an opportunity to repeat.
    private const int MinimumOneTwoPoints = 3;
    private const double MinimumOneTwoDistanceConsistency = .55;
    private const double MinimumOneTwoIntervalConsistency = .55;
    private const int OneTwoConfidenceFullLength = 8;
    private const double MinimumStableReversalTurnSine = .02;

    public static OneTwoJumpAnalysis AnalyzeOneTwo(IReadOnlyList<HitObject> objects, GameplayProfile profile)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(profile);

        var streamObjects = BuildStreamMask(objects.Count, profile.StreamSequences);
        var candidates = new List<OneTwoCandidate>();
        for (int i = 1; i + 1 < objects.Count; i++)
        {
            var previous = objects[i - 1];
            var current = objects[i];
            var next = objects[i + 1];
            if ((previous.Type & 1) == 0 || (current.Type & 1) == 0 || (next.Type & 1) == 0
                || streamObjects[i - 1] || streamObjects[i] || streamObjects[i + 1]
                || !GameplayAnalyzer.IsJumpLink(previous, current, out double previousDistance)
                || !GameplayAnalyzer.IsJumpLink(current, next, out double nextDistance)
                || previousDistance < MinimumOneTwoJumpDistance || nextDistance < MinimumOneTwoJumpDistance)
                continue;

            double angle = GameplayAnalyzer.GetTurnAngle(previous, current, next);
            double signedTurn = SignedTurn(previous, current, next, previousDistance, nextDistance);
            OneTwoTurnDirection direction = GetOneTwoTurnDirection(angle, signedTurn);
            if (direction == OneTwoTurnDirection.Neutral)
                continue;

            candidates.Add(new(new(i, current.Time, previous.X, previous.Y, current.X, current.Y,
                next.X, next.Y, previousDistance, nextDistance, angle,
                current.Time - previous.Time, next.Time - current.Time, direction, false), signedTurn));
        }

        var matches = new List<JumpPatternMatch>();
        var retained = new List<OneTwoJumpPoint>();
        var involved = new HashSet<int>();
        var distanceConsistencies = new List<double>();
        var intervalConsistencies = new List<double>();
        var alternationConsistencies = new List<double>();
        int longest = 0;
        int start = 0;
        while (start < candidates.Count)
        {
            int end = start;
            while (end + 1 < candidates.Count
                && candidates[end + 1].Point.ObjectIndex == candidates[end].Point.ObjectIndex + 1
                && AreAlternating(candidates[end], candidates[end + 1]))
                end++;

            int pointCount = end - start + 1;
            if (pointCount >= MinimumOneTwoPoints)
            {
                var sequence = candidates.Skip(start).Take(pointCount).ToArray();
                var distances = sequence.Select(candidate => candidate.Point.NextDistance)
                    .Append(sequence[0].Point.PreviousDistance).ToArray();
                var intervals = sequence.Select(candidate => (double)candidate.Point.NextInterval)
                    .Append(sequence[0].Point.PreviousInterval).ToArray();
                double distanceConsistency = Consistency(distances);
                double intervalConsistency = Consistency(intervals);
                if (distanceConsistency >= MinimumOneTwoDistanceConsistency
                    && intervalConsistency >= MinimumOneTwoIntervalConsistency)
                {
                    int objectCount = pointCount + 2;
                    for (int i = 0; i < sequence.Length; i++)
                        retained.Add(sequence[i].Point with { AlternatingWithPrevious = i > 0 });
                    matches.Add(new(JumpPatternType.OneTwo, sequence[0].Point.ObjectIndex - 1,
                        sequence[^1].Point.ObjectIndex + 1, objects[sequence[0].Point.ObjectIndex - 1].Time,
                        objects[sequence[^1].Point.ObjectIndex + 1].Time,
                        OneTwoConfidence(pointCount, objectCount, distanceConsistency, intervalConsistency, 1, objects.Count)));
                    for (int index = sequence[0].Point.ObjectIndex - 1; index <= sequence[^1].Point.ObjectIndex + 1; index++)
                        involved.Add(index);
                    longest = Math.Max(longest, objectCount);
                    distanceConsistencies.Add(distanceConsistency);
                    intervalConsistencies.Add(intervalConsistency);
                    alternationConsistencies.Add(1);
                }
            }
            start = end + 1;
        }

        double averageDistance = retained.Count == 0 ? 0 : retained.Average(point => (point.PreviousDistance + point.NextDistance) / 2);
        double averageInterval = retained.Count == 0 ? 0 : retained.Average(point => (point.PreviousInterval + point.NextInterval) / 2d);
        double distanceConsistencyResult = distanceConsistencies.Count == 0 ? 0 : distanceConsistencies.Average();
        double intervalConsistencyResult = intervalConsistencies.Count == 0 ? 0 : intervalConsistencies.Average();
        double alternationConsistency = alternationConsistencies.Count == 0 ? 0 : alternationConsistencies.Average();
        var result = new OneTwoJumpAnalysis(objects.Count, involved.Count, longest, averageDistance,
            distanceConsistencyResult, averageInterval, intervalConsistencyResult, alternationConsistency,
            OneTwoConfidence(retained.Count, longest, distanceConsistencyResult, intervalConsistencyResult,
                alternationConsistency, objects.Count), matches.ToArray(), retained.ToArray());
        GameplayDebug.OneTwoJump(result, GameplayDebug.OneTwoJumpEnabled, GameplayDebug.OneTwoJumpDetailedEnabled);
        return result;
    }

    private static OneTwoTurnDirection GetOneTwoTurnDirection(double angle, double signedTurn)
    {
        if (angle >= NearReversalTurnDegrees)
            return OneTwoTurnDirection.Reversal;
        if (angle < MinimumOneTwoTurnDegrees)
            return OneTwoTurnDirection.Neutral;

        return signedTurn > 0 ? OneTwoTurnDirection.Left : OneTwoTurnDirection.Right;
    }

    private static double SignedTurn(HitObject previous, HitObject current, HitObject next,
        double previousDistance, double nextDistance)
    {
        double previousX = current.X - previous.X;
        double previousY = current.Y - previous.Y;
        double nextX = next.X - current.X;
        double nextY = next.Y - current.Y;
        double sine = (previousX * nextY - previousY * nextX) / (previousDistance * nextDistance);
        return Math.Abs(sine) < MinimumStableReversalTurnSine ? 0 : Math.Sign(sine);
    }

    private static bool AreAlternating(OneTwoCandidate left, OneTwoCandidate right)
    {
        // Exact reversals have no stable cross-product sign, but a repeated exact
        // reversal is still a deterministic back-and-forth motif. A lone unsigned
        // reversal cannot bridge otherwise non-alternating turns.
        if (left.Point.TurnDirection == OneTwoTurnDirection.Reversal
            && right.Point.TurnDirection == OneTwoTurnDirection.Reversal
            && left.TurnSign == 0 && right.TurnSign == 0)
            return true;

        return left.TurnSign != 0 && right.TurnSign != 0 && left.TurnSign != right.TurnSign;
    }

    private static double Consistency(IEnumerable<double> values)
    {
        double[] sample = values.Where(value => double.IsFinite(value) && value > 0).ToArray();
        return sample.Length == 0 ? 0 : Math.Clamp(sample.Min() / sample.Max(), 0, 1);
    }

    private static double OneTwoConfidence(int pointCount, int longest, double distanceConsistency,
        double intervalConsistency, double alternationConsistency, int totalObjects)
    {
        if (pointCount == 0 || totalObjects == 0) return 0;
        double length = Math.Min(1, (double)longest / OneTwoConfidenceFullLength);
        return Math.Clamp((length + distanceConsistency + intervalConsistency + alternationConsistency) / 4, 0, 1);
    }

    private sealed record OneTwoCandidate(OneTwoJumpPoint Point, double TurnSign);
}
