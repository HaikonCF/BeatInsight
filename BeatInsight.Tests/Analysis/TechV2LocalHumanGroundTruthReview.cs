using BeatInsight.Analysis;
using BeatInsight.Models;
using BeatInsight.Parser;
using System.Globalization;
using System.Text;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// Phase 2.15 is a navigation report for human review. It deliberately does
/// not assign a local label, even when the enclosing map has a global human
/// reference label from earlier diagnostic phases.
/// </summary>
internal static class TechV2LocalHumanGroundTruthReview
{
    private static readonly IReadOnlySet<string> IncludedKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "Howling", "TaoHuaXiao", "RockOnExpert", "TrustInYou", "HappppySong", "RockOnSeAqours",
    };

    internal static TechV2LocalHumanReviewReport Run(string databasePath)
    {
        TechV2HumanGroundedAuditReport source = TechV2HumanGroundedStrongTechAudit.Run(databasePath);
        IReadOnlyList<TechV2LocalHumanReviewMap> maps = source.Maps
            .Where(map => IncludedKeys.Contains(map.Reference.Key))
            .Select(AnalyzeMap)
            .OrderBy(map => map.MapKey, StringComparer.Ordinal)
            .ToArray();
        return new TechV2LocalHumanReviewReport(maps);
    }

    internal static string FormatReport(TechV2LocalHumanReviewReport report)
    {
        StringBuilder text = new();
        text.AppendLine("===== TECH V2 PHASE 2.15 LOCAL HUMAN GROUND TRUTH REVIEW (TEST-ONLY) =====");
        text.AppendLine("Every listed local passage is marked HUMAN REVIEW REQUIRED. No local Tech/Non-Tech verdict is inferred.");
        text.AppendLine("REGIONS CONTRIBUTING TO CURRENT PHASE 2.11 PRESENCE | Map | Difficulty | Region | Start | End | Duration | Contribution | Strong | Sustained | Support | Type");
        foreach (TechV2LocalHumanReviewMap map in report.Maps)
        {
            if (!map.Resolved)
            {
                text.AppendLine($"  {map.MapKey} | unresolved: {map.ResolutionNote}");
                continue;
            }
            if (map.Regions.Count == 0)
            {
                text.AppendLine($"  {map.MapKey} | {map.Difficulty} | no current Tech V2 regions contributing to Presence");
                continue;
            }

            foreach (TechV2LocalHumanReviewRegion region in map.Regions)
            {
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0} | {1} | R{2} | {3} | {4} | {5:F2}s | {6:F2}% | {7} | {8} | {9} | {10}",
                    map.MapKey, map.Difficulty, region.RegionNumber,
                    FormatTime(region.StartTime), FormatTime(region.EndTime), region.Duration / 1000d,
                    region.PresenceContributionPercent, region.StrongTechCount,
                    region.SustainedTechCount, region.TechSupportCount, region.RegionType));
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "    HUMAN REVIEW REQUIRED | Avg G/M/S/Mo={0:F3}/{1:F3}/{2:F3}/{3:F3} | Spacing={4:F3} | Sharp={5:F3} | Structural={6:F3} | dominant pair={7}",
                    region.AverageGeometry, region.AverageMovement, region.AverageSlider, region.AverageMotif,
                    region.AverageSpacingVariation, region.AverageSharpRatio, region.AverageStructuralRatio,
                    region.DominantFamilyPair));
            }
        }

        text.AppendLine("HUMAN RESPONSE TEMPLATE:");
        foreach (TechV2LocalHumanReviewRegion region in report.Maps.Where(map => map.Resolved)
                     .SelectMany(map => map.Regions)
                     .OrderBy(region => region.MapKey, StringComparer.Ordinal).ThenBy(region => region.StartTime))
            text.AppendLine($"  {region.MapKey} R{region.RegionNumber} [{FormatTime(region.StartTime)}-{FormatTime(region.EndTime)}] = ?");
        return text.ToString();
    }

    private static TechV2LocalHumanReviewMap AnalyzeMap(TechV2HumanGroundedMap source)
    {
        if (!source.Resolved || source.Path is null)
            return TechV2LocalHumanReviewMap.Unresolved(source.Reference.Key, source.ResolutionNote);

        Beatmap beatmap = BeatmapParser.Load(source.Path);
        IReadOnlyList<TechV2ExperimentalWindowResult> windows = TechDetectorV2Experimental.Analyze(
            beatmap.HitObjects, GameplayAnalyzer.IsSpinner, GameplayAnalyzer.IsSlider,
            GameplayAnalyzer.IsComplexSlider, GameplayAnalyzer.GetObjectStartPosition,
            GameplayAnalyzer.GetObjectEndPosition, GameplayAnalyzer.GetTechTurnAngle);
        IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> byStart = windows.ToDictionary(window => window.StartIndex);
        IReadOnlyList<TechV2LocalHumanReviewRegion> regions = source.Presence.Regions
            .Select((region, index) => DescribeRegion(source.Reference.Key, index + 1, region,
                source.Decisions, byStart, beatmap.HitObjects, source.Presence.PlayableDuration))
            .ToArray();
        return new TechV2LocalHumanReviewMap(source.Reference.Key, beatmap.Version, true, source.ResolutionNote, regions);
    }

    private static TechV2LocalHumanReviewRegion DescribeRegion(
        string mapKey,
        int regionNumber,
        TechV2Region region,
        IReadOnlyList<TechV2WindowDecision> decisions,
        IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> sources,
        IReadOnlyList<HitObject> objects,
        double playableDuration)
    {
        // Region bounds are produced by the Phase 2.11 engine after Policy D
        // selection. Only positive/support windows inside those final bounds
        // are summarized, so the report cannot introduce non-contributing rows.
        IReadOnlyList<TechV2WindowDecision> selected = decisions
            .Where(decision => (decision.IsDetectedTech || decision.IsTechSupport)
                && decision.StartIndex >= region.StartWindowIndex
                && decision.EndIndex <= region.EndWindowIndex)
            .OrderBy(decision => decision.StartIndex)
            .ToArray();
        int strong = selected.Count(decision => decision.IsStrongTech);
        int sustained = selected.Count(decision => decision.IsSustainedTech);
        int support = selected.Count(decision => decision.IsTechSupport);
        IReadOnlyList<TechV2ExperimentalWindowResult> source = selected
            .Select(decision => sources[decision.StartIndex]).ToArray();
        (double sharp, double structural)[] ratios = selected
            .Select(decision => ComputeSharpAndStructural(objects, decision.StartIndex, decision.EndIndex))
            .ToArray();

        return new TechV2LocalHumanReviewRegion(
            mapKey, regionNumber, region.StartTime, region.EndTime, region.Duration,
            playableDuration <= 0 ? 0 : region.Duration / playableDuration * 100d,
            strong, sustained, support,
            RegionType(strong, sustained),
            Average(selected, decision => decision.GeometryEvidence),
            Average(selected, decision => decision.MovementEvidence),
            Average(selected, decision => decision.SliderEvidence),
            Average(selected, decision => decision.MotifEvidence),
            Average(source, window => window.SpacingVariation),
            ratios.Length == 0 ? 0 : ratios.Average(item => item.sharp),
            ratios.Length == 0 ? 0 : ratios.Average(item => item.structural),
            DominantFamilyPair(selected));
    }

    private static (double sharp, double structural) ComputeSharpAndStructural(IReadOnlyList<HitObject> objects, int start, int end)
    {
        int valid = 0, sharp = 0, structural = 0;
        for (int index = start + 1; index <= end - 1; index++)
        {
            HitObject previous = objects[index - 1], current = objects[index], next = objects[index + 1];
            if (GameplayAnalyzer.IsSpinner(previous) || GameplayAnalyzer.IsSpinner(current) || GameplayAnalyzer.IsSpinner(next)) continue;
            if (current.Time - previous.Time <= 0 || next.Time - current.Time <= 0) continue;
            double firstDistance = Distance(GameplayAnalyzer.GetObjectEndPosition(previous), GameplayAnalyzer.GetObjectStartPosition(current));
            double secondDistance = Distance(GameplayAnalyzer.GetObjectEndPosition(current), GameplayAnalyzer.GetObjectStartPosition(next));
            if (firstDistance > 160 || secondDistance > 160) continue;
            valid++;
            double angle = GameplayAnalyzer.GetTechTurnAngle(previous, current, next);
            double averageDistance = (firstDistance + secondDistance) / 2d;
            if (angle >= 110) structural++;
            if (angle >= 150 && averageDistance <= 125) sharp++;
        }
        return (Ratio(sharp, valid), Ratio(structural, valid));
    }

    private static string DominantFamilyPair(IReadOnlyList<TechV2WindowDecision> decisions)
    {
        if (decisions.Count == 0) return "None";
        (string Name, double Value)[] families =
        [
            ("Geometry", Average(decisions, decision => decision.GeometryEvidence)),
            ("Movement", Average(decisions, decision => decision.MovementEvidence)),
            ("Slider", Average(decisions, decision => decision.SliderEvidence)),
            ("Motif", Average(decisions, decision => decision.MotifEvidence)),
        ];
        return string.Join(" + ", families.OrderByDescending(family => family.Value)
            .ThenBy(family => family.Name, StringComparer.Ordinal).Take(2).Select(family => family.Name));
    }

    private static string RegionType(int strong, int sustained) => strong > 0 && sustained > 0
        ? "STRONG_PLUS_SUSTAINED"
        : strong > 0 ? "STRONG_ONLY"
        : "SUSTAINED_ONLY";
    private static double Average<T>(IReadOnlyList<T> values, Func<T, double> selector) => values.Count == 0 ? 0 : values.Average(selector);
    private static double Ratio(int numerator, int denominator) => denominator == 0 ? 0 : (double)numerator / denominator;
    private static double Distance((double X, double Y) first, (double X, double Y) second) => Math.Sqrt(Math.Pow(second.X - first.X, 2) + Math.Pow(second.Y - first.Y, 2));
    private static string FormatTime(double milliseconds)
    {
        TimeSpan time = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}.{2:D3}", (int)time.TotalMinutes, time.Seconds, time.Milliseconds);
    }
}

internal sealed record TechV2LocalHumanReviewRegion(
    string MapKey,
    int RegionNumber,
    double StartTime,
    double EndTime,
    double Duration,
    double PresenceContributionPercent,
    int StrongTechCount,
    int SustainedTechCount,
    int TechSupportCount,
    string RegionType,
    double AverageGeometry,
    double AverageMovement,
    double AverageSlider,
    double AverageMotif,
    double AverageSpacingVariation,
    double AverageSharpRatio,
    double AverageStructuralRatio,
    string DominantFamilyPair);

internal sealed record TechV2LocalHumanReviewMap(string MapKey, string Difficulty, bool Resolved, string ResolutionNote, IReadOnlyList<TechV2LocalHumanReviewRegion> Regions)
{
    internal static TechV2LocalHumanReviewMap Unresolved(string mapKey, string note) => new(mapKey, "", false, note, []);
}

internal sealed record TechV2LocalHumanReviewReport(IReadOnlyList<TechV2LocalHumanReviewMap> Maps);
