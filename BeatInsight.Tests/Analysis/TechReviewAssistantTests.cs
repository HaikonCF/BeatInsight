using BeatInsight.Analysis;
using System.IO;
using System.Text;

namespace BeatInsight.Tests.Analysis;

public sealed class TechReviewAssistantTests
{
    [Fact]
    public void GenerateFromWindows_MergesOverlappingWindowsForReview()
    {
        IReadOnlyList<TechV2ExperimentalWindowResult> windows =
        [
            Window(0, 100, 200, 0.90),
            Window(4, 150, 250, 0.89),
            Window(8, 2_000, 2_100, 0.88),
            Window(12, 4_000, 4_100, 0.87),
            Window(16, 6_000, 6_100, 0.50),
            Window(20, 8_000, 8_100, 0.49),
            Window(24, 10_000, 10_100, 0.48),
            Window(28, 12_000, 12_100, 0.20),
            Window(32, 14_000, 14_100, 0.19),
            Window(36, 16_000, 16_100, 0.18),
            Window(40, 18_000, 18_100, 0.17),
            Window(44, 20_000, 20_100, 0.16)
        ];

        TechReviewGenerationResult result =
            TechReviewAssistant.GenerateFromWindows(
                "map",
                "Map",
                "Diff",
                windows,
                options: new TechReviewSamplingOptions(
                    HighCandidateCount: 4,
                    MediumCandidateCount: 0,
                    LowCandidateCount: 0,
                    ReviewMergeGapMilliseconds: 0));

        TechReviewCandidate merged = Assert.Single(
            result.Document.Candidates,
            candidate => candidate.StartTime == 100);
        Assert.Equal(250, merged.EndTime);
        Assert.Equal(2, merged.WindowCount);
        Assert.Equal(TechReviewPriority.High, merged.Priority);
        Assert.Equal(TechReviewHumanLabel.Unreviewed, merged.HumanLabel);
    }

    [Fact]
    public void GenerateFromWindows_UsesRelativeHighMediumLowBandsAndTemporalDiversity()
    {
        IReadOnlyList<TechV2ExperimentalWindowResult> windows = Enumerable
            .Range(0, 15)
            .Select(index => Window(
                index * 4,
                index * 1_000,
                (index * 1_000) + 100,
                index % 3 switch
                {
                    0 => 0.95 - (index * 0.001),
                    1 => 0.55 - (index * 0.001),
                    _ => 0.15 - (index * 0.001)
                }))
            .ToArray();

        TechReviewGenerationResult result =
            TechReviewAssistant.GenerateFromWindows(
                "map",
                "Map",
                "Diff",
                windows,
                options: new TechReviewSamplingOptions(
                    HighCandidateCount: 3,
                    MediumCandidateCount: 3,
                    LowCandidateCount: 3,
                    ReviewMergeGapMilliseconds: 0));

        Assert.Equal(3, result.Statistics.High);
        Assert.Equal(3, result.Statistics.Medium);
        Assert.Equal(3, result.Statistics.Low);
        Assert.All(
            result.Document.Candidates,
            candidate => Assert.Equal(
                TechReviewHumanLabel.Unreviewed,
                candidate.HumanLabel));
        Assert.Equal(
            result.Document.Candidates.Count,
            result.Document.Candidates
                .Select(candidate => candidate.CandidateId)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.All(
            result.Document.Candidates.SelectMany(
                (candidate, index) => result.Document.Candidates
                    .Skip(index + 1)
                    .Select(other => (candidate, other))),
            pair => Assert.True(
                pair.candidate.EndTime <= pair.other.StartTime
                || pair.other.EndTime <= pair.candidate.StartTime,
                "Review candidates must not duplicate the same temporal passage."));
    }

    [Fact]
    public void GenerateFromWindows_DoesNotProposeAlreadyReviewedPassageUnlessRequested()
    {
        TechReviewCandidate reviewed = Candidate(
            "map",
            100,
            200,
            TechReviewPriority.High,
            TechReviewHumanLabel.Tech);
        TechReviewDocument existing = new()
        {
            StableBeatmapId = "map",
            MapName = "Map",
            Difficulty = "Diff",
            Candidates = [reviewed]
        };

        IReadOnlyList<TechV2ExperimentalWindowResult> windows =
        [
            Window(0, 100, 200, 0.90),
            Window(4, 2_000, 2_100, 0.80),
            Window(8, 4_000, 4_100, 0.70)
        ];

        TechReviewGenerationResult result =
            TechReviewAssistant.GenerateFromWindows(
                "map",
                "Map",
                "Diff",
                windows,
                existing,
                new TechReviewSamplingOptions(HighCandidateCount: 2, MediumCandidateCount: 0, LowCandidateCount: 0),
                includeReviewed: false);

        Assert.Single(result.Document.Candidates, candidate =>
            candidate.CandidateId == reviewed.CandidateId);
        Assert.DoesNotContain(
            result.Document.Candidates,
            candidate => candidate.StartTime == 100
                && candidate.HumanLabel == TechReviewHumanLabel.Unreviewed);
        Assert.Equal(1, result.Statistics.AlreadyReviewed);
    }

    [Fact]
    public void ReviewConversion_UsesOnlyExplicitTechAndNotTech_AndKeepsPartialAnnotation()
    {
        TechReviewDocument review = new()
        {
            StableBeatmapId = "map",
            MapName = "Map",
            Difficulty = "Diff",
            Candidates =
            [
                Candidate("map", 100, 200, TechReviewPriority.High, TechReviewHumanLabel.Tech),
                Candidate("map", 300, 400, TechReviewPriority.Medium, TechReviewHumanLabel.NotTech),
                Candidate("map", 500, 600, TechReviewPriority.Low, TechReviewHumanLabel.Unsure),
                Candidate("map", 700, 800, TechReviewPriority.Low, TechReviewHumanLabel.Unreviewed)
            ]
        };

        TechWindowAnnotationDocument annotation =
            TechReviewAnnotationConverter.Convert(review);

        Assert.False(annotation.AnnotationComplete);
        TechWindowAnnotationRegionDocument tech = Assert.Single(annotation.TechRegions);
        TechWindowAnnotationRegionDocument notTech = Assert.Single(annotation.NotTechRegions);
        Assert.Equal((100d, 200d), (tech.StartTime, tech.EndTime));
        Assert.Equal((300d, 400d), (notTech.StartTime, notTech.EndTime));

        TechWindowLabelResult techLabel = TechWindowAnnotationLabeler.LabelWindow(
            100,
            200,
            ToSet(annotation));
        TechWindowLabelResult notTechLabel = TechWindowAnnotationLabeler.LabelWindow(
            300,
            400,
            ToSet(annotation));
        TechWindowLabelResult unknownLabel = TechWindowAnnotationLabeler.LabelWindow(
            500,
            600,
            ToSet(annotation));

        Assert.Equal(TechWindowLabel.Tech, techLabel.Label);
        Assert.Equal(TechWindowLabel.NotTech, notTechLabel.Label);
        Assert.Equal(TechWindowLabel.Unknown, unknownLabel.Label);
    }

    [Fact]
    public void ReviewStore_RejectsDuplicateCandidates_AndCsvIsDeterministic()
    {
        using TemporaryDirectory directory = new();
        TechReviewCandidate reviewed = Candidate(
            "map",
            100,
            200,
            TechReviewPriority.High,
            TechReviewHumanLabel.Tech);
        TechReviewCandidate unreviewed = Candidate(
            "map",
            300,
            400,
            TechReviewPriority.Low,
            TechReviewHumanLabel.Unreviewed);
        TechReviewDocument document = new()
        {
            StableBeatmapId = "map",
            MapName = "Map, Name",
            Difficulty = "Diff",
            Candidates = [unreviewed, reviewed]
        };

        string first = TechReviewCsvExporter.CreateCsv(document);
        string second = TechReviewCsvExporter.CreateCsv(new TechReviewDocument
        {
            StableBeatmapId = document.StableBeatmapId,
            MapName = document.MapName,
            Difficulty = document.Difficulty,
            Candidates = [reviewed, unreviewed]
        });

        Assert.Equal(first, second);
        Assert.DoesNotContain(reviewed.CandidateId, first);
        Assert.Contains(unreviewed.CandidateId, first);

        TechReviewDocument duplicate = new()
        {
            StableBeatmapId = "map",
            Candidates = [reviewed, reviewed]
        };

        Assert.Throws<InvalidDataException>(() =>
            new TechReviewStore(directory.Path).Save(duplicate));
    }

    private static TechAnnotationSet ToSet(TechWindowAnnotationDocument document) =>
        new(
            document.AnnotationComplete,
            document.TechRegions
                .Select(region => new TechAnnotationRegion(
                    region.StartTime,
                    region.EndTime,
                    IsTech: true))
                .Concat(document.NotTechRegions.Select(region =>
                    new TechAnnotationRegion(
                        region.StartTime,
                        region.EndTime,
                        IsTech: false)))
                .ToList());

    private static TechReviewCandidate Candidate(
        string stableBeatmapId,
        double startTime,
        double endTime,
        TechReviewPriority priority,
        TechReviewHumanLabel label) =>
        new(
            TechReviewAssistant.CreateCandidateId(
                stableBeatmapId,
                startTime,
                endTime),
            stableBeatmapId,
            "Map",
            "Diff",
            startTime,
            endTime,
            priority,
            0.5,
            1,
            label);

    private static TechV2ExperimentalWindowResult Window(
        int startIndex,
        double startTime,
        double endTime,
        double likelihood) =>
        new(
            startIndex,
            startIndex + 11,
            startTime,
            endTime,
            0.2,
            0.2,
            0.2,
            0.2,
            0.2,
            likelihood,
            0.2,
            2,
            new TechV2ExperimentalSliderTransitionTopology(11, 0, 0, 0, 1),
            0,
            0.2,
            1);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "BeatInsight-TechReview-"
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
