using System.Collections.ObjectModel;
using BeatInsight.Models.Sections;

namespace BeatInsight.Analysis.Sections;

/// <summary>
/// Projects already-calculated Stream Pattern occurrences onto timeline
/// sections. This type never invokes a gameplay or Stream analyzer.
/// </summary>
public static class SectionStreamPatternAggregator
{
    public static IReadOnlyList<GameplaySectionAnalysis> Aggregate(
        IReadOnlyList<GameplaySectionAnalysis> sections,
        SectionStreamPatternAnalysisResults results) =>
        Aggregate(sections, SectionStreamPatternOccurrenceAdapter.Adapt(results));

    public static IReadOnlyList<GameplaySectionAnalysis> Aggregate(
        IReadOnlyList<GameplaySectionAnalysis> sections,
        IReadOnlyList<SectionStreamPatternOccurrence> occurrences)
    {
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(occurrences);

        if (sections.Count == 0)
        {
            if (occurrences.Count != 0)
                throw new ArgumentException(
                    "Stream occurrences cannot be assigned without sections.",
                    nameof(occurrences));
            return Array.Empty<GameplaySectionAnalysis>();
        }

        Dictionary<int, int> objectToSection = BuildObjectToSection(sections);
        ValidateLengthExclusivity(occurrences);
        var states = Enumerable.Range(0, sections.Count)
            .Select(_ => new SectionState())
            .ToArray();

        foreach (SectionStreamPatternOccurrence occurrence in occurrences)
        {
            if (occurrence.ObjectIndices.Count == 0)
                throw new ArgumentException("Stream occurrences must contain objects.", nameof(occurrences));
            if (!occurrence.ObjectIndices.Contains(occurrence.StartObjectIndex))
                throw new ArgumentException(
                    "A Stream occurrence start index must belong to its object indices.",
                    nameof(occurrences));
            if (!double.IsFinite(occurrence.Confidence))
                throw new ArgumentException("Stream occurrence confidence must be finite.", nameof(occurrences));
            if (!objectToSection.TryGetValue(occurrence.StartObjectIndex, out int startSectionIndex))
                throw new ArgumentException(
                    $"Stream occurrence start index {occurrence.StartObjectIndex} is not present in any section.",
                    nameof(occurrences));

            PatternState startState = states[startSectionIndex].For(occurrence.Type);
            startState.SequenceCount++;
            startState.ConfidenceTotal += occurrence.Confidence;

            foreach (int objectIndex in occurrence.ObjectIndices.Distinct())
            {
                if (!objectToSection.TryGetValue(objectIndex, out int sectionIndex))
                    throw new ArgumentException(
                        $"Stream occurrence object index {objectIndex} is not present in any section.",
                        nameof(occurrences));
                states[sectionIndex].For(occurrence.Type).ObjectIndices.Add(objectIndex);
            }
        }

        GameplaySectionAnalysis[] enriched = new GameplaySectionAnalysis[sections.Count];
        for (int index = 0; index < sections.Count; index++)
        {
            SectionState state = states[index];
            int objectCount = sections[index].ObjectCount;
            enriched[index] = sections[index] with
            {
                StreamPatterns = new SectionStreamPatternProfile(
                    state.Burst.ToMetric(objectCount),
                    state.Short.ToMetric(objectCount),
                    state.Long.ToMetric(objectCount),
                    state.Deathstream.ToMetric(objectCount),
                    state.Stamina.ToMetric(objectCount),
                    state.Irregular.ToMetric(objectCount),
                    state.Speed.ToMetric(objectCount),
                    state.Flow.ToMetric(objectCount),
                    state.Alternating.ToMetric(objectCount))
            };
        }

        return Array.AsReadOnly(enriched);
    }

    private static Dictionary<int, int> BuildObjectToSection(
        IReadOnlyList<GameplaySectionAnalysis> sections)
    {
        var result = new Dictionary<int, int>();
        for (int sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            foreach (int objectIndex in sections[sectionIndex].ObjectIndices)
            {
                if (!result.TryAdd(objectIndex, sectionIndex))
                    throw new ArgumentException(
                        $"Object index {objectIndex} belongs to more than one section.",
                        nameof(sections));
            }
        }
        return result;
    }

    private static void ValidateLengthExclusivity(
        IReadOnlyList<SectionStreamPatternOccurrence> occurrences)
    {
        var ownerByObject = new Dictionary<int, SectionStreamPatternType>();
        foreach (SectionStreamPatternOccurrence occurrence in occurrences.Where(
            occurrence => IsLengthPattern(occurrence.Type)))
        {
            foreach (int objectIndex in occurrence.ObjectIndices.Distinct())
            {
                if (ownerByObject.TryGetValue(objectIndex, out SectionStreamPatternType owner)
                    && owner != occurrence.Type)
                {
                    throw new ArgumentException(
                        $"Stream length object index {objectIndex} belongs to both {owner} and {occurrence.Type}.",
                        nameof(occurrences));
                }
                ownerByObject[objectIndex] = occurrence.Type;
            }
        }
    }

    private static bool IsLengthPattern(SectionStreamPatternType type) =>
        type is SectionStreamPatternType.Burst
            or SectionStreamPatternType.Short
            or SectionStreamPatternType.Long
            or SectionStreamPatternType.Deathstream;

    private sealed class SectionState
    {
        public PatternState Burst { get; } = new();
        public PatternState Short { get; } = new();
        public PatternState Long { get; } = new();
        public PatternState Deathstream { get; } = new();
        public PatternState Stamina { get; } = new();
        public PatternState Irregular { get; } = new();
        public PatternState Speed { get; } = new();
        public PatternState Flow { get; } = new();
        public PatternState Alternating { get; } = new();

        public PatternState For(SectionStreamPatternType type) => type switch
        {
            SectionStreamPatternType.Burst => Burst,
            SectionStreamPatternType.Short => Short,
            SectionStreamPatternType.Long => Long,
            SectionStreamPatternType.Deathstream => Deathstream,
            SectionStreamPatternType.Stamina => Stamina,
            SectionStreamPatternType.Irregular => Irregular,
            SectionStreamPatternType.Speed => Speed,
            SectionStreamPatternType.Flow => Flow,
            SectionStreamPatternType.Alternating => Alternating,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    }

    private sealed class PatternState
    {
        public HashSet<int> ObjectIndices { get; } = [];
        public int SequenceCount { get; set; }
        public double ConfidenceTotal { get; set; }

        public SectionPatternMetric ToMetric(int sectionObjectCount)
        {
            int[] indices = ObjectIndices.Order().ToArray();
            double coverage = sectionObjectCount == 0
                ? 0
                : (double)indices.Length / sectionObjectCount;
            return new SectionPatternMetric(
                SequenceCount,
                Math.Clamp(coverage, 0, 1),
                SequenceCount == 0 ? 0 : ConfidenceTotal / SequenceCount,
                new ReadOnlyCollection<int>(indices));
        }
    }
}
