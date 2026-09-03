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
        MlHumanLabel? primaryHumanLabel = null,
        bool humanValidated = false,
        MlHumanLabel? secondaryHumanLabel = null,
        int? beatmapId = 42)
    {
        return new MlDatasetSample
        {
            SourceFilePath = sourceFilePath,
            BeatmapId = beatmapId,
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
            PrimaryHumanLabel = primaryHumanLabel,
            SecondaryHumanLabel = secondaryHumanLabel,
            HumanValidated = humanValidated,
            CommunityEvidenceJson = """
                {"agreement":0.8,"relevantVotes":12}
                """,
            CommunityCapturedAtUtc = CommunityCapturedAtUtc,
        };
    }

    private static void CreateLegacySchemaWithHumanLabelColumn(
        string databasePath)
    {
        string? directory = Path.GetDirectoryName(databasePath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        };

        using SqliteConnection connection = new(builder.ToString());
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();

        // Reproduit fidèlement le schéma pré-V2.3.5b-1 (mono-label),
        // y compris son ancien index nommé.
        command.CommandText = """
            CREATE TABLE MlDatasetSample (
                SampleId INTEGER PRIMARY KEY AUTOINCREMENT,
                SourceFilePath TEXT NOT NULL UNIQUE,
                BeatmapId INTEGER NULL,
                Md5 TEXT NULL,

                FileSize INTEGER NOT NULL,
                FileLastWriteUtc INTEGER NOT NULL,

                FeatureSchemaVersion INTEGER NOT NULL,
                AnalyzerVersion INTEGER NOT NULL,
                CapturedAtUtc INTEGER NOT NULL,

                RawFeaturesJson TEXT NOT NULL,
                SectionFeaturesJson TEXT NULL,

                HumanLabel TEXT NULL,
                HumanValidated INTEGER NOT NULL,

                CommunityEvidenceJson TEXT NULL,
                CommunityCapturedAtUtc INTEGER NULL
            );

            CREATE INDEX IX_MlDatasetSample_BeatmapId
                ON MlDatasetSample(BeatmapId);

            CREATE INDEX IX_MlDatasetSample_HumanLabel
                ON MlDatasetSample(HumanLabel, HumanValidated);

            INSERT INTO MlDatasetSample (
                SourceFilePath, BeatmapId, Md5,
                FileSize, FileLastWriteUtc,
                FeatureSchemaVersion, AnalyzerVersion, CapturedAtUtc,
                RawFeaturesJson, SectionFeaturesJson,
                HumanLabel, HumanValidated,
                CommunityEvidenceJson, CommunityCapturedAtUtc
            ) VALUES (
                'C:\Songs\legacy\map.osu', 99, 'legacymd5',
                111222, 1000,
                1, 5, 2000,
                '{"streamCoverage":0.5}', '[{"family":"Jump"}]',
                'Tech', 1,
                '{"agreement":0.6}', 3000
            );
            """;
        command.ExecuteNonQuery();
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
        Assert.Contains("IX_MlDatasetSample_PrimaryHumanLabel", names);
        Assert.DoesNotContain("IX_MlDatasetSample_HumanLabel", names);
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
            humanValidated: true,
            secondaryHumanLabel: MlHumanLabel.Stream);
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
        Assert.Equal(
            expected.PrimaryHumanLabel,
            actual.PrimaryHumanLabel);
        Assert.Equal(
            expected.SecondaryHumanLabel,
            actual.SecondaryHumanLabel);
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
            PrimaryHumanLabel = null,
            SecondaryHumanLabel = null,
            HumanValidated = false,
        };
        repository.Upsert(sample);

        MlDatasetSample? actual =
            repository.FindBySourceFilePath(PathA);

        Assert.NotNull(actual);
        Assert.Null(actual.BeatmapId);
        Assert.Null(actual.Md5);
        Assert.Null(actual.SectionFeaturesJson);
        Assert.Null(actual.PrimaryHumanLabel);
        Assert.Null(actual.SecondaryHumanLabel);
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
        Assert.Equal(MlHumanLabel.ClassicMixed, actual.PrimaryHumanLabel);
        Assert.Equal(MlHumanLabel.ClassicMixed, actual.HumanLabel);
        Assert.Null(actual.SecondaryHumanLabel);
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
            PrimaryHumanLabel = MlHumanLabel.Stream,
            SecondaryHumanLabel = MlHumanLabel.Jump,
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
        Assert.Equal(MlHumanLabel.Stream, actual.PrimaryHumanLabel);
        Assert.Equal(MlHumanLabel.Jump, actual.SecondaryHumanLabel);
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

    [Theory]
    [InlineData("Stream")]
    [InlineData("Jump")]
    [InlineData("Tech")]
    [InlineData("ClassicMixed")]
    public void UpdateHumanLabel_UpdatesOnlyHumanFields(
        string labelName)
    {
        repository.EnsureSchema();

        MlHumanLabel humanLabel = Enum.Parse<MlHumanLabel>(labelName);

        MlDatasetSample original = CreateSample(PathA);
        repository.Upsert(original);

        Assert.True(repository.UpdateHumanLabel(PathA, humanLabel));

        MlDatasetSample updated = Assert.IsType<MlDatasetSample>(
            repository.FindBySourceFilePath(PathA));

        Assert.Equal(humanLabel, updated.PrimaryHumanLabel);
        Assert.Equal(humanLabel, updated.HumanLabel);
        Assert.Null(updated.SecondaryHumanLabel);
        Assert.True(updated.HumanValidated);
        Assert.Equal(original.RawFeaturesJson, updated.RawFeaturesJson);
        Assert.Equal(
            original.SectionFeaturesJson,
            updated.SectionFeaturesJson);
        Assert.Equal(original.BeatmapId, updated.BeatmapId);
        Assert.Equal(original.Md5, updated.Md5);
        Assert.Equal(original.FileSize, updated.FileSize);
        Assert.Equal(
            original.FileLastWriteUtc,
            updated.FileLastWriteUtc);
        Assert.Equal(
            original.FeatureSchemaVersion,
            updated.FeatureSchemaVersion);
        Assert.Equal(original.AnalyzerVersion, updated.AnalyzerVersion);
        Assert.Equal(original.CapturedAtUtc, updated.CapturedAtUtc);
        Assert.Equal(
            original.CommunityEvidenceJson,
            updated.CommunityEvidenceJson);
        Assert.Equal(
            original.CommunityCapturedAtUtc,
            updated.CommunityCapturedAtUtc);

        MlDatasetSampleStatistics statistics = repository.GetStatistics();
        Assert.Equal(1, statistics.SampleCount);
        Assert.Equal(1, statistics.HumanValidatedCount);
        Assert.Equal(0, statistics.UnlabeledCount);
    }

    [Fact]
    public void ClearHumanLabel_ResetsOnlyHumanFields()
    {
        repository.EnsureSchema();

        MlDatasetSample original = CreateSample(
            PathA,
            MlHumanLabel.Jump,
            humanValidated: true,
            secondaryHumanLabel: MlHumanLabel.Tech);
        repository.Upsert(original);

        Assert.True(repository.ClearHumanLabel(PathA));

        MlDatasetSample cleared = Assert.IsType<MlDatasetSample>(
            repository.FindBySourceFilePath(PathA));

        Assert.Null(cleared.PrimaryHumanLabel);
        Assert.Null(cleared.SecondaryHumanLabel);
        Assert.Null(cleared.HumanLabel);
        Assert.False(cleared.HumanValidated);
        Assert.Equal(original.RawFeaturesJson, cleared.RawFeaturesJson);
        Assert.Equal(
            original.SectionFeaturesJson,
            cleared.SectionFeaturesJson);
        Assert.Equal(
            original.CommunityEvidenceJson,
            cleared.CommunityEvidenceJson);
        Assert.Equal(
            original.CommunityCapturedAtUtc,
            cleared.CommunityCapturedAtUtc);

        MlDatasetSampleStatistics statistics = repository.GetStatistics();
        Assert.Equal(1, statistics.SampleCount);
        Assert.Equal(0, statistics.HumanValidatedCount);
        Assert.Equal(1, statistics.UnlabeledCount);
    }

    [Fact]
    public void UpdateHumanLabel_MissingSample_DoesNotCreateSample()
    {
        repository.EnsureSchema();

        Assert.False(repository.UpdateHumanLabel(PathA, MlHumanLabel.Stream));
        Assert.False(repository.ClearHumanLabel(PathA));
        Assert.Null(repository.FindBySourceFilePath(PathA));

        MlDatasetSampleStatistics statistics = repository.GetStatistics();
        Assert.Equal(0, statistics.SampleCount);
        Assert.Equal(0, statistics.HumanValidatedCount);
        Assert.Equal(0, statistics.UnlabeledCount);
    }


    // ============================================================
    // MIGRATION HumanLabel -> Primary/SecondaryHumanLabel
    // ============================================================

    [Fact]
    public void EnsureSchema_LegacyHumanLabelColumn_IsMigratedToPrimary()
    {
        CreateLegacySchemaWithHumanLabelColumn(databasePath);

        repository.EnsureSchema();

        MlDatasetSample? migrated =
            repository.FindBySourceFilePath(@"C:\Songs\legacy\map.osu");

        Assert.NotNull(migrated);
        Assert.Equal(MlHumanLabel.Tech, migrated.PrimaryHumanLabel);
        Assert.True(migrated.HumanValidated);
    }

    [Fact]
    public void EnsureSchema_LegacyDatabase_SecondaryHumanLabelIsInitiallyNull()
    {
        CreateLegacySchemaWithHumanLabelColumn(databasePath);

        repository.EnsureSchema();

        MlDatasetSample? migrated =
            repository.FindBySourceFilePath(@"C:\Songs\legacy\map.osu");

        Assert.NotNull(migrated);
        Assert.Null(migrated.SecondaryHumanLabel);
    }

    [Fact]
    public void EnsureSchema_Migration_PreservesFeaturesAndCommunityMetadata()
    {
        CreateLegacySchemaWithHumanLabelColumn(databasePath);

        repository.EnsureSchema();

        MlDatasetSample? migrated =
            repository.FindBySourceFilePath(@"C:\Songs\legacy\map.osu");

        Assert.NotNull(migrated);
        Assert.Equal(99, migrated.BeatmapId);
        Assert.Equal("legacymd5", migrated.Md5);
        Assert.Equal(111222, migrated.FileSize);
        Assert.Equal(1, migrated.FeatureSchemaVersion);
        Assert.Equal(5, migrated.AnalyzerVersion);
        Assert.Equal(
            "{\"streamCoverage\":0.5}",
            migrated.RawFeaturesJson);
        Assert.Equal(
            "[{\"family\":\"Jump\"}]",
            migrated.SectionFeaturesJson);
        Assert.Equal(
            "{\"agreement\":0.6}",
            migrated.CommunityEvidenceJson);
        Assert.NotNull(migrated.CommunityCapturedAtUtc);
    }

    [Fact]
    public void EnsureSchema_Migration_DropsLegacyIndexAndCreatesNewOne()
    {
        CreateLegacySchemaWithHumanLabelColumn(databasePath);

        repository.EnsureSchema();

        List<string> names = ReadSchemaObjectNames();

        Assert.DoesNotContain("IX_MlDatasetSample_HumanLabel", names);
        Assert.Contains("IX_MlDatasetSample_PrimaryHumanLabel", names);
        Assert.Contains("IX_MlDatasetSample_BeatmapId", names);
    }

    [Fact]
    public void EnsureSchema_MigrationIsIdempotent()
    {
        CreateLegacySchemaWithHumanLabelColumn(databasePath);

        // Deux appels successifs sur une base déjà migrée : le second
        // ne doit ni lever ni altérer les données.
        repository.EnsureSchema();
        repository.EnsureSchema();

        MlDatasetSample? migrated =
            repository.FindBySourceFilePath(@"C:\Songs\legacy\map.osu");

        Assert.NotNull(migrated);
        Assert.Equal(MlHumanLabel.Tech, migrated.PrimaryHumanLabel);
        Assert.True(migrated.HumanValidated);

        // De nouvelles écritures doivent rester possibles après coup.
        repository.Upsert(CreateSample(PathA));
        Assert.NotNull(repository.FindBySourceFilePath(PathA));
    }

    [Fact]
    public void EnsureSchema_FreshDatabase_NeverHadLegacyColumn()
    {
        // Une base neuve ne passe jamais par le chemin de migration :
        // EnsureSchema doit rester un no-op idempotent normal.
        repository.EnsureSchema();
        repository.EnsureSchema();

        List<string> names = ReadSchemaObjectNames();

        Assert.Contains("IX_MlDatasetSample_PrimaryHumanLabel", names);
        Assert.DoesNotContain("IX_MlDatasetSample_HumanLabel", names);
    }


    // ============================================================
    // DUAL LABEL : UpdateHumanLabels
    // ============================================================

    [Fact]
    public void UpdateHumanLabels_PrimaryOnly_RoundTrips()
    {
        repository.EnsureSchema();
        repository.Upsert(CreateSample(PathA));

        Assert.True(repository.UpdateHumanLabels(
            PathA,
            MlHumanLabel.Stream,
            null));

        MlDatasetSample updated = Assert.IsType<MlDatasetSample>(
            repository.FindBySourceFilePath(PathA));

        Assert.Equal(MlHumanLabel.Stream, updated.PrimaryHumanLabel);
        Assert.Null(updated.SecondaryHumanLabel);
        Assert.True(updated.HumanValidated);
    }

    [Fact]
    public void UpdateHumanLabels_PrimaryAndSecondary_RoundTrip()
    {
        repository.EnsureSchema();
        repository.Upsert(CreateSample(PathA));

        Assert.True(repository.UpdateHumanLabels(
            PathA,
            MlHumanLabel.Jump,
            MlHumanLabel.Tech));

        MlDatasetSample updated = Assert.IsType<MlDatasetSample>(
            repository.FindBySourceFilePath(PathA));

        Assert.Equal(MlHumanLabel.Jump, updated.PrimaryHumanLabel);
        Assert.Equal(MlHumanLabel.Tech, updated.SecondaryHumanLabel);
        Assert.True(updated.HumanValidated);
    }

    [Fact]
    public void UpdateHumanLabels_SecondaryEqualsPrimary_IsRejected()
    {
        repository.EnsureSchema();
        repository.Upsert(CreateSample(PathA));

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => repository.UpdateHumanLabels(
                PathA,
                MlHumanLabel.Stream,
                MlHumanLabel.Stream));

        Assert.Equal("secondary", exception.ParamName);

        // La ligne ne doit pas avoir été modifiée par la tentative
        // rejetée.
        Assert.Null(
            repository.FindBySourceFilePath(PathA)?.PrimaryHumanLabel);
    }

    [Fact]
    public void UpdateHumanLabels_SecondaryWithoutPrimary_IsRejected()
    {
        repository.EnsureSchema();
        repository.Upsert(CreateSample(PathA));

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => repository.UpdateHumanLabels(
                PathA,
                null,
                MlHumanLabel.Tech));

        Assert.Equal("primary", exception.ParamName);
    }

    [Fact]
    public void UpdateHumanLabels_NullPrimary_IsRejected()
    {
        repository.EnsureSchema();
        repository.Upsert(CreateSample(PathA));

        Assert.Throws<ArgumentException>(
            () => repository.UpdateHumanLabels(PathA, null, null));
    }

    [Fact]
    public void UpdateHumanLabels_AlwaysSetsHumanValidatedTrue()
    {
        repository.EnsureSchema();
        repository.Upsert(CreateSample(PathA, humanValidated: false));

        repository.UpdateHumanLabels(PathA, MlHumanLabel.Stream, null);

        Assert.True(
            repository.FindBySourceFilePath(PathA)!.HumanValidated);
    }

    [Fact]
    public void UpdateHumanLabels_MissingSample_ReturnsFalseAndCreatesNothing()
    {
        repository.EnsureSchema();

        Assert.False(repository.UpdateHumanLabels(
            PathA,
            MlHumanLabel.Stream,
            null));
        Assert.Null(repository.FindBySourceFilePath(PathA));
    }

    [Fact]
    public void UpdateHumanLabels_ReplacesPreviousSecondaryWithNull()
    {
        repository.EnsureSchema();
        repository.Upsert(CreateSample(PathA));

        repository.UpdateHumanLabels(
            PathA,
            MlHumanLabel.Jump,
            MlHumanLabel.Tech);
        repository.UpdateHumanLabels(PathA, MlHumanLabel.Jump, null);

        MlDatasetSample updated = Assert.IsType<MlDatasetSample>(
            repository.FindBySourceFilePath(PathA));

        Assert.Equal(MlHumanLabel.Jump, updated.PrimaryHumanLabel);
        Assert.Null(updated.SecondaryHumanLabel);
    }


    // ============================================================
    // FAST LABELING : NAVIGATION ET COMPTEURS
    // ============================================================

    private const string PathD = @"C:\Songs\set-d\map-d.osu";

    [Fact]
    public void FindNextUnlabeled_SkipsValidatedSamples()
    {
        repository.EnsureSchema();

        repository.Upsert(CreateSample(PathA));
        repository.Upsert(CreateSample(PathB));
        repository.Upsert(CreateSample(PathC));

        // PathB est validé : la navigation ne doit jamais le revisiter.
        repository.UpdateHumanLabels(PathB, MlHumanLabel.Jump, null);

        MlDatasetSample first = Assert.IsType<MlDatasetSample>(
            repository.FindNextUnlabeled(null));
        Assert.Equal(PathA, first.SourceFilePath);

        MlDatasetSample second = Assert.IsType<MlDatasetSample>(
            repository.FindNextUnlabeled(first.SampleId));
        Assert.Equal(PathC, second.SourceFilePath);

        Assert.Null(repository.FindNextUnlabeled(second.SampleId));
    }

    [Fact]
    public void FindPreviousUnlabeled_Works()
    {
        repository.EnsureSchema();

        repository.Upsert(CreateSample(PathA));
        repository.Upsert(CreateSample(PathB));
        repository.Upsert(CreateSample(PathC));

        repository.UpdateHumanLabels(PathB, MlHumanLabel.Jump, null);

        MlDatasetSample last = Assert.IsType<MlDatasetSample>(
            repository.FindPreviousUnlabeled(null));
        Assert.Equal(PathC, last.SourceFilePath);

        MlDatasetSample first = Assert.IsType<MlDatasetSample>(
            repository.FindPreviousUnlabeled(last.SampleId));
        Assert.Equal(PathA, first.SourceFilePath);

        Assert.Null(repository.FindPreviousUnlabeled(first.SampleId));
    }

    [Fact]
    public void FindNextAndPreviousUnlabeled_UseDeterministicSampleIdOrder()
    {
        repository.EnsureSchema();

        // Insertion volontairement hors ordre alphabétique : l'ordre
        // renvoyé doit suivre SampleId (donc l'ordre d'insertion), pas
        // SourceFilePath.
        repository.Upsert(CreateSample(PathC));
        repository.Upsert(CreateSample(PathA));
        repository.Upsert(CreateSample(PathB));

        MlDatasetSample firstForward = Assert.IsType<MlDatasetSample>(
            repository.FindNextUnlabeled(null));
        Assert.Equal(PathC, firstForward.SourceFilePath);

        MlDatasetSample firstBackward = Assert.IsType<MlDatasetSample>(
            repository.FindPreviousUnlabeled(null));
        Assert.Equal(PathB, firstBackward.SourceFilePath);
    }

    [Fact]
    public void FindNextUnlabeled_MissingSamples_ReturnsNull()
    {
        repository.EnsureSchema();

        Assert.Null(repository.FindNextUnlabeled(null));
        Assert.Null(repository.FindPreviousUnlabeled(null));
    }

    [Fact]
    public void CountValidated_And_CountUnlabeled_AreCoherent()
    {
        repository.EnsureSchema();

        repository.Upsert(CreateSample(PathA));
        repository.Upsert(CreateSample(PathB));
        repository.Upsert(CreateSample(PathC));
        repository.Upsert(CreateSample(PathD));

        repository.UpdateHumanLabels(PathA, MlHumanLabel.Stream, null);
        repository.UpdateHumanLabels(PathB, MlHumanLabel.Jump, MlHumanLabel.Tech);

        Assert.Equal(2, repository.CountValidated());
        Assert.Equal(2, repository.CountUnlabeled());
    }

    [Fact]
    public void ValidateThenFindNext_AdvancesPastJustValidatedSample()
    {
        repository.EnsureSchema();

        repository.Upsert(CreateSample(PathA));
        repository.Upsert(CreateSample(PathB));

        MlDatasetSample current = Assert.IsType<MlDatasetSample>(
            repository.FindNextUnlabeled(null));
        Assert.Equal(PathA, current.SourceFilePath);

        repository.UpdateHumanLabels(
            current.SourceFilePath,
            MlHumanLabel.Stream,
            null);

        MlDatasetSample next = Assert.IsType<MlDatasetSample>(
            repository.FindNextUnlabeled(current.SampleId));
        Assert.Equal(PathB, next.SourceFilePath);
    }

    [Fact]
    public void Skip_DoesNotAlterSample()
    {
        repository.EnsureSchema();

        repository.Upsert(CreateSample(PathA));
        repository.Upsert(CreateSample(PathB));

        MlDatasetSample before = Assert.IsType<MlDatasetSample>(
            repository.FindBySourceFilePath(PathA));

        // Un "skip" ne fait que lire le prochain sample non validé : il
        // n'existe aucune écriture associée à cette navigation.
        repository.FindNextUnlabeled(null);

        MlDatasetSample after = Assert.IsType<MlDatasetSample>(
            repository.FindBySourceFilePath(PathA));

        Assert.Equal(before.PrimaryHumanLabel, after.PrimaryHumanLabel);
        Assert.Equal(before.SecondaryHumanLabel, after.SecondaryHumanLabel);
        Assert.Equal(before.HumanValidated, after.HumanValidated);
    }


    // ============================================================
    // CALIBRATION QUEUE : FindCalibrationSamples
    // ============================================================

    [Fact]
    public void FindCalibrationSamples_FindsOnlyMatchingBeatmapIds()
    {
        repository.EnsureSchema();

        repository.Upsert(CreateSample(PathA, beatmapId: 111));
        repository.Upsert(CreateSample(PathB, beatmapId: 222));
        repository.Upsert(CreateSample(PathC, beatmapId: 333));

        IReadOnlyList<MlDatasetSample> matches =
            repository.FindCalibrationSamples([111, 333]);

        Assert.Equal(2, matches.Count);
        Assert.Contains(matches, s => s.SourceFilePath == PathA);
        Assert.Contains(matches, s => s.SourceFilePath == PathC);
        Assert.DoesNotContain(matches, s => s.SourceFilePath == PathB);
    }

    [Fact]
    public void FindCalibrationSamples_IgnoresIdsAbsentFromDataset()
    {
        repository.EnsureSchema();

        repository.Upsert(CreateSample(PathA, beatmapId: 111));

        IReadOnlyList<MlDatasetSample> matches =
            repository.FindCalibrationSamples([111, 999_999]);

        MlDatasetSample only = Assert.Single(matches);
        Assert.Equal(PathA, only.SourceFilePath);
    }

    [Fact]
    public void FindCalibrationSamples_NullBeatmapId_NeverMatches()
    {
        repository.EnsureSchema();

        repository.Upsert(CreateSample(PathA, beatmapId: null));

        IReadOnlyList<MlDatasetSample> matches =
            repository.FindCalibrationSamples([111]);

        Assert.Empty(matches);
    }

    [Fact]
    public void FindCalibrationSamples_EmptyIdList_ReturnsEmpty()
    {
        repository.EnsureSchema();

        repository.Upsert(CreateSample(PathA, beatmapId: 111));

        Assert.Empty(repository.FindCalibrationSamples([]));
    }

    [Fact]
    public void FindCalibrationSamples_NeverCreatesOrModifiesSamples()
    {
        repository.EnsureSchema();

        repository.Upsert(CreateSample(PathA, beatmapId: 111));

        repository.FindCalibrationSamples([111, 222, 333]);

        Assert.Single(repository.List());

        MlDatasetSample unchanged = Assert.IsType<MlDatasetSample>(
            repository.FindBySourceFilePath(PathA));
        Assert.False(unchanged.HumanValidated);
        Assert.Null(unchanged.PrimaryHumanLabel);
    }

    [Fact]
    public void FindCalibrationSamples_OrderedBySampleIdAscending()
    {
        repository.EnsureSchema();

        // Insertion hors ordre des IDs pour vérifier que l'ordre renvoyé
        // suit SampleId (donc l'insertion), pas les valeurs de BeatmapId.
        repository.Upsert(CreateSample(PathC, beatmapId: 300));
        repository.Upsert(CreateSample(PathA, beatmapId: 100));
        repository.Upsert(CreateSample(PathB, beatmapId: 200));

        IReadOnlyList<MlDatasetSample> matches =
            repository.FindCalibrationSamples([100, 200, 300]);

        Assert.Equal(
            [PathC, PathA, PathB],
            matches.Select(s => s.SourceFilePath));
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
