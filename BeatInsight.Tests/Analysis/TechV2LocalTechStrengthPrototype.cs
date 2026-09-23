using BeatInsight.Analysis;
using BeatInsight.Models;
using BeatInsight.Parser;
using System.Globalization;
using System.Text;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// Phase 2.16 is an offline human-local-label calibration pass. Scores here
/// are not probabilities and are intentionally confined to BeatInsight.Tests.
/// </summary>
internal static class TechV2LocalTechStrengthPrototype
{
    private static readonly IReadOnlySet<string> IncludedKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "Exit", "Kira", "Frozen", "WorldEndsNow", "GloriousDays",
        "HappppySong", "Howling", "RockOnExpert", "RockOnSeAqours", "TaoHuaXiao", "TrustInYou",
    };

    internal static TechV2LocalTechStrengthReport Run(string databasePath)
    {
        TechV2HumanGroundedAuditReport source = TechV2HumanGroundedStrongTechAudit.Run(databasePath);
        IReadOnlyList<TechV2LocalTechStrengthMap> maps = source.Maps
            .Where(map => IncludedKeys.Contains(map.Reference.Key))
            .Select(AnalyzeMap)
            .OrderBy(map => map.MapKey, StringComparer.Ordinal)
            .ToArray();
        IReadOnlyList<TechV2LocalTechStrengthRegion> regions = maps.Where(map => map.Resolved)
            .SelectMany(map => map.Regions).ToArray();
        return new TechV2LocalTechStrengthReport(maps, source.Maps, BuildDistributions(regions), BuildPrototypeComparison(regions));
    }

    internal static string FormatReport(TechV2LocalTechStrengthReport report)
    {
        StringBuilder text = new();
        text.AppendLine("===== TECH V2 PHASE 2.16 LOCAL TECH STRENGTH PROTOTYPE (TEST-ONLY) =====");
        text.AppendLine("osu! SR is context only. It is not read by StructuralValidity, TechnicalIntensity, SupportConfidence, or any prototype.");
        text.AppendLine("PER-REGION HUMAN LABELS | Map | R | Human | osu!SR | P1/P2/P3/P4/P5 | Structural | Intensity | Support | G/M/S/Mo | Sharp | StructuralRatio | Spacing | Rhythm | AngleSupport | pivots/valid | sliderRatio | density");
        foreach (TechV2LocalTechStrengthRegion region in report.Maps.Where(map => map.Resolved).SelectMany(map => map.Regions)
                     .OrderBy(region => region.MapKey, StringComparer.Ordinal).ThenBy(region => region.RegionNumber))
        {
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  {0} R{1} | {2} | {3:F2} | {4:F3}/{5:F3}/{6:F3}/{7:F3}/{8:F3} | {9:F3} | {10:F3} | {11:F3} | {12:F3}/{13:F3}/{14:F3}/{15:F3} | {16:F3} | {17:F3} | {18:F3} | {19:F3} | {20:F3} | {21:F2}/{22:F2} | {23:F3} | {24:F3}",
                region.MapKey, region.RegionNumber, region.HumanLabel, region.OsuStarRating,
                region.Prototypes.BalancedProduct, region.Prototypes.SupportedProduct,
                region.Prototypes.SupportedGeometricMean, region.Prototypes.SupportedMinimum,
                region.Prototypes.StructureLedProduct,
                region.StructuralValidity, region.TechnicalIntensity, region.SupportConfidence,
                region.Geometry, region.Movement, region.Slider, region.Motif,
                region.SharpRatio, region.StructuralRatio, region.SpacingVariation,
                region.RhythmIrregularity, region.AngleSupport,
                region.AverageStructuralPivots, region.AverageValidTransitions,
                region.ComplexSliderRatio, region.LocalDensity));
        }

        text.AppendLine("CLASS DISTRIBUTIONS | Human class | Prototype | mean/median/min/max");
        foreach (TechV2LocalTechStrengthDistribution distribution in report.Distributions)
            text.AppendLine($"  {distribution.HumanLabel} | {distribution.Prototype} | {Format(distribution.Stats)}");

        text.AppendLine("PROTOTYPE ORDERING (descriptive only) | Prototype | NON-TECH | BORDERLINE | TECH_WEAK | TECH");
        foreach (TechV2LocalTechStrengthPrototypeOrdering ordering in report.Ordering)
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  {0} | {1} | {2} | {3} | {4}", ordering.Prototype,
                ordering.NonTechMean, ordering.BorderlineMean, ordering.TechWeakMean, ordering.TechMean));
        text.AppendLine("P1=StructuralValidity × TechnicalIntensity; P2=P1 × SupportConfidence; P3=sqrt(StructuralValidity × TechnicalIntensity) × SupportConfidence; P4=min(StructuralValidity, TechnicalIntensity) × SupportConfidence; P5=StructuralValidity × sqrt(TechnicalIntensity) × SupportConfidence.");
        text.AppendLine("No prototype is a runtime threshold or a local verdict. Small BORDERLINE and TECH_WEAK groups are descriptive only.");
        return text.ToString();
    }

    private static TechV2LocalTechStrengthMap AnalyzeMap(TechV2HumanGroundedMap source)
    {
        if (!source.Resolved || source.Path is null)
            return TechV2LocalTechStrengthMap.Unresolved(source.Reference.Key, source.ResolutionNote);

        Beatmap beatmap = BeatmapParser.Load(source.Path);
        IReadOnlyList<TechV2ExperimentalWindowResult> windows = TechDetectorV2Experimental.Analyze(
            beatmap.HitObjects, GameplayAnalyzer.IsSpinner, GameplayAnalyzer.IsSlider,
            GameplayAnalyzer.IsComplexSlider, GameplayAnalyzer.GetObjectStartPosition,
            GameplayAnalyzer.GetObjectEndPosition, GameplayAnalyzer.GetTechTurnAngle);
        IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> byStart = windows.ToDictionary(window => window.StartIndex);
        IReadOnlyList<TechV2LocalTechStrengthRegion> regions = source.Presence.Regions
            .Select((region, index) => DescribeRegion(source.Reference.Key, index + 1, region,
                source.Decisions, byStart, beatmap.HitObjects, beatmap.OsuStarRating))
            .Where(region => region is not null)
            .Select(region => region!)
            .ToArray();
        return new TechV2LocalTechStrengthMap(source.Reference.Key, true, source.ResolutionNote, regions);
    }

    private static TechV2LocalTechStrengthRegion? DescribeRegion(
        string mapKey,
        int regionNumber,
        TechV2Region region,
        IReadOnlyList<TechV2WindowDecision> decisions,
        IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> sources,
        IReadOnlyList<HitObject> objects,
        double osuStarRating)
    {
        TechV2LocalHumanLabel? label = ResolveHumanLabel(mapKey, regionNumber);
        if (label is null)
            return null;

        return CreateScoredRegionForTest(mapKey, regionNumber, label.Value, region,
            decisions, sources, objects, osuStarRating);
    }

    // Shared only by subsequent test-only ground-truth audits. Keeping this
    // scoring construction in one place prevents a second, diverging P1-P5.
    internal static TechV2LocalTechStrengthRegion CreateScoredRegionForTest(
        string mapKey,
        int regionNumber,
        TechV2LocalHumanLabel label,
        TechV2Region region,
        IReadOnlyList<TechV2WindowDecision> decisions,
        IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> sources,
        IReadOnlyList<HitObject> objects,
        double osuStarRating)
    {

        IReadOnlyList<TechV2WindowDecision> selected = decisions
            .Where(decision => (decision.IsDetectedTech || decision.IsTechSupport)
                && decision.StartIndex >= region.StartWindowIndex
                && decision.EndIndex <= region.EndWindowIndex)
            .OrderBy(decision => decision.StartIndex)
            .ToArray();
        if (selected.Count == 0)
            throw new InvalidOperationException("The supplied final Presence region contains no selected V2 windows.");

        IReadOnlyList<TechV2ExperimentalWindowResult> source = selected.Select(decision => sources[decision.StartIndex]).ToArray();
        IReadOnlyList<TechV2StrengthSupport> support = selected.Select(decision =>
            DescribeSupport(decision, sources[decision.StartIndex], objects)).ToArray();
        (double sharp, double structural)[] ratios = selected.Select(decision =>
            ComputeSharpAndStructural(objects, decision.StartIndex, decision.EndIndex)).ToArray();

        double geometry = Average(selected, decision => decision.GeometryEvidence);
        double movement = Average(selected, decision => decision.MovementEvidence);
        double slider = Average(selected, decision => decision.SliderEvidence);
        double motif = Average(selected, decision => decision.MotifEvidence);
        double sharp = ratios.Average(item => item.sharp);
        double structural = ratios.Average(item => item.structural);
        double spacing = Average(source, window => window.SpacingVariation);
        double rhythm = Average(source, window => window.RhythmIrregularity);
        double angleSupport = Average(source, window => window.AngleCoherenceWithSupport);
        double complexSliderRatio = Average(source, window => window.SliderComplexity);
        double sliderToSlider = Average(source, window => window.ComplexSliderTransitionTopology.SliderToSliderRatio);
        double averagePivots = Average(support, value => value.StructuralPivotCount);
        double averageTransitions = Average(support, value => value.ValidTransitionCount);
        double localDensity = Average(selected, decision => 12d / Math.Max(.001, (decision.EndTime - decision.StartTime) / 1000d));

        // Structural validity rewards actual turn structure, angle support, and
        // sharpness. Technical intensity describes local demand separately.
        double structuralValidity = Math.Clamp(
            geometry * .45 + structural * .25 + sharp * .15 + angleSupport * .15,
            0, 1);
        double technicalIntensity = Math.Clamp(
            movement * .30 + slider * .25 + spacing * .20 + rhythm * .15 + motif * .10,
            0, 1);
        // Offline support attenuation: both independent counts must be present
        // before a compact single-event ratio can approach full confidence.
        double supportConfidence = Math.Sqrt(
            Math.Clamp(averageTransitions / 8d, 0, 1)
            * Math.Clamp(averagePivots / 3d, 0, 1));
        TechV2LocalTechStrengthPrototypes prototypes = new(
            structuralValidity * technicalIntensity,
            structuralValidity * technicalIntensity * supportConfidence,
            Math.Sqrt(structuralValidity * technicalIntensity) * supportConfidence,
            Math.Min(structuralValidity, technicalIntensity) * supportConfidence,
            structuralValidity * Math.Sqrt(technicalIntensity) * supportConfidence);

        return new TechV2LocalTechStrengthRegion(
            mapKey, regionNumber, label, osuStarRating,
            prototypes, structuralValidity, technicalIntensity, supportConfidence,
            geometry, movement, slider, motif, sharp, structural, spacing, rhythm,
            angleSupport, averagePivots, averageTransitions, complexSliderRatio,
            sliderToSlider, localDensity);
    }

    private static TechV2StrengthSupport DescribeSupport(TechV2WindowDecision decision, TechV2ExperimentalWindowResult source, IReadOnlyList<HitObject> objects)
    {
        TechV2StrongTechWindowDetail detail = TechV2StrongTechForensicAudit.DescribeWindow(decision, source, objects);
        return new(detail.Support.StructuralPivotCount, detail.Support.ValidTransitionCount);
    }

    private static TechV2LocalHumanLabel? ResolveHumanLabel(string mapKey, int regionNumber) => mapKey switch
    {
        "Exit" or "Kira" or "Frozen" or "WorldEndsNow" or "GloriousDays" => TechV2LocalHumanLabel.Tech,
        "HappppySong" when regionNumber is 1 or 2 => TechV2LocalHumanLabel.Borderline,
        "Howling" when regionNumber is 1 or 2 => TechV2LocalHumanLabel.NonTech,
        "RockOnExpert" when regionNumber is 1 or 2 => TechV2LocalHumanLabel.NonTech,
        "RockOnSeAqours" when regionNumber == 1 => TechV2LocalHumanLabel.Borderline,
        "RockOnSeAqours" when regionNumber == 2 => TechV2LocalHumanLabel.NonTech,
        "TaoHuaXiao" when regionNumber is >= 1 and <= 5 => TechV2LocalHumanLabel.NonTech,
        "TrustInYou" when regionNumber == 1 => TechV2LocalHumanLabel.TechWeak,
        "TrustInYou" when regionNumber == 2 => TechV2LocalHumanLabel.NonTech,
        _ => null,
    };

    private static IReadOnlyList<TechV2LocalTechStrengthDistribution> BuildDistributions(IReadOnlyList<TechV2LocalTechStrengthRegion> regions)
    {
        (string Name, Func<TechV2LocalTechStrengthRegion, double> Score)[] prototypes =
        [
            ("P1 BalancedProduct", region => region.Prototypes.BalancedProduct),
            ("P2 SupportedProduct", region => region.Prototypes.SupportedProduct),
            ("P3 SupportedGeometricMean", region => region.Prototypes.SupportedGeometricMean),
            ("P4 SupportedMinimum", region => region.Prototypes.SupportedMinimum),
            ("P5 StructureLedProduct", region => region.Prototypes.StructureLedProduct),
        ];
        return Enum.GetValues<TechV2LocalHumanLabel>().SelectMany(label => prototypes.Select(prototype =>
            new TechV2LocalTechStrengthDistribution(label, prototype.Name,
                Stats(regions.Where(region => region.HumanLabel == label).Select(prototype.Score))))).ToArray();
    }

    private static IReadOnlyList<TechV2LocalTechStrengthPrototypeOrdering> BuildPrototypeComparison(IReadOnlyList<TechV2LocalTechStrengthRegion> regions) =>
    [
        Ordering("P1 BalancedProduct", regions, region => region.Prototypes.BalancedProduct),
        Ordering("P2 SupportedProduct", regions, region => region.Prototypes.SupportedProduct),
        Ordering("P3 SupportedGeometricMean", regions, region => region.Prototypes.SupportedGeometricMean),
        Ordering("P4 SupportedMinimum", regions, region => region.Prototypes.SupportedMinimum),
        Ordering("P5 StructureLedProduct", regions, region => region.Prototypes.StructureLedProduct),
    ];

    private static TechV2LocalTechStrengthPrototypeOrdering Ordering(string name, IReadOnlyList<TechV2LocalTechStrengthRegion> regions, Func<TechV2LocalTechStrengthRegion, double> score) => new(
        name,
        regions.MeanFor(TechV2LocalHumanLabel.NonTech, score), regions.MeanFor(TechV2LocalHumanLabel.Borderline, score),
        regions.MeanFor(TechV2LocalHumanLabel.TechWeak, score), regions.MeanFor(TechV2LocalHumanLabel.Tech, score));

    private static double MeanFor(this IReadOnlyList<TechV2LocalTechStrengthRegion> regions, TechV2LocalHumanLabel label, Func<TechV2LocalTechStrengthRegion, double> score) =>
        regions.Where(region => region.HumanLabel == label).Select(score).DefaultIfEmpty(0).Average();

    private static (double sharp, double structural) ComputeSharpAndStructural(IReadOnlyList<HitObject> objects, int start, int end)
    {
        int valid = 0, sharp = 0, structural = 0;
        for (int index = start + 1; index <= end - 1; index++)
        {
            HitObject previous = objects[index - 1], current = objects[index], next = objects[index + 1];
            if (GameplayAnalyzer.IsSpinner(previous) || GameplayAnalyzer.IsSpinner(current) || GameplayAnalyzer.IsSpinner(next)) continue;
            if (current.Time - previous.Time <= 0 || next.Time - current.Time <= 0) continue;
            double first = Distance(GameplayAnalyzer.GetObjectEndPosition(previous), GameplayAnalyzer.GetObjectStartPosition(current));
            double second = Distance(GameplayAnalyzer.GetObjectEndPosition(current), GameplayAnalyzer.GetObjectStartPosition(next));
            if (first > 160 || second > 160) continue;
            valid++;
            double angle = GameplayAnalyzer.GetTechTurnAngle(previous, current, next);
            double averageDistance = (first + second) / 2d;
            if (angle >= 110) structural++;
            if (angle >= 150 && averageDistance <= 125) sharp++;
        }
        return (Ratio(sharp, valid), Ratio(structural, valid));
    }

    private static TechV2LocalTechStrengthStats Stats(IEnumerable<double> values)
    {
        double[] data = values.OrderBy(value => value).ToArray();
        return data.Length == 0 ? new(0, 0, 0, 0) : new(data.Average(), data[data.Length / 2], data[0], data[^1]);
    }

    private static string Format(TechV2LocalTechStrengthStats stats) => string.Format(CultureInfo.InvariantCulture, "{0:F3}/{1:F3}/{2:F3}/{3:F3}", stats.Mean, stats.Median, stats.Min, stats.Max);
    private static double Average<T>(IReadOnlyList<T> values, Func<T, double> selector) => values.Count == 0 ? 0 : values.Average(selector);
    private static double Ratio(int numerator, int denominator) => denominator == 0 ? 0 : (double)numerator / denominator;
    private static double Distance((double X, double Y) first, (double X, double Y) second) => Math.Sqrt(Math.Pow(second.X - first.X, 2) + Math.Pow(second.Y - first.Y, 2));
}

internal enum TechV2LocalHumanLabel { NonTech, Borderline, TechWeak, Tech }
internal sealed record TechV2StrengthSupport(int StructuralPivotCount, int ValidTransitionCount);
internal sealed record TechV2LocalTechStrengthPrototypes(double BalancedProduct, double SupportedProduct, double SupportedGeometricMean, double SupportedMinimum, double StructureLedProduct);
internal sealed record TechV2LocalTechStrengthRegion(string MapKey, int RegionNumber, TechV2LocalHumanLabel HumanLabel, double OsuStarRating, TechV2LocalTechStrengthPrototypes Prototypes, double StructuralValidity, double TechnicalIntensity, double SupportConfidence, double Geometry, double Movement, double Slider, double Motif, double SharpRatio, double StructuralRatio, double SpacingVariation, double RhythmIrregularity, double AngleSupport, double AverageStructuralPivots, double AverageValidTransitions, double ComplexSliderRatio, double SliderToSliderRatio, double LocalDensity);
internal sealed record TechV2LocalTechStrengthMap(string MapKey, bool Resolved, string ResolutionNote, IReadOnlyList<TechV2LocalTechStrengthRegion> Regions) { internal static TechV2LocalTechStrengthMap Unresolved(string key, string note) => new(key, false, note, []); }
internal sealed record TechV2LocalTechStrengthStats(double Mean, double Median, double Min, double Max);
internal sealed record TechV2LocalTechStrengthDistribution(TechV2LocalHumanLabel HumanLabel, string Prototype, TechV2LocalTechStrengthStats Stats);
internal sealed record TechV2LocalTechStrengthPrototypeOrdering(string Prototype, double NonTechMean, double BorderlineMean, double TechWeakMean, double TechMean);
internal sealed record TechV2LocalTechStrengthReport(IReadOnlyList<TechV2LocalTechStrengthMap> Maps, IReadOnlyList<TechV2HumanGroundedMap> SourceMaps, IReadOnlyList<TechV2LocalTechStrengthDistribution> Distributions, IReadOnlyList<TechV2LocalTechStrengthPrototypeOrdering> Ordering);
