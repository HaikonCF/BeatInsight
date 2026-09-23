using BeatInsight.Services.Ml;

namespace BeatInsight.Tests.Services;

public sealed class LabelingQueueNavigationPolicyTests
{
    [Fact]
    public void SuccessfulTechValidation_RequestsTheNextTechCandidate()
    {
        Assert.True(
            LabelingQueueNavigationPolicy
                .ShouldAutoAdvanceAfterSuccessfulValidation(
                    saveSucceeded: true,
                    LabelingQueueKind.TechCandidates));
    }

    [Fact]
    public void NormalValidation_DoesNotRequestNavigation()
    {
        Assert.False(
            LabelingQueueNavigationPolicy
                .ShouldAutoAdvanceAfterSuccessfulValidation(
                    saveSucceeded: true,
                    LabelingQueueKind.None));
    }

    [Fact]
    public void FailedTechValidation_DoesNotRequestNavigation()
    {
        Assert.False(
            LabelingQueueNavigationPolicy
                .ShouldAutoAdvanceAfterSuccessfulValidation(
                    saveSucceeded: false,
                    LabelingQueueKind.TechCandidates));
    }

    [Fact]
    public void TechCandidates_RemainExcludedFromSkipNavigation()
    {
        Assert.Null(LabelingQueueNavigationPolicy.GetSkipTarget(
            LabelingQueueKind.TechCandidates));
    }
}
