using System.IO;
using System.Text.Json;

namespace BeatInsight.Services.Settings;

public sealed record WindowLifecycleSettings(
    int Version,
    bool CloseToTray,
    bool StartMinimized = false)
{
    public const int CurrentVersion = 1;

    public static WindowLifecycleSettings Default { get; } = new(CurrentVersion, true, false);
}

public sealed class WindowLifecycleSettingsStore
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
        "window-lifecycle-settings-v1.json");

    private readonly string path;

    public WindowLifecycleSettingsStore(string? path = null)
    {
        this.path = string.IsNullOrWhiteSpace(path) ? DefaultPath : path;
    }

    public string Path => path;

    public WindowLifecycleSettings Load()
    {
        try
        {
            if (!File.Exists(path))
                return WindowLifecycleSettings.Default;

            WindowLifecycleSettingsDocument? document = JsonSerializer.Deserialize<WindowLifecycleSettingsDocument>(
                File.ReadAllText(path),
                JsonOptions);
            return document?.Version == WindowLifecycleSettings.CurrentVersion && document.CloseToTray.HasValue
                ? new WindowLifecycleSettings(
                    WindowLifecycleSettings.CurrentVersion,
                    document.CloseToTray.Value,
                    document.StartMinimized ?? false)
                : WindowLifecycleSettings.Default;
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
        {
            return WindowLifecycleSettings.Default;
        }
    }

    public bool Save(WindowLifecycleSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? directory = System.IO.Path.GetDirectoryName(path);
        string temporaryPath = path + ".tmp";
        try
        {
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var normalized = settings with { Version = WindowLifecycleSettings.CurrentVersion };
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(normalized, JsonOptions) + Environment.NewLine);
            File.Move(temporaryPath, path, true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            TryDelete(temporaryPath);
            return false;
        }
    }

    private static void TryDelete(string temporaryPath)
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

    private sealed class WindowLifecycleSettingsDocument
    {
        public int? Version { get; init; }
        public bool? CloseToTray { get; init; }
        public bool? StartMinimized { get; init; }
    }
}
