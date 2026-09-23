using BeatInsight.Analysis.TechPatterns;
using BeatInsight.Models.TechPatterns;
using BeatInsight.Parser;
using System.Text;
using System.IO;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// Verifies that TechPatternPresenceEngine.Analyze() properly populates InternalEvidence.
/// Compares ITechPatternDetector system vs TechPatternRecognitionEngine system.
/// </summary>
internal static class TechPatternPresenceEngineInternalEvidenceVerification
{
    internal static string Run()
    {
        var sb = new StringBuilder("TECH PATTERN PRESENCE ENGINE INTERNAL EVIDENCE VERIFICATION\n\n");

        var corpus = TechV2SliderDensityCalibrationAudit.Corpus;
        var glorious = corpus.FirstOrDefault(x => x.Name.Contains("GLORIOUS", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(glorious.Path) || !File.Exists(glorious.Path))
        {
            sb.AppendLine("GLORIOUS DAYS not found");
            return sb.ToString();
        }

        var beatmap = BeatmapParser.Load(glorious.Path);

        // System 1: TechPatternPresenceEngine (uses ITechPatternDetector)
        var presenceResult = TechPatternPresenceEngine.Analyze(beatmap, PatternConfirmationPolicy.BaselineV1);
        var withEvidence = presenceResult.RecognizedPatterns.Where(m => m.InternalEvidence.Count > 0).ToArray();

        sb.AppendLine("=== SYSTEM 1: TechPatternPresenceEngine (ITechPatternDetector) ===");
        sb.AppendLine($"Total matches: {presenceResult.RecognizedPatterns.Count}");
        sb.AppendLine($"Matches with InternalEvidence: {withEvidence.Length}");
        sb.AppendLine();

        if (withEvidence.Length > 0)
        {
            sb.AppendLine("Sample matches with InternalEvidence:");
            foreach (var match in withEvidence.Take(3))
            {
                sb.AppendLine($"\n  {match.Type} @ {match.StartTime:F2}s");
                sb.AppendLine($"    SupportingEventIndices: [{string.Join(", ", match.SupportingEventIndices)}]");
                sb.AppendLine($"    InternalEvidence ({match.InternalEvidence.Count} items):");
                foreach (var ev in match.InternalEvidence)
                {
                    var contained = ev.EventIndices.All(match.SupportingEventIndices.Contains);
                    sb.AppendLine($"      - {ev.Family}: indices [{string.Join(", ", ev.EventIndices)}] | Contained: {contained}");
                }
                var families = SelfContainedPrimaryPolicy.IndependentFamilies(match);
                sb.AppendLine($"    Independent families: [{string.Join(", ", families)}] ({families.Count})");
                sb.AppendLine($"    Can self-confirm: {SelfContainedPrimaryPolicy.CanConfirm(match)}");
            }
        }

        sb.AppendLine("\n\nBy pattern type:");
        var byType = withEvidence.GroupBy(m => m.Type);
        foreach (var group in byType)
        {
            var withEnoughFamilies = group.Count(m => SelfContainedPrimaryPolicy.IndependentFamilies(m).Count >= 3);
            sb.AppendLine($"  {group.Key}: {group.Count()} with evidence, {withEnoughFamilies} with ≥3 families");
        }

        // System 2: TechPatternRecognitionEngine (similarity-based)
        var recognitionMatches = TechPatternRecognitionEngine.FindMatches(beatmap.HitObjects);
        var withEvidenceRec = recognitionMatches.Where(m => m.InternalEvidence.Count > 0).ToArray();

        sb.AppendLine("\n\n=== SYSTEM 2: TechPatternRecognitionEngine (Similarity) ===");
        sb.AppendLine($"Total matches: {recognitionMatches.Count}");
        sb.AppendLine($"Matches with InternalEvidence: {withEvidenceRec.Length}");

        sb.AppendLine("\n\n=== CONCLUSION ===");
        if (withEvidence.Length > 0)
        {
            sb.AppendLine("INTERNAL_EVIDENCE_LOSS_POINT = TechPatternRecognitionEngine (similarity system has no InternalEvidence)");
            sb.AppendLine("CORRECT_SYSTEM = TechPatternPresenceEngine.Analyze() (uses ITechPatternDetector with InternalEvidence)");
        }
        else
        {
            sb.AppendLine("INTERNAL_EVIDENCE_LOSS_POINT = Still unknown - even TechPatternPresenceEngine has no evidence");
        }

        return sb.ToString();
    }
}
