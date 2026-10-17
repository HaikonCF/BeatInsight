using BeatInsight.Diagnostics.NativeLazer;
using Realms;
using System.IO;

namespace BeatInsight.Integration.Game;

internal interface ILazerBackgroundResolver
{
    string? Resolve(string beatmapBlobPath, int beatmapSetOnlineId);
}

internal sealed record LazerNamedFile(string Filename, string? Hash);

internal interface ILazerBeatmapSetFileCatalog
{
    IReadOnlyList<LazerNamedFile> ReadFiles(int beatmapSetOnlineId);
}

internal interface ILazerContentStore
{
    string? ResolveExistingBlob(string hash);
}

internal sealed class LazerBackgroundResolver(
    ILazerBeatmapSetFileCatalog catalog,
    ILazerContentStore contentStore) : ILazerBackgroundResolver
{
    internal LazerBackgroundResolver()
        : this(new LazerRealmBeatmapSetFileCatalog(), new NativeLazerContentStore())
    {
    }

    public string? Resolve(string beatmapBlobPath, int beatmapSetOnlineId)
    {
        if (beatmapSetOnlineId < 0)
            return null;

        string? eventFilename = BeatmapBackgroundResolver.ExtractBackgroundFileName(beatmapBlobPath);
        if (!TryNormalizeRelativePath(eventFilename, out string normalizedEvent))
            return null;

        IReadOnlyList<LazerNamedFile> files;
        try
        {
            files = catalog.ReadFiles(beatmapSetOnlineId);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }

        LazerNamedFile[] matches = files
            .Where(file => TryNormalizeRelativePath(file.Filename, out string normalized) &&
                           string.Equals(normalized, normalizedEvent, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        if (matches.Length != 1 ||
            matches[0].Hash is not string hash ||
            !NativeLazerRootFingerprintFactory.IsSha256(hash))
        {
            return null;
        }

        try
        {
            string? path = contentStore.ResolveExistingBlob(hash.ToUpperInvariant());
            if (string.IsNullOrWhiteSpace(path))
                return null;
            string fullPath = Path.GetFullPath(path);
            return File.Exists(fullPath) ? fullPath : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool TryNormalizeRelativePath(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string candidate = value.Trim().Replace('\\', '/');
        while (candidate.StartsWith("./", StringComparison.Ordinal))
            candidate = candidate[2..];
        string[] segments = candidate.Split('/', StringSplitOptions.None);
        if (segments.Length == 0 || segments.Any(segment =>
                string.IsNullOrWhiteSpace(segment) || segment is "." or ".." || segment.Contains(':')))
        {
            return false;
        }

        normalized = string.Join('/', segments);
        return true;
    }
}

internal sealed class NativeLazerContentStore : ILazerContentStore
{
    public string? ResolveExistingBlob(string hash)
    {
        (string? path, bool exists) = NativeLazerBlobPathResolver.Resolve(hash);
        return exists ? path : null;
    }
}

internal sealed class LazerRealmBeatmapSetFileCatalog : ILazerBeatmapSetFileCatalog
{
    public IReadOnlyList<LazerNamedFile> ReadFiles(int beatmapSetOnlineId)
    {
        if (beatmapSetOnlineId < 0)
            return [];

        string realmPath = ResolveRealmPath();
        var configuration = new RealmConfiguration(realmPath)
        {
            IsReadOnly = true,
            IsDynamic = true,
            ShouldDeleteIfMigrationNeeded = false,
            MigrationCallback = null,
            ShouldCompactOnLaunch = null
        };

        using Realm realm = Realm.GetInstance(configuration);
        if (realm.Config is not RealmConfiguration opened || !opened.IsReadOnly ||
            !opened.IsDynamic || realm.IsInTransaction)
        {
            return [];
        }

        string[] setClasses = realm.Schema
            .Select(schema => schema.Name)
            .Where(name => string.Equals(name, "BeatmapSet", StringComparison.Ordinal) ||
                           name.EndsWith(".BeatmapSet", StringComparison.Ordinal))
            .ToArray();
        if (setClasses.Length != 1)
            return [];

        IRealmObject[] sets = realm.DynamicApi.All(setClasses[0])
            .Filter("OnlineID == $0", beatmapSetOnlineId)
            .ToArray();
        if (sets.Length != 1 || !HasProperty(sets[0], "Files"))
            return [];

        var files = new List<LazerNamedFile>();
        foreach (IRealmObjectBase namedFile in sets[0].DynamicApi.GetList<IRealmObjectBase>("Files"))
        {
            string? filename = GetString(namedFile, "Filename");
            IRealmObjectBase? file = GetObject(namedFile, "File");
            string? hash = file is null ? null : GetString(file, "Hash");
            if (!string.IsNullOrWhiteSpace(filename))
                files.Add(new(filename, hash));
        }
        return files;
    }

    private static string ResolveRealmPath()
    {
        string? configured = Environment.GetEnvironmentVariable("BEATINSIGHT_LAZER_STORAGE_PATH");
        string storage = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "osu")
            : configured;
        string path = Path.Combine(Path.GetFullPath(storage), "client.realm");
        if (!File.Exists(path))
            throw new FileNotFoundException("Lazer client.realm was not found.", path);
        return path;
    }

    private static bool HasProperty(IRealmObjectBase value, string property) =>
        value.ObjectSchema?.Any(candidate =>
            string.Equals(candidate.Name, property, StringComparison.Ordinal)) == true;

    private static RealmValue? GetValue(IRealmObjectBase value, string property)
    {
        if (!HasProperty(value, property))
            return null;
        return value.DynamicApi.Get<RealmValue>(property);
    }

    private static string? GetString(IRealmObjectBase value, string property) =>
        GetValue(value, property)?.AsNullableString();

    private static IRealmObjectBase? GetObject(IRealmObjectBase value, string property) =>
        GetValue(value, property)?.AsNullableIRealmObject();
}

internal sealed class NullLazerBackgroundResolver : ILazerBackgroundResolver
{
    internal static NullLazerBackgroundResolver Instance { get; } = new();

    public string? Resolve(string beatmapBlobPath, int beatmapSetOnlineId) => null;
}
