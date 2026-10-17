using BeatInsight.Integration.Game;
using System.Globalization;
using System.IO;

namespace BeatInsight.Tests.Integration.Game;

public sealed class NativeStableBeatmapIdentityReaderTests
{
    [Fact]
    public void ValidMetadataReturnsBothPositiveIds()
    {
        using var file = new TempOsuFile("BeatmapID:847314", "BeatmapSetID:128931");

        NativeStableBeatmapIdentity result =
            new NativeStableBeatmapIdentityReader().Read(file.Path);

        Assert.Equal(847314, result.BeatmapId);
        Assert.Equal(128931, result.BeatmapSetId);
    }

    [Fact]
    public void MissingBeatmapSetIdReturnsNullSetId()
    {
        using var file = new TempOsuFile("BeatmapID:847314");

        NativeStableBeatmapIdentity result =
            new NativeStableBeatmapIdentityReader().Read(file.Path);

        Assert.Equal(847314, result.BeatmapId);
        Assert.Null(result.BeatmapSetId);
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("0")]
    [InlineData("-1")]
    public void InvalidBeatmapSetIdReturnsNull(string value)
    {
        using var file = new TempOsuFile(
            "BeatmapID:847314",
            $"BeatmapSetID:{value}");

        NativeStableBeatmapIdentity result =
            new NativeStableBeatmapIdentityReader().Read(file.Path);

        Assert.Equal(847314, result.BeatmapId);
        Assert.Null(result.BeatmapSetId);
    }

    private sealed class TempOsuFile : IDisposable
    {
        internal TempOsuFile(params string[] metadata)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"BeatInsight-NativeStableIdentity-{Guid.NewGuid():N}.osu");
            File.WriteAllText(
                Path,
                string.Join(Environment.NewLine,
                    [
                        "osu file format v14",
                        "[General]",
                        "AudioFilename:audio.mp3",
                        "[Metadata]",
                        .. metadata,
                        "[Difficulty]",
                        "HPDrainRate:5"
                    ]));
        }

        internal string Path { get; }

        public void Dispose() => File.Delete(Path);
    }
}
