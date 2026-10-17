using Realms;
using System.Globalization;
using System.IO;

namespace BeatInsight.Diagnostics.NativeLazer;

/// <summary>
/// Diagnostic-only dynamic Realm lookup. This type is not connected to game-state
/// composition and never opens a write transaction.
/// </summary>
public static class LazerRealmBeatmapLookup
{
    private const string storageVariable = "BEATINSIGHT_LAZER_STORAGE_PATH";
    private const string onlineIdVariable = "BEATINSIGHT_LAZER_LOOKUP_ONLINE_ID";
    private const string titleVariable = "BEATINSIGHT_LAZER_LOOKUP_TITLE";
    private const string difficultyVariable = "BEATINSIGHT_LAZER_LOOKUP_DIFFICULTY";
    private const string beatmapClass = "Beatmap";

    public static int RunFromEnvironment()
    {
        string? realmPath = null;
        RealmFileFingerprint? before = null;
        int exitCode = 1;

        try
        {
            LazerRealmLookupCriteria criteria = ReadCriteria();
            realmPath = ResolveRealmPath();
            before = RealmFileFingerprint.Capture(realmPath);

            LazerRealmLookupResult result = Lookup(realmPath, criteria);
            WriteResult(result);
            exitCode = result.Status == LazerRealmLookupStatus.Ambiguous ? 3 : 0;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"STATUS=FAIL");
            Console.Error.WriteLine($"ERROR_TYPE={exception.GetType().Name}");
            Console.Error.WriteLine($"ERROR={exception.Message}");
            exitCode = 1;
        }
        finally
        {
            if (realmPath is not null && before is not null)
            {
                try
                {
                    RealmFileFingerprint after = RealmFileFingerprint.Capture(realmPath);
                    if (before != after)
                    {
                        Console.Error.WriteLine("STATUS=FAIL");
                        Console.Error.WriteLine("ERROR=client.realm size or timestamp changed during read-only lookup.");
                        Console.Error.WriteLine($"BEFORE_SIZE={before.Length}");
                        Console.Error.WriteLine($"AFTER_SIZE={after.Length}");
                        Console.Error.WriteLine($"BEFORE_WRITE_UTC={before.LastWriteUtc:O}");
                        Console.Error.WriteLine($"AFTER_WRITE_UTC={after.LastWriteUtc:O}");
                        exitCode = 1;
                    }
                    else
                    {
                        Console.WriteLine("REALM_UNCHANGED=true");
                    }
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    Console.Error.WriteLine("STATUS=FAIL");
                    Console.Error.WriteLine($"ERROR=Unable to verify client.realm after lookup: {exception.Message}");
                    exitCode = 1;
                }
            }
        }

        return exitCode;
    }

    internal static LazerRealmLookupResult Lookup(
        string realmPath,
        LazerRealmLookupCriteria criteria)
    {
        var configuration = new RealmConfiguration(realmPath)
        {
            IsReadOnly = true,
            IsDynamic = true,
            ShouldDeleteIfMigrationNeeded = false,
            MigrationCallback = null,
            ShouldCompactOnLaunch = null
        };

        using Realm realm = Realm.GetInstance(configuration);
        if (realm.Config is not RealmConfiguration openedConfiguration ||
            !openedConfiguration.IsReadOnly ||
            !openedConfiguration.IsDynamic ||
            realm.IsInTransaction)
        {
            throw new InvalidOperationException("Realm did not open in the required read-only dynamic mode.");
        }

        string[] beatmapClasses = realm.Schema
            .Select(schema => schema.Name)
            .Where(name => string.Equals(name, beatmapClass, StringComparison.Ordinal) ||
                           name.EndsWith($".{beatmapClass}", StringComparison.Ordinal))
            .ToArray();
        if (beatmapClasses.Length != 1)
        {
            throw new InvalidDataException(
                $"Realm schema resolved {beatmapClasses.Length} {beatmapClass} classes; exactly one is required.");
        }

        var matches = new List<LazerRealmBeatmapRecord>();
        foreach (IRealmObject beatmap in realm.DynamicApi.All(beatmapClasses[0]))
        {
            LazerRealmBeatmapRecord record = ReadBeatmap(beatmap);
            if (Matches(record, criteria))
            {
                matches.Add(record);
            }
        }

        matches = matches
            .OrderBy(match => match.OnlineId ?? int.MaxValue)
            .ThenBy(match => match.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(match => match.Difficulty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(match => match.Guid)
            .ToList();

        return new(matches.Count switch
        {
            0 => LazerRealmLookupStatus.NotFound,
            1 => LazerRealmLookupStatus.Unique,
            _ => LazerRealmLookupStatus.Ambiguous
        }, criteria, matches);
    }

    private static LazerRealmBeatmapRecord ReadBeatmap(IRealmObject beatmap)
    {
        Guid? guid = GetGuid(beatmap, "ID");
        if (guid is null || guid == Guid.Empty)
        {
            throw new InvalidDataException("BeatmapInfo contains no valid ID GUID.");
        }

        IRealmObjectBase? beatmapSet = GetObject(beatmap, "BeatmapSet");
        IRealmObjectBase? metadata = GetObject(beatmap, "Metadata") ??
                                     (beatmapSet is null ? null : GetObject(beatmapSet, "Metadata"));
        string? beatmapHash = GetString(beatmap, "Hash");

        return new(
            guid.Value,
            GetInt32(beatmap, "OnlineID"),
            beatmapSet is null ? null : GetInt32(beatmapSet, "OnlineID"),
            GetString(beatmap, "DifficultyName"),
            metadata is null ? null : GetString(metadata, "Title"),
            metadata is null ? null : GetString(metadata, "Artist"),
            beatmapHash,
            ResolveFileHash(beatmapSet, beatmapHash));
    }

    private static string? ResolveFileHash(IRealmObjectBase? beatmapSet, string? beatmapHash)
    {
        Realms.Schema.ObjectSchema? setSchema = beatmapSet?.ObjectSchema;
        if (beatmapSet is null || string.IsNullOrWhiteSpace(beatmapHash) ||
            setSchema is null || !setSchema.Any(property =>
                string.Equals(property.Name, "Files", StringComparison.Ordinal)))
        {
            return null;
        }

        foreach (IRealmObjectBase namedFile in beatmapSet.DynamicApi.GetList<IRealmObjectBase>("Files"))
        {
            IRealmObjectBase? realmFile = GetObject(namedFile, "File");
            string? fileHash = realmFile is null ? null : GetString(realmFile, "Hash");
            if (string.Equals(fileHash, beatmapHash, StringComparison.OrdinalIgnoreCase))
            {
                return fileHash;
            }
        }

        return null;
    }

    private static bool Matches(
        LazerRealmBeatmapRecord record,
        LazerRealmLookupCriteria criteria)
    {
        if (criteria.OnlineId is not null)
        {
            return record.OnlineId == criteria.OnlineId &&
                   (criteria.Difficulty is null || EqualsText(record.Difficulty, criteria.Difficulty));
        }

        if (criteria.Title is not null)
        {
            return EqualsText(record.Title, criteria.Title) &&
                   (criteria.Difficulty is null || EqualsText(record.Difficulty, criteria.Difficulty));
        }

        return criteria.Difficulty is not null && EqualsText(record.Difficulty, criteria.Difficulty);
    }

    private static bool EqualsText(string? actual, string expected) =>
        string.Equals(actual?.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);

    private static RealmValue? GetValue(IRealmObjectBase value, string property)
    {
        Realms.Schema.ObjectSchema? schema = value.ObjectSchema;
        if (schema is null || !schema.Any(schemaProperty =>
                string.Equals(schemaProperty.Name, property, StringComparison.Ordinal)))
        {
            return null;
        }

        return value.DynamicApi.Get<RealmValue>(property);
    }

    private static string? GetString(IRealmObjectBase value, string property)
    {
        RealmValue? realmValue = GetValue(value, property);
        return realmValue?.AsNullableString();
    }

    private static Guid? GetGuid(IRealmObjectBase value, string property)
    {
        RealmValue? realmValue = GetValue(value, property);
        return realmValue?.AsNullableGuid();
    }

    private static int? GetInt32(IRealmObjectBase value, string property)
    {
        RealmValue? realmValue = GetValue(value, property);
        if (realmValue is null)
        {
            return null;
        }

        object? raw = realmValue.Value.AsAny();
        return raw is null ? null : Convert.ToInt32(raw, CultureInfo.InvariantCulture);
    }

    private static IRealmObjectBase? GetObject(IRealmObjectBase value, string property)
    {
        RealmValue? realmValue = GetValue(value, property);
        return realmValue?.AsNullableIRealmObject();
    }

    private static LazerRealmLookupCriteria ReadCriteria()
    {
        int? onlineId = ParseOptionalPositiveInt(onlineIdVariable);
        string? title = NormalizeOptionalText(Environment.GetEnvironmentVariable(titleVariable));
        string? difficulty = NormalizeOptionalText(Environment.GetEnvironmentVariable(difficultyVariable));

        if (onlineId is null && title is null && difficulty is null)
        {
            throw new InvalidDataException(
                $"Set at least one of {onlineIdVariable}, {titleVariable}, or {difficultyVariable}.");
        }

        return new(onlineId, title, difficulty);
    }

    private static int? ParseOptionalPositiveInt(string variable)
    {
        string? value = NormalizeOptionalText(Environment.GetEnvironmentVariable(variable));
        if (value is null)
        {
            return null;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) || parsed <= 0)
        {
            throw new InvalidDataException($"{variable} must be a positive integer.");
        }

        return parsed;
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ResolveRealmPath()
    {
        string? configured = NormalizeOptionalText(Environment.GetEnvironmentVariable(storageVariable));
        string storage = configured ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osu");
        string fullStorage = Path.GetFullPath(storage);
        string realmPath = Path.Combine(fullStorage, "client.realm");

        if (!File.Exists(realmPath))
        {
            throw new FileNotFoundException("Lazer client.realm was not found.", realmPath);
        }

        return realmPath;
    }

    private static void WriteResult(LazerRealmLookupResult result)
    {
        Console.WriteLine($"STATUS={result.Status switch
        {
            LazerRealmLookupStatus.NotFound => "NOT_FOUND",
            LazerRealmLookupStatus.Unique => "UNIQUE",
            LazerRealmLookupStatus.Ambiguous => "AMBIGUOUS",
            _ => throw new ArgumentOutOfRangeException()
        }}");
        Console.WriteLine($"RESULT_COUNT={result.Matches.Count}");
        Console.WriteLine($"AUTHORITY={result.Criteria.Authority}");

        for (int index = 0; index < result.Matches.Count; index++)
        {
            LazerRealmBeatmapRecord match = result.Matches[index];
            Console.WriteLine($"RESULT_INDEX={index + 1}");
            Console.WriteLine($"GUID={match.Guid:D}");
            Console.WriteLine($"ONLINE_ID={Format(match.OnlineId)}");
            Console.WriteLine($"SET_ONLINE_ID={Format(match.BeatmapSetOnlineId)}");
            Console.WriteLine($"DIFFICULTY={Format(match.Difficulty)}");
            Console.WriteLine($"TITLE={Format(match.Title)}");
            Console.WriteLine($"ARTIST={Format(match.Artist)}");
            Console.WriteLine($"BEATMAP_HASH={Format(match.BeatmapHash)}");
            Console.WriteLine($"FILE_HASH={Format(match.FileHash)}");
        }
    }

    private static string Format(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Format(string? value) => value ?? string.Empty;
}

internal sealed record RealmFileFingerprint(long Length, DateTime LastWriteUtc)
{
    internal static RealmFileFingerprint Capture(string path)
    {
        var file = new FileInfo(path);
        file.Refresh();
        if (!file.Exists)
        {
            throw new FileNotFoundException("Lazer client.realm disappeared.", path);
        }

        return new(file.Length, file.LastWriteTimeUtc);
    }
}

internal enum LazerRealmLookupStatus { NotFound, Unique, Ambiguous }

internal sealed record LazerRealmLookupCriteria(
    int? OnlineId,
    string? Title,
    string? Difficulty)
{
    internal string Authority => OnlineId is not null
        ? Difficulty is not null ? "ONLINE_ID_AND_DIFFICULTY" : "ONLINE_ID"
        : Title is not null
            ? Difficulty is not null ? "TITLE_AND_DIFFICULTY_DIAGNOSTIC" : "TITLE_ONLY_DIAGNOSTIC"
            : "DIFFICULTY_ONLY_DIAGNOSTIC";
}

internal sealed record LazerRealmBeatmapRecord(
    Guid Guid,
    int? OnlineId,
    int? BeatmapSetOnlineId,
    string? Difficulty,
    string? Title,
    string? Artist,
    string? BeatmapHash,
    string? FileHash);

internal sealed record LazerRealmLookupResult(
    LazerRealmLookupStatus Status,
    LazerRealmLookupCriteria Criteria,
    IReadOnlyList<LazerRealmBeatmapRecord> Matches);
