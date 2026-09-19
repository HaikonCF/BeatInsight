using BeatInsight.Models.TechPatterns;

namespace BeatInsight.Analysis.TechPatterns;

public enum PatternEvidenceStatus { Confirmed, PartialSupport, InsufficientEvidence }

public sealed record TechPatternMotif(double StartTime, double EndTime,
    IReadOnlyList<int> SupportingEventIndices, IReadOnlyList<TechPatternMatch> Matches,
    PatternEvidenceStatus Status, string ConfirmationReason);

public static class PatternEvidenceResolver
{
    // RecoveryV1 is intentionally not the default: the local hard-negative audit regressed.
    public static IReadOnlyList<TechPatternMotif> Resolve(IEnumerable<TechPatternMatch> matches,
        PatternConfirmationPolicy policy = PatternConfirmationPolicy.BaselineV1)
    {
        // Same supported events constitute ONE motif with multiple explanations.
        // Different intervals are deliberately not transitively grouped: support cannot grow a chain.
        var exact = matches.Where(m => m.SupportingEventIndices.Count > 0)
            .GroupBy(m => string.Join(",", m.SupportingEventIndices.Distinct().Order()))
            .Select(group =>
            {
                var unique = group.GroupBy(m => m.Type)
                    .Select(g => g.OrderByDescending(m => m.Similarity).ThenBy(m => m.StartTime).First())
                    .OrderBy(m => m.Type).ToArray();
                bool Has(TechPatternType type) => unique.Any(m => m.Type == type);
                bool exit = Has(TechPatternType.TechnicalSliderExit), s2s = Has(TechPatternType.SliderToSliderTransition);
                bool entry = Has(TechPatternType.TechnicalSliderEntry), rhythm = Has(TechPatternType.IrregularRhythmTransition);
                string reason = Has(TechPatternType.CoupledAimControl) ? "CoupledAimControl: complete ordered grammar"
                    : exit && s2s ? "Exit + S2S"
                    : s2s && entry ? "S2S + ENTRY_C"
                    : s2s && rhythm ? "S2S + IrregularRhythm"
                    : exit && entry && rhythm ? "Exit + ENTRY_C + IrregularRhythm" : "";
                if (reason.Length == 0 && policy == PatternConfirmationPolicy.RecoveryV1)
                {
                    var selfContained = unique.FirstOrDefault(SelfContainedPrimaryPolicy.CanConfirm);
                    if (selfContained is not null)
                        reason = $"SELF_CONTAINED_PRIMARY: {selfContained.Type} [{string.Join(", ", SelfContainedPrimaryPolicy.IndependentFamilies(selfContained))}]";
                }
                var status = reason.Length > 0 ? PatternEvidenceStatus.Confirmed
                    : exit || s2s || entry || rhythm ? PatternEvidenceStatus.PartialSupport : PatternEvidenceStatus.InsufficientEvidence;
                return new TechPatternMotif(unique.Min(m => m.StartTime), unique.Max(m => m.EndTime),
                    unique.SelectMany(m => m.SupportingEventIndices).Distinct().Order().ToArray(), unique, status, reason);
            }).OrderByDescending(m => m.SupportingEventIndices.Count).ThenBy(m => m.StartTime).ThenBy(m => m.EndTime).ToArray();

        var resolved = new List<TechPatternMotif>();
        foreach (var motif in exact)
        {
            // A triplet fully explained inside a confirmed four-object motif is an
            // additional explanation, not another independent confirming vote.
            // Never combine partial motifs to manufacture confirmation across intervals.
            int owner = resolved.FindIndex(candidate => candidate.Status == PatternEvidenceStatus.Confirmed
                && motif.SupportingEventIndices.All(candidate.SupportingEventIndices.Contains)
                && motif.StartTime >= candidate.StartTime && motif.EndTime <= candidate.EndTime);
            if (owner < 0) resolved.Add(motif);
            else
            {
                var existing = resolved[owner];
                resolved[owner] = existing with
                {
                    Matches = existing.Matches.Concat(motif.Matches).OrderBy(m => m.StartIndex).ThenBy(m => m.Type).ToArray(),
                    ConfirmationReason = motif.Status == PatternEvidenceStatus.Confirmed
                        ? existing.ConfirmationReason + "; also " + motif.ConfirmationReason : existing.ConfirmationReason,
                };
            }
        }
        return resolved.OrderBy(m => m.StartTime).ThenBy(m => m.EndTime).ToArray();
    }
}
