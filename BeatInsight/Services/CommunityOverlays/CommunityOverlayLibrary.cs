using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BeatInsight.Integration.CommunityOverlays.Managed;

namespace BeatInsight.Services.CommunityOverlays;

/// <summary>
/// FEAT-003 : overlay communautaire importé. Seule la configuration BeatInsight est conservée :
/// le dossier de l'overlay n'est jamais modifié ni supprimé.
/// </summary>
/// <param name="SettingsOverrides">
/// Réservé à une future persistance des réglages utilisateur de l'overlay (valeurs de <c>settings.json</c>),
/// stockée ici et jamais dans le dossier de l'overlay. Conservé tel quel, non appliqué pour l'instant :
/// le compagnon sert les valeurs par défaut de <c>settings.json</c>.
/// </param>
/// <param name="Scale">FEAT-003.1 : échelle du contenu en pourcentage (zoom WebView2), 25 à 200 au pas de 5.</param>
public sealed record CommunityOverlayEntry(
    string Id,
    string Name,
    string FolderPath,
    bool Enabled,
    CompanionBounds? Bounds = null,
    JsonObject? SettingsOverrides = null,
    int Scale = CommunityOverlayCompanionProtocol.DefaultScalePercent)
{
    public const string EntryDocument = "index.html";

    /// <summary>Identifiant stable dérivé du chemin complet (insensible à la casse), comme le profil WebView2 du compagnon.</summary>
    public static string IdFor(string folderPath)
    {
        string key = NormalizeFolder(folderPath).ToUpperInvariant();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return "overlay_" + Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    public static string NormalizeFolder(string folderPath) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));
}

public sealed class CommunityOverlayLibraryStore
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string DefaultPath { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BeatInsight",
        "community-overlays-v1.json");

    private readonly string path;

    public CommunityOverlayLibraryStore(string? path = null)
    {
        this.path = string.IsNullOrWhiteSpace(path) ? DefaultPath : path;
    }

    public string Path => path;

    /// <summary>Entrées valides, sans doublon. Fichier absent ou illisible : liste vide.</summary>
    public IReadOnlyList<CommunityOverlayEntry> Load()
    {
        try
        {
            if (!File.Exists(path))
                return [];

            LibraryDocument? document = JsonSerializer.Deserialize<LibraryDocument>(File.ReadAllText(path), JsonOptions);
            if (document?.Version != CurrentVersion || document.Overlays is null)
                return [];

            var entries = new List<CommunityOverlayEntry>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (OverlayDocument? overlay in document.Overlays)
            {
                if (overlay?.FolderPath is not { Length: > 0 } folder || !System.IO.Path.IsPathFullyQualified(folder))
                    continue;

                string normalized;
                try
                {
                    normalized = CommunityOverlayEntry.NormalizeFolder(folder);
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    continue;
                }

                string id = CommunityOverlayEntry.IdFor(normalized);
                if (!seen.Add(id))
                    continue;

                CompanionBounds? bounds = overlay.Bounds is { } b
                    && CommunityOverlayCompanionProtocol.IsValid(new CompanionBounds(b.X, b.Y, b.Width, b.Height))
                        ? new CompanionBounds(b.X, b.Y, b.Width, b.Height)
                        : null;
                string name = string.IsNullOrWhiteSpace(overlay.Name)
                    ? System.IO.Path.GetFileName(normalized)
                    : overlay.Name.Trim();
                entries.Add(new CommunityOverlayEntry(id, name, normalized, overlay.Enabled ?? false, bounds, overlay.SettingsOverrides,
                    ReadScale(overlay.Scale)));
            }

            return entries;
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
        {
            return [];
        }
    }

    public bool Save(IReadOnlyList<CommunityOverlayEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        string? directory = System.IO.Path.GetDirectoryName(path);
        string temporaryPath = path + ".tmp";
        try
        {
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var document = new LibraryDocument
            {
                Version = CurrentVersion,
                Overlays = entries.Select(entry => (OverlayDocument?)new OverlayDocument
                {
                    Name = entry.Name,
                    FolderPath = entry.FolderPath,
                    Enabled = entry.Enabled,
                    Bounds = entry.Bounds is { } b ? new BoundsDocument { X = b.X, Y = b.Y, Width = b.Width, Height = b.Height } : null,
                    SettingsOverrides = entry.SettingsOverrides,
                    Scale = CommunityOverlayCompanionProtocol.NormalizeScale(entry.Scale),
                }).ToList(),
            };
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, JsonOptions) + Environment.NewLine);
            File.Move(temporaryPath, path, true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }
            return false;
        }
    }

    private sealed class LibraryDocument
    {
        public int? Version { get; init; }
        public List<OverlayDocument?>? Overlays { get; init; }
    }

    private sealed class OverlayDocument
    {
        public string? Name { get; init; }
        public string? FolderPath { get; init; }
        public bool? Enabled { get; init; }
        public BoundsDocument? Bounds { get; init; }
        public JsonObject? SettingsOverrides { get; init; }
        /// <summary>Lu comme nœud brut : une valeur invalide revient à 100 % sans invalider tout le fichier.</summary>
        public JsonNode? Scale { get; init; }
    }

    /// <summary>Absent (configuration FEAT-003) ou non numérique : 100 %. Hors plage : ramené dans [25, 200] au pas de 5.</summary>
    private static int ReadScale(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out double percent) && double.IsFinite(percent)
            ? CommunityOverlayCompanionProtocol.NormalizeScale(percent)
            : CommunityOverlayCompanionProtocol.DefaultScalePercent;

    private sealed class BoundsDocument
    {
        public double X { get; init; }
        public double Y { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }
    }
}

public sealed record CommunityOverlayImportResult(bool Success, string Message, CommunityOverlayEntry? Entry = null);

/// <summary>Validation d'un dossier importé. Lecture seule : aucun fichier n'est créé, modifié ou copié.</summary>
public static class CommunityOverlayImportValidator
{
    public static CommunityOverlayImportResult Validate(string? folderPath, IEnumerable<CommunityOverlayEntry> existing)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return Fail("No folder selected.");

        string normalized;
        try
        {
            if (!Path.IsPathFullyQualified(folderPath))
                return Fail("The folder path must be absolute.");
            normalized = CommunityOverlayEntry.NormalizeFolder(folderPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Fail("Invalid folder path.");
        }

        if (!Directory.Exists(normalized))
            return Fail($"Folder not found: {normalized}");

        string entryPath = Path.Combine(normalized, CommunityOverlayEntry.EntryDocument);
        if (!File.Exists(entryPath))
            return Fail($"Invalid overlay folder: no {CommunityOverlayEntry.EntryDocument} at its root. Select the folder that contains the overlay's {CommunityOverlayEntry.EntryDocument}.");

        string id = CommunityOverlayEntry.IdFor(normalized);
        CommunityOverlayEntry? duplicate = existing.FirstOrDefault(entry => entry.Id == id);
        if (duplicate is not null)
            return Fail($"This overlay is already imported: {duplicate.Name}.");

        string name = Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(name))
            name = normalized;
        return new CommunityOverlayImportResult(true, $"Overlay imported: {name}.",
            new CommunityOverlayEntry(id, name, normalized, Enabled: false));
    }

    private static CommunityOverlayImportResult Fail(string message) => new(false, message);
}
