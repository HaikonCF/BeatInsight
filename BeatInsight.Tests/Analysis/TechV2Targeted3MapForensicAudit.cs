using BeatInsight.Analysis;
using BeatInsight.Models;
using BeatInsight.Parser;
using System.Globalization;
using System.IO;
using System.Text;

namespace BeatInsight.Tests.Analysis;

internal sealed record TargetedMapAuditResult(
    string Name,
    string Path,
    Beatmap Beatmap,
    IReadOnlyList<TechV2ExperimentalWindowResult> Windows,
    IReadOnlyList<TechV2WindowDecision> BaseDecisions,
    TechV2SustainedPromotionResult SustainedPromotion,
    TechV2PresenceResult Presence);

internal static class TechV2Targeted3MapForensicAudit
{
    private const double ModerateThreshold = 0.45;
    private const double StrongThreshold = 0.60;
    private const double ExceptionalThreshold = 0.80;

    internal static readonly (string Name, string Path)[] Targets = [
        ("Move That Body [RLC's Extra]", @"D:\osu!\Songs\220220 Sota Fujimori - Move That Body -Extended Mix-\Sota Fujimori - Move That Body -Extended Mix- (Amamiya Yuko) [RLC's Extra].osu"),
        ("Premonition [kodoku's Insane]", @"D:\osu!\Songs\1698774 Eminem - Premonition (Intro)\Eminem - Premonition (Intro) (hehe) [kodoku's Insane].osu"),
        ("Silhouette [Kage]", @"D:\osu!\Songs\399358 KANA-BOON - Silhouette [no video]\KANA-BOON - Silhouette (Kagetsu) [Kage].osu"),
    ];

    internal static TargetedMapAuditResult Analyze(string name, string path)
    {
        Beatmap bm = BeatmapParser.Load(path);
        var windows = TechDetectorV2Experimental.Analyze(
            bm.HitObjects, GameplayAnalyzer.IsSpinner, GameplayAnalyzer.IsSlider,
            GameplayAnalyzer.IsComplexSlider, GameplayAnalyzer.GetObjectStartPosition,
            GameplayAnalyzer.GetObjectEndPosition, GameplayAnalyzer.GetTechTurnAngle);

        double start = bm.HitObjects.FirstOrDefault()?.Time ?? 0;
        double end = bm.HitObjects.LastOrDefault()?.Time ?? 0;

        var baseDecisions = TechV2ExperimentalDecisionEngine.Decide(windows);
        var sustainedPromotion = TechV2ExperimentalDecisionEngine.PromoteSustainedTechWithDiagnostics(baseDecisions);
        var presence = TechV2ExperimentalDecisionEngine.BuildPresence(sustainedPromotion.Decisions, start, end);

        return new TargetedMapAuditResult(name, path, bm, windows, baseDecisions, sustainedPromotion, presence);
    }

    internal static string RunFullAudit()
    {
        StringBuilder sb = new();
        List<TargetedMapAuditResult> results = [];

        foreach (var (name, path) in Targets)
        {
            results.Add(Analyze(name, path));
        }

        sb.AppendLine("================================================================================");
        sb.AppendLine("                 TECH DETECTOR V2 — TARGETED 3-MAP FORENSIC AUDIT               ");
        sb.AppendLine("================================================================================");
        sb.AppendLine();

        // SECTION 1: Overview
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("SECTION 1: 3-MAP OVERVIEW REPLAY");
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "{0,-30} | {1,7} | {2,6} | {3,7} | {4,9} | {5,7} | {6,12} | {7,12} | {8,8}",
            "Map", "Windows", "Strong", "Support", "Sustained", "Regions", "TechDuration", "PlayDuration", "Presence"));
        sb.AppendLine(new string('-', 115));

        foreach (var r in results)
        {
            int totalWindows = r.Windows.Count;
            int strongCount = r.SustainedPromotion.Decisions.Count(d => d.IsStrongTech);
            int supportCount = r.BaseDecisions.Count(d => d.IsTechSupport);
            int sustainedCount = r.SustainedPromotion.Decisions.Count(d => d.IsSustainedTech);
            int regionCount = r.Presence.Regions.Count;
            double techDur = r.Presence.TotalTechDuration;
            double playDur = r.Presence.PlayableDuration;
            double presencePct = r.Presence.PresencePercent;

            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0,-30} | {1,7} | {2,6} | {3,7} | {4,9} | {5,7} | {6,10:F0}ms | {7,10:F0}ms | {8,7:F2}%",
                r.Name, totalWindows, strongCount, supportCount, sustainedCount, regionCount, techDur, playDur, presencePct));
        }
        sb.AppendLine();

        // SECTION 2: Positive Windows
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("SECTION 2: ALL POSITIVE WINDOWS (StrongTech or SustainedTech)");
        sb.AppendLine("--------------------------------------------------------------------------------");
        foreach (var r in results)
        {
            sb.AppendLine($"=== MAP: {r.Name} ===");
            var positiveWindows = r.SustainedPromotion.Decisions
                .Where(d => d.IsStrongTech || d.IsSustainedTech)
                .OrderBy(d => d.StartIndex)
                .ToList();

            if (positiveWindows.Count == 0)
            {
                sb.AppendLine("  (No positive windows detected)");
            }
            else
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-15} | {1,6} | {2,6} | {3,6} | {4,6} | {5,6} | {6,6} | {7,8} | {8,8} | {9,11}",
                    "Time (ms)", "Geom", "Move", "Slide", "Motif", "FamCnt", "RawSc", "IsStrong", "IsSupp", "IsSustained"));
                sb.AppendLine("  " + new string('-', 95));

                foreach (var d in positiveWindows)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  [{0,5:F0}-{1,5:F0}] | {2,6:F3} | {3,6:F3} | {4,6:F3} | {5,6:F3} | {6,6} | {7,6:F3} | {8,8} | {9,8} | {10,11}",
                        d.StartTime, d.EndTime, d.GeometryEvidence, d.MovementEvidence, d.SliderEvidence, d.MotifEvidence,
                        d.EvidenceFamilyCount, d.RawScore, d.IsStrongTech, d.IsTechSupport, d.IsSustainedTech));
                }
            }
            sb.AppendLine();
        }

        // SECTION 3: Silhouette False Positive
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("SECTION 3: SILHOUETTE — FALSE POSITIVE ANALYSIS");
        sb.AppendLine("--------------------------------------------------------------------------------");
        var sil = results.First(r => r.Name.Contains("Silhouette"));
        var silDecisions = sil.SustainedPromotion.Decisions;
        var silWindows = sil.Windows.ToDictionary(w => w.StartIndex);

        int geomMove = silDecisions.Count(d => d.GeometryEvidence >= ModerateThreshold && d.MovementEvidence >= ModerateThreshold);
        int geomSlide = silDecisions.Count(d => d.GeometryEvidence >= ModerateThreshold && d.SliderEvidence >= ModerateThreshold);
        int geomMotif = silDecisions.Count(d => d.GeometryEvidence >= ModerateThreshold && d.MotifEvidence >= ModerateThreshold);
        int moveSlide = silDecisions.Count(d => d.MovementEvidence >= ModerateThreshold && d.SliderEvidence >= ModerateThreshold);
        int moveMotif = silDecisions.Count(d => d.MovementEvidence >= ModerateThreshold && d.MotifEvidence >= ModerateThreshold);
        int slideMotif = silDecisions.Count(d => d.SliderEvidence >= ModerateThreshold && d.MotifEvidence >= ModerateThreshold);

        var familyAverages = new Dictionary<string, double>
        {
            ["Geometry"] = silDecisions.Average(d => d.GeometryEvidence),
            ["Movement"] = silDecisions.Average(d => d.MovementEvidence),
            ["Slider"] = silDecisions.Average(d => d.SliderEvidence),
            ["Motif"] = silDecisions.Average(d => d.MotifEvidence)
        };
        var sortedFamilies = familyAverages.OrderByDescending(kv => kv.Value).ToList();

        sb.AppendLine($"dominant family: {sortedFamilies[0].Key} (avg: {sortedFamilies[0].Value:F3}, windows >= 0.45: {silDecisions.Count(d => GetFamilyValue(d, sortedFamilies[0].Key) >= ModerateThreshold)}/{silDecisions.Count})");
        sb.AppendLine($"second family:   {sortedFamilies[1].Key} (avg: {sortedFamilies[1].Value:F3}, windows >= 0.45: {silDecisions.Count(d => GetFamilyValue(d, sortedFamilies[1].Key) >= ModerateThreshold)}/{silDecisions.Count})");
        sb.AppendLine();
        sb.AppendLine("number of windows triggered by:");
        sb.AppendLine($"  - Geometry + Movement: {geomMove}");
        sb.AppendLine($"  - Geometry + Slider:   {geomSlide}");
        sb.AppendLine($"  - Geometry + Motif:    {geomMotif}");
        sb.AppendLine($"  - Movement + Slider:   {moveSlide}");
        sb.AppendLine($"  - Movement + Motif:    {moveMotif}");
        sb.AppendLine($"  - Slider + Motif:      {slideMotif}");
        sb.AppendLine();

        // Top 5 contributing windows in Silhouette (SustainedTech or highest presence contributors)
        var silTop5 = silDecisions
            .Where(d => d.IsSustainedTech)
            .OrderByDescending(d => d.RawScore)
            .Take(5)
            .ToList();

        sb.AppendLine("Source features for the top 5 contributing windows:");
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "{0,-15} | {1,7} | {2,6} | {3,8} | {4,7} | {5,7} | {6,6} | {7,6} | {8,7} | {9,6} | {10,7} | {11,6}",
            "Time (ms)", "GeomIrr", "AngCoh", "RhythIrr", "Awkward", "SpacVar", "SldCmp", "S2SRat", "CmpSldR", "MotifC", "TrnsCnt", "SldCnt"));
        sb.AppendLine(new string('-', 112));

        foreach (var d in silTop5)
        {
            var w = silWindows[d.StartIndex];
            int sliderCount = sil.Beatmap.HitObjects.Skip(w.StartIndex).Take(TechDetectorV2Experimental.WindowSize).Count(GameplayAnalyzer.IsSlider);
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "[{0,5:F0}-{1,5:F0}] | {2,7:F3} | {3,6:F3} | {4,8:F3} | {5,7:F3} | {6,7:F3} | {7,6:F3} | {8,6:F3} | {9,7} | {10,6:F3} | {11,7} | {12,6}",
                d.StartTime, d.EndTime,
                w.GeometryIrregularity, w.AngleCoherenceWithSupport,
                w.RhythmIrregularity, w.Awkwardness, w.SpacingVariation,
                w.SliderComplexity, w.ComplexSliderTransitionTopology.SliderToSliderRatio,
                w.ComplexSliderRunLength, w.CoupledTechnicalMotifContinuity,
                w.ComplexSliderTransitionTopology.TransitionCount, sliderCount));
        }
        sb.AppendLine();

        // SECTION 4: False Negatives Analysis
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("SECTION 4: MOVE THAT BODY & PREMONITION — FALSE NEGATIVES (Top 10 Potential)");
        sb.AppendLine("--------------------------------------------------------------------------------");
        var fnTargets = results.Where(r => r.Name.Contains("Move That Body") || r.Name.Contains("Premonition")).ToList();

        foreach (var r in fnTargets)
        {
            sb.AppendLine($"=== MAP: {r.Name} ===");
            var decisions = r.SustainedPromotion.Decisions;

            var top10 = decisions
                .Select(d => new
                {
                    Decision = d,
                    DistToStrong = ComputeDistanceToStrong(d),
                    DistToModerate = ComputeDistanceToModerate(d),
                    Top2Score = GetTop2Sum(d)
                })
                .OrderBy(x => x.DistToStrong)
                .ThenByDescending(x => x.Top2Score)
                .Take(10)
                .ToList();

            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  {0,-15} | {1,6} | {2,6} | {3,6} | {4,6} | {5,6} | {6,6} | {7,6} | {8,10} | {9,9}",
                "Time (ms)", "Geom", "Move", "Slide", "Motif", "ModCnt", "StrCnt", "IsTech", "DistStrong", "DistMod"));
            sb.AppendLine("  " + new string('-', 95));

            foreach (var item in top10)
            {
                var d = item.Decision;
                int modCnt = (new[] { d.GeometryEvidence, d.MovementEvidence, d.SliderEvidence, d.MotifEvidence }).Count(v => v >= ModerateThreshold);
                int strCnt = (new[] { d.GeometryEvidence, d.MovementEvidence, d.SliderEvidence, d.MotifEvidence }).Count(v => v >= StrongThreshold);
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  [{0,5:F0}-{1,5:F0}] | {2,6:F3} | {3,6:F3} | {4,6:F3} | {5,6:F3} | {6,6} | {7,6} | {8,6} | {9,10:F3} | {10,9:F3}",
                    d.StartTime, d.EndTime,
                    d.GeometryEvidence, d.MovementEvidence, d.SliderEvidence, d.MotifEvidence,
                    modCnt, strCnt, d.IsStrongTech, item.DistToStrong, item.DistToModerate));
            }
            sb.AppendLine();
        }

        // SECTION 5: Direct Comparison Table
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("SECTION 5: DIRECT COMPARISON TABLE");
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "{0,-38} | {1,14} | {2,14} | {3,14}",
            "Metric", "Move That Body", "Premonition", "Silhouette"));
        sb.AppendLine(new string('-', 88));

        var mtb = results[0];
        var prem = results[1];
        var silh = results[2];

        void PrintMetric(string name, Func<TargetedMapAuditResult, string> format)
        {
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0,-38} | {1,14} | {2,14} | {3,14}",
                name, format(mtb), format(prem), format(silh)));
        }

        PrintMetric("avg GeometryEvidence", r => r.BaseDecisions.Average(d => d.GeometryEvidence).ToString("F3", CultureInfo.InvariantCulture));
        PrintMetric("avg MovementEvidence", r => r.BaseDecisions.Average(d => d.MovementEvidence).ToString("F3", CultureInfo.InvariantCulture));
        PrintMetric("avg SliderEvidence", r => r.BaseDecisions.Average(d => d.SliderEvidence).ToString("F3", CultureInfo.InvariantCulture));
        PrintMetric("avg MotifEvidence", r => r.BaseDecisions.Average(d => d.MotifEvidence).ToString("F3", CultureInfo.InvariantCulture));
        PrintMetric("avg RawScore", r => r.BaseDecisions.Average(d => d.RawScore).ToString("F3", CultureInfo.InvariantCulture));
        PrintMetric("% windows >= ModerateFamilyThreshold", r => (r.BaseDecisions.Count(d => (new[] { d.GeometryEvidence, d.MovementEvidence, d.SliderEvidence, d.MotifEvidence }).Any(v => v >= ModerateThreshold)) * 100.0 / r.BaseDecisions.Count).ToString("F1", CultureInfo.InvariantCulture) + "%");
        PrintMetric("% windows >= StrongFamilyThreshold", r => (r.BaseDecisions.Count(d => (new[] { d.GeometryEvidence, d.MovementEvidence, d.SliderEvidence, d.MotifEvidence }).Any(v => v >= StrongThreshold)) * 100.0 / r.BaseDecisions.Count).ToString("F1", CultureInfo.InvariantCulture) + "%");
        PrintMetric("StrongTech windows", r => r.SustainedPromotion.Decisions.Count(d => d.IsStrongTech).ToString(CultureInfo.InvariantCulture));
        PrintMetric("SustainedTech windows", r => r.SustainedPromotion.Decisions.Count(d => d.IsSustainedTech).ToString(CultureInfo.InvariantCulture));
        PrintMetric("PresencePercent", r => r.Presence.PresencePercent.ToString("F2", CultureInfo.InvariantCulture) + "%");
        sb.AppendLine();

        return sb.ToString();
    }

    private static double GetFamilyValue(TechV2WindowDecision d, string family) => family switch
    {
        "Geometry" => d.GeometryEvidence,
        "Movement" => d.MovementEvidence,
        "Slider" => d.SliderEvidence,
        "Motif" => d.MotifEvidence,
        _ => 0
    };

    private static double ComputeDistanceToStrong(TechV2WindowDecision d)
    {
        double[] f = [d.GeometryEvidence, d.MovementEvidence, d.SliderEvidence, d.MotifEvidence];
        Array.Sort(f);
        Array.Reverse(f);
        double dist1 = Math.Max(0, StrongThreshold - f[0]) + Math.Max(0, StrongThreshold - f[1]);
        double dist2 = Math.Max(0, ExceptionalThreshold - f[0]) + Math.Max(0, ModerateThreshold - f[1]);
        return Math.Min(dist1, dist2);
    }

    private static double ComputeDistanceToModerate(TechV2WindowDecision d)
    {
        double[] f = [d.GeometryEvidence, d.MovementEvidence, d.SliderEvidence, d.MotifEvidence];
        Array.Sort(f);
        Array.Reverse(f);
        return Math.Max(0, ModerateThreshold - f[0]) + Math.Max(0, ModerateThreshold - f[1]);
    }

    private static double GetTop2Sum(TechV2WindowDecision d)
    {
        double[] f = [d.GeometryEvidence, d.MovementEvidence, d.SliderEvidence, d.MotifEvidence];
        Array.Sort(f);
        return f[3] + f[2];
    }
}
