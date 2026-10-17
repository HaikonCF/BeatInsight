using BeatInsight.Theming;
using System.IO;
using System.Text.Json;

namespace BeatInsight.Overlay;

public static class OverlayThemeIds
{
    public const string Default = "BeatInsight.Default";
    public const string Bia = "BeatInsight.Bia";
}

public sealed record OverlayThemeTokens
{
    public IReadOnlyDictionary<string, string> Colors { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Text { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Cards { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Borders { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Bars { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Backgrounds { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Overlays { get; init; } = new Dictionary<string, string>();
}

public sealed record OverlayTheme(
    string Id,
    OverlayThemeTokens Tokens,
    IReadOnlyDictionary<string, string> Assets);

/// <summary>
/// The application theme is deliberately retained as a separate selection. The
/// overlay engine only resolves OverlayThemeId.
/// </summary>
public sealed record ThemeSelection(string AppThemeId, string OverlayThemeId);

public static class OverlayThemeCatalog
{
    private static readonly IReadOnlyDictionary<string, OverlayTheme> themes =
        new Dictionary<string, OverlayTheme>(StringComparer.Ordinal)
        {
            [OverlayThemeIds.Default] = new(
                OverlayThemeIds.Default,
                new OverlayThemeTokens
                {
                    Colors = new Dictionary<string, string>
                    {
                        ["accent.primary"] = "#31D7FF",
                        ["accent.secondary"] = "#8D67FF",
                        ["status.danger"] = "#F87171",
                        ["status.warning"] = "#FACC15",
                        ["status.positive"] = "#34D399",
                        ["text.primary"] = "#F5F8FF",
                        ["surface.primary"] = "#D9141724"
                    },
                    Text = new Dictionary<string, string>
                    {
                        ["primary"] = "#F5F8FF",
                        ["secondary"] = "#AEB7CC"
                    },
                    Cards = new Dictionary<string, string>
                    {
                        ["background"] = "#E8161724"
                    },
                    Borders = new Dictionary<string, string>
                    {
                        ["default"] = "#6631D7FF"
                    },
                    Backgrounds = new Dictionary<string, string>
                    {
                        ["window"] = "#B80B0C10"
                    }
                },
                new Dictionary<string, string>()),
            [OverlayThemeIds.Bia] = new(
                OverlayThemeIds.Bia,
                new OverlayThemeTokens
                {
                    Colors = new Dictionary<string, string>
                    {
                        ["accent.primary"] = "#E58AD7",
                        ["accent.secondary"] = "#7DBBFF",
                        ["status.danger"] = "#FF7F9F",
                        ["status.warning"] = "#F4C77A",
                        ["status.positive"] = "#78DCC8",
                        ["text.primary"] = "#F8F5FF",
                        ["surface.primary"] = "#DE171527"
                    },
                    Text = new Dictionary<string, string>
                    {
                        ["primary"] = "#F8F5FF",
                        ["secondary"] = "#C8B7DD"
                    },
                    Cards = new Dictionary<string, string>
                    {
                        ["background"] = "#EA171527"
                    },
                    Borders = new Dictionary<string, string>
                    {
                        ["default"] = "#77B77BFF"
                    },
                    Backgrounds = new Dictionary<string, string>
                    {
                        ["window"] = "#B80E0C18"
                    }
                },
                new Dictionary<string, string>
                {
                    ["mascot.idle"] = "Assets/BiaChan/BiaChanIdle.png"
                })
        };

    public static IReadOnlyCollection<string> Ids => themes.Keys.ToArray();

    public static string Normalize(string? id) =>
        id is not null && (themes.ContainsKey(id) || CommunityThemeRuntimeCatalog.TryGetOverlay(id, out _))
            ? id
            : OverlayThemeIds.Default;

    public static OverlayTheme Get(string? id)
    {
        string normalized = Normalize(id);
        return themes.TryGetValue(normalized, out OverlayTheme? builtIn)
            ? builtIn
            : CommunityThemeRuntimeCatalog.TryGetOverlay(normalized, out OverlayTheme? custom) && custom is not null
                ? custom
                : themes[OverlayThemeIds.Default];
    }

    public static bool TryGet(string id, out OverlayTheme? theme) =>
        themes.TryGetValue(id, out theme) || CommunityThemeRuntimeCatalog.TryGetOverlay(id, out theme);

    internal static OverlayTheme GetBuiltIn(string id) => themes[id];
}

public static class OverlayThemeResolver
{
    private static readonly JsonSerializerOptions options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static OverlayLoadResult<OverlayTheme> ResolveJson(
        string? json,
        OverlayTheme? fallback = null)
    {
        fallback ??= OverlayThemeCatalog.Get(OverlayThemeIds.Default);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new(fallback, true, "Theme JSON was empty.");
        }

        try
        {
            OverlayTheme? theme = JsonSerializer.Deserialize<OverlayTheme>(json, options);
            if (theme is null)
            {
                return new(fallback, true, "Theme JSON produced no theme.");
            }

            if (!TryValidate(theme, out string? diagnostic))
            {
                return new(fallback, true, diagnostic);
            }

            return new(theme, false, null);
        }
        catch (JsonException exception)
        {
            return new(fallback, true, $"Invalid theme JSON: {exception.GetType().Name}.");
        }
        catch (NotSupportedException exception)
        {
            return new(fallback, true, $"Unsupported theme JSON: {exception.GetType().Name}.");
        }
    }

    public static OverlayLoadResult<OverlayTheme> ResolveFile(
        string path,
        OverlayTheme? fallback = null)
    {
        try
        {
            return ResolveJson(File.ReadAllText(path), fallback);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            fallback ??= OverlayThemeCatalog.Get(OverlayThemeIds.Default);
            return new(fallback, true, $"Theme file unavailable: {exception.GetType().Name}.");
        }
    }

    public static bool TryValidate(OverlayTheme theme, out string? diagnostic)
    {
        if (string.IsNullOrWhiteSpace(theme.Id) || theme.Tokens is null || theme.Assets is null)
        {
            diagnostic = "Theme ID, tokens, and assets are required.";
            return false;
        }

        if (theme.Tokens.Colors is null ||
            theme.Tokens.Text is null ||
            theme.Tokens.Cards is null ||
            theme.Tokens.Borders is null ||
            theme.Tokens.Bars is null ||
            theme.Tokens.Backgrounds is null ||
            theme.Tokens.Overlays is null)
        {
            diagnostic = "Theme token collections cannot be null.";
            return false;
        }

        foreach ((string key, string relativePath) in theme.Assets)
        {
            if (string.IsNullOrWhiteSpace(key) || !IsSafeRelativePath(relativePath))
            {
                diagnostic = "Theme contains an invalid asset reference.";
                return false;
            }
        }

        diagnostic = null;
        return true;
    }

    internal static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            return false;
        }

        return !path.Replace('\\', '/').Split('/').Any(part => part == "..");
    }
}

public static class OverlayThemeAssetResolver
{
    public static string? Resolve(
        string assetKey,
        OverlayTheme selectedTheme,
        string selectedThemeRoot,
        OverlayTheme defaultTheme,
        string defaultThemeRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetKey);
        ArgumentNullException.ThrowIfNull(selectedTheme);
        ArgumentNullException.ThrowIfNull(defaultTheme);

        return TryResolve(selectedTheme, selectedThemeRoot, assetKey)
            ?? TryResolve(defaultTheme, defaultThemeRoot, assetKey);
    }

    private static string? TryResolve(OverlayTheme theme, string root, string key)
    {
        if (!theme.Assets.TryGetValue(key, out string? relativePath) ||
            !OverlayThemeResolver.IsSafeRelativePath(relativePath))
        {
            return null;
        }

        try
        {
            string fullRoot = Path.GetFullPath(root);
            string candidate = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
            string rootPrefix = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate)
                ? candidate
                : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
