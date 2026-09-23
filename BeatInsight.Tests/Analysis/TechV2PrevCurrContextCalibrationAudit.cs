using BeatInsight.Analysis;
using BeatInsight.Models;
using BeatInsight.Models.Persistence;
using BeatInsight.Parser;
using System.Globalization;
using System.IO;
using System.Text;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// Phase 2.26 is a fast, test-only comparison. It applies the Phase 2.25
/// zero-support policy in memory, then varies only the PREV+CURR contribution.
/// It never writes runtime state or feeds any decision engine.
/// </summary>
internal enum TechV2PrevCurrVariant
{
    PREV_A,
    PREV_B,
    PREV_C,
}

internal enum TechV2PrevCurrRecommendation
{
    SAFE_TO_ENABLE,
    NEEDS_MORE_CALIBRATION,
    REJECT,
}

internal sealed record TechV2Phase226Summary(
    TechV2PrevCurrVariant Variant,
    double MaxAbsDelta,
    double AverageDelta,
    int DecisionsChanged,
    int HumanGainsKept,
    int HumanGainsTotal,
    int Over015,
    int Over020,
    TechV2PrevCurrRecommendation Recommendation);

internal sealed record TechV2Phase226Extreme(
    string Map,
    string Difficulty,
    double StartTime,
    double Current,
    double ZeroA,
    double CurrentSupportFactor,
    IReadOnlyDictionary<TechV2PrevCurrVariant, double> Candidate);

internal sealed record TechV2Phase226HumanCase(
    string CaseId,
    double ZeroADelta,
    IReadOnlyDictionary<TechV2PrevCurrVariant, double> CandidateDelta,
    IReadOnlyDictionary<TechV2PrevCurrVariant, string> Status);

internal sealed record TechV2Phase226AuditReport(
    IReadOnlyList<TechV2Phase226Summary> Summaries,
    IReadOnlyList<TechV2Phase226Extreme> Extremes,
    IReadOnlyList<TechV2Phase226HumanCase> HumanCases,
    TechV2PrevCurrVariant RecommendedVariant,
    TechV2PrevCurrRecommendation Recommendation,
    string DominantRemainingRisk,
    bool AllDecisionsUnchanged);

internal static class TechV2PrevCurrContextCalibrationAudit
{
    private const double Epsilon = 1e-9;
    private const double PrevContributionCap = .10;
    private const int GlobalMapLimit = 150;
    private static readonly TechV2PrevCurrVariant[] Variants =
    [
        TechV2PrevCurrVariant.PREV_A,
        TechV2PrevCurrVariant.PREV_B,
        TechV2PrevCurrVariant.PREV_C,
    ];

    private static readonly string[] ExtremeTokens =
    [
        "SANTA SAN", "AliVe", "Ringing Bloom", "Koinu", "Don't Let Go",
        "Start Again", "Yuuki wa Doko ni?", "Deai Machi", "More-more LOVERS!!",
    ];

    private static readonly string[] HumanCaseIds =
    [
        "Exit R5", "WorldEndsNow R1", "TrustInYou R1", "Inferno R1",
        "TaoHuaXiao R1", "HappppySong R1", "HappppySong R2", "Kira R1",
        "Frozen R1", "GloriousDays R1",
    ];

    internal static TechV2Phase226AuditReport Run(string databasePath, int maxGlobalMaps = GlobalMapLimit)
    {
        IReadOnlyList<MlDatasetSample> samples = TechV2StrongTechForensicAudit.LoadLocalCacheSamples(databasePath);
        IReadOnlyList<ContextWindow> global = ScanGlobalSample(samples, Math.Min(GlobalMapLimit, Math.Max(0, maxGlobalMaps)));
        IReadOnlyDictionary<string, IReadOnlyList<ContextWindow>> human = ResolveHumanCases(databasePath);

        IReadOnlyList<TechV2Phase226Summary> summaries = Variants
            .Select(variant => BuildSummary(global, human, variant))
            .ToArray();
        IReadOnlyList<TechV2Phase226Extreme> extremes = BuildExtremes(global);
        IReadOnlyList<TechV2Phase226HumanCase> humanCases = BuildHumanCases(human);

        TechV2Phase226Summary best = summaries
            .OrderBy(item => item.DecisionsChanged)
            .ThenBy(item => item.MaxAbsDelta)
            .ThenByDescending(item => item.HumanGainsKept)
            .ThenBy(item => item.Variant)
            .First();

        bool unchanged = summaries.All(item => item.DecisionsChanged == 0);
        return new(
            summaries,
            extremes,
            humanCases,
            best.Variant,
            best.Recommendation,
            "PREV+CURR can still carry a locally unsupported previous-window signal; this audit only bounds or gates that contribution.",
            unchanged);
    }

    internal static string FormatReport(TechV2Phase226AuditReport report)
    {
        StringBuilder text = new();
        text.AppendLine("===== TECH V2 PHASE 2.26 PREV+CURR FAST CALIBRATION (TEST-ONLY/SHADOW) =====");
        text.AppendLine("ZERO_A is fixed: zero denominator means no ratio evidence.");
        text.AppendLine("PREV_A: prior context only when current ValidTransitions + SliderCount > 0.");
        text.AppendLine("PREV_B: prior context * clamp((ValidTransitions + SliderCount) / 3, 0, 1).");
        text.AppendLine("PREV_C: prior context contribution capped only on the positive side at +0.100000.");
        text.AppendLine("EXTREMES | raw-current | ZERO_A | support | PREV_A | PREV_B | PREV_C");
        foreach (TechV2Phase226Extreme item in report.Extremes)
        {
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0} [{1}] {2} | {3:F6} | {4:F6} ({5:+0.000000;-0.000000;0.000000}) | {6:F3} | {7:F6} ({8:+0.000000;-0.000000;0.000000}) | {9:F6} ({10:+0.000000;-0.000000;0.000000}) | {11:F6} ({12:+0.000000;-0.000000;0.000000})",
                item.Map, item.Difficulty, FormatTime(item.StartTime), item.Current,
                item.ZeroA, item.ZeroA - item.Current, item.CurrentSupportFactor,
                item.Candidate[TechV2PrevCurrVariant.PREV_A], item.Candidate[TechV2PrevCurrVariant.PREV_A] - item.Current,
                item.Candidate[TechV2PrevCurrVariant.PREV_B], item.Candidate[TechV2PrevCurrVariant.PREV_B] - item.Current,
                item.Candidate[TechV2PrevCurrVariant.PREV_C], item.Candidate[TechV2PrevCurrVariant.PREV_C] - item.Current));
        }

        text.AppendLine("HUMAN CASES | case | ZERO_A delta | PREV_A delta/status | PREV_B delta/status | PREV_C delta/status");
        foreach (TechV2Phase226HumanCase item in report.HumanCases)
        {
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0} | {1:+0.000000;-0.000000;0.000000} | {2:+0.000000;-0.000000;0.000000}/{3} | {4:+0.000000;-0.000000;0.000000}/{5} | {6:+0.000000;-0.000000;0.000000}/{7}",
                item.CaseId, item.ZeroADelta,
                item.CandidateDelta[TechV2PrevCurrVariant.PREV_A], item.Status[TechV2PrevCurrVariant.PREV_A],
                item.CandidateDelta[TechV2PrevCurrVariant.PREV_B], item.Status[TechV2PrevCurrVariant.PREV_B],
                item.CandidateDelta[TechV2PrevCurrVariant.PREV_C], item.Status[TechV2PrevCurrVariant.PREV_C]));
        }

        text.AppendLine("FINAL | Variant | MaxAbsDelta | DecisionsChanged | HumanGainsKept | >.15 | >.20");
        foreach (TechV2Phase226Summary item in report.Summaries)
            text.AppendLine($"{item.Variant} | {item.MaxAbsDelta:F6} | {item.DecisionsChanged} | {item.HumanGainsKept}/{item.HumanGainsTotal} | {item.Over015} | {item.Over020}");
        text.AppendLine($"RECOMMENDED VARIANT = {report.RecommendedVariant}");
        text.AppendLine($"RECOMMENDATION = {report.Recommendation}");
        text.AppendLine($"DOMINANT REMAINING RISK = {report.DominantRemainingRisk}");
        return text.ToString();
    }

    private static TechV2Phase226Summary BuildSummary(
        IReadOnlyList<ContextWindow> global,
        IReadOnlyDictionary<string, IReadOnlyList<ContextWindow>> human,
        TechV2PrevCurrVariant variant)
    {
        double[] deltas = global.Select(item => Candidate(item, variant) - item.Window.CurrentCombinedValue).ToArray();
        IReadOnlyList<ContextWindow> priorGains = human.Values.SelectMany(item => item)
            .Where(item => item.Window.CorrectedCombinedValue - item.Window.CurrentCombinedValue > Epsilon)
            .ToArray();
        int kept = priorGains.Count(item => Candidate(item, variant) - item.Window.CurrentCombinedValue > Epsilon);
        double maxAbs = deltas.Length == 0 ? 0 : deltas.Max(value => Math.Abs(value));
        // The candidate value never enters Decide/PromoteSustainedTech; retain an
        // explicit invariant check against the shadow diagnostics nevertheless.
        int changed = global.Count(item => !string.Equals(
            item.Window.DecisionKindBefore,
            item.Window.DecisionKindAfter,
            StringComparison.Ordinal));
        TechV2PrevCurrRecommendation recommendation = changed != 0
            ? TechV2PrevCurrRecommendation.REJECT
            : maxAbs <= .15 && kept >= Math.Max(0, priorGains.Count - 1)
                ? TechV2PrevCurrRecommendation.SAFE_TO_ENABLE
                : TechV2PrevCurrRecommendation.NEEDS_MORE_CALIBRATION;
        return new(variant, maxAbs, deltas.DefaultIfEmpty(0).Average(), changed, kept, priorGains.Count,
            deltas.Count(value => Math.Abs(value) > .15), deltas.Count(value => Math.Abs(value) > .20), recommendation);
    }

    private static IReadOnlyList<TechV2Phase226Extreme> BuildExtremes(IReadOnlyList<ContextWindow> global) =>
        ExtremeTokens.Select(token => global.Where(item => MatchesToken(item.Map, token))
                .OrderByDescending(item => item.Window.CorrectedCombinedValue - item.Window.CurrentCombinedValue)
                .ThenBy(item => item.Window.StartIndex).FirstOrDefault())
            .Where(item => item is not null).Cast<ContextWindow>()
            .OrderBy(item => item.Map, StringComparer.Ordinal)
            .Select(item => new TechV2Phase226Extreme(
                item.Map, item.Difficulty, item.Window.StartTime, item.Window.CurrentCombinedValue,
                ZeroA(item), SupportFactor(item),
                Variants.ToDictionary(variant => variant, variant => Candidate(item, variant))))
            .ToArray();

    private static IReadOnlyList<TechV2Phase226HumanCase> BuildHumanCases(IReadOnlyDictionary<string, IReadOnlyList<ContextWindow>> cases) =>
        HumanCaseIds.Where(cases.ContainsKey).Select(caseId =>
        {
            IReadOnlyList<ContextWindow> windows = cases[caseId];
            double zeroDelta = Mean(windows.Select(item => ZeroA(item) - item.Window.CurrentCombinedValue));
            Dictionary<TechV2PrevCurrVariant, double> deltas = Variants.ToDictionary(variant => variant,
                variant => Mean(windows.Select(item => Candidate(item, variant) - item.Window.CurrentCombinedValue)));
            Dictionary<TechV2PrevCurrVariant, string> status = Variants.ToDictionary(variant => variant,
                variant => AggregateStatus(windows, variant));
            return new TechV2Phase226HumanCase(caseId, zeroDelta, deltas, status);
        }).ToArray();

    private static IReadOnlyDictionary<string, IReadOnlyList<ContextWindow>> ResolveHumanCases(string databasePath)
    {
        TechV2HumanGroundedAuditReport grounded = TechV2HumanGroundedStrongTechAudit.Run(databasePath);
        Dictionary<string, IReadOnlyList<ContextWindow>> output = new(StringComparer.Ordinal);
        foreach (string caseId in HumanCaseIds)
        {
            if (!TryParseCase(caseId, out string key, out int region)) continue;
            TechV2HumanGroundedMap? map = grounded.Maps.FirstOrDefault(item => item.Reference.Key == key && item.Resolved && item.Path is not null);
            if (map?.Path is null || region > map.Presence.Regions.Count) continue;
            TechV2Region selected = map.Presence.Regions[region - 1];
            IReadOnlyList<ContextWindow> windows = AnalyzePath(map.Path)
                .Where(item => item.Window.StartIndex >= selected.StartWindowIndex && item.Window.EndIndex <= selected.EndWindowIndex)
                .ToArray();
            if (windows.Count > 0) output[caseId] = windows;
        }
        return output;
    }

    private static IReadOnlyList<ContextWindow> ScanGlobalSample(IReadOnlyList<MlDatasetSample> samples, int maxMaps)
    {
        IEnumerable<string> paths = samples.Select(item => item.SourceFilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<string> extremePaths = paths.Where(path => ExtremeTokens.Any(token => MatchesToken(path, token)))
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        // The mandatory extremes consume slots first; the deterministic prefix
        // fills only the remaining budget. This keeps the global sanity sample
        // at or below the requested 150 maps rather than scanning 150 plus extras.
        IReadOnlyList<string> deterministic = paths
            .Where(path => !extremePaths.Contains(path, StringComparer.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Take(Math.Max(0, maxMaps - extremePaths.Count))
            .ToArray();
        return extremePaths.Concat(deterministic)
            .SelectMany(AnalyzePathSafely).ToArray();
    }

    private static IEnumerable<ContextWindow> AnalyzePathSafely(string path)
    {
        try { return AnalyzePath(path); }
        catch (Exception exception) when (exception is IOException or FormatException or InvalidDataException) { return []; }
    }

    private static IReadOnlyList<ContextWindow> AnalyzePath(string path)
    {
        Beatmap map = BeatmapParser.Load(path);
        if (map.HitObjects.Count < TechDetectorV2Experimental.WindowSize) return [];
        IReadOnlyList<TechV2ExperimentalWindowResult> sources = TechDetectorV2Experimental.Analyze(map.HitObjects,
            GameplayAnalyzer.IsSpinner, GameplayAnalyzer.IsSlider, GameplayAnalyzer.IsComplexSlider,
            GameplayAnalyzer.GetObjectStartPosition, GameplayAnalyzer.GetObjectEndPosition, GameplayAnalyzer.GetTechTurnAngle);
        IReadOnlyList<TechV2WindowDecision> decisions = TechV2ExperimentalDecisionEngine.PromoteSustainedTech(TechV2ExperimentalDecisionEngine.Decide(sources));
        TechV2CombinedCorrectionShadowResult shadow = TechV2CombinedCorrectionShadowRuntime.Compute(map.HitObjects, sources, decisions);
        IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> sourceByStart = sources.ToDictionary(item => item.StartIndex);
        IReadOnlyDictionary<int, TechV2WindowDecision> decisionByStart = decisions.ToDictionary(item => item.StartIndex);
        string title = string.IsNullOrWhiteSpace(map.Title) ? Path.GetFileNameWithoutExtension(path) : map.Title;
        string difficulty = string.IsNullOrWhiteSpace(map.Version) ? "Unknown" : map.Version;
        List<ContextWindow> local = shadow.Windows.Where(item => sourceByStart.ContainsKey(item.StartIndex) && decisionByStart.ContainsKey(item.StartIndex))
            .OrderBy(item => item.StartIndex)
            .Select(item => new ContextWindow(title, difficulty, sourceByStart[item.StartIndex], decisionByStart[item.StartIndex], item, null))
            .ToList();
        for (int index = 0; index < local.Count; index++) local[index] = local[index] with { Previous = index > 0 ? local[index - 1] : null };
        return local;
    }

    private static double Candidate(ContextWindow item, TechV2PrevCurrVariant variant)
    {
        double prior = PrevContribution(item);
        double effective = variant switch
        {
            TechV2PrevCurrVariant.PREV_A => HasCurrentSupport(item) ? prior : 0,
            TechV2PrevCurrVariant.PREV_B => prior * SupportFactor(item),
            TechV2PrevCurrVariant.PREV_C => Math.Min(prior, PrevContributionCap),
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };
        return Math.Clamp(ZeroA(item) - prior + effective, 0, 1);
    }

    private static double ZeroA(ContextWindow item) => Math.Clamp((ZeroARatioMean(item.Window) + item.Window.CorrectedLocalContextValue) / 2d, 0, 1);
    private static double ZeroARatioMean(TechV2ShadowWindowDiagnostics window) => Mean(window.Ratios.Select(ratio => ratio.Denominator <= 0 ? 0 : ratio.SupportAware));
    private static double PrevContribution(ContextWindow item) => (item.Window.CorrectedLocalContextValue - item.Window.CurrentLocalContextValue) / 2d;
    private static bool HasCurrentSupport(ContextWindow item) => item.Window.SupportCounts.ValidTransitions + item.Window.SupportCounts.SliderCount > 0;
    private static double SupportFactor(ContextWindow item) => Math.Clamp((item.Window.SupportCounts.ValidTransitions + item.Window.SupportCounts.SliderCount) / 3d, 0, 1);
    private static double Mean(IEnumerable<double> values) => values.DefaultIfEmpty(0).Average();

    private static string AggregateStatus(IReadOnlyList<ContextWindow> windows, TechV2PrevCurrVariant variant)
    {
        IReadOnlyList<ContextWindow> prior = windows.Where(item => item.Window.CorrectedCombinedValue - item.Window.CurrentCombinedValue > Epsilon).ToArray();
        if (prior.Count == 0) return "NO_PRIOR_GAIN";
        int kept = prior.Count(item => Candidate(item, variant) - item.Window.CurrentCombinedValue > Epsilon);
        return kept == prior.Count ? "KEPT" : kept == 0 ? "LOST" : "REDUCED";
    }

    private static bool TryParseCase(string value, out string key, out int region)
    {
        key = string.Empty; region = 0;
        int split = value.LastIndexOf(" R", StringComparison.Ordinal);
        return split > 0 && int.TryParse(value[(split + 2)..], out region) && region > 0 && (key = value[..split]).Length > 0;
    }
    internal static bool MatchesToken(string value, string token) => Normalize(value).Contains(Normalize(token), StringComparison.Ordinal);
    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    private static string FormatTime(double milliseconds)
    {
        TimeSpan time = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}.{2:D3}", (int)time.TotalMinutes, time.Seconds, time.Milliseconds);
    }

    private sealed record ContextWindow(string Map, string Difficulty, TechV2ExperimentalWindowResult Source,
        TechV2WindowDecision Decision, TechV2ShadowWindowDiagnostics Window, ContextWindow? Previous);
}
