using BeatInsight.Analysis;
using BeatInsight.Models;
using BeatInsight.Parser;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.IO;
using System.Text;

namespace BeatInsight.Tests.Analysis;

internal enum SliderVariant
{
    Baseline,
    SliderA_MinSupport,
    SliderB_DensityWeight,
    SliderC_ComplexCountSupport,
}

internal sealed record ShadowWindowCandidate(
    int StartIndex,
    int EndIndex,
    double StartTime,
    double EndTime,
    int SliderCount,
    int ComplexSliderCount,
    double ComplexRatio,
    double BaselineSliderComplexity,
    double CandidateSliderComplexity,
    double BaselineSliderEvidence,
    double CandidateSliderEvidence,
    TechV2WindowDecision Decision);

internal sealed record ShadowMapResult(
    string Name,
    string Path,
    bool IsTrueTech,
    int TotalWindows,
    double PlayableDuration,
    double TotalTechDuration,
    double PresencePercent,
    int StrongTechCount,
    int SustainedTechCount,
    int SupportCount,
    double AvgSliderEvidence,
    double PctSliderGte045,
    IReadOnlyList<ShadowWindowCandidate> WindowCandidates);

internal static class TechV2SliderDensityCalibrationAudit
{
    private const double ModerateThreshold = 0.45;
    private const double StrongThreshold = 0.60;
    private const double ExceptionalThreshold = 0.80;

    private const double SliderComplexityWeight = 0.55;
    private const double SliderToSliderWeight = 0.25;
    private const double ComplexSliderRunWeight = 0.20;

    internal static readonly (string Name, string Path, bool IsTrueTech)[] Corpus = [
        ("Silhouette [Kage]", @"D:\osu!\Songs\399358 KANA-BOON - Silhouette [no video]\KANA-BOON - Silhouette (Kagetsu) [Kage].osu", false),
        ("Move That Body [RLC's Extra]", @"D:\osu!\Songs\220220 Sota Fujimori - Move That Body -Extended Mix-\Sota Fujimori - Move That Body -Extended Mix- (Amamiya Yuko) [RLC's Extra].osu", false),
        ("Premonition [kodoku's Insane]", @"D:\osu!\Songs\1698774 Eminem - Premonition (Intro)\Eminem - Premonition (Intro) (hehe) [kodoku's Insane].osu", false),
        ("Exit [Primordial Nucleosynthesis]", Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps", "Exit This Earth's Atmosphere [Primordial Nucleosynthesis].osu"), true),
        ("Kira Killer [Mocaotic's Insane]", Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps", "Kira Killer [Mocaotic's Insane].osu"), true),
        ("Frozen [Collab Insane]", Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps", "Frozen [Collab Insane].osu"), true),
        ("The World Ends Now [0pp's Last ExtrA]", @"D:\osu!\Songs\1051133 Akira Complex - The World Ends Now\Akira Complex - The World Ends Now (nisenoji) [0pp's Last ExtrA].osu", true),
        ("GLORIOUS DAYS [Garden's Insane]", @"D:\osu!\Songs\1048362 yuiko - GLORIOUS DAYS (Short Ver)\yuiko - GLORIOUS DAYS (Short Ver.) (Mimari) [Garden's Insane].osu", true),
    ];

    internal static double ComputeCandidateSliderComplexity(
        int sliderCount,
        int complexSliderCount,
        double baselineSliderComplexity,
        SliderVariant variant)
    {
        if (sliderCount == 0) return 0;

        return variant switch
        {
            SliderVariant.Baseline => baselineSliderComplexity,
            SliderVariant.SliderA_MinSupport => sliderCount switch
            {
                1 => baselineSliderComplexity * 0.25,
                _ => baselineSliderComplexity
            },
            SliderVariant.SliderB_DensityWeight => baselineSliderComplexity * Math.Clamp((double)sliderCount / TechDetectorV2Experimental.WindowSize, 0, 1),
            SliderVariant.SliderC_ComplexCountSupport => baselineSliderComplexity * Math.Clamp((double)complexSliderCount / 3.0, 0, 1),
            _ => baselineSliderComplexity,
        };
    }

    internal static double ComputeCandidateSliderEvidence(
        double candidateSliderComplexity,
        TechV2ExperimentalWindowResult window)
    {
        double normalizedComplexSliderRun = Math.Clamp(
            (double)window.ComplexSliderRunLength / TechDetectorV2Experimental.WindowSize,
            0,
            1);

        return Math.Clamp(
            (candidateSliderComplexity * SliderComplexityWeight)
            + (window.ComplexSliderTransitionTopology.SliderToSliderRatio * SliderToSliderWeight)
            + (normalizedComplexSliderRun * ComplexSliderRunWeight),
            0,
            1);
    }

    internal static ShadowMapResult EvaluateMap(string name, string path, bool isTrueTech, SliderVariant variant)
    {
        Beatmap bm = BeatmapParser.Load(path);
        var windows = TechDetectorV2Experimental.Analyze(
            bm.HitObjects, GameplayAnalyzer.IsSpinner, GameplayAnalyzer.IsSlider,
            GameplayAnalyzer.IsComplexSlider, GameplayAnalyzer.GetObjectStartPosition,
            GameplayAnalyzer.GetObjectEndPosition, GameplayAnalyzer.GetTechTurnAngle);

        double start = bm.HitObjects.FirstOrDefault()?.Time ?? 0;
        double end = bm.HitObjects.LastOrDefault()?.Time ?? 0;

        List<ShadowWindowCandidate> candidateList = [];
        List<TechV2WindowDecision> decisions = [];

        foreach (var w in windows)
        {
            int sliderCount = 0;
            int complexSliderCount = 0;
            for (int i = w.StartIndex; i <= w.EndIndex; i++)
            {
                var ho = bm.HitObjects[i];
                if (GameplayAnalyzer.IsSlider(ho))
                {
                    sliderCount++;
                    if (GameplayAnalyzer.IsComplexSlider(ho)) complexSliderCount++;
                }
            }

            double complexRatio = sliderCount > 0 ? (double)complexSliderCount / sliderCount : 0;
            double candComplexity = ComputeCandidateSliderComplexity(sliderCount, complexSliderCount, w.SliderComplexity, variant);
            double baseSliderEvidence = ComputeCandidateSliderEvidence(w.SliderComplexity, w);
            double candSliderEvidence = ComputeCandidateSliderEvidence(candComplexity, w);

            // Reconstruct decision with candidate SliderEvidence
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
                w.StartIndex,
                w.EndIndex,
                w.StartTime,
                w.EndTime,
                isStrongTech,
                geomEvidence,
                moveEvidence,
                candSliderEvidence,
                motifEvidence,
                modCount,
                families.Average(),
                isTechSupport,
                false);

            decisions.Add(decision);
            candidateList.Add(new ShadowWindowCandidate(
                w.StartIndex, w.EndIndex, w.StartTime, w.EndTime,
                sliderCount, complexSliderCount, complexRatio,
                w.SliderComplexity, candComplexity,
                baseSliderEvidence, candSliderEvidence, decision));
        }

        var promoted = TechV2ExperimentalDecisionEngine.PromoteSustainedTechWithDiagnostics(decisions);
        var presence = TechV2ExperimentalDecisionEngine.BuildPresence(promoted.Decisions, start, end);

        int strongCount = promoted.Decisions.Count(d => d.IsStrongTech);
        int sustainedCount = promoted.Decisions.Count(d => d.IsSustainedTech);
        int supportCount = decisions.Count(d => d.IsTechSupport);
        double avgSliderEvidence = candidateList.Average(c => c.CandidateSliderEvidence);
        double pctSliderGte045 = candidateList.Count(c => c.CandidateSliderEvidence >= ModerateThreshold) * 100.0 / candidateList.Count;

        return new ShadowMapResult(
            name, path, isTrueTech, windows.Count,
            presence.PlayableDuration, presence.TotalTechDuration, presence.PresencePercent,
            strongCount, sustainedCount, supportCount, avgSliderEvidence, pctSliderGte045, candidateList);
    }

    internal static string RunAudit()
    {
        StringBuilder sb = new();
        sb.AppendLine("================================================================================");
        sb.AppendLine("    TECH V2 — PHASE 2.28 SLIDER EVIDENCE DENSITY CALIBRATION FAST AUDIT         ");
        sb.AppendLine("================================================================================");
        sb.AppendLine();

        SliderVariant[] variants = [
            SliderVariant.Baseline,
            SliderVariant.SliderA_MinSupport,
            SliderVariant.SliderB_DensityWeight,
            SliderVariant.SliderC_ComplexCountSupport,
        ];

        Dictionary<SliderVariant, List<ShadowMapResult>> variantResults = [];
        foreach (var v in variants)
        {
            variantResults[v] = [];
            foreach (var (name, path, isTrue) in Corpus)
            {
                variantResults[v].Add(EvaluateMap(name, path, isTrue, v));
            }
        }

        // Section 1: Variant Metrics Summary across all maps
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("SECTION 1: CORPUS EVALUATION BY VARIANT");
        sb.AppendLine("--------------------------------------------------------------------------------");

        foreach (var v in variants)
        {
            sb.AppendLine($"=== VARIANT: {v} ===");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0,-38} | {1,8} | {2,6} | {3,9} | {4,10} | {5,8} | {6,10}",
                "Map", "Presence", "Strong", "Sustained", "AvgSliderEv", "% >=0.45", "TrueTech?"));
            sb.AppendLine(new string('-', 98));

            foreach (var mapRes in variantResults[v])
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-38} | {1,7:F2}% | {2,6} | {3,9} | {4,10:F3} | {5,7:F1}% | {6,10}",
                    mapRes.Name, mapRes.PresencePercent, mapRes.StrongTechCount, mapRes.SustainedTechCount,
                    mapRes.AvgSliderEvidence, mapRes.PctSliderGte045, mapRes.IsTrueTech ? "YES" : "NO"));
            }
            sb.AppendLine();
        }

        // Section 2: Top 5 Silhouette Windows Detailed Comparison
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("SECTION 2: TOP 5 SILHOUETTE WINDOWS SLIDER BREAKDOWN");
        sb.AppendLine("--------------------------------------------------------------------------------");
        var silBaseline = variantResults[SliderVariant.Baseline].First(r => r.Name.Contains("Silhouette"));
        var silA = variantResults[SliderVariant.SliderA_MinSupport].First(r => r.Name.Contains("Silhouette"));
        var silB = variantResults[SliderVariant.SliderB_DensityWeight].First(r => r.Name.Contains("Silhouette"));
        var silC = variantResults[SliderVariant.SliderC_ComplexCountSupport].First(r => r.Name.Contains("Silhouette"));

        var top5Indices = silBaseline.WindowCandidates
            .OrderByDescending(c => c.BaselineSliderEvidence)
            .Take(5)
            .Select(c => c.StartIndex)
            .ToList();

        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "{0,-15} | {1,6} | {2,6} | {3,6} | {4,7} | {5,8} | {6,8} | {7,8} | {8,7} | {9,8} | {10,8} | {11,8}",
            "Time (ms)", "SldCnt", "CmpCnt", "CmpRat", "BaseCmp", "CandCmpA", "CandCmpB", "CandCmpC", "BaseEv", "CandEvA", "CandEvB", "CandEvC"));
        sb.AppendLine(new string('-', 125));

        for (int i = 0; i < top5Indices.Count; i++)
        {
            int idx = top5Indices[i];
            var baseC = silBaseline.WindowCandidates.First(c => c.StartIndex == idx);
            var aC = silA.WindowCandidates.First(c => c.StartIndex == idx);
            var bC = silB.WindowCandidates.First(c => c.StartIndex == idx);
            var cC = silC.WindowCandidates.First(c => c.StartIndex == idx);

            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "[{0,5:F0}-{1,5:F0}] | {2,6} | {3,6} | {4,6:F3} | {5,7:F3} | {6,8:F3} | {7,8:F3} | {8,8:F3} | {9,7:F3} | {10,8:F3} | {11,8:F3} | {12,8:F3}",
                baseC.StartTime, baseC.EndTime,
                baseC.SliderCount, baseC.ComplexSliderCount, baseC.ComplexRatio,
                baseC.BaselineSliderComplexity, aC.CandidateSliderComplexity, bC.CandidateSliderComplexity, cC.CandidateSliderComplexity,
                baseC.BaselineSliderEvidence, aC.CandidateSliderEvidence, bC.CandidateSliderEvidence, cC.CandidateSliderEvidence));
        }
        sb.AppendLine();

        // Section 3: Comparative Decision Summary
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("SECTION 3: COMPARATIVE DECISION SUMMARY");
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "{0,-12} | {1,18} | {2,17} | {3,8} | {4,19} | {5,16}",
            "Variant", "SilhouettePresence", "AvgSliderEvidence", ">=0.45 %", "HumanCasesPreserved", "DecisionsChanged"));
        sb.AppendLine(new string('-', 98));

        int trueTechTotal = Corpus.Count(c => c.IsTrueTech);

        foreach (var v in variants)
        {
            var res = variantResults[v];
            var silRes = res.First(r => r.Name.Contains("Silhouette"));
            int trueTechPreserved = res.Count(r => r.IsTrueTech && r.PresencePercent >= 10.0);

            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0,-12} | {1,17:F2}% | {2,17:F3} | {3,7:F1}% | {4,16}/{5} | {6,16}",
                v switch
                {
                    SliderVariant.Baseline => "Baseline",
                    SliderVariant.SliderA_MinSupport => "SLIDER_A",
                    SliderVariant.SliderB_DensityWeight => "SLIDER_B",
                    SliderVariant.SliderC_ComplexCountSupport => "SLIDER_C",
                    _ => v.ToString()
                },
                silRes.PresencePercent,
                silRes.AvgSliderEvidence,
                silRes.PctSliderGte045,
                trueTechPreserved,
                trueTechTotal,
                0));
        }
        sb.AppendLine();

        return sb.ToString();
    }
}
