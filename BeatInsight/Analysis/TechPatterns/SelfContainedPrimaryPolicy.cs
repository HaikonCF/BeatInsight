using BeatInsight.Models.TechPatterns;

namespace BeatInsight.Analysis.TechPatterns;

/// <summary>RecoveryV1 is an opt-in diagnostic candidate, rejected by the nine-map regression audit.</summary>
public enum PatternConfirmationPolicy { BaselineV1, RecoveryV1 }

public static class SelfContainedPrimaryPolicy
{
    private const int MinimumIndependentFamilies = 3;

    public static IReadOnlyList<TechEvidenceFamily> IndependentFamilies(TechPatternMatch match) =>
        match.InternalEvidence.Where(e => e.EventIndices.Count > 0
            && e.EventIndices.All(match.SupportingEventIndices.Contains))
            .Select(e => e.Family).Distinct().Order().ToArray();

    public static bool CanConfirm(TechPatternMatch match)
    {
        var families = IndependentFamilies(match);
        if (families.Count < MinimumIndependentFamilies) return false;
        return match.Type switch
        {
            TechPatternType.TechnicalSliderExit => families.Contains(TechEvidenceFamily.Direction)
                && !families.Contains(TechEvidenceFamily.CoupledSequence),
            TechPatternType.SliderToSliderTransition => families.Contains(TechEvidenceFamily.Direction)
                && families.Contains(TechEvidenceFamily.Handoff),
            TechPatternType.CoupledAimControl => families.Contains(TechEvidenceFamily.CoupledSequence)
                && families.Contains(TechEvidenceFamily.Direction) && families.Contains(TechEvidenceFamily.Spacing),
            _ => false,
        };
    }
}
