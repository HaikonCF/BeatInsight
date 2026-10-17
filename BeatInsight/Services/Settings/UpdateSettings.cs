using System.IO;
using System.Text.Json;

namespace BeatInsight.Services.Settings;

public sealed record UpdateSettings(int Version, bool CheckAutomatically)
{
    public const int CurrentVersion = 1;

    public static UpdateSettings Default { get; } = new(CurrentVersion, true);
}

public sealed class UpdateSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BeatInsight",
        "update-settings-v1.json");

    private readonly string path;

    public UpdateSettingsStore(string? path = null)
    {
        this.path = string.IsNullOrWhiteSpace(path) ? DefaultPath : path;
    }

    public UpdateSettings Load()
    {
        try
        {
            if (!File.Exists(path))
                return UpdateSettings.Default;

            UpdateSettingsDocument? document = JsonSerializer.Deserialize<UpdateSettingsDocument>(
                File.ReadAllText(path),
                JsonOptions);
            return document?.Version == UpdateSettings.CurrentVersion
                && document.CheckAutomatically.HasValue
                    ? new UpdateSettings(
                        UpdateSettings.CurrentVersion,
                        document.CheckAutomatically.Value)
                    : UpdateSettings.Default;
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
        {
            return UpdateSettings.Default;
        }
    }

    public bool Save(UpdateSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? directory = Path.GetDirectoryName(path);
        string temporaryPath = path + ".tmp";
        try
        {
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(
                    settings with { Version = UpdateSettings.CurrentVersion },
                    JsonOptions) + Environment.NewLine);
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

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or ArgumentException)
        {
        }
    }

    private sealed class UpdateSettingsDocument
    {
        public int? Version { get; init; }
        public bool? CheckAutomatically { get; init; }
    }
}
