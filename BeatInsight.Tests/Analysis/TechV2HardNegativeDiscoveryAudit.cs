using BeatInsight.Analysis;
using BeatInsight.Models;
using BeatInsight.Models.Persistence;
using BeatInsight.Parser;
using BeatInsight.Services.Library;
using BeatInsight.Services.Persistence;
using System.Globalization;
using System.IO;
using System.Text;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// Phase 2.7 test-only discovery tool. It scans local beatmaps already
/// reachable through BeatInsight's existing local mechanisms (test fixtures,
/// or the real local analysis cache built by <see cref="MlDatasetBuilder"/>
/// from the user's osu! Songs folder) and looks for maps producing
/// StrongTech or SustainedTech windows while their current BeatInsight
/// structural identity does not already suggest Tech. It never runs the
/// full Tech V2 engine differently than production (default geometry guard
/// applies), never touches the network, and never mutates or feeds anything
/// back into runtime state, GameplayIdentity, or the ML pipeline. Results
/// are review candidates only: a map here is never automatically treated as
/// NotTech, and a Tech-identified map is never treated as a false positive
/// just because it appears here.
/// </summary>
internal static class TechV2HardNegativeDiscoveryAudit
{
    internal const int MaxCandidatesPerGroup = 15;
    internal const int DefaultLocalLibrarySampleSize = 500;

    /// <summary>
    /// Scans a flat directory of .osu fixtures (test-only corpus). Kept for
    /// the existing fixture-based regression coverage of this tool.
    /// </summary>
    internal static TechV2HardNegativeDiscoveryReport Scan(string fixturesDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixturesDirectory);

        if (!Directory.Exists(fixturesDirectory))
        {
            return EmptyReport();
        }

        IReadOnlyList<string> paths = Directory
            .EnumerateFiles(fixturesDirectory, "*.osu", SearchOption.TopDirectoryOnly)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();

        return ScanPaths(paths, localMapsDiscovered: paths.Count);
    }

    /// <summary>
    /// Scans a deterministic sample of the real local beatmap inventory.
    /// The inventory itself is never rebuilt or rescanned here: this reuses
    /// the same SQLite analysis cache that <see cref="MlDatasetBuilder"/>
    /// already populates from the user's configured osu! Songs folder
    /// (<see cref="MlDatasetSampleRepository.DefaultDatabasePath"/> by
    /// default). No network call, no filesystem walk outside that existing
    /// cache, and no arbitrary path is touched here.
    /// </summary>
    internal static TechV2HardNegativeDiscoveryReport ScanLocalLibrary(
        string? databasePath = null,
        int maxMaps = DefaultLocalLibrarySampleSize,
        int maxCandidatesPerGroup = MaxCandidatesPerGroup)
    {
        if (maxMaps <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMaps));
        }

        string resolvedDatabasePath =
            databasePath ?? MlDatasetSampleRepository.DefaultDatabasePath;

        if (!File.Exists(resolvedDatabasePath))
        {
            return EmptyReport(
                [$"local analysis cache not found at {resolvedDatabasePath}"]);
        }

        var repository = new MlDatasetSampleRepository(resolvedDatabasePath);
        IReadOnlyList<MlDatasetSample> samples = repository.List();

        // Deterministic regardless of insertion order/SampleId history: the
        // sample taken for this pass depends only on the file paths
        // currently present in the cache, never on scan timing.
        Dictionary<string, int?> beatmapIdByPath = samples
            .Where(sample => File.Exists(sample.SourceFilePath))
            .GroupBy(sample => sample.SourceFilePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().BeatmapId,
                StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<string> discoveredPaths = beatmapIdByPath.Keys
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        IReadOnlyList<string> sample = discoveredPaths
            .Take(maxMaps)
            .ToArray();

        return ScanPaths(
            sample,
            localMapsDiscovered: discoveredPaths.Count,
            beatmapIdLookup: path => beatmapIdByPath.GetValueOrDefault(path),
            maxCandidatesPerGroup: maxCandidatesPerGroup);
    }

    private static TechV2HardNegativeDiscoveryReport ScanPaths(
        IReadOnlyList<string> orderedPaths,
        int localMapsDiscovered,
        Func<string, int?>? beatmapIdLookup = null,
        int maxCandidatesPerGroup = MaxCandidatesPerGroup)
    {
        ArgumentNullException.ThrowIfNull(orderedPaths);

        List<TechV2HardNegativeCandidate> strongTechSuspects = [];
        List<TechV2HardNegativeCandidate> sustainedTechSuspects = [];
        List<string> skipped = [];
        int usable = 0;
        int unsupportedGameModeCount = 0;
        int parseFailureCount = 0;
        int techIdentityExcludedCount = 0;

        foreach (string path in orderedPaths)
        {
            string fileName = Path.GetFileName(path);

            // BeatInsight V2 analyzes osu!standard only; this reuses the
            // same cheap header read the production scanners already use
            // instead of parsing the full beatmap first.
            if (!BeatmapGameModeReader.IsSupportedForAnalysis(path))
            {
                unsupportedGameModeCount++;
                skipped.Add($"{fileName}: unsupported game mode (Mode != 0)");
                continue;
            }

            Beatmap map;
            try
            {
                map = BeatmapParser.Load(path);
            }
            catch (Exception exception) when (
                exception is IOException or FormatException or InvalidDataException)
            {
                parseFailureCount++;
                skipped.Add($"{fileName}: parse failure ({exception.GetType().Name})");
                continue;
            }

            if (map.HitObjects.Count < TechDetectorV2Experimental.WindowSize)
            {
                skipped.Add($"{fileName}: fewer objects than one Tech V2 window");
                continue;
            }

            usable++;

            IReadOnlyList<TechV2ExperimentalWindowResult> windows =
                TechDetectorV2Experimental.Analyze(
                    map.HitObjects,
                    GameplayAnalyzer.IsSpinner,
                    GameplayAnalyzer.IsSlider,
                    GameplayAnalyzer.IsComplexSlider,
                    GameplayAnalyzer.GetObjectStartPosition,
                    GameplayAnalyzer.GetObjectEndPosition,
                    GameplayAnalyzer.GetTechTurnAngle);
            IReadOnlyList<TechV2WindowDecision> baseDecisions =
                TechV2ExperimentalDecisionEngine.Decide(windows);
            // Default guard (applyGeometryGuard: true) reproduces production
            // Tech V2 behavior exactly; this discovery pass never simulates
            // an alternate geometry threshold.
            IReadOnlyList<TechV2WindowDecision> decisions =
                TechV2ExperimentalDecisionEngine
                    .PromoteSustainedTechWithDiagnostics(baseDecisions)
                    .Decisions;

            double playableStart = map.HitObjects.First().Time;
            double playableEnd = map.HitObjects.Last().Time;
            TechV2PresenceResult presence = TechV2ExperimentalDecisionEngine.BuildPresence(
                decisions, playableStart, playableEnd);

            int strongTechCount = decisions.Count(decision => decision.IsStrongTech);
            int techSupportCount = decisions.Count(decision => decision.IsTechSupport);
            int sustainedTechCount = decisions.Count(decision => decision.IsSustainedTech);

            GameplayProfile profile = GameplayAnalyzer.Analyze(map);
            GameplayIdentity identity = profile.Identity;

            // A map already identified as Tech (Primary or Secondary) is
            // excluded from the hard-negative shortlist: it is not a
            // suspect, it is an expected Tech detection. The converse is
            // never assumed: a non-Tech Primary/Secondary is not ground
            // truth NotTech, only the absence of a reason to exclude it.
            bool isTechIdentity =
                string.Equals(identity.Primary, "Tech", StringComparison.Ordinal)
                || string.Equals(identity.Secondary, "Tech", StringComparison.Ordinal);

            if (isTechIdentity)
            {
                techIdentityExcludedCount++;
                continue;
            }

            if (strongTechCount == 0 && sustainedTechCount == 0)
            {
                continue;
            }

            IReadOnlyList<TechV2HardNegativeRun> sustainedRuns = FindSustainedRuns(decisions);
            double? maxSustainedAverageGeometry = sustainedRuns.Count == 0
                ? null
                : sustainedRuns.Max(run => run.AverageGeometry);
            double? minSustainedAverageGeometry = sustainedRuns.Count == 0
                ? null
                : sustainedRuns.Min(run => run.AverageGeometry);

            int? beatmapId = beatmapIdLookup?.Invoke(path);
            int? stableBeatmapId = BeatmapMetadataReader.ReadBeatmapId(path);

            var candidate = new TechV2HardNegativeCandidate(
                map.Title,
                map.Version,
                beatmapId,
                stableBeatmapId,
                identity.Primary,
                identity.Secondary,
                identity.Pattern,
                strongTechCount,
                techSupportCount,
                sustainedTechCount,
                presence.Regions.Count,
                presence.PresencePercent,
                maxSustainedAverageGeometry,
                minSustainedAverageGeometry,
                path);

            if (strongTechCount > 0)
            {
                strongTechSuspects.Add(candidate);
            }
            else
            {
                sustainedTechSuspects.Add(candidate);
            }
        }

        IReadOnlyList<TechV2HardNegativeCandidate> topStrongTech = strongTechSuspects
            .OrderByDescending(candidate => candidate.PresencePercent)
            .ThenBy(candidate => candidate.Title, StringComparer.Ordinal)
            .Take(maxCandidatesPerGroup)
            .ToArray();
        IReadOnlyList<TechV2HardNegativeCandidate> topSustainedTech = sustainedTechSuspects
            .OrderByDescending(candidate => candidate.PresencePercent)
            .ThenBy(candidate => candidate.Title, StringComparer.Ordinal)
            .Take(maxCandidatesPerGroup)
            .ToArray();

        return new TechV2HardNegativeDiscoveryReport(
            localMapsDiscovered,
            orderedPaths.Count,
            usable,
            unsupportedGameModeCount,
            parseFailureCount,
            techIdentityExcludedCount,
            skipped,
            strongTechSuspects.Count,
            sustainedTechSuspects.Count,
            topStrongTech,
            topSustainedTech);
    }

    private static TechV2HardNegativeDiscoveryReport EmptyReport(
        IReadOnlyList<string>? skipped = null) => new(
            0, 0, 0, 0, 0, 0, skipped ?? [], 0, 0, [], []);

    private static IReadOnlyList<TechV2HardNegativeRun> FindSustainedRuns(
        IReadOnlyList<TechV2WindowDecision> decisions)
    {
        List<TechV2HardNegativeRun> runs = [];
        List<TechV2WindowDecision> active = [];

        void Flush()
        {
            if (active.Count == 0)
            {
                return;
            }

            runs.Add(new TechV2HardNegativeRun(
                active[0].StartTime,
                active[^1].EndTime,
                active.Average(item => item.GeometryEvidence)));
            active.Clear();
        }

        foreach (TechV2WindowDecision decision in decisions
            .OrderBy(item => item.StartIndex)
            .ThenBy(item => item.EndIndex))
        {
            bool continuous = active.Count > 0
                && decision.StartIndex == active[^1].StartIndex + TechDetectorV2Experimental.Stride
                && decision.StartTime <= active[^1].EndTime;

            if (decision.IsSustainedTech && (active.Count == 0 || continuous))
            {
                active.Add(decision);
                continue;
            }

            Flush();

            if (decision.IsSustainedTech)
            {
                active.Add(decision);
            }
        }

        Flush();
        return runs;
    }

    internal static string FormatReport(TechV2HardNegativeDiscoveryReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        StringBuilder text = new();
        text.AppendLine("===== TECH V2 PHASE 2.7 LOCAL HARD NEGATIVE DISCOVERY (TEST-ONLY) =====");
        text.AppendLine(
            $"Local maps discovered: {report.LocalMapsDiscovered} "
            + $"| Maps scanned: {report.MapsScanned} "
            + $"| Mode!=0 skipped: {report.UnsupportedGameModeSkippedCount} "
            + $"| Parse failures: {report.ParseFailureCount}");
        text.AppendLine(
            $"Tech-identity maps excluded: {report.TechIdentityExcludedCount} "
            + $"| Group A count: {report.GroupARawCount} "
            + $"| Group B count: {report.GroupBRawCount}");

        if (report.Skipped.Count > 0)
        {
            text.AppendLine("Skipped detail:");
            foreach (string reason in report.Skipped)
            {
                text.AppendLine($"  {reason}");
            }
        }

        text.AppendLine("GROUP A - StrongTech suspects, non-Tech identity (Presence descending):");
        AppendCandidates(text, report.StrongTechSuspects);

        text.AppendLine("GROUP B - SustainedTech suspects, non-Tech identity (Presence descending):");
        AppendCandidates(text, report.SustainedTechSuspects);

        return text.ToString();
    }

    private static void AppendCandidates(
        StringBuilder text,
        IReadOnlyList<TechV2HardNegativeCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            text.AppendLine("  None");
            return;
        }

        foreach (TechV2HardNegativeCandidate candidate in candidates)
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  {0} [{1}] | BeatmapId={2} | StableBeatmapId={3} | Primary={4} "
                + "| Secondary={5} | Pattern={6} | StrongTech={7} | TechSupport={8} "
                + "| Sustained={9} | Regions={10} | Presence={11:F2}% "
                + "| MinRunAvgGeometry={12} | MaxRunAvgGeometry={13} | Path={14}",
                candidate.Title,
                candidate.Difficulty,
                candidate.BeatmapId?.ToString(CultureInfo.InvariantCulture) ?? "N/A",
                candidate.StableBeatmapId?.ToString(CultureInfo.InvariantCulture) ?? "N/A",
                string.IsNullOrWhiteSpace(candidate.Primary) ? "N/A" : candidate.Primary,
                string.IsNullOrWhiteSpace(candidate.Secondary) ? "None" : candidate.Secondary,
                string.IsNullOrWhiteSpace(candidate.Pattern) ? "N/A" : candidate.Pattern,
                candidate.StrongTechCount,
                candidate.TechSupportCount,
                candidate.SustainedTechCount,
                candidate.RegionCount,
                candidate.PresencePercent,
                candidate.MinSustainedRunAverageGeometry is double min
                    ? min.ToString("F3", CultureInfo.InvariantCulture)
                    : "N/A",
                candidate.MaxSustainedRunAverageGeometry is double max
                    ? max.ToString("F3", CultureInfo.InvariantCulture)
                    : "N/A",
                candidate.LocalPath));
        }
    }
}

internal sealed record TechV2HardNegativeRun(
    double StartTime,
    double EndTime,
    double AverageGeometry);

internal sealed record TechV2HardNegativeCandidate(
    string Title,
    string Difficulty,
    int? BeatmapId,
    int? StableBeatmapId,
    string Primary,
    string Secondary,
    string Pattern,
    int StrongTechCount,
    int TechSupportCount,
    int SustainedTechCount,
    int RegionCount,
    double PresencePercent,
    double? MaxSustainedRunAverageGeometry,
    double? MinSustainedRunAverageGeometry,
    string LocalPath);

internal sealed record TechV2HardNegativeDiscoveryReport(
    int LocalMapsDiscovered,
    int MapsScanned,
    int MapsUsable,
    int UnsupportedGameModeSkippedCount,
    int ParseFailureCount,
    int TechIdentityExcludedCount,
    IReadOnlyList<string> Skipped,
    int GroupARawCount,
    int GroupBRawCount,
    IReadOnlyList<TechV2HardNegativeCandidate> StrongTechSuspects,
    IReadOnlyList<TechV2HardNegativeCandidate> SustainedTechSuspects);
