using BeatInsight.Analysis.TechPatterns;
using BeatInsight.Models.TechPatterns;
using BeatInsight.Parser;
using System.Text;
using System.IO;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// RecoveryV1 audit using the CORRECT TechPatternPresenceEngine.Analyze() V1 pipeline.
/// Compares BaselineV1 vs RecoveryV1 confirmed motifs and presence across 9-map corpus.
/// </summary>
internal static class TechPatternV1RecoveryAuditCorrectPipeline
{
    private static readonly (string Name, string Category, string? FixtureFile)[] Maps =
    [
        ("GLORIOUS DAYS", "EXPECTED_TECH", null),
        ("Kira Killer", "EXPECTED_TECH", "Kira Killer [Mocaotic''s Insane].osu"),
        ("The World Ends Now", "EXPECTED_TECH", null),
        ("Frozen", "EXPECTED_TECH", "Frozen [Collab Insane].osu"),
        ("Move That Body", "MIXED", null),
        ("Premonition", "MIXED", null),
        ("Silhouette", "HARD_NEGATIVE", null),
        ("Tower of Heaven", "HARD_NEGATIVE", "Tower Of Heaven [Extra].osu"),
        ("(can you) understand me?", "HARD_NEGATIVE", "(can you) understand me [uhh].osu")
    ];

    internal static string Run()
    {
        var sb = new StringBuilder("TECH PATTERN V1 RECOVERY AUDIT — CORRECT PIPELINE\n");
        sb.AppendLine("Using TechPatternPresenceEngine.Analyze() (actual V1 detector system)\n");

        var results = new List<(string Map, string Category, int BaselineConfirmed, int RecoveryConfirmed,
            double BaselinePresence, double RecoveryPresence)>();

        foreach (var map in Maps)
        {
            var path = ResolveMapPath(map.Name, map.FixtureFile);
            if (path is null)
            {
                sb.AppendLine($"{map.Name} | MAP_UNAVAILABLE\n");
                continue;
            }

            var beatmap = BeatmapParser.Load(path);

            // Baseline V1
            var baselineResult = TechPatternPresenceEngine.Analyze(beatmap, PatternConfirmationPolicy.BaselineV1);
            int baselineConfirmed = baselineResult.ConfirmedMotifCount;
            double baselinePresence = baselineResult.PresencePercent;

            // Recovery V1
            var recoveryResult = TechPatternPresenceEngine.Analyze(beatmap, PatternConfirmationPolicy.RecoveryV1);
            int recoveryConfirmed = recoveryResult.ConfirmedMotifCount;
            double recoveryPresence = recoveryResult.PresencePercent;

            int delta = recoveryConfirmed - baselineConfirmed;
            results.Add((map.Name, map.Category, baselineConfirmed, recoveryConfirmed,
                baselinePresence, recoveryPresence));

            sb.AppendLine($"=== {map.Name} ({map.Category}) ===");
            sb.AppendLine($"Baseline: {baselineConfirmed} confirmed motifs | {baselinePresence:F2}% presence");
            sb.AppendLine($"Recovery: {recoveryConfirmed} confirmed motifs | {recoveryPresence:F2}% presence");
            sb.AppendLine($"Delta: {(delta >= 0 ? "+" : "")}{delta} motifs");

            if (delta > 0)
            {
                // Find newly confirmed motifs
                var newMotifs = recoveryResult.Motifs.Where(rm =>
                    rm.Status == PatternEvidenceStatus.Confirmed &&
                    !baselineResult.Motifs.Any(bm =>
                        bm.Status == PatternEvidenceStatus.Confirmed &&
                        Math.Abs(bm.StartTime - rm.StartTime) < 0.001 &&
                        Math.Abs(bm.EndTime - rm.EndTime) < 0.001)).ToArray();

                sb.AppendLine($"\n{newMotifs.Length} newly confirmed motif(s) in RecoveryV1:");
                foreach (var motif in newMotifs.Take(3))
                {
                    sb.AppendLine($"  Time: {motif.StartTime:F2}s - {motif.EndTime:F2}s");
                    sb.AppendLine($"  Reason: {motif.ConfirmationReason}");

                    var selfContained = motif.Matches.Where(SelfContainedPrimaryPolicy.CanConfirm).ToArray();
                    if (selfContained.Length > 0)
                    {
                        foreach (var match in selfContained)
                        {
                            var families = SelfContainedPrimaryPolicy.IndependentFamilies(match);
                            sb.AppendLine($"    Self-contained: {match.Type} with families [{string.Join(", ", families)}]");
                        }
                    }
                }
            }
            sb.AppendLine();
        }

        sb.AppendLine("\n=== CATEGORY SUMMARY ===");
        foreach (var category in new[] { "EXPECTED_TECH", "MIXED", "HARD_NEGATIVE" })
        {
            var categoryMaps = results.Where(r => r.Category == category).ToArray();
            if (categoryMaps.Length == 0) continue;

            int totalBaselineConfirmed = categoryMaps.Sum(r => r.BaselineConfirmed);
            int totalRecoveryConfirmed = categoryMaps.Sum(r => r.RecoveryConfirmed);
            double avgBaselinePresence = categoryMaps.Average(r => r.BaselinePresence);
            double avgRecoveryPresence = categoryMaps.Average(r => r.RecoveryPresence);

            sb.AppendLine($"\n{category}:");
            sb.AppendLine($"  Baseline: {totalBaselineConfirmed} total confirmed | {avgBaselinePresence:F2}% avg presence");
            sb.AppendLine($"  Recovery: {totalRecoveryConfirmed} total confirmed | {avgRecoveryPresence:F2}% avg presence");
            sb.AppendLine($"  Delta: {(totalRecoveryConfirmed - totalBaselineConfirmed >= 0 ? "+" : "")}{totalRecoveryConfirmed - totalBaselineConfirmed} motifs");
        }

        sb.AppendLine("\n=== HARD NEGATIVE GUARD ===");
        var silhouette = results.FirstOrDefault(r => r.Map.Contains("Silhouette"));
        var tower = results.FirstOrDefault(r => r.Map.Contains("Tower"));
        var understand = results.FirstOrDefault(r => r.Map.Contains("understand"));

        bool silhouetteClean = silhouette.RecoveryConfirmed == 0;
        bool towerClean = tower.RecoveryConfirmed == 0;
        bool understandClean = understand.RecoveryConfirmed == 0;

        sb.AppendLine($"Silhouette: Baseline {silhouette.BaselineConfirmed} → Recovery {silhouette.RecoveryConfirmed} | Clean: {silhouetteClean}");
        sb.AppendLine($"Tower of Heaven: Baseline {tower.BaselineConfirmed} → Recovery {tower.RecoveryConfirmed} | Clean: {towerClean}");
        sb.AppendLine($"(can you) understand me?: Baseline {understand.BaselineConfirmed} → Recovery {understand.RecoveryConfirmed} | Clean: {understandClean}");

        bool guardPassed = silhouetteClean && towerClean && understandClean;
        sb.AppendLine($"\nHARD_NEGATIVE_GUARD_PASSED = {guardPassed}");
        sb.AppendLine($"RECOVERY_V1_SAFE = {(guardPassed ? "YES" : "NO")}");

        return sb.ToString();
    }

    private static string? ResolveMapPath(string name, string? fixtureFile)
    {
        if (fixtureFile is not null)
        {
            var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps", fixtureFile);
            if (File.Exists(fixturePath))
                return fixturePath;
        }

        var corpusEntry = TechV2SliderDensityCalibrationAudit.Corpus
            .FirstOrDefault(x => x.Name.Contains(name, StringComparison.OrdinalIgnoreCase));

        return !string.IsNullOrEmpty(corpusEntry.Path) && File.Exists(corpusEntry.Path)
            ? corpusEntry.Path
            : null;
    }
}
