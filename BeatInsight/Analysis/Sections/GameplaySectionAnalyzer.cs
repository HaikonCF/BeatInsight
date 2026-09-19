using BeatInsight.Models;
using BeatInsight.Models.Sections;

namespace BeatInsight.Analysis.Sections;

/// <summary>
/// Builds the global temporal partition used by Section Analysis V1.
/// This phase only creates boundaries and assigns source HitObject indices;
/// it contains no gameplay, pattern, skill, identity or rating logic.
/// </summary>
public static class GameplaySectionAnalyzer
{
    public const double TargetSectionDurationMilliseconds = 7_500.0;
    public const int MinimumSectionCount = 12;
    public const int MaximumSectionCount = 32;

    public static IReadOnlyList<GameplaySectionAnalysis> Analyze(
        Beatmap beatmap)
    {
        ArgumentNullException.ThrowIfNull(beatmap);
        return Analyze(beatmap.HitObjects);
    }

    public static IReadOnlyList<GameplaySectionAnalysis> Analyze(
        IReadOnlyList<HitObject> objects)
    {
        ArgumentNullException.ThrowIfNull(objects);

        if (objects.Count == 0)
            return Array.Empty<GameplaySectionAnalysis>();

        EnsureChronologicalOrder(objects);

        double analysisStart = objects[0].Time;
        double analysisEnd = objects[^1].Time;
        double analyzableDuration = analysisEnd - analysisStart;

        if (analyzableDuration <= 0)
        {
            return
            [
                CreateSection(
                    index: 0,
                    startTime: analysisStart,
                    endTime: analysisEnd,
                    objectIndices: Enumerable.Range(0, objects.Count),
                    objects)
            ];
        }

        int sectionCount = CalculateSectionCount(analyzableDuration);
        List<int>[] assignments = Enumerable.Range(0, sectionCount)
            .Select(_ => new List<int>())
            .ToArray();

        for (int objectIndex = 0; objectIndex < objects.Count; objectIndex++)
        {
            double offset = objects[objectIndex].Time - analysisStart;
            int sectionIndex = objects[objectIndex].Time == analysisEnd
                ? sectionCount - 1
                : (int)Math.Floor(
                    offset * sectionCount / analyzableDuration);

            sectionIndex = Math.Clamp(sectionIndex, 0, sectionCount - 1);
            assignments[sectionIndex].Add(objectIndex);
        }

        GameplaySectionAnalysis[] sections =
            new GameplaySectionAnalysis[sectionCount];

        for (int sectionIndex = 0;
             sectionIndex < sectionCount;
             sectionIndex++)
        {
            double sectionStart = analysisStart
                + (sectionIndex * analyzableDuration / sectionCount);
            double sectionEnd = sectionIndex == sectionCount - 1
                ? analysisEnd
                : analysisStart
                    + ((sectionIndex + 1) * analyzableDuration / sectionCount);

            sections[sectionIndex] = CreateSection(
                sectionIndex,
                sectionStart,
                sectionEnd,
                assignments[sectionIndex],
                objects);
        }

        return Array.AsReadOnly(sections);
    }

    /// <summary>
    /// Calculates the hybrid section count using an explicit away-from-zero
    /// midpoint rule. This avoids Math.Round's default banker rounding.
    /// </summary>
    public static int CalculateSectionCount(double analyzableDuration)
    {
        if (!double.IsFinite(analyzableDuration)
            || analyzableDuration <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(analyzableDuration),
                "Analyzable duration must be finite and greater than zero.");
        }

        double rawSectionCount = Math.Round(
            analyzableDuration / TargetSectionDurationMilliseconds,
            MidpointRounding.AwayFromZero);

        return (int)Math.Clamp(
            rawSectionCount,
            MinimumSectionCount,
            MaximumSectionCount);
    }

    private static void EnsureChronologicalOrder(
        IReadOnlyList<HitObject> objects)
    {
        for (int index = 1; index < objects.Count; index++)
        {
            if (objects[index].Time < objects[index - 1].Time)
            {
                throw new ArgumentException(
                    "Section analysis requires HitObjects in chronological order.",
                    nameof(objects));
            }
        }
    }

    private static GameplaySectionAnalysis CreateSection(
        int index,
        double startTime,
        double endTime,
        IEnumerable<int> objectIndices,
        IReadOnlyList<HitObject> objects)
    {
        int[] indices = objectIndices.ToArray();
        int circleCount = 0;
        int sliderCount = 0;
        int spinnerCount = 0;
        int otherObjectCount = 0;

        foreach (int objectIndex in indices)
        {
            HitObject hitObject = objects[objectIndex];

            if ((hitObject.Type & 1) == 1)
                circleCount++;
            else if ((hitObject.Type & 2) == 2)
                sliderCount++;
            else if ((hitObject.Type & 8) == 8)
                spinnerCount++;
            else
                otherObjectCount++;
        }

        return new GameplaySectionAnalysis(
            index,
            startTime,
            endTime,
            circleCount,
            sliderCount,
            spinnerCount,
            otherObjectCount,
            Array.AsReadOnly(indices));
    }
}
