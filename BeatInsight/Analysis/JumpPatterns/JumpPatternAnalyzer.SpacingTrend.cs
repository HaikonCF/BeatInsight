using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.JumpPatterns;

namespace BeatInsight.Analysis.JumpPatterns;

public static partial class JumpPatternAnalyzer
{
    private const double MinimumTrendJumpDistance = 60;
    private const int MinimumTrendLinks = 4;
    private const double MinimumTrendConsistency = .75;
    private const double MinimumTrendMagnitude = .25;
    private const double MinimumSpacingStepDifference = 10;
    private const int TrendConfidenceFullLength = 10;

    public static JumpSpacingTrendAnalysis AnalyzeSpacingTrend(IReadOnlyList<HitObject> objects,
        GameplayProfile profile)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(profile);

        var streamObjects = BuildStreamMask(objects.Count, profile.StreamSequences);
        var sequences = new List<JumpSpacingTrendSequence>();
        var links = new List<TrendLink>();

        void FinishLinks()
        {
            AddTrendSequences(links, objects, sequences);
            links.Clear();
        }

        for (int i = 0; i + 1 < objects.Count; i++)
        {
            HitObject from = objects[i];
            HitObject to = objects[i + 1];
            if ((from.Type & 1) == 0 || (to.Type & 1) == 0 || streamObjects[i] || streamObjects[i + 1]
                || !GameplayAnalyzer.IsJumpLink(from, to, out double distance)
                || distance < MinimumTrendJumpDistance)
            {
                FinishLinks();
                continue;
            }

            links.Add(new(i, i + 1, from.Time, to.Time, distance));
        }
        FinishLinks();

        JumpSpacingTrendMetrics accelerating = BuildMetrics(sequences, JumpSpacingTrendDirection.Accelerating,
            objects.Count);
        JumpSpacingTrendMetrics decelerating = BuildMetrics(sequences, JumpSpacingTrendDirection.Decelerating,
            objects.Count);
        var result = new JumpSpacingTrendAnalysis(objects.Count, accelerating, decelerating, sequences.ToArray());
        GameplayDebug.JumpSpacingTrend(result, GameplayDebug.JumpSpacingTrendEnabled,
            GameplayDebug.JumpSpacingTrendDetailedEnabled);
        return result;
    }

    private static void AddTrendSequences(IReadOnlyList<TrendLink> links, IReadOnlyList<HitObject> objects,
        ICollection<JumpSpacingTrendSequence> output)
    {
        int start = 0;
        while (links.Count - start >= MinimumTrendLinks)
        {
            int? splitAt = FindClearInversion(links, start);
            int end = splitAt ?? (links.Count - 1);
            if (TryBuildTrend(links, start, end, objects, out JumpSpacingTrendSequence sequence))
                output.Add(sequence);

            if (splitAt is null)
                break;

            // The peak/trough distance is shared as the physical boundary; each direction still deduplicates
            // its own object coverage.
            start = splitAt.Value;
        }
    }

    private static int? FindClearInversion(IReadOnlyList<TrendLink> links, int start)
    {
        int direction = 0;
        int firstOppositeDelta = -1;
        for (int deltaIndex = start; deltaIndex + 1 < links.Count; deltaIndex++)
        {
            int sign = SpacingSign(links[deltaIndex + 1].Distance - links[deltaIndex].Distance);
            if (sign == 0)
            {
                firstOppositeDelta = -1;
                continue;
            }
            if (direction == 0)
            {
                direction = sign;
                continue;
            }
            if (sign == direction)
            {
                firstOppositeDelta = -1;
                continue;
            }
            if (firstOppositeDelta < 0)
            {
                firstOppositeDelta = deltaIndex;
                continue;
            }

            // Two meaningful steps in the opposite direction mark a real spatial reversal.
            return firstOppositeDelta;
        }
        return null;
    }

    private static bool TryBuildTrend(IReadOnlyList<TrendLink> links, int start, int end,
        IReadOnlyList<HitObject> objects, out JumpSpacingTrendSequence sequence)
    {
        sequence = null!;
        int linkCount = end - start + 1;
        if (linkCount < MinimumTrendLinks)
            return false;

        TrendLink[] run = links.Skip(start).Take(linkCount).ToArray();
        double[] distances = run.Select(link => link.Distance).ToArray();
        double[] deltas = distances.Zip(distances.Skip(1), (left, right) => right - left).ToArray();
        int positive = deltas.Count(delta => SpacingSign(delta) > 0);
        int negative = deltas.Count(delta => SpacingSign(delta) < 0);
        int directional = positive + negative;
        if (directional == 0 || positive == negative)
            return false;

        JumpSpacingTrendDirection direction = positive > negative
            ? JumpSpacingTrendDirection.Accelerating
            : JumpSpacingTrendDirection.Decelerating;
        // A neutral (< 10px) step is not directional evidence. It must nevertheless remain
        // in the denominator: otherwise one large jump followed by a flat plateau could
        // incorrectly look like a perfectly consistent progressive trend.
        double consistency = (double)Math.Max(positive, negative) / deltas.Length;
        double average = distances.Average();
        double magnitude = average <= 0 ? 0 : Math.Abs(distances[^1] - distances[0]) / average;
        if (consistency < MinimumTrendConsistency || magnitude < MinimumTrendMagnitude)
            return false;

        int objectCount = linkCount + 1;
        sequence = new JumpSpacingTrendSequence(run[0].StartObjectIndex, run[^1].EndObjectIndex,
            objects[run[0].StartObjectIndex].Time, objects[run[^1].EndObjectIndex].Time, direction,
            distances, deltas, consistency, magnitude,
            TrendConfidence(objectCount, consistency, magnitude, objectCount, objects.Count));
        return true;
    }

    private static JumpSpacingTrendMetrics BuildMetrics(IReadOnlyList<JumpSpacingTrendSequence> all,
        JumpSpacingTrendDirection direction, int totalObjects)
    {
        JumpSpacingTrendSequence[] sequences = all.Where(sequence => sequence.Direction == direction).ToArray();
        if (sequences.Length == 0)
            return new(0, 0, 0, 0, 0, 0, 0, 0, 0);

        var involved = new HashSet<int>();
        foreach (JumpSpacingTrendSequence sequence in sequences)
            for (int index = sequence.StartObjectIndex; index <= sequence.EndObjectIndex; index++)
                involved.Add(index);

        int longest = sequences.Max(sequence => sequence.ObjectCount);
        double consistency = sequences.Average(sequence => sequence.TrendConsistency);
        double magnitude = sequences.Average(sequence => sequence.TrendMagnitude);
        double confidence = TrendConfidence(longest, consistency, magnitude, involved.Count, totalObjects);
        return new(sequences.Length, involved.Count, longest,
            sequences.Average(sequence => sequence.Distances[0]),
            sequences.Average(sequence => sequence.Distances[^1]), magnitude, consistency,
            sequences.Average(sequence => sequence.Distances.Average()), confidence);
    }

    private static double TrendConfidence(int longest, double consistency, double magnitude,
        int involvedObjectCount, int totalObjects)
    {
        if (longest == 0 || totalObjects == 0)
            return 0;
        double length = Math.Min(1, (double)longest / TrendConfidenceFullLength);
        double normalizedMagnitude = Math.Clamp((magnitude - MinimumTrendMagnitude) / .75, 0, 1);
        double frequency = Math.Clamp((double)involvedObjectCount / totalObjects, 0, 1);
        return Math.Clamp((length + consistency + normalizedMagnitude + frequency) / 4, 0, 1);
    }

    private static int SpacingSign(double delta) => Math.Abs(delta) >= MinimumSpacingStepDifference
        ? Math.Sign(delta) : 0;

    private sealed record TrendLink(int StartObjectIndex, int EndObjectIndex, int StartTime, int EndTime,
        double Distance);
}
