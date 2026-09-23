using BeatInsight.Analysis;
using BeatInsight.Models;
using BeatInsight.Parser;
using System.Globalization;
using System.Text;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// Phase 2.9 test-only calibration tool. It reuses the Phase 2.7 local
/// discovery mechanism (same deterministic osu! Songs cache enumeration,
/// same Mode=0 filter, same Primary/Secondary != Tech exclusion) to gather
/// a larger Group A ("StrongTech suspects") sample, then reuses Phase 2.8's
/// per-window feature extraction (<see cref="TechV2StrongTechForensicAudit.DescribeWindow"/>)
/// and its Movement+Slider/low-geometry guard candidate
/// (<see cref="TechV2StrongTechForensicAudit.MovementSliderLowGeometryGuard"/>)
/// to simulate that guard, purely offline, across the larger sample. No
/// DecisionKind, threshold, feature, weight, region-propagation, or
/// GameplayIdentity logic is modified anywhere in this file.
/// </summary>
internal static class TechV2StrongTechGuardCalibrationAudit
{
    internal const int DefaultMaxMaps = 1000;
    internal const int ShortlistSize = 10;
    internal const int CorpusShortlistSize = 5;

    internal static TechV2StrongTechGuardCalibrationReport Run(
        string? databasePath = null,
        int maxMaps = DefaultMaxMaps)
    {
        // Step 1: reuse the Phase 2.7 mechanism verbatim for map discovery,
        // Mode=0 filtering, and Tech-identity exclusion. Passing
        // int.MaxValue here only removes Phase 2.7's own top-15 reporting
        // cap; it does not change any Tech V2 rule.
        TechV2HardNegativeDiscoveryReport phase27Report =
            TechV2HardNegativeDiscoveryAudit.ScanLocalLibrary(
                databasePath,
                maxMaps,
                maxCandidatesPerGroup: int.MaxValue);

        IReadOnlyList<TechV2StrongTechCalibrationMap> maps = phase27Report
            .StrongTechSuspects
            .Select(candidate => AnalyzeCandidate(candidate, BeatmapParser.Load(candidate.LocalPath)))
            .ToArray();

        IReadOnlyList<TechV2StrongTechCalibrationWindow> allWindows = maps
            .SelectMany(map => map.Windows)
            .ToArray();

        return new TechV2StrongTechGuardCalibrationReport(
            phase27Report.MapsScanned,
            phase27Report.TechIdentityExcludedCount,
            maps.Count,
            allWindows.Count,
            allWindows.Count(window => window.G3WouldReject),
            allWindows.Count(window => !window.G3WouldReject),
            maps.Count(map => map.AffectedByG3),
            BuildPivotDistribution(allWindows, isMovementSliderPair: true),
            BuildPivotDistribution(allWindows, isMovementSliderPair: false),
            maps,
            BuildShortlist(maps, requireAffected: true),
            BuildShortlist(maps, requireAffected: false),
            BuildReferenceSanityCheck());
    }

    /// <summary>
    /// Phase 2.9b corpus fix. All ten human-verified reference maps used to
    /// justify the G3 candidate are NON-TECH, and several sit at a Star
    /// Rating low enough that BeatInsight's own identity gate can never
    /// reach a specialized Primary/Secondary regardless of local geometry.
    /// At that difficulty level, "Primary/Secondary != Tech" is guaranteed
    /// rather than observed, so it carries no calibration signal.
    ///
    /// This reuses BeatInsight's own existing Tech-secondary eligibility
    /// gate verbatim (see <c>GameplayAnalyzer.techSecondaryEligible</c>:
    /// <c>TechPresence &gt;= 0.08 &amp;&amp; TechIntensity &gt;= 28.0</c>).
    /// Below that gate a map cannot structurally reach Secondary=Tech no
    /// matter what a local StrongTech window says, so its non-Tech identity
    /// is excluded here from the calibration corpus only. No new threshold
    /// is invented, and GameplayIdentity itself is never touched: this only
    /// filters which already-classified maps are worth reading as evidence.
    /// </summary>
    private const double TechSecondaryEligibleMinimumPresence = 0.08;
    private const double TechSecondaryEligibleMinimumIntensity = 28.0;

    internal static TechV2StrongTechCalibrationCorpusReport RunEligibilityFiltered(
        string? databasePath = null,
        int maxMaps = DefaultMaxMaps)
    {
        TechV2HardNegativeDiscoveryReport phase27Report =
            TechV2HardNegativeDiscoveryAudit.ScanLocalLibrary(
                databasePath,
                maxMaps,
                maxCandidatesPerGroup: int.MaxValue);

        List<TechV2StrongTechEligibleMap> eligible = [];
        int lowDifficultyIdentityBiasExcluded = 0;

        foreach (TechV2HardNegativeCandidate candidate in phase27Report.StrongTechSuspects)
        {
            Beatmap map = BeatmapParser.Load(candidate.LocalPath);
            GameplayProfile profile = map.GameplayProfile;

            bool identityApplicable =
                profile.TechPresence >= TechSecondaryEligibleMinimumPresence
                && profile.TechIntensity >= TechSecondaryEligibleMinimumIntensity;

            if (!identityApplicable)
            {
                lowDifficultyIdentityBiasExcluded++;
                continue;
            }

            TechV2StrongTechCalibrationMap analyzed = AnalyzeCandidate(candidate, map);
            eligible.Add(new TechV2StrongTechEligibleMap(
                analyzed,
                map.OsuStarRating,
                profile.TechPresence,
                profile.TechIntensity));
        }

        IReadOnlyList<TechV2StrongTechEligibleMap> rejectShortlist = eligible
            .Where(entry => entry.Map.AffectedByG3)
            .OrderByDescending(entry => entry.StarRating)
            .ThenByDescending(entry => entry.Map.Candidate.PresencePercent)
            .Take(CorpusShortlistSize)
            .ToArray();

        IReadOnlyList<TechV2StrongTechEligibleMap> keepShortlist = eligible
            .Where(entry => !entry.Map.AffectedByG3)
            .OrderByDescending(entry => entry.StarRating)
            .ThenByDescending(entry => entry.Map.Candidate.PresencePercent)
            .Take(CorpusShortlistSize)
            .ToArray();

        return new TechV2StrongTechCalibrationCorpusReport(
            phase27Report.MapsScanned,
            phase27Report.TechIdentityExcludedCount,
            phase27Report.StrongTechSuspects.Count,
            lowDifficultyIdentityBiasExcluded,
            eligible.Count,
            rejectShortlist,
            keepShortlist);
    }

    private static TechV2StrongTechCalibrationMap AnalyzeCandidate(
        TechV2HardNegativeCandidate candidate,
        Beatmap map)
    {
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
        // Default guard (applyGeometryGuard: true): identical to production.
        // G3 is only evaluated afterwards, offline, on the resulting
        // StrongTech windows; DecisionKind itself is never touched.
        IReadOnlyList<TechV2WindowDecision> decisions =
            TechV2ExperimentalDecisionEngine
                .PromoteSustainedTechWithDiagnostics(baseDecisions)
                .Decisions;

        IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> byStartIndex =
            windows.ToDictionary(window => window.StartIndex);

        IReadOnlyList<TechV2StrongTechCalibrationWindow> strongTechWindows = decisions
            .Where(decision => decision.IsStrongTech)
            .OrderBy(decision => decision.StartIndex)
            .Select(decision =>
            {
                TechV2StrongTechWindowDetail detail = TechV2StrongTechForensicAudit.DescribeWindow(
                    decision,
                    byStartIndex[decision.StartIndex],
                    map.HitObjects);

                bool g3WouldReject = !TechV2StrongTechForensicAudit
                    .MovementSliderLowGeometryGuard
                    .KeepsWindow(detail);

                return new TechV2StrongTechCalibrationWindow(detail, g3WouldReject);
            })
            .ToArray();

        return new TechV2StrongTechCalibrationMap(candidate, strongTechWindows);
    }

    private static IReadOnlyDictionary<int, int> BuildPivotDistribution(
        IReadOnlyList<TechV2StrongTechCalibrationWindow> windows,
        bool isMovementSliderPair)
    {
        IEnumerable<TechV2StrongTechCalibrationWindow> filtered = windows.Where(
            window => IsMovementSliderPair(window.Detail) == isMovementSliderPair);

        Dictionary<int, int> buckets = Enumerable.Range(0, 6).ToDictionary(bucket => bucket, _ => 0);

        foreach (TechV2StrongTechCalibrationWindow window in filtered)
        {
            int bucket = Math.Min(window.Detail.Support.StructuralPivotCount, 5);
            buckets[bucket]++;
        }

        return buckets;
    }

    private static bool IsMovementSliderPair(TechV2StrongTechWindowDetail detail) =>
        (string.Equals(detail.BestFamily, "Movement", StringComparison.Ordinal)
            && string.Equals(detail.SecondBestFamily, "Slider", StringComparison.Ordinal))
        || (string.Equals(detail.BestFamily, "Slider", StringComparison.Ordinal)
            && string.Equals(detail.SecondBestFamily, "Movement", StringComparison.Ordinal));

    private static IReadOnlyList<TechV2StrongTechCalibrationMap> BuildShortlist(
        IReadOnlyList<TechV2StrongTechCalibrationMap> maps,
        bool requireAffected) => maps
            .Where(map => map.AffectedByG3 == requireAffected)
            .OrderByDescending(map => map.Candidate.PresencePercent)
            .ThenBy(map => map.Candidate.Title, StringComparer.Ordinal)
            .Take(ShortlistSize)
            .ToArray();

    /// <summary>
    /// Reuses Phase 2.8's own reference-map resolution and guard simulation
    /// verbatim: no map name is special-cased here, and no expected outcome
    /// is hardcoded. The observed KeptByMap map is reported as-is so a
    /// reader can compare it against the Phase 2.8 forensic report
    /// themselves.
    /// </summary>
    private static TechV2StrongTechGuardOutcome BuildReferenceSanityCheck()
    {
        IReadOnlyList<Models.Persistence.MlDatasetSample> localCacheSamples =
            TechV2StrongTechForensicAudit.LoadLocalCacheSamples();

        IReadOnlyList<TechV2StrongTechMapForensic> referenceMaps = TechV2StrongTechForensicAudit
            .ReferenceMaps
            .Select(definition => TechV2StrongTechForensicAudit.Resolve(definition, localCacheSamples))
            .ToArray();

        return TechV2StrongTechForensicAudit.SimulateGuard(
            TechV2StrongTechForensicAudit.MovementSliderLowGeometryGuard,
            referenceMaps);
    }

    internal static string FormatReport(TechV2StrongTechGuardCalibrationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        StringBuilder text = new();
        text.AppendLine("===== TECH V2 PHASE 2.9 STRONGTECH GUARD CALIBRATION (TEST-ONLY) =====");
        text.AppendLine(
            $"Maps scanned: {report.MapsScanned} "
            + $"| Tech-identity maps excluded: {report.TechIdentityExcludedCount} "
            + $"| Group A maps: {report.GroupAMapCount}");
        text.AppendLine(
            $"Total StrongTech suspect windows: {report.TotalStrongTechWindows} "
            + $"| G3 rejected windows: {report.G3RejectedWindows} "
            + $"| G3 kept windows: {report.G3KeptWindows} "
            + $"| Maps affected by G3: {report.MapsAffectedByG3}");

        text.AppendLine();
        text.AppendLine("StructuralPivotCount distribution | 0 | 1 | 2 | 3 | 4 | >=5");
        text.AppendLine(
            "Movement+Slider StrongTech | "
            + string.Join(" | ", Enumerable.Range(0, 6).Select(
                bucket => report.PivotDistributionMovementSlider[bucket])));
        text.AppendLine(
            "Other family pairs StrongTech | "
            + string.Join(" | ", Enumerable.Range(0, 6).Select(
                bucket => report.PivotDistributionOtherFamilyPairs[bucket])));

        text.AppendLine();
        text.AppendLine("GROUP A - G3 WOULD REJECT (top 10 by Presence, human review shortlist):");
        AppendShortlist(text, report.ShortlistG3WouldReject);

        text.AppendLine();
        text.AppendLine("GROUP B - G3 WOULD KEEP (top 10 by Presence, human review shortlist):");
        AppendShortlist(text, report.ShortlistG3WouldKeep);

        text.AppendLine();
        text.AppendLine("KNOWN HUMAN REFERENCES (Phase 2.8 reproduction check, observed only):");
        text.AppendLine($"  Guard: {report.ReferenceSanityCheck.Name}");
        foreach ((string label, bool kept) in report.ReferenceSanityCheck.KeptByMap)
        {
            text.AppendLine($"    {label}: kept={kept}");
        }

        return text.ToString();
    }

    private static void AppendShortlist(
        StringBuilder text,
        IReadOnlyList<TechV2StrongTechCalibrationMap> maps)
    {
        if (maps.Count == 0)
        {
            text.AppendLine("  None");
            return;
        }

        foreach (TechV2StrongTechCalibrationMap map in maps)
        {
            TechV2HardNegativeCandidate candidate = map.Candidate;
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  {0} [{1}] | BeatmapId={2} | Primary={3} | Secondary={4} | Pattern={5} "
                + "| Presence={6:F2}% | StrongTech={7} | G3Rejected={8} | G3Kept={9} "
                + "| MinPivots={10} | MaxPivots={11} | Path={12}",
                candidate.Title,
                candidate.Difficulty,
                candidate.BeatmapId?.ToString(CultureInfo.InvariantCulture) ?? "N/A",
                string.IsNullOrWhiteSpace(candidate.Primary) ? "N/A" : candidate.Primary,
                string.IsNullOrWhiteSpace(candidate.Secondary) ? "None" : candidate.Secondary,
                string.IsNullOrWhiteSpace(candidate.Pattern) ? "N/A" : candidate.Pattern,
                candidate.PresencePercent,
                candidate.StrongTechCount,
                map.RejectedCount,
                map.KeptCount,
                map.MinStructuralPivots?.ToString(CultureInfo.InvariantCulture) ?? "N/A",
                map.MaxStructuralPivots?.ToString(CultureInfo.InvariantCulture) ?? "N/A",
                candidate.LocalPath));
        }
    }

    internal static string FormatCorpusReport(TechV2StrongTechCalibrationCorpusReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        StringBuilder text = new();
        text.AppendLine("===== TECH V2 PHASE 2.9B CALIBRATION CORPUS FIX (TEST-ONLY) =====");
        text.AppendLine(
            $"Maps scanned: {report.MapsScanned} "
            + $"| Tech-identity maps excluded: {report.TechIdentityExcludedCount} "
            + $"| Group A raw count: {report.GroupARawCount}");
        text.AppendLine(
            $"Low-SR maps excluded from calibration (TechPresence<{TechSecondaryEligibleMinimumPresence:F2} "
            + $"or TechIntensity<{TechSecondaryEligibleMinimumIntensity:F1}, BeatInsight's own "
            + $"techSecondaryEligible gate): {report.LowDifficultyIdentityBiasExcludedCount}");
        text.AppendLine(
            $"Maps eligible after SR/Identity applicability filter: {report.EligibleAfterApplicabilityFilterCount}");

        text.AppendLine();
        text.AppendLine("5 MAPS - G3 WOULD REJECT (priority: Star Rating desc, then Presence desc):");
        AppendCorpusShortlist(text, report.ShortlistG3WouldReject);

        text.AppendLine();
        text.AppendLine("5 MAPS - G3 WOULD KEEP (priority: Star Rating desc, then Presence desc):");
        AppendCorpusShortlist(text, report.ShortlistG3WouldKeep);

        return text.ToString();
    }

    private static void AppendCorpusShortlist(
        StringBuilder text,
        IReadOnlyList<TechV2StrongTechEligibleMap> entries)
    {
        if (entries.Count == 0)
        {
            text.AppendLine("  None");
            return;
        }

        foreach (TechV2StrongTechEligibleMap entry in entries)
        {
            TechV2StrongTechCalibrationMap map = entry.Map;
            TechV2HardNegativeCandidate candidate = map.Candidate;
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  {0} [{1}] | StarRating={2:F2} | Primary={3} | Secondary={4} "
                + "| StrongTech={5} | Presence={6:F2}% | G3Rejected={7} | G3Kept={8} "
                + "| MinPivots={9} | MaxPivots={10} | Path={11}",
                candidate.Title,
                candidate.Difficulty,
                entry.StarRating,
                string.IsNullOrWhiteSpace(candidate.Primary) ? "N/A" : candidate.Primary,
                string.IsNullOrWhiteSpace(candidate.Secondary) ? "None" : candidate.Secondary,
                candidate.StrongTechCount,
                candidate.PresencePercent,
                map.RejectedCount,
                map.KeptCount,
                map.MinStructuralPivots?.ToString(CultureInfo.InvariantCulture) ?? "N/A",
                map.MaxStructuralPivots?.ToString(CultureInfo.InvariantCulture) ?? "N/A",
                candidate.LocalPath));
        }
    }
}

internal sealed record TechV2StrongTechCalibrationWindow(
    TechV2StrongTechWindowDetail Detail,
    bool G3WouldReject);

internal sealed record TechV2StrongTechCalibrationMap(
    TechV2HardNegativeCandidate Candidate,
    IReadOnlyList<TechV2StrongTechCalibrationWindow> Windows)
{
    internal int RejectedCount => Windows.Count(window => window.G3WouldReject);
    internal int KeptCount => Windows.Count(window => !window.G3WouldReject);
    internal bool AffectedByG3 => RejectedCount > 0;

    internal int? MinStructuralPivots => Windows.Count == 0
        ? null
        : Windows.Min(window => window.Detail.Support.StructuralPivotCount);

    internal int? MaxStructuralPivots => Windows.Count == 0
        ? null
        : Windows.Max(window => window.Detail.Support.StructuralPivotCount);
}

internal sealed record TechV2StrongTechGuardCalibrationReport(
    int MapsScanned,
    int TechIdentityExcludedCount,
    int GroupAMapCount,
    int TotalStrongTechWindows,
    int G3RejectedWindows,
    int G3KeptWindows,
    int MapsAffectedByG3,
    IReadOnlyDictionary<int, int> PivotDistributionMovementSlider,
    IReadOnlyDictionary<int, int> PivotDistributionOtherFamilyPairs,
    IReadOnlyList<TechV2StrongTechCalibrationMap> Maps,
    IReadOnlyList<TechV2StrongTechCalibrationMap> ShortlistG3WouldReject,
    IReadOnlyList<TechV2StrongTechCalibrationMap> ShortlistG3WouldKeep,
    TechV2StrongTechGuardOutcome ReferenceSanityCheck);

/// <summary>
/// A Group A map that survived the Phase 2.9b Star Rating / identity
/// applicability filter, i.e. one where BeatInsight's own existing
/// techSecondaryEligible gate (TechPresence &gt;= 0.08 &amp;&amp;
/// TechIntensity &gt;= 28.0) was actually reachable, so its non-Tech
/// Primary/Secondary is real evidence rather than a difficulty artifact.
/// </summary>
internal sealed record TechV2StrongTechEligibleMap(
    TechV2StrongTechCalibrationMap Map,
    double StarRating,
    double TechPresence,
    double TechIntensity);

internal sealed record TechV2StrongTechCalibrationCorpusReport(
    int MapsScanned,
    int TechIdentityExcludedCount,
    int GroupARawCount,
    int LowDifficultyIdentityBiasExcludedCount,
    int EligibleAfterApplicabilityFilterCount,
    IReadOnlyList<TechV2StrongTechEligibleMap> ShortlistG3WouldReject,
    IReadOnlyList<TechV2StrongTechEligibleMap> ShortlistG3WouldKeep);
