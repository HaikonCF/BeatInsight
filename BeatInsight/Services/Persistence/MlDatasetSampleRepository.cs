using BeatInsight.Models.Persistence;
using Microsoft.Data.Sqlite;
using System.IO;

namespace BeatInsight.Services.Persistence;

/// <summary>
/// Stockage SQLite dédié aux échantillons du futur dataset ML.
///
/// Ce repository partage éventuellement le fichier SQLite du cache
/// runtime, mais jamais sa table, ses DTO, ses règles de fraîcheur ou
/// son cycle de vie. Il ne référence ni GameplayAnalyzer, ni
/// GameplayIdentity, ni une dépendance ML.
/// </summary>
internal sealed class MlDatasetSampleRepository
{
    private readonly string databasePath;

    /// <summary>
    /// Emplacement par défaut partagé avec la base locale BeatInsight.
    /// La séparation est assurée par la table MlDatasetSample, pas par
    /// une seconde base qui compliquerait les sauvegardes utilisateur.
    /// </summary>
    internal static string DefaultDatabasePath =>
        BeatmapAnalysisRepository.DefaultDatabasePath;

    internal MlDatasetSampleRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        this.databasePath = databasePath;
    }


    // ============================================================
    // SCHÉMA
    // ============================================================

    private const string CreateSchemaSql = """
        CREATE TABLE IF NOT EXISTS MlDatasetSample (
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

        CREATE INDEX IF NOT EXISTS IX_MlDatasetSample_BeatmapId
            ON MlDatasetSample(BeatmapId);

        CREATE INDEX IF NOT EXISTS IX_MlDatasetSample_HumanLabel
            ON MlDatasetSample(HumanLabel, HumanValidated);
        """;

    /// <summary>
    /// Crée la table dédiée et ses index sans modifier la table de
    /// cache BeatmapAnalysis éventuellement présente dans la même base.
    /// </summary>
    internal void EnsureSchema()
    {
        string? directory = Path.GetDirectoryName(databasePath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = CreateSchemaSql;
        command.ExecuteNonQuery();
    }


    // ============================================================
    // LECTURE
    // ============================================================

    private const string SelectColumns = """
        SampleId, SourceFilePath, BeatmapId, Md5,
        FileSize, FileLastWriteUtc,
        FeatureSchemaVersion, AnalyzerVersion, CapturedAtUtc,
        RawFeaturesJson, SectionFeaturesJson,
        HumanLabel, HumanValidated,
        CommunityEvidenceJson, CommunityCapturedAtUtc
        """;

    internal MlDatasetSample? FindBySourceFilePath(string sourceFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = $"""
            SELECT {SelectColumns}
            FROM MlDatasetSample
            WHERE SourceFilePath = $sourceFilePath;
            """;
        command.Parameters.AddWithValue("$sourceFilePath", sourceFilePath);

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? ReadSample(reader)
            : null;
    }

    /// <summary>
    /// Retourne les échantillons par ordre stable de clé technique.
    /// La pagination et les filtres seront ajoutés avec les besoins
    /// d'annotation/export, pas avant.
    /// </summary>
    internal IReadOnlyList<MlDatasetSample> List()
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = $"""
            SELECT {SelectColumns}
            FROM MlDatasetSample
            ORDER BY SampleId ASC;
            """;

        using SqliteDataReader reader = command.ExecuteReader();

        List<MlDatasetSample> samples = [];

        while (reader.Read())
        {
            samples.Add(ReadSample(reader));
        }

        return samples;
    }


    // ============================================================
    // ÉCRITURE
    // ============================================================

    private const string UpsertSql = """
        INSERT INTO MlDatasetSample (
            SourceFilePath, BeatmapId, Md5,
            FileSize, FileLastWriteUtc,
            FeatureSchemaVersion, AnalyzerVersion, CapturedAtUtc,
            RawFeaturesJson, SectionFeaturesJson,
            HumanLabel, HumanValidated,
            CommunityEvidenceJson, CommunityCapturedAtUtc
        ) VALUES (
            $sourceFilePath, $beatmapId, $md5,
            $fileSize, $fileLastWriteUtc,
            $featureSchemaVersion, $analyzerVersion, $capturedAtUtc,
            $rawFeaturesJson, $sectionFeaturesJson,
            $humanLabel, $humanValidated,
            $communityEvidenceJson, $communityCapturedAtUtc
        )
        ON CONFLICT(SourceFilePath) DO UPDATE SET
            BeatmapId = excluded.BeatmapId,
            Md5 = excluded.Md5,
            FileSize = excluded.FileSize,
            FileLastWriteUtc = excluded.FileLastWriteUtc,
            FeatureSchemaVersion = excluded.FeatureSchemaVersion,
            AnalyzerVersion = excluded.AnalyzerVersion,
            CapturedAtUtc = excluded.CapturedAtUtc,
            RawFeaturesJson = excluded.RawFeaturesJson,
            SectionFeaturesJson = excluded.SectionFeaturesJson,
            HumanLabel = excluded.HumanLabel,
            HumanValidated = excluded.HumanValidated,
            CommunityEvidenceJson = excluded.CommunityEvidenceJson,
            CommunityCapturedAtUtc = excluded.CommunityCapturedAtUtc;
        """;

    /// <summary>
    /// Insère un nouvel échantillon ou remplace ses données lorsqu'il
    /// existe déjà pour le même fichier source. SampleId est conservé
    /// lors d'une mise à jour.
    /// </summary>
    internal void Upsert(MlDatasetSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentException.ThrowIfNullOrWhiteSpace(sample.SourceFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sample.RawFeaturesJson);

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = UpsertSql;

        command.Parameters.AddWithValue(
            "$sourceFilePath",
            sample.SourceFilePath);
        command.Parameters.AddWithValue(
            "$beatmapId",
            sample.BeatmapId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue(
            "$md5",
            sample.Md5 ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$fileSize", sample.FileSize);
        command.Parameters.AddWithValue(
            "$fileLastWriteUtc",
            sample.FileLastWriteUtc.Ticks);
        command.Parameters.AddWithValue(
            "$featureSchemaVersion",
            sample.FeatureSchemaVersion);
        command.Parameters.AddWithValue(
            "$analyzerVersion",
            sample.AnalyzerVersion);
        command.Parameters.AddWithValue(
            "$capturedAtUtc",
            sample.CapturedAtUtc.Ticks);
        command.Parameters.AddWithValue(
            "$rawFeaturesJson",
            sample.RawFeaturesJson);
        command.Parameters.AddWithValue(
            "$sectionFeaturesJson",
            sample.SectionFeaturesJson ?? (object)DBNull.Value);
        command.Parameters.AddWithValue(
            "$humanLabel",
            ToDatabaseHumanLabel(sample.HumanLabel)
                ?? (object)DBNull.Value);
        command.Parameters.AddWithValue(
            "$humanValidated",
            sample.HumanValidated ? 1 : 0);
        command.Parameters.AddWithValue(
            "$communityEvidenceJson",
            sample.CommunityEvidenceJson ?? (object)DBNull.Value);
        command.Parameters.AddWithValue(
            "$communityCapturedAtUtc",
            sample.CommunityCapturedAtUtc.HasValue
                ? sample.CommunityCapturedAtUtc.Value.Ticks
                : DBNull.Value);

        command.ExecuteNonQuery();
    }

    internal bool Delete(string sourceFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = """
            DELETE FROM MlDatasetSample
            WHERE SourceFilePath = $sourceFilePath;
            """;
        command.Parameters.AddWithValue("$sourceFilePath", sourceFilePath);

        return command.ExecuteNonQuery() > 0;
    }


    // ============================================================
    // CONNEXION
    // ============================================================

    private SqliteConnection OpenConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        };

        SqliteConnection connection = new(builder.ToString());

        try
        {
            connection.Open();

            using SqliteCommand pragma = connection.CreateCommand();
            pragma.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;
                """;
            pragma.ExecuteNonQuery();

            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }


    // ============================================================
    // CONVERSION
    // ============================================================

    private static MlDatasetSample ReadSample(SqliteDataReader reader)
    {
        return new MlDatasetSample
        {
            SampleId = reader.GetInt64(0),
            SourceFilePath = reader.GetString(1),
            BeatmapId = reader.IsDBNull(2)
                ? null
                : reader.GetInt32(2),
            Md5 = reader.IsDBNull(3)
                ? null
                : reader.GetString(3),
            FileSize = reader.GetInt64(4),
            FileLastWriteUtc = new DateTime(
                reader.GetInt64(5),
                DateTimeKind.Utc),
            FeatureSchemaVersion = reader.GetInt32(6),
            AnalyzerVersion = reader.GetInt32(7),
            CapturedAtUtc = new DateTime(
                reader.GetInt64(8),
                DateTimeKind.Utc),
            RawFeaturesJson = reader.GetString(9),
            SectionFeaturesJson = reader.IsDBNull(10)
                ? null
                : reader.GetString(10),
            HumanLabel = reader.IsDBNull(11)
                ? null
                : FromDatabaseHumanLabel(reader.GetString(11)),
            HumanValidated = reader.GetInt64(12) != 0,
            CommunityEvidenceJson = reader.IsDBNull(13)
                ? null
                : reader.GetString(13),
            CommunityCapturedAtUtc = reader.IsDBNull(14)
                ? null
                : new DateTime(
                    reader.GetInt64(14),
                    DateTimeKind.Utc),
        };
    }

    private static string? ToDatabaseHumanLabel(
        MlHumanLabel? label)
    {
        return label switch
        {
            null => null,
            MlHumanLabel.Stream => "Stream",
            MlHumanLabel.Jump => "Jump",
            MlHumanLabel.Tech => "Tech",
            MlHumanLabel.ClassicMixed => "Classic/Mixed",
            _ => throw new ArgumentOutOfRangeException(
                nameof(label),
                label,
                "Unsupported ML human label."),
        };
    }

    private static MlHumanLabel FromDatabaseHumanLabel(string value)
    {
        return value switch
        {
            "Stream" => MlHumanLabel.Stream,
            "Jump" => MlHumanLabel.Jump,
            "Tech" => MlHumanLabel.Tech,
            "Classic/Mixed" => MlHumanLabel.ClassicMixed,
            _ => throw new FormatException(
                "Unsupported ML human label in database."),
        };
    }
}
