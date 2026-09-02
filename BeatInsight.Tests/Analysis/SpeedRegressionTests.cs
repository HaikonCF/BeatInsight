using BeatInsight.Models;

namespace BeatInsight.Tests.Analysis;

public sealed class SpeedRegressionTests
{
    [Fact]
    public void Analyze_Arles_HasHighSpeedPressure()
    {
        Beatmap map = FixtureLoader.Load("FREEDOM DiVE [Arles].osu");

        Assert.True(map.GameplayProfile.SpeedScore >= 70.0);
        Assert.Contains(
            "High Speed Pressure",
            map.GameplayProfile.Identity.Traits);
    }

    [Fact]
    public void Analyze_FrenZ_IsNotArtificiallySpeedDominant()
    {
        Beatmap map = FixtureLoader.Load(
            "FREEDOM DiVE [FrenZ's Insane].osu");

        Assert.InRange(map.GameplayProfile.SpeedScore, 10.0, 35.0);
        Assert.DoesNotContain(
            "High Speed Pressure",
            map.GameplayProfile.Identity.Traits);
        Assert.NotEqual("Speed", map.GameplayProfile.Identity.Primary);
    }
}
