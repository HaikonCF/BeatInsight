using BeatInsight.Analysis;
using BeatInsight.Models;
using System.Globalization;

namespace BeatInsight.Tests.Analysis;

public sealed class TechWindowDatasetPipelineTests
{
    [Theory]
    [InlineData(0, 0, "Unknown")]
    [InlineData(0, 49, "Unknown")]
    [InlineData(0, 50, "Tech")]
    [InlineData(0, 80, "Tech")]
    [InlineData(0, 100, "Tech")]
    public void LabelWindow_UsesCentralizedTechOverlapRule(
        double regionStart,
        double regionEnd,
        string expected)
    {
        IReadOnlyList<TechAnnotationRegion> regions = regionEnd > regionStart
            ? [new TechAnnotationRegion(regionStart, regionEnd, IsTech: true)]
            : [];

        TechWindowLabelResult result =
            TechWindowAnnotationLabeler.LabelWindow(
                0,
                100,
                new TechAnnotationSet(AnnotationComplete: false, regions));

        Assert.Equal(expected, result.Label.ToString());
        Assert.Equal(regionEnd / 100.0, result.TechOverlapRatio, precision: 10);
    }

    [Fact]
    public void LabelWindow_CompleteAnnotationTurnsUncoveredWindowIntoNotTech()
    {
        TechWindowLabelResult result =
            TechWindowAnnotationLabeler.LabelWindow(
                100,
                200,
                new TechAnnotationSet(
                    AnnotationComplete: true,
                    [new TechAnnotationRegion(0, 50, IsTech: true)]));

        Assert.Equal(TechWindowLabel.NotTech, result.Label);
        Assert.Equal(0, result.TechOverlapRatio);
        Assert.False(result.IsBoundaryWindow);
    }

    [Fact]
    public void LabelWindow_IncompleteAnnotationLeavesUncoveredWindowUnknown()
    {
        TechWindowLabelResult result =
            TechWindowAnnotationLabeler.LabelWindow(
                100,
                200,
                new TechAnnotationSet(
                    AnnotationComplete: false,
                    [new TechAnnotationRegion(0, 50, IsTech: true)]));

        Assert.Equal(TechWindowLabel.Unknown, result.Label);
        Assert.Equal(0, result.TechOverlapRatio);
    }

    [Fact]
    public void LabelWindow_InvalidTimeRangeIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TechWindowAnnotationLabeler.LabelWindow(
                100,
                100,
                new TechAnnotationSet(AnnotationComplete: false, Regions: [])));
    }

    [Fact]
    public void Build_TestAnnotation_ProducesRowsStatisticsAndStableSampleIds()
    {
        // TEST ANNOTATION only: this validates overlap -> label -> dataset
        // mechanics; it is not a final human ground-truth annotation.
        Beatmap exit = FixtureLoader.Load(
            "Exit This Earth's Atmosphere [Primordial Nucleosynthesis].osu");
        TechWindowDatasetSource source = new(
            exit,
            StableBeatmapId: "test-exit-primordial",
            BeatmapId: null,
            BeatmapSetId: null,
            MapName: "Exit, \"Primordial\"",
            Difficulty: "Primordial Nucleosynthesis",
            Annotations: new TechAnnotationSet(
                AnnotationComplete: false,
                [new TechAnnotationRegion(284_000, 296_500, IsTech: true)]));

        TechWindowDatasetBuildResult result =
            TechWindowDatasetBuilder.Build([source]);

        Assert.NotEmpty(result.Rows);
        Assert.Equal(result.Rows.Count, result.Statistics.TotalWindows);
        Assert.Equal(1, result.Statistics.TotalBeatmaps);
        Assert.Equal(
            result.Rows.Count,
            result.Statistics.TechWindows
            + result.Statistics.NotTechWindows
            + result.Statistics.IgnoredWindows);
        Assert.All(result.Rows, row =>
            Assert.Equal(
                TechWindowDatasetBuilder.CreateSampleId(
                    "test-exit-primordial",
                    row.StartIndex,
                    row.EndIndex),
                row.SampleId));
        Assert.Contains(result.Rows, row => row.Label == TechWindowLabel.Tech);
        Assert.Contains(result.Rows, row => row.Label == TechWindowLabel.Unknown);
    }

    [Fact]
    public void Build_DuplicateStableMapIdentifiersAreRejected()
    {
        Beatmap map = FixtureLoader.Load("Tower Of Heaven [Extra].osu");
        TechWindowDatasetSource first = CreateCompleteSource(map, "duplicate");
        TechWindowDatasetSource second = CreateCompleteSource(map, "duplicate");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => TechWindowDatasetBuilder.Build([first, second]));

        Assert.Contains("Duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Csv_IsDeterministicInvariantAndEscapesText_AndTrainingExcludesUnknown()
    {
        TechV2ExperimentalWindowResult window = CreateWindow();
        TechWindowDatasetRow unknown = new(
            "map:4-15", "map", 1, 2, "A, \"quoted\" map", "Diff",
            4, 15, 1.25, 2.5, window, 0.49, true, TechWindowLabel.Unknown);
        TechWindowDatasetRow tech = unknown with
        {
            SampleId = "map:0-11",
            Label = TechWindowLabel.Tech,
            TechOverlapRatio = 0.5,
        };

        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            string first = TechWindowDatasetCsvExporter.CreateCsv(
                [unknown, tech],
                includeUnknown: false);
            string second = TechWindowDatasetCsvExporter.CreateCsv(
                [tech, unknown],
                includeUnknown: false);

            Assert.Equal(first, second);
            Assert.Contains("\"A, \"\"quoted\"\" map\"", first);
            Assert.Contains("1.25", first);
            Assert.DoesNotContain("1,25", first);
            Assert.Contains("map:0-11", first);
            Assert.DoesNotContain("map:4-15", first);

            string audit = TechWindowDatasetCsvExporter.CreateCsv(
                [unknown, tech],
                includeUnknown: true);
            Assert.Contains("map:4-15", audit);
            Assert.Contains(",true,", audit);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    private static TechWindowDatasetSource CreateCompleteSource(
        Beatmap map,
        string stableBeatmapId) =>
        new(
            map,
            stableBeatmapId,
            BeatmapId: null,
            BeatmapSetId: null,
            MapName: map.Title,
            Difficulty: map.Version,
            Annotations: new TechAnnotationSet(
                AnnotationComplete: true,
                Regions: []));

    private static IReadOnlyList<TechV2ExperimentalWindowResult> Analyze(
        Beatmap map) =>
        TechDetectorV2Experimental.Analyze(
            map.HitObjects,
            GameplayAnalyzer.IsSpinner,
            GameplayAnalyzer.IsSlider,
            GameplayAnalyzer.IsComplexSlider,
            GameplayAnalyzer.GetObjectStartPosition,
            GameplayAnalyzer.GetObjectEndPosition,
            GameplayAnalyzer.GetTechTurnAngle);

    private static TechV2ExperimentalWindowResult CreateWindow() =>
        new(
            4,
            15,
            1.25,
            2.5,
            0.1,
            0.2,
            0.3,
            0.4,
            0.5,
            0.49,
            0.6,
            3,
            new TechV2ExperimentalSliderTransitionTopology(
                11,
                0.1,
                0.2,
                0.3,
                0.4),
            3,
            0.5,
            2);
}
