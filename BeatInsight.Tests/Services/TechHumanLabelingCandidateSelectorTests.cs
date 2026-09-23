using BeatInsight.Models.Persistence;
using BeatInsight.Services.Ml;
using BeatInsight.Services.Persistence;
using Microsoft.Data.Sqlite;
using System.IO;

namespace BeatInsight.Tests.Services;

public sealed class TechHumanLabelingCandidateSelectorTests : IDisposable
{
    private readonly string directory;
    private readonly string databasePath;
    private readonly MlDatasetSampleRepository sampleRepository;
    private readonly BeatmapAnalysisRepository analysisRepository;
    private readonly TechHumanLabelingCandidateSelector selector;

    public TechHumanLabelingCandidateSelectorTests()
    {
        directory = Path.Combine(
            Path.GetTempPath(),
            "beatinsight-tech-labeling-" + Guid.NewGuid().ToString("N"));
        databasePath = Path.Combine(directory, "dataset.db");
        sampleRepository = new MlDatasetSampleRepository(databasePath);
        analysisRepository = new BeatmapAnalysisRepository(databasePath);
        selector = new TechHumanLabelingCandidateSelector(
            sampleRepository,
            analysisRepository);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ListCandidates_SelectsOnlyUnlabeledStandardTechSamples()
    {
        string selected = AddSample("tech", 101, "Tech", 86.0);
        AddSample("validated", 102, "Tech", 95.0, humanValidated: true);
        AddSample("jump", 103, "Jump", 99.0);
        AddSample("mania", 104, "Tech", 98.0, mode: 3);
        AddSampleWithoutAnalysis("no-analysis", 105);

        TechHumanLabelingCandidate candidate = Assert.Single(
            selector.ListCandidates());

        Assert.Equal(selected, candidate.Sample.SourceFilePath);
        Assert.Equal(86.0, candidate.IdentityConfidence);
        Assert.False(candidate.Sample.HumanValidated);
    }

    [Fact]
    public void ListCandidates_DeduplicatesBeatmapAndSetThenOrdersDeterministically()
    {
        // Même BeatmapId dans deux fichiers : seul le plus confiant reste.
        AddSample("duplicate-low", 201, "Tech", 81.0);
        string duplicateHigh = AddSample("duplicate-high", 201, "Tech", 93.0);

        // Deux difficultés du même dossier (même set local) : une seule.
        AddSample("same-set", 202, "Tech", 90.0, setFolder: "set-a");
        string sameSetBest = AddSample(
            "same-set-best",
            203,
            "Tech",
            92.0,
            setFolder: "set-a");

        string stableFirst = AddSample("stable-first", 204, "Tech", 80.0);
        string stableSecond = AddSample("stable-second", 205, "Tech", 80.0);

        IReadOnlyList<TechHumanLabelingCandidate> candidates =
            selector.ListCandidates();

        Assert.Equal(
            [duplicateHigh, sameSetBest, stableFirst, stableSecond],
            candidates.Select(candidate => candidate.Sample.SourceFilePath));
    }

    [Fact]
    public void FindNext_EndsGracefullyAndNeverValidatesTheSample()
    {
        string first = AddSample("first", 301, "Tech", 91.0);
        string second = AddSample("second", 302, "Tech", 82.0);

        TechHumanLabelingCandidate initial = Assert.IsType<
            TechHumanLabelingCandidate>(selector.FindNext(null));
        TechHumanLabelingCandidate following = Assert.IsType<
            TechHumanLabelingCandidate>(selector.FindNext(initial.Sample.SampleId));

        Assert.Equal(first, initial.Sample.SourceFilePath);
        Assert.Equal(second, following.Sample.SourceFilePath);
        Assert.Null(selector.FindNext(following.Sample.SampleId));

        MlDatasetSample persisted = Assert.IsType<MlDatasetSample>(
            sampleRepository.FindBySourceFilePath(first));
        Assert.False(persisted.HumanValidated);
        Assert.Null(persisted.PrimaryHumanLabel);
        Assert.Null(persisted.SecondaryHumanLabel);
    }

    [Fact]
    public void FindNext_AfterSuccessfulTechValidation_ReturnsTheNextCandidate()
    {
        string first = AddSample("first", 351, "Tech", 91.0);
        string second = AddSample("second", 352, "Tech", 82.0);

        TechHumanLabelingCandidate current = Assert.IsType<
            TechHumanLabelingCandidate>(selector.FindNext(null));

        Assert.True(sampleRepository.UpdateHumanLabels(
            first,
            MlHumanLabel.Tech,
            secondary: null));

        TechHumanLabelingCandidate next = Assert.IsType<
            TechHumanLabelingCandidate>(selector.FindNext(current.Sample.SampleId));

        Assert.Equal(second, next.Sample.SourceFilePath);
        Assert.False(next.Sample.HumanValidated);
    }

    [Fact]
    public void FindNext_AfterLastTechValidation_ReturnsNull()
    {
        string only = AddSample("only", 361, "Tech", 91.0);
        TechHumanLabelingCandidate current = Assert.IsType<
            TechHumanLabelingCandidate>(selector.FindNext(null));

        Assert.True(sampleRepository.UpdateHumanLabels(
            only,
            MlHumanLabel.Tech,
            secondary: null));

        Assert.Null(selector.FindNext(current.Sample.SampleId));
    }

    [Fact]
    public void FindNext_WhenNoTechCandidateRemains_ReturnsNullWithoutExternalServices()
    {
        AddSample("jump-only", 401, "Jump", 99.0);

        // Le sélecteur ne reçoit que les deux repositories locaux : ce test
        // couvre son absence totale de dépendance réseau/API/Community.
        Assert.Null(selector.FindNext(null));
    }

    private string AddSample(
        string name,
        int beatmapId,
        string identityPrimary,
        double confidence,
        bool humanValidated = false,
        int mode = 0,
        string? setFolder = null)
    {
        string path = WriteOsuFile(name, mode, setFolder);
        sampleRepository.EnsureSchema();
        analysisRepository.EnsureSchema();
        sampleRepository.Upsert(CreateSample(path, beatmapId, humanValidated));
        analysisRepository.Upsert(CreateAnalysis(
            path,
            beatmapId,
            identityPrimary,
            confidence));
        return path;
    }

    private void AddSampleWithoutAnalysis(string name, int beatmapId)
    {
        string path = WriteOsuFile(name, mode: 0, setFolder: null);
        sampleRepository.EnsureSchema();
        sampleRepository.Upsert(CreateSample(path, beatmapId, false));
    }

    private string WriteOsuFile(string name, int mode, string? setFolder)
    {
        string folder = Path.Combine(directory, setFolder ?? name);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name + ".osu");
        File.WriteAllText(path, $"osu file format v14\n\n[General]\nMode: {mode}\n");
        return path;
    }

    private static MlDatasetSample CreateSample(
        string path,
        int beatmapId,
        bool humanValidated) => new()
        {
            SourceFilePath = path,
            BeatmapId = beatmapId,
            FileSize = 1,
            FileLastWriteUtc = DateTime.UtcNow,
            FeatureSchemaVersion = 1,
            AnalyzerVersion = 1,
            CapturedAtUtc = DateTime.UtcNow,
            RawFeaturesJson = "{}",
            HumanValidated = humanValidated,
            PrimaryHumanLabel = humanValidated ? MlHumanLabel.Tech : null,
        };

    private static BeatmapAnalysisRecord CreateAnalysis(
        string path,
        int beatmapId,
        string primary,
        double confidence) => new()
        {
            FilePath = path,
            FileSize = 1,
            FileLastWriteUtc = DateTime.UtcNow,
            AnalyzerVersion = 1,
            SchemaVersion = 1,
            BeatmapId = beatmapId,
            AnalysedAtUtc = DateTime.UtcNow,
            Title = "Title",
            Artist = "Artist",
            Creator = "Mapper",
            Version = "Difficulty",
            Profile = new GameplayProfileRecord
            {
                IdentityPrimary = primary,
                IdentitySecondary = "",
                IdentityPattern = primary,
                IdentityConfidence = confidence,
            },
        };
}
