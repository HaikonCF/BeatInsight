using BeatInsight.Analysis.TechPatterns;
using BeatInsight.Models.TechPatterns;
using BeatInsight.Parser;
using System.Text;
using System.IO;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// Verifies the actual TechPatternPresenceEngine.Analyze() V1 pipeline.
/// Reports InternalEvidence status for Exit, S2S, Entry, and CoupledAimControl patterns.
/// </summary>
internal static class TechPatternV1ActualPipelineVerification
{
    private static readonly (string Name, string Category, string? FixtureFile)[] TestMaps =
    [
        ("GLORIOUS DAYS", "EXPECTED_TECH", null),
        ("Silhouette", "HARD_NEGATIVE", null),
        ("(can you) understand me?", "HARD_NEGATIVE", "(can you) understand me [uhh].osu")
    ];

    internal static string Run()
    {
        var sb = new StringBuilder("TECH PATTERN V1 ACTUAL PIPELINE VERIFICATION\n\n");

        sb.AppendLine("Testing TechPatternPresenceEngine.Analyze() — the actual V1 detector pipeline");
        sb.AppendLine("NOT TechPatternRecognitionEngine.FindMatches() (similarity prototype)\n");

        bool anyEvidenceFound = false;
        int totalMatches = 0;
        int totalWithEvidence = 0;

        foreach (var map in TestMaps)
        {
            var path = ResolveMapPath(map.Name, map.FixtureFile);
            if (path is null)
            {
                sb.AppendLine($"{map.Name} | MAP_UNAVAILABLE\n");
                continue;
            }

            var beatmap = BeatmapParser.Load(path);
            var result = TechPatternPresenceEngine.Analyze(beatmap, PatternConfirmationPolicy.BaselineV1);

            sb.AppendLine($"=== {map.Name} ({map.Category}) ===");
            sb.AppendLine($"Total matches from V1 detectors: {result.RecognizedPatterns.Count}");

            // Filter by primary patterns only
            var primaryPatterns = result.RecognizedPatterns.Where(m =>
                m.Type is TechPatternType.TechnicalSliderExit
                       or TechPatternType.SliderToSliderTransition
                       or TechPatternType.TechnicalSliderEntry
                       or TechPatternType.CoupledAimControl).ToArray();

            sb.AppendLine($"Primary patterns (Exit/S2S/Entry/Coupled): {primaryPatterns.Length}");

            var withEvidence = primaryPatterns.Where(m => m.InternalEvidence.Count > 0).ToArray();
            sb.AppendLine($"Primary patterns with InternalEvidence: {withEvidence.Length}");

            totalMatches += primaryPatterns.Length;
            totalWithEvidence += withEvidence.Length;

            if (withEvidence.Length > 0)
            {
                anyEvidenceFound = true;
                sb.AppendLine("\nSample patterns with InternalEvidence:");

                foreach (var match in withEvidence.Take(2))
                {
                    sb.AppendLine($"\n  {match.Type} @ {match.StartTime:F2}s - {match.EndTime:F2}s");
                    sb.AppendLine($"    SupportingEventIndices: [{string.Join(", ", match.SupportingEventIndices)}]");
                    sb.AppendLine($"    InternalEvidence: {match.InternalEvidence.Count} items");

                    foreach (var ev in match.InternalEvidence)
                    {
                        bool contained = ev.EventIndices.All(match.SupportingEventIndices.Contains);
                        sb.AppendLine($"      - Family: {ev.Family}");
                        sb.AppendLine($"        EventIndices: [{string.Join(", ", ev.EventIndices)}]");
                        sb.AppendLine($"        Contained in Supporting: {contained}");
                        sb.AppendLine($"        Reason: {ev.Reason}");
                    }

                    var families = SelfContainedPrimaryPolicy.IndependentFamilies(match);
                    sb.AppendLine($"    IndependentFamilies: [{string.Join(", ", families)}] (count: {families.Count})");
                    sb.AppendLine($"    CanSelfConfirm: {SelfContainedPrimaryPolicy.CanConfirm(match)}");
                }
            }

            // Pattern type breakdown
            sb.AppendLine("\nBy pattern type:");
            foreach (var type in new[] {
                TechPatternType.TechnicalSliderExit,
                TechPatternType.SliderToSliderTransition,
                TechPatternType.TechnicalSliderEntry,
                TechPatternType.CoupledAimControl })
            {
                var typeMatches = primaryPatterns.Where(m => m.Type == type).ToArray();
                var typeWithEvidence = typeMatches.Where(m => m.InternalEvidence.Count > 0).ToArray();
                var typeWith3Families = typeWithEvidence.Where(m =>
                    SelfContainedPrimaryPolicy.IndependentFamilies(m).Count >= 3).ToArray();

                if (typeMatches.Length > 0)
                {
                    sb.AppendLine($"  {type}: {typeMatches.Length} total, " +
                        $"{typeWithEvidence.Length} with evidence, " +
                        $"{typeWith3Families.Length} with ≥3 families");
                }
            }

            sb.AppendLine();
        }

        sb.AppendLine("\n=== SUMMARY ===");
        sb.AppendLine($"Total primary patterns across test maps: {totalMatches}");
        sb.AppendLine($"Total with InternalEvidence: {totalWithEvidence}");
        sb.AppendLine($"ACTUAL_V1_PIPELINE_HAS_INTERNAL_EVIDENCE = {(anyEvidenceFound ? "YES" : "NO")}");

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
