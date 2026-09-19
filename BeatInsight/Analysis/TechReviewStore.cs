using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeatInsight.Analysis;

/// <summary>
/// Persists generated review passages and their human-only review state. A
/// review document is deliberately separate from the ground-truth annotation
/// document consumed by the dataset builder.
/// </summary>
internal sealed class TechReviewStore
{
    public static readonly string DefaultDirectoryPath = Path.Combine(
        AppContext.BaseDirectory,
        "Dataset",
        "TechWindowReviews");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public TechReviewStore(string? directoryPath = null)
    {
        DirectoryPath = directoryPath ?? DefaultDirectoryPath;
    }

    public string DirectoryPath { get; }

    public TechReviewDocument? Load(string stableBeatmapId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableBeatmapId);

        string path = GetPath(stableBeatmapId);
        if (!File.Exists(path))
            return null;

        TechReviewDocument? document = JsonSerializer.Deserialize<TechReviewDocument>(
            File.ReadAllText(path, Encoding.UTF8),
            JsonOptions);

        if (document is null)
        {
            throw new InvalidDataException("The Tech review document is empty.");
        }

        ValidateDocument(document);
        return Normalize(document);
    }

    public void Save(TechReviewDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateDocument(document);

        TechReviewDocument normalized = Normalize(document);
        Directory.CreateDirectory(DirectoryPath);
        string json = JsonSerializer.Serialize(normalized, JsonOptions)
            + Environment.NewLine;
        File.WriteAllText(
            GetPath(normalized.StableBeatmapId),
            json,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public string GetPath(string stableBeatmapId) =>
        Path.Combine(
            DirectoryPath,
            Uri.EscapeDataString(stableBeatmapId) + ".json");

    private static TechReviewDocument Normalize(TechReviewDocument document) =>
        new()
        {
            StableBeatmapId = document.StableBeatmapId.Trim(),
            MapName = document.MapName.Trim(),
            Difficulty = document.Difficulty.Trim(),
            Candidates = document.Candidates
                .OrderBy(candidate => candidate.StartTime)
                .ThenBy(candidate => candidate.EndTime)
                .ThenBy(candidate => candidate.Priority)
                .ThenBy(candidate => candidate.CandidateId, StringComparer.Ordinal)
                .ToList()
        };

    private static void ValidateDocument(TechReviewDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.StableBeatmapId))
        {
            throw new InvalidDataException(
                "A Tech review document requires StableBeatmapId.");
        }

        ArgumentNullException.ThrowIfNull(document.Candidates);
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (TechReviewCandidate candidate in document.Candidates)
        {
            if (candidate.StableBeatmapId != document.StableBeatmapId
                || candidate.EndTime <= candidate.StartTime
                || !double.IsFinite(candidate.StartTime)
                || !double.IsFinite(candidate.EndTime)
                || !double.IsFinite(candidate.RepresentativeScore)
                || candidate.RepresentativeScore < 0
                || candidate.RepresentativeScore > 1
                || candidate.WindowCount <= 0
                || !Enum.IsDefined(candidate.Priority)
                || !Enum.IsDefined(candidate.HumanLabel)
                || candidate.CandidateId != TechReviewAssistant.CreateCandidateId(
                    document.StableBeatmapId,
                    candidate.StartTime,
                    candidate.EndTime)
                || !ids.Add(candidate.CandidateId))
            {
                throw new InvalidDataException(
                    "The Tech review document contains an invalid or duplicate candidate.");
            }
        }
    }
}

/// <summary>
/// Converts only explicit human review decisions into the separate annotation
/// representation. Unreviewed and Unsure passages remain Unknown, and this
/// conversion never completes an annotation automatically.
/// </summary>
internal static class TechReviewAnnotationConverter
{
    internal static TechWindowAnnotationDocument Convert(
        TechReviewDocument review,
        TechWindowAnnotationDocument? existingAnnotation = null)
    {
        ArgumentNullException.ThrowIfNull(review);

        if (existingAnnotation is not null
            && !string.Equals(
                review.StableBeatmapId,
                existingAnnotation.StableBeatmapId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Review and annotation StableBeatmapId values must match.",
                nameof(existingAnnotation));
        }

        List<TechWindowAnnotationRegionDocument> tech = MergeRegions(
            (existingAnnotation?.TechRegions ?? [])
                .Concat(review.Candidates
                    .Where(candidate => candidate.HumanLabel == TechReviewHumanLabel.Tech)
                    .Select(ToRegion)));
        List<TechWindowAnnotationRegionDocument> notTech = MergeRegions(
            (existingAnnotation?.NotTechRegions ?? [])
                .Concat(review.Candidates
                    .Where(candidate => candidate.HumanLabel == TechReviewHumanLabel.NotTech)
                    .Select(ToRegion)));

        if (tech.Any(techRegion => notTech.Any(notTechRegion =>
            techRegion.StartTime < notTechRegion.EndTime
            && notTechRegion.StartTime < techRegion.EndTime)))
        {
            throw new InvalidDataException(
                "Human Tech and NotTech review passages must not overlap.");
        }

        return new TechWindowAnnotationDocument
        {
            StableBeatmapId = review.StableBeatmapId,
            MapName = string.IsNullOrWhiteSpace(existingAnnotation?.MapName)
                ? review.MapName
                : existingAnnotation.MapName,
            Difficulty = string.IsNullOrWhiteSpace(existingAnnotation?.Difficulty)
                ? review.Difficulty
                : existingAnnotation.Difficulty,
            // Review samples cover selected passages only. Completion remains
            // an explicit human decision already present in the base document.
            AnnotationComplete = existingAnnotation?.AnnotationComplete ?? false,
            TechRegions = tech,
            NotTechRegions = notTech
        };
    }

    private static TechWindowAnnotationRegionDocument ToRegion(
        TechReviewCandidate candidate) =>
        new()
        {
            StartTime = candidate.StartTime,
            EndTime = candidate.EndTime
        };

    private static List<TechWindowAnnotationRegionDocument> MergeRegions(
        IEnumerable<TechWindowAnnotationRegionDocument> regions)
    {
        var merged = new List<TechWindowAnnotationRegionDocument>();

        foreach (TechWindowAnnotationRegionDocument region in regions
            .OrderBy(region => region.StartTime)
            .ThenBy(region => region.EndTime))
        {
            if (merged.Count == 0 || region.StartTime > merged[^1].EndTime)
            {
                merged.Add(new TechWindowAnnotationRegionDocument
                {
                    StartTime = region.StartTime,
                    EndTime = region.EndTime
                });
                continue;
            }

            merged[^1].EndTime = Math.Max(merged[^1].EndTime, region.EndTime);
        }

        return merged;
    }
}
