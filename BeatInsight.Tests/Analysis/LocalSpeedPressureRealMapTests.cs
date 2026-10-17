using BeatInsight.Analysis.Speed;

namespace BeatInsight.Tests.Analysis;

public sealed class LocalSpeedPressureRealMapTests
{
    [Theory]
    [InlineData("FREEDOM DiVE [Arles].osu", 91.25)]
    [InlineData("Vickeblanka - Black Rover (TV Size) (Sotarks) [Extreme].osu", 51.49)]
    [InlineData("Kira Killer [Mocaotic's Insane].osu", 59.23)]
    [InlineData("Frozen [Collab Insane].osu", 52.20)]
    [InlineData("(can you) understand me [uhh].osu", 0.00)]
    public void AcceptedCorpusFixturesPreserveAuditedLocalPressure(string fixture, double expected)
    {
        double actual = LocalSpeedPressureAnalyzer.Analyze(FixtureLoader.Load(fixture)).PressurePercent;

        Assert.InRange(actual, expected - 0.01, expected + 0.01);
    }
}
