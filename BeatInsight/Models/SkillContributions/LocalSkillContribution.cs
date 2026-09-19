using System.Collections.ObjectModel;

namespace BeatInsight.Models;

/// <summary>
/// Immutable object-indexed snapshot of an existing local skill calculation.
/// Pressure is normalized to [0, 1]. Weight preserves an analyzer's existing
/// aggregation semantics without introducing section-specific knowledge.
/// </summary>
public sealed record LocalSkillContribution(
    int ObjectIndex,
    double Pressure,
    bool IsEligible,
    bool IsCoverageQualified,
    double Weight = 1.0);

/// <summary>
/// Read-only contribution collection for one skill. CoverageSupported is false
/// when the analyzer cannot expose a faithful local coverage contract yet.
/// </summary>
public sealed class LocalSkillContributionSet
{
    public static LocalSkillContributionSet Unsupported { get; } =
        new(false, Array.Empty<LocalSkillContribution>());

    public static LocalSkillContributionSet EmptySupported { get; } =
        new(true, Array.Empty<LocalSkillContribution>());

    public LocalSkillContributionSet(
        bool coverageSupported,
        IEnumerable<LocalSkillContribution> contributions)
    {
        ArgumentNullException.ThrowIfNull(contributions);

        LocalSkillContribution[] snapshot = contributions.ToArray();
        Validate(snapshot);

        CoverageSupported = coverageSupported;
        Contributions = new ReadOnlyCollection<LocalSkillContribution>(snapshot);
    }

    public bool CoverageSupported { get; }

    public IReadOnlyList<LocalSkillContribution> Contributions { get; }

    private static void Validate(IReadOnlyList<LocalSkillContribution> contributions)
    {
        HashSet<int> objectIndices = [];

        foreach (LocalSkillContribution contribution in contributions)
        {
            if (contribution.ObjectIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(contributions), "ObjectIndex must be non-negative.");

            if (!double.IsFinite(contribution.Pressure)
                || contribution.Pressure < 0
                || contribution.Pressure > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(contributions), "Pressure must be finite and in [0, 1].");
            }

            if (!double.IsFinite(contribution.Weight)
                || contribution.Weight < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(contributions), "Weight must be finite and non-negative.");
            }

            if (contribution.IsCoverageQualified && !contribution.IsEligible)
                throw new ArgumentException("A coverage-qualified contribution must be eligible.", nameof(contributions));

            if (!objectIndices.Add(contribution.ObjectIndex))
                throw new ArgumentException("ObjectIndex must be unique within a skill contribution set.", nameof(contributions));
        }
    }
}

/// <summary>
/// Transient analytical output. It is intentionally separate from the
/// presentation-only GameplayStyleProfile and is not persisted.
/// </summary>
public sealed record GameplaySkillContributionProfile(
    LocalSkillContributionSet Aim,
    LocalSkillContributionSet Speed,
    LocalSkillContributionSet Reading,
    LocalSkillContributionSet Tech)
{
    public static GameplaySkillContributionProfile Empty { get; } = new(
        LocalSkillContributionSet.Unsupported,
        LocalSkillContributionSet.Unsupported,
        LocalSkillContributionSet.Unsupported,
        LocalSkillContributionSet.Unsupported);
}
