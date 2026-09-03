using BeatInsight.Analysis;
using BeatInsight.Models.Ml;
using BeatInsight.Models.Persistence;
using BeatInsight.Services.Ml;
using BeatInsight.Services.Persistence;
using Microsoft.Data.Sqlite;
using System.IO;

namespace BeatInsight.Tests.Ml;

/// <summary>
/// Contrats du backfill ML incrémental. Chaque test utilise son dossier de
/// maps et sa base SQLite temporaire : aucun cache runtime ou dossier Songs
/// utilisateur n'est sollicité.
/// </summary>
public sealed class MlDatasetBuilderTests : IDisposable
{
    private readonly string directory;
    private readonly string songsRoot;
    private readonly string databasePath;
    private readonly MlDatasetSampleRepository repository;
    private readonly MlDatasetBuilder builder;

    public MlDatasetBuilderTests()
    {
        directory = Path.Combine(
            Path.GetTempPath(),
            "beatinsight-ml-builder-" + Guid.NewGuid().ToString("N"));
        songsRoot = Path.Combine(directory, "Songs");
        Directory.CreateDirectory(songsRoot);

        databasePath = Path.Combine(directory, "ml-dataset.db");
        repository = new MlDatasetSampleRepository(databasePath);
        builder = new MlDatasetBuilder(repository);
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

    private const string MinimalValidOsu = """
        osu file format v14

        [General]
        Mode: 0

        [Metadata]
        Title:ML Builder Test Map
        Artist:Test Artist
        Creator:Test Creator
        Version:Normal

        [Difficulty]
        ApproachRate:5
        OverallDifficulty:5
        CircleSize:4
        HPDrainRate:5
        SliderMultiplier:1.4
        SliderTickRate:1

        [TimingPoints]
        0,500,4,2,0,50,1,0

        [HitObjects]
        100,100,0,1,0,0:0:0:0:
        200,100,600,1,0,0:0:0:0:
        """;

    private const string ZeroIntervalOsu = """
        osu file format v14

        [General]
        Mode: 0

        [Metadata]
        Title:ML Zero Interval
        Artist:Test Artist
        Creator:Test Creator
        Version:Normal

        [Difficulty]
        ApproachRate:5
        OverallDifficulty:5
        CircleSize:4
        HPDrainRate:5
        SliderMultiplier:1.4
        SliderTickRate:1

        [TimingPoints]
        0,500,4,2,0,50,1,0

        [HitObjects]
        100,100,0,1,0,0:0:0:0:
        200,100,0,1,0,0:0:0:0:
        200,100,600,1,0,0:0:0:0:
        """;

    private const string UnsupportedModeOsu = """
        osu file format v14

        [General]
        Mode: 3
        """;

    private const string InvalidOsu = """
        osu file format v14

        [General]
        Mode: 0

        [Difficulty]
        ApproachRate:not-a-number
        """;

    private string WriteMap(
        string relativePath,
        string content = MinimalValidOsu)
    {
        string path = Path.Combine(songsRoot, relativePath);
        string? parent = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllText(path, content);

        return path;
    }

    private static MlDatasetSample CopySample(
        MlDatasetSample source,
        int? featureSchemaVersion = null,
        int? analyzerVersion = null,
        MlHumanLabel? primaryHumanLabel = null,
        MlHumanLabel? secondaryHumanLabel = null,
        bool? humanValidated = null,
        string? communityEvidenceJson = null,
        DateTime? communityCapturedAtUtc = null)
    {
        return new MlDatasetSample
        {
            SourceFilePath = source.SourceFilePath,
            BeatmapId = source.BeatmapId,
            Md5 = source.Md5,
            FileSize = source.FileSize,
            FileLastWriteUtc = source.FileLastWriteUtc,
            FeatureSchemaVersion =
                featureSchemaVersion ?? source.FeatureSchemaVersion,
            AnalyzerVersion = analyzerVersion ?? source.AnalyzerVersion,
            CapturedAtUtc = source.CapturedAtUtc,
            RawFeaturesJson = source.RawFeaturesJson,
            SectionFeaturesJson = source.SectionFeaturesJson,
            PrimaryHumanLabel =
                primaryHumanLabel ?? source.PrimaryHumanLabel,
            SecondaryHumanLabel =
                secondaryHumanLabel ?? source.SecondaryHumanLabel,
            HumanValidated = humanValidated ?? source.HumanValidated,
            CommunityEvidenceJson =
                communityEvidenceJson ?? source.CommunityEvidenceJson,
            CommunityCapturedAtUtc =
                communityCapturedAtUtc ?? source.CommunityCapturedAtUtc,
        };
    }

    /// <summary>
    /// Insère, via le schéma pré-V2.3.5b-1 (colonne HumanLabel unique),
    /// un sample déjà à jour au sens des features et de l'analyseur :
    /// FeatureSchemaVersion et AnalyzerVersion correspondent aux
    /// constantes courantes, et FileSize/FileLastWriteUtc reflètent
    /// exactement l'état actuel du fichier sur disque.
    ///
    /// Reproduit fidèlement un dataset réel capturé avant la migration
    /// dual-label, pour vérifier que EnsureSchema() ne fait que
    /// renommer/ajouter des colonnes sans jamais faire regresser ces
    /// versions ni ces métadonnées de fraîcheur.
    /// </summary>
    private void CreateLegacySchemaSampleAtCurrentVersions(
        string sourceFilePath,
        FileInfo fileInfo)
    {
        string? directory = Path.GetDirectoryName(databasePath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connectionBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        };

        using SqliteConnection connection =
            new(connectionBuilder.ToString());
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();

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

            INSERT INTO MlDatasetSample (
                SourceFilePath, FileSize, FileLastWriteUtc,
                FeatureSchemaVersion, AnalyzerVersion, CapturedAtUtc,
                RawFeaturesJson, SectionFeaturesJson,
                HumanLabel, HumanValidated
            ) VALUES (
                $sourceFilePath, $fileSize, $fileLastWriteUtc,
                $featureSchemaVersion, $analyzerVersion, $capturedAtUtc,
                $rawFeaturesJson, $sectionFeaturesJson,
                'Tech', 1
            );
            """;

        command.Parameters.AddWithValue("$sourceFilePath", sourceFilePath);
        command.Parameters.AddWithValue("$fileSize", fileInfo.Length);
        command.Parameters.AddWithValue(
            "$fileLastWriteUtc",
            fileInfo.LastWriteTimeUtc.Ticks);
        command.Parameters.AddWithValue(
            "$featureSchemaVersion",
            MlFeatureSchemaVersion.Current);
        command.Parameters.AddWithValue(
            "$analyzerVersion",
            AnalyzerVersion.Current);
        command.Parameters.AddWithValue(
            "$capturedAtUtc",
            DateTime.UtcNow.Ticks);
        command.Parameters.AddWithValue(
            "$rawFeaturesJson",
            "{\"streamCoverage\":0.4}");
        command.Parameters.AddWithValue(
            "$sectionFeaturesJson",
            "[{\"family\":\"Stream\"}]");

        command.ExecuteNonQuery();
    }

    private static void Touch(string filePath)
    {
        File.AppendAllText(filePath, Environment.NewLine);
        File.SetLastWriteTimeUtc(
            filePath,
            DateTime.UtcNow.AddMinutes(1));
    }

    private List<string> ReadTableNames()
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
            WHERE type = 'table'
            ORDER BY name;
            """;

        using SqliteDataReader reader = command.ExecuteReader();
        List<string> tableNames = [];

        while (reader.Read())
        {
            tableNames.Add(reader.GetString(0));
        }

        return tableNames;
    }

    [Fact]
    public void FirstCapture_CreatesDatasetSample()
    {
        string path = WriteMap("first.osu");

        MlDatasetBuildResult result = builder.Build(songsRoot);
        MlDatasetSample? sample = repository.FindBySourceFilePath(path);

        Assert.Equal(1, result.TotalFiles);
        Assert.Equal(1, result.ProcessedFiles);
        Assert.Equal(1, result.CapturedFiles);
        Assert.Equal(0, result.DatasetUpToDateFiles);
        Assert.Equal(0, result.FailedFiles);
        Assert.NotNull(sample);
        Assert.Equal(MlFeatureSchemaVersion.Current,
            sample.FeatureSchemaVersion);
        Assert.Equal(AnalyzerVersion.Current, sample.AnalyzerVersion);
        Assert.False(string.IsNullOrWhiteSpace(sample.RawFeaturesJson));
        Assert.False(string.IsNullOrWhiteSpace(sample.SectionFeaturesJson));
    }

    [Fact]
    public void Capture_CreatesOnlyDatasetTableNotRuntimeCacheTable()
    {
        WriteMap("dataset-only.osu");

        builder.Build(songsRoot);

        List<string> tableNames = ReadTableNames();

        Assert.Contains("MlDatasetSample", tableNames);
        Assert.DoesNotContain("BeatmapAnalysis", tableNames);
    }

    [Fact]
    public void SecondUnchangedPass_IsDatasetUpToDate()
    {
        string path = WriteMap("unchanged.osu");

        builder.Build(songsRoot);
        MlDatasetSample before = Assert.IsType<MlDatasetSample>(
            repository.FindBySourceFilePath(path));

        MlDatasetBuildResult second = builder.Build(songsRoot);
        MlDatasetSample after = Assert.IsType<MlDatasetSample>(
            repository.FindBySourceFilePath(path));

        Assert.Equal(0, second.CapturedFiles);
        Assert.Equal(1, second.DatasetUpToDateFiles);
        Assert.Equal(before.SampleId, after.SampleId);
        Assert.Equal(before.CapturedAtUtc, after.CapturedAtUtc);
    }

    [Fact]
    public void ModifiedFile_IsRecaptured()
    {
        string path = WriteMap("modified.osu");

        builder.Build(songsRoot);
        MlDatasetSample before = Assert.IsType<MlDatasetSample>(
            repository.FindBySourceFilePath(path));

        Touch(path);

        MlDatasetBuildResult result = builder.Build(songsRoot);
        MlDatasetSample after = Assert.IsType<MlDatasetSample>(
            repository.FindBySourceFilePath(path));

        Assert.Equal(1, result.CapturedFiles);
        Assert.Equal(0, result.DatasetUpToDateFiles);
        Assert.Equal(before.SampleId, after.SampleId);
        Assert.NotEqual(before.FileSize, after.FileSize);
    }

    [Fact]
    public void StaleAnalyzerVersion_IsRecaptured()
    {
        string path = WriteMap("analyzer-version.osu");

        builder.Build(songsRoot);
        MlDatasetSample stored = Assert.IsType<MlDatasetSample>(
            repository.FindBySourceFilePath(path));
        repository.Upsert(CopySample(stored, analyzerVersion: 0));

        MlDatasetBuildResult result = builder.Build(songsRoot);
        MlDatasetSample refreshed = Assert.IsType<MlDatasetSample>(
            repository.FindBySourceFilePath(path));

        Assert.Equal(1, result.CapturedFiles);
        Assert.Equal(AnalyzerVersion.Current, refreshed.AnalyzerVersion);
    }

    [Fact]
    public void StaleFeatureSchemaVersion_IsRecaptured()
    {
        string path = WriteMap("schema-version.osu");

        builder.Build(songsRoot);
        MlDatasetSample stored = Assert.IsType<MlDatasetSample>(
            repository.FindBySourceFilePath(path));
        repository.Upsert(CopySample(stored, featureSchemaVersion: 0));

        MlDatasetBuildResult result = builder.Build(songsRoot);
        MlDatasetSample refreshed = Assert.IsType<MlDatasetSample>(
            repository.FindBySourceFilePath(path));

        Assert.Equal(1, result.CapturedFiles);
        Assert.Equal(
            MlFeatureSchemaVersion.Current,
            refreshed.FeatureSchemaVersion);
    }

    [Fact]
    public void Refresh_PreservesHumanAndCommunityMetadata()
    {
        string path = WriteMap("annotated.osu");

        builder.Build(songsRoot);
        MlDatasetSample stored = Assert.IsType<MlDatasetSample>(
            repository.FindBySourceFilePath(path));
        DateTime communityCapturedAtUtc = new(
            2026,
            9,
            3,
            12,
            0,
            0,
            DateTimeKind.Utc);
        repository.Upsert(CopySample(
            stored,
            primaryHumanLabel: MlHumanLabel.Tech,
            secondaryHumanLabel: MlHumanLabel.Jump,
            humanValidated: true,
            communityEvidenceJson: "{\"agreement\":0.75}",
            communityCapturedAtUtc: communityCapturedAtUtc));

        Touch(path);

        MlDatasetBuildResult result = builder.Build(songsRoot);
        MlDatasetSample refreshed = Assert.IsType<MlDatasetSample>(
            repository.FindBySourceFilePath(path));

        Assert.Equal(1, result.CapturedFiles);
        Assert.Equal(MlHumanLabel.Tech, refreshed.PrimaryHumanLabel);
        Assert.Equal(MlHumanLabel.Jump, refreshed.SecondaryHumanLabel);
        Assert.True(refreshed.HumanValidated);
        Assert.Equal("{\"agreement\":0.75}",
            refreshed.CommunityEvidenceJson);
        Assert.Equal(communityCapturedAtUtc,
            refreshed.CommunityCapturedAtUtc);
    }

    /// <summary>
    /// La migration dual-label (HumanLabel -> Primary/SecondaryHumanLabel)
    /// ne touche ni RawFeaturesJson, ni SectionFeaturesJson, ni leur
    /// sémantique : MlFeatureSchemaVersion ne doit donc pas être
    /// incrémentée pour elle. Un sample déjà à jour au sens des
    /// features avant la migration doit le rester après, sans
    /// déclencher de réanalyse à cause du seul changement de forme des
    /// annotations humaines.
    /// </summary>
    [Fact]
    public void PostMigration_SampleAtCurrentVersions_StaysUpToDate()
    {
        string path = WriteMap("post-migration.osu");
        FileInfo fileInfo = new(path);

        CreateLegacySchemaSampleAtCurrentVersions(path, fileInfo);

        MlDatasetBuildResult result = builder.Build(songsRoot);

        Assert.Equal(1, result.TotalFiles);
        Assert.Equal(1, result.ProcessedFiles);
        Assert.Equal(0, result.CapturedFiles);
        Assert.Equal(1, result.DatasetUpToDateFiles);
        Assert.Equal(0, result.FailedFiles);

        MlDatasetSample migrated = Assert.IsType<MlDatasetSample>(
            repository.FindBySourceFilePath(path));

        Assert.Equal(
            MlFeatureSchemaVersion.Current,
            migrated.FeatureSchemaVersion);
        Assert.Equal(AnalyzerVersion.Current, migrated.AnalyzerVersion);

        // La migration de schéma a bien eu lieu (colonne renommée),
        // et l'annotation pré-existante a été préservée au passage.
        Assert.Equal(MlHumanLabel.Tech, migrated.PrimaryHumanLabel);
        Assert.Null(migrated.SecondaryHumanLabel);
    }


    [Fact]
    public void UnsupportedMode_IsSkippedWithoutCreatingDatasetSample()
    {
        string path = WriteMap("mania.osu", UnsupportedModeOsu);

        MlDatasetBuildResult result = builder.Build(songsRoot);

        Assert.Equal(1, result.TotalFiles);
        Assert.Equal(1, result.ProcessedFiles);
        Assert.Equal(0, result.CapturedFiles);
        Assert.Equal(1, result.UnsupportedFiles);
        Assert.Equal(0, result.FailedFiles);
        Assert.Null(repository.FindBySourceFilePath(path));
    }

    [Fact]
    public void InvalidMap_IsCountedAsFailedAndDoesNotCreateDatasetSample()
    {
        string path = WriteMap("invalid.osu", InvalidOsu);

        MlDatasetBuildResult result = builder.Build(songsRoot);

        Assert.Equal(1, result.TotalFiles);
        Assert.Equal(1, result.ProcessedFiles);
        Assert.Equal(0, result.CapturedFiles);
        Assert.Equal(1, result.FailedFiles);
        Assert.Null(repository.FindBySourceFilePath(path));
    }

    [Fact]
    public void Cancellation_PreservesSamplesCapturedBeforeIt()
    {
        string first = WriteMap("a.osu");
        string second = WriteMap("b.osu");
        string third = WriteMap("c.osu");

        using var cts = new CancellationTokenSource();
        var progress = new Progress<MlDatasetBuildProgress>(report =>
        {
            if (report.ProcessedFiles >= 1)
            {
                cts.Cancel();
            }
        });

        MlDatasetBuildResult result = builder.Build(
            songsRoot,
            progress,
            cts.Token);

        string[] paths = [first, second, third];
        int persistedCount = paths.Count(path =>
            repository.FindBySourceFilePath(path) is not null);

        Assert.True(result.WasCancelled);
        Assert.True(result.ProcessedFiles < result.TotalFiles);
        Assert.Equal(result.CapturedFiles, persistedCount);
        Assert.True(persistedCount >= 1);
    }

    [Fact]
    public void Capture_ProducesNoNaNOrInfinityInSerializedFeatures()
    {
        string path = WriteMap("zero-interval.osu", ZeroIntervalOsu);

        MlDatasetBuildResult result = builder.Build(songsRoot);
        MlDatasetSample sample = Assert.IsType<MlDatasetSample>(
            repository.FindBySourceFilePath(path));

        Assert.Equal(0, result.FailedFiles);
        Assert.DoesNotContain("NaN", sample.RawFeaturesJson);
        Assert.DoesNotContain("Infinity", sample.RawFeaturesJson);
        Assert.DoesNotContain("NaN",
            Assert.IsType<string>(sample.SectionFeaturesJson));
        Assert.DoesNotContain("Infinity",
            Assert.IsType<string>(sample.SectionFeaturesJson));
    }
}
