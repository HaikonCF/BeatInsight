using System.IO;
using BeatInsight.Parser;
using BeatInsight.Services;

namespace BeatInsight.Tests.Analysis;

public sealed class MaxComboV2Tests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Theory]
    [InlineData("100,100,0,1,0,0:0:0:0:\n200,100,1000,1,0,0:0:0:0:", 2)]
    [InlineData("100,100,0,2,0,L|300:100,1,200", 3)]
    [InlineData("100,100,0,2,0,L|300:100,2,200", 5)]
    public void ParserUsesSameOsuDifficultyPassForStarsAndCombo(string hitObjects, int expectedCombo)
    {
        string path = Path.Combine(Path.GetTempPath(), $"beatinsight-combo-v2-{Guid.NewGuid():N}.osu");
        try
        {
            File.WriteAllText(path, """
                osu file format v14

                [General]
                Mode:0
                [Metadata]
                Title:Combo Test
                Artist:Test
                Creator:Test
                Version:Test
                [Difficulty]
                ApproachRate:5
                OverallDifficulty:5
                CircleSize:4
                HPDrainRate:5
                SliderMultiplier:1.4
                SliderTickRate:1
                [TimingPoints]
                0,500,4,2,0,50,1,0
                [HitObjects]
                """ + "\n" + hitObjects + "\n");

            var map = BeatmapParser.Load(path);
            var summary = OsuStarRatingCalculator.CalculateSummary(path);

            Assert.Equal(expectedCombo, summary.MaxCombo);
            Assert.Equal(summary.MaxCombo, map.OsuMaxCombo);
            Assert.Equal(summary.StarRating, map.OsuStarRating);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(0, 1498)]
    [InlineData(3, 1787)]
    [InlineData(4, 505)]
    [InlineData(5, 979)]
    [InlineData(12, 887)]
    [InlineData(13, 1936)]
    [InlineData(17, 454)]
    public void RealAuditMapComboMatchesOsuReference(int corpusIndex, int expectedCombo)
    {
        string path = StreamPatternGlobalForensicTests.Corpus[corpusIndex].Path;
        Assert.True(File.Exists(path), $"Missing audit map: {path}");

        var map = BeatmapParser.Load(path);
        Assert.Equal(expectedCombo, map.OsuMaxCombo);
        Assert.Equal(StreamPatternGlobalForensicTests.Corpus[corpusIndex].Title, map.Title);
        Assert.Equal(StreamPatternGlobalForensicTests.Corpus[corpusIndex].Version, map.Version);
        output.WriteLine($"{map.Title} [{map.Version}]: MaxCombo={map.OsuMaxCombo}; expected={expectedCombo}; delta={map.OsuMaxCombo - expectedCombo}; SHA256={Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))}");
    }
    [Theory]
    [InlineData(14, "100,100,1000,2,0,L|380:100,2,280", 3000, 5)]
    [InlineData(5, "100,100,1000,2,0,L|380:100,2,280", 3000, 5)]
    [InlineData(14, "256,192,1000,8,0,3000", 3000, 1)]
    public void PlayableWindowIncludesSliderAndSpinnerEnds(int version, string hitObject, double expectedEnd, int expectedCombo)
    {
        string path = Path.Combine(Path.GetTempPath(), $"beatinsight-playable-v2-{Guid.NewGuid():N}.osu");
        try
        {
            // v5 timing lines omit the later uninherited flag; positive tempos are red points.
            string timing = version == 5 ? "0,500\n2000,400\n4000,200" :
                "0,500,4,2,0,50,1,0\n2000,400,4,2,0,50,1,0\n4000,200,4,2,0,50,1,0";
            File.WriteAllText(path, $"""
                osu file format v{version}

                [General]
                Mode:0
                [Metadata]
                Title:Playable window
                Artist:Test
                Creator:Test
                Version:Test
                [Difficulty]
                ApproachRate:5
                OverallDifficulty:5
                CircleSize:4
                HPDrainRate:5
                SliderMultiplier:1.4
                SliderTickRate:1
                [TimingPoints]
                {timing}
                [HitObjects]
                {hitObject}
                """);
            var map = BeatmapParser.Load(path);
            var summary = OsuStarRatingCalculator.CalculateSummary(path);
            // The difficulty decoder disables the legacy global audio offset.
            Assert.Equal(1000, summary.FirstObjectTime);
            Assert.Equal(expectedEnd, summary.LastObjectEndTime);
            Assert.Equal(expectedCombo, map.OsuMaxCombo);
            Assert.Equal(120, map.BpmV2.PrimaryBpm);
            Assert.Equal(120, map.BpmV2.MinBpm);
            Assert.Equal(150, map.BpmV2.MaxBpm);
            Assert.True(map.BpmV2.IsVariableBpm);
        }
        finally { File.Delete(path); }
    }
}
