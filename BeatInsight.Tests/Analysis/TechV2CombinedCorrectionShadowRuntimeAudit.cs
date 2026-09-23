using BeatInsight.Analysis;
using BeatInsight.Models;
using BeatInsight.Models.Persistence;
using BeatInsight.Parser;
using BeatInsight.Services.Persistence;
using System.Globalization;
using System.IO;
using System.Text;

namespace BeatInsight.Tests.Analysis;

internal enum TechV2Phase222Recommendation
{
    SAFE_TO_ENABLE,
    NEEDS_MORE_CALIBRATION,
    REJECT,
}

internal sealed record TechV2Phase222HumanRegionShadow(
    string MapKey,
    int RegionNumber,
    string HumanLabel,
    double CurrentValue,
    double ShadowCorrectedValue,
    double Delta);

internal sealed record TechV2Phase222DeltaDistribution(
    int Count,
    double AverageDelta,
    double MedianDelta,
    double MinDelta,
    double MaxDelta,
    double PercentageIncreased,
    double PercentageDecreased,
    double PercentageUnchanged);

internal sealed record TechV2Phase222ExtremeDelta(
    string Map,
    string Difficulty,
    double Timestamp,
    double Current,
    double Corrected,
    double Delta,
    TechV2ShadowSupportCounts SupportCounts);

internal sealed record TechV2Phase222DenominatorExample(
    string Map,
    string Difficulty,
    string Ratio,
    int Numerator,
    int Denominator,
    double Before,
    double After);

internal sealed record TechV2Phase222ContaminationFinding(
    string ReviewId,
    double Current,
    double PrevCurr,
    double Delta,
    string Assessment);

internal sealed record TechV2Phase222DecisionInvarianceCheck(
    bool AllDecisionKindUnchanged,
    bool AllStrongTechCountsUnchanged,
    bool AllTechSupportCountsUnchanged,
    bool AllSustainedTechCountsUnchanged,
    bool AllPresenceValuesUnchanged,
    int MapsChecked,
    int WindowsChecked);

internal sealed record TechV2Phase222AuditReport(
    IReadOnlyList<TechV2Phase222HumanRegionShadow> HumanCorpusShadowResults,
    TechV2Phase222DeltaDistribution ThousandMapDeltaDistribution,
    IReadOnlyList<TechV2Phase222ExtremeDelta> ExtremePositiveDeltas,
    IReadOnlyList<TechV2Phase222ExtremeDelta> ExtremeNegativeDeltas,
    IReadOnlyList<TechV2Phase222DenominatorExample> DenominatorOneBehavior,
    IReadOnlyList<TechV2Phase222ContaminationFinding> LocalContextContaminationFindings,
    TechV2Phase222DecisionInvarianceCheck DecisionInvarianceCheck,
    TechV2Phase222Recommendation Recommendation);

internal static class TechV2CombinedCorrectionShadowRuntimeAudit
{
    private const double Epsilon = 1e-6;

    private static readonly string[] RequestedHumanKeys =
    [
        "Exit",
        "Kira",
        "Frozen",
        "WorldEndsNow",
        "GloriousDays",
        "Inferno",
        "Howling",
        "TaoHuaXiao",
        "RockOnExpert",
        "TrustInYou",
        "HappppySong",
        "RockOnSeAqours",
        "FinestSky",
    ];

    internal static TechV2Phase222AuditReport Run(string databasePath)
    {
        IReadOnlyList<MlDatasetSample> samples = TechV2StrongTechForensicAudit.LoadLocalCacheSamples(databasePath);
        TechV2HumanGroundedAuditReport grounded = TechV2HumanGroundedStrongTechAudit.Run(databasePath);

        List<TechV2Phase222HumanRegionShadow> humanRegions = [];
        List<WindowWithMap> allWindows = [];

        bool allDecisionKindUnchanged = true;
        bool allStrongUnchanged = true;
        bool allSupportUnchanged = true;
        bool allSustainedUnchanged = true;
        bool allPresenceUnchanged = true;
        int mapsChecked = 0;
        int windowsChecked = 0;

        foreach (TechV2HumanGroundedMap map in grounded.Maps
            .Where(item => item.Resolved && RequestedHumanKeys.Contains(item.Reference.Key, StringComparer.Ordinal))
            .OrderBy(item => item.Reference.Key, StringComparer.Ordinal))
        {
            if (map.Path is null)
                continue;

            Beatmap beatmap;
            try
            {
                beatmap = BeatmapParser.Load(map.Path);
            }
            catch (Exception exception) when (exception is IOException or FormatException or InvalidDataException)
            {
                continue;
            }

            IReadOnlyList<TechV2ExperimentalWindowResult> windows = TechDetectorV2Experimental.Analyze(
                beatmap.HitObjects,
                GameplayAnalyzer.IsSpinner,
                GameplayAnalyzer.IsSlider,
                GameplayAnalyzer.IsComplexSlider,
                GameplayAnalyzer.GetObjectStartPosition,
                GameplayAnalyzer.GetObjectEndPosition,
                GameplayAnalyzer.GetTechTurnAngle);
            IReadOnlyList<TechV2WindowDecision> decisions = TechV2ExperimentalDecisionEngine.PromoteSustainedTech(
                TechV2ExperimentalDecisionEngine.Decide(windows));

            TechV2CombinedCorrectionShadowResult shadow = TechV2CombinedCorrectionShadowRuntime.Compute(
                beatmap.HitObjects,
                windows,
                decisions);

            mapsChecked++;
            windowsChecked += shadow.Windows.Count;

            allDecisionKindUnchanged &= shadow.ChangedDecisionKindCount == 0;
            allStrongUnchanged &= map.Decisions.Count(item => item.IsStrongTech) == decisions.Count(item => item.IsStrongTech);
            allSupportUnchanged &= map.Decisions.Count(item => item.IsTechSupport) == decisions.Count(item => item.IsTechSupport);
            allSustainedUnchanged &= map.Decisions.Count(item => item.IsSustainedTech) == decisions.Count(item => item.IsSustainedTech);

            double playableStartTime = beatmap.HitObjects.Count > 0 ? beatmap.HitObjects[0].Time : 0;
            double playableEndTime = beatmap.HitObjects.Count > 0 ? beatmap.HitObjects[^1].Time : 0;
            TechV2PresenceResult presenceAfter = TechV2ExperimentalDecisionEngine.BuildPresence(
                decisions,
                playableStartTime,
                playableEndTime);
            allPresenceUnchanged &= PresenceEquals(map.Presence, presenceAfter);

            IReadOnlyList<TechV2ShadowWindowDiagnostics> orderedWindows = shadow.Windows
                .OrderBy(item => item.StartIndex)
                .ToArray();

            for (int index = 0; index < map.Presence.Regions.Count; index++)
            {
                TechV2Region region = map.Presence.Regions[index];
                IReadOnlyList<TechV2ShadowWindowDiagnostics> selected = orderedWindows
                    .Where(window =>
                        window.StartIndex >= region.StartWindowIndex
                        && window.EndIndex <= region.EndWindowIndex)
                    .ToArray();

                if (selected.Count == 0)
                    continue;

                double current = selected.Average(window => window.CurrentCombinedValue);
                double corrected = selected.Average(window => window.CorrectedCombinedValue);
                humanRegions.Add(new TechV2Phase222HumanRegionShadow(
                    map.Reference.Key,
                    index + 1,
                    map.Reference.HumanTruth.ToString(),
                    current,
                    corrected,
                    corrected - current));
            }

            allWindows.AddRange(orderedWindows.Select(window =>
                new WindowWithMap(map.Reference.Key, beatmap.Version, window)));
        }

        IReadOnlyList<WindowWithMap> localSampleWindows = ScanDeterministicLocalSample(samples, maxMaps: 1000);
        TechV2Phase222DeltaDistribution distribution = BuildDistribution(localSampleWindows);
        IReadOnlyList<TechV2Phase222ExtremeDelta> strongestIncrease = localSampleWindows
            .OrderByDescending(item => item.Window.Delta)
            .ThenBy(item => item.MapKey, StringComparer.Ordinal)
            .Take(10)
            .Select(ToExtreme)
            .ToArray();
        IReadOnlyList<TechV2Phase222ExtremeDelta> strongestDecrease = localSampleWindows
            .OrderBy(item => item.Window.Delta)
            .ThenBy(item => item.MapKey, StringComparer.Ordinal)
            .Take(10)
            .Select(ToExtreme)
            .ToArray();

        IReadOnlyList<TechV2Phase222DenominatorExample> denominatorOne = localSampleWindows
            .SelectMany(item => item.Window.Ratios.Select(ratio => (item, ratio)))
            .Where(tuple => tuple.ratio.Denominator == 1)
            .OrderByDescending(tuple => Math.Abs(tuple.ratio.SupportAware - tuple.ratio.Raw))
            .ThenBy(tuple => tuple.item.MapKey, StringComparer.Ordinal)
            .Take(8)
            .Select(tuple => new TechV2Phase222DenominatorExample(
                tuple.item.MapKey,
                tuple.item.Difficulty,
                tuple.ratio.Name,
                tuple.ratio.Numerator,
                tuple.ratio.Denominator,
                tuple.ratio.Raw,
                tuple.ratio.SupportAware))
            .ToArray();

        IReadOnlyList<TechV2Phase222ContaminationFinding> contamination = BuildContaminationFindings(databasePath);

        TechV2Phase222DecisionInvarianceCheck invariance = new(
            allDecisionKindUnchanged,
            allStrongUnchanged,
            allSupportUnchanged,
            allSustainedUnchanged,
            allPresenceUnchanged,
            mapsChecked,
            windowsChecked);

        TechV2Phase222Recommendation recommendation = Recommend(
            invariance,
            humanRegions,
            contamination,
            strongestIncrease,
            strongestDecrease);

        return new TechV2Phase222AuditReport(
            humanRegions
                .OrderBy(item => item.MapKey, StringComparer.Ordinal)
                .ThenBy(item => item.RegionNumber)
                .ToArray(),
            distribution,
            strongestIncrease,
            strongestDecrease,
            denominatorOne,
            contamination,
            invariance,
            recommendation);
    }

    internal static string FormatReport(TechV2Phase222AuditReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        StringBuilder text = new();
        text.AppendLine("===== TECH V2 PHASE 2.22 COMBINED CORRECTION SHADOW RUNTIME (TEST-ONLY) =====");
        text.AppendLine("SHADOW-ONLY: SA-LAPLACE-A1-P0.5 + LA-PREV+CURR-SIMPLE calculés en parallèle.");
        text.AppendLine("Aucun changement de décision, guard, Policy D ou présence.");

        text.AppendLine("HUMAN CORPUS SHADOW RESULTS");
        foreach (TechV2Phase222HumanRegionShadow region in report.HumanCorpusShadowResults)
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  {0} R{1} | label={2} | current={3:F4} | corrected={4:F4} | delta={5:+0.0000;-0.0000;0.0000}",
                region.MapKey,
                region.RegionNumber,
                region.HumanLabel,
                region.CurrentValue,
                region.ShadowCorrectedValue,
                region.Delta));
        }

        text.AppendLine("1000-MAP DELTA DISTRIBUTION");
        text.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "  count={0} | avg={1:+0.0000;-0.0000;0.0000} | median={2:+0.0000;-0.0000;0.0000} | min={3:+0.0000;-0.0000;0.0000} | max={4:+0.0000;-0.0000;0.0000} | increased={5:F2}% | decreased={6:F2}% | unchanged={7:F2}%",
            report.ThousandMapDeltaDistribution.Count,
            report.ThousandMapDeltaDistribution.AverageDelta,
            report.ThousandMapDeltaDistribution.MedianDelta,
            report.ThousandMapDeltaDistribution.MinDelta,
            report.ThousandMapDeltaDistribution.MaxDelta,
            report.ThousandMapDeltaDistribution.PercentageIncreased,
            report.ThousandMapDeltaDistribution.PercentageDecreased,
            report.ThousandMapDeltaDistribution.PercentageUnchanged));

        text.AppendLine("EXTREME POSITIVE DELTAS");
        foreach (TechV2Phase222ExtremeDelta item in report.ExtremePositiveDeltas)
            AppendExtreme(text, item);

        text.AppendLine("EXTREME NEGATIVE DELTAS");
        foreach (TechV2Phase222ExtremeDelta item in report.ExtremeNegativeDeltas)
            AppendExtreme(text, item);

        text.AppendLine("DENOMINATOR=1 BEHAVIOR");
        foreach (TechV2Phase222DenominatorExample item in report.DenominatorOneBehavior)
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  {0} [{1}] {2} {3}/{4} | before={5:F4} | after={6:F4} | delta={7:+0.0000;-0.0000;0.0000}",
                item.Map,
                item.Difficulty,
                item.Ratio,
                item.Numerator,
                item.Denominator,
                item.Before,
                item.After,
                item.After - item.Before));
        }

        text.AppendLine("LOCAL-CONTEXT CONTAMINATION FINDINGS");
        foreach (TechV2Phase222ContaminationFinding finding in report.LocalContextContaminationFindings)
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  {0} | current={1:F4} | prev+curr={2:F4} | delta={3:+0.0000;-0.0000;0.0000} | {4}",
                finding.ReviewId,
                finding.Current,
                finding.PrevCurr,
                finding.Delta,
                finding.Assessment));
        }

        TechV2Phase222DecisionInvarianceCheck invariance = report.DecisionInvarianceCheck;
        text.AppendLine("DECISION INVARIANCE CHECK");
        text.AppendLine($"  maps={invariance.MapsChecked} windows={invariance.WindowsChecked}");
        text.AppendLine($"  all DecisionKind unchanged = {invariance.AllDecisionKindUnchanged}");
        text.AppendLine($"  all StrongTech counts unchanged = {invariance.AllStrongTechCountsUnchanged}");
        text.AppendLine($"  all TechSupport counts unchanged = {invariance.AllTechSupportCountsUnchanged}");
        text.AppendLine($"  all SustainedTech counts unchanged = {invariance.AllSustainedTechCountsUnchanged}");
        text.AppendLine($"  all Presence values unchanged = {invariance.AllPresenceValuesUnchanged}");

        text.AppendLine($"RECOMMENDATION: {report.Recommendation}");

        return text.ToString();
    }

    private static IReadOnlyList<WindowWithMap> ScanDeterministicLocalSample(
        IReadOnlyList<MlDatasetSample> samples,
        int maxMaps)
    {
        IReadOnlyList<string> paths = samples
            .Select(sample => sample.SourceFilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Take(Math.Max(0, maxMaps))
            .ToArray();

        List<WindowWithMap> all = [];
        foreach (string path in paths)
        {
            Beatmap beatmap;
            try
            {
                beatmap = BeatmapParser.Load(path);
            }
            catch (Exception exception) when (exception is IOException or FormatException or InvalidDataException)
            {
                continue;
            }

            if (beatmap.HitObjects.Count < TechDetectorV2Experimental.WindowSize)
                continue;

            IReadOnlyList<TechV2ExperimentalWindowResult> windows = TechDetectorV2Experimental.Analyze(
                beatmap.HitObjects,
                GameplayAnalyzer.IsSpinner,
                GameplayAnalyzer.IsSlider,
                GameplayAnalyzer.IsComplexSlider,
                GameplayAnalyzer.GetObjectStartPosition,
                GameplayAnalyzer.GetObjectEndPosition,
                GameplayAnalyzer.GetTechTurnAngle);
            IReadOnlyList<TechV2WindowDecision> decisions = TechV2ExperimentalDecisionEngine.PromoteSustainedTech(
                TechV2ExperimentalDecisionEngine.Decide(windows));

            TechV2CombinedCorrectionShadowResult shadow = TechV2CombinedCorrectionShadowRuntime.Compute(
                beatmap.HitObjects,
                windows,
                decisions);

            string mapKey = string.IsNullOrWhiteSpace(beatmap.Title)
                ? Path.GetFileNameWithoutExtension(path)
                : beatmap.Title;
            string difficulty = string.IsNullOrWhiteSpace(beatmap.Version)
                ? "Unknown"
                : beatmap.Version;

            all.AddRange(shadow.Windows.Select(window =>
                new WindowWithMap(mapKey, difficulty, window)));
        }

        return all;
    }

    private static TechV2Phase222DeltaDistribution BuildDistribution(
        IReadOnlyList<WindowWithMap> windows)
    {
        IReadOnlyList<double> deltas = windows
            .Select(item => item.Window.Delta)
            .OrderBy(delta => delta)
            .ToArray();

        if (deltas.Count == 0)
            return new TechV2Phase222DeltaDistribution(0, 0, 0, 0, 0, 0, 0, 100);

        int increased = deltas.Count(delta => delta > Epsilon);
        int decreased = deltas.Count(delta => delta < -Epsilon);
        int unchanged = deltas.Count - increased - decreased;

        return new TechV2Phase222DeltaDistribution(
            deltas.Count,
            deltas.Average(),
            Median(deltas),
            deltas[0],
            deltas[^1],
            100.0 * increased / deltas.Count,
            100.0 * decreased / deltas.Count,
            100.0 * unchanged / deltas.Count);
    }

    private static IReadOnlyList<TechV2Phase222ContaminationFinding> BuildContaminationFindings(string databasePath)
    {
        TechV2ExpandedLocalGroundTruthReport report = TechV2ExpandedLocalGroundTruthEvaluation.Run(databasePath);

        return report.LocalizationNotes
            .Where(note => note.ReviewId is "R16" or "R23")
            .Select(note =>
            {
                TechV2LocalizedWindow? currentWindow = note.Windows.FirstOrDefault();
                if (currentWindow is null || note.AdjacentBefore is null)
                {
                    return new TechV2Phase222ContaminationFinding(
                        note.ReviewId,
                        0,
                        0,
                        0,
                        "insufficient localized context");
                }

                double current = Mean([
                    currentWindow.Geometry,
                    currentWindow.Movement,
                    currentWindow.Slider,
                    currentWindow.Motif,
                    currentWindow.StructuralRatio,
                    currentWindow.SharpRatio,
                    currentWindow.SpacingVariation,
                ]);
                double previous = Mean([
                    note.AdjacentBefore.Geometry,
                    note.AdjacentBefore.Movement,
                    note.AdjacentBefore.Slider,
                    note.AdjacentBefore.Motif,
                    note.AdjacentBefore.StructuralRatio,
                    note.AdjacentBefore.SharpRatio,
                    note.AdjacentBefore.SpacingVariation,
                ]);
                double prevCurr = (previous + current) / 2.0;
                double delta = prevCurr - current;

                string assessment = Math.Abs(delta) > 0.05
                    ? "possible contamination"
                    : "no excessive contamination observed";

                return new TechV2Phase222ContaminationFinding(
                    note.ReviewId,
                    current,
                    prevCurr,
                    delta,
                    assessment);
            })
            .OrderBy(item => item.ReviewId, StringComparer.Ordinal)
            .ToArray();
    }

    private static TechV2Phase222Recommendation Recommend(
        TechV2Phase222DecisionInvarianceCheck invariance,
        IReadOnlyList<TechV2Phase222HumanRegionShadow> human,
        IReadOnlyList<TechV2Phase222ContaminationFinding> contamination,
        IReadOnlyList<TechV2Phase222ExtremeDelta> positive,
        IReadOnlyList<TechV2Phase222ExtremeDelta> negative)
    {
        if (!invariance.AllDecisionKindUnchanged
            || !invariance.AllStrongTechCountsUnchanged
            || !invariance.AllTechSupportCountsUnchanged
            || !invariance.AllSustainedTechCountsUnchanged
            || !invariance.AllPresenceValuesUnchanged)
        {
            return TechV2Phase222Recommendation.REJECT;
        }

        double techMean = Mean(human.Where(item => item.HumanLabel == "Tech").Select(item => item.Delta));
        double nonTechMean = Mean(human.Where(item => item.HumanLabel == "NonTech").Select(item => item.Delta));
        bool contaminationRisk = contamination.Any(item =>
            string.Equals(item.Assessment, "possible contamination", StringComparison.Ordinal));
        double extremeAbs = Math.Max(
            positive.Count == 0 ? 0 : Math.Abs(positive.Max(item => item.Delta)),
            negative.Count == 0 ? 0 : Math.Abs(negative.Min(item => item.Delta)));

        if (contaminationRisk || extremeAbs > 0.15)
            return TechV2Phase222Recommendation.NEEDS_MORE_CALIBRATION;

        return techMean >= 0 && nonTechMean <= 0
            ? TechV2Phase222Recommendation.SAFE_TO_ENABLE
            : TechV2Phase222Recommendation.NEEDS_MORE_CALIBRATION;
    }

    private static bool PresenceEquals(TechV2PresenceResult first, TechV2PresenceResult second)
    {
        return first.Regions.Count == second.Regions.Count
            && first.WindowCount == second.WindowCount
            && first.TechWindowCount == second.TechWindowCount
            && Math.Abs(first.TotalTechDuration - second.TotalTechDuration) <= Epsilon
            && Math.Abs(first.PlayableDuration - second.PlayableDuration) <= Epsilon
            && Math.Abs(first.PresenceRatio - second.PresenceRatio) <= Epsilon
            && Math.Abs(first.PresencePercent - second.PresencePercent) <= Epsilon;
    }

    private static TechV2Phase222ExtremeDelta ToExtreme(WindowWithMap item) =>
        new(
            item.MapKey,
            item.Difficulty,
            item.Window.StartTime,
            item.Window.CurrentCombinedValue,
            item.Window.CorrectedCombinedValue,
            item.Window.Delta,
            item.Window.SupportCounts);

    private static void AppendExtreme(StringBuilder text, TechV2Phase222ExtremeDelta item)
    {
        text.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "  {0} [{1}] {2} | current={3:F4} | corrected={4:F4} | delta={5:+0.0000;-0.0000;0.0000} | support(valid={6},struct={7},sharp={8},spacing={9},alt={10},slider={11},complex={12},topo={13},s2s={14})",
            item.Map,
            item.Difficulty,
            FormatTime(item.Timestamp),
            item.Current,
            item.Corrected,
            item.Delta,
            item.SupportCounts.ValidTransitions,
            item.SupportCounts.StructuralCount,
            item.SupportCounts.SharpCount,
            item.SupportCounts.SpacingVariationCount,
            item.SupportCounts.AlternatingCount,
            item.SupportCounts.SliderCount,
            item.SupportCounts.ComplexSliderCount,
            item.SupportCounts.TopologyTransitionCount,
            item.SupportCounts.SliderToSliderTransitionCount));
    }

    private static double Median(IReadOnlyList<double> sorted)
    {
        if (sorted.Count == 0)
            return 0;

        int middle = sorted.Count / 2;
        return sorted.Count % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2.0;
    }

    private static double Mean(IEnumerable<double> values)
    {
        double[] items = values.ToArray();
        return items.Length == 0 ? 0 : items.Average();
    }

    private static string FormatTime(double milliseconds)
    {
        TimeSpan value = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:D2}:{1:D2}.{2:D3}",
            (int)value.TotalMinutes,
            value.Seconds,
            value.Milliseconds);
    }

    private sealed record WindowWithMap(
        string MapKey,
        string Difficulty,
        TechV2ShadowWindowDiagnostics Window);
}
