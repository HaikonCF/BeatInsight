using BeatInsight.Models;
using BeatInsight.Models.Persistence;
using BeatInsight.Services.Persistence;
using Microsoft.Data.Sqlite;
using System.IO;

namespace BeatInsight.Tests.Persistence;

/// <summary>
/// Vérifie l'orchestration du cache : hit valide, miss, péremption,
/// réparation et tolérance aux pannes de stockage.
///
/// ISOLATION
///
/// Chaque instance travaille dans un dossier temporaire dédié :
/// - une copie de la fixture, afin de pouvoir en altérer les
///   métadonnées sans jamais toucher aux fixtures du dépôt ;
/// - une base SQLite dédiée, la base réelle de l'utilisateur n'étant
///   jamais ouverte.
///
/// PÉREMPTION DE VERSION
///
/// Les constantes globales AnalyzerVersion.Current et
/// PersistenceSchemaVersion.Current ne sont jamais modifiées. Les
/// scénarios de version périmée sont produits en altérant
/// directement la ligne stockée en SQL.
/// </summary>
public sealed class CacheServiceTests : IDisposable
{
    private const string FixtureName = "Tower Of Heaven [Extra].osu";

    private readonly string directory;
    private readonly string mapPath;
    private readonly string databasePath;
    private readonly BeatmapAnalysisRepository repository;
    private readonly BeatmapAnalysisCacheService service;

    public CacheServiceTests()
    {
        directory = Path.Combine(
            Path.GetTempPath(),
            "beatinsight-cache-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        // Copie de travail : la fixture du dépôt reste intacte.
        string source = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Maps",
            FixtureName);

        Assert.True(
            File.Exists(source),
            $"Fixture introuvable : {source}");

        mapPath = Path.Combine(directory, FixtureName);
        File.Copy(source, mapPath);

        databasePath = Path.Combine(directory, "cache.db");
        repository = new BeatmapAnalysisRepository(databasePath);
        service = new BeatmapAnalysisCacheService(repository);
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

    /// <summary>
    /// Une analyse fraîche possède ses HitObjects ; un snapshot
    /// restauré depuis le cache ne les persiste pas. C'est le
    /// discriminant utilisé pour distinguer un hit d'un miss sans
    /// instrumenter le pipeline.
    /// </summary>
    private static void AssertFreshAnalysis(Beatmap beatmap)
    {
        Assert.NotEmpty(beatmap.HitObjects);
    }

    private static void AssertRestoredSnapshot(Beatmap beatmap)
    {
        Assert.Empty(beatmap.HitObjects);
    }

    private void ExecuteRaw(string sql)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
        };

        using SqliteConnection connection = new(builder.ToString());
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
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

    /// <summary>
    /// Rend la base illisible pour SQLite tout en laissant le fichier
    /// en place, afin de simuler une panne de stockage réaliste.
    /// </summary>
    private void CorruptDatabaseFile()
    {
        SqliteConnection.ClearAllPools();
        File.WriteAllText(databasePath, "ceci n'est pas une base");
    }


    // ============================================================
    // MISS INITIAL
    // ============================================================

    [Fact]
    public void FirstCall_AbsentDatabase_AnalysesAndPersists()
    {
        Assert.False(File.Exists(databasePath));

        Beatmap result = service.GetOrAnalyze(mapPath);

        AssertFreshAnalysis(result);

        // La base a été créée et la ligne écrite.
        Assert.True(File.Exists(databasePath));
        Assert.NotNull(repository.Find(mapPath));
        Assert.Equal(1, CountRows());
    }

    [Fact]
    public void FirstCall_EmptySchema_AnalysesAndPersists()
    {
        repository.EnsureSchema();

        Beatmap result = service.GetOrAnalyze(mapPath);

        AssertFreshAnalysis(result);
        Assert.NotNull(repository.Find(mapPath));
    }

    [Fact]
    public void PersistedRecord_CarriesCurrentVersionsAndFileState()
    {
        service.GetOrAnalyze(mapPath);

        BeatmapAnalysisRecord? record = repository.Find(mapPath);
        FileInfo info = new(mapPath);

        Assert.NotNull(record);
        Assert.Equal(mapPath, record.FilePath);
        Assert.Equal(info.Length, record.FileSize);
        Assert.Equal(info.LastWriteTimeUtc, record.FileLastWriteUtc);
        Assert.Equal(
            BeatInsight.Analysis.AnalyzerVersion.Current,
            record.AnalyzerVersion);
        Assert.Equal(
            PersistenceSchemaVersion.Current,
            record.SchemaVersion);
    }


    // ============================================================
    // HIT
    // ============================================================

    [Fact]
    public void SecondCall_ReturnsRestoredSnapshot()
    {
        Beatmap first = service.GetOrAnalyze(mapPath);
        Beatmap second = service.GetOrAnalyze(mapPath);

        AssertFreshAnalysis(first);
        AssertRestoredSnapshot(second);

        // Aucune ligne supplémentaire n'a été créée.
        Assert.Equal(1, CountRows());
    }

    [Fact]
    public void SecondCall_NewServiceInstance_StillHits()
    {
        service.GetOrAnalyze(mapPath);

        BeatmapAnalysisCacheService reopened =
            new(new BeatmapAnalysisRepository(databasePath));

        AssertRestoredSnapshot(reopened.GetOrAnalyze(mapPath));
    }

    [Fact]
    public void CacheHit_PreservesUiAndReportFields()
    {
        Beatmap fresh = service.GetOrAnalyze(mapPath);
        Beatmap hit = service.GetOrAnalyze(mapPath);

        AssertFreshAnalysis(fresh);
        AssertRestoredSnapshot(hit);

        // Métadonnées UI
        Assert.Equal(fresh.Title, hit.Title);
        Assert.Equal(fresh.Artist, hit.Artist);
        Assert.Equal(fresh.Creator, hit.Creator);
        Assert.Equal(fresh.Version, hit.Version);
        Assert.Equal(fresh.LengthDisplay, hit.LengthDisplay);
        Assert.Equal(fresh.BPM, hit.BPM);
        Assert.Equal(fresh.MaxCombo, hit.MaxCombo);
        Assert.Equal(fresh.AR, hit.AR);
        Assert.Equal(fresh.OD, hit.OD);
        Assert.Equal(fresh.CS, hit.CS);
        Assert.Equal(fresh.HP, hit.HP);
        Assert.Equal(fresh.CircleCount, hit.CircleCount);
        Assert.Equal(fresh.SliderCount, hit.SliderCount);
        Assert.Equal(fresh.SpinnerCount, hit.SpinnerCount);
        Assert.Equal(fresh.OsuStarRating, hit.OsuStarRating);
        Assert.Equal(
            fresh.BeatInsightRating,
            hit.BeatInsightRating);

        GameplayProfile before = fresh.GameplayProfile;
        GameplayProfile after = hit.GameplayProfile;

        // Bindings UI du profil
        Assert.Equal(before.StreamRatio, after.StreamRatio);
        Assert.Equal(before.JumpRatio, after.JumpRatio);
        Assert.Equal(before.BurstRatio, after.BurstRatio);
        Assert.Equal(before.TechPresence, after.TechPresence);
        Assert.Equal(before.SpeedScore, after.SpeedScore);
        Assert.Equal(before.AimScore, after.AimScore);
        Assert.Equal(before.ReadScore, after.ReadScore);

        // Identité et propriétés dérivées
        Assert.Equal(
            before.Identity.Primary,
            after.Identity.Primary);
        Assert.Equal(
            before.Identity.Secondary,
            after.Identity.Secondary);
        Assert.Equal(
            before.Identity.FullName,
            after.Identity.FullName);
        Assert.Equal(
            before.Identity.Confidence,
            after.Identity.Confidence);
        Assert.Equal(
            before.Identity.Traits,
            after.Identity.Traits);
        Assert.Equal(
            before.Identity.TraitsDisplay,
            after.Identity.TraitsDisplay);
        Assert.Equal(
            before.ClassificationReasons,
            after.ClassificationReasons);

        // Champs du rapport détaillé
        Assert.Equal(before.TechScore, after.TechScore);
        Assert.Equal(
            before.TechTransitionSignal,
            after.TechTransitionSignal);
        Assert.Equal(
            before.TechStructureSignal,
            after.TechStructureSignal);
        Assert.Equal(
            before.TechSpatialSignal,
            after.TechSpatialSignal);
        Assert.Equal(
            before.TechTemporalSignal,
            after.TechTemporalSignal);
        Assert.Equal(before.ReadIntensity, after.ReadIntensity);
        Assert.Equal(before.ReadCoverage, after.ReadCoverage);
        Assert.Equal(
            before.ReadSectionCount,
            after.ReadSectionCount);
        Assert.Equal(
            before.ReadDensitySignal,
            after.ReadDensitySignal);
        Assert.Equal(
            before.ReadClutterSignal,
            after.ReadClutterSignal);
        Assert.Equal(before.ReadCSSignal, after.ReadCSSignal);
        Assert.Equal(
            before.ReadPredictability,
            after.ReadPredictability);
        Assert.Equal(before.ReadNovelty, after.ReadNovelty);
        Assert.Equal(
            before.ReadTemporalRegularity,
            after.ReadTemporalRegularity);
        Assert.Equal(
            before.ReadSpacingRegularity,
            after.ReadSpacingRegularity);
        Assert.Equal(
            before.ReadTrajectoryRepetition,
            after.ReadTrajectoryRepetition);
        Assert.Equal(before.ReadAmbiguity, after.ReadAmbiguity);
        Assert.Equal(
            before.SpeedFastObjectRatio,
            after.SpeedFastObjectRatio);
        Assert.Equal(
            before.SpeedDensitySignal,
            after.SpeedDensitySignal);
        Assert.Equal(before.SpeedARSignal, after.SpeedARSignal);
        Assert.Equal(
            before.AimDistanceSignal,
            after.AimDistanceSignal);
        Assert.Equal(before.AimSpeedSignal, after.AimSpeedSignal);
        Assert.Equal(before.AimAngleSignal, after.AimAngleSignal);
        Assert.Equal(
            before.AimTemporalSignal,
            after.AimTemporalSignal);
    }


    // ============================================================
    // PÉREMPTION
    // ============================================================

    [Fact]
    public void StaleFileSize_TriggersReanalysis()
    {
        service.GetOrAnalyze(mapPath);

        ExecuteRaw(
            "UPDATE BeatmapAnalysis SET FileSize = FileSize + 1;");

        AssertFreshAnalysis(service.GetOrAnalyze(mapPath));
    }

    [Fact]
    public void StaleLastWriteUtc_TriggersReanalysis()
    {
        service.GetOrAnalyze(mapPath);

        ExecuteRaw("""
            UPDATE BeatmapAnalysis
            SET FileLastWriteUtc = FileLastWriteUtc + 1;
            """);

        AssertFreshAnalysis(service.GetOrAnalyze(mapPath));
    }

    [Fact]
    public void StaleAnalyzerVersion_TriggersReanalysis()
    {
        service.GetOrAnalyze(mapPath);

        // Les constantes globales ne sont pas touchées : seule la
        // ligne stockée est altérée.
        ExecuteRaw(
            "UPDATE BeatmapAnalysis SET AnalyzerVersion = 999;");

        AssertFreshAnalysis(service.GetOrAnalyze(mapPath));
    }

    [Fact]
    public void StaleSchemaVersion_TriggersReanalysis()
    {
        service.GetOrAnalyze(mapPath);

        ExecuteRaw(
            "UPDATE BeatmapAnalysis SET SchemaVersion = 999;");

        AssertFreshAnalysis(service.GetOrAnalyze(mapPath));
    }

    [Fact]
    public void ModifiedFileOnDisk_TriggersReanalysis()
    {
        service.GetOrAnalyze(mapPath);
        AssertRestoredSnapshot(service.GetOrAnalyze(mapPath));

        // Édition réelle du fichier : la taille et la date changent.
        File.AppendAllText(mapPath, Environment.NewLine);

        AssertFreshAnalysis(service.GetOrAnalyze(mapPath));
    }

    [Fact]
    public void StaleRecord_IsReplacedNotDuplicated()
    {
        service.GetOrAnalyze(mapPath);

        ExecuteRaw(
            "UPDATE BeatmapAnalysis SET AnalyzerVersion = 999;");

        service.GetOrAnalyze(mapPath);

        Assert.Equal(1, CountRows());

        BeatmapAnalysisRecord? record = repository.Find(mapPath);

        Assert.NotNull(record);
        Assert.Equal(
            BeatInsight.Analysis.AnalyzerVersion.Current,
            record.AnalyzerVersion);
    }

    [Fact]
    public void StaleRecord_IsFollowedByHit()
    {
        service.GetOrAnalyze(mapPath);

        ExecuteRaw(
            "UPDATE BeatmapAnalysis SET SchemaVersion = 999;");

        AssertFreshAnalysis(service.GetOrAnalyze(mapPath));

        // La ligne réécrite doit redevenir un hit valide.
        AssertRestoredSnapshot(service.GetOrAnalyze(mapPath));
    }


    // ============================================================
    // LIGNE ILLISIBLE
    // ============================================================

    [Fact]
    public void CorruptRow_TriggersReanalysisAndRepairsRow()
    {
        service.GetOrAnalyze(mapPath);

        ExecuteRaw(
            "UPDATE BeatmapAnalysis SET ProfileJson = 'broken';");

        // Find retourne null : traité comme un miss.
        Assert.Null(repository.Find(mapPath));

        AssertFreshAnalysis(service.GetOrAnalyze(mapPath));

        // La ligne est réparée et redevient exploitable.
        Assert.NotNull(repository.Find(mapPath));
        Assert.Equal(1, CountRows());
        AssertRestoredSnapshot(service.GetOrAnalyze(mapPath));
    }


    // ============================================================
    // BEATMAP ID
    // ============================================================

    [Fact]
    public void BeatmapId_IsStoredOnNewRecord()
    {
        service.GetOrAnalyze(mapPath, beatmapId: 4242);

        BeatmapAnalysisRecord? record = repository.Find(mapPath);

        Assert.NotNull(record);
        Assert.Equal(4242, record.BeatmapId);
    }

    [Fact]
    public void BeatmapId_OmittedIsStoredAsNull()
    {
        service.GetOrAnalyze(mapPath);

        BeatmapAnalysisRecord? record = repository.Find(mapPath);

        Assert.NotNull(record);
        Assert.Null(record.BeatmapId);
    }

    [Fact]
    public void BeatmapId_DoesNotParticipateInValidity()
    {
        service.GetOrAnalyze(mapPath, beatmapId: 1);

        // Un identifiant différent ne doit pas périmer la ligne :
        // seul l'état du fichier et les versions comptent.
        AssertRestoredSnapshot(
            service.GetOrAnalyze(mapPath, beatmapId: 999));
    }

    [Fact]
    public void Md5_DoesNotParticipateInValidity()
    {
        service.GetOrAnalyze(mapPath);

        ExecuteRaw(
            "UPDATE BeatmapAnalysis SET Md5 = 'valeur-arbitraire';");

        AssertRestoredSnapshot(service.GetOrAnalyze(mapPath));
    }


    // ============================================================
    // TOLÉRANCE AUX PANNES DE STOCKAGE
    // ============================================================

    [Fact]
    public void UnreadableDatabase_DoesNotPreventAnalysis()
    {
        CorruptDatabaseFile();

        Beatmap result = service.GetOrAnalyze(mapPath);

        AssertFreshAnalysis(result);
        Assert.True(result.OsuStarRating > 0.0);
    }

    [Fact]
    public void FailedPersistence_StillReturnsAnalysis()
    {
        // Le premier appel échoue en lecture ET en écriture.
        CorruptDatabaseFile();

        Beatmap first = service.GetOrAnalyze(mapPath);
        AssertFreshAnalysis(first);

        // Rien n'a pu être stocké : l'appel suivant réanalyse, sans
        // jamais lever d'exception.
        Beatmap second = service.GetOrAnalyze(mapPath);
        AssertFreshAnalysis(second);
    }

    [Fact]
    public void UnwritableDatabaseDirectory_DoesNotPreventAnalysis()
    {
        // Un chemin dont le parent est un fichier ne peut pas être
        // créé : l'initialisation du cache échoue.
        string blocker = Path.Combine(directory, "blocker");
        File.WriteAllText(blocker, "fichier, pas dossier");

        BeatmapAnalysisCacheService broken = new(
            new BeatmapAnalysisRepository(
                Path.Combine(blocker, "nested", "cache.db")));

        AssertFreshAnalysis(broken.GetOrAnalyze(mapPath));
    }

    [Fact]
    public void StorageFailureAfterSuccess_DoesNotPreventAnalysis()
    {
        service.GetOrAnalyze(mapPath);
        AssertRestoredSnapshot(service.GetOrAnalyze(mapPath));

        // La base devient illisible en cours de session.
        CorruptDatabaseFile();

        AssertFreshAnalysis(service.GetOrAnalyze(mapPath));
    }


    // ============================================================
    // LES ERREURS D'ANALYSE NE SONT PAS MASQUÉES
    // ============================================================

    [Fact]
    public void MissingFile_PropagatesAnalysisError()
    {
        string missing = Path.Combine(directory, "absent.osu");

        // Une beatmap introuvable est une erreur d'analyse, pas une
        // panne de cache : elle doit remonter.
        Assert.ThrowsAny<IOException>(
            () => service.GetOrAnalyze(missing));
    }

    [Fact]
    public void MissingFile_WithUnreadableDatabase_StillPropagates()
    {
        CorruptDatabaseFile();

        string missing = Path.Combine(directory, "absent.osu");

        Assert.ThrowsAny<IOException>(
            () => service.GetOrAnalyze(missing));
    }

    [Fact]
    public void BlankPath_IsRejected()
    {
        Assert.Throws<ArgumentException>(
            () => service.GetOrAnalyze("   "));
    }
}
