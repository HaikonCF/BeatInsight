using BeatInsight.Analysis;
using BeatInsight.Models;
using BeatInsight.Parser;
using System.Globalization;
using System.IO;
using System.Text;

namespace BeatInsight.Tests.Analysis;

internal sealed record SliderInspectionResult(
    int Time,
    string CurveType,
    int ControlPointsCount,
    int Slides,
    double Length,
    double Displacement,
    double TravelRatio,
    double MaxPointAngleChange,
    bool IsBaselineComplex,
    bool IsCand1Complex,
    bool IsCand2Complex,
    bool IsCand3Complex);

internal static class TechV2ComplexSliderDefinitionAudit
{
    internal static readonly (string Key, string Name, string Path, bool IsTech)[] Maps = [
        ("Silhouette", "Silhouette [Kage]", @"D:\osu!\Songs\399358 KANA-BOON - Silhouette [no video]\KANA-BOON - Silhouette (Kagetsu) [Kage].osu", false),
        ("Exit", "Exit [Primordial Nucleosynthesis]", Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps", "Exit This Earth's Atmosphere [Primordial Nucleosynthesis].osu"), true),
        ("Kira", "Kira Killer [Mocaotic's Insane]", Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps", "Kira Killer [Mocaotic's Insane].osu"), true),
        ("Frozen", "Frozen [Collab Insane]", Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps", "Frozen [Collab Insane].osu"), true),
        ("WorldEndsNow", "The World Ends Now [0pp's Last ExtrA]", @"D:\osu!\Songs\1051133 Akira Complex - The World Ends Now\Akira Complex - The World Ends Now (nisenoji) [0pp's Last ExtrA].osu", true),
        ("GloriousDays", "GLORIOUS DAYS [Garden's Insane]", @"D:\osu!\Songs\1048362 yuiko - GLORIOUS DAYS (Short Ver)\yuiko - GLORIOUS DAYS (Short Ver.) (Mimari) [Garden's Insane].osu", true),
    ];

    internal static bool Candidate1_ExcludeP(HitObject ho)
    {
        if (!GameplayAnalyzer.IsSlider(ho)) return false;
        bool isC = ho.SliderCurveType == "C";
        bool isMultiBezier = ho.SliderCurveType == "B" && ho.SliderControlPoints.Count >= 3;
        bool isMultiRepeat = ho.Slides >= 3;
        return isC || isMultiBezier || isMultiRepeat;
    }

    internal static bool Candidate2_CurvatureAware(HitObject ho)
    {
        if (!GameplayAnalyzer.IsSlider(ho)) return false;
        if (ho.SliderCurveType == "C") return true;
        if (ho.Slides >= 3) return true;
        if (ho.SliderCurveType == "B" && ho.SliderControlPoints.Count >= 3) return true;

        if (ho.SliderCurveType == "P")
        {
            double startX = ho.X;
            double startY = ho.Y;
            double endX = ho.SliderEndPosition.X;
            double endY = ho.SliderEndPosition.Y;
            double displacement = Math.Sqrt(Math.Pow(endX - startX, 2) + Math.Pow(endY - startY, 2));
            double travelRatio = ho.Length > 0 ? displacement / ho.Length : 1.0;
            return travelRatio < 0.60;
        }

        return false;
    }

    internal static bool Candidate3_StrictTech(HitObject ho)
    {
        if (!GameplayAnalyzer.IsSlider(ho)) return false;
        if (ho.SliderCurveType == "C") return true;
        if (ho.Slides >= 3) return true;
        if (ho.SliderCurveType == "B")
        {
            if (ho.SliderControlPoints.Count >= 4) return true;
            if (ho.SliderControlPoints.Count >= 3)
            {
                double startX = ho.X, startY = ho.Y;
                List<(double X, double Y)> pts = [(startX, startY)];
                foreach (var cp in ho.SliderControlPoints) pts.Add((cp.X, cp.Y));
                for (int i = 1; i < pts.Count - 1; i++)
                {
                    double v1x = pts[i].X - pts[i - 1].X, v1y = pts[i].Y - pts[i - 1].Y;
                    double v2x = pts[i + 1].X - pts[i].X, v2y = pts[i + 1].Y - pts[i].Y;
                    double len1 = Math.Sqrt(v1x * v1x + v1y * v1y);
                    double len2 = Math.Sqrt(v2x * v2x + v2y * v2y);
                    if (len1 > 1e-4 && len2 > 1e-4)
                    {
                        double dot = Math.Clamp((v1x * v2x + v1y * v2y) / (len1 * len2), -1.0, 1.0);
                        double angle = Math.Acos(dot) * (180.0 / Math.PI);
                        if (angle >= 35.0) return true;
                    }
                }
            }
        }
        return false;
    }

    internal static IReadOnlyList<SliderInspectionResult> InspectSliders(Beatmap beatmap)
    {
        List<SliderInspectionResult> list = [];
        foreach (var ho in beatmap.HitObjects)
        {
            if (!GameplayAnalyzer.IsSlider(ho)) continue;

            double startX = ho.X;
            double startY = ho.Y;
            double endX = ho.SliderEndPosition.X;
            double endY = ho.SliderEndPosition.Y;
            double displacement = Math.Sqrt(Math.Pow(endX - startX, 2) + Math.Pow(endY - startY, 2));
            double travelRatio = ho.Length > 0 ? displacement / ho.Length : 1.0;

            List<(double X, double Y)> allPoints = [(startX, startY)];
            foreach (var cp in ho.SliderControlPoints) allPoints.Add((cp.X, cp.Y));

            double maxAngleChange = 0;
            for (int i = 1; i < allPoints.Count - 1; i++)
            {
                var p0 = allPoints[i - 1];
                var p1 = allPoints[i];
                var p2 = allPoints[i + 1];
                double v1x = p1.X - p0.X, v1y = p1.Y - p0.Y;
                double v2x = p2.X - p1.X, v2y = p2.Y - p1.Y;
                double len1 = Math.Sqrt(v1x * v1x + v1y * v1y);
                double len2 = Math.Sqrt(v2x * v2x + v2y * v2y);
                if (len1 > 1e-4 && len2 > 1e-4)
                {
                    double dot = Math.Clamp((v1x * v2x + v1y * v2y) / (len1 * len2), -1.0, 1.0);
                    double angleDeg = Math.Acos(dot) * (180.0 / Math.PI);
                    if (angleDeg > maxAngleChange) maxAngleChange = angleDeg;
                }
            }

            list.Add(new SliderInspectionResult(
                ho.Time, ho.SliderCurveType, ho.SliderControlPoints.Count, ho.Slides,
                ho.Length, displacement, travelRatio, maxAngleChange,
                GameplayAnalyzer.IsComplexSlider(ho),
                Candidate1_ExcludeP(ho),
                Candidate2_CurvatureAware(ho),
                Candidate3_StrictTech(ho)));
        }
        return list;
    }

    internal static TechV2PresenceResult RunShadowPipeline(Beatmap bm, Func<HitObject, bool> isComplexSliderFunc)
    {
        var windows = TechDetectorV2Experimental.Analyze(
            bm.HitObjects, GameplayAnalyzer.IsSpinner, GameplayAnalyzer.IsSlider,
            isComplexSliderFunc, GameplayAnalyzer.GetObjectStartPosition,
            GameplayAnalyzer.GetObjectEndPosition, GameplayAnalyzer.GetTechTurnAngle);

        double start = bm.HitObjects.FirstOrDefault()?.Time ?? 0;
        double end = bm.HitObjects.LastOrDefault()?.Time ?? 0;

        var baseDecisions = TechV2ExperimentalDecisionEngine.Decide(windows);
        var promoted = TechV2ExperimentalDecisionEngine.PromoteSustainedTechWithDiagnostics(baseDecisions);
        return TechV2ExperimentalDecisionEngine.BuildPresence(promoted.Decisions, start, end);
    }

    internal static string RunFullAudit()
    {
        StringBuilder sb = new();
        sb.AppendLine("================================================================================");
        sb.AppendLine("     TECH V2 — PHASE 2.29 COMPLEX SLIDER DEFINITION AUDIT                       ");
        sb.AppendLine("================================================================================");
        sb.AppendLine();

        // Section 1: Slider Geometry & CurveType Breakdown per Map
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("SECTION 1: SLIDER GEOMETRY & CURVETYPE DISTRIBUTION");
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "{0,-38} | {1,7} | {2,5} | {3,5} | {4,5} | {5,5} | {6,6} | {7,11} | {8,10}",
            "Map", "Sliders", "P", "B", "L", "C", "Rep>=3", "CPs>=3 (B)", "AvgTravelRat"));
        sb.AppendLine(new string('-', 108));

        Dictionary<string, IReadOnlyList<SliderInspectionResult>> mapSliders = [];
        Dictionary<string, Beatmap> mapBeatmaps = [];

        foreach (var (key, name, path, isTech) in Maps)
        {
            var bm = BeatmapParser.Load(path);
            mapBeatmaps[key] = bm;
            var sliders = InspectSliders(bm);
            mapSliders[key] = sliders;

            int totalSliders = sliders.Count;
            int countP = sliders.Count(s => s.CurveType == "P");
            int countB = sliders.Count(s => s.CurveType == "B");
            int countL = sliders.Count(s => s.CurveType == "L");
            int countC = sliders.Count(s => s.CurveType == "C");
            int countRep = sliders.Count(s => s.Slides >= 3);
            int countBMulti = sliders.Count(s => s.CurveType == "B" && s.ControlPointsCount >= 3);
            double avgTravel = sliders.Count > 0 ? sliders.Average(s => s.TravelRatio) : 1.0;

            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0,-38} | {1,7} | {2,5} | {3,5} | {4,5} | {5,5} | {6,6} | {7,11} | {8,10:F3}",
                name, totalSliders, countP, countB, countL, countC, countRep, countBMulti, avgTravel));
        }
        sb.AppendLine();

        // Section 2: Complex Slider Counts by Candidate Definition
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("SECTION 2: COMPLEX SLIDER COUNT BY DEFINITION");
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "{0,-38} | {1,8} | {2,12} | {3,12} | {4,12} | {5,12}",
            "Map", "Sliders", "Baseline", "Cand 1 (-P)", "Cand 2 (Curv)", "Cand 3 (Strt)"));
        sb.AppendLine(new string('-', 98));

        foreach (var (key, name, path, isTech) in Maps)
        {
            var sliders = mapSliders[key];
            int total = sliders.Count;
            int baseCmp = sliders.Count(s => s.IsBaselineComplex);
            int c1Cmp = sliders.Count(s => s.IsCand1Complex);
            int c2Cmp = sliders.Count(s => s.IsCand2Complex);
            int c3Cmp = sliders.Count(s => s.IsCand3Complex);

            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0,-38} | {1,8} | {2,6} ({3,4:F0}%) | {4,6} ({5,4:F0}%) | {6,6} ({7,4:F0}%) | {8,6} ({9,4:F0}%)",
                name, total,
                baseCmp, total > 0 ? baseCmp * 100.0 / total : 0,
                c1Cmp, total > 0 ? c1Cmp * 100.0 / total : 0,
                c2Cmp, total > 0 ? c2Cmp * 100.0 / total : 0,
                c3Cmp, total > 0 ? c3Cmp * 100.0 / total : 0));
        }
        sb.AppendLine();

        // Section 3: End-to-End Presence Impact by Definition
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("SECTION 3: END-TO-END TECH PRESENCE IMPACT (SHADOW SIMULATION)");
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "{0,-38} | {1,10} | {2,12} | {3,12} | {4,12}",
            "Map", "Baseline", "Cand 1 (-P)", "Cand 2 (Curv)", "Cand 3 (Strt)"));
        sb.AppendLine(new string('-', 92));

        Dictionary<string, double> presBaseline = [];
        Dictionary<string, double> presC1 = [];
        Dictionary<string, double> presC2 = [];
        Dictionary<string, double> presC3 = [];

        foreach (var (key, name, path, isTech) in Maps)
        {
            var bm = mapBeatmaps[key];
            var rBase = RunShadowPipeline(bm, GameplayAnalyzer.IsComplexSlider);
            var rC1 = RunShadowPipeline(bm, Candidate1_ExcludeP);
            var rC2 = RunShadowPipeline(bm, Candidate2_CurvatureAware);
            var rC3 = RunShadowPipeline(bm, Candidate3_StrictTech);

            presBaseline[key] = rBase.PresencePercent;
            presC1[key] = rC1.PresencePercent;
            presC2[key] = rC2.PresencePercent;
            presC3[key] = rC3.PresencePercent;

            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0,-38} | {1,9:F2}% | {2,11:F2}% | {3,11:F2}% | {4,11:F2}%",
                name, rBase.PresencePercent, rC1.PresencePercent, rC2.PresencePercent, rC3.PresencePercent));
        }
        sb.AppendLine();

        return sb.ToString();
    }
}
