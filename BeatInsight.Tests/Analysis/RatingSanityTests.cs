using BeatInsight.Models;

namespace BeatInsight.Tests.Analysis;

public sealed class RatingSanityTests
{
    [Fact]
    public void Load_ValidMap_ProducesBoundedNonNegativeRating()
    {
        Beatmap map = FixtureLoader.Load("Tower Of Heaven [Extra].osu");
        GameplayProfile profile = map.GameplayProfile;

        FixtureLoader.AssertFinite(map.OsuStarRating, nameof(map.OsuStarRating));
        FixtureLoader.AssertFinite(
            map.BeatInsightRating,
            nameof(map.BeatInsightRating));
        FixtureLoader.AssertFinite(profile.AimScore, nameof(profile.AimScore));
        FixtureLoader.AssertFinite(profile.SpeedScore, nameof(profile.SpeedScore));
        FixtureLoader.AssertFinite(profile.ReadScore, nameof(profile.ReadScore));
        FixtureLoader.AssertFinite(profile.TechScore, nameof(profile.TechScore));

        Assert.True(map.OsuStarRating > 0.0);
        Assert.True(map.BeatInsightRating >= 0.0);
        Assert.InRange(
            map.BeatInsightRating - map.OsuStarRating,
            -1.000001,
            1.000001);
        Assert.True(
            string.IsNullOrWhiteSpace(profile.Identity.Secondary)
            || !profile.Identity.Primary.Equals(
                profile.Identity.Secondary,
                StringComparison.OrdinalIgnoreCase));
    }
}
