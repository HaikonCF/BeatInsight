using BeatInsight.Analysis.TechPatterns;
using BeatInsight.Models.TechPatterns;
using BeatInsight.Parser;
using System.Text;
using System.IO;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// Final Self-Contained Primary Precision Gate forensic audit.
/// Compares BaselineV1 vs RecoveryV1 to identify newly self-confirmed motifs.
/// Determines if hard-negative false positives can be separated from recovered GLORIOUS motif.
/// </summary>
internal static class TechPatternFinalPrecisionGateForensicAudit
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
        var sb = new StringBuilder("TECH PATTERN V1 FINAL SELF-CONTAINED PRIMARY PRECISION GATE FORENSIC AUDIT\n");
        sb.AppendLine("Comparing BaselineV1 vs RecoveryV1 policy\n");

        var baselineResults = new List<(string Map, string Category, int Confirmed)>();
        var recoveryResults = new List<(string Map, string Category, int Confirmed)>();
        var deltaAnalysis = new List<(string Map, string Category, int Delta, TechPatternMotif[] NewMotifs)>();

        foreach (var map in Maps)
        {
            var path = ResolveMapPath(map.Name, map.FixtureFile);
            if (path is null)
            {
                sb.AppendLine($"{map.Name} | MAP_UNAVAILABLE\n");
                continue;
            }

            var beatmap = BeatmapParser.Load(path);
            var matches = TechPatternRecognitionEngine.FindMatches(beatmap.HitObjects);

            // Baseline
            var baselineMotifs = PatternEvidenceResolver.Resolve(matches, PatternConfirmationPolicy.BaselineV1);
            var baselineConfirmed = baselineMotifs.Count(m => m.Status == PatternEvidenceStatus.Confirmed);

            // Recovery
            var recoveryMotifs = PatternEvidenceResolver.Resolve(matches, PatternConfirmationPolicy.RecoveryV1);
            var recoveryConfirmed = recoveryMotifs.Count(m => m.Status == PatternEvidenceStatus.Confirmed);

            int delta = recoveryConfirmed - baselineConfirmed;

            // Find newly confirmed motifs
            var newMotifs = recoveryMotifs.Where(rm => rm.Status == PatternEvidenceStatus.Confirmed
                && !baselineMotifs.Any(bm => bm.Status == PatternEvidenceStatus.Confirmed
                    && Math.Abs(bm.StartTime - rm.StartTime) < 0.001
                    && Math.Abs(bm.EndTime - rm.EndTime) < 0.001)).ToArray();

            baselineResults.Add((map.Name, map.Category, baselineConfirmed));
            recoveryResults.Add((map.Name, map.Category, recoveryConfirmed));
            deltaAnalysis.Add((map.Name, map.Category, delta, newMotifs));

            sb.AppendLine($"=== {map.Name} ({map.Category}) ===");
            sb.AppendLine($"Baseline: {baselineConfirmed} confirmed | Recovery: {recoveryConfirmed} confirmed | Delta: {(delta >= 0 ? "+" : "")}{delta}");

            if (newMotifs.Length > 0)
            {
                sb.AppendLine($"\n{newMotifs.Length} newly self-confirmed motif(s):");
                foreach (var motif in newMotifs)
                {
                    sb.AppendLine($"\n  Time: {motif.StartTime:F2}s - {motif.EndTime:F2}s");
                    sb.AppendLine($"  Reason: {motif.ConfirmationReason}");
                    sb.AppendLine($"  Supporting indices: [{string.Join(", ", motif.SupportingEventIndices)}]");

                    foreach (var match in motif.Matches)
                    {
                        sb.AppendLine($"\n    Pattern: {match.Type}");
                        sb.AppendLine($"    Similarity: {match.Similarity:F3}");
                        sb.AppendLine($"    Geometry: {match.GeometrySimilarity:F3}");
                        sb.AppendLine($"    Rhythm: {match.RhythmSimilarity:F3}");
                        sb.AppendLine($"    Spacing: {match.SpacingSimilarity:F3}");
                        sb.AppendLine($"    SliderContext: {match.SliderContextSimilarity:F3}");

                        var families = SelfContainedPrimaryPolicy.IndependentFamilies(match);
                        sb.AppendLine($"    Independent Families ({families.Count}): [{string.Join(", ", families)}]");
                        sb.AppendLine($"    Can Self-Confirm: {SelfContainedPrimaryPolicy.CanConfirm(match)}");

                        if (match.Reasons.Count > 0)
                        {
                            sb.AppendLine($"    Reasons: {string.Join("; ", match.Reasons)}");
                        }

                        if (match.InternalEvidence.Count > 0)
                        {
                            sb.AppendLine($"    Internal Evidence:");
                            foreach (var evidence in match.InternalEvidence)
                            {
                                sb.AppendLine($"      - {evidence.Family}: {evidence.Reason} (events: [{string.Join(", ", evidence.EventIndices)}])");
                            }
                        }
                    }
                }
            }
            sb.AppendLine();
        }

        sb.AppendLine("\n=== DELTA SUMMARY ===");
        foreach (var (map, category, delta, newMotifs) in deltaAnalysis)
        {
            if (delta != 0)
                sb.AppendLine($"{map}: {(delta >= 0 ? "+" : "")}{delta} ({newMotifs.Length} new self-confirmed)");
        }

        sb.AppendLine("\n=== CATEGORY BREAKDOWN ===");
        sb.AppendLine("EXPECTED_TECH:");
        foreach (var (map, _, delta, newMotifs) in deltaAnalysis.Where(d => d.Item2 == "EXPECTED_TECH" && d.Delta > 0))
            sb.AppendLine($"  {map}: +{delta}");

        sb.AppendLine("\nHARD_NEGATIVE:");
        foreach (var (map, _, delta, newMotifs) in deltaAnalysis.Where(d => d.Item2 == "HARD_NEGATIVE" && d.Delta > 0))
            sb.AppendLine($"  {map}: +{delta}");

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
