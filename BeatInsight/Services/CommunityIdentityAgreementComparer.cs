using BeatInsight.Models;

namespace BeatInsight.Services;

/// <summary>
/// Compare l'identité structurelle interne aux seules preuves
/// communautaires explicitement Stream, Jump ou Tech.
/// </summary>
public static class CommunityIdentityAgreementComparer
{
    public static CommunityIdentityAgreement Compare(
        IEnumerable<CommunityTag> communityTags,
        GameplayIdentity identity)
    {
        HashSet<string> expectedFamilies =
            GetExpectedFamilies(identity);

        var matchedFamilies = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        var conflictingFamilies = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        var evidence = new List<CommunityIdentityEvidence>();

        double relevantVoteMass = 0.0;
        double matchedVoteMass = 0.0;
        int relevantVotes = 0;

        foreach (CommunityTag tag in communityTags)
        {
            List<string> families =
                ResolveStructuralFamilies(tag.Name);

            int votes = Math.Max(0, tag.Votes);
            double voteWeight = Math.Log10(votes + 1);

            if (families.Count == 0 || voteWeight <= 0.0)
                continue;

            List<string> matched =
                families
                    .Where(expectedFamilies.Contains)
                    .ToList();

            List<string> conflicting =
                families
                    .Where(family => !expectedFamilies.Contains(family))
                    .ToList();

            // Un tag qui porte plusieurs familles partage son poids entre elles,
            // afin de ne pas masquer une famille concurrente.
            double familyWeight =
                voteWeight / families.Count;

            relevantVoteMass += voteWeight;
            matchedVoteMass += familyWeight * matched.Count;
            relevantVotes += votes;

            foreach (string family in matched)
                matchedFamilies.Add(family);

            foreach (string family in conflicting)
                conflictingFamilies.Add(family);

            evidence.Add(
                new CommunityIdentityEvidence
                {
                    Tag = tag.Name,
                    Votes = votes,
                    VoteWeight = voteWeight,
                    Families = families,
                    MatchedFamilies = matched,
                    ConflictingFamilies = conflicting
                });
        }

        bool hasStructuralEvidence = relevantVoteMass > 0.0;

        return new CommunityIdentityAgreement
        {
            HasStructuralEvidence = hasStructuralEvidence,
            Agreement = hasStructuralEvidence
                ? Math.Clamp(
                    matchedVoteMass / relevantVoteMass,
                    0.0,
                    1.0)
                : null,
            Reliability = hasStructuralEvidence
                ? Math.Clamp(
                    1.0 - Math.Exp(-relevantVoteMass / 3.0),
                    0.0,
                    1.0)
                : 0.0,
            RelevantVotes = relevantVotes,
            RelevantVoteMass = relevantVoteMass,
            MatchedFamilies = matchedFamilies.OrderBy(x => x).ToList(),
            ConflictingFamilies = conflictingFamilies.OrderBy(x => x).ToList(),
            Evidence = evidence
        };
    }

    private static HashSet<string> GetExpectedFamilies(
        GameplayIdentity identity)
    {
        var families = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        AddStructuralFamily(identity.Primary, families);
        AddStructuralFamily(identity.Secondary, families);

        return families;
    }

    private static void AddStructuralFamily(
        string value,
        HashSet<string> families)
    {
        if (value.Equals("Stream", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Jump", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Tech", StringComparison.OrdinalIgnoreCase))
        {
            families.Add(value);
        }
    }

    private static List<string> ResolveStructuralFamilies(string tagName)
    {
        string normalized =
            tagName
                .Trim()
                .ToLowerInvariant()
                .Replace("_", " ")
                .Replace("-", " ");

        var families = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        // Ces règles reprennent les correspondances structurelles déjà
        // présentes dans GameplayTagComparer, sans ses concepts de skill.
        if (normalized.Contains("stream"))
            families.Add("Stream");

        if (normalized.Contains("jump"))
            families.Add("Jump");

        if (normalized.Contains("tech")
            || normalized.Contains("technical")
            || normalized.Contains("finger"))
        {
            families.Add("Tech");
        }

        return families.OrderBy(x => x).ToList();
    }
}
