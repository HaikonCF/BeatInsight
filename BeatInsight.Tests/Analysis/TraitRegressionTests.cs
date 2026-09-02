using BeatInsight.Models;

namespace BeatInsight.Tests.Analysis;

public sealed class TraitRegressionTests
{
    [Fact]
    public void Analyze_Tower_HasNoBurstInfluence()
    {
        Beatmap map = FixtureLoader.Load("Tower Of Heaven [Extra].osu");

        Assert.Equal("Stream", map.GameplayProfile.Identity.Primary);
        Assert.Equal("None", map.GameplayProfile.BurstPresence);
        Assert.DoesNotContain("Burst Influence", map.GameplayProfile.Identity.Traits);
    }

    [Fact]
    public void Analyze_Snow_UsesFinalReadScoreForTraits()
    {
        Beatmap map = FixtureLoader.Load("Snow Goose [Extra].osu");

        Assert.InRange(map.GameplayProfile.ReadScore, 35.0, 60.0);
        Assert.Contains("Reading Influence", map.GameplayProfile.Identity.Traits);
        Assert.DoesNotContain("High Reading Demand", map.GameplayProfile.Identity.Traits);
    }

    [Fact]
    public void Analyze_Frozen_ExposesTechSecondaryTraits()
    {
        Beatmap map = FixtureLoader.Load("Frozen [Collab Insane].osu");

        Assert.Contains("Tech Secondary", map.GameplayProfile.Identity.Traits);
        Assert.Contains("Technical Patterns", map.GameplayProfile.Identity.Traits);
    }

    [Fact]
    public void Analyze_Kira_RetainsTechnicalPatternsAndTechSecondary()
    {
        Beatmap map = FixtureLoader.Load("Kira Killer [Mocaotic's Insane].osu");

        Assert.Contains("Tech Secondary", map.GameplayProfile.Identity.Traits);
        Assert.Contains("Technical Patterns", map.GameplayProfile.Identity.Traits);
    }

    [Fact]
    public void Analyze_Ashioto_HasHighBurstPresence()
    {
        Beatmap map = FixtureLoader.Load(
            "Ashioto Tarte Tatin [Koori's Insane].osu");

        Assert.Equal("High", map.GameplayProfile.BurstPresence);
        Assert.Contains("Burst Influence", map.GameplayProfile.Identity.Traits);
    }

    [Fact]
    public void Analyze_Forever_HasBurstInfluence()
    {
        Beatmap map = FixtureLoader.Load(
            "forever we can make it! [Kudowari's Expert].osu");

        Assert.Equal("High", map.GameplayProfile.BurstPresence);
        Assert.Contains("Burst Influence", map.GameplayProfile.Identity.Traits);
    }
}
