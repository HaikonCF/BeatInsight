using BeatInsight.Analysis.TechPatterns;
using BeatInsight.Models.TechPatterns;
using BeatInsight.Parser;
using System.Text;
using System.IO;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// Diagnostic audit to understand why InternalEvidence is not being counted as independent.
/// Verifies SupportingEventIndices alignment with evidence EventIndices.
/// </summary>
internal static class TechPatternInternalEvidenceDiagnostic
{
    internal static string Run()
    {
        var sb = new StringBuilder("TECH PATTERN INTERNAL EVIDENCE DIAGNOSTIC\n\n");

        var corpus = TechV2SliderDensityCalibrationAudit.Corpus;
        var glorious = corpus.FirstOrDefault(x => x.Name.Contains("GLORIOUS", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(glorious.Path) || !File.Exists(glorious.Path))
        {
            sb.AppendLine("GLORIOUS DAYS not found");
            return sb.ToString();
        }

        var beatmap = BeatmapParser.Load(glorious.Path);
        var matches = TechPatternRecognitionEngine.FindMatches(beatmap.HitObjects);

        // Find Exit and S2S matches with InternalEvidence
        var withEvidence = matches.Where(m => m.InternalEvidence.Count > 0).ToArray();

        sb.AppendLine($"Total matches: {matches.Count}");
        sb.AppendLine($"Matches with InternalEvidence: {withEvidence.Length}");
        sb.AppendLine();

        if (withEvidence.Length > 0)
        {
            sb.AppendLine("Sample matches with InternalEvidence:");
            foreach (var match in withEvidence.Take(3))
            {
                sb.AppendLine($"\n{match.Type} @ {match.StartTime:F2}s");
                sb.AppendLine($"  SupportingEventIndices: [{string.Join(", ", match.SupportingEventIndices)}]");
                sb.AppendLine($"  InternalEvidence count: {match.InternalEvidence.Count}");

                foreach (var ev in match.InternalEvidence)
                {
                    var contained = ev.EventIndices.All(match.SupportingEventIndices.Contains);
                    sb.AppendLine($"    - {ev.Family}: indices [{string.Join(", ", ev.EventIndices)}] | Contained: {contained}");
                    sb.AppendLine($"      Reason: {ev.Reason}");
                }

                var families = SelfContainedPrimaryPolicy.IndependentFamilies(match);
                sb.AppendLine($"  Independent families ({families.Count}): [{string.Join(", ", families)}]");
                sb.AppendLine($"  Can self-confirm: {SelfContainedPrimaryPolicy.CanConfirm(match)}");
            }
        }

        // Count by type
        var byType = withEvidence.GroupBy(m => m.Type);
        sb.AppendLine("\n\nBy pattern type:");
        foreach (var group in byType)
        {
            sb.AppendLine($"{group.Key}: {group.Count()} with evidence");
            var withEnoughFamilies = group.Count(m => SelfContainedPrimaryPolicy.IndependentFamilies(m).Count >= 3);
            sb.AppendLine($"  With ≥3 independent families: {withEnoughFamilies}");
        }

        return sb.ToString();
    }
}
