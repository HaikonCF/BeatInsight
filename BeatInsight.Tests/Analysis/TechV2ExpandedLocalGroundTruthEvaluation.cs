using BeatInsight.Analysis;
using BeatInsight.Models;
using BeatInsight.Parser;
using System.Globalization;
using System.IO;
using System.Text;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// Phase 2.18 combines explicit human local labels only. It neither infers a
/// label nor writes one back to Phase 2.15/2.17 data or production runtime.
/// </summary>
internal static class TechV2ExpandedLocalGroundTruthEvaluation
{
    private static readonly IReadOnlyDictionary<string, TechV2LocalHumanLabel> ExpandedLabels =
        new Dictionary<string, TechV2LocalHumanLabel>(StringComparer.Ordinal)
        {
            ["R01"] = TechV2LocalHumanLabel.TechWeak, ["R02"] = TechV2LocalHumanLabel.NonTech,
            ["R03"] = TechV2LocalHumanLabel.NonTech, ["R04"] = TechV2LocalHumanLabel.NonTech,
            ["R05"] = TechV2LocalHumanLabel.Tech, ["R06"] = TechV2LocalHumanLabel.TechWeak,
            ["R07"] = TechV2LocalHumanLabel.NonTech, ["R08"] = TechV2LocalHumanLabel.Tech,
            ["R09"] = TechV2LocalHumanLabel.TechWeak, ["R10"] = TechV2LocalHumanLabel.NonTech,
            ["R11"] = TechV2LocalHumanLabel.Tech, ["R12"] = TechV2LocalHumanLabel.NonTech,
            ["R13"] = TechV2LocalHumanLabel.Tech, ["R14"] = TechV2LocalHumanLabel.Tech,
            ["R15"] = TechV2LocalHumanLabel.NonTech, ["R16"] = TechV2LocalHumanLabel.NonTech,
            ["R17"] = TechV2LocalHumanLabel.Tech, ["R18"] = TechV2LocalHumanLabel.NonTech,
            ["R19"] = TechV2LocalHumanLabel.NonTech, ["R20"] = TechV2LocalHumanLabel.Tech,
            ["R21"] = TechV2LocalHumanLabel.NonTech, ["R22"] = TechV2LocalHumanLabel.NonTech,
            ["R23"] = TechV2LocalHumanLabel.TechWeak, ["R24"] = TechV2LocalHumanLabel.Tech,
        };

    internal static TechV2ExpandedLocalGroundTruthReport Run(string databasePath)
    {
        TechV2LocalTechStrengthReport historical = TechV2LocalTechStrengthPrototype.Run(databasePath);
        IReadOnlyDictionary<string, TechV2HumanGroundedMap> sourceMaps = historical.SourceMaps
            .Where(map => map.Resolved)
            .ToDictionary(map => map.Reference.Key, StringComparer.Ordinal);
        List<TechV2ExpandedLocalRegion> regions = historical.Maps.Where(map => map.Resolved)
            .SelectMany(map => map.Regions)
            .Select(region => new TechV2ExpandedLocalRegion($"{region.MapKey} R{region.RegionNumber}", null,
                sourceMaps.TryGetValue(region.MapKey, out TechV2HumanGroundedMap? sourceMap)
                    && region.RegionNumber <= sourceMap.Presence.Regions.Count
                    ? GetRegionType(sourceMap.Presence.Regions[region.RegionNumber - 1], sourceMap.Decisions)
                    : "Unavailable",
                region, null))
            .ToList();

        TechV2LocalReviewExpansionReport expansion = TechV2LocalHumanReviewExpansion.Run(databasePath);
        foreach (TechV2LocalReviewExpansionCandidate candidate in expansion.Shortlist)
        {
            if (!ExpandedLabels.TryGetValue(candidate.ReviewId, out TechV2LocalHumanLabel label))
                continue;
            TechV2ExpandedLocalRegion? scored = ReanalyzeExpandedCandidate(candidate, label);
            if (scored is not null)
                regions.Add(scored);
        }

        IReadOnlyList<TechV2ExpandedLocalRegion> ordered = regions.OrderBy(region => region.HumanLabel)
            .ThenBy(region => region.Key, StringComparer.Ordinal).ToArray();
        return new TechV2ExpandedLocalGroundTruthReport(
            ordered,
            BuildClassStats(ordered),
            BuildOrdering(ordered),
            BuildRegionTypeSummary(ordered),
            BuildLocalizationNotes(ordered));
    }

    internal static string FormatReport(TechV2ExpandedLocalGroundTruthReport report)
    {
        StringBuilder text = new();
        text.AppendLine("===== TECH V2 PHASE 2.18 EXPANDED LOCAL GROUND TRUTH EVALUATION (TEST-ONLY) =====");
        text.AppendLine("All classes below are explicit human local labels. osu! Star Rating is not read by any P1-P5 calculation.");
        text.AppendLine("CLASS DISTRIBUTIONS | Class | Prototype | Count | Mean | Median | Q1 | Q3 | Min | Max");
        foreach (TechV2ExpandedClassStats item in report.ClassStats)
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  {0} | {1} | {2} | {3:F3} | {4:F3} | {5:F3} | {6:F3} | {7:F3} | {8:F3}",
                item.HumanLabel, item.Prototype, item.Stats.Count, item.Stats.Mean, item.Stats.Median,
                item.Stats.Q1, item.Stats.Q3, item.Stats.Min, item.Stats.Max));

        text.AppendLine("MEAN ORDERING CHECK | Prototype | NON_TECH | BORDERLINE | TECH_WEAK | TECH");
        foreach (TechV2ExpandedOrdering ordering in report.Ordering)
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  {0} | {1:F3} | {2:F3} | {3:F3} | {4:F3}", ordering.Prototype,
                ordering.NonTech, ordering.Borderline, ordering.TechWeak, ordering.Tech));

        foreach (string prototype in PrototypeNames)
        {
            text.AppendLine($"OVERLAP AUDIT — {prototype}");
            AppendCases(text, "highest NON_TECH", report.Regions.Where(region => region.HumanLabel == TechV2LocalHumanLabel.NonTech)
                .OrderByDescending(region => Score(region, prototype)).Take(3), prototype);
            AppendCases(text, "lowest TECH", report.Regions.Where(region => region.HumanLabel == TechV2LocalHumanLabel.Tech)
                .OrderBy(region => Score(region, prototype)).Take(3), prototype);
            AppendCases(text, "lowest TECH_WEAK", report.Regions.Where(region => region.HumanLabel == TechV2LocalHumanLabel.TechWeak)
                .OrderBy(region => Score(region, prototype)).Take(3), prototype);
        }

        text.AppendLine("REGION TYPE BY HUMAN CLASS | Class | Type | Count | P2 mean");
        foreach (TechV2ExpandedRegionTypeStats item in report.RegionTypes)
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  {0} | {1} | {2} | {3:F3}", item.HumanLabel, item.RegionType, item.Count, item.MeanSupportedProduct));

        text.AppendLine("LOCALIZATION_NOTE (diagnostic only; no region or label is changed):");
        foreach (TechV2LocalizationNote note in report.LocalizationNotes)
        {
            text.AppendLine($"  {note.ReviewId}: {note.Note}");
            foreach (TechV2LocalizedWindow window in note.Windows)
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "    {0}->{1} | {2} | G/M/S/Mo={3:F3}/{4:F3}/{5:F3}/{6:F3} | Spacing={7:F3} | Structural={8:F3} | Sharp={9:F3}",
                    FormatTime(window.StartTime), FormatTime(window.EndTime), window.DecisionKind,
                    window.Geometry, window.Movement, window.Slider, window.Motif,
                    window.SpacingVariation, window.StructuralRatio, window.SharpRatio));
            if (note.AdjacentBefore is not null)
                text.AppendLine($"    adjacent-before (not in region): {FormatTime(note.AdjacentBefore.StartTime)}->{FormatTime(note.AdjacentBefore.EndTime)} | {note.AdjacentBefore.DecisionKind}");
        }
        text.AppendLine("P1=BalancedProduct; P2=SupportedProduct; P3=SupportedGeometricMean; P4=SupportedMinimum; P5=StructureLedProduct. No ordering or overlap is a runtime threshold recommendation.");
        return text.ToString();
    }

    private static TechV2ExpandedLocalRegion? ReanalyzeExpandedCandidate(TechV2LocalReviewExpansionCandidate candidate, TechV2LocalHumanLabel label)
    {
        try
        {
            Beatmap map = BeatmapParser.Load(candidate.SourceFilePath);
            IReadOnlyList<TechV2ExperimentalWindowResult> windows = TechDetectorV2Experimental.Analyze(
                map.HitObjects, GameplayAnalyzer.IsSpinner, GameplayAnalyzer.IsSlider,
                GameplayAnalyzer.IsComplexSlider, GameplayAnalyzer.GetObjectStartPosition,
                GameplayAnalyzer.GetObjectEndPosition, GameplayAnalyzer.GetTechTurnAngle);
            IReadOnlyList<TechV2WindowDecision> decisions = TechV2ExperimentalDecisionEngine
                .PromoteSustainedTechWithDiagnostics(TechV2ExperimentalDecisionEngine.Decide(windows)).Decisions;
            TechV2PresenceResult presence = TechV2ExperimentalDecisionEngine.BuildPresence(decisions,
                map.HitObjects[0].Time, map.HitObjects[^1].Time);
            if (candidate.RegionNumber < 1 || candidate.RegionNumber > presence.Regions.Count) return null;
            IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> source = windows.ToDictionary(window => window.StartIndex);
            TechV2Region region = presence.Regions[candidate.RegionNumber - 1];
            TechV2LocalTechStrengthRegion scored = TechV2LocalTechStrengthPrototype.CreateScoredRegionForTest(
                candidate.ReviewId, candidate.RegionNumber, label, region, decisions, source,
                map.HitObjects, map.OsuStarRating);
            IReadOnlyList<TechV2LocalizedWindow> windowDetails = DescribeWindows(region, decisions, source, map.HitObjects);
            TechV2LocalizedWindow? adjacent = FindAdjacentBefore(region, decisions, source, map.HitObjects);
            return new TechV2ExpandedLocalRegion(candidate.ReviewId, candidate.SourceFilePath, candidate.RegionType, scored,
                candidate.ReviewId is "R16" or "R23" ? new TechV2LocalizedSource(windowDetails, adjacent) : null);
        }
        catch (Exception exception) when (exception is IOException or FormatException or InvalidDataException)
        {
            return null;
        }
    }

    private static IReadOnlyList<TechV2LocalizedWindow> DescribeWindows(TechV2Region region, IReadOnlyList<TechV2WindowDecision> decisions,
        IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> source, IReadOnlyList<HitObject> objects) => decisions
        .Where(decision => (decision.IsDetectedTech || decision.IsTechSupport)
            && decision.StartIndex >= region.StartWindowIndex && decision.EndIndex <= region.EndWindowIndex)
        .OrderBy(decision => decision.StartIndex)
        .Select(decision => DescribeWindow(decision, source[decision.StartIndex], objects))
        .ToArray();

    private static TechV2LocalizedWindow? FindAdjacentBefore(TechV2Region region, IReadOnlyList<TechV2WindowDecision> decisions,
        IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> source, IReadOnlyList<HitObject> objects) => decisions
        .Where(decision => decision.EndIndex < region.StartWindowIndex)
        .OrderByDescending(decision => decision.EndIndex)
        .Select(decision => DescribeWindow(decision, source[decision.StartIndex], objects))
        .FirstOrDefault();

    private static TechV2LocalizedWindow DescribeWindow(TechV2WindowDecision decision, TechV2ExperimentalWindowResult source, IReadOnlyList<HitObject> objects)
    {
        (double sharp, double structural) = ComputeSharpAndStructural(objects, decision.StartIndex, decision.EndIndex);
        return new(decision.StartTime, decision.EndTime, DecisionKind(decision), decision.GeometryEvidence,
            decision.MovementEvidence, decision.SliderEvidence, decision.MotifEvidence,
            source.SpacingVariation, structural, sharp);
    }

    private static IReadOnlyList<TechV2ExpandedClassStats> BuildClassStats(IReadOnlyList<TechV2ExpandedLocalRegion> regions) =>
        Enum.GetValues<TechV2LocalHumanLabel>().SelectMany(label => PrototypeNames.Select(prototype =>
            new TechV2ExpandedClassStats(label, prototype,
                Stats(regions.Where(region => region.HumanLabel == label).Select(region => Score(region, prototype)))))).ToArray();

    private static IReadOnlyList<TechV2ExpandedOrdering> BuildOrdering(IReadOnlyList<TechV2ExpandedLocalRegion> regions) => PrototypeNames.Select(prototype => new TechV2ExpandedOrdering(
        prototype,
        Mean(regions, TechV2LocalHumanLabel.NonTech, prototype), Mean(regions, TechV2LocalHumanLabel.Borderline, prototype),
        Mean(regions, TechV2LocalHumanLabel.TechWeak, prototype), Mean(regions, TechV2LocalHumanLabel.Tech, prototype))).ToArray();

    private static IReadOnlyList<TechV2ExpandedRegionTypeStats> BuildRegionTypeSummary(IReadOnlyList<TechV2ExpandedLocalRegion> regions) =>
        Enum.GetValues<TechV2LocalHumanLabel>().SelectMany(label => regions.Where(region => region.HumanLabel == label)
            .GroupBy(region => RegionType(region))
            .Select(group => new TechV2ExpandedRegionTypeStats(label, group.Key, group.Count(), group.Average(region => region.Scored.Prototypes.SupportedProduct)))).ToArray();

    private static IReadOnlyList<TechV2LocalizationNote> BuildLocalizationNotes(IReadOnlyList<TechV2ExpandedLocalRegion> regions) => regions
        .Where(region => region.Localized is not null)
        .OrderBy(region => region.Key, StringComparer.Ordinal)
        .Select(region => new TechV2LocalizationNote(region.Key,
            region.Key == "R23"
                ? "Human note: TECH_WEAK is concentrated toward the end of this region."
                : "Human note: this V2 region is NON_TECH; a real Tech passage is reported near/before it.",
            region.Localized!.Windows, region.Localized.AdjacentBefore)).ToArray();

    private static readonly string[] PrototypeNames = ["P1", "P2", "P3", "P4", "P5"];
    private static double Score(TechV2ExpandedLocalRegion region, string prototype) => prototype switch
    {
        "P1" => region.Scored.Prototypes.BalancedProduct,
        "P2" => region.Scored.Prototypes.SupportedProduct,
        "P3" => region.Scored.Prototypes.SupportedGeometricMean,
        "P4" => region.Scored.Prototypes.SupportedMinimum,
        "P5" => region.Scored.Prototypes.StructureLedProduct,
        _ => throw new ArgumentOutOfRangeException(nameof(prototype)),
    };
    private static double Mean(IReadOnlyList<TechV2ExpandedLocalRegion> regions, TechV2LocalHumanLabel label, string prototype) =>
        regions.Where(region => region.HumanLabel == label).Select(region => Score(region, prototype)).DefaultIfEmpty(0).Average();
    private static TechV2ExpandedStats Stats(IEnumerable<double> values)
    {
        double[] data = values.OrderBy(value => value).ToArray();
        return data.Length == 0 ? new(0, 0, 0, 0, 0, 0, 0) : new(data.Length, data.Average(), data[data.Length / 2], Quantile(data, .25), Quantile(data, .75), data[0], data[^1]);
    }
    private static double Quantile(IReadOnlyList<double> values, double fraction) => values[(int)Math.Round((values.Count - 1) * fraction, MidpointRounding.AwayFromZero)];
    private static void AppendCases(StringBuilder text, string label, IEnumerable<TechV2ExpandedLocalRegion> regions, string prototype)
    {
        text.AppendLine($"  {label}:");
        foreach (TechV2ExpandedLocalRegion region in regions)
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "    {0} | {1:F3} | G/M/S/Mo={2:F3}/{3:F3}/{4:F3}/{5:F3} | Structural={6:F3} Sharp={7:F3} Spacing={8:F3} Support={9:F3}",
                region.Key, Score(region, prototype), region.Scored.Geometry, region.Scored.Movement,
                region.Scored.Slider, region.Scored.Motif, region.Scored.StructuralRatio,
                region.Scored.SharpRatio, region.Scored.SpacingVariation, region.Scored.SupportConfidence));
    }
    private static string RegionType(TechV2ExpandedLocalRegion region) => region.OriginalRegionType ?? "HISTORICAL";
    private static string GetRegionType(TechV2Region region, IReadOnlyList<TechV2WindowDecision> decisions)
    {
        IReadOnlyList<TechV2WindowDecision> selected = decisions.Where(decision =>
                (decision.IsDetectedTech || decision.IsTechSupport)
                && decision.StartIndex >= region.StartWindowIndex && decision.EndIndex <= region.EndWindowIndex)
            .ToArray();
        bool strong = selected.Any(decision => decision.IsStrongTech);
        bool sustained = selected.Any(decision => decision.IsSustainedTech);
        return strong && sustained ? "STRONG_PLUS_SUSTAINED" : strong ? "STRONG_ONLY" : "SUSTAINED_ONLY";
    }
    private static string DecisionKind(TechV2WindowDecision decision) => decision.IsStrongTech ? "StrongTech" : decision.IsSustainedTech ? "SustainedTech" : decision.IsTechSupport ? "TechSupport" : "None";
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
        return (valid == 0 ? 0 : (double)sharp / valid, valid == 0 ? 0 : (double)structural / valid);
    }
    private static double Distance((double X, double Y) first, (double X, double Y) second) => Math.Sqrt(Math.Pow(second.X - first.X, 2) + Math.Pow(second.Y - first.Y, 2));
    private static string FormatTime(double milliseconds)
    {
        TimeSpan time = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}.{2:D3}", (int)time.TotalMinutes, time.Seconds, time.Milliseconds);
    }
}

internal sealed record TechV2ExpandedLocalRegion(string Key, string? SourceFilePath, string? OriginalRegionType, TechV2LocalTechStrengthRegion Scored, TechV2LocalizedSource? Localized)
{
    internal TechV2LocalHumanLabel HumanLabel => Scored.HumanLabel;
}
internal sealed record TechV2ExpandedStats(int Count, double Mean, double Median, double Q1, double Q3, double Min, double Max);
internal sealed record TechV2ExpandedClassStats(TechV2LocalHumanLabel HumanLabel, string Prototype, TechV2ExpandedStats Stats);
internal sealed record TechV2ExpandedOrdering(string Prototype, double NonTech, double Borderline, double TechWeak, double Tech);
internal sealed record TechV2ExpandedRegionTypeStats(TechV2LocalHumanLabel HumanLabel, string RegionType, int Count, double MeanSupportedProduct);
internal sealed record TechV2LocalizedWindow(double StartTime, double EndTime, string DecisionKind, double Geometry, double Movement, double Slider, double Motif, double SpacingVariation, double StructuralRatio, double SharpRatio);
internal sealed record TechV2LocalizedSource(IReadOnlyList<TechV2LocalizedWindow> Windows, TechV2LocalizedWindow? AdjacentBefore);
internal sealed record TechV2LocalizationNote(string ReviewId, string Note, IReadOnlyList<TechV2LocalizedWindow> Windows, TechV2LocalizedWindow? AdjacentBefore);
internal sealed record TechV2ExpandedLocalGroundTruthReport(IReadOnlyList<TechV2ExpandedLocalRegion> Regions, IReadOnlyList<TechV2ExpandedClassStats> ClassStats, IReadOnlyList<TechV2ExpandedOrdering> Ordering, IReadOnlyList<TechV2ExpandedRegionTypeStats> RegionTypes, IReadOnlyList<TechV2LocalizationNote> LocalizationNotes);
