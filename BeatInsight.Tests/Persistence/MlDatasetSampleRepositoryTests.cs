using BeatInsight.Models.Persistence;
using BeatInsight.Services.Persistence;
using Microsoft.Data.Sqlite;
using System.IO;

namespace BeatInsight.Tests.Persistence;

/// <summary>
/// Vérifie le stockage du dataset ML sans référencer le cache runtime,
/// l'analyseur ou l'Identity. Chaque test utilise sa propre base
/// temporaire.
/// </summary>
public sealed class MlDatasetSampleRepositoryTests : IDisposable
{
    private const string PathA = @"C:\Songs\set-a\map-a.osu";
    private const string PathB = @"C:\Songs\set-b\map-b.osu";
    private const string PathC = @"C:\Songs\set-c\map-c.osu";

    private readonly string directory;
    private readonly string databasePath;
    private readonly MlDatasetSampleRepository repository;

    public MlDatasetSampleRepositoryTests()
    {
        directory = Path.Combine(
            Path.GetTempPath(),
            "beatinsight-ml-dataset-" + Guid.NewGuid().ToString("N"));
        databasePath = Path.Combine(directory, "dataset.db");
        repository = new MlDatasetSampleRepository(databasePath);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }


    // ============================================================
    // HELPERS
    // ============================================================

    private static readonly DateTime FileLastWriteUtc =
        new(2026, 9, 3, 10, 20, 30, DateTimeKind.Utc);

    private static readonly DateTime CapturedAtUtc =
        new(2026, 9, 3, 11, 20, 30, DateTimeKind.Utc);

    private static readonly DateTime CommunityCapturedAtUtc =
        new(2026, 9, 3, 12, 20, 30, DateTimeKind.Utc);

    private static MlDatasetSample CreateSample(
        string sourceFilePath,
        MlHumanLabel? humanLabel = null,
        bool humanValidated = false)
    {
        return new MlDatasetSample
        {
            SourceFilePath = sourceFilePath,
            BeatmapId = 42,
            Md5 = "9a6df3ce",
            FileSize = 123_456,
            FileLastWriteUtc = FileLastWriteUtc,
            FeatureSchemaVersion = MlFeatureSchemaVersion.Current,
            AnalyzerVersion = 17,
            CapturedAtUtc = CapturedAtUtc,
            RawFeaturesJson = """
                {"streamCoverage":0.42,"jumpCoverage":0.18}
                """,
            SectionFeaturesJson = """
                [{"family":"Stream","objects":18}]
                """,
            HumanLabel = humanLabel,
            HumanValidated = humanValidated,
            CommunityEvidenceJson = """
                {"agreement":0.8,"relevantVotes":12}
                """,
            CommunityCapturedAtUtc = CommunityCapturedAtUtc,
        };
    }

    private List<string> ReadSchemaObjectNames()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
        };

        using SqliteConnection connection = new(builder.ToString());
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT name FROM sqlite_master
            WHERE type IN ('table', 'index')
            ORDER BY name;
            """;

        using SqliteDataReader reader = command.ExecuteReader();
        List<string> names = [];

        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }


    // ============================================================
    // SCHÉMA
    // ============================================================

    [Fact]
    public void EnsureSchema_CreatesDedicatedDatasetTable()
    {
        Assert.False(File.Exists(databasePath));

        repository.EnsureSchema();

        Assert.True(File.Exists(databasePath));

        List<string> names = ReadSchemaObjectNames();

        Assert.Contains("MlDatasetSample", names);
        Assert.Contains("IX_MlDatasetSample_BeatmapId", names);
        Assert.Contains("IX_MlDatasetSample_HumanLabel", names);
        Assert.DoesNotContain("BeatmapAnalysis", names);
    }

    [Fact]
    public void EnsureSchema_IsIdempotent()
    {
        repository.EnsureSchema();
        repository.EnsureSchema();

        repository.Upsert(CreateSample(PathA));

        Assert.NotNull(repository.FindBySourceFilePath(PathA));
    }


    // ============================================================
    // ROUND-TRIP
    // ============================================================

    [Fact]
    public void Upsert_ThenFind_RoundTripsEveryField()
    {
        repository.EnsureSchema();

        MlDatasetSample expected = CreateSample(
            PathA,
            MlHumanLabel.Tech,
            humanValidated: true);
        repository.Upsert(expected);

        MlDatasetSample? actual =
            repository.FindBySourceFilePath(PathA);

        Assert.NotNull(actual);
        Assert.True(actual.SampleId > 0);
        Assert.Equal(expected.SourceFilePath, actual.SourceFilePath);
        Assert.Equal(expected.BeatmapId, actual.BeatmapId);
        Assert.Equal(expected.Md5, actual.Md5);
        Assert.Equal(expected.FileSize, actual.FileSize);
        Assert.Equal(expected.FileLastWriteUtc, actual.FileLastWriteUtc);
        Assert.Equal(
            expected.FeatureSchemaVersion,
            actual.FeatureSchemaVersion);
        Assert.Equal(expected.AnalyzerVersion, actual.AnalyzerVersion);
        Assert.Equal(expected.CapturedAtUtc, actual.CapturedAtUtc);
        Assert.Equal(expected.RawFeaturesJson, actual.RawFeaturesJson);
        Assert.Equal(
            expected.SectionFeaturesJson,
            actual.SectionFeaturesJson);
        Assert.Equal(expected.HumanLabel, actual.HumanLabel);
        Assert.Equal(expected.HumanValidated, actual.HumanValidated);
        Assert.Equal(
            expected.CommunityEvidenceJson,
            actual.CommunityEvidenceJson);
        Assert.Equal(
            expected.CommunityCapturedAtUtc,
            actual.CommunityCapturedAtUtc);
    }

    [Fact]
    public void NullableFields_RoundTripWithoutInventedValues()
    {
        repository.EnsureSchema();

        MlDatasetSample sample = new()
        {
            SourceFilePath = PathA,
            FileSize = 1,
            FileLastWriteUtc = FileLastWriteUtc,
            FeatureSchemaVersion = MlFeatureSchemaVersion.Current,
            AnalyzerVersion = 1,
            CapturedAtUtc = CapturedAtUtc,
            RawFeaturesJson = "{}",
            HumanLabel = null,
            HumanValidated = false,
        };
        repository.Upsert(sample);

        MlDatasetSample? actual =
            repository.FindBySourceFilePath(PathA);

        Assert.NotNull(actual);
        Assert.Null(actual.BeatmapId);
        Assert.Null(actual.Md5);
        Assert.Null(actual.SectionFeaturesJson);
        Assert.Null(actual.HumanLabel);
        Assert.False(actual.HumanValidated);
        Assert.Null(actual.CommunityEvidenceJson);
        Assert.Null(actual.CommunityCapturedAtUtc);
    }

    [Fact]
    public void HumanValidatedLabel_RoundTripsAsIndependentHumanData()
    {
        repository.EnsureSchema();

        repository.Upsert(CreateSample(
            PathA,
            MlHumanLabel.ClassicMixed,
            humanValidated: true));

        MlDatasetSample? actual =
            repository.FindBySourceFilePath(PathA);

        Assert.NotNull(actual);
        Assert.Equal(MlHumanLabel.ClassicMixed, actual.HumanLabel);
        Assert.True(actual.HumanValidated);
    }


    // ============================================================
    // COLLECTION / MISE À JOUR / SUPPRESSION
    // ============================================================

    [Fact]
    public void List_ReturnsSamplesInSampleIdOrder()
    {
        repository.EnsureSchema();
        repository.Upsert(CreateSample(PathA));
        repository.Upsert(CreateSample(PathB));

        IReadOnlyList<MlDatasetSample> samples = repository.List();

        Assert.Equal(2, samples.Count);
        Assert.True(samples[0].SampleId < samples[1].SampleId);
        Assert.Equal(PathA, samples[0].SourceFilePath);
        Assert.Equal(PathB, samples[1].SourceFilePath);
    }

    [Fact]
    public void Upsert_ExistingPath_UpdatesSampleAndPreservesSampleId()
    {
        repository.EnsureSchema();
        repository.Upsert(CreateSample(PathA));

        MlDatasetSample first =
            Assert.IsType<MlDatasetSample>(
                repository.FindBySourceFilePath(PathA));

        MlDatasetSample updated = new()
        {
            SourceFilePath = PathA,
            BeatmapId = 84,
            Md5 = "updated-md5",
            FileSize = 987_654,
            FileLastWriteUtc = FileLastWriteUtc.AddMinutes(5),
            FeatureSchemaVersion = 2,
            AnalyzerVersion = 18,
            CapturedAtUtc = CapturedAtUtc.AddMinutes(5),
            RawFeaturesJson = "{\"streamCoverage\":0.75}",
            SectionFeaturesJson = null,
            HumanLabel = MlHumanLabel.Stream,
            HumanValidated = true,
            CommunityEvidenceJson = null,
            CommunityCapturedAtUtc = null,
        };
        repository.Upsert(updated);

        MlDatasetSample actual =
            Assert.IsType<MlDatasetSample>(
                repository.FindBySourceFilePath(PathA));

        Assert.Equal(first.SampleId, actual.SampleId);
        Assert.Equal(84, actual.BeatmapId);
        Assert.Equal("updated-md5", actual.Md5);
        Assert.Equal(987_654, actual.FileSize);
        Assert.Equal(2, actual.FeatureSchemaVersion);
        Assert.Equal(18, actual.AnalyzerVersion);
        Assert.Equal("{\"streamCoverage\":0.75}", actual.RawFeaturesJson);
        Assert.Equal(MlHumanLabel.Stream, actual.HumanLabel);
        Assert.True(actual.HumanValidated);
        Assert.Null(actual.SectionFeaturesJson);
        Assert.Null(actual.CommunityEvidenceJson);
    }

    [Fact]
    public void Delete_RemovesOnlyRequestedSample()
    {
        repository.EnsureSchema();
        repository.Upsert(CreateSample(PathA));
        repository.Upsert(CreateSample(PathB));

        Assert.True(repository.Delete(PathA));
        Assert.Null(repository.FindBySourceFilePath(PathA));
        Assert.NotNull(repository.FindBySourceFilePath(PathB));
        Assert.False(repository.Delete(PathA));
    }

    [Fact]
    public void GetStatistics_CountsSamplesValidatedAndUnlabeled()
    {
        repository.EnsureSchema();
        repository.Upsert(CreateSample(PathA));
        repository.Upsert(CreateSample(
            PathB,
            MlHumanLabel.Tech,
            humanValidated: true));
        repository.Upsert(CreateSample(
            PathC,
            MlHumanLabel.Jump,
            humanValidated: false));

        MlDatasetSampleStatistics statistics = repository.GetStatistics();

        Assert.Equal(3, statistics.SampleCount);
        Assert.Equal(1, statistics.HumanValidatedCount);
        Assert.Equal(1, statistics.UnlabeledCount);
    }


    // ============================================================
    // COEXISTENCE AVEC LE CACHE RUNTIME
    // ============================================================

    [Fact]
    public void DatasetTable_CoexistsWithRuntimeCacheTable()
    {
        var cacheRepository = new BeatmapAnalysisRepository(databasePath);

        cacheRepository.EnsureSchema();
        repository.EnsureSchema();
        repository.Upsert(CreateSample(PathA, MlHumanLabel.Jump));

        List<string> names = ReadSchemaObjectNames();

        Assert.Contains("BeatmapAnalysis", names);
        Assert.Contains("MlDatasetSample", names);
        Assert.NotNull(repository.FindBySourceFilePath(PathA));
    }
}
