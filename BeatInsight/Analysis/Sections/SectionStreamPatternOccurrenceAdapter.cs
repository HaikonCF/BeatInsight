using BeatInsight.Models.Sections;

namespace BeatInsight.Analysis.Sections;

/// <summary>
/// Converts already-calculated production Stream results into the common
/// occurrence shape. No qualification or detector logic is performed here.
/// </summary>
public static class SectionStreamPatternOccurrenceAdapter
{
    public static IReadOnlyList<SectionStreamPatternOccurrence> Adapt(
        SectionStreamPatternAnalysisResults results)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(results.Burst);
        ArgumentNullException.ThrowIfNull(results.Short);
        ArgumentNullException.ThrowIfNull(results.Long);
        ArgumentNullException.ThrowIfNull(results.Deathstream);
        ArgumentNullException.ThrowIfNull(results.Stamina);
        ArgumentNullException.ThrowIfNull(results.Irregular);
        ArgumentNullException.ThrowIfNull(results.Speed);
        ArgumentNullException.ThrowIfNull(results.Flow);
        ArgumentNullException.ThrowIfNull(results.Alternating);

        var occurrences = new List<SectionStreamPatternOccurrence>();

        AddContiguous(occurrences, SectionStreamPatternType.Burst,
            results.Burst.Sequences.Select(sequence => (sequence.StartIndex, sequence.EndIndex)),
            results.Burst.Confidence);
        AddContiguous(occurrences, SectionStreamPatternType.Short,
            results.Short.Sequences.Select(sequence => (sequence.StartIndex, sequence.EndIndex)),
            results.Short.Confidence);
        AddContiguous(occurrences, SectionStreamPatternType.Long,
            results.Long.Sequences.Select(sequence => (sequence.StartIndex, sequence.EndIndex)),
            results.Long.Confidence);
        AddContiguous(occurrences, SectionStreamPatternType.Deathstream,
            results.Deathstream.Sequences.Select(sequence => (sequence.StartIndex, sequence.EndIndex)),
            results.Deathstream.Confidence);

        foreach (var sequence in results.Stamina.Sequences)
        {
            int[] rapidObjectIndices = sequence.Runs
                .SelectMany(run => InclusiveRange(run.StartIndex, run.EndIndex))
                .Distinct()
                .Order()
                .ToArray();
            if (rapidObjectIndices.Length == 0)
                continue;

            occurrences.Add(new SectionStreamPatternOccurrence(
                SectionStreamPatternType.Stamina,
                rapidObjectIndices[0],
                Array.AsReadOnly(rapidObjectIndices),
                results.Stamina.Confidence));
        }

        AddContiguous(occurrences, SectionStreamPatternType.Irregular,
            results.Irregular.Sequences.Select(sequence => (sequence.StartIndex, sequence.EndIndex)),
            results.Irregular.Confidence);
        AddContiguous(occurrences, SectionStreamPatternType.Speed,
            results.Speed.Sequences.Select(sequence => (sequence.StartIndex, sequence.EndIndex)),
            results.Speed.Confidence);
        AddContiguous(occurrences, SectionStreamPatternType.Flow,
            results.Flow.Sequences.Select(sequence => (sequence.StartIndex, sequence.EndIndex)),
            results.Flow.Confidence);
        AddContiguous(occurrences, SectionStreamPatternType.Alternating,
            results.Alternating.Sequences.Select(sequence => (sequence.StartIndex, sequence.EndIndex)),
            results.Alternating.Confidence);

        return Array.AsReadOnly(occurrences.ToArray());
    }

    private static void AddContiguous(
        ICollection<SectionStreamPatternOccurrence> destination,
        SectionStreamPatternType type,
        IEnumerable<(int StartIndex, int EndIndex)> sequences,
        double confidence)
    {
        foreach ((int startIndex, int endIndex) in sequences)
        {
            destination.Add(new SectionStreamPatternOccurrence(
                type,
                startIndex,
                Array.AsReadOnly(InclusiveRange(startIndex, endIndex).ToArray()),
                confidence));
        }
    }

    private static IEnumerable<int> InclusiveRange(int startIndex, int endIndex)
    {
        if (endIndex < startIndex)
            yield break;

        for (int objectIndex = startIndex; objectIndex <= endIndex; objectIndex++)
            yield return objectIndex;
    }
}
