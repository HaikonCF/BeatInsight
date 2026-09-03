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

    // Schéma d'une base neuve. Les bases créées avant V2.3.5b-1
    // possèdent encore une colonne HumanLabel unique : voir
    // MigrateLegacyHumanLabelColumnIfNeeded, appelée juste après.
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

            PrimaryHumanLabel TEXT NULL,
            SecondaryHumanLabel TEXT NULL,
            HumanValidated INTEGER NOT NULL,

            CommunityEvidenceJson TEXT NULL,
            CommunityCapturedAtUtc INTEGER NULL
        );
        """;

    /// <summary>
    /// Crée la table dédiée et ses index, migre une éventuelle
    /// colonne HumanLabel héritée, et ne modifie jamais la table de
    /// cache BeatmapAnalysis éventuellement présente dans la même
    /// base.
    ///
    /// Idempotent : rouvrir une base déjà migrée, ou une base neuve
    /// déjà à jour, ne modifie rien et ne lève pas.
    /// </summary>
    internal void EnsureSchema()
    {
        string? directory = Path.GetDirectoryName(databasePath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using SqliteConnection connection = OpenConnection();

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = CreateSchemaSql;
            command.ExecuteNonQuery();
        }

        MigrateLegacyHumanLabelColumnIfNeeded(connection);
        EnsureIndexes(connection);
    }

    /// <summary>
    /// Migre une base créée avant V2.3.5b-1 : renomme l'ancienne
    /// colonne HumanLabel en PrimaryHumanLabel et ajoute
    /// SecondaryHumanLabel à NULL. Aucun sample n'est perdu ; les
    /// autres colonnes (features, provenance, Community Evidence) ne
    /// sont pas touchées.
    ///
    /// Chaque étape vérifie l'état réel des colonnes avant d'agir,
    /// afin qu'une migration interrompue en cours de route (par
    /// exemple entre le renommage et l'ajout de colonne) puisse être
    /// reprise sans erreur au prochain appel.
    /// </summary>
    private static void MigrateLegacyHumanLabelColumnIfNeeded(
        SqliteConnection connection)
    {
        List<string> columns = ReadColumnNames(connection);

        bool hasLegacyColumn = columns.Contains("HumanLabel");
        bool hasPrimaryColumn = columns.Contains("PrimaryHumanLabel");

        if (hasLegacyColumn && !hasPrimaryColumn)
        {
            using SqliteCommand rename = connection.CreateCommand();

            rename.CommandText = """
                ALTER TABLE MlDatasetSample
                RENAME COLUMN HumanLabel TO PrimaryHumanLabel;
                """;
            rename.ExecuteNonQuery();

            columns = ReadColumnNames(connection);
        }

        if (!columns.Contains("SecondaryHumanLabel"))
        {
            using SqliteCommand addSecondary = connection.CreateCommand();

            addSecondary.CommandText = """
                ALTER TABLE MlDatasetSample
                ADD COLUMN SecondaryHumanLabel TEXT NULL;
                """;
            addSecondary.ExecuteNonQuery();
        }
    }

    private static List<string> ReadColumnNames(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(MlDatasetSample);";

        using SqliteDataReader reader = command.ExecuteReader();
        List<string> columns = [];

        // PRAGMA table_info renvoie le nom de colonne en position 1.
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    /// <summary>
    /// (Re)crée les index attendus. L'ancien index nommé
    /// IX_MlDatasetSample_HumanLabel référençait la colonne avant
    /// renommage ; il est supprimé explicitement plutôt que de
    /// compter sur un suivi automatique de SQLite.
    /// </summary>
    private static void EnsureIndexes(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = """
            DROP INDEX IF EXISTS IX_MlDatasetSample_HumanLabel;

            CREATE INDEX IF NOT EXISTS IX_MlDatasetSample_BeatmapId
                ON MlDatasetSample(BeatmapId);

            CREATE INDEX IF NOT EXISTS
                IX_MlDatasetSample_PrimaryHumanLabel
                ON MlDatasetSample(
                    PrimaryHumanLabel,
                    SecondaryHumanLabel,
                    HumanValidated);
            """;
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
        PrimaryHumanLabel, SecondaryHumanLabel, HumanValidated,
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
    /// Retourne les échantillons dont BeatmapId figure dans
    /// <paramref name="beatmapIds"/> (par exemple un pack de
    /// calibration), par ordre stable de SampleId. Un ID absent du
    /// dataset est simplement ignoré ; aucun sample n'est créé.
    ///
    /// L'ordre par pack (ex. Aim puis Stream puis...) est une
    /// préoccupation d'affichage, pas de stockage : voir
    /// <see cref="CalibrationQueue.OrderByPackSequence"/> côté appelant.
    /// </summary>
    internal IReadOnlyList<MlDatasetSample> FindCalibrationSamples(
        IReadOnlyCollection<int> beatmapIds)
    {
        ArgumentNullException.ThrowIfNull(beatmapIds);

        List<int> distinctIds = beatmapIds.Distinct().ToList();

        if (distinctIds.Count == 0)
        {
            return [];
        }

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        string placeholders = string.Join(
            ", ",
            distinctIds.Select((_, index) => $"$id{index}"));

        command.CommandText = $"""
            SELECT {SelectColumns}
            FROM MlDatasetSample
            WHERE BeatmapId IN ({placeholders})
            ORDER BY SampleId ASC;
            """;

        for (int index = 0; index < distinctIds.Count; index++)
        {
            command.Parameters.AddWithValue($"$id{index}", distinctIds[index]);
        }

        using SqliteDataReader reader = command.ExecuteReader();

        List<MlDatasetSample> samples = [];

        while (reader.Read())
        {
            samples.Add(ReadSample(reader));
        }

        return samples;
    }

    /// <summary>
    /// Retourne les échantillons dont BeatmapId est encore NULL, par
    /// ordre stable de SampleId. Destiné au backfill de métadonnées
    /// (voir <c>BeatmapIdBackfillService</c>) : cette lecture ne
    /// modifie rien.
    /// </summary>
    internal IReadOnlyList<MlDatasetSample> FindSamplesMissingBeatmapId()
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = $"""
            SELECT {SelectColumns}
            FROM MlDatasetSample
            WHERE BeatmapId IS NULL
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

    /// <summary>
    /// Met à jour uniquement la colonne BeatmapId d'un échantillon
    /// existant, par exemple depuis un backfill de métadonnées. Toutes
    /// les autres colonnes (features, labels humains, Community
    /// Evidence, versions de schéma/analyseur) restent strictement
    /// inchangées : contrairement à <see cref="Upsert"/>, cette
    /// méthode ne réécrit jamais la ligne entière.
    /// </summary>
    internal bool UpdateBeatmapId(string sourceFilePath, int beatmapId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = """
            UPDATE MlDatasetSample
            SET BeatmapId = $beatmapId
            WHERE SourceFilePath = $sourceFilePath;
            """;
        command.Parameters.AddWithValue("$beatmapId", beatmapId);
        command.Parameters.AddWithValue("$sourceFilePath", sourceFilePath);

        return command.ExecuteNonQuery() > 0;
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

    /// <summary>
    /// Retourne le premier échantillon non validé dont SampleId est
    /// strictement supérieur à <paramref name="afterSampleId"/> (ou le
    /// tout premier échantillon non validé si null), par ordre
    /// croissant de SampleId. Utilisé par le mode Fast Labeling pour
    /// avancer dans la file sans jamais revisiter un échantillon déjà
    /// validé.
    /// </summary>
    internal MlDatasetSample? FindNextUnlabeled(long? afterSampleId)
    {
        long anchor = afterSampleId ?? 0;

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = $"""
            SELECT {SelectColumns}
            FROM MlDatasetSample
            WHERE HumanValidated = 0 AND SampleId > $anchor
            ORDER BY SampleId ASC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$anchor", anchor);

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadSample(reader) : null;
    }

    /// <summary>
    /// Retourne le dernier échantillon non validé dont SampleId est
    /// strictement inférieur à <paramref name="beforeSampleId"/> (ou le
    /// tout dernier échantillon non validé si null), par ordre
    /// décroissant de SampleId. Symétrique de
    /// <see cref="FindNextUnlabeled"/>.
    /// </summary>
    internal MlDatasetSample? FindPreviousUnlabeled(long? beforeSampleId)
    {
        long anchor = beforeSampleId ?? long.MaxValue;

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = $"""
            SELECT {SelectColumns}
            FROM MlDatasetSample
            WHERE HumanValidated = 0 AND SampleId < $anchor
            ORDER BY SampleId DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$anchor", anchor);

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadSample(reader) : null;
    }

    /// <summary>
    /// Nombre d'échantillons validés par un humain. Utilisé par le
    /// panneau Fast Labeling pour afficher une progression sans
    /// exposer de SQL brut à l'UI.
    /// </summary>
    internal int CountValidated()
    {
        return CountWhere("HumanValidated <> 0");
    }

    /// <summary>
    /// Nombre d'échantillons restant à valider. Symétrique de
    /// <see cref="CountValidated"/>.
    /// </summary>
    internal int CountUnlabeled()
    {
        return CountWhere("HumanValidated = 0");
    }

    private int CountWhere(string sqlCondition)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = $"""
            SELECT COUNT(*)
            FROM MlDatasetSample
            WHERE {sqlCondition};
            """;

        return checked((int)(long)command.ExecuteScalar()!);
    }

    /// <summary>
    /// Retourne les compteurs nécessaires à la présentation du corpus ML.
    /// Cette agrégation reste dans le repository afin que les appelants, y
    /// compris l'UI, ne construisent jamais de SQL eux-mêmes.
    /// </summary>
    internal MlDatasetSampleStatistics GetStatistics()
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                COUNT(*),
                COALESCE(SUM(
                    CASE WHEN HumanValidated <> 0 THEN 1 ELSE 0 END), 0),
                COALESCE(SUM(
                    CASE WHEN PrimaryHumanLabel IS NULL
                         THEN 1 ELSE 0 END), 0)
            FROM MlDatasetSample;
            """;

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return new MlDatasetSampleStatistics(0, 0, 0);
        }

        return new MlDatasetSampleStatistics(
            SampleCount: checked((int)reader.GetInt64(0)),
            HumanValidatedCount: checked((int)reader.GetInt64(1)),
            UnlabeledCount: checked((int)reader.GetInt64(2)));
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
            PrimaryHumanLabel, SecondaryHumanLabel, HumanValidated,
            CommunityEvidenceJson, CommunityCapturedAtUtc
        ) VALUES (
            $sourceFilePath, $beatmapId, $md5,
            $fileSize, $fileLastWriteUtc,
            $featureSchemaVersion, $analyzerVersion, $capturedAtUtc,
            $rawFeaturesJson, $sectionFeaturesJson,
            $primaryHumanLabel, $secondaryHumanLabel, $humanValidated,
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
            PrimaryHumanLabel = excluded.PrimaryHumanLabel,
            SecondaryHumanLabel = excluded.SecondaryHumanLabel,
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
            "$primaryHumanLabel",
            ToDatabaseHumanLabel(sample.PrimaryHumanLabel)
                ?? (object)DBNull.Value);
        command.Parameters.AddWithValue(
            "$secondaryHumanLabel",
            ToDatabaseHumanLabel(sample.SecondaryHumanLabel)
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

    /// <summary>
    /// Met à jour l'annotation humaine primaire et secondaire d'un
    /// échantillon existant. Les features, la provenance et la preuve
    /// communautaire restent strictement inchangées.
    ///
    /// Règles validées avant toute écriture :
    /// - <paramref name="primary"/> est obligatoire (une validation
    ///   humaine implique toujours un label primaire) ;
    /// - <paramref name="secondary"/>, s'il est fourni, doit différer
    ///   de <paramref name="primary"/>.
    ///
    /// L'échantillon est marqué HumanValidated = true.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Si <paramref name="primary"/> est null, ou si
    /// <paramref name="secondary"/> est égal à
    /// <paramref name="primary"/>.
    /// </exception>
    internal bool UpdateHumanLabels(
        string sourceFilePath,
        MlHumanLabel? primary,
        MlHumanLabel? secondary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);

        if (primary is null)
        {
            throw new ArgumentException(
                "Un label primaire est requis pour valider un "
                    + "échantillon : HumanValidated = true implique "
                    + "PrimaryHumanLabel != null.",
                nameof(primary));
        }

        if (secondary is not null && secondary == primary)
        {
            throw new ArgumentException(
                "Le label secondaire doit différer du label "
                    + "primaire.",
                nameof(secondary));
        }

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = """
            UPDATE MlDatasetSample
            SET PrimaryHumanLabel = $primaryHumanLabel,
                SecondaryHumanLabel = $secondaryHumanLabel,
                HumanValidated = 1
            WHERE SourceFilePath = $sourceFilePath;
            """;
        command.Parameters.AddWithValue(
            "$primaryHumanLabel",
            ToDatabaseHumanLabel(primary));
        command.Parameters.AddWithValue(
            "$secondaryHumanLabel",
            ToDatabaseHumanLabel(secondary) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$sourceFilePath", sourceFilePath);

        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// Compatibilité avec l'appel mono-label existant (panneau HUMAN
    /// LABEL de MainWindow, non modifié par cette migration).
    /// Équivalent à <see cref="UpdateHumanLabels"/> sans label
    /// secondaire.
    /// </summary>
    internal bool UpdateHumanLabel(
        string sourceFilePath,
        MlHumanLabel humanLabel)
    {
        return UpdateHumanLabels(
            sourceFilePath,
            primary: humanLabel,
            secondary: null);
    }

    /// <summary>
    /// Supprime les deux annotations humaines d'un échantillon
    /// existant et réinitialise sa validation, sans supprimer ni
    /// recréer le sample.
    /// </summary>
    internal bool ClearHumanLabel(string sourceFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = """
            UPDATE MlDatasetSample
            SET PrimaryHumanLabel = NULL,
                SecondaryHumanLabel = NULL,
                HumanValidated = 0
            WHERE SourceFilePath = $sourceFilePath;
            """;
        command.Parameters.AddWithValue("$sourceFilePath", sourceFilePath);

        return command.ExecuteNonQuery() > 0;
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
            PrimaryHumanLabel = reader.IsDBNull(11)
                ? null
                : FromDatabaseHumanLabel(reader.GetString(11)),
            SecondaryHumanLabel = reader.IsDBNull(12)
                ? null
                : FromDatabaseHumanLabel(reader.GetString(12)),
            HumanValidated = reader.GetInt64(13) != 0,
            CommunityEvidenceJson = reader.IsDBNull(14)
                ? null
                : reader.GetString(14),
            CommunityCapturedAtUtc = reader.IsDBNull(15)
                ? null
                : new DateTime(
                    reader.GetInt64(15),
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
