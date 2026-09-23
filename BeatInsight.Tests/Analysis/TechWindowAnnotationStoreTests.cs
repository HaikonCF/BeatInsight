using BeatInsight.Analysis;
using BeatInsight.Models;
using System.IO;
using System.Text;

namespace BeatInsight.Tests.Analysis;

public sealed class TechWindowAnnotationStoreTests
{
    [Fact]
    public void LoadAll_ReportsIncompleteEmptyInvalidOverlapAndDuplicate_AndNormalizesRanges()
    {
        using TemporaryDirectory directory = new();

        WriteJson(directory.Path, "a-alpha.json", """
            {
              "stableBeatmapId": "alpha",
              "mapName": "Alpha",
              "difficulty": "Test",
              "annotationComplete": false,
              "techRegions": [
                { "startTime": 100, "endTime": 200 },
                { "startTime": 150, "endTime": 250 }
              ]
            }
            """);
        WriteJson(directory.Path, "b-empty.json", """
            {
              "stableBeatmapId": "complete-empty",
              "annotationComplete": true,
              "techRegions": []
            }
            """);
        WriteJson(directory.Path, "c-invalid.json", """
            {
              "stableBeatmapId": "invalid",
              "annotationComplete": true,
              "techRegions": [{ "startTime": 200, "endTime": 100 }]
            }
            """);
        WriteJson(directory.Path, "z-duplicate.json", """
            {
              "stableBeatmapId": "alpha",
              "annotationComplete": true,
              "techRegions": []
            }
            """);

        TechWindowAnnotationLoadResult result =
            new TechWindowAnnotationStore(directory.Path).LoadAll();

        Assert.Equal(["alpha", "complete-empty"],
            result.Annotations.Select(annotation => annotation.StableBeatmapId));

        TechWindowAnnotationDocument alpha = Assert.Single(
            result.Annotations,
            annotation => annotation.StableBeatmapId == "alpha");
        TechWindowAnnotationRegionDocument mergedRegion =
            Assert.Single(alpha.TechRegions);
        Assert.Equal(100, mergedRegion.StartTime);
        Assert.Equal(250, mergedRegion.EndTime);

        Assert.Single(result.AuditReport.IncompleteAnnotations);
        Assert.Single(result.AuditReport.CompleteAnnotationsWithoutTechRegions);
        Assert.Single(result.AuditReport.InvalidRegions);
        Assert.Single(result.AuditReport.OverlappingRegions);
        Assert.Single(result.AuditReport.DuplicateBeatmapAnnotations);
    }

    [Fact]
    public void Save_IsDeterministicCamelCaseAndRoundTripsNormalizedRegions()
    {
        using TemporaryDirectory directory = new();
        TechWindowAnnotationStore store = new(directory.Path);
        TechWindowAnnotationDocument document = new()
        {
            StableBeatmapId = "set/map",
            MapName = "Example",
            Difficulty = "Test",
            AnnotationComplete = true,
            TechRegions =
            [
                new TechWindowAnnotationRegionDocument { StartTime = 100, EndTime = 200 },
                new TechWindowAnnotationRegionDocument { StartTime = 150, EndTime = 300 }
            ]
        };

        store.Save(document);
        string annotationPath = Assert.Single(Directory.EnumerateFiles(directory.Path));
        byte[] first = File.ReadAllBytes(annotationPath);
        string json = File.ReadAllText(annotationPath, Encoding.UTF8);

        store.Save(document);
        byte[] second = File.ReadAllBytes(annotationPath);
        TechWindowAnnotationDocument loaded = Assert.Single(store.LoadAll().Annotations);

        Assert.Equal(first, second);
        Assert.False(first.Length >= 3 && first[0] == 0xEF && first[1] == 0xBB && first[2] == 0xBF);
        Assert.Contains("\"stableBeatmapId\"", json);
        Assert.Single(loaded.TechRegions);
        Assert.Equal(100, loaded.TechRegions[0].StartTime);
        Assert.Equal(300, loaded.TechRegions[0].EndTime);
    }

    [Fact]
    public void Pipeline_BuildsAndExportsAvailableAnnotationsWithoutLikelihoodLabels()
    {
        using TemporaryDirectory directory = new();
        TechWindowAnnotationStore store = new(directory.Path);
        store.Save(new TechWindowAnnotationDocument
        {
            StableBeatmapId = "test-exit",
            MapName = "Exit TEST ANNOTATION",
            Difficulty = "Primordial Nucleosynthesis",
            AnnotationComplete = false,
            TechRegions =
            [
                new TechWindowAnnotationRegionDocument
                {
                    StartTime = 284_000,
                    EndTime = 296_500
                }
            ]
        });

        Beatmap exit = FixtureLoader.Load(
            "Exit This Earth's Atmosphere [Primordial Nucleosynthesis].osu");
        TechWindowAnnotationDatasetPipeline pipeline = new(store);
        TechWindowAnnotationDatasetBuildResult result = pipeline.Build(
            annotation => annotation.StableBeatmapId == "test-exit" ? exit : null);

        string trainingPath = Path.Combine(directory.Path, "training.csv");
        string auditPath = Path.Combine(directory.Path, "audit.csv");
        pipeline.ExportCsv(result, trainingPath, auditPath);

        Assert.Empty(result.UnresolvedBeatmaps);
        Assert.Equal(1, result.Dataset.Statistics.TotalBeatmaps);
        Assert.True(result.Dataset.Statistics.TechWindows > 0);
        Assert.True(result.Dataset.Statistics.IgnoredWindows > 0);
        Assert.All(result.Dataset.Rows, row =>
        {
            Assert.InRange(row.Window.GeometryIrregularity, 0, 1);
            Assert.InRange(row.Window.CoupledTechnicalMotifContinuity, 0, 1);
        });

        string training = File.ReadAllText(trainingPath);
        string audit = File.ReadAllText(auditPath);
        Assert.Equal(
            result.Dataset.Rows.Count(row => row.Label != TechWindowLabel.Unknown) + 1,
            training.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Equal(
            result.Dataset.Rows.Count + 1,
            audit.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.DoesNotContain("Likelihood", training);
    }

    [Fact]
    public void Pipeline_ReportsUnresolvedBeatmapsWithoutCreatingDatasetRows()
    {
        using TemporaryDirectory directory = new();
        new TechWindowAnnotationStore(directory.Path).Save(new TechWindowAnnotationDocument
        {
            StableBeatmapId = "not-local",
            AnnotationComplete = true
        });

        TechWindowAnnotationDatasetBuildResult result =
            new TechWindowAnnotationDatasetPipeline(
                new TechWindowAnnotationStore(directory.Path))
            .Build(_ => null);

        TechWindowAnnotationIssue issue = Assert.Single(result.UnresolvedBeatmaps);
        Assert.Equal(TechWindowAnnotationIssueKind.UnresolvedBeatmap, issue.Kind);
        Assert.Empty(result.Dataset.Rows);
        Assert.Equal(0, result.Dataset.Statistics.TotalBeatmaps);
    }

    private static void WriteJson(string directoryPath, string name, string json) =>
        File.WriteAllText(Path.Combine(directoryPath, name), json, new UTF8Encoding(false));

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "BeatInsight-TechAnnotations-"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
