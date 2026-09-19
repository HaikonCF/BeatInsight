using BeatInsight.Models;
using System.Globalization;

namespace BeatInsight.Analysis;

/// <summary>
/// Review-only priority. It describes where a human should look first, never a
/// Tech / NotTech decision.
/// </summary>
internal enum TechReviewPriority
{
    High,
    Medium,
    Low
}

internal enum TechReviewHumanLabel
{
    Unreviewed,
    Tech,
    NotTech,
    Unsure
}

internal sealed record TechReviewCandidate(
    string CandidateId,
    string StableBeatmapId,
    string MapName,
    string Difficulty,
    double StartTime,
    double EndTime,
    TechReviewPriority Priority,
    double RepresentativeScore,
    int WindowCount,
    TechReviewHumanLabel HumanLabel)
{
    internal bool IsReviewed => HumanLabel != TechReviewHumanLabel.Unreviewed;
}

internal sealed class TechReviewDocument
{
    public string StableBeatmapId { get; set; } = string.Empty;
    public string MapName { get; set; } = string.Empty;
    public string Difficulty { get; set; } = string.Empty;
    public List<TechReviewCandidate> Candidates { get; set; } = [];
}

internal sealed record TechReviewMapSource(
    Beatmap Beatmap,
    string StableBeatmapId,
    string MapName,
    string Difficulty);

/// <summary>
/// Review-only sampling controls. These are counts and a presentation merge
/// gap, not gameplay thresholds or labels.
/// </summary>
internal sealed record TechReviewSamplingOptions(
    int HighCandidateCount = 4,
    int MediumCandidateCount = 3,
    int LowCandidateCount = 3,
    double ReviewMergeGapMilliseconds = 250);

internal sealed record TechReviewMapStatistics(
    int TotalWindows,
    int CandidateRegions,
    int High,
    int Medium,
    int Low,
    int AlreadyReviewed,
    int Unreviewed,
    int TechLabels,
    int NotTechLabels,
    int UnsureLabels);

internal sealed record TechReviewGenerationResult(
    TechReviewDocument Document,
    TechReviewMapStatistics Statistics,
    int NewCandidateRegions);

/// <summary>
/// Selects compact, temporally diverse review passages from the existing
/// experimental window ranking. It neither writes labels nor changes runtime
/// analysis.
/// </summary>
internal static class TechReviewAssistant
{
    internal static TechReviewGenerationResult Generate(
        TechReviewMapSource source,
        TechReviewDocument? existingReview = null,
        TechReviewSamplingOptions? options = null,
        bool includeReviewed = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(source.Beatmap);

        IReadOnlyList<TechV2ExperimentalWindowResult> windows =
            TechDetectorV2Experimental.Analyze(
                source.Beatmap.HitObjects,
                GameplayAnalyzer.IsSpinner,
                GameplayAnalyzer.IsSlider,
                GameplayAnalyzer.IsComplexSlider,
                GameplayAnalyzer.GetObjectStartPosition,
                GameplayAnalyzer.GetObjectEndPosition,
                GameplayAnalyzer.GetTechTurnAngle);

        return GenerateFromWindows(
            source.StableBeatmapId,
            source.MapName,
            source.Difficulty,
            windows,
            existingReview,
            options,
            includeReviewed);
    }

    internal static TechReviewGenerationResult GenerateFromWindows(
        string stableBeatmapId,
        string mapName,
        string difficulty,
        IReadOnlyList<TechV2ExperimentalWindowResult> windows,
        TechReviewDocument? existingReview = null,
        TechReviewSamplingOptions? options = null,
        bool includeReviewed = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableBeatmapId);
        ArgumentNullException.ThrowIfNull(windows);

        options ??= new TechReviewSamplingOptions();
        ValidateOptions(options);

        IReadOnlyList<ReviewCluster> selected = SelectClusters(
            windows,
            existingReview?.Candidates ?? [],
            options,
            includeReviewed);

        var existingCandidates = existingReview?.Candidates ?? [];
        var knownCandidateIds = new HashSet<string>(
            existingCandidates.Select(candidate => candidate.CandidateId),
            StringComparer.Ordinal);
        List<TechReviewCandidate> newCandidates = selected
            .Select(cluster => ToCandidate(
                stableBeatmapId,
                mapName,
                difficulty,
                cluster))
            .Where(candidate => knownCandidateIds.Add(candidate.CandidateId))
            .ToList();

        var document = new TechReviewDocument
        {
            StableBeatmapId = stableBeatmapId.Trim(),
            MapName = mapName.Trim(),
            Difficulty = difficulty.Trim(),
            Candidates = existingCandidates
                .Concat(newCandidates)
                .OrderBy(candidate => candidate.StartTime)
                .ThenBy(candidate => candidate.EndTime)
                .ThenBy(candidate => candidate.Priority)
                .ThenBy(candidate => candidate.CandidateId, StringComparer.Ordinal)
                .ToList()
        };

        return new TechReviewGenerationResult(
            document,
            CreateStatistics(document, windows.Count),
            newCandidates.Count);
    }

    internal static string CreateCandidateId(
        string stableBeatmapId,
        double startTime,
        double endTime) =>
        stableBeatmapId
        + ":"
        + startTime.ToString("0.###", CultureInfo.InvariantCulture)
        + "-"
        + endTime.ToString("0.###", CultureInfo.InvariantCulture);

    internal static TechReviewMapStatistics CreateStatistics(
        TechReviewDocument document,
        int totalWindows)
    {
        ArgumentNullException.ThrowIfNull(document);

        IReadOnlyList<TechReviewCandidate> candidates = document.Candidates;

        return new TechReviewMapStatistics(
            totalWindows,
            candidates.Count,
            candidates.Count(candidate => candidate.Priority == TechReviewPriority.High),
            candidates.Count(candidate => candidate.Priority == TechReviewPriority.Medium),
            candidates.Count(candidate => candidate.Priority == TechReviewPriority.Low),
            candidates.Count(candidate => candidate.IsReviewed),
            candidates.Count(candidate => !candidate.IsReviewed),
            candidates.Count(candidate => candidate.HumanLabel == TechReviewHumanLabel.Tech),
            candidates.Count(candidate => candidate.HumanLabel == TechReviewHumanLabel.NotTech),
            candidates.Count(candidate => candidate.HumanLabel == TechReviewHumanLabel.Unsure));
    }

    private static IReadOnlyList<ReviewCluster> SelectClusters(
        IReadOnlyList<TechV2ExperimentalWindowResult> windows,
        IReadOnlyList<TechReviewCandidate> existingCandidates,
        TechReviewSamplingOptions options,
        bool includeReviewed)
    {
        if (windows.Count == 0)
            return [];

        IReadOnlyList<RankedWindow> ranked = windows
            .OrderByDescending(window => window.TechLikelihoodExperimental)
            .ThenBy(window => window.StartIndex)
            .Select((window, rank) => new RankedWindow(window, rank))
            .ToArray();

        (IReadOnlyList<RankedWindow> high,
            IReadOnlyList<RankedWindow> medium,
            IReadOnlyList<RankedWindow> low) =
            PartitionRelativeRanks(ranked);

        IReadOnlyList<TechReviewCandidate> reviewed = existingCandidates
            .Where(candidate => candidate.IsReviewed)
            .ToArray();
        var selected = new List<ReviewCluster>();

        SelectPriority(
            high,
            TechReviewPriority.High,
            options.HighCandidateCount,
            options,
            reviewed,
            includeReviewed,
            selected);
        SelectPriority(
            medium,
            TechReviewPriority.Medium,
            options.MediumCandidateCount,
            options,
            reviewed,
            includeReviewed,
            selected);
        SelectPriority(
            low,
            TechReviewPriority.Low,
            options.LowCandidateCount,
            options,
            reviewed,
            includeReviewed,
            selected);

        return selected;
    }

    private static void SelectPriority(
        IReadOnlyList<RankedWindow> rankedWindows,
        TechReviewPriority priority,
        int requestedCount,
        TechReviewSamplingOptions options,
        IReadOnlyList<TechReviewCandidate> reviewed,
        bool includeReviewed,
        ICollection<ReviewCluster> selected)
    {
        if (requestedCount <= 0 || rankedWindows.Count == 0)
            return;

        IReadOnlyList<ReviewCluster> clusters = MergeForReview(
                rankedWindows,
                priority,
                options.ReviewMergeGapMilliseconds)
            .Where(cluster =>
                includeReviewed
                || !reviewed.Any(candidate => IsSamePassage(
                    cluster.StartTime,
                    cluster.EndTime,
                    candidate.StartTime,
                    candidate.EndTime,
                    options.ReviewMergeGapMilliseconds)))
            .Where(cluster => !selected.Any(other => IsSamePassage(
                cluster.StartTime,
                cluster.EndTime,
                other.StartTime,
                other.EndTime,
                options.ReviewMergeGapMilliseconds)))
            .ToArray();

        IReadOnlyList<ReviewCluster> rankedClusters = RankClusters(
            clusters,
            priority,
            rankedWindows.Select(item => item.Window.TechLikelihoodExperimental));

        foreach (ReviewCluster cluster in SelectTemporallyDiverse(
            rankedClusters,
            requestedCount,
            selected))
        {
            selected.Add(cluster);
        }
    }

    private static (
        IReadOnlyList<RankedWindow> High,
        IReadOnlyList<RankedWindow> Medium,
        IReadOnlyList<RankedWindow> Low)
        PartitionRelativeRanks(IReadOnlyList<RankedWindow> ranked)
    {
        int bandSize = (int)Math.Ceiling(ranked.Count / 3.0);
        int highEnd = Math.Min(bandSize, ranked.Count);
        int lowStart = Math.Max(highEnd, ranked.Count - bandSize);

        return (
            ranked.Take(highEnd).ToArray(),
            ranked.Skip(highEnd).Take(lowStart - highEnd).ToArray(),
            ranked.Skip(lowStart).ToArray());
    }

    private static IReadOnlyList<ReviewCluster> MergeForReview(
        IReadOnlyList<RankedWindow> rankedWindows,
        TechReviewPriority priority,
        double mergeGapMilliseconds)
    {
        IReadOnlyList<RankedWindow> ordered = rankedWindows
            .OrderBy(item => item.Window.StartTime)
            .ThenBy(item => item.Window.EndTime)
            .ThenBy(item => item.Rank)
            .ToArray();

        if (ordered.Count == 0)
            return [];

        var clusters = new List<ReviewCluster>();
        var current = new ReviewCluster(
            priority,
            ordered[0].Window.StartTime,
            ordered[0].Window.EndTime,
            [ordered[0]]);

        foreach (RankedWindow next in ordered.Skip(1))
        {
            if (next.Window.StartTime <= current.EndTime + mergeGapMilliseconds)
            {
                current = current with
                {
                    EndTime = Math.Max(current.EndTime, next.Window.EndTime),
                    Windows = current.Windows.Append(next).ToArray()
                };
                continue;
            }

            clusters.Add(current);
            current = new ReviewCluster(
                priority,
                next.Window.StartTime,
                next.Window.EndTime,
                [next]);
        }

        clusters.Add(current);
        return clusters;
    }

    private static IReadOnlyList<ReviewCluster> RankClusters(
        IReadOnlyList<ReviewCluster> clusters,
        TechReviewPriority priority,
        IEnumerable<double> scores)
    {
        double middleScore = scores.OrderBy(score => score).ElementAt(
            Math.Max(0, (scores.Count() - 1) / 2));

        return priority switch
        {
            TechReviewPriority.High => clusters
                .OrderByDescending(cluster => cluster.MaxScore)
                .ThenBy(cluster => cluster.StartTime)
                .ToArray(),
            TechReviewPriority.Low => clusters
                .OrderBy(cluster => cluster.MinScore)
                .ThenBy(cluster => cluster.StartTime)
                .ToArray(),
            _ => clusters
                .OrderBy(cluster => Math.Abs(cluster.MeanScore - middleScore))
                .ThenBy(cluster => cluster.StartTime)
                .ToArray()
        };
    }

    private static IReadOnlyList<ReviewCluster> SelectTemporallyDiverse(
        IReadOnlyList<ReviewCluster> rankedClusters,
        int requestedCount,
        IEnumerable<ReviewCluster> alreadySelected)
    {
        var remaining = rankedClusters.ToList();
        var chosen = new List<ReviewCluster>();
        List<double> centers = alreadySelected
            .Select(cluster => cluster.CenterTime)
            .ToList();

        while (remaining.Count > 0 && chosen.Count < requestedCount)
        {
            ReviewCluster next;
            if (centers.Count == 0)
            {
                next = remaining[0];
            }
            else
            {
                next = remaining
                    .Select((cluster, index) => new
                    {
                        Cluster = cluster,
                        Index = index,
                        Distance = centers.Min(center =>
                            Math.Abs(cluster.CenterTime - center))
                    })
                    .OrderByDescending(item => item.Distance)
                    .ThenBy(item => item.Index)
                    .Select(item => item.Cluster)
                    .First();
            }

            chosen.Add(next);
            centers.Add(next.CenterTime);
            remaining.Remove(next);
        }

        return chosen;
    }

    private static TechReviewCandidate ToCandidate(
        string stableBeatmapId,
        string mapName,
        string difficulty,
        ReviewCluster cluster) =>
        new(
            CreateCandidateId(stableBeatmapId, cluster.StartTime, cluster.EndTime),
            stableBeatmapId,
            mapName,
            difficulty,
            cluster.StartTime,
            cluster.EndTime,
            cluster.Priority,
            cluster.RepresentativeScore,
            cluster.Windows.Count,
            TechReviewHumanLabel.Unreviewed);

    private static bool IsSamePassage(
        double firstStart,
        double firstEnd,
        double secondStart,
        double secondEnd,
        double gapMilliseconds) =>
        firstStart <= secondEnd + gapMilliseconds
        && secondStart <= firstEnd + gapMilliseconds;

    private static void ValidateOptions(TechReviewSamplingOptions options)
    {
        if (options.HighCandidateCount < 0
            || options.MediumCandidateCount < 0
            || options.LowCandidateCount < 0
            || !double.IsFinite(options.ReviewMergeGapMilliseconds)
            || options.ReviewMergeGapMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Review candidate counts and merge gap must be non-negative finite values.");
        }
    }

    private sealed record RankedWindow(
        TechV2ExperimentalWindowResult Window,
        int Rank);

    private sealed record ReviewCluster(
        TechReviewPriority Priority,
        double StartTime,
        double EndTime,
        IReadOnlyList<RankedWindow> Windows)
    {
        public double CenterTime => (StartTime + EndTime) / 2.0;

        public double MaxScore => Windows.Max(
            item => item.Window.TechLikelihoodExperimental);

        public double MinScore => Windows.Min(
            item => item.Window.TechLikelihoodExperimental);

        public double MeanScore => Windows.Average(
            item => item.Window.TechLikelihoodExperimental);

        public double RepresentativeScore => Priority switch
        {
            TechReviewPriority.High => MaxScore,
            TechReviewPriority.Low => MinScore,
            _ => MeanScore
        };
    }
}
