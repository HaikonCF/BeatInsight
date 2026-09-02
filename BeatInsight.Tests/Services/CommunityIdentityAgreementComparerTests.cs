using BeatInsight.Models;
using BeatInsight.Services;

namespace BeatInsight.Tests.Services;

public sealed class CommunityIdentityAgreementComparerTests
{
    [Fact]
    public void Compare_NoStructuralEvidence_IsUnavailable()
    {
        GameplayIdentity identity = new()
        {
            Primary = "Classic / Mixed"
        };

        CommunityIdentityAgreement result =
            CommunityIdentityAgreementComparer.Compare(
                [
                    new CommunityTag { Name = "style/freeform", Votes = 50 },
                    new CommunityTag { Name = "skillset/aim", Votes = 10 }
                ],
                identity);

        Assert.False(result.HasStructuralEvidence);
        Assert.Null(result.Agreement);
        Assert.Equal(0.0, result.Reliability);
        Assert.Equal(0, result.RelevantVotes);
        Assert.Equal(0.0, result.RelevantVoteMass);
        Assert.Empty(result.MatchedFamilies);
        Assert.Empty(result.ConflictingFamilies);
    }

    [Fact]
    public void Compare_MatchingAndConflictingTags_UsesLogVoteMass()
    {
        GameplayIdentity identity = new()
        {
            Primary = "Stream",
            Secondary = "Tech"
        };

        CommunityIdentityAgreement result =
            CommunityIdentityAgreementComparer.Compare(
                [
                    new CommunityTag { Name = "skillset/streams", Votes = 9 },
                    new CommunityTag { Name = "skillset/tech", Votes = 99 },
                    new CommunityTag { Name = "skillset/jumps", Votes = 9 },
                    new CommunityTag { Name = "style/freeform", Votes = 999 }
                ],
                identity);

        const double expectedMass = 4.0;
        double expectedReliability = 1.0 - Math.Exp(-expectedMass / 3.0);

        Assert.True(result.HasStructuralEvidence);
        Assert.Equal(117, result.RelevantVotes);
        Assert.Equal(expectedMass, result.RelevantVoteMass, 10);
        Assert.Equal(0.75, result.Agreement!.Value, 10);
        Assert.Equal(expectedReliability, result.Reliability, 10);
        Assert.Equal(["Stream", "Tech"], result.MatchedFamilies);
        Assert.Equal(["Jump"], result.ConflictingFamilies);
        Assert.Equal(3, result.Evidence.Count);
    }

    [Fact]
    public void Compare_ClassicMixedWithTechSecondary_ExpectsOnlyTech()
    {
        GameplayIdentity identity = new()
        {
            Primary = "Classic / Mixed",
            Secondary = "Tech"
        };

        CommunityIdentityAgreement result =
            CommunityIdentityAgreementComparer.Compare(
                [
                    new CommunityTag { Name = "slider tech", Votes = 9 },
                    new CommunityTag { Name = "skillset/jumps", Votes = 9 }
                ],
                identity);

        Assert.True(result.HasStructuralEvidence);
        Assert.Equal(0.5, result.Agreement!.Value, 10);
        Assert.Equal(["Tech"], result.MatchedFamilies);
        Assert.Equal(["Jump"], result.ConflictingFamilies);
    }
}
