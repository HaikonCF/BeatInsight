using System.Collections.ObjectModel;
using BeatInsight.Models.JumpPatterns;
using BeatInsight.Models.Sections;

namespace BeatInsight.Analysis.Sections;

/// <summary>
/// Projects already-calculated Jump Pattern matches onto timeline sections.
/// This type never invokes JumpPatternAnalyzer or any detector.
/// </summary>
public static class SectionJumpPatternAggregator
{
    public static IReadOnlyList<GameplaySectionAnalysis> Aggregate(
        IReadOnlyList<GameplaySectionAnalysis> sections,
        IReadOnlyList<JumpPatternMatch> jumpPatternMatches)
    {
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(jumpPatternMatches);

        if (sections.Count == 0)
        {
            if (jumpPatternMatches.Count != 0)
                throw new ArgumentException(
                    "Jump occurrences cannot be assigned without sections.",
                    nameof(jumpPatternMatches));

            return Array.Empty<GameplaySectionAnalysis>();
        }

        Dictionary<int, int> objectToSection = BuildObjectToSection(sections);
        var states = Enumerable.Range(0, sections.Count)
            .Select(_ => new SectionState())
            .ToArray();

        foreach (JumpPatternMatch occurrence in jumpPatternMatches)
        {
            if (!TryGetSlot(occurrence.Type, out PatternSlot slot))
                continue;

            if (occurrence.EndObjectIndex < occurrence.StartObjectIndex)
            {
                throw new ArgumentException(
                    "Jump occurrence end indices must not precede start indices.",
                    nameof(jumpPatternMatches));
            }

            if (!objectToSection.TryGetValue(
                    occurrence.StartObjectIndex,
                    out int startSectionIndex))
            {
                throw new ArgumentException(
                    $"Jump occurrence start index {occurrence.StartObjectIndex} is not present in any section.",
                    nameof(jumpPatternMatches));
            }

            PatternState startState = states[startSectionIndex].For(slot);
            startState.SequenceCount++;
            startState.ConfidenceTotal += occurrence.Confidence;

            if (occurrence.Type == JumpPatternType.Accelerating)
                states[startSectionIndex].AcceleratingSequenceCount++;
            else if (occurrence.Type == JumpPatternType.Decelerating)
                states[startSectionIndex].DeceleratingSequenceCount++;

            for (int objectIndex = occurrence.StartObjectIndex;
                 objectIndex <= occurrence.EndObjectIndex;
                 objectIndex++)
            {
                if (!objectToSection.TryGetValue(objectIndex, out int sectionIndex))
                {
                    throw new ArgumentException(
                        $"Jump occurrence object index {objectIndex} is not present in any section.",
                        nameof(jumpPatternMatches));
                }

                states[sectionIndex].For(slot).ObjectIndices.Add(objectIndex);
            }
        }

        GameplaySectionAnalysis[] enriched = new GameplaySectionAnalysis[sections.Count];
        for (int index = 0; index < sections.Count; index++)
        {
            SectionState state = states[index];
            int sectionObjectCount = sections[index].ObjectCount;
            enriched[index] = sections[index] with
            {
                JumpPatterns = new SectionJumpPatternProfile(
                    state.Linear.ToMetric(sectionObjectCount),
                    state.Edge.ToMetric(sectionObjectCount),
                    state.Sharp.ToMetric(sectionObjectCount),
                    state.Triangle.ToMetric(sectionObjectCount),
                    state.OneTwo.ToMetric(sectionObjectCount),
                    state.Spaced.ToMetric(sectionObjectCount),
                    state.WideAngle.ToMetric(sectionObjectCount),
                    state.Reversal.ToMetric(sectionObjectCount),
                    state.Arc.ToMetric(sectionObjectCount),
                    state.SpacingTrend.ToMetric(sectionObjectCount),
                    state.AcceleratingSequenceCount,
                    state.DeceleratingSequenceCount,
                    state.AlternatingSpacing.ToMetric(sectionObjectCount))
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
                {
                    throw new ArgumentException(
                        $"Object index {objectIndex} belongs to more than one section.",
                        nameof(sections));
                }
            }
        }

        return result;
    }

    private static bool TryGetSlot(JumpPatternType type, out PatternSlot slot)
    {
        slot = type switch
        {
            JumpPatternType.Linear => PatternSlot.Linear,
            JumpPatternType.Edge => PatternSlot.Edge,
            JumpPatternType.Sharp => PatternSlot.Sharp,
            JumpPatternType.Triangle => PatternSlot.Triangle,
            JumpPatternType.OneTwo => PatternSlot.OneTwo,
            JumpPatternType.Spaced => PatternSlot.Spaced,
            JumpPatternType.WideAngle => PatternSlot.WideAngle,
            JumpPatternType.Reversal => PatternSlot.Reversal,
            JumpPatternType.Arc => PatternSlot.Arc,
            JumpPatternType.Accelerating or JumpPatternType.Decelerating
                => PatternSlot.SpacingTrend,
            JumpPatternType.AlternatingSpacing => PatternSlot.AlternatingSpacing,
            _ => PatternSlot.Unsupported
        };

        return slot != PatternSlot.Unsupported;
    }

    private enum PatternSlot
    {
        Unsupported,
        Linear,
        Edge,
        Sharp,
        Triangle,
        OneTwo,
        Spaced,
        WideAngle,
        Reversal,
        Arc,
        SpacingTrend,
        AlternatingSpacing
    }

    private sealed class SectionState
    {
        public PatternState Linear { get; } = new();
        public PatternState Edge { get; } = new();
        public PatternState Sharp { get; } = new();
        public PatternState Triangle { get; } = new();
        public PatternState OneTwo { get; } = new();
        public PatternState Spaced { get; } = new();
        public PatternState WideAngle { get; } = new();
        public PatternState Reversal { get; } = new();
        public PatternState Arc { get; } = new();
        public PatternState SpacingTrend { get; } = new();
        public PatternState AlternatingSpacing { get; } = new();
        public int AcceleratingSequenceCount { get; set; }
        public int DeceleratingSequenceCount { get; set; }

        public PatternState For(PatternSlot slot) => slot switch
        {
            PatternSlot.Linear => Linear,
            PatternSlot.Edge => Edge,
            PatternSlot.Sharp => Sharp,
            PatternSlot.Triangle => Triangle,
            PatternSlot.OneTwo => OneTwo,
            PatternSlot.Spaced => Spaced,
            PatternSlot.WideAngle => WideAngle,
            PatternSlot.Reversal => Reversal,
            PatternSlot.Arc => Arc,
            PatternSlot.SpacingTrend => SpacingTrend,
            PatternSlot.AlternatingSpacing => AlternatingSpacing,
            _ => throw new ArgumentOutOfRangeException(nameof(slot))
        };
    }

    private sealed class PatternState
    {
        public HashSet<int> ObjectIndices { get; } = [];
        public int SequenceCount { get; set; }
        public double ConfidenceTotal { get; set; }

        public SectionPatternMetric ToMetric(int sectionObjectCount)
        {
            int[] objectIndices = ObjectIndices.Order().ToArray();
            double coverage = sectionObjectCount == 0
                ? 0
                : (double)objectIndices.Length / sectionObjectCount;
            double averageConfidence = SequenceCount == 0
                ? 0
                : ConfidenceTotal / SequenceCount;

            return new SectionPatternMetric(
                SequenceCount,
                Math.Clamp(coverage, 0, 1),
                averageConfidence,
                new ReadOnlyCollection<int>(objectIndices));
        }
    }
}
