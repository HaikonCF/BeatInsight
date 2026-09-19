using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.JumpPatterns;

namespace BeatInsight.Analysis.JumpPatterns;

public static partial class JumpPatternAnalyzer
{
    // GetTurnAngle uses 0° for a straight continuation and 180° for a reversal.
    private const double MinimumSharpTurnDegrees = 120;
    private const double MinimumSharpJumpDistance = 60;
    private const int MinimumSharpPoints = 2;
    private const int SharpConfidenceFullLength = 8;
    private const double ReversalTurnDegrees = 170;

    public static SharpJumpAnalysis AnalyzeSharp(IReadOnlyList<HitObject> objects, GameplayProfile profile)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(profile);
        var streamObjects = BuildStreamMask(objects.Count, profile.StreamSequences);
        var candidates = new List<SharpJumpPoint>();

        for (int i = 1; i + 1 < objects.Count; i++)
        {
            var previous = objects[i - 1];
            var current = objects[i];
            var next = objects[i + 1];
            if ((previous.Type & 1) == 0 || (current.Type & 1) == 0 || (next.Type & 1) == 0
                || streamObjects[i - 1] || streamObjects[i] || streamObjects[i + 1]
                || !GameplayAnalyzer.IsJumpLink(previous, current, out double previousDistance)
                || !GameplayAnalyzer.IsJumpLink(current, next, out double nextDistance)
                || previousDistance < MinimumSharpJumpDistance || nextDistance < MinimumSharpJumpDistance)
                continue;

            double angle = GameplayAnalyzer.GetTurnAngle(previous, current, next);
            if (angle < MinimumSharpTurnDegrees)
                continue;
            candidates.Add(new(i, current.Time, previous.X, previous.Y, current.X, current.Y,
                next.X, next.Y, angle, previousDistance, nextDistance, angle >= ReversalTurnDegrees));
        }

        var matches = new List<JumpPatternMatch>();
        var retained = new List<SharpJumpPoint>();
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
            if (pointCount >= MinimumSharpPoints)
            {
                SharpJumpPoint first = candidates[start];
                SharpJumpPoint last = candidates[end];
                int objectCount = pointCount + 2;
                double averageAngle = candidates.Skip(start).Take(pointCount).Average(point => point.Angle);
                matches.Add(new(JumpPatternType.Sharp, first.ObjectIndex - 1, last.ObjectIndex + 1,
                    objects[first.ObjectIndex - 1].Time, objects[last.ObjectIndex + 1].Time,
                    SharpConfidence(pointCount, objectCount, averageAngle, objects.Count)));
                retained.AddRange(candidates.Skip(start).Take(pointCount));
                for (int index = first.ObjectIndex - 1; index <= last.ObjectIndex + 1; index++)
                    involved.Add(index);
                longest = Math.Max(longest, objectCount);
            }
            start = end + 1;
        }

        double average = retained.Count == 0 ? 0 : retained.Average(point => point.Angle);
        double strongest = retained.Count == 0 ? 0 : retained.Max(point => point.Angle);
        var result = new SharpJumpAnalysis(objects.Count, involved.Count, longest, average, strongest,
            SharpConfidence(retained.Count, longest, average, objects.Count), matches.ToArray(), retained.ToArray());
        GameplayDebug.SharpJump(result, GameplayDebug.SharpJumpEnabled, GameplayDebug.SharpJumpDetailedEnabled);
        return result;
    }

    private static bool[] BuildStreamMask(int count, IReadOnlyList<PatternSequence> sequences)
    {
        var mask = new bool[count];
        foreach (var sequence in sequences)
            for (int i = Math.Max(0, sequence.StartObjectIndex); i <= Math.Min(count - 1, sequence.EndObjectIndex); i++)
                mask[i] = true;
        return mask;
    }

    private static double SharpConfidence(int pointCount, int longest, double averageAngle, int totalObjects)
    {
        if (pointCount == 0 || totalObjects == 0) return 0;
        double frequency = (double)pointCount / Math.Max(1, totalObjects - 2);
        double length = Math.Min(1, (double)longest / SharpConfidenceFullLength);
        double intensity = Math.Clamp((averageAngle - MinimumSharpTurnDegrees)
            / (180 - MinimumSharpTurnDegrees), 0, 1);
        return Math.Clamp((frequency + length + intensity) / 3, 0, 1);
    }
}
