using BeatInsight.Analysis;
using BeatInsight.Models;
using BeatInsight.Models.Persistence;
using BeatInsight.Parser;
using System.Globalization;
using System.IO;
using System.Text;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// Phase 2.10 is an offline, human-grounded forensic pass.  It never feeds a
/// simulated policy back into the V2 runtime decision engine.
/// </summary>
internal static class TechV2AnchorAmplificationForensicAudit
{
    internal static TechV2AnchorAmplificationReport Run(string? databasePath = null)
    {
        IReadOnlyList<MlDatasetSample> samples =
            TechV2StrongTechForensicAudit.LoadLocalCacheSamples(databasePath);

        IReadOnlyList<TechV2AnchorAmplificationMap> maps =
            TechV2HumanGroundedStrongTechAudit.References
                .Select(reference => ResolveAndAnalyze(reference, samples))
                .ToArray();

        IReadOnlyList<TechV2AnchorPolicyResult> policies = OfflinePolicies
            .Select(policy => SimulatePolicy(policy, maps))
            .ToArray();

        return new TechV2AnchorAmplificationReport(maps, policies);
    }

    internal static string FormatReport(TechV2AnchorAmplificationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        StringBuilder text = new();
        text.AppendLine("===== TECH V2 PHASE 2.10 ANCHOR AMPLIFICATION FORENSIC (TEST-ONLY) =====");
        text.AppendLine("Human truth is audit-only. All policy simulations use copied decisions.");

        text.AppendLine("RESOLVED HUMAN REFERENCES:");
        foreach (TechV2AnchorAmplificationMap map in report.Maps.Where(map => map.Resolved))
            text.AppendLine($"  {map.Reference.Key} | {map.Reference.HumanTruth} | {map.Path}");
        text.AppendLine("UNRESOLVED HUMAN REFERENCES:");
        foreach (TechV2AnchorAmplificationMap map in report.Maps.Where(map => !map.Resolved))
            text.AppendLine($"  {map.Reference.Key} | {map.ResolutionNote}");

        text.AppendLine("ALL TECH V2 REGIONS | map | truth | start-end | duration | total/strong/sustained/support | first/last strong");
        foreach (TechV2AnchorAmplificationMap map in report.Maps.Where(map => map.Resolved))
        {
            if (map.Regions.Count == 0)
            {
                text.AppendLine($"  {map.Reference.Key} | {map.Reference.HumanTruth} | no regions");
                continue;
            }
            foreach (TechV2AnchorAmplificationRegion region in map.Regions)
            {
                string anchors = region.StrongTechCount == 0
                    ? "NO STRONGTECH"
                    : $"{region.FirstStrongIndex}/{region.LastStrongIndex}";
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0} | {1} | {2:F0}->{3:F0}ms | {4:F0}ms | {5}/{6}/{7}/{8} | {9}",
                    map.Reference.Key, map.Reference.HumanTruth,
                    region.StartTime, region.EndTime, region.FinalRegionDuration,
                    region.TotalWindows, region.StrongTechCount, region.SustainedTechCount,
                    region.TechSupportCount, anchors));
            }
        }

        text.AppendLine("STRONG-ANCHORED REGION AMPLIFICATION | map | strong span | final | amplified | ratio | support before/after | duration before/after | support distance avg/max");
        foreach (TechV2AnchorAmplificationRegion region in report.Maps.Where(map => map.Resolved)
            .SelectMany(map => map.Regions).Where(region => region.StrongTechCount > 0)
            .OrderBy(region => region.MapKey, StringComparer.Ordinal).ThenBy(region => region.StartTime))
        {
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  {0} | {1:F0}ms | {2:F0}ms | {3:F0}ms | {4:F2}x | {5}/{6} | {7:F0}/{8:F0}ms | {9:F2}/{10}",
                region.MapKey, region.StrongSpanDuration, region.FinalRegionDuration,
                region.AmplificationDuration, region.AmplificationRatio,
                region.SupportWindowsBeforeFirstStrong, region.SupportWindowsAfterLastStrong,
                region.DurationBeforeFirstStrong, region.DurationAfterLastStrong,
                region.AverageSupportDistanceInStrides, region.MaximumSupportDistanceInStrides));
        }

        AppendSupportFeatureComparison(text, report.Maps);
        AppendGroupSummary(text, "HUMAN TECH", report.Maps.Where(map => map.Resolved && map.Reference.HumanTruth == TechV2HumanTruth.Tech));
        AppendGroupSummary(text, "HUMAN NON-TECH", report.Maps.Where(map => map.Resolved && map.Reference.HumanTruth == TechV2HumanTruth.NonTech));
        AppendAnchorTypeSummary(text, report.Maps);
        AppendShapeSummary(text, report.Maps);

        text.AppendLine("OFFLINE POLICY SIMULATIONS | policy | group | presence before -> after | regions shortened");
        foreach (TechV2AnchorPolicyResult policy in report.PolicyResults)
        {
            foreach (TechV2AnchorPolicyGroupSummary group in policy.Groups)
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0} | {1} | {2:F2}% -> {3:F2}% | {4}",
                    policy.Policy.Name, group.HumanTruth, group.PresenceBeforePercent,
                    group.PresenceAfterPercent, group.AnchoredRegionsShortened));

            text.AppendLine($"  {policy.Policy.Name} per map | map | before -> after | shortened regions");
            foreach (TechV2AnchorPolicyMapResult map in policy.Maps)
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "    {0} | {1:F2}% -> {2:F2}% | {3}",
                    map.MapKey, map.PresenceBeforePercent, map.PresenceAfterPercent,
                    map.AnchoredRegionsShortened));
        }

        text.AppendLine("INTERPRETATION BOUNDARY:");
        text.AppendLine("  False StrongTech classification = a human NON-TECH map has a locally StrongTech window.");
        text.AppendLine("  Anchor amplification = the region expands beyond its StrongTech span through support absorption.");
        text.AppendLine("  This audit distinguishes the two; it does not alter either runtime rule.");
        text.AppendLine("POLICY E: not simulated unless close/distant support feature distributions provide a clear separation; this resolved corpus is reported without inventing X.");
        return text.ToString();
    }

    internal static TechV2AnchorAmplificationRegion AnalyzeRegionForTest(
        string mapKey,
        TechV2HumanTruth truth,
        TechV2Region region,
        IReadOnlyList<TechV2WindowDecision> decisions)
    {
        IReadOnlyList<TechV2WindowDecision> inRegion = decisions
            .Where(decision => decision.StartIndex >= region.StartWindowIndex
                && decision.EndIndex <= region.EndWindowIndex)
            .OrderBy(decision => decision.StartIndex).ToArray();
        IReadOnlyList<TechV2WindowDecision> strong = inRegion.Where(decision => decision.IsStrongTech).ToArray();
        IReadOnlyList<TechV2WindowDecision> support = inRegion.Where(decision => decision.IsTechSupport).ToArray();
        int? firstStrong = strong.Count == 0 ? null : strong.Min(decision => decision.StartIndex);
        int? lastStrong = strong.Count == 0 ? null : strong.Max(decision => decision.StartIndex);
        double strongSpanStart = strong.Count == 0 ? 0 : strong.Min(decision => decision.StartTime);
        double strongSpanEnd = strong.Count == 0 ? 0 : strong.Max(decision => decision.EndTime);
        IReadOnlyList<TechV2AnchorSupportWindow> supportWindows = support.Select(decision =>
        {
            int distance = strong.Count == 0 ? 0 : strong.Min(anchor =>
                Math.Abs(decision.StartIndex - anchor.StartIndex) / TechDetectorV2Experimental.Stride);
            return new TechV2AnchorSupportWindow(decision.StartIndex, decision.StartTime, decision.EndTime,
                distance, decision.GeometryEvidence, decision.MovementEvidence, decision.SliderEvidence,
                decision.MotifEvidence, 0, 0, "Unavailable");
        }).ToArray();

        return CreateRegion(mapKey, truth, region, inRegion, strong, support, supportWindows,
            firstStrong, lastStrong, strongSpanStart, strongSpanEnd);
    }

    private static TechV2AnchorAmplificationMap ResolveAndAnalyze(
        TechV2HumanReference reference,
        IReadOnlyList<MlDatasetSample> samples)
    {
        (string? path, string note) = TechV2HumanGroundedStrongTechAudit.ResolvePath(reference, samples);
        if (path is null)
            return TechV2AnchorAmplificationMap.Unresolved(reference, note);

        try
        {
            Beatmap map = BeatmapParser.Load(path);
            IReadOnlyList<TechV2ExperimentalWindowResult> windows = TechDetectorV2Experimental.Analyze(
                map.HitObjects, GameplayAnalyzer.IsSpinner, GameplayAnalyzer.IsSlider,
                GameplayAnalyzer.IsComplexSlider, GameplayAnalyzer.GetObjectStartPosition,
                GameplayAnalyzer.GetObjectEndPosition, GameplayAnalyzer.GetTechTurnAngle);
            IReadOnlyList<TechV2WindowDecision> baseDecisions = TechV2ExperimentalDecisionEngine.Decide(windows);
            IReadOnlyList<TechV2WindowDecision> decisions = TechV2ExperimentalDecisionEngine
                .PromoteSustainedTechWithDiagnostics(baseDecisions).Decisions;
            double playableStart = map.HitObjects.FirstOrDefault()?.Time ?? 0;
            double playableEnd = map.HitObjects.LastOrDefault()?.Time ?? 0;
            TechV2PresenceResult presence = TechV2ExperimentalDecisionEngine.BuildPresence(decisions, playableStart, playableEnd);
            IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> sources = windows.ToDictionary(window => window.StartIndex);
            IReadOnlyList<TechV2AnchorAmplificationRegion> regions = presence.Regions.Select(region =>
                AnalyzeRealRegion(reference.Key, reference.HumanTruth, region, decisions, sources, map.HitObjects)).ToArray();
            return new TechV2AnchorAmplificationMap(reference, path, note, true, playableStart, playableEnd,
                decisions, presence, regions);
        }
        catch (Exception exception) when (exception is IOException or FormatException or InvalidDataException)
        {
            return TechV2AnchorAmplificationMap.Unresolved(reference, $"parse failure: {exception.GetType().Name}");
        }
    }

    private static TechV2AnchorAmplificationRegion AnalyzeRealRegion(
        string mapKey,
        TechV2HumanTruth truth,
        TechV2Region region,
        IReadOnlyList<TechV2WindowDecision> decisions,
        IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> sources,
        IReadOnlyList<HitObject> objects)
    {
        IReadOnlyList<TechV2WindowDecision> inRegion = decisions
            .Where(decision => decision.StartIndex >= region.StartWindowIndex && decision.EndIndex <= region.EndWindowIndex)
            .OrderBy(decision => decision.StartIndex).ToArray();
        IReadOnlyList<TechV2WindowDecision> strong = inRegion.Where(decision => decision.IsStrongTech).ToArray();
        IReadOnlyList<TechV2WindowDecision> support = inRegion.Where(decision => decision.IsTechSupport).ToArray();
        int? firstStrong = strong.Count == 0 ? null : strong.Min(decision => decision.StartIndex);
        int? lastStrong = strong.Count == 0 ? null : strong.Max(decision => decision.StartIndex);
        double strongSpanStart = strong.Count == 0 ? 0 : strong.Min(decision => decision.StartTime);
        double strongSpanEnd = strong.Count == 0 ? 0 : strong.Max(decision => decision.EndTime);
        IReadOnlyList<TechV2AnchorSupportWindow> supportWindows = support.Select(decision =>
        {
            TechV2StrongTechWindowDetail detail = TechV2StrongTechForensicAudit.DescribeWindow(
                decision, sources[decision.StartIndex], objects);
            int distance = strong.Count == 0 ? 0 : strong.Min(anchor =>
                Math.Abs(decision.StartIndex - anchor.StartIndex) / TechDetectorV2Experimental.Stride);
            return new TechV2AnchorSupportWindow(decision.StartIndex, decision.StartTime, decision.EndTime,
                distance, decision.GeometryEvidence, decision.MovementEvidence, decision.SliderEvidence,
                decision.MotifEvidence, detail.Support.StructuralPivotCount,
                detail.Support.ValidTransitionCount, detail.SecondBestFamily);
        }).ToArray();
        return CreateRegion(mapKey, truth, region, inRegion, strong, support, supportWindows,
            firstStrong, lastStrong, strongSpanStart, strongSpanEnd);
    }

    private static TechV2AnchorAmplificationRegion CreateRegion(
        string mapKey, TechV2HumanTruth truth, TechV2Region region,
        IReadOnlyList<TechV2WindowDecision> inRegion,
        IReadOnlyList<TechV2WindowDecision> strong,
        IReadOnlyList<TechV2WindowDecision> support,
        IReadOnlyList<TechV2AnchorSupportWindow> supportWindows,
        int? firstStrong, int? lastStrong, double strongSpanStart, double strongSpanEnd)
    {
        double strongSpan = strong.Count == 0 ? 0 : strongSpanEnd - strongSpanStart;
        return new TechV2AnchorAmplificationRegion(
            mapKey, truth, region.StartTime, region.EndTime, region.Duration,
            inRegion.Count, strong.Count, inRegion.Count(decision => decision.IsSustainedTech), support.Count,
            firstStrong, lastStrong, strongSpan,
            strong.Count == 0 ? 0 : Math.Max(0, region.Duration - strongSpan),
            strong.Count == 0 || strongSpan <= 0 ? 0 : region.Duration / strongSpan,
            firstStrong is null ? 0 : support.Count(window => window.StartIndex < firstStrong),
            lastStrong is null ? 0 : support.Count(window => window.StartIndex > lastStrong),
            firstStrong is null ? 0 : Math.Max(0, strongSpanStart - region.StartTime),
            lastStrong is null ? 0 : Math.Max(0, region.EndTime - strongSpanEnd),
            supportWindows, region.StartWindowIndex, region.EndWindowIndex);
    }

    private static readonly IReadOnlyList<TechV2AnchorPolicy> OfflinePolicies =
    [
        new("A: single Strong max 1 support / side", TechV2AnchorPolicyKind.SingleOnePerSide),
        new("B: single Strong max 2 supports / side", TechV2AnchorPolicyKind.SingleTwoPerSide),
        new("C: long propagation only with >=2 Strong", TechV2AnchorPolicyKind.MultiStrongOnly),
        new("D: long propagation with >=2 Strong or Strong+Sustained", TechV2AnchorPolicyKind.MultiStrongOrSustained),
    ];

    private static TechV2AnchorPolicyResult SimulatePolicy(
        TechV2AnchorPolicy policy,
        IReadOnlyList<TechV2AnchorAmplificationMap> maps)
    {
        IReadOnlyList<TechV2AnchorPolicyMapResult> simulated = maps.Where(map => map.Resolved)
            .Select(map => SimulatePolicy(policy, map)).ToArray();
        IReadOnlyList<TechV2AnchorPolicyGroupSummary> groups = simulated.GroupBy(map => map.HumanTruth)
            .OrderBy(group => group.Key).Select(group => new TechV2AnchorPolicyGroupSummary(
                group.Key,
                ToAggregatePresence(group.Select(map => map.PresenceBefore).ToArray()),
                ToAggregatePresence(group.Select(map => map.PresenceAfter).ToArray()),
                group.Sum(map => map.AnchoredRegionsShortened))).ToArray();
        return new TechV2AnchorPolicyResult(policy, simulated, groups);
    }

    private static TechV2AnchorPolicyMapResult SimulatePolicy(
        TechV2AnchorPolicy policy,
        TechV2AnchorAmplificationMap map)
    {
        HashSet<int> retainedSupport = map.Decisions.Where(decision => decision.IsTechSupport)
            .Select(decision => decision.StartIndex).ToHashSet();
        int shortened = 0;
        foreach (TechV2AnchorAmplificationRegion region in map.Regions.Where(region => region.StrongTechCount > 0))
        {
            IReadOnlyList<TechV2AnchorSupportWindow> support = region.SupportWindows.OrderBy(window => window.StartIndex).ToArray();
            bool keepLong = policy.Kind switch
            {
                TechV2AnchorPolicyKind.SingleOnePerSide or TechV2AnchorPolicyKind.SingleTwoPerSide => region.StrongTechCount >= 2,
                TechV2AnchorPolicyKind.MultiStrongOnly => region.StrongTechCount >= 2,
                TechV2AnchorPolicyKind.MultiStrongOrSustained => region.StrongTechCount >= 2 || region.SustainedTechCount > 0,
                _ => false,
            };
            if (keepLong)
                continue;

            int cap = policy.Kind switch
            {
                TechV2AnchorPolicyKind.SingleOnePerSide => 1,
                TechV2AnchorPolicyKind.SingleTwoPerSide => 2,
                _ => 0,
            };
            IReadOnlyList<TechV2AnchorSupportWindow> before = support
                .Where(window => region.FirstStrongIndex is not null && window.StartIndex < region.FirstStrongIndex)
                .OrderByDescending(window => window.StartIndex).Take(cap).ToArray();
            IReadOnlyList<TechV2AnchorSupportWindow> after = support
                .Where(window => region.LastStrongIndex is not null && window.StartIndex > region.LastStrongIndex)
                .OrderBy(window => window.StartIndex).Take(cap).ToArray();
            HashSet<int> keep = before.Concat(after).Select(window => window.StartIndex).ToHashSet();
            foreach (TechV2AnchorSupportWindow window in support.Where(window => !keep.Contains(window.StartIndex)))
                retainedSupport.Remove(window.StartIndex);
            if (support.Count > keep.Count)
                shortened++;
        }

        IReadOnlyList<TechV2WindowDecision> copied = map.Decisions.Select(decision =>
            decision.IsTechSupport && !retainedSupport.Contains(decision.StartIndex)
                ? decision with { IsModerateSupport = false, IsSustainedTech = false }
                : decision).ToArray();
        TechV2PresenceResult presence = TechV2ExperimentalDecisionEngine.BuildPresence(
            copied, map.PlayableStartTime, map.PlayableEndTime);
        return new TechV2AnchorPolicyMapResult(map.Reference.Key, map.Reference.HumanTruth,
            map.Presence, presence, shortened);
    }

    private static double ToAggregatePresence(IReadOnlyList<TechV2PresenceResult> results)
    {
        double playable = results.Sum(result => result.PlayableDuration);
        return playable <= 0 ? 0 : Math.Clamp(results.Sum(result => result.TotalTechDuration) / playable * 100, 0, 100);
    }

    private static void AppendSupportFeatureComparison(StringBuilder text, IReadOnlyList<TechV2AnchorAmplificationMap> maps)
    {
        text.AppendLine("ABSORBED SUPPORT FEATURES (strong-anchored regions) | group/distance | windows | avg G/M/S/Mo | avg pivots/transitions | second-best families");
        foreach (TechV2HumanTruth truth in Enum.GetValues<TechV2HumanTruth>())
        {
            IReadOnlyList<TechV2AnchorSupportWindow> support = maps.Where(map => map.Resolved && map.Reference.HumanTruth == truth)
                .SelectMany(map => map.Regions).Where(region => region.StrongTechCount > 0)
                .SelectMany(region => region.SupportWindows).ToArray();
            foreach ((string name, Func<TechV2AnchorSupportWindow, bool> predicate) in new[]
            {
                ("close <=1 stride", (Func<TechV2AnchorSupportWindow, bool>)(window => window.DistanceInStrides <= 1)),
                ("distant >=2 strides", (Func<TechV2AnchorSupportWindow, bool>)(window => window.DistanceInStrides >= 2)),
            })
            {
                IReadOnlyList<TechV2AnchorSupportWindow> bucket = support.Where(predicate).ToArray();
                string families = bucket.Count == 0 ? "None" : string.Join(", ", bucket.GroupBy(window => window.SecondBestFamily)
                    .OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => $"{group.Key}={group.Count()}"));
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0} / {1} | {2} | {3:F3}/{4:F3}/{5:F3}/{6:F3} | {7:F2}/{8:F2} | {9}",
                    truth, name, bucket.Count,
                    bucket.Count == 0 ? 0 : bucket.Average(window => window.GeometryEvidence),
                    bucket.Count == 0 ? 0 : bucket.Average(window => window.MovementEvidence),
                    bucket.Count == 0 ? 0 : bucket.Average(window => window.SliderEvidence),
                    bucket.Count == 0 ? 0 : bucket.Average(window => window.MotifEvidence),
                    bucket.Count == 0 ? 0 : bucket.Average(window => window.StructuralPivotCount),
                    bucket.Count == 0 ? 0 : bucket.Average(window => window.ValidTransitionCount), families));
            }
        }
    }

    private static void AppendGroupSummary(StringBuilder text, string title, IEnumerable<TechV2AnchorAmplificationMap> source)
    {
        IReadOnlyList<TechV2AnchorAmplificationRegion> regions = source.SelectMany(map => map.Regions)
            .Where(region => region.StrongTechCount > 0).ToArray();
        text.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "{0} STRONG-ANCHORED SUMMARY | regions={1} | avg strong/region={2:F2} | avg final={3:F0}ms | avg strong span={4:F0}ms | avg amplified={5:F0}ms | avg ratio={6:F2}x | avg supports={7:F2} | avg max support distance={8:F2}",
            title, regions.Count,
            regions.Count == 0 ? 0 : regions.Average(region => region.StrongTechCount),
            regions.Count == 0 ? 0 : regions.Average(region => region.FinalRegionDuration),
            regions.Count == 0 ? 0 : regions.Average(region => region.StrongSpanDuration),
            regions.Count == 0 ? 0 : regions.Average(region => region.AmplificationDuration),
            regions.Count == 0 ? 0 : regions.Average(region => region.AmplificationRatio),
            regions.Count == 0 ? 0 : regions.Average(region => region.TechSupportCount),
            regions.Count == 0 ? 0 : regions.Average(region => region.MaximumSupportDistanceInStrides)));
    }

    private static void AppendAnchorTypeSummary(StringBuilder text, IReadOnlyList<TechV2AnchorAmplificationMap> maps)
    {
        text.AppendLine("SINGLE VS MULTI STRONG | group/type | regions | avg duration | avg amplification ratio | avg supports | avg max support distance");
        foreach (TechV2HumanTruth truth in Enum.GetValues<TechV2HumanTruth>())
        foreach ((string name, Func<TechV2AnchorAmplificationRegion, bool> predicate) in new[]
        {
            ("single", (Func<TechV2AnchorAmplificationRegion, bool>)(region => region.StrongTechCount == 1)),
            ("multi", (Func<TechV2AnchorAmplificationRegion, bool>)(region => region.StrongTechCount >= 2)),
        })
        {
            IReadOnlyList<TechV2AnchorAmplificationRegion> regions = maps.Where(map => map.Resolved && map.Reference.HumanTruth == truth)
                .SelectMany(map => map.Regions).Where(predicate).ToArray();
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  {0}/{1} | {2} | {3:F0}ms | {4:F2}x | {5:F2} | {6:F2}", truth, name, regions.Count,
                regions.Count == 0 ? 0 : regions.Average(region => region.FinalRegionDuration),
                regions.Count == 0 ? 0 : regions.Average(region => region.AmplificationRatio),
                regions.Count == 0 ? 0 : regions.Average(region => region.TechSupportCount),
                regions.Count == 0 ? 0 : regions.Average(region => region.MaximumSupportDistanceInStrides)));
        }
    }

    private static void AppendShapeSummary(StringBuilder text, IReadOnlyList<TechV2AnchorAmplificationMap> maps)
    {
        text.AppendLine("STRONG ONLY VS STRONG+SUSTAINED | group/shape | regions | avg duration | avg ratio | avg supports");
        foreach (TechV2HumanTruth truth in Enum.GetValues<TechV2HumanTruth>())
        foreach ((string name, Func<TechV2AnchorAmplificationRegion, bool> predicate) in new[]
        {
            ("strong-only", (Func<TechV2AnchorAmplificationRegion, bool>)(region => region.StrongTechCount > 0 && region.SustainedTechCount == 0)),
            ("strong-plus-sustained", (Func<TechV2AnchorAmplificationRegion, bool>)(region => region.StrongTechCount > 0 && region.SustainedTechCount > 0)),
        })
        {
            IReadOnlyList<TechV2AnchorAmplificationRegion> regions = maps.Where(map => map.Resolved && map.Reference.HumanTruth == truth)
                .SelectMany(map => map.Regions).Where(predicate).ToArray();
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  {0}/{1} | {2} | {3:F0}ms | {4:F2}x | {5:F2}", truth, name, regions.Count,
                regions.Count == 0 ? 0 : regions.Average(region => region.FinalRegionDuration),
                regions.Count == 0 ? 0 : regions.Average(region => region.AmplificationRatio),
                regions.Count == 0 ? 0 : regions.Average(region => region.TechSupportCount)));
        }
    }
}

internal enum TechV2AnchorPolicyKind { SingleOnePerSide, SingleTwoPerSide, MultiStrongOnly, MultiStrongOrSustained }
internal sealed record TechV2AnchorPolicy(string Name, TechV2AnchorPolicyKind Kind);
internal sealed record TechV2AnchorSupportWindow(int StartIndex, double StartTime, double EndTime, int DistanceInStrides, double GeometryEvidence, double MovementEvidence, double SliderEvidence, double MotifEvidence, int StructuralPivotCount, int ValidTransitionCount, string SecondBestFamily);
internal sealed record TechV2AnchorAmplificationRegion(string MapKey, TechV2HumanTruth HumanTruth, double StartTime, double EndTime, double FinalRegionDuration, int TotalWindows, int StrongTechCount, int SustainedTechCount, int TechSupportCount, int? FirstStrongIndex, int? LastStrongIndex, double StrongSpanDuration, double AmplificationDuration, double AmplificationRatio, int SupportWindowsBeforeFirstStrong, int SupportWindowsAfterLastStrong, double DurationBeforeFirstStrong, double DurationAfterLastStrong, IReadOnlyList<TechV2AnchorSupportWindow> SupportWindows, int StartWindowIndex, int EndWindowIndex)
{
    internal double AverageSupportDistanceInStrides => SupportWindows.Count == 0 ? 0 : SupportWindows.Average(window => window.DistanceInStrides);
    internal int MaximumSupportDistanceInStrides => SupportWindows.Count == 0 ? 0 : SupportWindows.Max(window => window.DistanceInStrides);
}
internal sealed record TechV2AnchorAmplificationMap(TechV2HumanReference Reference, string? Path, string ResolutionNote, bool Resolved, double PlayableStartTime, double PlayableEndTime, IReadOnlyList<TechV2WindowDecision> Decisions, TechV2PresenceResult Presence, IReadOnlyList<TechV2AnchorAmplificationRegion> Regions)
{
    internal static TechV2AnchorAmplificationMap Unresolved(TechV2HumanReference reference, string note) => new(reference, null, note, false, 0, 0, [], new([], 0, 0, 0, 0, 0, 0), []);
}
internal sealed record TechV2AnchorPolicyMapResult(string MapKey, TechV2HumanTruth HumanTruth, TechV2PresenceResult PresenceBefore, TechV2PresenceResult PresenceAfter, int AnchoredRegionsShortened)
{
    internal double PresenceBeforePercent => PresenceBefore.PresencePercent;
    internal double PresenceAfterPercent => PresenceAfter.PresencePercent;
}
internal sealed record TechV2AnchorPolicyGroupSummary(TechV2HumanTruth HumanTruth, double PresenceBeforePercent, double PresenceAfterPercent, int AnchoredRegionsShortened);
internal sealed record TechV2AnchorPolicyResult(TechV2AnchorPolicy Policy, IReadOnlyList<TechV2AnchorPolicyMapResult> Maps, IReadOnlyList<TechV2AnchorPolicyGroupSummary> Groups);
internal sealed record TechV2AnchorAmplificationReport(IReadOnlyList<TechV2AnchorAmplificationMap> Maps, IReadOnlyList<TechV2AnchorPolicyResult> PolicyResults);
