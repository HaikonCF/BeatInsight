using System.Windows;
using System.Windows.Media;

namespace BeatInsight.Theming;

public static class AppThemeIds
{
    public const string Default = "BeatInsight.Default";
    public const string Bia = "BeatInsight.Bia";
}

public sealed record AppThemeIdentity(string Label, bool UsesCharacterAsset);

public sealed record AppTheme(
    string Id,
    IReadOnlyDictionary<string, string> Colors,
    AppThemeIdentity Identity);

public static class AppThemeCatalog
{
    private static readonly IReadOnlyDictionary<string, AppTheme> Themes = new Dictionary<string, AppTheme>(StringComparer.Ordinal)
    {
        [AppThemeIds.Default] = new(
            AppThemeIds.Default,
            new Dictionary<string, string>
            {
                ["background"] = "#FF0B0C10", ["surface"] = "#FF171820", ["surface.alt"] = "#FF20222C",
                ["card"] = "#EE161722", ["border"] = "#FF272836", ["border.accent"] = "#66FF2E93",
                ["text.primary"] = "#FFFFFFFF", ["text.secondary"] = "#FFA0A5B5", ["text.muted"] = "#FF7B8192",
                ["accent.primary"] = "#FFFF2E93", ["accent.soft"] = "#FF2A1320",
                ["danger"] = "#FFEF4444", ["success"] = "#FF10B981"
            },
            new AppThemeIdentity("BEATINSIGHT", false)),
        [AppThemeIds.Bia] = new(
            AppThemeIds.Bia,
            new Dictionary<string, string>
            {
                ["background"] = "#FF0E0C18", ["surface"] = "#FF171527", ["surface.alt"] = "#FF242038",
                ["card"] = "#EE19162B", ["border"] = "#FF39334B", ["border.accent"] = "#77E58AD7",
                ["text.primary"] = "#FFF8F5FF", ["text.secondary"] = "#FFC8B7DD", ["text.muted"] = "#FF9385A8",
                ["accent.primary"] = "#FFE58AD7", ["accent.soft"] = "#FF302038",
                ["danger"] = "#FFFF7F9F", ["success"] = "#FF78DCC8"
            },
            new AppThemeIdentity("BIA-CHAN", true))
    };

    public static IReadOnlyCollection<string> Ids => Themes.Keys.ToArray();

    public static string Normalize(string? id) =>
        id is not null && (Themes.ContainsKey(id) || CommunityThemeRuntimeCatalog.TryGetApp(id, out _))
            ? id
            : AppThemeIds.Default;

    public static AppTheme Get(string? id)
    {
        string normalized = Normalize(id);
        return Themes.TryGetValue(normalized, out AppTheme? builtIn)
            ? builtIn
            : CommunityThemeRuntimeCatalog.TryGetApp(normalized, out AppTheme? custom) && custom is not null
                ? custom
                : Themes[AppThemeIds.Default];
    }

    internal static AppTheme GetBuiltIn(string id) => Themes[id];
}

public sealed class AppThemeManager
{
    private static readonly IReadOnlyDictionary<string, string[]> ResourceAliases = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["background"] = ["AppBackgroundBrush"],
        ["surface"] = ["AppSurfaceBrush"],
        ["surface.alt"] = ["AppSurfaceAltBrush"],
        ["card"] = ["AppCardBrush", "CardBackgroundTranslucentBrush"],
        ["border"] = ["AppBorderBrush", "BorderSubtleBrush"],
        ["border.accent"] = ["AppBorderAccentBrush", "BorderAccentBrush"],
        ["text.primary"] = ["AppTextPrimaryBrush", "TextPrimaryBrush"],
        ["text.secondary"] = ["AppTextSecondaryBrush", "TextSecondaryBrush"],
        ["text.muted"] = ["AppTextMutedBrush", "TextMutedBrush"],
        ["accent.primary"] = ["AppAccentBrush", "AccentMagentaBrush"],
        ["accent.soft"] = ["AppAccentSoftBrush", "AccentSoftBrush"],
        ["danger"] = ["AppDangerBrush", "StatusErrorBrush"],
        ["success"] = ["AppSuccessBrush"]
    };

    public string CurrentThemeId { get; private set; } = AppThemeIds.Default;

    public string Apply(ResourceDictionary resources, string? themeId)
    {
        ArgumentNullException.ThrowIfNull(resources);
        AppTheme theme = AppThemeCatalog.Get(themeId);
        AppTheme fallback = AppThemeCatalog.Get(AppThemeIds.Default);
        foreach ((string token, string[] resourceKeys) in ResourceAliases)
        {
            string value = theme.Colors.TryGetValue(token, out string? selected) ? selected : fallback.Colors[token];
            Color color = (Color)ColorConverter.ConvertFromString(value);
            foreach (string resourceKey in resourceKeys)
            {
                if (resources[resourceKey] is SolidColorBrush existing && !existing.IsFrozen)
                    existing.Color = color;
                else
                    resources[resourceKey] = new SolidColorBrush(color);
            }
        }
        resources["AppThemeIdentityLabel"] = theme.Identity.Label;
        resources["AppThemeCharacterVisibility"] = theme.Identity.UsesCharacterAsset
            ? Visibility.Visible
            : Visibility.Collapsed;
        resources["AppThemeNeutralIdentityVisibility"] = theme.Identity.UsesCharacterAsset
            ? Visibility.Collapsed
            : Visibility.Visible;
        CurrentThemeId = theme.Id;
        return theme.Id;
    }
}
