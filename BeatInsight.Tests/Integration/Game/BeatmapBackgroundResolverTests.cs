using BeatInsight.Integration.Game;
using System.IO;

namespace BeatInsight.Tests.Integration.Game;

public sealed class BeatmapBackgroundResolverTests
{
    [Fact]
    public void StandardBackgroundFilename_IsExtractedWithoutAdjacentFile()
    {
        using var scope = new TempScope();
        string beatmap = scope.Beatmap("[Events]\n0,0,BG3.jpg,0,0");

        string? result = BeatmapBackgroundResolver.ExtractBackgroundFileName(beatmap);

        Assert.Equal("BG3.jpg", result);
    }

    [Fact]
    public void QuotedRelativeBackgroundFilename_IsExtracted()
    {
        using var scope = new TempScope();
        string beatmap = scope.Beatmap(
            "[Events]\n0,0,\"subfolder\\my background.PNG\",0,0");

        string? result = BeatmapBackgroundResolver.ExtractBackgroundFileName(beatmap);

        Assert.Equal("subfolder\\my background.PNG", result);
    }

    [Fact]
    public void NoBackgroundEvent_ExtractsNull()
    {
        using var scope = new TempScope();
        string beatmap = scope.Beatmap("[Events]\n// no background\n[TimingPoints]");

        Assert.Null(BeatmapBackgroundResolver.ExtractBackgroundFileName(beatmap));
    }

    [Fact]
    public void StandardBackground_IsResolved()
    {
        using var scope = new TempScope();
        string background = scope.File("background.jpg", "image");
        string beatmap = scope.Beatmap("[Events]\n0,0,background.jpg,0,0");

        string? result = BeatmapBackgroundResolver.Resolve(beatmap);

        Assert.Equal(background, result);
    }

    [Fact]
    public void QuotedFilename_IsResolved()
    {
        using var scope = new TempScope();
        string background = scope.File("my background.PNG", "image");
        string beatmap = scope.Beatmap(
            "[Events]\n0,0,\"my background.PNG\",0,0");

        string? result = BeatmapBackgroundResolver.Resolve(beatmap);

        Assert.Equal(background, result);
    }

    [Fact]
    public void MissingBackground_ReturnsNull()
    {
        using var scope = new TempScope();
        string beatmap = scope.Beatmap("[Events]\n// no background");

        Assert.Null(BeatmapBackgroundResolver.Resolve(beatmap));
    }

    [Fact]
    public void MissingFile_ReturnsNull()
    {
        using var scope = new TempScope();
        string beatmap = scope.Beatmap(
            "[Events]\n0,0,\"missing.jpeg\",0,0");

        Assert.Null(BeatmapBackgroundResolver.Resolve(beatmap));
    }

    [Fact]
    public void InvalidEvents_ReturnsNull()
    {
        using var scope = new TempScope();
        scope.File("background.jpg", "image");
        string beatmap = scope.Beatmap(
            "[Events]\n0,0,\"background.jpg,0,0\n[TimingPoints]");

        Assert.Null(BeatmapBackgroundResolver.Resolve(beatmap));
    }

    [Fact]
    public void TraversalOutsideBeatmapFolder_IsRejected()
    {
        using var scope = new TempScope(useBeatmapSubdirectory: true);
        scope.RootFile("outside.jpg", "image");
        string beatmap = scope.Beatmap(
            "[Events]\n0,0,\"..\\outside.jpg\",0,0");

        Assert.Null(BeatmapBackgroundResolver.Resolve(beatmap));
    }

    private sealed class TempScope : IDisposable
    {
        internal TempScope(bool useBeatmapSubdirectory = false)
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                $"BeatInsight-Background-{Guid.NewGuid():N}");
            BeatmapDirectory = useBeatmapSubdirectory
                ? Path.Combine(Root, "set")
                : Root;
            Directory.CreateDirectory(BeatmapDirectory);
        }

        private string Root { get; }

        private string BeatmapDirectory { get; }

        internal string Beatmap(string events)
        {
            string path = Path.Combine(BeatmapDirectory, "map.osu");
            System.IO.File.WriteAllText(path, $"osu file format v14\n{events}\n");
            return path;
        }

        internal string File(string name, string content)
        {
            string path = Path.Combine(BeatmapDirectory, name);
            System.IO.File.WriteAllText(path, content);
            return path;
        }

        internal string RootFile(string name, string content)
        {
            string path = Path.Combine(Root, name);
            System.IO.File.WriteAllText(path, content);
            return path;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
