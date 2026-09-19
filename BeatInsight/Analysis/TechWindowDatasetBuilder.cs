using BeatInsight.Models;

namespace BeatInsight.Analysis;

internal sealed record TechWindowDatasetSource(
    Beatmap Beatmap,
    string StableBeatmapId,
    long? BeatmapId,
    long? BeatmapSetId,
    string MapName,
    string Difficulty,
    TechAnnotationSet Annotations);

internal sealed record TechWindowDatasetRow(
    string SampleId,
    string StableBeatmapId,
    long? BeatmapId,
    long? BeatmapSetId,
    string MapName,
    string Difficulty,
    int StartIndex,
    int EndIndex,
    double StartTime,
    double EndTime,
    TechV2ExperimentalWindowResult Window,
    double TechOverlapRatio,
    bool IsBoundaryWindow,
    TechWindowLabel Label)
{
    internal bool? IsTech => Label switch
    {
        TechWindowLabel.Tech => true,
        TechWindowLabel.NotTech => false,
        _ => null,
    };
}

internal sealed record TechWindowDatasetMapStatistics(
    string MapName,
    int TechWindows,
    int NotTechWindows,
    int IgnoredWindows,
    int BoundaryWindows);

internal sealed record TechWindowDatasetStatistics(
    int TotalBeatmaps,
    int TotalWindows,
    int TechWindows,
    int NotTechWindows,
    int IgnoredWindows,
    int BoundaryWindows,
    IReadOnlyList<TechWindowDatasetMapStatistics> PerMap);

internal sealed record TechWindowDatasetBuildResult(
    IReadOnlyList<TechWindowDatasetRow> Rows,
    TechWindowDatasetStatistics Statistics);

/// <summary>
/// Builds a reproducible window-level dataset from the existing Tech V2
/// feature extraction. It never changes runtime Tech decisions.
/// </summary>
internal static class TechWindowDatasetBuilder
{
    internal static TechWindowDatasetBuildResult Build(
        IEnumerable<TechWindowDatasetSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        List<TechWindowDatasetRow> rows = [];
        List<TechWindowDatasetMapStatistics> perMap = [];
        HashSet<string> sampleIds = new(StringComparer.Ordinal);
        int mapCount = 0;

        foreach (TechWindowDatasetSource source in sources
            .OrderBy(source => source.StableBeatmapId, StringComparer.Ordinal))
        {
            ValidateSource(source);
            mapCount++;

            IReadOnlyList<TechV2ExperimentalWindowResult> windows =
                TechDetectorV2Experimental.Analyze(
                    source.Beatmap.HitObjects,
                    GameplayAnalyzer.IsSpinner,
                    GameplayAnalyzer.IsSlider,
                    GameplayAnalyzer.IsComplexSlider,
                    GameplayAnalyzer.GetObjectStartPosition,
                    GameplayAnalyzer.GetObjectEndPosition,
                    GameplayAnalyzer.GetTechTurnAngle);

            int tech = 0;
            int notTech = 0;
            int ignored = 0;
            int boundary = 0;

            foreach (TechV2ExperimentalWindowResult window in windows)
            {
                TechWindowLabelResult label =
                    TechWindowAnnotationLabeler.LabelWindow(
                        window.StartTime,
                        window.EndTime,
                        source.Annotations);

                string sampleId = CreateSampleId(
                    source.StableBeatmapId,
                    window.StartIndex,
                    window.EndIndex);

                if (!sampleIds.Add(sampleId))
                {
                    throw new InvalidOperationException(
                        $"Duplicate Tech window SampleId detected: {sampleId}");
                }

                ValidateRow(window, label);

                rows.Add(new TechWindowDatasetRow(
                    sampleId,
                    source.StableBeatmapId,
                    source.BeatmapId,
                    source.BeatmapSetId,
                    source.MapName,
                    source.Difficulty,
                    window.StartIndex,
                    window.EndIndex,
                    window.StartTime,
                    window.EndTime,
                    window,
                    label.TechOverlapRatio,
                    label.IsBoundaryWindow,
                    label.Label));

                switch (label.Label)
                {
                    case TechWindowLabel.Tech:
                        tech++;
                        break;
                    case TechWindowLabel.NotTech:
                        notTech++;
                        break;
                    default:
                        ignored++;
                        break;
                }

                if (label.IsBoundaryWindow)
                    boundary++;
            }

            perMap.Add(new TechWindowDatasetMapStatistics(
                source.MapName,
                tech,
                notTech,
                ignored,
                boundary));
        }

        IReadOnlyList<TechWindowDatasetRow> orderedRows = rows
            .OrderBy(row => row.SampleId, StringComparer.Ordinal)
            .ToArray();

        return new TechWindowDatasetBuildResult(
            orderedRows,
            new TechWindowDatasetStatistics(
                mapCount,
                orderedRows.Count,
                orderedRows.Count(row => row.Label == TechWindowLabel.Tech),
                orderedRows.Count(row => row.Label == TechWindowLabel.NotTech),
                orderedRows.Count(row => row.Label == TechWindowLabel.Unknown),
                orderedRows.Count(row => row.IsBoundaryWindow),
                perMap.OrderBy(map => map.MapName, StringComparer.Ordinal).ToArray()));
    }

    internal static string CreateSampleId(
        string stableBeatmapId,
        int startIndex,
        int endIndex) =>
        $"{stableBeatmapId}:{startIndex}-{endIndex}";

    private static void ValidateSource(TechWindowDatasetSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(source.Beatmap);
        ArgumentNullException.ThrowIfNull(source.Annotations);

        if (string.IsNullOrWhiteSpace(source.StableBeatmapId))
        {
            throw new ArgumentException(
                "A stable beatmap identifier is required for a reproducible dataset.",
                nameof(source));
        }
    }

    private static void ValidateRow(
        TechV2ExperimentalWindowResult window,
        TechWindowLabelResult label)
    {
        if (window.EndTime <= window.StartTime)
        {
            throw new InvalidOperationException(
                $"Invalid Tech window time range: {window.StartTime} to {window.EndTime}.");
        }

        double[] normalizedFeatures =
        [
            window.GeometryIrregularity,
            window.RhythmIrregularity,
            window.SliderComplexity,
            window.Awkwardness,
            window.SpacingVariation,
            window.AngleCoherenceWithSupport,
            window.ComplexSliderTransitionTopology.SliderToSliderRatio,
            window.ComplexSliderTransitionTopology.SliderToCircleRatio,
            window.ComplexSliderTransitionTopology.CircleToSliderRatio,
            window.ComplexSliderTransitionTopology.CircleToCircleRatio,
            window.CoupledTechnicalMotifContinuity,
            label.TechOverlapRatio,
        ];

        if (normalizedFeatures.Any(value =>
            double.IsNaN(value)
            || double.IsInfinity(value)
            || value < 0
            || value > 1))
        {
            throw new InvalidOperationException(
                "Tech window dataset contains a non-finite or out-of-range normalized value.");
        }

        if (window.ComplexSliderRunLength < 0
            || window.ComplexSliderRunLength > TechDetectorV2Experimental.WindowSize)
        {
            throw new InvalidOperationException(
                "ComplexSliderRunLength is outside the window bounds.");
        }
    }
}
