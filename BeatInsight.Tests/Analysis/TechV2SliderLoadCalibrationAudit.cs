using BeatInsight.Analysis;
using BeatInsight.Models;
using BeatInsight.Parser;
using System.Globalization;
using System.IO;
using System.Text;

namespace BeatInsight.Tests.Analysis;

internal enum LoadAggregator
{
    Baseline,
    LoadA_Sum2,
    LoadB_Sum3,
    LoadC_WeightedTop3,
}

internal sealed record WindowLoadDetail(
    int WindowIndex,
    double StartTime,
    double EndTime,
    int SliderCount,
    int PSliderCount,
    IReadOnlyList<double> IndividualScores,
    double SummedScore,
    double FinalLoad,
    double SliderComplexity,
    double SliderEvidence,
    double GeometryEvidence,
    double MovementEvidence,
    bool IsStrongTech,
    bool IsTechSupport,
    bool IsSustainedTech);

internal sealed record MapLoadEvaluation(
    string Key,
    string Name,
    string Path,
    bool IsTech,
    double PresencePercent,
    int StrongCount,
    int SustainedCount,
    int SupportCount,
    double AvgLoad,
    double AvgSliderEvidence,
    double PctSliderGte045,
    IReadOnlyList<WindowLoadDetail> Windows);

internal static class TechV2SliderLoadCalibrationAudit
{
    private const double ModerateThreshold = 0.45;
    private const double StrongThreshold = 0.60;
    private const double ExceptionalThreshold = 0.80;

    internal static readonly (string Key, string Name, string Path, bool IsTech)[] Corpus = [
        ("Silhouette", "Silhouette [Kage]", @"D:\osu!\Songs\399358 KANA-BOON - Silhouette [no video]\KANA-BOON - Silhouette (Kagetsu) [Kage].osu", false),
        ("GloriousDays", "GLORIOUS DAYS [Garden's Insane]", @"D:\osu!\Songs\1048362 yuiko - GLORIOUS DAYS (Short Ver)\yuiko - GLORIOUS DAYS (Short Ver.) (Mimari) [Garden's Insane].osu", true),
        ("Kira", "Kira Killer [Mocaotic's Insane]", Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps", "Kira Killer [Mocaotic's Insane].osu"), true),
        ("WorldEndsNow", "The World Ends Now [0pp's Last ExtrA]", @"D:\osu!\Songs\1051133 Akira Complex - The World Ends Now\Akira Complex - The World Ends Now (nisenoji) [0pp's Last ExtrA].osu", true),
        ("Frozen", "Frozen [Collab Insane]", Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps", "Frozen [Collab Insane].osu"), true),
        ("Exit", "Exit [Primordial Nucleosynthesis]", Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps", "Exit This Earth's Atmosphere [Primordial Nucleosynthesis].osu"), true),
    ];

    internal static double ComputeTechnicalSliderLoad(IReadOnlyList<double> scores, LoadAggregator aggregator)
    {
        if (scores.Count == 0) return 0;

        return aggregator switch
        {
            LoadAggregator.LoadA_Sum2 => Math.Clamp(scores.Sum() / 2.0, 0, 1),
            LoadAggregator.LoadB_Sum3 => Math.Clamp(scores.Sum() / 3.0, 0, 1),
            LoadAggregator.LoadC_WeightedTop3 => ComputeWeightedTop3(scores),
            _ => 0,
        };
    }

    private static double ComputeWeightedTop3(IReadOnlyList<double> scores)
    {
        var sorted = scores.OrderByDescending(s => s).ToArray();
        double top1 = sorted.Length > 0 ? sorted[0] : 0;
        double top2 = sorted.Length > 1 ? sorted[1] : 0;
        double top3 = sorted.Length > 2 ? sorted[2] : 0;
        return Math.Clamp(top1 + 0.70 * top2 + 0.40 * top3, 0, 1);
    }

    internal static MapLoadEvaluation Evaluate(string key, string name, string path, bool isTech, LoadAggregator aggregator)
    {
        Beatmap bm = BeatmapParser.Load(path);

        // Precompute individual technicality scores
        double[] sliderScores = new double[bm.HitObjects.Count];
        for (int i = 0; i < bm.HitObjects.Count; i++)
        {
            sliderScores[i] = TechV2PSliderWindowAttributionAudit.ComputePScoreB(bm.HitObjects[i], bm.HitObjects, i);
        }

        var windows = TechDetectorV2Experimental.Analyze(
            bm.HitObjects,
            GameplayAnalyzer.IsSpinner,
            GameplayAnalyzer.IsSlider,
            ho => GameplayAnalyzer.IsComplexSlider(ho), // used for baseline comparison if needed
            GameplayAnalyzer.GetObjectStartPosition,
            GameplayAnalyzer.GetObjectEndPosition,
            GameplayAnalyzer.GetTechTurnAngle);

        double start = bm.HitObjects.FirstOrDefault()?.Time ?? 0;
        double end = bm.HitObjects.LastOrDefault()?.Time ?? 0;

        List<TechV2WindowDecision> decisions = [];
        List<WindowLoadDetail> details = [];

        for (int wIdx = 0; wIdx < windows.Count; wIdx++)
        {
            var w = windows[wIdx];
            List<double> scoresInWindow = [];
            int sCount = 0;
            int pCount = 0;
            double slideIntensity = 0;

            for (int i = w.StartIndex; i <= w.EndIndex; i++)
            {
                var ho = bm.HitObjects[i];
                if (GameplayAnalyzer.IsSlider(ho))
                {
                    sCount++;
                    scoresInWindow.Add(sliderScores[i]);
                    if (ho.SliderCurveType == "P") pCount++;
                    slideIntensity += Math.Clamp((ho.Slides - 1) / 3.0, 0, 1);
                }
            }

            double load = 0;
            double candComplexity = 0;

            if (aggregator == LoadAggregator.Baseline)
            {
                candComplexity = w.SliderComplexity;
                load = sCount > 0 ? (double)scoresInWindow.Count(s => s >= 0.50) / sCount : 0;
            }
            else
            {
                load = ComputeTechnicalSliderLoad(scoresInWindow, aggregator);
                double avgIntensity = sCount > 0 ? slideIntensity / sCount : 0;
                candComplexity = Math.Clamp(load * 0.80 + avgIntensity * 0.20, 0, 1);
            }

            double normalizedComplexSliderRun = Math.Clamp((double)w.ComplexSliderRunLength / TechDetectorV2Experimental.WindowSize, 0, 1);
            double candSliderEvidence = Math.Clamp(
                (candComplexity * 0.55)
                + (w.ComplexSliderTransitionTopology.SliderToSliderRatio * 0.25)
                + (normalizedComplexSliderRun * 0.20),
                0, 1);

            double geomEvidence = Math.Clamp((w.GeometryIrregularity * 0.65) + (w.AngleCoherenceWithSupport * 0.35), 0, 1);
            double moveEvidence = Math.Clamp((w.RhythmIrregularity * 0.45) + (w.Awkwardness * 0.35) + (w.SpacingVariation * 0.20), 0, 1);
            double motifEvidence = Math.Clamp(w.CoupledTechnicalMotifContinuity, 0, 1);

            double[] families = [geomEvidence, moveEvidence, candSliderEvidence, motifEvidence];
            int modCount = families.Count(f => f >= ModerateThreshold);
            int strCount = families.Count(f => f >= StrongThreshold);
            bool hasExceptional = families.Any(f => f >= ExceptionalThreshold);

            bool isStrongTech = strCount >= 2 || (hasExceptional && modCount >= 2);
            bool isTechSupport = !isStrongTech && modCount >= 2;

            var decision = new TechV2WindowDecision(
                w.StartIndex, w.EndIndex, w.StartTime, w.EndTime,
                isStrongTech, geomEvidence, moveEvidence, candSliderEvidence, motifEvidence,
                modCount, families.Average(), isTechSupport, false);

            decisions.Add(decision);
            details.Add(new WindowLoadDetail(
                wIdx, w.StartTime, w.EndTime, sCount, pCount, scoresInWindow,
                scoresInWindow.Sum(), load, candComplexity, candSliderEvidence,
                geomEvidence, moveEvidence, isStrongTech, isTechSupport, false));
        }

        var promoted = TechV2ExperimentalDecisionEngine.PromoteSustainedTechWithDiagnostics(decisions);
        var presence = TechV2ExperimentalDecisionEngine.BuildPresence(promoted.Decisions, start, end);

        // Update details with sustained promotion flags
        for (int i = 0; i < details.Count; i++)
        {
            var pDec = promoted.Decisions[i];
            details[i] = details[i] with { IsSustainedTech = pDec.IsSustainedTech };
        }

        int strongCount = promoted.Decisions.Count(d => d.IsStrongTech);
        int sustainedCount = promoted.Decisions.Count(d => d.IsSustainedTech);
        int supportCount = decisions.Count(d => d.IsTechSupport);
        double avgLoad = details.Count > 0 ? details.Average(d => d.FinalLoad) : 0;
        double avgSliderEv = details.Count > 0 ? details.Average(d => d.SliderEvidence) : 0;
        double pctGte045 = details.Count > 0 ? details.Count(d => d.SliderEvidence >= ModerateThreshold) * 100.0 / details.Count : 0;

        return new MapLoadEvaluation(
            key, name, path, isTech, presence.PresencePercent,
            strongCount, sustainedCount, supportCount, avgLoad, avgSliderEv, pctGte045, details);
    }

    internal static string RunFullAudit()
    {
        StringBuilder sb = new();
        sb.AppendLine("================================================================================");
        sb.AppendLine("    TECH V2 — PHASE 2.32 TECHNICAL SLIDER LOAD CALIBRATION AUDIT                ");
        sb.AppendLine("================================================================================");
        sb.AppendLine();

        LoadAggregator[] aggregators = [
            LoadAggregator.Baseline,
            LoadAggregator.LoadA_Sum2,
            LoadAggregator.LoadB_Sum3,
            LoadAggregator.LoadC_WeightedTop3,
        ];

        Dictionary<LoadAggregator, List<MapLoadEvaluation>> evals = [];
        var availableCorpus = Corpus.Where(c => File.Exists(c.Path)).ToArray();
        if (availableCorpus.Length == 0)
        {
            sb.AppendLine("No corpus maps are available; calibration tables were not generated.");
            return sb.ToString();
        }

        foreach (var agg in aggregators)
        {
            evals[agg] = [];
            foreach (var (key, name, path, isTech) in availableCorpus)
            {
                evals[agg].Add(Evaluate(key, name, path, isTech, agg));
            }
        }

        // Section 1: End-to-End Presence Table
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("SECTION 1: END-TO-END TECH PRESENCE BY AGGREGATOR");
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "{0,-38} | {1,10} | {2,10} | {3,10} | {4,10}",
            "Map", "Baseline", "LOAD_A (/2)", "LOAD_B (/3)", "LOAD_C (Top3)"));
        sb.AppendLine(new string('-', 85));

        for (int i = 0; i < availableCorpus.Length; i++)
        {
            var c = availableCorpus[i];
            double pBase = evals[LoadAggregator.Baseline][i].PresencePercent;
            double pA = evals[LoadAggregator.LoadA_Sum2][i].PresencePercent;
            double pB = evals[LoadAggregator.LoadB_Sum3][i].PresencePercent;
            double pC = evals[LoadAggregator.LoadC_WeightedTop3][i].PresencePercent;

            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0,-38} | {1,9:F2}% | {2,9:F2}% | {3,9:F2}% | {4,9:F2}%",
                c.Name, pBase, pA, pB, pC));
        }
        sb.AppendLine();

        // Section 2: Detailed Metrics Table per Variant
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("SECTION 2: DETAILED METRICS BY VARIANT");
        sb.AppendLine("--------------------------------------------------------------------------------");
        foreach (var agg in aggregators)
        {
            sb.AppendLine($"=== AGGREGATOR: {agg} ===");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0,-38} | {1,8} | {2,6} | {3,9} | {4,8} | {5,10} | {6,8}",
                "Map", "Presence", "Strong", "Sustained", "AvgLoad", "AvgSliderEv", "% >=0.45"));
            sb.AppendLine(new string('-', 98));

            foreach (var r in evals[agg])
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-38} | {1,7:F2}% | {2,6} | {3,9} | {4,8:F3} | {5,10:F3} | {6,7:F1}%",
                    r.Name, r.PresencePercent, r.StrongCount, r.SustainedCount,
                    r.AvgLoad, r.AvgSliderEvidence, r.PctSliderGte045));
            }
            sb.AppendLine();
        }

        // Section 3: Top 10 Windows Comparison for Silhouette and GLORIOUS DAYS
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("SECTION 3: TOP 10 LOAD WINDOWS (SILHOUETTE VS GLORIOUS DAYS under LOAD_A)");
        sb.AppendLine("--------------------------------------------------------------------------------");

        void PrintTop10(string title, MapLoadEvaluation mapEval)
        {
            sb.AppendLine($"=== {title} ===");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0,-15} | {1,4} | {2,4} | {3,-28} | {4,6} | {5,6} | {6,6} | {7,8} | {8,10}",
                "Time (ms)", "Sld", "P", "Individual P Scores", "SumScr", "Load", "SldEv", "IsStrong", "IsSustained"));
            sb.AppendLine(new string('-', 102));

            var top10 = mapEval.Windows.OrderByDescending(w => w.FinalLoad).Take(10).ToList();
            foreach (var w in top10)
            {
                string scoresStr = string.Join(",", w.IndividualScores.Take(4).Select(s => s.ToString("F2", CultureInfo.InvariantCulture)));
                if (w.IndividualScores.Count > 4) scoresStr += "...";

                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "[{0,5:F0}-{1,5:F0}] | {2,4} | {3,4} | {4,-28} | {5,6:F2} | {6,6:F3} | {7,6:F3} | {8,8} | {9,10}",
                    w.StartTime, w.EndTime, w.SliderCount, w.PSliderCount, scoresStr,
                    w.SummedScore, w.FinalLoad, w.SliderEvidence, w.IsStrongTech, w.IsSustainedTech));
            }
            sb.AppendLine();
        }

        var silhouette = evals[LoadAggregator.LoadA_Sum2].FirstOrDefault(e => e.Key == "Silhouette");
        var glorious = evals[LoadAggregator.LoadA_Sum2].FirstOrDefault(e => e.Key == "GloriousDays");
        if (silhouette is not null) PrintTop10("Silhouette [Kage] (LOAD_A)", silhouette);
        if (glorious is not null) PrintTop10("GLORIOUS DAYS [Garden's Insane] (LOAD_A)", glorious);

        return sb.ToString();
    }
}
