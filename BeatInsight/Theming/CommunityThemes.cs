using BeatInsight.Overlay;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BeatInsight.Theming;

public sealed record ThemeDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Author { get; init; }
    public string Version { get; init; } = "1.0.0";
    public Dictionary<string, string>? App { get; init; }
    public Dictionary<string, string>? Overlay { get; init; }
    public Dictionary<string, string> Assets { get; init; } = new(StringComparer.Ordinal);

    [JsonIgnore]
    public bool BuiltIn { get; init; }

    [JsonIgnore]
    public string? RootPath { get; init; }

    [JsonIgnore]
    public bool HasAppTheme => App is not null;

    [JsonIgnore]
    public bool HasOverlayTheme => Overlay is not null;
}

public sealed record ThemeOperationResult<T>(
    bool Success,
    T? Value,
    string? Error,
    IReadOnlyList<string> Warnings)
{
    public static ThemeOperationResult<T> Ok(T value, params string[] warnings) =>
        new(true, value, null, warnings);

    public static ThemeOperationResult<T> Fail(string error) =>
        new(false, default, error, Array.Empty<string>());
}

public static class CommunityThemeContract
{
    public const int SchemaVersion = 1;

    public static readonly IReadOnlyDictionary<string, string> AppTokenMap =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AppBackground"] = "background",
            ["AppSurface"] = "surface",
            ["AppSurfaceAlt"] = "surface.alt",
            ["AppBorder"] = "border",
            ["AppTextPrimary"] = "text.primary",
            ["AppTextSecondary"] = "text.secondary",
            ["AppAccent"] = "accent.primary",
            ["AppAccentSecondary"] = "accent.soft",
            ["AppDanger"] = "danger",
            ["AppSuccess"] = "success",
            ["AppMuted"] = "text.muted"
        };

    public static readonly IReadOnlySet<string> OverlayTokens = new HashSet<string>(StringComparer.Ordinal)
    {
        "OverlayBackground", "OverlaySurface", "OverlayBorder",
        "OverlayTextPrimary", "OverlayTextSecondary", "OverlayAccent",
        "OverlayPositive", "OverlayWarning", "OverlayDanger", "OverlayMuted",
        "CurrentAccent", "NextAccent", "LiveStatsAccent", "PerformanceAccent",
        "EditBorder", "EditHandle"
    };

    public static readonly IReadOnlySet<string> AssetKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "logo", "background", "mascot", "decorative"
    };

    private static readonly Regex ColorPattern = new(
        "^#(?:[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$",
        RegexOptions.CultureInvariant);

    private static readonly Regex IdPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._-]{1,63}$",
        RegexOptions.CultureInvariant);

    public static bool IsValidId(string? id) =>
        id is not null && IdPattern.IsMatch(id);

    public static bool TryNormalizeColor(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (value is null || !ColorPattern.IsMatch(value))
            return false;

        normalized = value.Length == 7
            ? $"#FF{value[1..].ToUpperInvariant()}"
            : value.ToUpperInvariant();
        return true;
    }

    public static bool IsSafeAssetPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) ||
            Uri.TryCreate(path, UriKind.Absolute, out _))
        {
            return false;
        }

        string[] parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(part => part is "." or ".."))
            return false;

        string extension = Path.GetExtension(parts[^1]);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".ico", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class CommunityThemeRegistry
{
    private const long MaximumAssetBytes = 20L * 1024 * 1024;
    private const long MaximumPackageBytes = 50L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string themesRoot;

    public CommunityThemeRegistry(string? themesRoot = null)
    {
        this.themesRoot = string.IsNullOrWhiteSpace(themesRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BeatInsight",
                "Themes")
            : Path.GetFullPath(themesRoot);
    }

    public string ThemesRoot => themesRoot;

    public IReadOnlyList<ThemeDefinition> LoadCatalog()
    {
        var themes = new List<ThemeDefinition>
        {
            BuiltIn(AppThemeIds.Default, "Default"),
            BuiltIn(AppThemeIds.Bia, "Bia")
        };

        try
        {
            if (!Directory.Exists(themesRoot))
                return themes;

            foreach (string directory in Directory.EnumerateDirectories(themesRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                string manifest = Path.Combine(directory, "theme.json");
                if (!File.Exists(manifest))
                    continue;

                ThemeOperationResult<ThemeDefinition> result = LoadAndValidate(manifest, directory);
                if (result.Success && result.Value is not null)
                    themes.Add(result.Value);
            }
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
        }

        return themes;
    }

    public IReadOnlyList<ThemeDefinition> RefreshRuntimeCatalog()
    {
        IReadOnlyList<ThemeDefinition> themes = LoadCatalog();
        CommunityThemeRuntimeCatalog.Replace(themes.Where(theme => !theme.BuiltIn));
        return themes;
    }

    public ThemeOperationResult<ThemeDefinition> LoadAndValidate(string manifestPath, string? rootPath = null)
    {
        try
        {
            string json = File.ReadAllText(manifestPath);
            ThemeDefinition? definition = JsonSerializer.Deserialize<ThemeDefinition>(json, JsonOptions);
            return definition is null
                ? ThemeOperationResult<ThemeDefinition>.Fail("Theme JSON produced no theme.")
                : ValidateAndNormalize(definition, rootPath ?? Path.GetDirectoryName(manifestPath));
        }
        catch (JsonException)
        {
            return ThemeOperationResult<ThemeDefinition>.Fail("Malformed theme JSON.");
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            return ThemeOperationResult<ThemeDefinition>.Fail($"Theme file unavailable: {exception.GetType().Name}.");
        }
    }

    public ThemeOperationResult<ThemeDefinition> ValidateAndNormalize(
        ThemeDefinition definition,
        string? rootPath = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.SchemaVersion != CommunityThemeContract.SchemaVersion)
            return ThemeOperationResult<ThemeDefinition>.Fail("Unsupported schemaVersion.");
        if (!CommunityThemeContract.IsValidId(definition.Id))
            return ThemeOperationResult<ThemeDefinition>.Fail("Theme id is missing or invalid.");
        if (IsBuiltInId(definition.Id))
            return ThemeOperationResult<ThemeDefinition>.Fail("Built-in theme IDs are protected.");
        if (string.IsNullOrWhiteSpace(definition.Name))
            return ThemeOperationResult<ThemeDefinition>.Fail("Theme name is required.");
        if (string.IsNullOrWhiteSpace(definition.Version))
            return ThemeOperationResult<ThemeDefinition>.Fail("Theme version is required.");
        if (definition.App is null && definition.Overlay is null)
            return ThemeOperationResult<ThemeDefinition>.Fail("Theme must contain app or overlay tokens.");

        ThemeOperationResult<Dictionary<string, string>?> app = NormalizeColors(
            definition.App,
            CommunityThemeContract.AppTokenMap.Keys);
        if (!app.Success)
            return ThemeOperationResult<ThemeDefinition>.Fail(app.Error!);
        ThemeOperationResult<Dictionary<string, string>?> overlay = NormalizeColors(
            definition.Overlay,
            CommunityThemeContract.OverlayTokens);
        if (!overlay.Success)
            return ThemeOperationResult<ThemeDefinition>.Fail(overlay.Error!);

        var assets = new Dictionary<string, string>(StringComparer.Ordinal);
        var warnings = new List<string>();
        foreach ((string key, string path) in definition.Assets ?? new Dictionary<string, string>())
        {
            if (!CommunityThemeContract.AssetKeys.Contains(key) ||
                !CommunityThemeContract.IsSafeAssetPath(path))
            {
                return ThemeOperationResult<ThemeDefinition>.Fail("Theme contains an invalid asset reference.");
            }

            string normalizedPath = path.Replace('\\', '/');
            assets[key] = normalizedPath;
            if (rootPath is not null && !File.Exists(Path.Combine(rootPath, normalizedPath)))
                warnings.Add($"Asset '{key}' is missing; fallback will be used.");
        }

        var normalized = definition with
        {
            Id = definition.Id.Trim(),
            Name = definition.Name.Trim(),
            Author = string.IsNullOrWhiteSpace(definition.Author) ? null : definition.Author.Trim(),
            Version = definition.Version.Trim(),
            App = app.Value,
            Overlay = overlay.Value,
            Assets = assets,
            BuiltIn = false,
            RootPath = rootPath is null ? null : Path.GetFullPath(rootPath)
        };
        return ThemeOperationResult<ThemeDefinition>.Ok(normalized, warnings.ToArray());
    }

    public ThemeOperationResult<ThemeDefinition> CreateFrom(
        string baseThemeId,
        string name,
        string? author = null)
    {
        string id = NextLocalId(name);
        AppTheme appBase = AppThemeCatalog.Get(baseThemeId);
        OverlayTheme overlayBase = OverlayThemeCatalog.Get(baseThemeId);
        var definition = new ThemeDefinition
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(name) ? "New Theme" : name.Trim(),
            Author = string.IsNullOrWhiteSpace(author) ? null : author.Trim(),
            App = CommunityThemeRuntimeCatalog.ToContractApp(appBase),
            Overlay = CommunityThemeRuntimeCatalog.ToContractOverlay(overlayBase)
        };
        return ThemeOperationResult<ThemeDefinition>.Ok(definition);
    }

    public ThemeOperationResult<ThemeDefinition> Duplicate(ThemeDefinition source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var duplicate = source with
        {
            Id = NextLocalId(source.Name + " Copy"),
            Name = source.Name + " Copy",
            BuiltIn = false,
            RootPath = null,
            App = source.App is null ? null : new(source.App, StringComparer.Ordinal),
            Overlay = source.Overlay is null ? null : new(source.Overlay, StringComparer.Ordinal),
            Assets = new(source.Assets, StringComparer.Ordinal)
        };
        if (source.RootPath is null || source.Assets.Count == 0)
            return Save(duplicate);

        string staging = CreateStagingDirectory();
        try
        {
            foreach (string relativePath in source.Assets.Values.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string sourceAsset = Path.GetFullPath(Path.Combine(source.RootPath, relativePath));
                if (!IsWithin(source.RootPath, sourceAsset) || !File.Exists(sourceAsset))
                    continue;
                string destinationAsset = Path.Combine(staging, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationAsset)!);
                File.Copy(sourceAsset, destinationAsset, overwrite: false);
            }
            File.WriteAllText(
                Path.Combine(staging, "theme.json"),
                JsonSerializer.Serialize(duplicate, JsonOptions) + Environment.NewLine);
            return ImportFromPreparedSource(staging, replace: false);
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            return ThemeOperationResult<ThemeDefinition>.Fail($"Theme duplicate failed: {exception.GetType().Name}.");
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    public ThemeOperationResult<ThemeDefinition> Save(ThemeDefinition definition)
    {
        ThemeOperationResult<ThemeDefinition> validated = ValidateAndNormalize(definition);
        if (!validated.Success || validated.Value is null)
            return validated;

        ThemeDefinition normalized = validated.Value;
        string destination = GetThemeDirectory(normalized.Id);
        string temporaryFile = Path.Combine(destination, "theme.json.tmp");
        try
        {
            Directory.CreateDirectory(destination);
            ThemeDefinition persisted = normalized with { RootPath = null, BuiltIn = false };
            File.WriteAllText(temporaryFile, JsonSerializer.Serialize(persisted, JsonOptions) + Environment.NewLine);
            File.Move(temporaryFile, Path.Combine(destination, "theme.json"), true);
            ThemeDefinition saved = persisted with { RootPath = destination };
            RefreshRuntimeCatalog();
            return ThemeOperationResult<ThemeDefinition>.Ok(saved, validated.Warnings.ToArray());
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            TryDeleteFile(temporaryFile);
            return ThemeOperationResult<ThemeDefinition>.Fail($"Theme save failed: {exception.GetType().Name}.");
        }
    }

    public ThemeOperationResult<ThemeDefinition> ImportFolder(string sourceDirectory, bool replace = false) =>
        ImportFromPreparedSource(sourceDirectory, replace);

    public ThemeOperationResult<ThemeDefinition> ImportZip(string zipPath, bool replace = false)
    {
        string staging = CreateStagingDirectory();
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            long totalBytes = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;
                if (!IsSafePackageEntry(entry.FullName))
                    return ThemeOperationResult<ThemeDefinition>.Fail("ZIP contains an unsafe path.");
                totalBytes += entry.Length;
                if (entry.Length > MaximumAssetBytes || totalBytes > MaximumPackageBytes)
                    return ThemeOperationResult<ThemeDefinition>.Fail("Theme package is too large.");

                string destination = Path.GetFullPath(Path.Combine(staging, entry.FullName));
                if (!IsWithin(staging, destination))
                    return ThemeOperationResult<ThemeDefinition>.Fail("ZIP path traversal was rejected.");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: false);
            }

            return ImportFromPreparedSource(staging, replace);
        }
        catch (InvalidDataException)
        {
            return ThemeOperationResult<ThemeDefinition>.Fail("Invalid theme ZIP.");
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            return ThemeOperationResult<ThemeDefinition>.Fail($"Theme import failed: {exception.GetType().Name}.");
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    public ThemeOperationResult<string> Export(ThemeDefinition definition, string zipPath)
    {
        if (definition.BuiltIn || definition.RootPath is null)
            return ThemeOperationResult<string>.Fail("Only installed user themes can be exported.");

        ThemeOperationResult<ThemeDefinition> validated = ValidateAndNormalize(definition, definition.RootPath);
        if (!validated.Success || validated.Value is null)
            return ThemeOperationResult<string>.Fail(validated.Error!);

        string temporaryZip = zipPath + ".tmp";
        try
        {
            string? outputDirectory = Path.GetDirectoryName(Path.GetFullPath(zipPath));
            if (!string.IsNullOrEmpty(outputDirectory))
                Directory.CreateDirectory(outputDirectory);
            TryDeleteFile(temporaryZip);
            using (ZipArchive archive = ZipFile.Open(temporaryZip, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(
                    Path.Combine(definition.RootPath, "theme.json"),
                    "theme.json",
                    CompressionLevel.Optimal);
                foreach (string relativePath in definition.Assets.Values.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    string asset = Path.GetFullPath(Path.Combine(definition.RootPath, relativePath));
                    if (IsWithin(definition.RootPath, asset) && File.Exists(asset))
                        archive.CreateEntryFromFile(asset, relativePath.Replace('\\', '/'), CompressionLevel.Optimal);
                }
            }
            File.Move(temporaryZip, zipPath, true);
            return ThemeOperationResult<string>.Ok(Path.GetFullPath(zipPath), validated.Warnings.ToArray());
        }
        catch (Exception exception) when (IsFileFailure(exception) || exception is InvalidDataException)
        {
            TryDeleteFile(temporaryZip);
            return ThemeOperationResult<string>.Fail($"Theme export failed: {exception.GetType().Name}.");
        }
    }

    public ThemeOperationResult<bool> Delete(ThemeDefinition definition)
    {
        if (definition.BuiltIn || IsBuiltInId(definition.Id))
            return ThemeOperationResult<bool>.Fail("Built-in themes cannot be deleted.");

        string directory;
        try
        {
            directory = GetThemeDirectory(definition.Id);
            if (!IsWithin(themesRoot, directory))
                return ThemeOperationResult<bool>.Fail("Theme path escaped the Themes root.");
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
            RefreshRuntimeCatalog();
            return ThemeOperationResult<bool>.Ok(true);
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            return ThemeOperationResult<bool>.Fail($"Theme delete failed: {exception.GetType().Name}.");
        }
    }

    public static bool HasLowContrast(ThemeDefinition definition)
    {
        string? background = definition.App?.GetValueOrDefault("AppBackground");
        string? foreground = definition.App?.GetValueOrDefault("AppTextPrimary");
        if (!CommunityThemeContract.TryNormalizeColor(background, out string bg) ||
            !CommunityThemeContract.TryNormalizeColor(foreground, out string fg))
        {
            return false;
        }

        return ContrastRatio(bg, fg) < 2.5;
    }

    private ThemeOperationResult<ThemeDefinition> ImportFromPreparedSource(string sourceDirectory, bool replace)
    {
        try
        {
            string source = Path.GetFullPath(sourceDirectory);
            string manifest = Path.Combine(source, "theme.json");
            ThemeOperationResult<ThemeDefinition> validation = LoadAndValidate(manifest, source);
            if (!validation.Success || validation.Value is null)
                return validation;
            ThemeDefinition definition = validation.Value;

            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(source, file).Replace('\\', '/');
                if (relative.Equals("theme.json", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!definition.Assets.Values.Contains(relative, StringComparer.OrdinalIgnoreCase))
                    return ThemeOperationResult<ThemeDefinition>.Fail("Package contains an unreferenced or unsupported file.");
                if (new FileInfo(file).Length > MaximumAssetBytes)
                    return ThemeOperationResult<ThemeDefinition>.Fail("Theme asset is too large.");
            }

            string destination = GetThemeDirectory(definition.Id);
            if (Directory.Exists(destination) && !replace)
                return ThemeOperationResult<ThemeDefinition>.Fail("Theme ID is already installed.");

            string staging = CreateStagingDirectory();
            string backup = destination + ".backup-" + Guid.NewGuid().ToString("N");
            bool installed = false;
            try
            {
                CopyDirectory(source, staging);
                if (Directory.Exists(destination))
                    Directory.Move(destination, backup);
                Directory.Move(staging, destination);
                installed = true;
                TryDeleteDirectory(backup);
            }
            catch
            {
                if (!Directory.Exists(destination) && Directory.Exists(backup))
                    Directory.Move(backup, destination);
                throw;
            }
            finally
            {
                TryDeleteDirectory(staging);
                if (installed)
                    TryDeleteDirectory(backup);
            }

            RefreshRuntimeCatalog();
            return ThemeOperationResult<ThemeDefinition>.Ok(
                definition with { RootPath = destination },
                validation.Warnings.ToArray());
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            return ThemeOperationResult<ThemeDefinition>.Fail($"Theme import failed: {exception.GetType().Name}.");
        }
    }

    private string NextLocalId(string name)
    {
        string slug = Regex.Replace(name.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (string.IsNullOrWhiteSpace(slug))
            slug = "theme";
        string candidate = "local." + slug;
        int suffix = 2;
        HashSet<string> ids = LoadCatalog().Select(theme => theme.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        while (ids.Contains(candidate))
            candidate = $"local.{slug}-{suffix++}";
        return candidate;
    }

    private string GetThemeDirectory(string id)
    {
        if (!CommunityThemeContract.IsValidId(id))
            throw new ArgumentException("Invalid theme id.", nameof(id));
        string directory = Path.GetFullPath(Path.Combine(themesRoot, id));
        if (!IsWithin(themesRoot, directory))
            throw new UnauthorizedAccessException("Theme path escaped its root.");
        return directory;
    }

    private string CreateStagingDirectory()
    {
        string parent = Path.Combine(themesRoot, ".staging");
        Directory.CreateDirectory(parent);
        string path = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static ThemeOperationResult<Dictionary<string, string>?> NormalizeColors(
        Dictionary<string, string>? values,
        IEnumerable<string> allowed)
    {
        if (values is null)
            return ThemeOperationResult<Dictionary<string, string>?>.Ok(null);
        HashSet<string> allowedSet = allowed.ToHashSet(StringComparer.Ordinal);
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string key, string value) in values)
        {
            if (!allowedSet.Contains(key))
                return ThemeOperationResult<Dictionary<string, string>?>.Fail($"Unknown color token: {key}.");
            if (!CommunityThemeContract.TryNormalizeColor(value, out string color))
                return ThemeOperationResult<Dictionary<string, string>?>.Fail($"Invalid color for {key}.");
            normalized[key] = color;
        }
        return ThemeOperationResult<Dictionary<string, string>?>.Ok(normalized);
    }

    private static ThemeDefinition BuiltIn(string id, string name) => new()
    {
        Id = id,
        Name = name,
        Version = "built-in",
        BuiltIn = true,
        App = CommunityThemeRuntimeCatalog.ToContractApp(AppThemeCatalog.Get(id)),
        Overlay = CommunityThemeRuntimeCatalog.ToContractOverlay(OverlayThemeCatalog.Get(id))
    };

    private static bool IsBuiltInId(string id) =>
        string.Equals(id, AppThemeIds.Default, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(id, AppThemeIds.Bia, StringComparison.OrdinalIgnoreCase);

    private static bool IsSafePackageEntry(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) ||
            relativePath.Replace('\\', '/').Split('/').Any(part => part is "." or ".."))
        {
            return false;
        }
        return relativePath.Equals("theme.json", StringComparison.OrdinalIgnoreCase) ||
            CommunityThemeContract.IsSafeAssetPath(relativePath);
    }

    private static bool IsWithin(string root, string candidate)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string fullCandidate = Path.GetFullPath(candidate);
        return fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }

    private static double ContrastRatio(string first, string second)
    {
        double l1 = Luminance(first);
        double l2 = Luminance(second);
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    private static double Luminance(string color)
    {
        byte r = Convert.ToByte(color.Substring(3, 2), 16);
        byte g = Convert.ToByte(color.Substring(5, 2), 16);
        byte b = Convert.ToByte(color.Substring(7, 2), 16);
        static double Channel(byte value)
        {
            double normalized = value / 255d;
            return normalized <= 0.03928 ? normalized / 12.92 : Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }

    private static bool IsFileFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception exception) when (IsFileFailure(exception)) { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception exception) when (IsFileFailure(exception)) { }
    }
}

public static class CommunityThemeRuntimeCatalog
{
    private static readonly object Sync = new();
    private static IReadOnlyDictionary<string, AppTheme> appThemes =
        new Dictionary<string, AppTheme>(StringComparer.Ordinal);
    private static IReadOnlyDictionary<string, OverlayTheme> overlayThemes =
        new Dictionary<string, OverlayTheme>(StringComparer.Ordinal);
    private static IReadOnlyDictionary<string, string> roots =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public static void Replace(IEnumerable<ThemeDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        AppTheme appFallback = AppThemeCatalog.GetBuiltIn(AppThemeIds.Default);
        OverlayTheme overlayFallback = OverlayThemeCatalog.GetBuiltIn(OverlayThemeIds.Default);
        var apps = new Dictionary<string, AppTheme>(StringComparer.Ordinal);
        var overlays = new Dictionary<string, OverlayTheme>(StringComparer.Ordinal);
        var newRoots = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ThemeDefinition definition in definitions)
        {
            if (definition.App is not null)
                apps[definition.Id] = CreateAppTheme(definition, appFallback);
            if (definition.Overlay is not null)
                overlays[definition.Id] = CreateOverlayTheme(definition, overlayFallback);
            if (definition.RootPath is not null)
                newRoots[definition.Id] = definition.RootPath;
        }
        lock (Sync)
        {
            appThemes = apps;
            overlayThemes = overlays;
            roots = newRoots;
        }
    }

    public static bool TryGetApp(string id, out AppTheme? theme)
    {
        lock (Sync) return appThemes.TryGetValue(id, out theme);
    }

    public static bool TryGetOverlay(string id, out OverlayTheme? theme)
    {
        lock (Sync) return overlayThemes.TryGetValue(id, out theme);
    }

    public static string? GetRoot(string id)
    {
        lock (Sync) return roots.GetValueOrDefault(id);
    }

    public static Dictionary<string, string> ToContractApp(AppTheme theme)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string external, string internalKey) in CommunityThemeContract.AppTokenMap)
        {
            if (theme.Colors.TryGetValue(internalKey, out string? color))
                result[external] = color;
        }
        return result;
    }

    public static Dictionary<string, string> ToContractOverlay(OverlayTheme theme) => new(StringComparer.Ordinal)
    {
        ["OverlayBackground"] = Token(theme.Tokens.Backgrounds, "window"),
        ["OverlaySurface"] = Token(theme.Tokens.Cards, "background"),
        ["OverlayBorder"] = Token(theme.Tokens.Borders, "default"),
        ["OverlayTextPrimary"] = Token(theme.Tokens.Text, "primary"),
        ["OverlayTextSecondary"] = Token(theme.Tokens.Text, "secondary"),
        ["OverlayAccent"] = Token(theme.Tokens.Colors, "accent.primary"),
        ["OverlayPositive"] = Token(theme.Tokens.Colors, "status.positive"),
        ["OverlayWarning"] = Token(theme.Tokens.Colors, "status.warning"),
        ["OverlayDanger"] = Token(theme.Tokens.Colors, "status.danger"),
        ["OverlayMuted"] = Token(theme.Tokens.Text, "secondary"),
        ["CurrentAccent"] = Token(theme.Tokens.Colors, "accent.primary"),
        ["NextAccent"] = Token(theme.Tokens.Colors, "accent.secondary"),
        ["LiveStatsAccent"] = Token(theme.Tokens.Colors, "accent.primary"),
        ["PerformanceAccent"] = Token(theme.Tokens.Colors, "accent.secondary"),
        ["EditBorder"] = Token(theme.Tokens.Borders, "default"),
        ["EditHandle"] = Token(theme.Tokens.Colors, "accent.primary")
    };

    private static AppTheme CreateAppTheme(ThemeDefinition definition, AppTheme fallback)
    {
        var colors = new Dictionary<string, string>(fallback.Colors, StringComparer.Ordinal);
        foreach ((string external, string value) in definition.App!)
        {
            if (CommunityThemeContract.AppTokenMap.TryGetValue(external, out string? internalKey))
                colors[internalKey] = value;
        }
        return new AppTheme(definition.Id, colors, new AppThemeIdentity(definition.Name.ToUpperInvariant(), false));
    }

    private static OverlayTheme CreateOverlayTheme(ThemeDefinition definition, OverlayTheme fallback)
    {
        var colors = new Dictionary<string, string>(fallback.Tokens.Colors, StringComparer.Ordinal);
        var text = new Dictionary<string, string>(fallback.Tokens.Text, StringComparer.Ordinal);
        var cards = new Dictionary<string, string>(fallback.Tokens.Cards, StringComparer.Ordinal);
        var borders = new Dictionary<string, string>(fallback.Tokens.Borders, StringComparer.Ordinal);
        var backgrounds = new Dictionary<string, string>(fallback.Tokens.Backgrounds, StringComparer.Ordinal);
        Dictionary<string, string> values = definition.Overlay!;
        Set(values, "OverlayBackground", backgrounds, "window");
        Set(values, "OverlaySurface", cards, "background");
        Set(values, "OverlaySurface", colors, "surface.primary");
        Set(values, "OverlayBorder", borders, "default");
        Set(values, "OverlayTextPrimary", text, "primary");
        Set(values, "OverlayTextPrimary", colors, "text.primary");
        Set(values, "OverlayTextSecondary", text, "secondary");
        Set(values, "OverlayMuted", text, "muted");
        Set(values, "OverlayAccent", colors, "accent.primary");
        Set(values, "OverlayPositive", colors, "status.positive");
        Set(values, "OverlayWarning", colors, "status.warning");
        Set(values, "OverlayDanger", colors, "status.danger");
        Set(values, "CurrentAccent", colors, "current.accent");
        Set(values, "NextAccent", colors, "next.accent");
        Set(values, "LiveStatsAccent", colors, "liveStats.accent");
        Set(values, "PerformanceAccent", colors, "accent.secondary");
        Set(values, "PerformanceAccent", colors, "performance.accent");
        Set(values, "EditBorder", borders, "edit");
        Set(values, "EditHandle", colors, "edit.handle");
        return new OverlayTheme(
            definition.Id,
            new OverlayThemeTokens
            {
                Colors = colors,
                Text = text,
                Cards = cards,
                Borders = borders,
                Bars = new Dictionary<string, string>(fallback.Tokens.Bars, StringComparer.Ordinal),
                Backgrounds = backgrounds,
                Overlays = new Dictionary<string, string>(fallback.Tokens.Overlays, StringComparer.Ordinal)
            },
            new Dictionary<string, string>(definition.Assets, StringComparer.Ordinal));
    }

    private static void Set(
        IReadOnlyDictionary<string, string> source,
        string sourceKey,
        IDictionary<string, string> target,
        string targetKey)
    {
        if (source.TryGetValue(sourceKey, out string? value))
            target[targetKey] = value;
    }

    private static string Token(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value) ? value : "#FFFFFFFF";
}
