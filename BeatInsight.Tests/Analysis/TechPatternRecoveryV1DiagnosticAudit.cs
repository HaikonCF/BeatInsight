using BeatInsight.Analysis.TechPatterns;
using BeatInsight.Models.TechPatterns;
using BeatInsight.Parser;
using System.Text;
using System.IO;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// Diagnostic audit to understand why RecoveryV1 shows no deltas.
/// Inspects raw pattern matches and self-confirmation eligibility.
/// </summary>
internal static class TechPatternRecoveryV1DiagnosticAudit
{
    internal static string Run()
    {
        var sb = new StringBuilder("TECH PATTERN RECOVERY V1 DIAGNOSTIC AUDIT\n");
        sb.AppendLine("Investigating why RecoveryV1 shows no deltas from BaselineV1\n");

        // Check GLORIOUS DAYS specifically
        var corpus = TechV2SliderDensityCalibrationAudit.Corpus;
        var glorious = corpus.FirstOrDefault(x => x.Name.Contains("GLORIOUS", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(glorious.Path) || !File.Exists(glorious.Path))
        {
            sb.AppendLine("GLORIOUS DAYS not found in corpus");
            return sb.ToString();
        }

        var beatmap = BeatmapParser.Load(glorious.Path);
        var matches = TechPatternRecognitionEngine.FindMatches(beatmap.HitObjects);

        sb.AppendLine($"=== GLORIOUS DAYS ===");
        sb.AppendLine($"Total raw matches found: {matches.Count}");
        sb.AppendLine();

        // Group by pattern type
        var byType = matches.GroupBy(m => m.Type).OrderBy(g => g.Key.ToString());
        foreach (var group in byType)
        {
            sb.AppendLine($"{group.Key}: {group.Count()} matches");
        }
        sb.AppendLine();

        // Check which matches could potentially self-confirm
        var eligibleForSelfConfirm = matches.Where(m =>
        {
            var families = SelfContainedPrimaryPolicy.IndependentFamilies(m);
            return families.Count >= 3;
        }).ToArray();

        sb.AppendLine($"Matches with ≥3 independent families: {eligibleForSelfConfirm.Length}");
        sb.AppendLine();

        if (eligibleForSelfConfirm.Length > 0)
        {
            sb.AppendLine("Eligible matches:");
            foreach (var match in eligibleForSelfConfirm.Take(5))
            {
                var families = SelfContainedPrimaryPolicy.IndependentFamilies(match);
                var canConfirm = SelfContainedPrimaryPolicy.CanConfirm(match);
                sb.AppendLine($"\n  {match.Type} @ {match.StartTime:F2}s");
                sb.AppendLine($"  Families ({families.Count}): [{string.Join(", ", families)}]");
                sb.AppendLine($"  Can self-confirm: {canConfirm}");
                sb.AppendLine($"  Similarity: {match.Similarity:F3}");

                if (match.InternalEvidence.Count > 0)
                {
                    sb.AppendLine($"  Internal Evidence:");
                    foreach (var ev in match.InternalEvidence)
                    {
                        sb.AppendLine($"    - {ev.Family}: {ev.Reason}");
                    }
                }
            }
        }

        // Check if any matches actually pass CanConfirm
        var selfConfirmable = matches.Where(SelfContainedPrimaryPolicy.CanConfirm).ToArray();
        sb.AppendLine($"\n\nMatches that pass CanConfirm(): {selfConfirmable.Length}");

        if (selfConfirmable.Length > 0)
        {
            sb.AppendLine("\nSelf-confirmable matches:");
            foreach (var match in selfConfirmable.Take(3))
            {
                sb.AppendLine($"\n  {match.Type} @ {match.StartTime:F2}s - {match.EndTime:F2}s");
                sb.AppendLine($"  Supporting events: [{string.Join(", ", match.SupportingEventIndices)}]");
                var families = SelfContainedPrimaryPolicy.IndependentFamilies(match);
                sb.AppendLine($"  Families: [{string.Join(", ", families)}]");
            }
        }

        // Now check what motifs are actually created
        var baselineMotifs = PatternEvidenceResolver.Resolve(matches, PatternConfirmationPolicy.BaselineV1);
        var recoveryMotifs = PatternEvidenceResolver.Resolve(matches, PatternConfirmationPolicy.RecoveryV1);

        sb.AppendLine($"\n\nMotifs:");
        sb.AppendLine($"Baseline confirmed: {baselineMotifs.Count(m => m.Status == PatternEvidenceStatus.Confirmed)}");
        sb.AppendLine($"Recovery confirmed: {recoveryMotifs.Count(m => m.Status == PatternEvidenceStatus.Confirmed)}");

        return sb.ToString();
    }
}
