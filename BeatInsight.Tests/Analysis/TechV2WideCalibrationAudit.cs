using BeatInsight.Analysis;
using BeatInsight.Models;
using System.Globalization;
using System.Text;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// Phase 2.5 test-only calibration view. It invokes the Phase 2.4 engine as
/// shipped and performs all alternate geometry thresholds on copied decisions.
/// No result from this type is fed back into runtime analysis.
/// </summary>
internal static class TechV2WideCalibrationAudit
{
    internal static readonly double[] GeometryThresholds = [0.20, 0.22, 0.24, 0.25, 0.266, 0.28, 0.30];

    internal static TechV2WideCalibrationMap Analyze(
        string fixtureName,
        TechV2CalibrationCategory category,
        Beatmap map)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixtureName);
        ArgumentNullException.ThrowIfNull(map);

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
        TechV2SustainedPromotionResult beforeGuard =
            TechV2ExperimentalDecisionEngine.PromoteSustainedTechWithDiagnostics(
                baseDecisions,
                applyGeometryGuard: false);
        TechV2SustainedPromotionResult currentGuard =
            TechV2ExperimentalDecisionEngine.PromoteSustainedTechWithDiagnostics(baseDecisions);

        double playableStart = map.HitObjects.FirstOrDefault()?.Time ?? 0;
        double playableEnd = map.HitObjects.LastOrDefault()?.Time ?? 0;
        TechV2PresenceResult beforePresence = TechV2ExperimentalDecisionEngine.BuildPresence(
            beforeGuard.Decisions, playableStart, playableEnd);
        TechV2PresenceResult afterPresence = TechV2ExperimentalDecisionEngine.BuildPresence(
            currentGuard.Decisions, playableStart, playableEnd);

        IReadOnlyList<TechV2WideCalibrationRun> candidateRuns = FindCandidateRuns(
            beforeGuard.Decisions,
            currentGuard.Decisions);
        IReadOnlyDictionary<double, TechV2WideCalibrationSimulation> simulations =
            GeometryThresholds.ToDictionary(
                threshold => threshold,
                threshold => SimulateThreshold(
                    baseDecisions,
                    candidateRuns,
                    threshold,
                    playableStart,
                    playableEnd));

        return new TechV2WideCalibrationMap(
            fixtureName,
            category,
            windows.Count,
            baseDecisions.Count(decision => decision.IsStrongTech),
            baseDecisions.Count(decision => decision.IsTechSupport),
            beforeGuard.Decisions.Count(decision => decision.IsSustainedTech),
            currentGuard.Decisions.Count(decision => decision.IsSustainedTech),
            currentGuard.RejectedRuns,
            beforePresence,
            afterPresence,
            candidateRuns,
            simulations);
    }

    internal static string FormatReport(IReadOnlyList<TechV2WideCalibrationMap> maps)
    {
        StringBuilder text = new();
        text.AppendLine("===== TECH DETECTOR V2 PHASE 2.5 WIDE CALIBRATION (TEST-ONLY) =====");
        text.AppendLine("Categories are fixture/test references only; they are not derived from V2 output.");
        text.AppendLine("MAP | Category | Windows | Strong | Support | Sustained pre/post | Rejected | Regions | Longest | Tech/playable | Presence pre/post | Delta");
        foreach (TechV2WideCalibrationMap map in maps.OrderBy(item => item.Category).ThenBy(item => item.FixtureName, StringComparer.Ordinal))
        {
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0} | {1} | {2} | {3} | {4} | {5}/{6} | {7} | {8} | {9:F2}s | {10:F2}/{11:F2}s | {12:F2}/{13:F2}% | {14:F2}%",
                map.FixtureName, map.Category, map.WindowCount, map.StrongTechWindowCount,
                map.TechSupportWindowCount, map.SustainedBeforeGuardCount, map.SustainedAfterGuardCount,
                map.RejectedRuns.Count, map.AfterGuardPresence.Regions.Count, map.LongestRegionDuration / 1000.0,
                map.AfterGuardPresence.TotalTechDuration / 1000.0,
                map.AfterGuardPresence.PlayableDuration / 1000.0,
                map.BeforeGuardPresence.PresencePercent, map.AfterGuardPresence.PresencePercent,
                map.AfterGuardPresence.PresencePercent - map.BeforeGuardPresence.PresencePercent));
        }

        text.AppendLine("SUSTAINED CANDIDATE RUNS | Map | start->end | windows | avg geometry/movement/slider/motif | current .266 kept");
        foreach (TechV2WideCalibrationMap map in maps)
        {
            if (map.CandidateRuns.Count == 0)
                text.AppendLine($"{map.FixtureName} | none");
            foreach (TechV2WideCalibrationRun run in map.CandidateRuns)
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0} | {1:F0}->{2:F0}ms | {3} | {4:F3}/{5:F3}/{6:F3}/{7:F3} | {8}",
                    map.FixtureName, run.StartTime, run.EndTime, run.WindowCount,
                    run.AverageGeometry, run.AverageMovement, run.AverageSlider, run.AverageMotif,
                    run.KeptByCurrentGeometryGuard));

            IReadOnlyList<TechV2WideCalibrationRun> kept = map.CandidateRuns
                .Where(run => run.KeptByCurrentGeometryGuard)
                .ToArray();
            IReadOnlyList<TechV2WideCalibrationRun> rejected = map.CandidateRuns
                .Where(run => !run.KeptByCurrentGeometryGuard)
                .ToArray();
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  geometry summary: kept avg/min={0}/{1}; rejected avg/max={2}/{3}",
                FormatAverage(kept), FormatMinimum(kept),
                FormatAverage(rejected), FormatMaximum(rejected)));
        }

        text.AppendLine("GEOMETRY RUN DISTRIBUTION | Category | <.15 | .15-.20 | .20-.25 | .25-.266 | .266-.30 | .30-.35 | .35-.40 | >=.40");
        foreach (TechV2CalibrationCategory category in Enum.GetValues<TechV2CalibrationCategory>())
        {
            IReadOnlyList<TechV2WideCalibrationRun> runs = maps
                .Where(map => map.Category == category)
                .SelectMany(map => map.CandidateRuns)
                .ToArray();
            int[] buckets = GeometryBuckets(runs.Select(run => run.AverageGeometry));
            text.AppendLine($"{category} | {string.Join(" | ", buckets)}");
        }

        text.AppendLine("OFFLINE GEOMETRY THRESHOLD SIMULATION | threshold | Tech kept/rejected | NonTech kept/rejected | BorderlineUnknown kept/rejected");
        foreach (double threshold in GeometryThresholds)
        {
            string[] categories = Enum.GetValues<TechV2CalibrationCategory>()
                .Select(category =>
                {
                    IReadOnlyList<TechV2WideCalibrationRun> runs = maps.Where(map => map.Category == category)
                        .SelectMany(map => map.CandidateRuns).ToArray();
                    int kept = runs.Count(run => run.AverageGeometry >= threshold);
                    return $"{kept}/{runs.Count - kept}";
                })
                .ToArray();
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0:F3} | {1} | {2} | {3}", threshold, categories[0], categories[1], categories[2]));
            foreach (TechV2WideCalibrationMap map in maps)
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0}: simulated presence {1:F2}%", map.FixtureName,
                    map.Simulations[threshold].Presence.PresencePercent));
        }

        AppendProblemCases(text, maps);
        return text.ToString();
    }

    private static IReadOnlyList<TechV2WideCalibrationRun> FindCandidateRuns(
        IReadOnlyList<TechV2WindowDecision> beforeGuard,
        IReadOnlyList<TechV2WindowDecision> currentGuard)
    {
        IReadOnlyDictionary<int, TechV2WindowDecision> currentByIndex = currentGuard
            .ToDictionary(decision => decision.StartIndex);
        List<TechV2WideCalibrationRun> runs = [];
        List<TechV2WindowDecision> active = [];

        void AddActive()
        {
            if (active.Count == 0)
                return;
            runs.Add(new TechV2WideCalibrationRun(
                active[0].StartTime,
                active[^1].EndTime,
                active.Count,
                active.Average(item => item.GeometryEvidence),
                active.Average(item => item.MovementEvidence),
                active.Average(item => item.SliderEvidence),
                active.Average(item => item.MotifEvidence),
                currentByIndex[active[0].StartIndex].IsSustainedTech,
                active.Select(item => item.StartIndex).ToArray()));
            active.Clear();
        }

        foreach (TechV2WindowDecision decision in beforeGuard.OrderBy(item => item.StartIndex))
        {
            bool continues = active.Count > 0
                && decision.IsSustainedTech
                && decision.StartIndex == active[^1].StartIndex + TechDetectorV2Experimental.Stride
                && decision.StartTime <= active[^1].EndTime;
            if (decision.IsSustainedTech && (active.Count == 0 || continues))
            {
                active.Add(decision);
                continue;
            }
            AddActive();
            if (decision.IsSustainedTech)
                active.Add(decision);
        }
        AddActive();
        return runs;
    }

    private static TechV2WideCalibrationSimulation SimulateThreshold(
        IReadOnlyList<TechV2WindowDecision> baseDecisions,
        IReadOnlyList<TechV2WideCalibrationRun> runs,
        double threshold,
        double playableStart,
        double playableEnd)
    {
        HashSet<int> promoted = runs
            .Where(run => run.AverageGeometry >= threshold)
            .SelectMany(run => run.WindowStartIndices)
            .ToHashSet();
        IReadOnlyList<TechV2WindowDecision> decisions = baseDecisions
            .Select(decision => decision with { IsSustainedTech = promoted.Contains(decision.StartIndex) })
            .ToArray();
        return new TechV2WideCalibrationSimulation(
            threshold,
            runs.Count(run => run.AverageGeometry >= threshold),
            runs.Count(run => run.AverageGeometry < threshold),
            TechV2ExperimentalDecisionEngine.BuildPresence(decisions, playableStart, playableEnd));
    }

    private static int[] GeometryBuckets(IEnumerable<double> values)
    {
        int[] result = new int[8];
        foreach (double value in values)
        {
            int index = value < .15 ? 0
                : value < .20 ? 1
                : value < .25 ? 2
                : value < .266 ? 3
                : value < .30 ? 4
                : value < .35 ? 5
                : value < .40 ? 6
                : 7;
            result[index]++;
        }
        return result;
    }

    private static string FormatAverage(IReadOnlyList<TechV2WideCalibrationRun> runs) =>
        runs.Count == 0 ? "n/a" : runs.Average(run => run.AverageGeometry).ToString("F3", CultureInfo.InvariantCulture);

    private static string FormatMinimum(IReadOnlyList<TechV2WideCalibrationRun> runs) =>
        runs.Count == 0 ? "n/a" : runs.Min(run => run.AverageGeometry).ToString("F3", CultureInfo.InvariantCulture);

    private static string FormatMaximum(IReadOnlyList<TechV2WideCalibrationRun> runs) =>
        runs.Count == 0 ? "n/a" : runs.Max(run => run.AverageGeometry).ToString("F3", CultureInfo.InvariantCulture);

    private static void AppendProblemCases(StringBuilder text, IReadOnlyList<TechV2WideCalibrationMap> maps)
    {
        IEnumerable<TechV2WideCalibrationMap> tech = maps.Where(map => map.Category == TechV2CalibrationCategory.TechReference);
        IEnumerable<TechV2WideCalibrationMap> nonTech = maps.Where(map => map.Category == TechV2CalibrationCategory.NonTechReference);
        TechV2WideCalibrationMap? highestNonTech = nonTech.OrderByDescending(map => map.AfterGuardPresence.PresencePercent).FirstOrDefault();
        TechV2WideCalibrationMap? lowestTech = tech.OrderBy(map => map.AfterGuardPresence.PresencePercent).FirstOrDefault();
        TechV2WideCalibrationMap? largestTechReduction = tech.OrderByDescending(map => map.BeforeGuardPresence.PresencePercent - map.AfterGuardPresence.PresencePercent).FirstOrDefault();
        text.AppendLine("PROBLEM-CASE FLAGS (diagnostic only):");
        if (highestNonTech is not null)
            text.AppendLine($"Highest non-Tech presence: {highestNonTech.FixtureName} ({highestNonTech.AfterGuardPresence.PresencePercent:F2}%).");
        if (lowestTech is not null)
            text.AppendLine($"Lowest Tech presence: {lowestTech.FixtureName} ({lowestTech.AfterGuardPresence.PresencePercent:F2}%).");
        if (largestTechReduction is not null)
            text.AppendLine($"Largest guard reduction on Tech reference: {largestTechReduction.FixtureName} ({largestTechReduction.BeforeGuardPresence.PresencePercent - largestTechReduction.AfterGuardPresence.PresencePercent:F2} points).");
        foreach (TechV2WideCalibrationMap map in nonTech.Where(map => map.SustainedAfterGuardCount > 0))
            text.AppendLine($"Non-Tech reference still producing SustainedTech: {map.FixtureName} ({map.SustainedAfterGuardCount} windows).");
        foreach (TechV2WideCalibrationMap map in tech.Where(map => map.SustainedBeforeGuardCount > map.SustainedAfterGuardCount))
            text.AppendLine($"Tech reference losing SustainedTech: {map.FixtureName} ({map.SustainedBeforeGuardCount - map.SustainedAfterGuardCount} windows).");
    }
}

internal enum TechV2CalibrationCategory
{
    TechReference,
    NonTechReference,
    BorderlineOrUnknown,
}

internal sealed record TechV2WideCalibrationRun(
    double StartTime,
    double EndTime,
    int WindowCount,
    double AverageGeometry,
    double AverageMovement,
    double AverageSlider,
    double AverageMotif,
    bool KeptByCurrentGeometryGuard,
    IReadOnlyList<int> WindowStartIndices);

internal sealed record TechV2WideCalibrationSimulation(
    double Threshold,
    int RunsKept,
    int RunsRejected,
    TechV2PresenceResult Presence);

internal sealed record TechV2WideCalibrationMap(
    string FixtureName,
    TechV2CalibrationCategory Category,
    int WindowCount,
    int StrongTechWindowCount,
    int TechSupportWindowCount,
    int SustainedBeforeGuardCount,
    int SustainedAfterGuardCount,
    IReadOnlyList<TechV2SustainedRunGeometryRejection> RejectedRuns,
    TechV2PresenceResult BeforeGuardPresence,
    TechV2PresenceResult AfterGuardPresence,
    IReadOnlyList<TechV2WideCalibrationRun> CandidateRuns,
    IReadOnlyDictionary<double, TechV2WideCalibrationSimulation> Simulations)
{
    internal double LongestRegionDuration => AfterGuardPresence.Regions.Count == 0
        ? 0
        : AfterGuardPresence.Regions.Max(region => region.Duration);
}
