using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.TechPatterns;
using System.Numerics;

namespace BeatInsight.Analysis.TechPatterns;

public static partial class TechPatternAnalyzer
{
    private static OverlapPatternAnalysis AnalyzeOverlapCore(
        IReadOnlyList<HitObject> objects,
        OverlapPatternOptions options)
    {
        ArgumentNullException.ThrowIfNull(objects);
        Validate(options);

        OverlapPatternLink[] links = Enumerable.Range(0, Math.Max(0, objects.Count - 1))
            .Select(index => CreateOverlapLink(objects, index, options))
            .ToArray();
        OverlapPatternSequence[] runs = SegmentOverlapSequences(links, options).ToArray();
        OverlapPatternSequence[] sequences = runs.Where(run => run.Qualified).ToArray();
        OverlapPatternLink[] qualifiedLinks = links.Where(link => link.Qualified).ToArray();

        var involvedObjects = new HashSet<int>();
        foreach (OverlapPatternSequence sequence in sequences)
        {
            for (int index = sequence.StartIndex; index <= sequence.EndIndex; index++)
                involvedObjects.Add(index);
        }

        double averageDistance = qualifiedLinks.Length == 0
            ? 0
            : qualifiedLinks.Average(link => link.Distance!.Value);
        double minimumDistance = qualifiedLinks.Length == 0
            ? 0
            : qualifiedLinks.Min(link => link.Distance!.Value);
        double averageStrength = qualifiedLinks.Length == 0
            ? 0
            : qualifiedLinks.Average(link => link.OverlapStrength!.Value);
        double maximumStrength = qualifiedLinks.Length == 0
            ? 0
            : qualifiedLinks.Max(link => link.OverlapStrength!.Value);
        double averageSequenceLength = sequences.Length == 0
            ? 0
            : sequences.Average(sequence => sequence.ObjectCount);
        double coverage = objects.Count == 0
            ? 0
            : (double)involvedObjects.Count / objects.Count;
        double sequenceSignal = (double)sequences.Length / (sequences.Length + 1);
        double confidence = Math.Clamp(
            (sequenceSignal + coverage + averageStrength) / 3,
            0,
            1);

        var result = new OverlapPatternAnalysis(
            objects.Count,
            qualifiedLinks.Length,
            sequences.Length,
            involvedObjects.Count,
            averageDistance,
            minimumDistance,
            averageStrength,
            maximumStrength,
            averageSequenceLength,
            confidence,
            links,
            runs);

        GameplayDebug.OverlapPattern(
            result,
            GameplayDebug.OverlapPatternEnabled,
            GameplayDebug.OverlapPatternDetailedEnabled);
        return result;
    }

    private static OverlapPatternLink CreateOverlapLink(
        IReadOnlyList<HitObject> objects,
        int startIndex,
        OverlapPatternOptions options)
    {
        int endIndex = startIndex + 1;
        HitObject start = objects[startIndex];
        HitObject end = objects[endIndex];
        double gap = end.Time - start.Time;
        OverlapTransitionType? transitionType = GetOverlapTransitionType(start, end);

        if (!transitionType.HasValue || gap < 0 || gap > options.MaximumOverlapGap)
            return new OverlapPatternLink(
                startIndex,
                endIndex,
                start.Time,
                end.Time,
                gap,
                null,
                null,
                transitionType,
                false,
                "IntervalBreak");

        (double finalX, double finalY) = GameplayAnalyzer.GetObjectEndPosition(start);
        double distance = Vector2.Distance(
            new Vector2((float)finalX, (float)finalY),
            new Vector2(end.X, end.Y));
        double strength = Math.Clamp(
            1 - distance / options.MaximumOverlapDistance,
            0,
            1);
        bool qualified = distance <= options.MaximumOverlapDistance;

        return new OverlapPatternLink(
            startIndex,
            endIndex,
            start.Time,
            end.Time,
            gap,
            distance,
            strength,
            transitionType,
            qualified,
            qualified ? "QualifiedLink" : "AboveOverlapDistance");
    }

    private static IEnumerable<OverlapPatternSequence> SegmentOverlapSequences(
        IReadOnlyList<OverlapPatternLink> links,
        OverlapPatternOptions options)
    {
        var current = new List<OverlapPatternLink>();

        OverlapPatternSequence? FinalizeCurrent()
        {
            if (current.Count == 0)
                return null;
            OverlapPatternSequence sequence = CreateOverlapSequence(current, options);
            current.Clear();
            return sequence;
        }

        foreach (OverlapPatternLink link in links)
        {
            if (!link.Qualified)
            {
                OverlapPatternSequence? completed = FinalizeCurrent();
                if (completed is not null)
                    yield return completed;
                continue;
            }

            if (current.Count > 0 && current[^1].EndIndex != link.StartIndex)
            {
                OverlapPatternSequence? completed = FinalizeCurrent();
                if (completed is not null)
                    yield return completed;
            }
            current.Add(link);
        }

        OverlapPatternSequence? trailing = FinalizeCurrent();
        if (trailing is not null)
            yield return trailing;
    }

    private static OverlapPatternSequence CreateOverlapSequence(
        IReadOnlyList<OverlapPatternLink> source,
        OverlapPatternOptions options)
    {
        OverlapPatternLink[] links = source.ToArray();
        double minimumDistance = links.Min(link => link.Distance!.Value);
        double maximumDistance = links.Max(link => link.Distance!.Value);
        double averageDistance = links.Average(link => link.Distance!.Value);
        double averageStrength = links.Average(link => link.OverlapStrength!.Value);
        double maximumStrength = links.Max(link => link.OverlapStrength!.Value);
        double lengthSignal = Math.Clamp(links.Length / 5.0, 0, 1);
        double consistencySignal = 1 - Math.Clamp(
            (maximumDistance - minimumDistance) / options.MaximumOverlapDistance,
            0,
            1);
        double score = Math.Clamp(
            (averageStrength + lengthSignal + consistencySignal) / 3,
            0,
            1);
        bool qualified = links.Length >= options.MinimumOverlapSequenceLinks;

        return new OverlapPatternSequence(
            links[0].StartIndex,
            links[^1].EndIndex,
            links[0].StartTime,
            links[^1].EndTime,
            minimumDistance,
            maximumDistance,
            averageDistance,
            averageStrength,
            maximumStrength,
            links.Count(link => link.TransitionType == OverlapTransitionType.CircleCircle),
            links.Count(link => link.TransitionType == OverlapTransitionType.CircleSlider),
            links.Count(link => link.TransitionType == OverlapTransitionType.SliderCircle),
            links.Count(link => link.TransitionType == OverlapTransitionType.SliderSlider),
            score,
            qualified,
            qualified ? "Qualified" : "TooFewOverlapLinks",
            links);
    }

    private static OverlapTransitionType? GetOverlapTransitionType(HitObject start, HitObject end)
    {
        bool startCircle = IsCircle(start);
        bool startSlider = IsSlider(start);
        bool endCircle = IsCircle(end);
        bool endSlider = IsSlider(end);
        return (startCircle, startSlider, endCircle, endSlider) switch
        {
            (true, false, true, false) => OverlapTransitionType.CircleCircle,
            (true, false, false, true) => OverlapTransitionType.CircleSlider,
            (false, true, true, false) => OverlapTransitionType.SliderCircle,
            (false, true, false, true) => OverlapTransitionType.SliderSlider,
            _ => null
        };
    }

    private static void Validate(OverlapPatternOptions options)
    {
        if (!double.IsFinite(options.MaximumOverlapDistance) || options.MaximumOverlapDistance <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumOverlapDistance must be positive.");
        if (!double.IsFinite(options.MaximumOverlapGap) || options.MaximumOverlapGap < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumOverlapGap must not be negative.");
        if (options.MinimumOverlapSequenceLinks < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumOverlapSequenceLinks must be at least 1.");
    }
}
