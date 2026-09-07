using BeatInsight.Models;
using BeatInsight.Models.Persistence;
using BeatInsight.Services.Ml;

namespace BeatInsight.Tests.Ml;

public sealed class HumanLabelPrefillPolicyTests
{
    [Theory]
    [InlineData(80.0)]
    [InlineData(95.0)]
    public void ConfidenceAtOrAboveThreshold_PrefillsPrimary(
        double confidence)
    {
        GameplayIdentity identity = CreateIdentity(
            confidence,
            primary: "Tech");

        Assert.True(
            HumanLabelPrefillPolicy.TryCreate(identity, out HumanLabelPrefill prefill));
        Assert.Equal(MlHumanLabel.Tech, prefill.Primary);
    }

    [Fact]
    public void NextUnlabeledSuggestion_UsesGameplayIdentityValues()
    {
        GameplayIdentity identity = CreateIdentity(
            confidence: 87.5,
            primary: "Jump",
            secondary: "Stream");

        Assert.True(
            HumanLabelPrefillPolicy.TryCreate(identity, out HumanLabelPrefill prefill));
        Assert.Equal(MlHumanLabel.Jump, prefill.Primary);
        Assert.Equal(MlHumanLabel.Stream, prefill.Secondary);
        Assert.Equal(87.5, identity.Confidence);
    }

    [Fact]
    public void PrefillSelection_MakesValidationEligibleWithoutSaving()
    {
        GameplayIdentity identity = CreateIdentity(
            confidence: 80.0,
            primary: "Classic/Mixed");

        Assert.True(
            HumanLabelPrefillPolicy.TryCreate(identity, out HumanLabelPrefill prefill));
        Assert.Equal(MlHumanLabel.ClassicMixed, prefill.Primary);

        MlDatasetSample sample = new()
        {
            PrimaryHumanLabel = null,
            HumanValidated = false,
        };

        Assert.False(sample.HumanValidated);
        Assert.Null(sample.PrimaryHumanLabel);
    }

    [Fact]
    public void UserOverride_CanReplacePrefilledPrimaryBeforeValidation()
    {
        GameplayIdentity identity = CreateIdentity(
            confidence: 90.0,
            primary: "Stream");

        Assert.True(
            HumanLabelPrefillPolicy.TryCreate(identity, out HumanLabelPrefill prefill));
        Assert.Equal(MlHumanLabel.Stream, prefill.Primary);

        MlHumanLabel userSelection = MlHumanLabel.Tech;

        Assert.NotEqual(prefill.Primary, userSelection);
        Assert.Equal(MlHumanLabel.Tech, userSelection);
    }

    [Fact]
    public void ConfidenceBelowThreshold_DoesNotPrefill()
    {
        GameplayIdentity identity = CreateIdentity(
            confidence: 79.99,
            primary: "Tech");

        Assert.False(HumanLabelPrefillPolicy.TryCreate(identity, out _));
    }

    [Fact]
    public void ValidStructuralSecondary_IsPrefilled()
    {
        GameplayIdentity identity = CreateIdentity(
            confidence: 80.0,
            primary: "Jump",
            secondary: "Tech");

        Assert.True(
            HumanLabelPrefillPolicy.TryCreate(identity, out HumanLabelPrefill prefill));
        Assert.Equal(MlHumanLabel.Jump, prefill.Primary);
        Assert.Equal(MlHumanLabel.Tech, prefill.Secondary);
    }

    [Fact]
    public void InvalidOrDuplicateSecondary_IsNotPrefilled()
    {
        GameplayIdentity invalid = CreateIdentity(
            confidence: 90.0,
            primary: "Jump",
            secondary: "Aim");
        GameplayIdentity duplicate = CreateIdentity(
            confidence: 90.0,
            primary: "Jump",
            secondary: "Jump");

        Assert.Null(GetSecondary(invalid));
        Assert.Null(GetSecondary(duplicate));
    }

    [Fact]
    public void PrefillDoesNotRepresentValidationOrCommunityData()
    {
        GameplayIdentity identity = CreateIdentity(
            confidence: 100.0,
            primary: "Stream");

        Assert.True(
            HumanLabelPrefillPolicy.TryCreate(identity, out HumanLabelPrefill prefill));

        MlDatasetSample sample = new()
        {
            PrimaryHumanLabel = null,
            SecondaryHumanLabel = null,
            HumanValidated = false,
            CommunityEvidenceJson = "{\"agreement\":0.8}",
        };

        Assert.Equal(MlHumanLabel.Stream, prefill.Primary);
        Assert.Null(sample.PrimaryHumanLabel);
        Assert.Null(sample.SecondaryHumanLabel);
        Assert.False(sample.HumanValidated);
        Assert.Equal("{\"agreement\":0.8}", sample.CommunityEvidenceJson);
    }

    private static MlHumanLabel? GetSecondary(GameplayIdentity identity)
    {
        return HumanLabelPrefillPolicy.TryCreate(
            identity,
            out HumanLabelPrefill prefill)
            ? prefill.Secondary
            : null;
    }

    private static GameplayIdentity CreateIdentity(
        double confidence,
        string primary,
        string secondary = "") => new()
        {
            Confidence = confidence,
            Primary = primary,
            Secondary = secondary,
        };
}
