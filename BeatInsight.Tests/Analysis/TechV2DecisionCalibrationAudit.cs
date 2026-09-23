using BeatInsight.Analysis;
using System.Globalization;
using System.Text;

namespace BeatInsight.Tests.Analysis;

internal enum TechV2DecisionFailureReason
{
    PASS_TWO_STRONG,
    PASS_EXCEPTIONAL_PLUS_MODERATE,
    FAIL_ONLY_ONE_STRONG,
    FAIL_SECOND_FAMILY_TOO_LOW,
    FAIL_NO_STRONG_FAMILY,
}

internal sealed record TechV2RankedEvidenceFamily(
    string Name,
    double Value,
    int StableOrder);

internal sealed record TechV2DecisionCalibrationWindow(
    TechV2WindowDecision Decision,
    IReadOnlyList<TechV2RankedEvidenceFamily> RankedFamilies,
    int StrongFamilyCount,
    bool HasExceptionalFamily,
    bool HasModerateSecondFamily,
    bool SatisfiesExceptionalPlusModerate,
    TechV2DecisionFailureReason FailureReason,
    bool IsNearMiss)
{
    internal TechV2RankedEvidenceFamily BestFamily => RankedFamilies[0];
    internal TechV2RankedEvidenceFamily SecondBestFamily => RankedFamilies[1];
    internal string TopTwoPair => string.Join(
        " + ",
        RankedFamilies
            .Take(2)
            .OrderBy(family => family.StableOrder)
            .Select(family => family.Name));
}

internal sealed record TechV2NearMissRun(
    int WindowCount,
    int StartIndex,
    int EndIndex,
    double StartTime,
    double EndTime);

internal sealed record TechV2DecisionCalibrationAuditResult(
    string MapName,
    int TotalWindows,
    int TechWindows,
    int NonTechWindows,
    double PresencePercent,
    double AverageGeometry,
    double AverageMovement,
    double AverageSlider,
    double AverageMotif,
    double MaxGeometry,
    double MaxMovement,
    double MaxSlider,
    double MaxMotif,
    double AverageSecondBestFamily,
    double MaxSecondBestFamily,
    IReadOnlyDictionary<int, int> StrongFamilyDistribution,
    int ExceptionalWindowCount,
    int SecondBestModerateWindowCount,
    int ExceptionalPlusModerateWindowCount,
    IReadOnlyDictionary<string, int> NearThresholdDistribution,
    IReadOnlyList<TechV2DecisionCalibrationWindow> TopWindows,
    IReadOnlyDictionary<string, int> TopDominantFamilyDistribution,
    IReadOnlyDictionary<string, int> TopTwoPairDistribution,
    int NearMissWindowCount,
    IReadOnlyList<TechV2NearMissRun> NearMissRuns)
{
    internal double NearMissPercent => TotalWindows == 0
        ? 0
        : (double)NearMissWindowCount / TotalWindows * 100;

    internal int LongestNearMissRun => NearMissRuns.Count == 0
        ? 0
        : NearMissRuns.Max(run => run.WindowCount);
}

/// <summary>
/// Test-only Phase 2.1 calibration audit. It mirrors the documented current
/// thresholds solely to explain existing decisions; it never calls or changes
/// the production decision engine.
/// </summary>
internal static class TechV2DecisionCalibrationAudit
{
    private const double ModerateFamilyThreshold = 0.45;
    private const double StrongFamilyThreshold = 0.60;
    private const double ExceptionalFamilyThreshold = 0.80;
    private const double NearMissSecondFamilyThreshold = 0.40;
    private const int TopWindowCount = 20;

    internal static TechV2DecisionCalibrationAuditResult Analyze(
        string mapName,
        IReadOnlyList<TechV2WindowDecision> decisions,
        TechV2PresenceResult presence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapName);
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentNullException.ThrowIfNull(presence);

        IReadOnlyList<TechV2DecisionCalibrationWindow> windows = decisions
            .Select(Describe)
            .OrderBy(window => window.Decision.StartIndex)
            .ToArray();

        IReadOnlyList<TechV2DecisionCalibrationWindow> topWindows = windows
            .OrderByDescending(window => window.Decision.RawScore)
            .ThenBy(window => window.Decision.StartIndex)
            .ThenBy(window => window.Decision.EndIndex)
            .Take(TopWindowCount)
            .ToArray();

        return new TechV2DecisionCalibrationAuditResult(
            mapName,
            windows.Count,
            windows.Count(window => window.Decision.IsTech),
            windows.Count(window => !window.Decision.IsTech),
            presence.PresencePercent,
            Average(windows, window => window.Decision.GeometryEvidence),
            Average(windows, window => window.Decision.MovementEvidence),
            Average(windows, window => window.Decision.SliderEvidence),
            Average(windows, window => window.Decision.MotifEvidence),
            Maximum(windows, window => window.Decision.GeometryEvidence),
            Maximum(windows, window => window.Decision.MovementEvidence),
            Maximum(windows, window => window.Decision.SliderEvidence),
            Maximum(windows, window => window.Decision.MotifEvidence),
            Average(windows, window => window.SecondBestFamily.Value),
            Maximum(windows, window => window.SecondBestFamily.Value),
            Enumerable.Range(0, 5).ToDictionary(
                count => count,
                count => windows.Count(window => window.StrongFamilyCount == count)),
            windows.Count(window => window.HasExceptionalFamily),
            windows.Count(window => window.HasModerateSecondFamily),
            windows.Count(window => window.SatisfiesExceptionalPlusModerate),
            CreateNearThresholdDistribution(windows),
            topWindows,
            CountBy(topWindows, window => window.BestFamily.Name),
            CountBy(topWindows, window => window.TopTwoPair),
            windows.Count(window => window.IsNearMiss),
            FindNearMissRuns(windows));
    }

    internal static TechV2DecisionCalibrationWindow Describe(
        TechV2WindowDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        IReadOnlyList<TechV2RankedEvidenceFamily> ranked =
        [
            new("Geometry", decision.GeometryEvidence, 0),
            new("Movement", decision.MovementEvidence, 1),
            new("Slider", decision.SliderEvidence, 2),
            new("Motif", decision.MotifEvidence, 3),
        ];

        ranked = ranked
            .OrderByDescending(family => family.Value)
            .ThenBy(family => family.StableOrder)
            .ToArray();

        int strongFamilyCount = ranked.Count(
            family => family.Value >= StrongFamilyThreshold);
        bool hasExceptionalFamily = ranked.Any(
            family => family.Value >= ExceptionalFamilyThreshold);
        bool hasModerateSecondFamily = ranked[1].Value >= ModerateFamilyThreshold;
        bool exceptionalPlusModerate = hasExceptionalFamily && hasModerateSecondFamily;

        TechV2DecisionFailureReason reason = decision.IsTech
            ? strongFamilyCount >= 2
                ? TechV2DecisionFailureReason.PASS_TWO_STRONG
                : TechV2DecisionFailureReason.PASS_EXCEPTIONAL_PLUS_MODERATE
            : strongFamilyCount == 0
                ? TechV2DecisionFailureReason.FAIL_NO_STRONG_FAMILY
                : !hasModerateSecondFamily
                    ? TechV2DecisionFailureReason.FAIL_SECOND_FAMILY_TOO_LOW
                    : TechV2DecisionFailureReason.FAIL_ONLY_ONE_STRONG;

        return new TechV2DecisionCalibrationWindow(
            decision,
            ranked,
            strongFamilyCount,
            hasExceptionalFamily,
            hasModerateSecondFamily,
            exceptionalPlusModerate,
            reason,
            ranked[0].Value >= StrongFamilyThreshold
                && ranked[1].Value >= NearMissSecondFamilyThreshold);
    }

    internal static string FormatReport(TechV2DecisionCalibrationAuditResult audit)
    {
        ArgumentNullException.ThrowIfNull(audit);

        StringBuilder report = new();
        report.AppendLine($"===== TECH V2 DECISION AUDIT: {audit.MapName} =====");
        report.AppendLine(
            $"Windows={audit.TotalWindows} | Tech={audit.TechWindows} | NonTech={audit.NonTechWindows} | Presence={audit.PresencePercent:F2}%");
        report.AppendLine(
            $"Avg G/M/S/Mo={audit.AverageGeometry:F3}/{audit.AverageMovement:F3}/{audit.AverageSlider:F3}/{audit.AverageMotif:F3}");
        report.AppendLine(
            $"Max G/M/S/Mo={audit.MaxGeometry:F3}/{audit.MaxMovement:F3}/{audit.MaxSlider:F3}/{audit.MaxMotif:F3}");
        report.AppendLine(
            $"Second-best avg/max={audit.AverageSecondBestFamily:F3}/{audit.MaxSecondBestFamily:F3}");
        report.AppendLine($"Strong families: {FormatDistribution(audit.StrongFamilyDistribution)}");
        report.AppendLine(
            $"Exceptional={audit.ExceptionalWindowCount} | Second>=.45={audit.SecondBestModerateWindowCount} | Exceptional+Moderate={audit.ExceptionalPlusModerateWindowCount}");
        report.AppendLine($"Near thresholds: {FormatDistribution(audit.NearThresholdDistribution)}");
        report.AppendLine(
            $"NearMiss={audit.NearMissWindowCount}/{audit.TotalWindows} ({audit.NearMissPercent:F2}%) | LongestRun={audit.LongestNearMissRun} | Runs>=2={audit.NearMissRuns.Count(run => run.WindowCount >= 2)} | Runs>=3={audit.NearMissRuns.Count(run => run.WindowCount >= 3)} | Runs>=5={audit.NearMissRuns.Count(run => run.WindowCount >= 5)}");
        report.AppendLine($"Top dominant: {FormatDistribution(audit.TopDominantFamilyDistribution)}");
        report.AppendLine($"Top pairs: {FormatDistribution(audit.TopTwoPairDistribution)}");
        report.AppendLine("Top 20 by RawScore:");

        foreach (TechV2DecisionCalibrationWindow window in audit.TopWindows)
        {
            report.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0:F3}->{1:F3} | Tech={2} | G={3:F3} M={4:F3} S={5:F3} Mo={6:F3} | Strong={7} | Best={8}:{9:F3} | Second={10}:{11:F3} | Raw={12:F3} | {13}",
                window.Decision.StartTime,
                window.Decision.EndTime,
                window.Decision.IsTech,
                window.Decision.GeometryEvidence,
                window.Decision.MovementEvidence,
                window.Decision.SliderEvidence,
                window.Decision.MotifEvidence,
                window.StrongFamilyCount,
                window.BestFamily.Name,
                window.BestFamily.Value,
                window.SecondBestFamily.Name,
                window.SecondBestFamily.Value,
                window.Decision.RawScore,
                window.FailureReason));
        }

        return report.ToString();
    }

    private static IReadOnlyDictionary<string, int> CreateNearThresholdDistribution(
        IReadOnlyList<TechV2DecisionCalibrationWindow> windows) =>
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Strong + second [0.55,0.60)"] = CountNearThreshold(windows, 0.55, 0.60),
            ["Strong + second [0.50,0.55)"] = CountNearThreshold(windows, 0.50, 0.55),
            ["Strong + second [0.45,0.50)"] = CountNearThreshold(windows, 0.45, 0.50),
            ["Strong + second [0.40,0.45)"] = CountNearThreshold(windows, 0.40, 0.45),
            ["Exceptional + second [0.40,0.45)"] = windows.Count(window =>
                window.BestFamily.Value >= ExceptionalFamilyThreshold
                && window.SecondBestFamily.Value >= 0.40
                && window.SecondBestFamily.Value < 0.45),
        };

    private static int CountNearThreshold(
        IReadOnlyList<TechV2DecisionCalibrationWindow> windows,
        double inclusiveLower,
        double exclusiveUpper) =>
        windows.Count(window =>
            window.BestFamily.Value >= StrongFamilyThreshold
            && window.SecondBestFamily.Value >= inclusiveLower
            && window.SecondBestFamily.Value < exclusiveUpper);

    private static IReadOnlyList<TechV2NearMissRun> FindNearMissRuns(
        IReadOnlyList<TechV2DecisionCalibrationWindow> windows)
    {
        List<TechV2NearMissRun> runs = [];
        List<TechV2DecisionCalibrationWindow> active = [];

        foreach (TechV2DecisionCalibrationWindow window in windows)
        {
            bool contiguous = active.Count > 0
                && window.IsNearMiss
                && window.Decision.StartIndex
                    == active[^1].Decision.StartIndex + TechDetectorV2Experimental.Stride
                && window.Decision.StartTime <= active[^1].Decision.EndTime;

            if (window.IsNearMiss && (active.Count == 0 || contiguous))
            {
                active.Add(window);
                continue;
            }

            AddRun(runs, active);
            active.Clear();

            if (window.IsNearMiss)
                active.Add(window);
        }

        AddRun(runs, active);
        return runs;
    }

    private static void AddRun(
        ICollection<TechV2NearMissRun> runs,
        IReadOnlyList<TechV2DecisionCalibrationWindow> windows)
    {
        if (windows.Count == 0)
            return;

        runs.Add(new TechV2NearMissRun(
            windows.Count,
            windows[0].Decision.StartIndex,
            windows[^1].Decision.EndIndex,
            windows[0].Decision.StartTime,
            windows[^1].Decision.EndTime));
    }

    private static IReadOnlyDictionary<string, int> CountBy(
        IEnumerable<TechV2DecisionCalibrationWindow> windows,
        Func<TechV2DecisionCalibrationWindow, string> selector) =>
        windows
            .GroupBy(selector, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    private static double Average(
        IReadOnlyList<TechV2DecisionCalibrationWindow> windows,
        Func<TechV2DecisionCalibrationWindow, double> selector) =>
        windows.Count == 0 ? 0 : windows.Average(selector);

    private static double Maximum(
        IReadOnlyList<TechV2DecisionCalibrationWindow> windows,
        Func<TechV2DecisionCalibrationWindow, double> selector) =>
        windows.Count == 0 ? 0 : windows.Max(selector);

    private static string FormatDistribution<TKey>(IReadOnlyDictionary<TKey, int> values)
        where TKey : notnull =>
        string.Join(
            " | ",
            values.OrderBy(item => item.Key?.ToString(), StringComparer.Ordinal)
                .Select(item => $"{item.Key}={item.Value}"));
}
