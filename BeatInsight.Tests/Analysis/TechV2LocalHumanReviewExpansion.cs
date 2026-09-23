using BeatInsight.Analysis;
using BeatInsight.Models;
using BeatInsight.Models.Persistence;
using BeatInsight.Parser;
using System.Globalization;
using System.IO;
using System.Text;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// Phase 2.17 produces only a deterministic human-review queue. The existing
/// Phase 2.16 supported-product prototype is used solely to spread examples
/// across low/mid/high ranked groups; it is never a label or runtime score.
/// </summary>
internal static class TechV2LocalHumanReviewExpansion
{
    private const int SourceMapSampleSize = 80;
    private const int TargetPerBand = 8;
    private const int MaximumRegionsPerDifficulty = 2;

    internal static TechV2LocalReviewExpansionReport Run(string databasePath)
    {
        IReadOnlyList<MlDatasetSample> samples = TechV2StrongTechForensicAudit.LoadLocalCacheSamples(databasePath);
        IReadOnlyList<string> paths = samples.Select(sample => sample.SourceFilePath)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => !IsHistoricalReference(path))
            .OrderBy(StableHash)
            .ThenBy(path => path, StringComparer.Ordinal)
            .Take(SourceMapSampleSize)
            .ToArray();

        List<TechV2LocalReviewExpansionCandidate> candidates = [];
        int parsed = 0, failed = 0;
        foreach (string path in paths)
        {
            try
            {
                Beatmap map = BeatmapParser.Load(path);
                parsed++;
                candidates.AddRange(FindPresenceCandidates(map, path));
            }
            catch (Exception exception) when (exception is IOException or FormatException or InvalidDataException)
            {
                failed++;
            }
        }

        IReadOnlyList<TechV2LocalReviewExpansionCandidate> shortlist = SelectShortlist(candidates);
        return new TechV2LocalReviewExpansionReport(paths.Count, parsed, failed, candidates.Count, shortlist);
    }

    internal static string FormatReport(TechV2LocalReviewExpansionReport report)
    {
        StringBuilder text = new();
        text.AppendLine("===== TECH V2 PHASE 2.17 LOCAL HUMAN REVIEW EXPANSION (TEST-ONLY) =====");
        text.AppendLine($"Deterministic cache-path sample: {report.SourceMapsConsidered} maps; parsed={report.ParsedMaps}; parse failures={report.ParseFailures}; presence regions discovered={report.CandidatesDiscovered}.");
        foreach (IGrouping<TechV2LocalReviewStrengthBand, TechV2LocalReviewExpansionCandidate> band in report.Shortlist
                     .GroupBy(candidate => candidate.Band).OrderBy(group => group.Key))
        {
            text.AppendLine($"{band.Key.ToString().ToUpperInvariant()} PROTOTYPE STRENGTH — HUMAN REVIEW REQUIRED");
            text.AppendLine("  ReviewId | Title | Difficulty | osu!SR | Start-End | Duration | Contribution | Type | Strong/Sustained/Support | G/M/S/Motif | Structural | Sharp | Spacing");
            foreach (TechV2LocalReviewExpansionCandidate candidate in band.OrderBy(candidate => candidate.ReviewId, StringComparer.Ordinal))
            {
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0} | {1} | {2} | {3:F2} | {4}-{5} | {6:F2}s | {7:F2}% | {8} | {9}/{10}/{11} | {12:F3}/{13:F3}/{14:F3}/{15:F3} | {16:F3} | {17:F3} | {18:F3}",
                    candidate.ReviewId, candidate.Title, candidate.Difficulty, candidate.OsuStarRating,
                    FormatTime(candidate.StartTime), FormatTime(candidate.EndTime), candidate.Duration / 1000d,
                    candidate.PresenceContributionPercent, candidate.RegionType,
                    candidate.StrongTechCount, candidate.SustainedTechCount, candidate.TechSupportCount,
                    candidate.Geometry, candidate.Movement, candidate.Slider, candidate.Motif,
                    candidate.StructuralRatio, candidate.SharpRatio, candidate.SpacingVariation));
            }
        }
        text.AppendLine("HUMAN LABEL TEMPLATE:");
        text.AppendLine("  T = TECH | TW = TECH_WEAK | B = BORDERLINE | N = NON_TECH");
        foreach (TechV2LocalReviewExpansionCandidate candidate in report.Shortlist.OrderBy(candidate => candidate.ReviewId, StringComparer.Ordinal))
            text.AppendLine($"  {candidate.ReviewId} = ?");
        text.AppendLine("Band membership is a review-diversification rank only. LOW is not NON_TECH and HIGH is not TECH.");
        return text.ToString();
    }

    private static IReadOnlyList<TechV2LocalReviewExpansionCandidate> FindPresenceCandidates(Beatmap map, string path)
    {
        if (map.HitObjects.Count == 0)
            return [];
        IReadOnlyList<TechV2ExperimentalWindowResult> windows = TechDetectorV2Experimental.Analyze(
            map.HitObjects, GameplayAnalyzer.IsSpinner, GameplayAnalyzer.IsSlider,
            GameplayAnalyzer.IsComplexSlider, GameplayAnalyzer.GetObjectStartPosition,
            GameplayAnalyzer.GetObjectEndPosition, GameplayAnalyzer.GetTechTurnAngle);
        IReadOnlyList<TechV2WindowDecision> decisions = TechV2ExperimentalDecisionEngine
            .PromoteSustainedTechWithDiagnostics(TechV2ExperimentalDecisionEngine.Decide(windows)).Decisions;
        TechV2PresenceResult presence = TechV2ExperimentalDecisionEngine.BuildPresence(
            decisions, map.HitObjects[0].Time, map.HitObjects[^1].Time);
        IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> byStart = windows.ToDictionary(window => window.StartIndex);
        return presence.Regions.Select((region, index) => DescribeRegion(map, path, index + 1, region, decisions, byStart)).ToArray();
    }

    private static TechV2LocalReviewExpansionCandidate DescribeRegion(
        Beatmap map,
        string path,
        int regionNumber,
        TechV2Region region,
        IReadOnlyList<TechV2WindowDecision> decisions,
        IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> sources)
    {
        IReadOnlyList<TechV2WindowDecision> selected = decisions
            .Where(decision => (decision.IsDetectedTech || decision.IsTechSupport)
                && decision.StartIndex >= region.StartWindowIndex
                && decision.EndIndex <= region.EndWindowIndex)
            .OrderBy(decision => decision.StartIndex).ToArray();
        IReadOnlyList<TechV2ExperimentalWindowResult> source = selected.Select(decision => sources[decision.StartIndex]).ToArray();
        (double sharp, double structural)[] ratios = selected.Select(decision => ComputeSharpAndStructural(map.HitObjects, decision.StartIndex, decision.EndIndex)).ToArray();
        double geometry = Average(selected, decision => decision.GeometryEvidence);
        double movement = Average(selected, decision => decision.MovementEvidence);
        double slider = Average(selected, decision => decision.SliderEvidence);
        double motif = Average(selected, decision => decision.MotifEvidence);
        double sharp = ratios.Average(item => item.sharp);
        double structural = ratios.Average(item => item.structural);
        double spacing = Average(source, window => window.SpacingVariation);
        double angle = Average(source, window => window.AngleCoherenceWithSupport);
        IReadOnlyList<TechV2StrengthSupport> support = selected.Select(decision =>
        {
            TechV2StrongTechWindowDetail detail = TechV2StrongTechForensicAudit.DescribeWindow(decision, sources[decision.StartIndex], map.HitObjects);
            return new TechV2StrengthSupport(detail.Support.StructuralPivotCount, detail.Support.ValidTransitionCount);
        }).ToArray();
        double structuralValidity = Math.Clamp(geometry * .45 + structural * .25 + sharp * .15 + angle * .15, 0, 1);
        double technicalIntensity = Math.Clamp(movement * .30 + slider * .25 + spacing * .20
            + Average(source, window => window.RhythmIrregularity) * .15 + motif * .10, 0, 1);
        double supportConfidence = Math.Sqrt(Math.Clamp(Average(support, value => value.ValidTransitionCount) / 8d, 0, 1)
            * Math.Clamp(Average(support, value => value.StructuralPivotCount) / 3d, 0, 1));
        double phase216SupportedProduct = structuralValidity * technicalIntensity * supportConfidence;
        string difficultyKey = path + "|" + map.Title + "|" + map.Version;
        return new TechV2LocalReviewExpansionCandidate(
            "", TechV2LocalReviewStrengthBand.Mid, path, difficultyKey,
            map.Title, map.Version, map.OsuStarRating, regionNumber,
            region.StartTime, region.EndTime, region.Duration,
            region.Duration / Math.Max(1d, map.HitObjects[^1].Time - map.HitObjects[0].Time) * 100d,
            selected.Count(decision => decision.IsStrongTech), selected.Count(decision => decision.IsSustainedTech),
            selected.Count(decision => decision.IsTechSupport), RegionType(selected),
            geometry, movement, slider, motif, structural, sharp, spacing, phase216SupportedProduct);
    }

    private static IReadOnlyList<TechV2LocalReviewExpansionCandidate> SelectShortlist(IReadOnlyList<TechV2LocalReviewExpansionCandidate> candidates)
    {
        if (candidates.Count == 0) return [];
        IReadOnlyList<TechV2LocalReviewExpansionCandidate> ordered = candidates
            .OrderBy(candidate => candidate.Phase216SupportedProduct)
            .ThenBy(candidate => candidate.SourceFilePath, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.StartTime).ToArray();
        double median = ordered[ordered.Count / 2].Phase216SupportedProduct;
        List<TechV2LocalReviewExpansionCandidate> selected = [];
        Dictionary<string, int> perDifficulty = new(StringComparer.OrdinalIgnoreCase);
        SelectBand(ordered, TechV2LocalReviewStrengthBand.Low, selected, perDifficulty);
        SelectBand(ordered.OrderBy(candidate => Math.Abs(candidate.Phase216SupportedProduct - median)).ThenBy(candidate => candidate.SourceFilePath, StringComparer.Ordinal), TechV2LocalReviewStrengthBand.Mid, selected, perDifficulty);
        SelectBand(ordered.OrderByDescending(candidate => candidate.Phase216SupportedProduct).ThenBy(candidate => candidate.SourceFilePath, StringComparer.Ordinal), TechV2LocalReviewStrengthBand.High, selected, perDifficulty);

        return selected.Select((candidate, index) => candidate with { ReviewId = $"R{index + 1:D2}" })
            .ToArray();
    }

    private static void SelectBand(
        IEnumerable<TechV2LocalReviewExpansionCandidate> candidates,
        TechV2LocalReviewStrengthBand band,
        List<TechV2LocalReviewExpansionCandidate> selected,
        Dictionary<string, int> perDifficulty)
    {
        int added = 0;
        HashSet<string> representedTypes = [];
        foreach (TechV2LocalReviewExpansionCandidate candidate in candidates)
        {
            if (added >= TargetPerBand) break;
            if (selected.Any(item => string.Equals(item.SourceFilePath, candidate.SourceFilePath, StringComparison.OrdinalIgnoreCase)
                && item.RegionNumber == candidate.RegionNumber)) continue;
            if (perDifficulty.GetValueOrDefault(candidate.DifficultyKey) >= MaximumRegionsPerDifficulty) continue;
            // First prioritize missing region types; after all available types
            // are represented, keep the deterministic score ordering.
            if (representedTypes.Count < 3 && representedTypes.Contains(candidate.RegionType))
            {
                bool hasMissingType = candidates.Any(other => !representedTypes.Contains(other.RegionType)
                    && perDifficulty.GetValueOrDefault(other.DifficultyKey) < MaximumRegionsPerDifficulty);
                if (hasMissingType) continue;
            }
            selected.Add(candidate with { Band = band });
            perDifficulty[candidate.DifficultyKey] = perDifficulty.GetValueOrDefault(candidate.DifficultyKey) + 1;
            representedTypes.Add(candidate.RegionType);
            added++;
        }
    }

    private static bool IsHistoricalReference(string path) => TechV2HumanGroundedStrongTechAudit.References.Any(reference =>
        Path.GetFileName(path).Contains(reference.TitleContains, StringComparison.OrdinalIgnoreCase)
        && Path.GetFileName(path).Contains(reference.DifficultyContains, StringComparison.OrdinalIgnoreCase));

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

    private static string RegionType(IReadOnlyList<TechV2WindowDecision> decisions)
    {
        bool strong = decisions.Any(decision => decision.IsStrongTech);
        bool sustained = decisions.Any(decision => decision.IsSustainedTech);
        return strong && sustained ? "STRONG_PLUS_SUSTAINED" : strong ? "STRONG_ONLY" : "SUSTAINED_ONLY";
    }
    private static uint StableHash(string value)
    {
        uint hash = 2166136261;
        foreach (char character in value)
        {
            hash ^= char.ToUpperInvariant(character);
            hash *= 16777619;
        }
        return hash;
    }
    private static double Average<T>(IReadOnlyList<T> values, Func<T, double> selector) => values.Count == 0 ? 0 : values.Average(selector);
    private static double Ratio(int numerator, int denominator) => denominator == 0 ? 0 : (double)numerator / denominator;
    private static double Distance((double X, double Y) first, (double X, double Y) second) => Math.Sqrt(Math.Pow(second.X - first.X, 2) + Math.Pow(second.Y - first.Y, 2));
    private static string FormatTime(double milliseconds)
    {
        TimeSpan time = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}.{2:D3}", (int)time.TotalMinutes, time.Seconds, time.Milliseconds);
    }
}

internal enum TechV2LocalReviewStrengthBand { Low, Mid, High }
internal sealed record TechV2LocalReviewExpansionCandidate(string ReviewId, TechV2LocalReviewStrengthBand Band, string SourceFilePath, string DifficultyKey, string Title, string Difficulty, double OsuStarRating, int RegionNumber, double StartTime, double EndTime, double Duration, double PresenceContributionPercent, int StrongTechCount, int SustainedTechCount, int TechSupportCount, string RegionType, double Geometry, double Movement, double Slider, double Motif, double StructuralRatio, double SharpRatio, double SpacingVariation, double Phase216SupportedProduct);
internal sealed record TechV2LocalReviewExpansionReport(int SourceMapsConsidered, int ParsedMaps, int ParseFailures, int CandidatesDiscovered, IReadOnlyList<TechV2LocalReviewExpansionCandidate> Shortlist);
