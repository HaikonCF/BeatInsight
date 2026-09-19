using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.JumpPatterns;

namespace BeatInsight.Analysis.JumpPatterns;

public static partial class JumpPatternAnalyzer
{
    private const double MinimumAlternatingSpacingDistance = 60;
    private const int MinimumAlternatingSpacingLinks = 5;
    private const double MinimumAlternationConsistency = .75;
    private const double MinimumSpacingContrast = .20;
    // Alternating signs alone can describe arbitrary distance noise. This is a small,
    // local coherence guard on the size of meaningful oscillations, not an angle rule.
    private const double MinimumOscillationMagnitudeConsistency = .50;
    private const int AlternatingSpacingConfidenceFullLength = 10;

    public static AlternatingSpacingJumpAnalysis AnalyzeAlternatingSpacing(IReadOnlyList<HitObject> objects,
        GameplayProfile profile)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(profile);

        var streamObjects = BuildStreamMask(objects.Count, profile.StreamSequences);
        var sequences = new List<AlternatingSpacingJumpSequence>();
        var links = new List<AlternatingSpacingLink>();

        void FinishLinks()
        {
            AddAlternatingSequences(links, objects, sequences);
            links.Clear();
        }

        for (int index = 0; index + 1 < objects.Count; index++)
        {
            HitObject from = objects[index];
            HitObject to = objects[index + 1];
            if ((from.Type & 1) == 0 || (to.Type & 1) == 0 || streamObjects[index] || streamObjects[index + 1]
                || !GameplayAnalyzer.IsJumpLink(from, to, out double distance)
                || distance < MinimumAlternatingSpacingDistance)
            {
                FinishLinks();
                continue;
            }
            links.Add(new(index, index + 1, distance));
        }
        FinishLinks();

        var involved = new HashSet<int>();
        foreach (AlternatingSpacingJumpSequence sequence in sequences)
            for (int index = sequence.StartObjectIndex; index <= sequence.EndObjectIndex; index++)
                involved.Add(index);

        int longest = sequences.Count == 0 ? 0 : sequences.Max(sequence => sequence.ObjectCount);
        double averageConsistency = AverageOrZero(sequences.Select(sequence => sequence.AlternationConsistency));
        double averageContrast = AverageOrZero(sequences.Select(sequence => sequence.SpacingContrast));
        double averageOscillation = AverageOrZero(sequences.Select(sequence => sequence.OscillationMagnitudeConsistency));
        var result = new AlternatingSpacingJumpAnalysis(objects.Count, involved.Count, longest,
            AverageOrZero(sequences.SelectMany(sequence => sequence.Distances)),
            AverageOrZero(sequences.Select(sequence => sequence.HighSpacingAverage)),
            AverageOrZero(sequences.Select(sequence => sequence.LowSpacingAverage)),
            AverageOrZero(sequences.Select(sequence => sequence.HighLowRatio)), averageContrast,
            averageConsistency, averageOscillation,
            sequences.Count(sequence => sequence.Start == AlternatingSpacingStart.ExpandingFirst),
            sequences.Count(sequence => sequence.Start == AlternatingSpacingStart.ContractingFirst),
            AlternatingSpacingConfidence(longest, averageConsistency, averageContrast, averageOscillation,
                involved.Count, objects.Count),
            sequences.Select(sequence => new JumpPatternMatch(JumpPatternType.AlternatingSpacing,
                sequence.StartObjectIndex, sequence.EndObjectIndex, sequence.StartTime, sequence.EndTime,
                sequence.Confidence)).ToArray(), sequences.ToArray());
        GameplayDebug.AlternatingSpacingJump(result, GameplayDebug.AlternatingSpacingJumpEnabled,
            GameplayDebug.AlternatingSpacingJumpDetailedEnabled);
        return result;
    }

    private static void AddAlternatingSequences(IReadOnlyList<AlternatingSpacingLink> links,
        IReadOnlyList<HitObject> objects, ICollection<AlternatingSpacingJumpSequence> output)
    {
        int start = 0;
        while (links.Count - start >= MinimumAlternatingSpacingLinks)
        {
            int? split = FindAlternationBreak(links, start);
            int end = split ?? links.Count - 1;
            if (TryBuildAlternatingSequence(links, start, end, objects,
                out AlternatingSpacingJumpSequence sequence))
                output.Add(sequence);
            if (split is null)
                break;
            start = split.Value;
        }
    }

    private static int? FindAlternationBreak(IReadOnlyList<AlternatingSpacingLink> links, int start)
    {
        int previous = 0;
        int? firstFailureBoundary = null;
        for (int index = start + 1; index < links.Count; index++)
        {
            int current = SpacingSign(links[index].Distance - links[index - 1].Distance);
            if (previous == 0)
            {
                previous = current;
                continue;
            }

            bool alternates = current != 0 && current != previous;
            if (alternates)
            {
                firstFailureBoundary = null;
            }
            else if (firstFailureBoundary is null)
            {
                firstFailureBoundary = index - 1;
            }
            else
            {
                // A recovered next step permits one local anomaly; repeated failure ends the run.
                return firstFailureBoundary.Value;
            }
            previous = current;
        }

        // A failure that persists to the end is a real trailing break, not a valid extension.
        return firstFailureBoundary;
    }

    private static bool TryBuildAlternatingSequence(IReadOnlyList<AlternatingSpacingLink> links, int start, int end,
        IReadOnlyList<HitObject> objects, out AlternatingSpacingJumpSequence sequence)
    {
        sequence = null!;
        int linkCount = end - start + 1;
        if (linkCount < MinimumAlternatingSpacingLinks)
            return false;

        AlternatingSpacingLink[] run = links.Skip(start).Take(linkCount).ToArray();
        double[] distances = run.Select(link => link.Distance).ToArray();
        double[] deltas = distances.Zip(distances.Skip(1), (left, right) => right - left).ToArray();
        int[] signs = deltas.Select(SpacingSign).ToArray();
        int validTransitions = signs.Zip(signs.Skip(1), (left, right) => left != 0 && right != 0 && left != right)
            .Count(value => value);
        double alternationConsistency = deltas.Length < 2 ? 0 : (double)validTransitions / (deltas.Length - 1);
        double averageDistance = distances.Average();
        double[] significantMagnitudes = deltas.Where(delta => SpacingSign(delta) != 0).Select(Math.Abs).ToArray();
        double contrast = significantMagnitudes.Length == 0 || averageDistance <= 0 ? 0
            : significantMagnitudes.Average() / averageDistance;
        double oscillationConsistency = Consistency(significantMagnitudes);
        if (alternationConsistency < MinimumAlternationConsistency || contrast < MinimumSpacingContrast
            || oscillationConsistency < MinimumOscillationMagnitudeConsistency)
            return false;

        int firstSign = signs.FirstOrDefault(sign => sign != 0);
        if (firstSign == 0)
            return false;
        AlternatingSpacingStart direction = firstSign > 0
            ? AlternatingSpacingStart.ExpandingFirst : AlternatingSpacingStart.ContractingFirst;
        (double low, double high) = SpacingBands(distances);
        double ratio = high <= 0 ? 0 : low / high;
        int objectCount = linkCount + 1;
        sequence = new(run[0].StartObjectIndex, run[^1].EndObjectIndex,
            objects[run[0].StartObjectIndex].Time, objects[run[^1].EndObjectIndex].Time, direction,
            distances, deltas, high, low, ratio, contrast, alternationConsistency, oscillationConsistency,
            AlternatingSpacingConfidence(objectCount, alternationConsistency, contrast, oscillationConsistency,
                objectCount, objects.Count));
        return true;
    }

    private static (double Low, double High) SpacingBands(IReadOnlyList<double> distances)
    {
        double[] sorted = distances.OrderBy(distance => distance).ToArray();
        int half = sorted.Length / 2;
        if (half == 0) return (0, 0);
        return (sorted.Take(half).Average(), sorted.Skip(sorted.Length - half).Average());
    }

    private static double AlternatingSpacingConfidence(int longest, double alternationConsistency, double contrast,
        double oscillationConsistency, int involvedObjects, int totalObjects)
    {
        if (longest == 0 || totalObjects == 0) return 0;
        double length = Math.Min(1, (double)longest / AlternatingSpacingConfidenceFullLength);
        double normalizedContrast = Math.Clamp((contrast - MinimumSpacingContrast) / .80, 0, 1);
        double frequency = Math.Clamp((double)involvedObjects / totalObjects, 0, 1);
        return Math.Clamp((length + alternationConsistency + normalizedContrast + oscillationConsistency + frequency) / 5,
            0, 1);
    }

    private static double AverageOrZero(IEnumerable<double> values)
    {
        double[] finite = values.Where(double.IsFinite).ToArray();
        return finite.Length == 0 ? 0 : finite.Average();
    }

    private sealed record AlternatingSpacingLink(int StartObjectIndex, int EndObjectIndex, double Distance);
}
