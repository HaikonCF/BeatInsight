using BeatInsight.Analysis;
using BeatInsight.Models;
using BeatInsight.Parser;
using System.Globalization;
using System.IO;
using System.Text;

namespace BeatInsight.Tests.Analysis;

internal sealed record WindowPTechCDiagnostic(
    int WindowIndex,
    double StartTime,
    double EndTime,
    int SliderCount,
    int PSliderCount,
    int QualifiedPCount,
    double QualifiedRatio,
    double SliderComplexity,
    double SliderEvidence,
    double GeometryEvidence,
    double MovementEvidence,
    int ModerateFamilyCount,
    int StrongFamilyCount,
    bool IsTechSupport,
    bool IsStrongTech,
    bool IsSustainedTech);

internal static class TechV2PSliderWindowAttributionAudit
{
    private const double ModerateThreshold = 0.45;
    private const double StrongThreshold = 0.60;
    private const double ExceptionalThreshold = 0.80;

    internal static readonly (string Key, string Name, string Path)[] Targets = [
        ("Silhouette", "Silhouette [Kage]", @"D:\osu!\Songs\399358 KANA-BOON - Silhouette [no video]\KANA-BOON - Silhouette (Kagetsu) [Kage].osu"),
        ("GloriousDays", "GLORIOUS DAYS [Garden's Insane]", @"D:\osu!\Songs\1048362 yuiko - GLORIOUS DAYS (Short Ver)\yuiko - GLORIOUS DAYS (Short Ver.) (Mimari) [Garden's Insane].osu"),
    ];

    internal static (IReadOnlyList<WindowPTechCDiagnostic> Diagnostics, TechV2PresenceResult Presence)
        AuditMapPTechC(string path)
    {
        Beatmap bm = BeatmapParser.Load(path);
        bool[] complexFlags = new bool[bm.HitObjects.Count];
        for (int i = 0; i < bm.HitObjects.Count; i++)
        {
            complexFlags[i] = TechV2PSliderTechnicalityAudit.IsComplex_PTechC(bm.HitObjects[i], bm.HitObjects, i);
        }

        var windows = TechDetectorV2Experimental.Analyze(
            bm.HitObjects,
            GameplayAnalyzer.IsSpinner,
            GameplayAnalyzer.IsSlider,
            ho =>
            {
                int idx = bm.HitObjects.IndexOf(ho);
                return idx >= 0 && complexFlags[idx];
            },
            GameplayAnalyzer.GetObjectStartPosition,
            GameplayAnalyzer.GetObjectEndPosition,
            GameplayAnalyzer.GetTechTurnAngle);

        double start = bm.HitObjects.FirstOrDefault()?.Time ?? 0;
        double end = bm.HitObjects.LastOrDefault()?.Time ?? 0;

        var baseDecisions = TechV2ExperimentalDecisionEngine.Decide(windows);
        var promoted = TechV2ExperimentalDecisionEngine.PromoteSustainedTechWithDiagnostics(baseDecisions);
        var presence = TechV2ExperimentalDecisionEngine.BuildPresence(promoted.Decisions, start, end);

        List<WindowPTechCDiagnostic> list = [];
        for (int wIdx = 0; wIdx < windows.Count; wIdx++)
        {
            var w = windows[wIdx];
            var d = promoted.Decisions[wIdx];

            int sCount = 0;
            int pCount = 0;
            int qCount = 0;
            for (int i = w.StartIndex; i <= w.EndIndex; i++)
            {
                var ho = bm.HitObjects[i];
                if (GameplayAnalyzer.IsSlider(ho))
                {
                    sCount++;
                    if (ho.SliderCurveType == "P")
                    {
                        pCount++;
                        if (complexFlags[i]) qCount++;
                    }
                }
            }

            double qRatio = sCount > 0 ? (double)qCount / sCount : 0;

            list.Add(new WindowPTechCDiagnostic(
                wIdx, w.StartTime, w.EndTime, sCount, pCount, qCount, qRatio,
                w.SliderComplexity, d.SliderEvidence, d.GeometryEvidence, d.MovementEvidence,
                d.EvidenceFamilyCount,
                (new[] { d.GeometryEvidence, d.MovementEvidence, d.SliderEvidence, d.MotifEvidence }).Count(v => v >= StrongThreshold),
                d.IsTechSupport, d.IsStrongTech, d.IsSustainedTech));
        }

        return (list, presence);
    }

    // Continuous P-Score calculation
    internal static double ComputePScoreA(HitObject ho, IReadOnlyList<HitObject> all, int index)
    {
        if (!GameplayAnalyzer.IsSlider(ho)) return 0;
        if (ho.SliderCurveType == "C" || ho.Slides >= 3 || (ho.SliderCurveType == "B" && ho.SliderControlPoints.Count >= 3))
            return 1.0;
        if (ho.SliderCurveType != "P") return 0.0;

        if (index == 0 || index + 1 >= all.Count) return 0.0;

        var prev = all[index - 1];
        var next = all[index + 1];
        var (startX, startY) = GameplayAnalyzer.GetObjectStartPosition(ho);
        var (endX, endY) = GameplayAnalyzer.GetObjectEndPosition(ho);
        var (pEndX, pEndY) = GameplayAnalyzer.GetObjectEndPosition(prev);
        var (nStartX, nStartY) = GameplayAnalyzer.GetObjectStartPosition(next);

        double turnAngle = GameplayAnalyzer.GetTechTurnAngle(prev, ho, next);
        double beforeDist = Math.Sqrt(Math.Pow(startX - pEndX, 2) + Math.Pow(startY - pEndY, 2));
        double afterDist = Math.Sqrt(Math.Pow(nStartX - endX, 2) + Math.Pow(nStartY - endY, 2));
        double maxDist = Math.Max(1.0, Math.Max(beforeDist, afterDist));
        double spacingVar = Math.Abs(afterDist - beforeDist) / maxDist;

        // Angle awkwardness: peak at 85 deg, drops for wide angles (>130 deg) and very flat angles (<30 deg)
        double angleScore = Math.Clamp(1.0 - Math.Abs(turnAngle - 85.0) / 55.0, 0, 1);
        double spacingScore = Math.Clamp((spacingVar - 0.25) / 0.50, 0, 1);

        // Travel curvature loop bonus
        double displacement = Math.Sqrt(Math.Pow(endX - startX, 2) + Math.Pow(endY - startY, 2));
        double travelRatio = ho.Length > 0 ? displacement / ho.Length : 1.0;
        double travelScore = Math.Clamp((1.0 - travelRatio) / 0.40, 0, 1);

        return Math.Clamp(0.50 * angleScore + 0.35 * spacingScore + 0.15 * travelScore, 0, 1);
    }

    internal static double ComputePScoreB(HitObject ho, IReadOnlyList<HitObject> all, int index)
    {
        double scoreA = ComputePScoreA(ho, all, index);
        if (scoreA < 0.25 || index == 0 || index + 1 >= all.Count) return scoreA;

        var prev = all[index - 1];
        var next = all[index + 1];
        double intBefore = Math.Max(1.0, ho.Time - prev.Time);
        double intAfter = Math.Max(1.0, next.Time - ho.Time);
        double rhythmVar = Math.Abs(intAfter - intBefore) / Math.Max(intBefore, intAfter);

        // Rhythm is gated: only applies if base geometry/spacing is already non-trivial
        double rhythmBonus = Math.Clamp((rhythmVar - 0.25) / 0.50, 0, 1) * 0.25;
        return Math.Clamp(scoreA * 0.85 + rhythmBonus, 0, 1);
    }

    internal static (TechV2PresenceResult Presence, double AvgSliderEvidence, double PctGte045)
        EvaluateContinuousPrototype(string path, Func<HitObject, IReadOnlyList<HitObject>, int, double> scoreFunc)
    {
        Beatmap bm = BeatmapParser.Load(path);
        double[] scores = new double[bm.HitObjects.Count];
        for (int i = 0; i < bm.HitObjects.Count; i++)
        {
            scores[i] = scoreFunc(bm.HitObjects[i], bm.HitObjects, i);
        }

        var windows = TechDetectorV2Experimental.Analyze(
            bm.HitObjects,
            GameplayAnalyzer.IsSpinner,
            GameplayAnalyzer.IsSlider,
            _ => false, // do not use binary complex flag
            GameplayAnalyzer.GetObjectStartPosition,
            GameplayAnalyzer.GetObjectEndPosition,
            GameplayAnalyzer.GetTechTurnAngle);

        double start = bm.HitObjects.FirstOrDefault()?.Time ?? 0;
        double end = bm.HitObjects.LastOrDefault()?.Time ?? 0;

        List<TechV2WindowDecision> decisions = [];
        List<double> candidateSliderEvidences = [];

        for (int wIdx = 0; wIdx < windows.Count; wIdx++)
        {
            var w = windows[wIdx];
            int sliderCount = 0;
            double scoreSum = 0;
            double slideIntensity = 0;

            for (int i = w.StartIndex; i <= w.EndIndex; i++)
            {
                var ho = bm.HitObjects[i];
                if (GameplayAnalyzer.IsSlider(ho))
                {
                    sliderCount++;
                    scoreSum += scores[i];
                    slideIntensity += Math.Clamp((ho.Slides - 1) / 3.0, 0, 1);
                }
            }

            double avgPTech = sliderCount > 0 ? scoreSum / sliderCount : 0;
            double avgIntensity = sliderCount > 0 ? slideIntensity / sliderCount : 0;
            double candidateSliderComplexity = Math.Clamp(avgPTech * 0.80 + avgIntensity * 0.20, 0, 1);

            double normalizedComplexSliderRun = Math.Clamp(
                (double)w.ComplexSliderRunLength / TechDetectorV2Experimental.WindowSize, 0, 1);

            double candSliderEvidence = Math.Clamp(
                (candidateSliderComplexity * 0.55)
                + (w.ComplexSliderTransitionTopology.SliderToSliderRatio * 0.25)
                + (normalizedComplexSliderRun * 0.20),
                0, 1);

            candidateSliderEvidences.Add(candSliderEvidence);

            double geomEvidence = Math.Clamp((w.GeometryIrregularity * 0.65) + (w.AngleCoherenceWithSupport * 0.35), 0, 1);
            double moveEvidence = Math.Clamp((w.RhythmIrregularity * 0.45) + (w.Awkwardness * 0.35) + (w.SpacingVariation * 0.20), 0, 1);
            double motifEvidence = Math.Clamp(w.CoupledTechnicalMotifContinuity, 0, 1);

            double[] families = [geomEvidence, moveEvidence, candSliderEvidence, motifEvidence];
            int modCount = families.Count(f => f >= ModerateThreshold);
            int strCount = families.Count(f => f >= StrongThreshold);
            bool hasExceptional = families.Any(f => f >= ExceptionalThreshold);

            bool isStrongTech = strCount >= 2 || (hasExceptional && modCount >= 2);
            bool isTechSupport = !isStrongTech && modCount >= 2;

            decisions.Add(new TechV2WindowDecision(
                w.StartIndex, w.EndIndex, w.StartTime, w.EndTime,
                isStrongTech, geomEvidence, moveEvidence, candSliderEvidence, motifEvidence,
                modCount, families.Average(), isTechSupport, false));
        }

        var promoted = TechV2ExperimentalDecisionEngine.PromoteSustainedTechWithDiagnostics(decisions);
        var presence = TechV2ExperimentalDecisionEngine.BuildPresence(promoted.Decisions, start, end);

        double avgEv = candidateSliderEvidences.Average();
        double pctGte045 = candidateSliderEvidences.Count(e => e >= ModerateThreshold) * 100.0 / candidateSliderEvidences.Count;

        return (presence, avgEv, pctGte045);
    }

    internal static string RunFullAudit()
    {
        StringBuilder sb = new();
        sb.AppendLine("================================================================================");
        sb.AppendLine("    TECH V2 — PHASE 2.31 P-SLIDER WINDOW ATTRIBUTION AUDIT                      ");
        sb.AppendLine("================================================================================");
        sb.AppendLine();

        // Section 1: Window-Level Audit of PTECH_C
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("SECTION 1: WINDOW-LEVEL AUDIT OF PTECH_C (SILHOUETTE VS GLORIOUS DAYS)");
        sb.AppendLine("--------------------------------------------------------------------------------");

        var (silDiag, silPres) = AuditMapPTechC(Targets[0].Path);
        var (glorDiag, glorPres) = AuditMapPTechC(Targets[1].Path);

        void PrintWindowSummary(string name, IReadOnlyList<WindowPTechCDiagnostic> diag, TechV2PresenceResult pres)
        {
            sb.AppendLine($"=== MAP: {name} (Presence: {pres.PresencePercent:F2}%, Regions: {pres.Regions.Count}) ===");
            int totalW = diag.Count;
            int wWithGte1Q = diag.Count(d => d.QualifiedPCount >= 1);
            int wWithGte2Q = diag.Count(d => d.QualifiedPCount >= 2);
            double avgQRatio = diag.Average(d => d.QualifiedRatio);
            double avgSliderEv = diag.Average(d => d.SliderEvidence);
            double avgGeomEv = diag.Average(d => d.GeometryEvidence);
            double avgMoveEv = diag.Average(d => d.MovementEvidence);
            int pctSliderGte045 = (int)Math.Round(diag.Count(d => d.SliderEvidence >= ModerateThreshold) * 100.0 / totalW);
            int pctGeomGte045 = (int)Math.Round(diag.Count(d => d.GeometryEvidence >= ModerateThreshold) * 100.0 / totalW);
            int pctMoveGte045 = (int)Math.Round(diag.Count(d => d.MovementEvidence >= ModerateThreshold) * 100.0 / totalW);
            int supportCount = diag.Count(d => d.IsTechSupport);
            int sustainedCount = diag.Count(d => d.IsSustainedTech);
            int strongCount = diag.Count(d => d.IsStrongTech);

            sb.AppendLine($"  Total Windows: {totalW}");
            sb.AppendLine($"  Windows with >=1 Qualified P : {wWithGte1Q} ({wWithGte1Q * 100.0 / totalW:F1}%)");
            sb.AppendLine($"  Windows with >=2 Qualified P : {wWithGte2Q} ({wWithGte2Q * 100.0 / totalW:F1}%)");
            sb.AppendLine($"  Avg Qualified P Ratio        : {avgQRatio:F3}");
            sb.AppendLine($"  Avg SliderEvidence           : {avgSliderEv:F3} (windows >= 0.45: {pctSliderGte045}%)");
            sb.AppendLine($"  Avg GeometryEvidence         : {avgGeomEv:F3} (windows >= 0.45: {pctGeomGte045}%)");
            sb.AppendLine($"  Avg MovementEvidence         : {avgMoveEv:F3} (windows >= 0.45: {pctMoveGte045}%)");
            sb.AppendLine($"  Decision Counts              : Support={supportCount} | Sustained={sustainedCount} | Strong={strongCount}");
            sb.AppendLine();
        }

        PrintWindowSummary(Targets[0].Name, silDiag, silPres);
        PrintWindowSummary(Targets[1].Name, glorDiag, glorPres);

        // Section 2: Detailed Look into GLORIOUS DAYS Windows
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("SECTION 2: GLORIOUS DAYS PTECH_C WINDOW DISTRIBUTION BREAKDOWN");
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "{0,-15} | {1,6} | {2,6} | {3,6} | {4,7} | {5,7} | {6,6} | {7,6} | {8,6} | {9,8} | {10,11}",
            "Time (ms)", "SldCnt", "PCnt", "QCnt", "SldCmp", "SldEv", "GeomEv", "MoveEv", "ModCnt", "IsStrong", "IsSustained"));
        sb.AppendLine(new string('-', 105));

        foreach (var d in glorDiag.Where(d => d.QualifiedPCount >= 2 || d.SliderEvidence >= 0.40).Take(15))
        {
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "[{0,5:F0}-{1,5:F0}] | {2,6} | {3,6} | {4,6} | {5,7:F3} | {6,7:F3} | {7,6:F3} | {8,6:F3} | {9,6} | {10,8} | {11,11}",
                d.StartTime, d.EndTime, d.SliderCount, d.PSliderCount, d.QualifiedPCount,
                d.SliderComplexity, d.SliderEvidence, d.GeometryEvidence, d.MovementEvidence,
                d.ModerateFamilyCount, d.IsStrongTech, d.IsSustainedTech));
        }
        sb.AppendLine();

        // Section 3: Continuous P-Score Prototypes Evaluation
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("SECTION 3: CONTINUOUS P-TECHNICALITY PROTOTYPE EVALUATION");
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "{0,-35} | {1,14} | {2,14} | {3,14}",
            "Metric / Map", "Baseline", "PSCORE_A (Geom+Spac)", "PSCORE_B (+GatedRhythm)"));
        sb.AppendLine(new string('-', 85));

        var (silPresA, silEvA, silPctA) = EvaluateContinuousPrototype(Targets[0].Path, ComputePScoreA);
        var (silPresB, silEvB, silPctB) = EvaluateContinuousPrototype(Targets[0].Path, ComputePScoreB);
        var (glorPresA, glorEvA, glorPctA) = EvaluateContinuousPrototype(Targets[1].Path, ComputePScoreA);
        var (glorPresB, glorEvB, glorPctB) = EvaluateContinuousPrototype(Targets[1].Path, ComputePScoreB);

        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "{0,-35} | {1,13:F2}% | {2,19:F2}% | {3,21:F2}%",
            "Silhouette Presence", silPres.PresencePercent, silPresA.PresencePercent, silPresB.PresencePercent));
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "{0,-35} | {1,14:F3} | {2,20:F3} | {3,22:F3}",
            "Silhouette Avg SliderEvidence", silDiag.Average(d => d.SliderEvidence), silEvA, silEvB));
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "{0,-35} | {1,13:F1}% | {2,19:F1}% | {3,21:F1}%",
            "Silhouette % SliderEv >= 0.45", silDiag.Count(d => d.SliderEvidence >= ModerateThreshold) * 100.0 / silDiag.Count, silPctA, silPctB));
        sb.AppendLine();
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "{0,-35} | {1,13:F2}% | {2,19:F2}% | {3,21:F2}%",
            "GLORIOUS DAYS Presence", glorPres.PresencePercent, glorPresA.PresencePercent, glorPresB.PresencePercent));
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "{0,-35} | {1,14:F3} | {2,20:F3} | {3,22:F3}",
            "GLORIOUS DAYS Avg SliderEvidence", glorDiag.Average(d => d.SliderEvidence), glorEvA, glorEvB));
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "{0,-35} | {1,13:F1}% | {2,19:F1}% | {3,21:F1}%",
            "GLORIOUS DAYS % SliderEv >= 0.45", glorDiag.Count(d => d.SliderEvidence >= ModerateThreshold) * 100.0 / glorDiag.Count, glorPctA, glorPctB));
        sb.AppendLine();

        return sb.ToString();
    }
}
