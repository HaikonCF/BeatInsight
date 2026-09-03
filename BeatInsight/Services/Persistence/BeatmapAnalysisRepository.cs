using BeatInsight.Models.Persistence;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Text.Json;

namespace BeatInsight.Services.Persistence;

/// <summary>
/// Stockage SQLite des analyses de beatmaps.
///
/// SQL BRUT, AUCUN ORM
///
/// Toutes les requêtes sont écrites à la main et paramétrées. Le
/// repository ne connaît que <see cref="BeatmapAnalysisRecord"/> et
/// <see cref="GameplayProfileRecord"/> : il ne référence ni
/// BeatmapParser, ni GameplayAnalyzer, ni aucun modèle de domaine.
///
/// AUCUNE LOGIQUE MÉTIER, AUCUNE INVALIDATION
///
/// Ce type lit et écrit des lignes, rien de plus. Il ne compare
/// jamais les versions ni les horodatages : décider qu'un
/// enregistrement est périmé relève du futur service de cache, pas du
/// stockage. Find retourne la ligne telle qu'elle est stockée.
///
/// TOLÉRANCE AUX DONNÉES ILLISIBLES
///
/// Une ligne dont le JSON est corrompu ou incomplet est traitée comme
/// un miss : <see cref="Find"/> retourne null au lieu de propager une
/// exception. Une analyse pourra ainsi toujours être recalculée, ce
/// qui rend le cache non bloquant par construction.
/// </summary>
internal sealed class BeatmapAnalysisRepository
{
    private readonly string databasePath;

    /// <summary>
    /// Emplacement de la base utilisée à l'exécution :
    /// %LOCALAPPDATA%\BeatInsight\beatinsight.db
    ///
    /// LOCALAPPDATA est retenu plutôt que le dossier de l'exécutable
    /// car BeatInsight est distribué en archive extractible : un
    /// index placé à côté de l'exe serait perdu à chaque mise à jour,
    /// et le dossier d'installation peut être en lecture seule.
    /// </summary>
    internal static string DefaultDatabasePath =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "BeatInsight",
            "beatinsight.db");

    /// <summary>
    /// Crée un repository sur la base indiquée.
    ///
    /// Le chemin est explicite afin que les tests puissent utiliser
    /// une base temporaire dédiée et ne jamais toucher à la base de
    /// l'utilisateur.
    /// </summary>
    internal BeatmapAnalysisRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        this.databasePath = databasePath;
    }


    // ============================================================
    // SCHÉMA
    // ============================================================

    private const string CreateSchemaSql = """
        CREATE TABLE IF NOT EXISTS BeatmapAnalysis (
            FilePath TEXT NOT NULL PRIMARY KEY,
            FileSize INTEGER NOT NULL,
            FileLastWriteUtc INTEGER NOT NULL,
            AnalyzerVersion INTEGER NOT NULL,
            SchemaVersion INTEGER NOT NULL,
            BeatmapId INTEGER NULL,
            Md5 TEXT NULL,
            AnalysedAtUtc INTEGER NOT NULL,

            Title TEXT NOT NULL,
            Artist TEXT NOT NULL,
            Creator TEXT NOT NULL,
            Version TEXT NOT NULL,
            LengthTicks INTEGER NOT NULL,
            Bpm INTEGER NOT NULL,
            MaxCombo INTEGER NOT NULL,
            Ar REAL NOT NULL,
            Od REAL NOT NULL,
            Cs REAL NOT NULL,
            Hp REAL NOT NULL,
            CircleCount INTEGER NOT NULL,
            SliderCount INTEGER NOT NULL,
            SpinnerCount INTEGER NOT NULL,
            OsuStarRating REAL NOT NULL,
            BeatInsightRating REAL NOT NULL,

            IdentityPrimary TEXT NOT NULL,
            IdentitySecondary TEXT NOT NULL,
            IdentityPattern TEXT NOT NULL,
            IdentityConfidence REAL NOT NULL,
            TraitsJson TEXT NOT NULL DEFAULT '[]',
            ProfileJson TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS IX_BeatmapAnalysis_BeatmapId
            ON BeatmapAnalysis(BeatmapId);

        CREATE INDEX IF NOT EXISTS IX_BeatmapAnalysis_Identity
            ON BeatmapAnalysis(IdentityPrimary, IdentitySecondary);
        """;

    /// <summary>
    /// Crée la base, son dossier parent et son schéma s'ils sont
    /// absents.
    ///
    /// Idempotent : peut être appelé à chaque démarrage. Supprimer le
    /// fichier de base est donc une opération sûre, BeatInsight le
    /// recrée vide et recalcule tout.
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

    private const string SelectSql = """
        SELECT FilePath, FileSize, FileLastWriteUtc, AnalyzerVersion,
               SchemaVersion, BeatmapId, Md5, AnalysedAtUtc,
               Title, Artist, Creator, Version, LengthTicks, Bpm,
               MaxCombo, Ar, Od, Cs, Hp, CircleCount, SliderCount,
               SpinnerCount, OsuStarRating, BeatInsightRating,
               IdentityPrimary, IdentitySecondary, IdentityPattern,
               IdentityConfidence, TraitsJson, ProfileJson
        FROM BeatmapAnalysis
        WHERE FilePath = $filePath;
        """;

    /// <summary>
    /// Retourne l'enregistrement associé au chemin, ou null s'il est
    /// absent ou illisible.
    ///
    /// Aucune notion de fraîcheur n'est appliquée ici : comparer les
    /// versions et les horodatages est la responsabilité de
    /// l'appelant.
    /// </summary>
    internal BeatmapAnalysisRecord? Find(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = SelectSql;
        command.Parameters.AddWithValue("$filePath", filePath);

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        try
        {
            return ReadRecord(reader);
        }
        catch (JsonException)
        {
            // JSON corrompu : traité comme un miss afin que
            // l'analyse puisse être recalculée normalement.
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Retourne les BeatmapId déjà présents dans l'index runtime local. Cette
    /// lecture sert à enrichir des candidats distants sans parcourir le dossier
    /// Songs, et ne déduit jamais qu'une map non indexée n'est pas possédée.
    /// </summary>
    internal HashSet<int> FindOwnedBeatmapIds(
        IReadOnlyCollection<int> beatmapIds)
    {
        ArgumentNullException.ThrowIfNull(beatmapIds);

        int[] ids = beatmapIds
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return [];
        }

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        string placeholders = string.Join(
            ", ",
            ids.Select((_, index) => $"$id{index}"));

        command.CommandText = $"""
            SELECT DISTINCT BeatmapId
            FROM BeatmapAnalysis
            WHERE BeatmapId IN ({placeholders});
            """;

        for (int index = 0; index < ids.Length; index++)
        {
            command.Parameters.AddWithValue($"$id{index}", ids[index]);
        }

        using SqliteDataReader reader = command.ExecuteReader();

        var owned = new HashSet<int>();

        while (reader.Read())
        {
            owned.Add(reader.GetInt32(0));
        }

        return owned;
    }

    /// <summary>
    /// Retourne le dernier chemin source indexé pour une difficulté osu!
    /// précise. La découverte communautaire utilise cette lecture ciblée pour
    /// charger une map déjà possédée sans explorer le dossier Songs.
    /// </summary>
    internal string? FindSourceFilePathByBeatmapId(int beatmapId)
    {
        if (beatmapId <= 0)
        {
            return null;
        }

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT FilePath
            FROM BeatmapAnalysis
            WHERE BeatmapId = $beatmapId
            ORDER BY AnalysedAtUtc DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$beatmapId", beatmapId);

        object? value = command.ExecuteScalar();

        return value is string filePath ? filePath : null;
    }


    // ============================================================
    // ÉCRITURE
    // ============================================================

    // INSERT OR REPLACE est sûr ici : toutes les colonnes de la table
    // sont fournies à chaque écriture, aucune valeur existante ne peut
    // donc être perdue lors du remplacement.
    private const string UpsertSql = """
        INSERT OR REPLACE INTO BeatmapAnalysis (
            FilePath, FileSize, FileLastWriteUtc, AnalyzerVersion,
            SchemaVersion, BeatmapId, Md5, AnalysedAtUtc,
            Title, Artist, Creator, Version, LengthTicks, Bpm,
            MaxCombo, Ar, Od, Cs, Hp, CircleCount, SliderCount,
            SpinnerCount, OsuStarRating, BeatInsightRating,
            IdentityPrimary, IdentitySecondary, IdentityPattern,
            IdentityConfidence, TraitsJson, ProfileJson
        ) VALUES (
            $filePath, $fileSize, $fileLastWriteUtc, $analyzerVersion,
            $schemaVersion, $beatmapId, $md5, $analysedAtUtc,
            $title, $artist, $creator, $version, $lengthTicks, $bpm,
            $maxCombo, $ar, $od, $cs, $hp, $circleCount, $sliderCount,
            $spinnerCount, $osuStarRating, $beatInsightRating,
            $identityPrimary, $identitySecondary, $identityPattern,
            $identityConfidence, $traitsJson, $profileJson
        );
        """;

    /// <summary>
    /// Insère l'enregistrement, ou remplace celui qui porte déjà le
    /// même FilePath.
    /// </summary>
    internal void Upsert(BeatmapAnalysisRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = UpsertSql;

        command.Parameters.AddWithValue("$filePath", record.FilePath);
        command.Parameters.AddWithValue("$fileSize", record.FileSize);
        command.Parameters.AddWithValue(
            "$fileLastWriteUtc",
            record.FileLastWriteUtc.Ticks);
        command.Parameters.AddWithValue(
            "$analyzerVersion",
            record.AnalyzerVersion);
        command.Parameters.AddWithValue(
            "$schemaVersion",
            record.SchemaVersion);
        command.Parameters.AddWithValue(
            "$beatmapId",
            record.BeatmapId.HasValue
                ? record.BeatmapId.Value
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$md5",
            record.Md5 ?? (object)DBNull.Value);
        command.Parameters.AddWithValue(
            "$analysedAtUtc",
            record.AnalysedAtUtc.Ticks);

        command.Parameters.AddWithValue("$title", record.Title);
        command.Parameters.AddWithValue("$artist", record.Artist);
        command.Parameters.AddWithValue("$creator", record.Creator);
        command.Parameters.AddWithValue("$version", record.Version);
        command.Parameters.AddWithValue(
            "$lengthTicks",
            record.LengthTicks);
        command.Parameters.AddWithValue("$bpm", record.BPM);
        command.Parameters.AddWithValue("$maxCombo", record.MaxCombo);
        command.Parameters.AddWithValue("$ar", record.AR);
        command.Parameters.AddWithValue("$od", record.OD);
        command.Parameters.AddWithValue("$cs", record.CS);
        command.Parameters.AddWithValue("$hp", record.HP);
        command.Parameters.AddWithValue(
            "$circleCount",
            record.CircleCount);
        command.Parameters.AddWithValue(
            "$sliderCount",
            record.SliderCount);
        command.Parameters.AddWithValue(
            "$spinnerCount",
            record.SpinnerCount);
        command.Parameters.AddWithValue(
            "$osuStarRating",
            record.OsuStarRating);
        command.Parameters.AddWithValue(
            "$beatInsightRating",
            record.BeatInsightRating);

        GameplayProfileRecord profile = record.Profile;

        command.Parameters.AddWithValue(
            "$identityPrimary",
            profile.IdentityPrimary);
        command.Parameters.AddWithValue(
            "$identitySecondary",
            profile.IdentitySecondary);
        command.Parameters.AddWithValue(
            "$identityPattern",
            profile.IdentityPattern);
        command.Parameters.AddWithValue(
            "$identityConfidence",
            profile.IdentityConfidence);
        command.Parameters.AddWithValue(
            "$traitsJson",
            SerializeTraits(profile.Traits));
        command.Parameters.AddWithValue(
            "$profileJson",
            SerializeProfile(profile));

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Supprime l'enregistrement associé au chemin.
    /// </summary>
    /// <returns>true si une ligne a été supprimée.</returns>
    internal bool Delete(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = """
            DELETE FROM BeatmapAnalysis WHERE FilePath = $filePath;
            """;

        command.Parameters.AddWithValue("$filePath", filePath);

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

        // Si l'ouverture ou les PRAGMA échouent (fichier illisible,
        // base corrompue, verrou), la connexion doit être libérée
        // avant que l'exception ne remonte. Sans cela elle fuirait et
        // conserverait un handle sur le fichier.
        try
        {
            connection.Open();

            using SqliteCommand pragma = connection.CreateCommand();

            // WAL survit à la fermeture (propriété du fichier) et
            // permet des lectures concurrentes pendant une écriture,
            // ce dont le futur scan incrémental aura besoin.
            //
            // synchronous=NORMAL est propre à la connexion et doit
            // donc être réappliqué à chaque ouverture.
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
    // SÉRIALISATION
    //
    // Écriture et lecture explicites, champ par champ.
    //
    // La sérialisation par réflexion n'est pas utilisable : les
    // propriétés des enregistrements sont internal, et
    // System.Text.Json ignore les membres non publics. Écrire le JSON
    // à la main évite surtout de rendre les DTO publics uniquement
    // pour satisfaire un sérialiseur, et fixe un format stable dont
    // toute évolution passe par PersistenceSchemaVersion.
    // ============================================================

    private static string SerializeTraits(IReadOnlyList<string> traits)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();

            foreach (string trait in traits)
            {
                writer.WriteStringValue(trait);
            }

            writer.WriteEndArray();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static List<string> DeserializeTraits(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException(
                "TraitsJson doit être un tableau JSON.");
        }

        List<string> traits = [];

        foreach (JsonElement element in
                 document.RootElement.EnumerateArray())
        {
            traits.Add(element.GetString() ?? string.Empty);
        }

        return traits;
    }

    /// <summary>
    /// Sérialise les valeurs scalaires du profil.
    ///
    /// L'identité et les traits sont volontairement exclus : ils
    /// disposent de colonnes dédiées afin de rester requêtables en
    /// SQL, et les dupliquer ici créerait deux sources de vérité.
    /// </summary>
    private static string SerializeProfile(GameplayProfileRecord profile)
    {
        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();

            // Familles structurelles
            writer.WriteNumber("StreamRatio", profile.StreamRatio);
            writer.WriteNumber("JumpRatio", profile.JumpRatio);
            writer.WriteNumber("BurstRatio", profile.BurstRatio);

            // Tech : les trois sémantiques restent distinctes.
            writer.WriteNumber("TechPresence", profile.TechPresence);
            writer.WriteNumber("TechScore", profile.TechScore);
            writer.WriteNumber(
                "TechTransitionSignal",
                profile.TechTransitionSignal);
            writer.WriteNumber(
                "TechStructureSignal",
                profile.TechStructureSignal);
            writer.WriteNumber(
                "TechSpatialSignal",
                profile.TechSpatialSignal);
            writer.WriteNumber(
                "TechTemporalSignal",
                profile.TechTemporalSignal);

            // Reading
            writer.WriteNumber("ReadScore", profile.ReadScore);
            writer.WriteNumber("ReadCoverage", profile.ReadCoverage);
            writer.WriteString(
                "ReadIntensity",
                profile.ReadIntensity);
            writer.WriteNumber(
                "ReadSectionCount",
                profile.ReadSectionCount);
            writer.WriteNumber(
                "ReadDensitySignal",
                profile.ReadDensitySignal);
            writer.WriteNumber(
                "ReadClutterSignal",
                profile.ReadClutterSignal);
            writer.WriteNumber("ReadCSSignal", profile.ReadCSSignal);
            writer.WriteNumber(
                "ReadPredictability",
                profile.ReadPredictability);
            writer.WriteNumber("ReadNovelty", profile.ReadNovelty);
            writer.WriteNumber(
                "ReadTemporalRegularity",
                profile.ReadTemporalRegularity);
            writer.WriteNumber(
                "ReadSpacingRegularity",
                profile.ReadSpacingRegularity);
            writer.WriteNumber(
                "ReadTrajectoryRepetition",
                profile.ReadTrajectoryRepetition);
            writer.WriteNumber(
                "ReadAmbiguity",
                profile.ReadAmbiguity);

            // Speed
            writer.WriteNumber("SpeedScore", profile.SpeedScore);
            writer.WriteNumber(
                "SpeedFastObjectRatio",
                profile.SpeedFastObjectRatio);
            writer.WriteNumber(
                "SpeedDensitySignal",
                profile.SpeedDensitySignal);
            writer.WriteNumber(
                "SpeedARSignal",
                profile.SpeedARSignal);

            // Aim
            writer.WriteNumber("AimScore", profile.AimScore);
            writer.WriteNumber(
                "AimDistanceSignal",
                profile.AimDistanceSignal);
            writer.WriteNumber(
                "AimSpeedSignal",
                profile.AimSpeedSignal);
            writer.WriteNumber(
                "AimAngleSignal",
                profile.AimAngleSignal);
            writer.WriteNumber(
                "AimTemporalSignal",
                profile.AimTemporalSignal);

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }


    // ============================================================
    // LECTURE DE LIGNE
    // ============================================================

    private static BeatmapAnalysisRecord ReadRecord(
        SqliteDataReader reader)
    {
        string traitsJson = reader.GetString(28);
        string profileJson = reader.GetString(29);

        using JsonDocument document = JsonDocument.Parse(profileJson);
        JsonElement root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException(
                "ProfileJson doit être un objet JSON.");
        }

        GameplayProfileRecord profile = new()
        {
            IdentityPrimary = reader.GetString(24),
            IdentitySecondary = reader.GetString(25),
            IdentityPattern = reader.GetString(26),
            IdentityConfidence = reader.GetDouble(27),
            Traits = DeserializeTraits(traitsJson),

            StreamRatio = Number(root, "StreamRatio"),
            JumpRatio = Number(root, "JumpRatio"),
            BurstRatio = Number(root, "BurstRatio"),

            TechPresence = Number(root, "TechPresence"),
            TechScore = Number(root, "TechScore"),
            TechTransitionSignal =
                Number(root, "TechTransitionSignal"),
            TechStructureSignal = Number(root, "TechStructureSignal"),
            TechSpatialSignal = Number(root, "TechSpatialSignal"),
            TechTemporalSignal = Number(root, "TechTemporalSignal"),

            ReadScore = Number(root, "ReadScore"),
            ReadCoverage = Number(root, "ReadCoverage"),
            ReadIntensity = Text(root, "ReadIntensity"),
            ReadSectionCount = Integer(root, "ReadSectionCount"),
            ReadDensitySignal = Number(root, "ReadDensitySignal"),
            ReadClutterSignal = Number(root, "ReadClutterSignal"),
            ReadCSSignal = Number(root, "ReadCSSignal"),
            ReadPredictability = Number(root, "ReadPredictability"),
            ReadNovelty = Number(root, "ReadNovelty"),
            ReadTemporalRegularity =
                Number(root, "ReadTemporalRegularity"),
            ReadSpacingRegularity =
                Number(root, "ReadSpacingRegularity"),
            ReadTrajectoryRepetition =
                Number(root, "ReadTrajectoryRepetition"),
            ReadAmbiguity = Number(root, "ReadAmbiguity"),

            SpeedScore = Number(root, "SpeedScore"),
            SpeedFastObjectRatio =
                Number(root, "SpeedFastObjectRatio"),
            SpeedDensitySignal = Number(root, "SpeedDensitySignal"),
            SpeedARSignal = Number(root, "SpeedARSignal"),

            AimScore = Number(root, "AimScore"),
            AimDistanceSignal = Number(root, "AimDistanceSignal"),
            AimSpeedSignal = Number(root, "AimSpeedSignal"),
            AimAngleSignal = Number(root, "AimAngleSignal"),
            AimTemporalSignal = Number(root, "AimTemporalSignal"),
        };

        return new BeatmapAnalysisRecord
        {
            FilePath = reader.GetString(0),
            FileSize = reader.GetInt64(1),
            FileLastWriteUtc = new DateTime(
                reader.GetInt64(2),
                DateTimeKind.Utc),
            AnalyzerVersion = reader.GetInt32(3),
            SchemaVersion = reader.GetInt32(4),
            BeatmapId = reader.IsDBNull(5)
                ? null
                : reader.GetInt32(5),
            Md5 = reader.IsDBNull(6)
                ? null
                : reader.GetString(6),
            AnalysedAtUtc = new DateTime(
                reader.GetInt64(7),
                DateTimeKind.Utc),

            Title = reader.GetString(8),
            Artist = reader.GetString(9),
            Creator = reader.GetString(10),
            Version = reader.GetString(11),
            LengthTicks = reader.GetInt64(12),
            BPM = reader.GetInt32(13),
            MaxCombo = reader.GetInt32(14),
            AR = reader.GetDouble(15),
            OD = reader.GetDouble(16),
            CS = reader.GetDouble(17),
            HP = reader.GetDouble(18),
            CircleCount = reader.GetInt32(19),
            SliderCount = reader.GetInt32(20),
            SpinnerCount = reader.GetInt32(21),
            OsuStarRating = reader.GetDouble(22),
            BeatInsightRating = reader.GetDouble(23),

            Profile = profile,
        };
    }

    // Accès stricts : une propriété absente ou du mauvais type rend la
    // ligne illisible, donc équivalente à un miss.

    private static double Number(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement element)
            || element.ValueKind != JsonValueKind.Number)
        {
            throw new JsonException(
                $"ProfileJson : propriété numérique '{name}' "
                + "absente ou invalide.");
        }

        return element.GetDouble();
    }

    private static int Integer(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement element)
            || element.ValueKind != JsonValueKind.Number
            || !element.TryGetInt32(out int value))
        {
            throw new JsonException(
                $"ProfileJson : propriété entière '{name}' "
                + "absente ou invalide.");
        }

        return value;
    }

    private static string Text(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement element)
            || element.ValueKind != JsonValueKind.String)
        {
            throw new JsonException(
                $"ProfileJson : propriété texte '{name}' "
                + "absente ou invalide.");
        }

        return element.GetString() ?? string.Empty;
    }
}
