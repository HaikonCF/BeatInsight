using BeatInsight.Models;

namespace BeatInsight.Tests.Analysis;

public sealed class IdentityAndTechTests
{
    [Fact]
    public void Analyze_ExitPrimordial_IsTechThenJump()
    {
        Beatmap map = FixtureLoader.Load(
            "Exit This Earth's Atmosphere [Primordial Nucleosynthesis].osu");

        GameplayProfile profile = map.GameplayProfile;

        Assert.Equal("Tech", profile.Identity.Primary);
        Assert.Equal("Jump", profile.Identity.Secondary);
        Assert.InRange(profile.TechPresence, 0.40, 1.0);
        Assert.True(profile.TechIntensity >= 40.0);
        Assert.NotEqual(profile.Identity.Primary, profile.Identity.Secondary);
    }

    [Fact]
    public void Analyze_Frozen_IsClassicMixedThenTech()
    {
        Beatmap map = FixtureLoader.Load("Frozen [Collab Insane].osu");

        Assert.Equal("Classic / Mixed", map.GameplayProfile.Identity.Primary);
        Assert.Equal("Tech", map.GameplayProfile.Identity.Secondary);
        Assert.NotEqual(
            map.GameplayProfile.Identity.Primary,
            map.GameplayProfile.Identity.Secondary);
    }

    [Fact]
    public void Analyze_CanYouUnderstandMe_IsJumpWithoutTech()
    {
        Beatmap map = FixtureLoader.Load("(can you) understand me [uhh].osu");

        Assert.Equal("Jump", map.GameplayProfile.Identity.Primary);
        Assert.True(string.IsNullOrWhiteSpace(map.GameplayProfile.Identity.Secondary));
        Assert.Equal(0.0, map.GameplayProfile.TechPresence);
        Assert.Equal(0.0, map.GameplayProfile.TechScore);
    }
}
