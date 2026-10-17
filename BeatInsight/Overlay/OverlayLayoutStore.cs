using System.IO;
using System.Text.Json;

namespace BeatInsight.Overlay;

public sealed class OverlayLayoutStore
{
    private const int CurrentVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string DefaultPath { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BeatInsight",
        "overlay-layout.json");

    private readonly string path;

    public OverlayLayoutStore(string? path = null) =>
        this.path = string.IsNullOrWhiteSpace(path) ? DefaultPath : path;

    public string Path => path;

    public OverlayLayout Load(string presetId, OverlayLayout fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        try
        {
            if (!File.Exists(path))
                return fallback;

            Document? document = JsonSerializer.Deserialize<Document>(
                File.ReadAllText(path), JsonOptions);
            if (document?.Version != CurrentVersion ||
                document.Presets is null ||
                !document.Presets.TryGetValue(presetId, out OverlayLayout? layout) ||
                layout is null ||
                !OverlayLayoutResolver.TryValidate(layout, out _))
            {
                return fallback;
            }

            return layout with { Id = presetId };
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or JsonException or NotSupportedException or
            ArgumentException)
        {
            return fallback;
        }
    }

    public bool Save(string presetId, OverlayLayout layout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presetId);
        ArgumentNullException.ThrowIfNull(layout);
        if (!OverlayLayoutResolver.TryValidate(layout, out _))
            return false;

        string? directory = System.IO.Path.GetDirectoryName(path);
        string temporaryPath = path + ".tmp";
        try
        {
            Dictionary<string, OverlayLayout> presets = ReadPresets();
            presets[presetId] = layout with { Id = presetId };
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(new Document(CurrentVersion, presets), JsonOptions) +
                Environment.NewLine);
            File.Move(temporaryPath, path, true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or JsonException or NotSupportedException or
            ArgumentException)
        {
            TryDelete(temporaryPath);
            return false;
        }
    }

    private Dictionary<string, OverlayLayout> ReadPresets()
    {
        if (!File.Exists(path))
            return new(StringComparer.Ordinal);

        try
        {
            Document? document = JsonSerializer.Deserialize<Document>(
                File.ReadAllText(path), JsonOptions);
            return document?.Version == CurrentVersion && document.Presets is not null
                ? new(document.Presets, StringComparer.Ordinal)
                : new(StringComparer.Ordinal);
        }
        catch
        {
            return new(StringComparer.Ordinal);
        }
    }

    private static void TryDelete(string temporaryPath)
    {
        try
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
        catch
        {
        }
    }

    private sealed record Document(
        int Version,
        Dictionary<string, OverlayLayout> Presets);
}
