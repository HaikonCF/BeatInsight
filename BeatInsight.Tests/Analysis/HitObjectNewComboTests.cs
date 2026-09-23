using System.IO;
using BeatInsight.Parser;

namespace BeatInsight.Tests.Analysis;

public sealed class HitObjectNewComboTests
{
    [Fact]
    public void Parser_ReadsNewComboIndependentlyOfObjectKindAndComboColourBits()
    {
        string path = Path.Combine(Path.GetTempPath(), $"beatinsight-new-combo-{Guid.NewGuid():N}.osu");
        try
        {
            File.WriteAllText(path, """
                osu file format v14

                [General]
                Mode:0
                [Metadata]
                Title:New Combo Test
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
                100,100,0,1,0,0:0:0:0:
                200,100,600,5,0,0:0:0:0:
                100,100,1200,21,0,0:0:0:0:
                200,100,1800,2,0,L|300:100,1,100
                100,100,2400,6,0,L|200:100,1,100
                256,192,3000,8,0,3400
                256,192,4000,12,0,4400
                """);
            var map = BeatmapParser.Load(path);
            Assert.Equal(new[] { false, true, true, false, true, false, true },
                map.HitObjects.Select(o => o.IsNewCombo));
            Assert.Equal(new[] { 1, 5, 21, 2, 6, 8, 12 }, map.HitObjects.Select(o => o.Type));
            Assert.Equal(3, map.CircleCount);
            Assert.Equal(2, map.SliderCount);
            Assert.Equal(2, map.SpinnerCount);
        }
        finally { File.Delete(path); }
    }
}
