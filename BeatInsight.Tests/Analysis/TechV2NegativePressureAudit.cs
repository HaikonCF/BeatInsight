using BeatInsight.Analysis;
using BeatInsight.Models;
using System.Globalization;
using System.Text;

namespace BeatInsight.Tests.Analysis;

internal enum TechV2NegativeReferenceDisposition
{
    KnownNonTech,
    Unknown,
    TechReference,
}

internal enum TechV2NegativeReferenceFamily
{
    Jump,
    Stream,
    Burst,
    ClassicMixed,
}

internal sealed record TechV2NegativeFixtureReference(
    string FixtureName,
    TechV2NegativeReferenceDisposition Disposition,
    TechV2NegativeReferenceFamily Family,
    string ReferenceCategory,
    string EvidenceSource);

internal sealed record TechV2NegativePressureRun(
    double StartTime,
    double EndTime,
    int WindowCount,
    double AverageGeometry,
    double AverageMovement,
    double AverageSlider,
    double AverageMotif,
    int StrongTechCount,
    int SupportCount,
    bool KeptByGeometryGuard,
    IReadOnlyList<int> WindowStartIndices);

internal sealed record TechV2NegativeStrongTechWindow(
    double StartTime,
    double EndTime,
    int StartIndex,
    int EndIndex,
    string DominantFamilyPair,
    double GeometryEvidence,
    double MovementEvidence,
    double SliderEvidence,
    double MotifEvidence,
    double GeometryIrregularity,
    double RhythmIrregularity,
    double SliderComplexity,
    double Awkwardness,
    double SpacingVariation);

internal sealed record TechV2NegativePressureMapReport(
    string FixtureName,
    string Map,
    string Difficulty,
    string ReferenceCategory,
    TechV2NegativeReferenceDisposition Disposition,
    TechV2NegativeReferenceFamily Family,
    int Windows,
    int StrongTech,
    int TechSupport,
    int SustainedTechBeforeGuard,
    int SustainedTechAfterGuard,
    int RejectedRuns,
    int RegionCount,
    double LongestRegion,
    double PresenceBeforeGuard,
    double PresenceAfterGuard,
    IReadOnlyList<TechV2NegativePressureRun> CandidateRuns,
    IReadOnlyList<TechV2NegativeStrongTechWindow> StrongTechWindows,
    string DominantFamilyPair,
    string MainSuspectedCause,
    double MaxRunGeometry,
    double AvgRunGeometry,
    bool IsHardNegativeCandidate);

internal sealed record TechV2NegativeThresholdSummary(
    double Threshold,
    int NonTechRunsKept,
    int NonTechRunsRejected,
    int NonTechMapsWithPresence,
    double MaxNonTechPresence,
    double AverageNonTechPresence,
    int TechRunsKept,
    int TechRunsRejected,
    double ExitPresence,
    double KiraPresence,
    double FrozenPresence);

internal sealed record TechV2NegativeCategorySummary(
    TechV2NegativeReferenceFamily Family,
    int MapCount,
    int MapsProducingSupport,
    int MapsProducingSustainedTech,
    int MapsProducingStrongTech,
    double AveragePresence,
    double MaximumPresence);

internal sealed record TechV2NegativePressureReport(
    IReadOnlyList<TechV2NegativeFixtureReference> Fixtures,
    IReadOnlyList<TechV2NegativePressureMapReport> Maps,
    IReadOnlyDictionary<string, int> GeometryDistributionBuckets,
    IReadOnlyList<TechV2NegativeThresholdSummary> ThresholdTradeoff,
    IReadOnlyList<TechV2NegativePressureMapReport> TopHardNegatives,
    IReadOnlyList<TechV2NegativePressureMapReport> StrongTechFalsePositives,
    IReadOnlyList<TechV2NegativeCategorySummary> CategorySummaries,
    string GeometryGuardAssessment)
{
    internal int MapsDiscovered => Maps.Count;

    internal int KnownNonTechCount => Maps.Count(
        map => map.Disposition == TechV2NegativeReferenceDisposition.KnownNonTech);

    internal int UnknownCount => Maps.Count(
        map => map.Disposition == TechV2NegativeReferenceDisposition.Unknown);

    internal int HardNegativeCount => Maps.Count(
        map => map.IsHardNegativeCandidate);
}

internal static class TechV2NegativePressureAudit
{
    internal static readonly double[] GeometryThresholds =
        [0.18, 0.20, 0.22, 0.24, 0.25, 0.266, 0.28, 0.30];

    internal static readonly IReadOnlyList<TechV2NegativeFixtureReference> FixtureReferences =
    [
        new(
            "(can you) understand me [uhh].osu",
            TechV2NegativeReferenceDisposition.KnownNonTech,
            TechV2NegativeReferenceFamily.Jump,
            "KnownNonTech",
            "IdentityAndTechTests.Analyze_CanYouUnderstandMe_IsJumpWithoutTech"),
        new(
            "Tower Of Heaven [Extra].osu",
            TechV2NegativeReferenceDisposition.KnownNonTech,
            TechV2NegativeReferenceFamily.Stream,
            "KnownNonTech",
            "TechV2WideCalibrationAuditTests.NonTechReference + TraitRegressionTests.Analyze_Tower_HasNoBurstInfluence"),
        new(
            "Ashioto Tarte Tatin [Koori's Insane].osu",
            TechV2NegativeReferenceDisposition.Unknown,
            TechV2NegativeReferenceFamily.Burst,
            "Unknown",
            "TraitRegressionTests.Analyze_Ashioto_HasHighBurstPresence (signal only)"),
        new(
            "forever we can make it! [Kudowari's Expert].osu",
            TechV2NegativeReferenceDisposition.Unknown,
            TechV2NegativeReferenceFamily.Burst,
            "Unknown",
            "TraitRegressionTests.Analyze_Forever_HasBurstInfluence (signal only)"),
        new(
            "FREEDOM DiVE [Arles].osu",
            TechV2NegativeReferenceDisposition.Unknown,
            TechV2NegativeReferenceFamily.Stream,
            "Unknown",
            "SpeedRegressionTests.Analyze_Arles_HasHighSpeedPressure (signal only)"),
        new(
            "FREEDOM DiVE [FrenZ's Insane].osu",
            TechV2NegativeReferenceDisposition.Unknown,
            TechV2NegativeReferenceFamily.Jump,
            "Unknown",
            "SpeedRegressionTests.Analyze_FrenZ_IsNotArtificiallySpeedDominant (signal only)"),
        new(
            "Snow Goose [Extra].osu",
            TechV2NegativeReferenceDisposition.Unknown,
            TechV2NegativeReferenceFamily.ClassicMixed,
            "Unknown",
            "TraitRegressionTests.Analyze_Snow_UsesFinalReadScoreForTraits (signal only)"),
        new(
            "Exit This Earth's Atmosphere [Primordial Nucleosynthesis].osu",
            TechV2NegativeReferenceDisposition.TechReference,
            TechV2NegativeReferenceFamily.Stream,
            "TechReference",
            "IdentityAndTechTests.Analyze_ExitPrimordial_IsTechThenJump"),
        new(
            "Kira Killer [Mocaotic's Insane].osu",
            TechV2NegativeReferenceDisposition.TechReference,
            TechV2NegativeReferenceFamily.ClassicMixed,
            "TechReference",
            "TechV2WideCalibrationAuditTests.TechReference"),
        new(
            "Frozen [Collab Insane].osu",
            TechV2NegativeReferenceDisposition.TechReference,
            TechV2NegativeReferenceFamily.ClassicMixed,
            "TechReference",
            "TechV2WideCalibrationAuditTests.TechReference + IdentityAndTechTests.Analyze_Frozen_IsClassicMixedThenTech"),
    ];

    internal static TechV2NegativePressureReport Analyze()
    {
        IReadOnlyList<MapComputation> computed = FixtureReferences
            .OrderBy(reference => reference.FixtureName, StringComparer.Ordinal)
            .Select(AnalyzeFixture)
            .ToArray();

        IReadOnlyList<TechV2NegativePressureMapReport> maps = computed
            .Select(item => item.Report)
            .OrderBy(item => item.ReferenceCategory, StringComparer.Ordinal)
            .ThenBy(item => item.Map, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Difficulty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.FixtureName, StringComparer.Ordinal)
            .ToArray();

        IReadOnlyList<MapComputation> knownNonTech = computed
            .Where(item => item.Reference.Disposition == TechV2NegativeReferenceDisposition.KnownNonTech)
            .ToArray();

        IReadOnlyDictionary<string, int> geometryDistribution = BuildGeometryDistribution(knownNonTech);
        IReadOnlyList<TechV2NegativeThresholdSummary> thresholdTradeoff = GeometryThresholds
            .Select(threshold => SimulateThreshold(computed, threshold))
            .ToArray();

        IReadOnlyList<TechV2NegativePressureMapReport> hardNegatives = maps
            .Where(map => map.IsHardNegativeCandidate)
            .OrderByDescending(map => map.PresenceAfterGuard)
            .ThenByDescending(map => map.PresenceBeforeGuard)
            .ThenByDescending(map => map.MaxRunGeometry)
            .ThenBy(map => map.Map, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();

        IReadOnlyList<TechV2NegativePressureMapReport> strongTechFalsePositives = maps
            .Where(map => map.Disposition == TechV2NegativeReferenceDisposition.KnownNonTech
                && map.StrongTech > 0)
            .OrderByDescending(map => map.StrongTech)
            .ThenByDescending(map => map.PresenceAfterGuard)
            .ThenBy(map => map.Map, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        IReadOnlyList<TechV2NegativeCategorySummary> categorySummaries = BuildCategorySummaries(maps);

        return new TechV2NegativePressureReport(
            FixtureReferences,
            maps,
            geometryDistribution,
            thresholdTradeoff,
            hardNegatives,
            strongTechFalsePositives,
            categorySummaries,
            AssessGeometryGuard(maps, thresholdTradeoff));
    }

    internal static string FormatReport(TechV2NegativePressureReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        StringBuilder text = new();
        text.AppendLine("===== TECH DETECTOR V2 PHASE 2.6 NEGATIVE PRESSURE PACK (TEST-ONLY) =====");
        text.AppendLine("No runtime threshold, feature, weight, hysteresis, region, identity, rating, UI, or ML behavior is changed.");
        text.AppendLine($"Maps discovered: {report.MapsDiscovered} (requested min 20; local availability may be lower)");
        text.AppendLine($"KnownNonTech: {report.KnownNonTechCount} | Unknown: {report.UnknownCount} | Hard negatives: {report.HardNegativeCount}");
        text.AppendLine("Reference categories are sourced only from explicit test/audit references.");
        text.AppendLine();

        text.AppendLine("PER-MAP TABLE");
        text.AppendLine("Map | Difficulty | ReferenceCategory | Windows | StrongTech | TechSupport | SustainedBeforeGuard | SustainedAfterGuard | RejectedRuns | RegionCount | LongestRegion(s) | PresenceBeforeGuard(%) | PresenceAfterGuard(%)");
        foreach (TechV2NegativePressureMapReport map in report.Maps)
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} | {10:F2} | {11:F2} | {12:F2}",
                map.Map,
                map.Difficulty,
                map.ReferenceCategory,
                map.Windows,
                map.StrongTech,
                map.TechSupport,
                map.SustainedTechBeforeGuard,
                map.SustainedTechAfterGuard,
                map.RejectedRuns,
                map.RegionCount,
                map.LongestRegion / 1000.0,
                map.PresenceBeforeGuard,
                map.PresenceAfterGuard));
        }
        text.AppendLine();

        text.AppendLine("HARD NEGATIVE CANDIDATES (KnownNonTech only)");
        foreach (TechV2NegativePressureMapReport map in report.Maps
            .Where(item => item.IsHardNegativeCandidate)
            .OrderByDescending(item => item.PresenceAfterGuard)
            .ThenBy(item => item.Map, StringComparer.OrdinalIgnoreCase))
        {
            text.AppendLine($"{map.Map} [{map.Difficulty}] ({map.ReferenceCategory})");
            if (map.CandidateRuns.Count == 0)
            {
                text.AppendLine("  No sustained candidate run.");
                continue;
            }

            foreach (TechV2NegativePressureRun run in map.CandidateRuns)
            {
                text.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "  {0:F0}->{1:F0}ms | Windows={2} | Avg G/M/S/Mo={3:F3}/{4:F3}/{5:F3}/{6:F3} | StrongTechCount={7} | SupportCount={8} | KeptByGeometryGuard={9}",
                    run.StartTime,
                    run.EndTime,
                    run.WindowCount,
                    run.AverageGeometry,
                    run.AverageMovement,
                    run.AverageSlider,
                    run.AverageMotif,
                    run.StrongTechCount,
                    run.SupportCount,
                    run.KeptByGeometryGuard));
            }
        }
        text.AppendLine();

        text.AppendLine("NON-TECH GEOMETRY DISTRIBUTION (KnownNonTech candidate runs)");
        foreach ((string bucket, int count) in report.GeometryDistributionBuckets)
            text.AppendLine($"{bucket}: {count}");
        text.AppendLine();

        text.AppendLine("THRESHOLD TRADEOFF TABLE");
        text.AppendLine("Threshold | NonTech runs kept | NonTech maps with Presence | Max NonTech Presence | Tech runs kept | Exit Presence | Kira Presence | Frozen Presence");
        foreach (TechV2NegativeThresholdSummary threshold in report.ThresholdTradeoff)
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0:F3} | {1} | {2} | {3:F2}% | {4} | {5:F2}% | {6:F2}% | {7:F2}%",
                threshold.Threshold,
                threshold.NonTechRunsKept,
                threshold.NonTechMapsWithPresence,
                threshold.MaxNonTechPresence,
                threshold.TechRunsKept,
                threshold.ExitPresence,
                threshold.KiraPresence,
                threshold.FrozenPresence));
        }
        text.AppendLine();

        text.AppendLine("TOP HARD NEGATIVES (up to 10)");
        foreach (TechV2NegativePressureMapReport map in report.TopHardNegatives)
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0} | {1} | pre={2:F2}% | post={3:F2}% | maxG={4:F3} | avgG={5:F3} | dominant={6} | strong={7} | cause={8}",
                map.Map,
                map.ReferenceCategory,
                map.PresenceBeforeGuard,
                map.PresenceAfterGuard,
                map.MaxRunGeometry,
                map.AvgRunGeometry,
                map.DominantFamilyPair,
                map.StrongTech > 0 ? "yes" : "no",
                map.MainSuspectedCause));
        }
        text.AppendLine();

        text.AppendLine("STRONGTECH FALSE POSITIVES (KnownNonTech)");
        if (report.StrongTechFalsePositives.Count == 0)
        {
            text.AppendLine("none");
        }
        else
        {
            foreach (TechV2NegativePressureMapReport map in report.StrongTechFalsePositives)
            {
                text.AppendLine($"{map.Map} [{map.Difficulty}] StrongTech={map.StrongTech}");
                foreach (TechV2NegativeStrongTechWindow window in map.StrongTechWindows)
                {
                    text.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "  {0:F0}->{1:F0}ms idx {2}-{3} | pair={4} | evidence G/M/S/Mo={5:F3}/{6:F3}/{7:F3}/{8:F3} | raw geom/rhythm/slider/awk/spacing={9:F3}/{10:F3}/{11:F3}/{12:F3}/{13:F3}",
                        window.StartTime,
                        window.EndTime,
                        window.StartIndex,
                        window.EndIndex,
                        window.DominantFamilyPair,
                        window.GeometryEvidence,
                        window.MovementEvidence,
                        window.SliderEvidence,
                        window.MotifEvidence,
                        window.GeometryIrregularity,
                        window.RhythmIrregularity,
                        window.SliderComplexity,
                        window.Awkwardness,
                        window.SpacingVariation));
                }
            }
        }
        text.AppendLine();

        text.AppendLine("CATEGORY-LEVEL FALSE-POSITIVE ANALYSIS");
        text.AppendLine("Category | map count | maps producing Support | maps producing SustainedTech | maps producing StrongTech | average Presence | maximum Presence");
        foreach (TechV2NegativeCategorySummary category in report.CategorySummaries)
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0} | {1} | {2} | {3} | {4} | {5:F2}% | {6:F2}%",
                category.Family,
                category.MapCount,
                category.MapsProducingSupport,
                category.MapsProducingSustainedTech,
                category.MapsProducingStrongTech,
                category.AveragePresence,
                category.MaximumPresence));
        }
        text.AppendLine();

        text.AppendLine($"Geometry guard assessment: {report.GeometryGuardAssessment}");
        return text.ToString();
    }

    private static MapComputation AnalyzeFixture(TechV2NegativeFixtureReference reference)
    {
        Beatmap map = FixtureLoader.Load(reference.FixtureName);
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
        TechV2SustainedPromotionResult afterGuard =
            TechV2ExperimentalDecisionEngine.PromoteSustainedTechWithDiagnostics(baseDecisions);

        double playableStart = map.HitObjects.FirstOrDefault()?.Time ?? 0;
        double playableEnd = map.HitObjects.LastOrDefault()?.Time ?? 0;
        TechV2PresenceResult beforePresence = TechV2ExperimentalDecisionEngine.BuildPresence(
            beforeGuard.Decisions,
            playableStart,
            playableEnd);
        TechV2PresenceResult afterPresence = TechV2ExperimentalDecisionEngine.BuildPresence(
            afterGuard.Decisions,
            playableStart,
            playableEnd);

        IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> windowsByStart = windows
            .ToDictionary(window => window.StartIndex);

        IReadOnlyList<TechV2NegativePressureRun> runs = FindCandidateRuns(
            beforeGuard.Decisions,
            afterGuard.Decisions);

        IReadOnlyList<TechV2NegativeStrongTechWindow> strongWindows = afterGuard.Decisions
            .Where(decision => decision.IsStrongTech)
            .OrderBy(decision => decision.StartIndex)
            .Select(decision =>
            {
                TechV2ExperimentalWindowResult source = windowsByStart[decision.StartIndex];
                TechV2DecisionCalibrationWindow described =
                    TechV2DecisionCalibrationAudit.Describe(decision);
                return new TechV2NegativeStrongTechWindow(
                    decision.StartTime,
                    decision.EndTime,
                    decision.StartIndex,
                    decision.EndIndex,
                    described.TopTwoPair,
                    decision.GeometryEvidence,
                    decision.MovementEvidence,
                    decision.SliderEvidence,
                    decision.MotifEvidence,
                    source.GeometryIrregularity,
                    source.RhythmIrregularity,
                    source.SliderComplexity,
                    source.Awkwardness,
                    source.SpacingVariation);
            })
            .ToArray();

        int sustainedBefore = beforeGuard.Decisions.Count(decision => decision.IsSustainedTech);
        int sustainedAfter = afterGuard.Decisions.Count(decision => decision.IsSustainedTech);
        int strong = afterGuard.Decisions.Count(decision => decision.IsStrongTech);
        int support = afterGuard.Decisions.Count(decision => decision.IsTechSupport);

        string dominantPair = FindDominantFamilyPair(afterGuard.Decisions);
        double maxRunGeometry = runs.Count == 0 ? 0 : runs.Max(run => run.AverageGeometry);
        double avgRunGeometry = runs.Count == 0 ? 0 : runs.Average(run => run.AverageGeometry);

        bool isHardNegativeCandidate =
            reference.Disposition == TechV2NegativeReferenceDisposition.KnownNonTech
            && (support > 0
                || beforePresence.PresencePercent > 0
                || strong > 0);

        TechV2NegativePressureMapReport report = new(
            reference.FixtureName,
            map.Title,
            map.Version,
            reference.ReferenceCategory,
            reference.Disposition,
            reference.Family,
            windows.Count,
            strong,
            support,
            sustainedBefore,
            sustainedAfter,
            afterGuard.RejectedRuns.Count,
            afterPresence.Regions.Count,
            afterPresence.Regions.Count == 0
                ? 0
                : afterPresence.Regions.Max(region => region.Duration),
            beforePresence.PresencePercent,
            afterPresence.PresencePercent,
            runs,
            strongWindows,
            dominantPair,
            ClassifyMainCause(strong > 0, runs),
            maxRunGeometry,
            avgRunGeometry,
            isHardNegativeCandidate);

        return new MapComputation(
            reference,
            report,
            baseDecisions,
            playableStart,
            playableEnd);
    }

    private static IReadOnlyList<TechV2NegativePressureRun> FindCandidateRuns(
        IReadOnlyList<TechV2WindowDecision> beforeGuard,
        IReadOnlyList<TechV2WindowDecision> currentGuard)
    {
        IReadOnlyDictionary<int, TechV2WindowDecision> currentByIndex = currentGuard
            .ToDictionary(decision => decision.StartIndex);
        List<TechV2NegativePressureRun> runs = [];
        List<TechV2WindowDecision> active = [];

        static bool IsContinuous(TechV2WindowDecision previous, TechV2WindowDecision current)
        {
            return current.StartIndex == previous.StartIndex + TechDetectorV2Experimental.Stride
                && current.StartTime <= previous.EndTime;
        }

        void Flush()
        {
            if (active.Count == 0)
                return;

            double averageGeometry = active.Average(item => item.GeometryEvidence);
            runs.Add(new TechV2NegativePressureRun(
                active[0].StartTime,
                active[^1].EndTime,
                active.Count,
                averageGeometry,
                active.Average(item => item.MovementEvidence),
                active.Average(item => item.SliderEvidence),
                active.Average(item => item.MotifEvidence),
                active.Count(item => item.IsStrongTech),
                active.Count(item => item.IsTechSupport),
                averageGeometry >= 0.266
                    && active.All(item =>
                        currentByIndex[item.StartIndex].IsSustainedTech),
                active.Select(item => item.StartIndex).ToArray()));
            active.Clear();
        }

        foreach (TechV2WindowDecision decision in beforeGuard
            .OrderBy(item => item.StartIndex)
            .ThenBy(item => item.EndIndex))
        {
            if (!decision.IsSustainedTech)
            {
                Flush();
                continue;
            }

            if (active.Count == 0 || IsContinuous(active[^1], decision))
            {
                active.Add(decision);
                continue;
            }

            Flush();
            active.Add(decision);
        }

        Flush();
        return runs;
    }

    private static IReadOnlyDictionary<string, int> BuildGeometryDistribution(
        IEnumerable<MapComputation> knownNonTech)
    {
        Dictionary<string, int> buckets = new(StringComparer.Ordinal)
        {
            ["< .15"] = 0,
            [".15-.20"] = 0,
            [".20-.22"] = 0,
            [".22-.24"] = 0,
            [".24-.25"] = 0,
            [".25-.266"] = 0,
            [".266-.28"] = 0,
            [".28-.30"] = 0,
            [".30-.35"] = 0,
            [">= .35"] = 0,
        };

        foreach (double value in knownNonTech
            .SelectMany(map => map.Report.CandidateRuns)
            .Select(run => run.AverageGeometry))
        {
            string key = value < .15 ? "< .15"
                : value < .20 ? ".15-.20"
                : value < .22 ? ".20-.22"
                : value < .24 ? ".22-.24"
                : value < .25 ? ".24-.25"
                : value < .266 ? ".25-.266"
                : value < .28 ? ".266-.28"
                : value < .30 ? ".28-.30"
                : value < .35 ? ".30-.35"
                : ">= .35";
            buckets[key]++;
        }

        return buckets;
    }

    private static TechV2NegativeThresholdSummary SimulateThreshold(
        IReadOnlyList<MapComputation> maps,
        double threshold)
    {
        IReadOnlyList<(MapComputation Map, TechV2PresenceResult Presence, int Kept, int Rejected)> byMap = maps
            .Select(map =>
            {
                HashSet<int> promotedStartIndices = map.Report.CandidateRuns
                    .Where(run => run.AverageGeometry >= threshold)
                    .SelectMany(run => run.WindowStartIndices)
                    .ToHashSet();
                IReadOnlyList<TechV2WindowDecision> simulated = map.BaseDecisions
                    .Select(decision => decision with
                    {
                        IsSustainedTech = promotedStartIndices.Contains(decision.StartIndex)
                    })
                    .ToArray();
                TechV2PresenceResult presence = TechV2ExperimentalDecisionEngine.BuildPresence(
                    simulated,
                    map.PlayableStart,
                    map.PlayableEnd);
                return (
                    map,
                    presence,
                    map.Report.CandidateRuns.Count(run => run.AverageGeometry >= threshold),
                    map.Report.CandidateRuns.Count(run => run.AverageGeometry < threshold));
            })
            .ToArray();

        IReadOnlyList<(MapComputation Map, TechV2PresenceResult Presence, int Kept, int Rejected)> nonTech = byMap
            .Where(entry => entry.Map.Reference.Disposition == TechV2NegativeReferenceDisposition.KnownNonTech)
            .ToArray();
        IReadOnlyList<(MapComputation Map, TechV2PresenceResult Presence, int Kept, int Rejected)> tech = byMap
            .Where(entry => entry.Map.Reference.Disposition == TechV2NegativeReferenceDisposition.TechReference)
            .ToArray();

        double exitPresence = FindPresenceByFixtureName(
            tech,
            "Exit This Earth's Atmosphere [Primordial Nucleosynthesis].osu");
        double kiraPresence = FindPresenceByFixtureName(
            tech,
            "Kira Killer [Mocaotic's Insane].osu");
        double frozenPresence = FindPresenceByFixtureName(
            tech,
            "Frozen [Collab Insane].osu");

        return new TechV2NegativeThresholdSummary(
            threshold,
            nonTech.Sum(entry => entry.Kept),
            nonTech.Sum(entry => entry.Rejected),
            nonTech.Count(entry => entry.Presence.PresencePercent > 0),
            nonTech.Count == 0 ? 0 : nonTech.Max(entry => entry.Presence.PresencePercent),
            nonTech.Count == 0 ? 0 : nonTech.Average(entry => entry.Presence.PresencePercent),
            tech.Sum(entry => entry.Kept),
            tech.Sum(entry => entry.Rejected),
            exitPresence,
            kiraPresence,
            frozenPresence);
    }

    private static double FindPresenceByFixtureName(
        IEnumerable<(MapComputation Map, TechV2PresenceResult Presence, int Kept, int Rejected)> maps,
        string fixtureName)
    {
        return maps
            .Single(entry => entry.Map.Reference.FixtureName.Equals(fixtureName, StringComparison.Ordinal))
            .Presence
            .PresencePercent;
    }

    private static IReadOnlyList<TechV2NegativeCategorySummary> BuildCategorySummaries(
        IReadOnlyList<TechV2NegativePressureMapReport> maps)
    {
        return Enum.GetValues<TechV2NegativeReferenceFamily>()
            .Select(category =>
            {
                IReadOnlyList<TechV2NegativePressureMapReport> categoryMaps = maps
                    .Where(map => map.Family == category)
                    .ToArray();
                return new TechV2NegativeCategorySummary(
                    category,
                    categoryMaps.Count,
                    categoryMaps.Count(map => map.TechSupport > 0),
                    categoryMaps.Count(map => map.SustainedTechAfterGuard > 0),
                    categoryMaps.Count(map => map.StrongTech > 0),
                    categoryMaps.Count == 0 ? 0 : categoryMaps.Average(map => map.PresenceAfterGuard),
                    categoryMaps.Count == 0 ? 0 : categoryMaps.Max(map => map.PresenceAfterGuard));
            })
            .ToArray();
    }

    private static string FindDominantFamilyPair(
        IReadOnlyList<TechV2WindowDecision> decisions)
    {
        return decisions
            .Where(decision => decision.IsDetectedTech || decision.IsTechSupport)
            .Select(decision => TechV2DecisionCalibrationAudit.Describe(decision).TopTwoPair)
            .GroupBy(pair => pair, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.Key)
            .FirstOrDefault() ?? "none";
    }

    private static string ClassifyMainCause(
        bool hasStrongTech,
        IReadOnlyList<TechV2NegativePressureRun> runs)
    {
        if (hasStrongTech && runs.Count == 0)
            return "isolated StrongTech";

        if (runs.Count == 0)
            return "unknown";

        double movement = runs.Average(run => run.AverageMovement);
        double slider = runs.Average(run => run.AverageSlider);
        double geometry = runs.Average(run => run.AverageGeometry);

        if (movement >= 0.45 && slider >= 0.45)
            return "Movement+Slider";
        if (slider >= 0.50)
            return "Slider topology";
        if (geometry >= 0.266)
            return "Geometry";
        if (runs.Any(run => run.SupportCount > 0))
            return "sustained support";
        if (hasStrongTech)
            return "isolated StrongTech";

        return "unknown";
    }

    private static string AssessGeometryGuard(
        IReadOnlyList<TechV2NegativePressureMapReport> maps,
        IReadOnlyList<TechV2NegativeThresholdSummary> thresholds)
    {
        IReadOnlyList<TechV2NegativePressureMapReport> knownNonTech = maps
            .Where(map => map.Disposition == TechV2NegativeReferenceDisposition.KnownNonTech)
            .ToArray();
        IReadOnlyList<TechV2NegativePressureMapReport> techRefs = maps
            .Where(map => map.Disposition == TechV2NegativeReferenceDisposition.TechReference)
            .ToArray();

        TechV2NegativeThresholdSummary current = thresholds.Single(item => item.Threshold == 0.266);
        bool anyNonTechStillPresent = knownNonTech.Any(map => map.PresenceAfterGuard > 0);
        bool techStillPresent = techRefs.All(map => map.PresenceAfterGuard > 0);

        if (techStillPresent && !anyNonTechStillPresent)
            return "PROMISING";
        if (current.TechRunsKept == 0 && techStillPresent)
            return "TOO STRICT";
        if (current.NonTechMapsWithPresence > 0)
            return "TOO PERMISSIVE";
        return "INCONCLUSIVE";
    }

    private sealed record MapComputation(
        TechV2NegativeFixtureReference Reference,
        TechV2NegativePressureMapReport Report,
        IReadOnlyList<TechV2WindowDecision> BaseDecisions,
        double PlayableStart,
        double PlayableEnd);
}