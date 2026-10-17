using System.IO;
using System.Text.Json;
using BeatInsight.Overlay.GameplayHud;
using BeatInsight.Theming;

namespace BeatInsight.Overlay;

public sealed record OverlaySettings(
    int Version,
    string PresetId,
    GameplayHudDisplayOptions GameplayHud,
    string AppThemeId = AppThemeIds.Default,
    string OverlayThemeId = OverlayThemeIds.Default)
{
    public const int CurrentVersion = 2;

    public static OverlaySettings ForPreset(string presetId)
    {
        GameplayHudDisplayOptions options = presetId switch
        {
            OverlayLayoutIds.Analysis => new(true, true, true, true, true, true),
            OverlayLayoutIds.Timeline => new(true, true, true, false, true, true),
            _ => new(true, true, false, false, true, true)
        };

        return new(
            CurrentVersion,
            NormalizePresetId(presetId),
            options,
            AppThemeIds.Default,
            OverlayThemeIds.Default);
    }

    public static string NormalizePresetId(string? presetId) =>
        presetId is not null && OverlayLayoutCatalog.ExposedIds.Contains(
            presetId,
            StringComparer.Ordinal)
                ? presetId
                : OverlayLayoutIds.Minimal;

    public static string NormalizeAppThemeId(string? themeId) => AppThemeCatalog.Normalize(themeId);

    public static string NormalizeOverlayThemeId(string? themeId) => OverlayThemeCatalog.Normalize(themeId);
}

public sealed class OverlaySettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string DefaultPath { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BeatInsight",
        "overlay-settings-v1.json");

    private readonly string path;

    public OverlaySettingsStore(string? path = null)
    {
        this.path = string.IsNullOrWhiteSpace(path) ? DefaultPath : path;
    }

    public string Path => path;

    public OverlaySettings Load()
    {
        OverlaySettings fallback = OverlaySettings.ForPreset(OverlayLayoutIds.Minimal);
        try
        {
            if (!File.Exists(path))
                return fallback;

            OverlaySettingsDocument? document = JsonSerializer.Deserialize<OverlaySettingsDocument>(
                File.ReadAllText(path),
                JsonOptions);
            if (document?.Version is not (1 or OverlaySettings.CurrentVersion) ||
                document.PresetId is null ||
                !OverlayLayoutCatalog.ExposedIds.Contains(
                    document.PresetId,
                    StringComparer.Ordinal))
            {
                return fallback;
            }

            OverlaySettings preset = OverlaySettings.ForPreset(document.PresetId);
            GameplayHudSettingsDocument? hud = document.GameplayHud;
            return preset with
            {
                GameplayHud = new GameplayHudDisplayOptions(
                    hud?.ShowCurrentSection ?? preset.GameplayHud.ShowCurrentSection,
                    hud?.ShowUpNext ?? preset.GameplayHud.ShowUpNext,
                    hud?.ShowDifficultyTimeline ?? preset.GameplayHud.ShowDifficultyTimeline,
                    hud?.ShowLivePatterns ?? preset.GameplayHud.ShowLivePatterns,
                    hud?.ShowLiveStats ?? preset.GameplayHud.ShowLiveStats,
                    hud?.ShowPerformance ?? preset.GameplayHud.ShowPerformance),
                AppThemeId = OverlaySettings.NormalizeAppThemeId(document.AppThemeId),
                OverlayThemeId = OverlaySettings.NormalizeOverlayThemeId(document.OverlayThemeId)
            };
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or JsonException or NotSupportedException or
            ArgumentException)
        {
            return fallback;
        }
    }

    public bool Save(OverlaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? directory = System.IO.Path.GetDirectoryName(path);
        string temporaryPath = path + ".tmp";
        try
        {
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            OverlaySettings normalized = settings with
            {
                Version = OverlaySettings.CurrentVersion,
                PresetId = OverlaySettings.NormalizePresetId(settings.PresetId),
                AppThemeId = OverlaySettings.NormalizeAppThemeId(settings.AppThemeId),
                OverlayThemeId = OverlaySettings.NormalizeOverlayThemeId(settings.OverlayThemeId)
            };
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(normalized, JsonOptions) + Environment.NewLine);
            File.Move(temporaryPath, path, true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            TryDeleteTemporary(temporaryPath);
            return false;
        }
    }

    private static void TryDeleteTemporary(string temporaryPath)
    {
        try
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or ArgumentException)
        {
        }
    }

    private sealed class OverlaySettingsDocument
    {
        public int? Version { get; init; }
        public string? PresetId { get; init; }
        public GameplayHudSettingsDocument? GameplayHud { get; init; }
        public string? AppThemeId { get; init; }
        public string? OverlayThemeId { get; init; }
    }

    private sealed class GameplayHudSettingsDocument
    {
        public bool? ShowCurrentSection { get; init; }
        public bool? ShowUpNext { get; init; }
        public bool? ShowDifficultyTimeline { get; init; }
        public bool? ShowLivePatterns { get; init; }
        public bool? ShowLiveStats { get; init; }
        public bool? ShowPerformance { get; init; }
    }
}
