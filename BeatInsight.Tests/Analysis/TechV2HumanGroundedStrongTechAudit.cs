using BeatInsight.Analysis;
using BeatInsight.Models;
using BeatInsight.Models.Persistence;
using BeatInsight.Parser;
using System.Globalization;
using System.IO;
using System.Text;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// Phase 2.9C calibration-only audit. Human verdicts live exclusively in the
/// reference list below; neither GameplayIdentity nor any rating/ML output is
/// used to create a label or select the corpus.
/// </summary>
internal static class TechV2HumanGroundedStrongTechAudit
{
    internal static readonly IReadOnlyList<TechV2HumanReference> References =
    [
        new("Exit", TechV2HumanTruth.Tech, "Exit This Earth's Atmosphere", "Primordial Nucleosynthesis", "Exit This Earth's Atmosphere [Primordial Nucleosynthesis].osu"),
        new("Kira", TechV2HumanTruth.Tech, "Kira Killer", "Mocaotic's Insane", "Kira Killer [Mocaotic's Insane].osu"),
        new("Frozen", TechV2HumanTruth.Tech, "Frozen", "Collab Insane", "Frozen [Collab Insane].osu"),
        new("WorldEndsNow", TechV2HumanTruth.Tech, "The World Ends Now", "0pp's Last ExtrA"),
        new("GloriousDays", TechV2HumanTruth.Tech, "GLORIOUS DAYS", "Garden's Insane"),
        new("Tower", TechV2HumanTruth.NonTech, "Tower Of Heaven", "Extra", "Tower Of Heaven [Extra].osu"),
        new("CanYou", TechV2HumanTruth.NonTech, "(can you) understand me?", "uhh?", "(can you) understand me [uhh].osu"),
        new("Inferno", TechV2HumanTruth.NonTech, "Inferno (TV Size)", "Rein_Liya's Hard"),
        new("Howling", TechV2HumanTruth.NonTech, "Howling (TV Size)", "A r M i N's Extra"),
        new("Kataomoi", TechV2HumanTruth.NonTech, "Kataomoi", "Hard"),
        new("PadoruSmokelind", TechV2HumanTruth.NonTech, "PADORU/PADORU", "SMOKELIND's Insane"),
        new("TaoHuaXiao", TechV2HumanTruth.NonTech, "Tao Hua Xiao", "Hyper"),
        new("RockOnExpert", TechV2HumanTruth.NonTech, "Rock on.", "Expert"),
        new("TrustInYou", TechV2HumanTruth.NonTech, "Trust in you (TV Size)", "Hard"),
        new("Ray", TechV2HumanTruth.NonTech, "Ray", "Easy"),
        new("HappppySong", TechV2HumanTruth.NonTech, "Happppy song", "normal."),
        new("PadoruInsane", TechV2HumanTruth.NonTech, "PADORU/PADORU", "Insane"),
        new("RockOnSeAqours", TechV2HumanTruth.NonTech, "Rock on.", "SeAqours' Hard"),
        new("FinestSky", TechV2HumanTruth.NonTech, "Finest Sky", "Normal"),
    ];

    internal static TechV2HumanGroundedAuditReport Run(string? databasePath = null)
    {
        IReadOnlyList<MlDatasetSample> samples =
            TechV2StrongTechForensicAudit.LoadLocalCacheSamples(databasePath);

        IReadOnlyList<TechV2HumanGroundedMap> maps = References
            .Select(reference => ResolveAndAnalyze(reference, samples))
            .ToArray();

        IReadOnlyList<TechV2HumanStrongWindow> allStrong = maps
            .Where(map => map.Resolved)
            .SelectMany(map => map.StrongTechWindows)
            .ToArray();
        IReadOnlyList<TechV2HumanStrongWindow> humanTech = allStrong
            .Where(window => window.HumanTruth == TechV2HumanTruth.Tech)
            .ToArray();
        IReadOnlyList<TechV2HumanStrongWindow> humanNonTech = allStrong
            .Where(window => window.HumanTruth == TechV2HumanTruth.NonTech)
            .ToArray();

        TechV2HumanGuardSimulation g3 = SimulateGuard(
            TechV2StrongTechForensicAudit.MovementSliderLowGeometryGuard,
            maps);
        IReadOnlyList<TechV2HumanGuardSimulation> secondGuardCandidates =
            TechV2StrongTechForensicAudit.CandidateGuards
                .Where(guard => !string.Equals(
                    guard.Name,
                    TechV2StrongTechForensicAudit.MovementSliderLowGeometryGuard.Name,
                    StringComparison.Ordinal))
                .Select(guard => SimulateGuard(guard, maps))
                .ToArray();

        return new TechV2HumanGroundedAuditReport(
            maps,
            BuildDistribution(humanTech),
            BuildDistribution(humanNonTech),
            g3,
            secondGuardCandidates,
            BuildAnchorAmplification(maps),
            SelectBestHypothesis(secondGuardCandidates));
    }

    internal static string FormatReport(TechV2HumanGroundedAuditReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        StringBuilder text = new();
        text.AppendLine("===== TECH V2 PHASE 2.9C HUMAN-GROUNDED STRONGTECH AUDIT (TEST-ONLY) =====");
        text.AppendLine("Human truth is the sole calibration label. Identity is reported only as context.");
        text.AppendLine("RESOLVED HUMAN REFERENCES:");
        foreach (TechV2HumanGroundedMap map in report.Maps.Where(map => map.Resolved))
        {
            text.AppendLine($"  {map.Reference.Key} | {map.Reference.HumanTruth} | {map.Path}");
        }
        text.AppendLine("UNRESOLVED HUMAN REFERENCES:");
        foreach (TechV2HumanGroundedMap map in report.Maps.Where(map => !map.Resolved))
        {
            text.AppendLine($"  {map.Reference.Key} | {map.Reference.HumanTruth} | {map.ResolutionNote}");
        }

        text.AppendLine("PER-MAP V2 RESULT | Map | Human | Primary/Secondary context | Strong | Support | Sustained | Regions | Presence");
        foreach (TechV2HumanGroundedMap map in report.Maps)
        {
            if (!map.Resolved)
            {
                text.AppendLine($"  {map.Reference.Key} | {map.Reference.HumanTruth} | unresolved");
                continue;
            }
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  {0} | {1} | {2}/{3} | {4} | {5} | {6} | {7} | {8:F2}%",
                map.Reference.Key, map.Reference.HumanTruth,
                map.IdentityPrimary, DisplaySecondary(map.IdentitySecondary),
                map.StrongTechWindows.Count, map.TechSupportCount, map.SustainedTechCount,
                map.Presence.Regions.Count, map.Presence.PresencePercent));
        }

        AppendDistribution(text, "HUMAN TECH STRONGTECH DISTRIBUTION", report.HumanTechDistribution);
        AppendDistribution(text, "HUMAN NON-TECH STRONGTECH DISTRIBUTION", report.HumanNonTechDistribution);
        AppendAllStrongWindows(text, report.Maps);

        text.AppendLine("G3 SIMULATION (OFFLINE ONLY) | Human group | removed/kept StrongTech windows | maps affected");
        text.AppendLine($"  TECH | {report.G3.HumanTechRemoved}/{report.G3.HumanTechKept} | {DisplayKeys(report.G3.HumanTechAffectedMaps)}");
        text.AppendLine($"  NON-TECH | {report.G3.HumanNonTechRemoved}/{report.G3.HumanNonTechKept} | {DisplayKeys(report.G3.HumanNonTechAffectedMaps)}");

        text.AppendLine("NON-TECH STRONGTECH WINDOWS SURVIVING G3:");
        IReadOnlyList<TechV2HumanStrongWindow> survivingNonTech = report.Maps
            .Where(map => map.Resolved && map.Reference.HumanTruth == TechV2HumanTruth.NonTech)
            .SelectMany(map => map.StrongTechWindows)
            .Where(window => !window.G3WouldReject)
            .OrderBy(window => window.MapKey, StringComparer.Ordinal)
            .ThenBy(window => window.Detail.StartTime)
            .ToArray();
        if (survivingNonTech.Count == 0)
        {
            text.AppendLine("  None");
        }
        foreach (IGrouping<string, TechV2HumanStrongWindow> group in survivingNonTech.GroupBy(window => window.MapKey))
        {
            TechV2HumanGroundedMap map = report.Maps.Single(map => map.Reference.Key == group.Key);
            text.AppendLine($"  {group.Key}: StrongTech={map.StrongTechWindows.Count}, TechSupport={map.TechSupportCount}");
            foreach (TechV2HumanStrongWindow window in group)
            {
                TechV2StrongTechWindowDetail detail = window.Detail;
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "    {0:F0}->{1:F0}ms | pair={2}+{3} | G/M/S/Mo={4:F3}/{5:F3}/{6:F3}/{7:F3} | "
                    + "pivots={8} validTransitions={9} sliderComplexity={10:F3} complexSliders={11} awkward={12} spacingVariation={13} "
                    + "| G3 kept: pair is not Movement+Slider with pivots<3",
                    detail.StartTime, detail.EndTime, detail.BestFamily, detail.SecondBestFamily,
                    detail.Geometry, detail.Movement, detail.Slider, detail.Motif,
                    detail.Support.StructuralPivotCount, detail.Support.ValidTransitionCount,
                    detail.SliderComplexity, detail.Support.ComplexSliderCount,
                    detail.Support.AwkwardTransitionCount, detail.Support.SpacingVariationTransitionCount));
            }
        }

        text.AppendLine("ANCHOR AMPLIFICATION | Map | StrongWindow | RegionDuration | NeighboringSupportAbsorbed | RegionPresenceContribution");
        foreach (TechV2HumanAnchorAmplification item in report.AnchorAmplification)
        {
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  {0} | {1:F0}->{2:F0}ms | {3:F0}ms | {4} | {5:F2}%",
                item.MapKey, item.StrongWindowStartTime, item.StrongWindowEndTime,
                item.RegionDuration, item.NeighboringTechSupportAbsorbed,
                item.RegionPresenceContributionPercent));
        }

        text.AppendLine("SECOND-GUARD CANDIDATES (offline human-corpus observations only):");
        foreach (TechV2HumanGuardSimulation candidate in report.SecondGuardCandidates)
        {
            text.AppendLine($"  {candidate.Name} | Tech removed/kept={candidate.HumanTechRemoved}/{candidate.HumanTechKept} "
                + $"| Non-Tech removed/kept={candidate.HumanNonTechRemoved}/{candidate.HumanNonTechKept} "
                + $"| GLORIOUS DAYS kept={candidate.GloriousDaysHasStrongTechAfterGuard}");
        }
        text.AppendLine($"BEST NEXT HYPOTHESIS: {report.BestNextHypothesis}");
        return text.ToString();
    }

    private static TechV2HumanGroundedMap ResolveAndAnalyze(
        TechV2HumanReference reference,
        IReadOnlyList<MlDatasetSample> samples)
    {
        (string? path, string note) = ResolvePath(reference, samples);
        if (path is null)
        {
            return TechV2HumanGroundedMap.Unresolved(reference, note);
        }

        Beatmap map;
        try
        {
            map = BeatmapParser.Load(path);
        }
        catch (Exception exception) when (exception is IOException or FormatException or InvalidDataException)
        {
            return TechV2HumanGroundedMap.Unresolved(reference, $"parse failure: {exception.GetType().Name}");
        }

        IReadOnlyList<TechV2ExperimentalWindowResult> windows = TechDetectorV2Experimental.Analyze(
            map.HitObjects, GameplayAnalyzer.IsSpinner, GameplayAnalyzer.IsSlider,
            GameplayAnalyzer.IsComplexSlider, GameplayAnalyzer.GetObjectStartPosition,
            GameplayAnalyzer.GetObjectEndPosition, GameplayAnalyzer.GetTechTurnAngle);
        IReadOnlyList<TechV2WindowDecision> baseDecisions = TechV2ExperimentalDecisionEngine.Decide(windows);
        IReadOnlyList<TechV2WindowDecision> decisions = TechV2ExperimentalDecisionEngine
            .PromoteSustainedTechWithDiagnostics(baseDecisions).Decisions;
        TechV2PresenceResult presence = TechV2ExperimentalDecisionEngine.BuildPresence(
            decisions,
            map.HitObjects.FirstOrDefault()?.Time ?? 0,
            map.HitObjects.LastOrDefault()?.Time ?? 0);
        IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> byStart = windows.ToDictionary(window => window.StartIndex);
        IReadOnlyList<TechV2HumanStrongWindow> strong = decisions
            .Where(decision => decision.IsStrongTech)
            .OrderBy(decision => decision.StartIndex)
            .Select(decision =>
            {
                TechV2StrongTechWindowDetail detail = TechV2StrongTechForensicAudit.DescribeWindow(
                    decision, byStart[decision.StartIndex], map.HitObjects);
                bool g3WouldReject = !TechV2StrongTechForensicAudit.MovementSliderLowGeometryGuard.KeepsWindow(detail);
                return new TechV2HumanStrongWindow(reference.Key, reference.HumanTruth, detail, g3WouldReject);
            })
            .ToArray();

        return new TechV2HumanGroundedMap(
            reference, path, note, true,
            map.GameplayProfile.Identity.Primary,
            map.GameplayProfile.Identity.Secondary,
            decisions.Count(decision => decision.IsTechSupport),
            decisions.Count(decision => decision.IsSustainedTech),
            presence, decisions, strong);
    }

    // Shared by subsequent human-grounded, test-only forensic passes so they
    // resolve exactly the same difficulty rather than selecting a near match.
    internal static (string? Path, string Note) ResolvePath(
        TechV2HumanReference reference,
        IReadOnlyList<MlDatasetSample> samples)
    {
        if (!string.IsNullOrWhiteSpace(reference.FixtureName))
        {
            string fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps", reference.FixtureName);
            if (File.Exists(fixturePath))
            {
                return (fixturePath, "fixture exact");
            }
        }

        IReadOnlyList<string> candidates = samples
            .Select(sample => sample.SourceFilePath)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => Path.GetFileName(path).Contains(reference.TitleContains, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(path).Contains(reference.DifficultyContains, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        List<string> verified = [];
        foreach (string candidate in candidates)
        {
            try
            {
                Beatmap map = BeatmapParser.Load(candidate);
                if (map.Title.Contains(reference.TitleContains, StringComparison.OrdinalIgnoreCase)
                    && map.Version.Contains(reference.DifficultyContains, StringComparison.OrdinalIgnoreCase))
                {
                    verified.Add(candidate);
                }
            }
            catch (Exception exception) when (exception is IOException or FormatException or InvalidDataException)
            {
                // The reference stays unresolved unless a single parsable exact match exists.
            }
        }

        return verified.Count switch
        {
            1 => (verified[0], "local cache exact"),
            0 => (null, "no exact local cached source path"),
            _ => (null, $"ambiguous local cached matches ({verified.Count})"),
        };
    }

    private static TechV2HumanDistribution BuildDistribution(IReadOnlyList<TechV2HumanStrongWindow> windows) => new(
        windows.Count,
        windows.Count == 0 ? 0 : windows.Average(window => window.Detail.Geometry),
        windows.Count == 0 ? 0 : windows.Average(window => window.Detail.Movement),
        windows.Count == 0 ? 0 : windows.Average(window => window.Detail.Slider),
        windows.Count == 0 ? 0 : windows.Average(window => window.Detail.Motif),
        windows.Count == 0 ? 0 : windows.Average(window => window.Detail.Support.StructuralPivotCount),
        windows.Count == 0 ? 0 : windows.Average(window => window.Detail.Support.ValidTransitionCount),
        windows.Count == 0 ? 0 : windows.Average(window => window.Detail.AngleCoherenceWithSupport),
        windows.GroupBy(window => $"{window.Detail.BestFamily}+{window.Detail.SecondBestFamily}")
            .OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal));

    private static TechV2HumanGuardSimulation SimulateGuard(
        TechV2StrongTechGuardDefinition guard,
        IReadOnlyList<TechV2HumanGroundedMap> maps)
    {
        IReadOnlyList<TechV2HumanStrongWindow> tech = maps.Where(map => map.Resolved && map.Reference.HumanTruth == TechV2HumanTruth.Tech)
            .SelectMany(map => map.StrongTechWindows).ToArray();
        IReadOnlyList<TechV2HumanStrongWindow> nonTech = maps.Where(map => map.Resolved && map.Reference.HumanTruth == TechV2HumanTruth.NonTech)
            .SelectMany(map => map.StrongTechWindows).ToArray();
        bool keeps(TechV2HumanStrongWindow item) => guard.KeepsWindow(item.Detail);
        return new TechV2HumanGuardSimulation(
            guard.Name,
            tech.Count(item => !keeps(item)), tech.Count(keeps),
            nonTech.Count(item => !keeps(item)), nonTech.Count(keeps),
            maps.Where(map => map.Resolved && map.Reference.HumanTruth == TechV2HumanTruth.Tech && map.StrongTechWindows.Any(item => !keeps(item))).Select(map => map.Reference.Key).ToArray(),
            maps.Where(map => map.Resolved && map.Reference.HumanTruth == TechV2HumanTruth.NonTech && map.StrongTechWindows.Any(item => !keeps(item))).Select(map => map.Reference.Key).ToArray(),
            maps.FirstOrDefault(map => map.Reference.Key == "GloriousDays")?.StrongTechWindows.Any(keeps) ?? false);
    }

    private static IReadOnlyList<TechV2HumanAnchorAmplification> BuildAnchorAmplification(
        IReadOnlyList<TechV2HumanGroundedMap> maps) => maps
        .Where(map => map.Resolved)
        .SelectMany(map => map.StrongTechWindows.Select(window =>
        {
            TechV2Region? region = map.Presence.Regions.FirstOrDefault(candidate =>
                candidate.StartTime <= window.Detail.StartTime && candidate.EndTime >= window.Detail.EndTime);
            int supports = region is null ? 0 : map.Decisions.Count(decision =>
                decision.IsTechSupport && !decision.IsStrongTech
                && decision.StartIndex >= region.StartWindowIndex && decision.StartIndex <= region.EndWindowIndex);
            return new TechV2HumanAnchorAmplification(
                map.Reference.Key, map.Reference.HumanTruth,
                window.Detail.StartTime, window.Detail.EndTime,
                window.Detail.EndTime - window.Detail.StartTime,
                region?.Duration ?? 0, supports,
                map.Presence.PlayableDuration <= 0 || region is null ? 0 : region.Duration / map.Presence.PlayableDuration * 100);
        }))
        .OrderBy(item => item.MapKey, StringComparer.Ordinal).ThenBy(item => item.StrongWindowStartTime).ToArray();

    private static string SelectBestHypothesis(IReadOnlyList<TechV2HumanGuardSimulation> candidates)
    {
        TechV2HumanGuardSimulation? best = candidates
            .Where(candidate => candidate.GloriousDaysHasStrongTechAfterGuard)
            // This is a diagnostic ranking, not a proposed runtime formula:
            // a guard must remove non-Tech windows without simply deleting
            // substantially more human-Tech windows.
            .OrderByDescending(candidate => (double)candidate.HumanNonTechRemoved / (candidate.HumanTechRemoved + 1))
            .ThenByDescending(candidate => candidate.HumanNonTechRemoved)
            .ThenBy(candidate => candidate.HumanTechRemoved)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        return best is null
            ? "No candidate preserves a GLORIOUS DAYS StrongTech window in this resolved corpus."
            : $"{best.Name} has the best observed non-Tech-removal to human-Tech-removal balance while preserving GLORIOUS DAYS; this is not a runtime recommendation.";
    }

    private static void AppendAllStrongWindows(
        StringBuilder text,
        IReadOnlyList<TechV2HumanGroundedMap> maps)
    {
        text.AppendLine("ALL HUMAN-CORPUS STRONGTECH WINDOWS | Map | Human | time | top-2 pair | G/M/S/Motif | pivots | valid transitions | angle coherence | slider complexity | complex sliders | awkward | spacing variation");
        IReadOnlyList<TechV2HumanStrongWindow> windows = maps
            .Where(map => map.Resolved)
            .SelectMany(map => map.StrongTechWindows)
            .OrderBy(window => window.HumanTruth)
            .ThenBy(window => window.MapKey, StringComparer.Ordinal)
            .ThenBy(window => window.Detail.StartTime)
            .ToArray();
        if (windows.Count == 0)
        {
            text.AppendLine("  None");
            return;
        }

        foreach (TechV2HumanStrongWindow window in windows)
        {
            TechV2StrongTechWindowDetail detail = window.Detail;
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  {0} | {1} | {2:F0}->{3:F0}ms | {4}+{5} | {6:F3}/{7:F3}/{8:F3}/{9:F3} | {10} | {11} | {12:F3} | {13:F3} | {14} | {15} | {16}",
                window.MapKey, window.HumanTruth, detail.StartTime, detail.EndTime, detail.BestFamily, detail.SecondBestFamily,
                detail.Geometry, detail.Movement, detail.Slider, detail.Motif,
                detail.Support.StructuralPivotCount, detail.Support.ValidTransitionCount,
                detail.AngleCoherenceWithSupport, detail.SliderComplexity,
                detail.Support.ComplexSliderCount, detail.Support.AwkwardTransitionCount,
                detail.Support.SpacingVariationTransitionCount));
        }
    }

    private static void AppendDistribution(StringBuilder text, string heading, TechV2HumanDistribution distribution)
    {
        text.AppendLine($"{heading} | windows={distribution.WindowCount} | avg G/M/S/Mo={distribution.AverageGeometry:F3}/{distribution.AverageMovement:F3}/{distribution.AverageSlider:F3}/{distribution.AverageMotif:F3} | avg pivots/transitions={distribution.AverageStructuralPivots:F2}/{distribution.AverageValidTransitions:F2} | avg angle coherence={distribution.AverageAngleCoherence:F3}");
        text.AppendLine("  family pairs: " + (distribution.FamilyPairs.Count == 0 ? "None" : string.Join(", ", distribution.FamilyPairs.Select(pair => $"{pair.Key}={pair.Value}"))));
    }

    private static string DisplaySecondary(string? secondary) => string.IsNullOrWhiteSpace(secondary) ? "None" : secondary;
    private static string DisplayKeys(IReadOnlyList<string> keys) => keys.Count == 0 ? "None" : string.Join(", ", keys);
}

internal enum TechV2HumanTruth { Tech, NonTech }

internal sealed record TechV2HumanReference(string Key, TechV2HumanTruth HumanTruth, string TitleContains, string DifficultyContains, string? FixtureName = null);

internal sealed record TechV2HumanStrongWindow(string MapKey, TechV2HumanTruth HumanTruth, TechV2StrongTechWindowDetail Detail, bool G3WouldReject);

internal sealed record TechV2HumanGroundedMap(
    TechV2HumanReference Reference, string? Path, string ResolutionNote, bool Resolved,
    string IdentityPrimary, string IdentitySecondary, int TechSupportCount, int SustainedTechCount,
    TechV2PresenceResult Presence, IReadOnlyList<TechV2WindowDecision> Decisions,
    IReadOnlyList<TechV2HumanStrongWindow> StrongTechWindows)
{
    internal static TechV2HumanGroundedMap Unresolved(TechV2HumanReference reference, string note) => new(reference, null, note, false, "", "", 0, 0, new([], 0, 0, 0, 0, 0, 0), [], []);
}

internal sealed record TechV2HumanDistribution(int WindowCount, double AverageGeometry, double AverageMovement, double AverageSlider, double AverageMotif, double AverageStructuralPivots, double AverageValidTransitions, double AverageAngleCoherence, IReadOnlyDictionary<string, int> FamilyPairs);

internal sealed record TechV2HumanGuardSimulation(string Name, int HumanTechRemoved, int HumanTechKept, int HumanNonTechRemoved, int HumanNonTechKept, IReadOnlyList<string> HumanTechAffectedMaps, IReadOnlyList<string> HumanNonTechAffectedMaps, bool GloriousDaysHasStrongTechAfterGuard);

internal sealed record TechV2HumanAnchorAmplification(string MapKey, TechV2HumanTruth HumanTruth, double StrongWindowStartTime, double StrongWindowEndTime, double StrongWindowDuration, double RegionDuration, int NeighboringTechSupportAbsorbed, double RegionPresenceContributionPercent);

internal sealed record TechV2HumanGroundedAuditReport(IReadOnlyList<TechV2HumanGroundedMap> Maps, TechV2HumanDistribution HumanTechDistribution, TechV2HumanDistribution HumanNonTechDistribution, TechV2HumanGuardSimulation G3, IReadOnlyList<TechV2HumanGuardSimulation> SecondGuardCandidates, IReadOnlyList<TechV2HumanAnchorAmplification> AnchorAmplification, string BestNextHypothesis);
