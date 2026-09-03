using BeatInsight.Models.Persistence;
using BeatInsight.Services.Persistence;
using Microsoft.Data.Sqlite;
using System.IO;

namespace BeatInsight.Tests.Persistence;

/// <summary>
/// Vérifie le stockage SQLite des analyses.
///
/// Chaque instance de test utilise une base temporaire dédiée : la
/// base réelle de l'utilisateur
/// (%LOCALAPPDATA%\BeatInsight\beatinsight.db) n'est jamais touchée.
/// </summary>
public sealed class RepositoryTests : IDisposable
{
    private readonly string directory;
    private readonly string databasePath;
    private readonly BeatmapAnalysisRepository repository;

    public RepositoryTests()
    {
        directory = Path.Combine(
            Path.GetTempPath(),
            "beatinsight-tests-" + Guid.NewGuid().ToString("N"));

        databasePath = Path.Combine(directory, "test.db");

        repository = new BeatmapAnalysisRepository(databasePath);
    }

    public void Dispose()
    {
        // Le pooling de connexions peut conserver des handles sur le
        // fichier : les vider avant de supprimer le dossier.
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
            // Le nettoyage d'un fichier temporaire ne doit jamais
            // faire échouer un test.
        }
    }


    // ============================================================
    // HELPERS
    // ============================================================

    private const string PathA = @"C:\Songs\a\map-a.osu";
    private const string PathB = @"C:\Songs\b\map-b.osu";

    private static readonly DateTime LastWriteUtc =
        new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private static readonly DateTime AnalysedAtUtc =
        new(2026, 6, 7, 8, 9, 10, DateTimeKind.Utc);

    private static BeatmapAnalysisRecord CreateRecord(
        string filePath,
        int? beatmapId = 1234,
        string? md5 = "abcdef0123456789",
        double osuStarRating = 6.54,
        string identityPrimary = "Stream",
        IReadOnlyList<string>? traits = null)
    {
        return new BeatmapAnalysisRecord
        {
            FilePath = filePath,
            FileSize = 161_926L,
            FileLastWriteUtc = LastWriteUtc,
            AnalyzerVersion = 1,
            SchemaVersion = 1,
            BeatmapId = beatmapId,
            Md5 = md5,
            AnalysedAtUtc = AnalysedAtUtc,

            Title = "FREEDOM DiVE",
            Artist = "xi",
            Creator = "Nakagawa-Kanon",
            Version = "Arles",
            LengthTicks = TimeSpan.FromSeconds(254.9).Ticks,
            BPM = 222,
            MaxCombo = 3245,
            AR = 9.3,
            OD = 8.5,
            CS = 4.2,
            HP = 6.1,
            CircleCount = 1896,
            SliderCount = 342,
            SpinnerCount = 0,
            OsuStarRating = osuStarRating,
            BeatInsightRating = 6.12,

            Profile = new GameplayProfileRecord
            {
                IdentityPrimary = identityPrimary,
                IdentitySecondary = "Jump",
                IdentityPattern = "Jump / Stream",
                IdentityConfidence = 87.5,
                Traits = traits
                    ?? ["Stream Heavy", "High Speed Pressure"],

                StreamRatio = 0.6123,
                JumpRatio = 0.2456,
                BurstRatio = 0.0789,

                TechPresence = 12.5,
                TechScore = 24.75,
                TechTransitionSignal = 31.5,
                TechStructureSignal = 42.25,
                TechSpatialSignal = 18.75,
                TechTemporalSignal = 55.5,

                ReadScore = 48.25,
                ReadCoverage = 0.7321,
                ReadIntensity = "Moderate",
                ReadSectionCount = 7,
                ReadDensitySignal = 0.5123,
                ReadClutterSignal = 0.2789,
                ReadCSSignal = 0.4567,
                ReadPredictability = 0.6234,
                ReadNovelty = 0.3456,
                ReadTemporalRegularity = 0.8123,
                ReadSpacingRegularity = 0.7456,
                ReadTrajectoryRepetition = 0.2345,
                ReadAmbiguity = 0.1234,

                SpeedScore = 72.5,
                SpeedFastObjectRatio = 0.4321,
                SpeedDensitySignal = 66.25,
                SpeedARSignal = 58.75,

                AimScore = 61.25,
                AimDistanceSignal = 44.5,
                AimSpeedSignal = 52.75,
                AimAngleSignal = 37.25,
                AimTemporalSignal = 29.5,
            },
        };
    }

    private void ExecuteRaw(string sql)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        };

        using SqliteConnection connection = new(builder.ToString());
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }


    // ============================================================
    // SCHÉMA
    // ============================================================

    [Fact]
    public void EnsureSchema_AbsentDatabase_CreatesFileAndTable()
    {
        Assert.False(File.Exists(databasePath));

        repository.EnsureSchema();

        Assert.True(File.Exists(databasePath));

        // La table et les deux index doivent exister.
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
            WHERE name IN (
                'BeatmapAnalysis',
                'IX_BeatmapAnalysis_BeatmapId',
                'IX_BeatmapAnalysis_Identity')
            ORDER BY name;
            """;

        List<string> names = [];

        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        Assert.Equal(
            [
                "BeatmapAnalysis",
                "IX_BeatmapAnalysis_BeatmapId",
                "IX_BeatmapAnalysis_Identity",
            ],
            names);
    }

    [Fact]
    public void EnsureSchema_CalledTwice_IsIdempotent()
    {
        repository.EnsureSchema();
        repository.EnsureSchema();

        repository.Upsert(CreateRecord(PathA));

        Assert.NotNull(repository.Find(PathA));
    }

    [Fact]
    public void EnsureSchema_EnablesWalJournalMode()
    {
        repository.EnsureSchema();

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
        };

        using SqliteConnection connection = new(builder.ToString());
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";

        string? mode = command.ExecuteScalar() as string;

        Assert.Equal(
            "wal",
            mode?.ToLowerInvariant());
    }


    // ============================================================
    // INSERT / FIND
    // ============================================================

    [Fact]
    public void Find_UnknownPath_ReturnsNull()
    {
        repository.EnsureSchema();

        Assert.Null(repository.Find(PathA));
    }

    [Fact]
    public void Upsert_ThenFind_ReturnsAllScalarFields()
    {
        repository.EnsureSchema();

        BeatmapAnalysisRecord expected = CreateRecord(PathA);
        repository.Upsert(expected);

        BeatmapAnalysisRecord? actual = repository.Find(PathA);

        Assert.NotNull(actual);

        // Identité et fraîcheur
        Assert.Equal(expected.FilePath, actual.FilePath);
        Assert.Equal(expected.FileSize, actual.FileSize);
        Assert.Equal(
            expected.FileLastWriteUtc,
            actual.FileLastWriteUtc);
        Assert.Equal(
            expected.AnalyzerVersion,
            actual.AnalyzerVersion);
        Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
        Assert.Equal(expected.BeatmapId, actual.BeatmapId);
        Assert.Equal(expected.Md5, actual.Md5);
        Assert.Equal(expected.AnalysedAtUtc, actual.AnalysedAtUtc);

        // Métadonnées
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.Artist, actual.Artist);
        Assert.Equal(expected.Creator, actual.Creator);
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.LengthTicks, actual.LengthTicks);
        Assert.Equal(expected.BPM, actual.BPM);
        Assert.Equal(expected.MaxCombo, actual.MaxCombo);
        Assert.Equal(expected.AR, actual.AR);
        Assert.Equal(expected.OD, actual.OD);
        Assert.Equal(expected.CS, actual.CS);
        Assert.Equal(expected.HP, actual.HP);
        Assert.Equal(expected.CircleCount, actual.CircleCount);
        Assert.Equal(expected.SliderCount, actual.SliderCount);
        Assert.Equal(expected.SpinnerCount, actual.SpinnerCount);
        Assert.Equal(expected.OsuStarRating, actual.OsuStarRating);
        Assert.Equal(
            expected.BeatInsightRating,
            actual.BeatInsightRating);
    }

    [Fact]
    public void Upsert_MultipleRows_AreIsolatedByFilePath()
    {
        repository.EnsureSchema();

        repository.Upsert(
            CreateRecord(PathA, osuStarRating: 6.54));
        repository.Upsert(
            CreateRecord(PathB, osuStarRating: 7.89));

        Assert.Equal(6.54, repository.Find(PathA)!.OsuStarRating);
        Assert.Equal(7.89, repository.Find(PathB)!.OsuStarRating);
    }


    // ============================================================
    // UPSERT REMPLACE
    // ============================================================

    [Fact]
    public void Upsert_ExistingPath_ReplacesRowWithoutDuplicating()
    {
        repository.EnsureSchema();

        repository.Upsert(
            CreateRecord(
                PathA,
                osuStarRating: 6.54,
                identityPrimary: "Stream"));

        repository.Upsert(
            CreateRecord(
                PathA,
                osuStarRating: 7.11,
                identityPrimary: "Tech",
                traits: ["Technical Patterns"]));

        BeatmapAnalysisRecord? actual = repository.Find(PathA);

        Assert.NotNull(actual);
        Assert.Equal(7.11, actual.OsuStarRating);
        Assert.Equal("Tech", actual.Profile.IdentityPrimary);
        Assert.Equal(["Technical Patterns"], actual.Profile.Traits);

        // Une seule ligne doit subsister pour ce chemin.
        Assert.Equal(1, CountRows());
    }

    private long CountRows()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
        };

        using SqliteConnection connection = new(builder.ToString());
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM BeatmapAnalysis;";

        return Convert.ToInt64(command.ExecuteScalar());
    }


    // ============================================================
    // DELETE
    // ============================================================

    [Fact]
    public void Delete_ExistingRow_RemovesItAndReportsTrue()
    {
        repository.EnsureSchema();
        repository.Upsert(CreateRecord(PathA));

        Assert.True(repository.Delete(PathA));
        Assert.Null(repository.Find(PathA));
    }

    [Fact]
    public void Delete_UnknownRow_ReportsFalse()
    {
        repository.EnsureSchema();

        Assert.False(repository.Delete(PathA));
    }

    [Fact]
    public void Delete_DoesNotAffectOtherRows()
    {
        repository.EnsureSchema();
        repository.Upsert(CreateRecord(PathA));
        repository.Upsert(CreateRecord(PathB));

        repository.Delete(PathA);

        Assert.Null(repository.Find(PathA));
        Assert.NotNull(repository.Find(PathB));
    }


    // ============================================================
    // CHAMPS NULLABLES
    // ============================================================

    [Fact]
    public void Upsert_NullBeatmapIdAndMd5_RoundTripsAsNull()
    {
        repository.EnsureSchema();

        repository.Upsert(
            CreateRecord(PathA, beatmapId: null, md5: null));

        BeatmapAnalysisRecord? actual = repository.Find(PathA);

        Assert.NotNull(actual);
        Assert.Null(actual.BeatmapId);
        Assert.Null(actual.Md5);
    }

    [Fact]
    public void Upsert_PopulatedBeatmapIdAndMd5_RoundTripsValues()
    {
        repository.EnsureSchema();

        repository.Upsert(
            CreateRecord(PathA, beatmapId: 4242, md5: "deadbeef"));

        BeatmapAnalysisRecord? actual = repository.Find(PathA);

        Assert.NotNull(actual);
        Assert.Equal(4242, actual.BeatmapId);
        Assert.Equal("deadbeef", actual.Md5);
    }

    [Fact]
    public void FindOwnedBeatmapIds_ReturnsOnlyIdsInThePersistedRuntimeIndex()
    {
        repository.EnsureSchema();
        repository.Upsert(CreateRecord(PathA, beatmapId: 111));
        repository.Upsert(CreateRecord(PathB, beatmapId: 222));

        HashSet<int> owned = repository.FindOwnedBeatmapIds(
            [111, 222, 333, 111]);

        Assert.Equal([111, 222], owned.OrderBy(id => id));
    }

    [Fact]
    public void FindSourceFilePathByBeatmapId_ReturnsIndexedPathOnly()
    {
        repository.EnsureSchema();
        repository.Upsert(CreateRecord(PathA, beatmapId: 111));

        Assert.Equal(PathA, repository.FindSourceFilePathByBeatmapId(111));
        Assert.Null(repository.FindSourceFilePathByBeatmapId(999));
        Assert.Null(repository.FindSourceFilePathByBeatmapId(0));
    }


    // ============================================================
    // TRAITS
    // ============================================================

    [Fact]
    public void Traits_RoundTripPreservesContentAndOrder()
    {
        repository.EnsureSchema();

        string[] traits =
        [
            "Jump Heavy",
            "High Aim Pressure",
            "Reading Influence",
        ];

        repository.Upsert(CreateRecord(PathA, traits: traits));

        BeatmapAnalysisRecord? actual = repository.Find(PathA);

        Assert.NotNull(actual);
        Assert.Equal(traits, actual.Profile.Traits);
    }

    [Fact]
    public void Traits_EmptyList_RoundTripsAsEmpty()
    {
        repository.EnsureSchema();

        repository.Upsert(CreateRecord(PathA, traits: []));

        BeatmapAnalysisRecord? actual = repository.Find(PathA);

        Assert.NotNull(actual);
        Assert.Empty(actual.Profile.Traits);
    }

    [Fact]
    public void Traits_ValuesNeedingJsonEscaping_RoundTripIntact()
    {
        repository.EnsureSchema();

        string[] traits =
        [
            "Quote \" inside",
            "Backslash \\ inside",
            "Accentué é à ù",
        ];

        repository.Upsert(CreateRecord(PathA, traits: traits));

        BeatmapAnalysisRecord? actual = repository.Find(PathA);

        Assert.NotNull(actual);
        Assert.Equal(traits, actual.Profile.Traits);
    }


    // ============================================================
    // PROFILE JSON
    // ============================================================

    [Fact]
    public void ProfileJson_RoundTripPreservesEveryScalar()
    {
        repository.EnsureSchema();

        BeatmapAnalysisRecord expected = CreateRecord(PathA);
        repository.Upsert(expected);

        GameplayProfileRecord? actual =
            repository.Find(PathA)?.Profile;

        Assert.NotNull(actual);

        GameplayProfileRecord before = expected.Profile;

        // Identité (colonnes dédiées)
        Assert.Equal(
            before.IdentityPrimary,
            actual.IdentityPrimary);
        Assert.Equal(
            before.IdentitySecondary,
            actual.IdentitySecondary);
        Assert.Equal(
            before.IdentityPattern,
            actual.IdentityPattern);
        Assert.Equal(
            before.IdentityConfidence,
            actual.IdentityConfidence);

        // Familles structurelles
        Assert.Equal(before.StreamRatio, actual.StreamRatio);
        Assert.Equal(before.JumpRatio, actual.JumpRatio);
        Assert.Equal(before.BurstRatio, actual.BurstRatio);

        // Tech : TechPresence et TechScore restent distincts.
        Assert.Equal(before.TechPresence, actual.TechPresence);
        Assert.Equal(before.TechScore, actual.TechScore);
        Assert.Equal(
            before.TechTransitionSignal,
            actual.TechTransitionSignal);
        Assert.Equal(
            before.TechStructureSignal,
            actual.TechStructureSignal);
        Assert.Equal(
            before.TechSpatialSignal,
            actual.TechSpatialSignal);
        Assert.Equal(
            before.TechTemporalSignal,
            actual.TechTemporalSignal);

        // Reading
        Assert.Equal(before.ReadScore, actual.ReadScore);
        Assert.Equal(before.ReadCoverage, actual.ReadCoverage);
        Assert.Equal(before.ReadIntensity, actual.ReadIntensity);
        Assert.Equal(
            before.ReadSectionCount,
            actual.ReadSectionCount);
        Assert.Equal(
            before.ReadDensitySignal,
            actual.ReadDensitySignal);
        Assert.Equal(
            before.ReadClutterSignal,
            actual.ReadClutterSignal);
        Assert.Equal(before.ReadCSSignal, actual.ReadCSSignal);
        Assert.Equal(
            before.ReadPredictability,
            actual.ReadPredictability);
        Assert.Equal(before.ReadNovelty, actual.ReadNovelty);
        Assert.Equal(
            before.ReadTemporalRegularity,
            actual.ReadTemporalRegularity);
        Assert.Equal(
            before.ReadSpacingRegularity,
            actual.ReadSpacingRegularity);
        Assert.Equal(
            before.ReadTrajectoryRepetition,
            actual.ReadTrajectoryRepetition);
        Assert.Equal(before.ReadAmbiguity, actual.ReadAmbiguity);

        // Speed
        Assert.Equal(before.SpeedScore, actual.SpeedScore);
        Assert.Equal(
            before.SpeedFastObjectRatio,
            actual.SpeedFastObjectRatio);
        Assert.Equal(
            before.SpeedDensitySignal,
            actual.SpeedDensitySignal);
        Assert.Equal(before.SpeedARSignal, actual.SpeedARSignal);

        // Aim
        Assert.Equal(before.AimScore, actual.AimScore);
        Assert.Equal(
            before.AimDistanceSignal,
            actual.AimDistanceSignal);
        Assert.Equal(before.AimSpeedSignal, actual.AimSpeedSignal);
        Assert.Equal(before.AimAngleSignal, actual.AimAngleSignal);
        Assert.Equal(
            before.AimTemporalSignal,
            actual.AimTemporalSignal);
    }


    // ============================================================
    // DONNÉES ILLISIBLES
    // ============================================================

    [Fact]
    public void Find_CorruptedProfileJson_ReturnsNullInsteadOfThrowing()
    {
        repository.EnsureSchema();
        repository.Upsert(CreateRecord(PathA));

        ExecuteRaw("""
            UPDATE BeatmapAnalysis
            SET ProfileJson = 'ceci n''est pas du JSON';
            """);

        Assert.Null(repository.Find(PathA));
    }

    [Fact]
    public void Find_ProfileJsonMissingProperty_ReturnsNull()
    {
        repository.EnsureSchema();
        repository.Upsert(CreateRecord(PathA));

        // JSON valide, mais amputé des champs attendus.
        ExecuteRaw("""
            UPDATE BeatmapAnalysis
            SET ProfileJson = '{"StreamRatio":0.5}';
            """);

        Assert.Null(repository.Find(PathA));
    }

    [Fact]
    public void Find_ProfileJsonWrongType_ReturnsNull()
    {
        repository.EnsureSchema();
        repository.Upsert(CreateRecord(PathA));

        ExecuteRaw("""
            UPDATE BeatmapAnalysis
            SET ProfileJson = '[1,2,3]';
            """);

        Assert.Null(repository.Find(PathA));
    }

    [Fact]
    public void Find_CorruptedTraitsJson_ReturnsNull()
    {
        repository.EnsureSchema();
        repository.Upsert(CreateRecord(PathA));

        ExecuteRaw("""
            UPDATE BeatmapAnalysis
            SET TraitsJson = '{oops';
            """);

        Assert.Null(repository.Find(PathA));
    }

    [Fact]
    public void Find_CorruptedRow_DoesNotBlockOtherRows()
    {
        repository.EnsureSchema();
        repository.Upsert(CreateRecord(PathA));
        repository.Upsert(CreateRecord(PathB));

        ExecuteRaw($"""
            UPDATE BeatmapAnalysis
            SET ProfileJson = 'broken'
            WHERE FilePath = '{PathA}';
            """);

        Assert.Null(repository.Find(PathA));
        Assert.NotNull(repository.Find(PathB));
    }

    [Fact]
    public void Find_CorruptedRow_CanBeOverwrittenByUpsert()
    {
        repository.EnsureSchema();
        repository.Upsert(CreateRecord(PathA));

        ExecuteRaw("""
            UPDATE BeatmapAnalysis SET ProfileJson = 'broken';
            """);

        Assert.Null(repository.Find(PathA));

        // Un recalcul doit pouvoir réparer la ligne.
        repository.Upsert(CreateRecord(PathA, osuStarRating: 8.25));

        BeatmapAnalysisRecord? actual = repository.Find(PathA);

        Assert.NotNull(actual);
        Assert.Equal(8.25, actual.OsuStarRating);
    }


    // ============================================================
    // PERSISTANCE ENTRE DEUX OUVERTURES
    // ============================================================

    [Fact]
    public void Data_SurvivesNewRepositoryInstanceOnSameFile()
    {
        repository.EnsureSchema();
        repository.Upsert(CreateRecord(PathA, osuStarRating: 6.54));

        // Nouvelle instance, nouvelle connexion, même fichier.
        BeatmapAnalysisRepository reopened =
            new(databasePath);

        BeatmapAnalysisRecord? actual = reopened.Find(PathA);

        Assert.NotNull(actual);
        Assert.Equal(6.54, actual.OsuStarRating);
        Assert.Equal("Stream", actual.Profile.IdentityPrimary);
        Assert.Equal(7, actual.Profile.ReadSectionCount);
    }

    [Fact]
    public void EnsureSchema_OnExistingDatabase_PreservesData()
    {
        repository.EnsureSchema();
        repository.Upsert(CreateRecord(PathA));

        BeatmapAnalysisRepository reopened =
            new(databasePath);

        // EnsureSchema au démarrage ne doit rien effacer.
        reopened.EnsureSchema();

        Assert.NotNull(reopened.Find(PathA));
    }


    // ============================================================
    // EMPLACEMENT PAR DÉFAUT
    // ============================================================

    [Fact]
    public void DefaultDatabasePath_PointsInsideLocalAppData()
    {
        string expectedRoot = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        string actual =
            BeatmapAnalysisRepository.DefaultDatabasePath;

        Assert.StartsWith(expectedRoot, actual);
        Assert.EndsWith("beatinsight.db", actual);
        Assert.Contains("BeatInsight", actual);
    }
}
