using BeatInsight.Models;
using BeatInsight.Models.JumpPatterns;

namespace BeatInsight.Analysis.JumpPatterns;

/// <summary>
/// Local recognition using the existing analyzer's Jump qualification and Stream exclusion.
/// Linear consumes JumpSequences; Edge reuses jump links with its own three-object minimum.
/// Call after gameplay analysis using the same objects/profile. No global score or identity mutation.
/// Other pattern detectors may later match the same objects independently.
/// </summary>
public static partial class JumpPatternAnalyzer
{
    private const int MinimumLinearObjects = 4;
    private const double MaximumLocalTurnDegrees = 25;
    private const double MaximumGlobalDeviationDegrees = 30;

    public static IReadOnlyList<JumpPatternMatch> Analyze(
        IReadOnlyList<HitObject> objects, GameplayProfile profile)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(profile);
        var matches = new List<JumpPatternMatch>();

        // Qualification already includes circle type, distance, timing bounds and Stream exclusion.
        foreach (var sequence in profile.JumpSequences)
        {
            int first = sequence.StartObjectIndex, last = sequence.EndObjectIndex;
            if (first < 0 || last >= objects.Count || last - first + 1 < MinimumLinearObjects)
                continue;

            // Compute directions once; do not duplicate Jump distance calculations.
            var headings = new double[last - first];
            for (int i = first; i < last; i++)
                headings[i - first] = Heading(objects[i], objects[i + 1]);

            int start = first;
            while (start + MinimumLinearObjects - 1 <= last)
            {
                int end = start;
                double cleanliness = 0;
                while (end < last && Fits(objects, headings, first, start, end + 1, out double nextCleanliness))
                {
                    end++;
                    cleanliness = nextCleanliness;
                }

                if (end - start + 1 >= MinimumLinearObjects)
                {
                    // Confidence is 0..1 geometric cleanliness, never a recognition gate.
                    matches.Add(new(JumpPatternType.Linear, start, end,
                        objects[start].Time, objects[end].Time, cleanliness));
                    start = end; // A turn can be the shared endpoint of two independent lines.
                }
                else
                    start++;
            }
        }
        matches.AddRange(AnalyzeEdge(objects, profile).Matches);
        matches.AddRange(AnalyzeSharp(objects, profile).Matches);
        matches.AddRange(AnalyzeReversal(objects, profile).Matches);
        matches.AddRange(AnalyzeTriangle(objects, profile).Matches);
        matches.AddRange(AnalyzeOneTwo(objects, profile).Matches);
        matches.AddRange(AnalyzeSpaced(objects, profile).Matches);
        matches.AddRange(AnalyzeSpacingTrend(objects, profile).Sequences.Select(sequence => new JumpPatternMatch(
            sequence.Direction == JumpSpacingTrendDirection.Accelerating
                ? JumpPatternType.Accelerating : JumpPatternType.Decelerating,
            sequence.StartObjectIndex, sequence.EndObjectIndex, sequence.StartTime, sequence.EndTime,
            sequence.Confidence)));
        matches.AddRange(AnalyzeAlternatingSpacing(objects, profile).Matches);
        matches.AddRange(AnalyzeWideAngle(objects, profile).Matches);
        matches.AddRange(AnalyzeArc(objects, profile).Matches);
        return matches;
    }

    private static bool Fits(IReadOnlyList<HitObject> objects, double[] headings,
        int offset, int start, int end, out double confidence)
    {
        confidence = 0;
        double axis = Heading(objects[start], objects[end]);
        if (!double.IsFinite(axis)) return false;
        double maxLocal = 0, maxGlobal = 0;
        for (int i = start; i < end; i++)
        {
            double heading = headings[i - offset];
            if (!double.IsFinite(heading)) return false;
            maxGlobal = Math.Max(maxGlobal, AngleDifference(heading, axis));
            if (i > start)
                maxLocal = Math.Max(maxLocal, AngleDifference(headings[i - offset - 1], heading));
        }
        if (maxLocal > MaximumLocalTurnDegrees || maxGlobal > MaximumGlobalDeviationDegrees)
            return false;
        confidence = Math.Clamp(1 - (maxLocal / MaximumLocalTurnDegrees
            + maxGlobal / MaximumGlobalDeviationDegrees) / 2, 0, 1);
        return true;
    }

    private static double Heading(HitObject from, HitObject to)
    {
        double dx = (double)to.X - from.X, dy = (double)to.Y - from.Y;
        return dx == 0 && dy == 0 ? double.NaN : Math.Atan2(dy, dx) * 180 / Math.PI;
    }

    private static double AngleDifference(double a, double b)
    {
        double difference = Math.Abs(a - b) % 360;
        return Math.Min(difference, 360 - difference);
    }
}
