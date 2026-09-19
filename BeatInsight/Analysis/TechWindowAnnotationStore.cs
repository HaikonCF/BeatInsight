using System.Text;
using System.Text.Json;
using System.IO;

namespace BeatInsight.Analysis;

/// <summary>
/// Human-maintained, file-backed annotation for one beatmap. Explicit positive
/// and negative regions are retained independently; areas outside them remain
/// Unknown unless <see cref="AnnotationComplete"/> is true.
/// </summary>
internal sealed class TechWindowAnnotationDocument
{
    public string StableBeatmapId { get; set; } = string.Empty;
    public string MapName { get; set; } = string.Empty;
    public string Difficulty { get; set; } = string.Empty;
    public bool AnnotationComplete { get; set; }
    public List<TechWindowAnnotationRegionDocument> TechRegions { get; set; } = [];
    public List<TechWindowAnnotationRegionDocument> NotTechRegions { get; set; } = [];
}

internal sealed class TechWindowAnnotationRegionDocument
{
    public double StartTime { get; set; }
    public double EndTime { get; set; }
}

internal enum TechWindowAnnotationIssueKind
{
    IncompleteAnnotation,
    CompleteAnnotationWithoutTechRegions,
    InvalidDocument,
    InvalidRegion,
    OverlappingRegions,
    DuplicateBeatmapAnnotation,
    UnresolvedBeatmap
}

internal sealed record TechWindowAnnotationIssue(
    TechWindowAnnotationIssueKind Kind,
    string SourcePath,
    string? StableBeatmapId,
    string Message);

internal sealed record TechWindowAnnotationAuditReport(
    IReadOnlyList<TechWindowAnnotationIssue> Issues)
{
    public IReadOnlyList<TechWindowAnnotationIssue> IncompleteAnnotations =>
        Issues.Where(issue => issue.Kind == TechWindowAnnotationIssueKind.IncompleteAnnotation).ToList();

    public IReadOnlyList<TechWindowAnnotationIssue> CompleteAnnotationsWithoutTechRegions =>
        Issues.Where(issue => issue.Kind == TechWindowAnnotationIssueKind.CompleteAnnotationWithoutTechRegions).ToList();

    public IReadOnlyList<TechWindowAnnotationIssue> InvalidRegions =>
        Issues.Where(issue => issue.Kind == TechWindowAnnotationIssueKind.InvalidRegion).ToList();

    public IReadOnlyList<TechWindowAnnotationIssue> OverlappingRegions =>
        Issues.Where(issue => issue.Kind == TechWindowAnnotationIssueKind.OverlappingRegions).ToList();

    public IReadOnlyList<TechWindowAnnotationIssue> DuplicateBeatmapAnnotations =>
        Issues.Where(issue => issue.Kind == TechWindowAnnotationIssueKind.DuplicateBeatmapAnnotation).ToList();
}

internal sealed record TechWindowAnnotationLoadResult(
    IReadOnlyList<TechWindowAnnotationDocument> Annotations,
    TechWindowAnnotationAuditReport AuditReport);

/// <summary>
/// Loads and writes manually editable Tech-region annotations. It deliberately
/// has no dependency on the experimental likelihood or on any runtime score.
/// </summary>
internal sealed class TechWindowAnnotationStore
{
    public static readonly string DefaultDirectoryPath = Path.Combine(
        AppContext.BaseDirectory,
        "Dataset",
        "TechWindowAnnotations");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public TechWindowAnnotationStore(string? directoryPath = null)
    {
        DirectoryPath = directoryPath ?? DefaultDirectoryPath;
    }

    public string DirectoryPath { get; }

    public TechWindowAnnotationLoadResult LoadAll()
    {
        var issues = new List<TechWindowAnnotationIssue>();
        var annotationsByStableId = new Dictionary<string, TechWindowAnnotationDocument>(StringComparer.Ordinal);

        if (!Directory.Exists(DirectoryPath))
        {
            return new TechWindowAnnotationLoadResult(
                [],
                new TechWindowAnnotationAuditReport(issues));
        }

        foreach (var sourcePath in Directory
                     .EnumerateFiles(DirectoryPath, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            TechWindowAnnotationDocument? document;

            try
            {
                document = JsonSerializer.Deserialize<TechWindowAnnotationDocument>(
                    File.ReadAllText(sourcePath, Encoding.UTF8),
                    JsonOptions);
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                issues.Add(new TechWindowAnnotationIssue(
                    TechWindowAnnotationIssueKind.InvalidDocument,
                    sourcePath,
                    null,
                    exception.Message));
                continue;
            }

            if (document is null || string.IsNullOrWhiteSpace(document.StableBeatmapId))
            {
                issues.Add(new TechWindowAnnotationIssue(
                    TechWindowAnnotationIssueKind.InvalidDocument,
                    sourcePath,
                    null,
                    "StableBeatmapId is required."));
                continue;
            }

            if (!TryNormalize(document, sourcePath, issues, out var normalized))
            {
                continue;
            }

            if (!annotationsByStableId.TryAdd(normalized.StableBeatmapId, normalized))
            {
                issues.Add(new TechWindowAnnotationIssue(
                    TechWindowAnnotationIssueKind.DuplicateBeatmapAnnotation,
                    sourcePath,
                    normalized.StableBeatmapId,
                    "A prior annotation file already owns this StableBeatmapId; the first file in ordinal path order is retained."));
                continue;
            }

            if (!normalized.AnnotationComplete)
            {
                issues.Add(new TechWindowAnnotationIssue(
                    TechWindowAnnotationIssueKind.IncompleteAnnotation,
                    sourcePath,
                    normalized.StableBeatmapId,
                    "AnnotationComplete is false; unannotated windows remain Unknown."));
            }

            if (normalized.AnnotationComplete && normalized.TechRegions.Count == 0)
            {
                issues.Add(new TechWindowAnnotationIssue(
                    TechWindowAnnotationIssueKind.CompleteAnnotationWithoutTechRegions,
                    sourcePath,
                    normalized.StableBeatmapId,
                    "AnnotationComplete is true but the map contains no positive Tech region."));
            }
        }

        return new TechWindowAnnotationLoadResult(
            annotationsByStableId.Values
                .OrderBy(annotation => annotation.StableBeatmapId, StringComparer.Ordinal)
                .ToList(),
            new TechWindowAnnotationAuditReport(issues));
    }

    public void Save(TechWindowAnnotationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var issues = new List<TechWindowAnnotationIssue>();
        if (!TryNormalize(document, "<save>", issues, out var normalized))
        {
            throw new ArgumentException(
                string.Join(" ", issues.Select(issue => issue.Message)),
                nameof(document));
        }

        Directory.CreateDirectory(DirectoryPath);
        var fileName = Uri.EscapeDataString(normalized.StableBeatmapId) + ".json";
        var destinationPath = Path.Combine(DirectoryPath, fileName);
        var json = JsonSerializer.Serialize(normalized, JsonOptions) + Environment.NewLine;
        File.WriteAllText(destinationPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static bool TryNormalize(
        TechWindowAnnotationDocument document,
        string sourcePath,
        ICollection<TechWindowAnnotationIssue> issues,
        out TechWindowAnnotationDocument normalized)
    {
        var stableBeatmapId = document.StableBeatmapId.Trim();
        if (!TryNormalizeRegions(
                document.TechRegions ?? [],
                sourcePath,
                stableBeatmapId,
                issues,
                out var mergedTechRegions)
            || !TryNormalizeRegions(
                document.NotTechRegions ?? [],
                sourcePath,
                stableBeatmapId,
                issues,
                out var mergedNotTechRegions))
        {
            normalized = null!;
            return false;
        }

        if (HasOverlap(mergedTechRegions, mergedNotTechRegions))
        {
            issues.Add(new TechWindowAnnotationIssue(
                TechWindowAnnotationIssueKind.InvalidRegion,
                sourcePath,
                stableBeatmapId,
                "Tech and NotTech regions must not overlap."));
            normalized = null!;
            return false;
        }

        normalized = new TechWindowAnnotationDocument
        {
            StableBeatmapId = stableBeatmapId,
            MapName = document.MapName.Trim(),
            Difficulty = document.Difficulty.Trim(),
            AnnotationComplete = document.AnnotationComplete,
            TechRegions = mergedTechRegions,
            NotTechRegions = mergedNotTechRegions
        };

        return true;
    }

    private static bool TryNormalizeRegions(
        IEnumerable<TechWindowAnnotationRegionDocument> sourceRegions,
        string sourcePath,
        string stableBeatmapId,
        ICollection<TechWindowAnnotationIssue> issues,
        out List<TechWindowAnnotationRegionDocument> normalizedRegions)
    {
        var orderedRegions = sourceRegions
            .OrderBy(region => region.StartTime)
            .ThenBy(region => region.EndTime)
            .ToList();

        foreach (var region in orderedRegions)
        {
            if (!double.IsFinite(region.StartTime)
                || !double.IsFinite(region.EndTime)
                || region.EndTime <= region.StartTime)
            {
                issues.Add(new TechWindowAnnotationIssue(
                    TechWindowAnnotationIssueKind.InvalidRegion,
                    sourcePath,
                    stableBeatmapId,
                    "Every annotation region must have finite times and EndTime > StartTime."));
                normalizedRegions = [];
                return false;
            }
        }

        normalizedRegions = [];
        foreach (var region in orderedRegions)
        {
            if (normalizedRegions.Count == 0)
            {
                normalizedRegions.Add(new TechWindowAnnotationRegionDocument
                {
                    StartTime = region.StartTime,
                    EndTime = region.EndTime
                });
                continue;
            }

            var previous = normalizedRegions[^1];
            if (region.StartTime <= previous.EndTime)
            {
                issues.Add(new TechWindowAnnotationIssue(
                    TechWindowAnnotationIssueKind.OverlappingRegions,
                    sourcePath,
                    stableBeatmapId,
                    "Overlapping regions with the same human label were merged deterministically."));
                previous.EndTime = Math.Max(previous.EndTime, region.EndTime);
                continue;
            }

            normalizedRegions.Add(new TechWindowAnnotationRegionDocument
            {
                StartTime = region.StartTime,
                EndTime = region.EndTime
            });
        }

        return true;
    }

    private static bool HasOverlap(
        IReadOnlyList<TechWindowAnnotationRegionDocument> techRegions,
        IReadOnlyList<TechWindowAnnotationRegionDocument> notTechRegions) =>
        techRegions.Any(tech =>
            notTechRegions.Any(notTech =>
                tech.StartTime < notTech.EndTime
                && notTech.StartTime < tech.EndTime));
}
