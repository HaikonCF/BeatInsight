using BeatInsight.Diagnostics.NativeMcOsu;
using System.IO;

namespace BeatInsight.Tests.Diagnostics;

public sealed class NativeMcOsuPocTests
{
    [Fact]
    public void ProcessAbsent_IsNone()
    {
        Assert.Equal(Classification.None, NativeMcOsuValidation.ClassifyCandidateCount(0, truncated: false));
    }

    [Fact]
    public void TruncatedSearchWithoutCandidate_IsUnknown()
    {
        Assert.Equal(Classification.Unknown, NativeMcOsuValidation.ClassifyCandidateCount(0, truncated: true));
    }

    [Fact]
    public void MultipleCandidates_AreAmbiguous()
    {
        Assert.Equal(Classification.Ambiguous, NativeMcOsuValidation.ClassifyCandidateCount(2, truncated: false));
    }

    [Fact]
    public void GenerationMismatch_IsRejected()
    {
        DateTime start = new(2026, 9, 30, 20, 0, 0, DateTimeKind.Utc);
        var expected = new NativeMcOsuProcessGeneration(41, start);

        Assert.False(NativeMcOsuValidation.IsSameGeneration(
            expected,
            new NativeMcOsuProcessGeneration(42, start)));
        Assert.False(NativeMcOsuValidation.IsSameGeneration(
            expected,
            new NativeMcOsuProcessGeneration(41, start.AddSeconds(1))));
    }

    [Fact]
    public void SameGeneration_IsAccepted()
    {
        var generation = new NativeMcOsuProcessGeneration(
            41,
            new DateTime(2026, 9, 30, 20, 0, 0, DateTimeKind.Utc));

        Assert.True(NativeMcOsuValidation.IsSameGeneration(generation, generation));
    }

    [Fact]
    public void ValidOsuPath_ExistsUnderSongsRoot_AndParserAccepts()
    {
        using var fixture = new TemporaryBeatmap();

        NativeMcOsuPathValidation result = NativeMcOsuValidation.ValidatePath(
            fixture.Path,
            fixture.Root,
            path => File.ReadAllText(path).Contains("[HitObjects]", StringComparison.Ordinal));

        Assert.True(result.IsValid);
        Assert.Equal(System.IO.Path.GetFullPath(fixture.Path), result.NormalizedPath);
        Assert.True(NativeMcOsuBeatInsightParserProbe.CanParse(fixture.Path));
    }

    [Fact]
    public void InvalidAndOutsidePaths_AreRejected()
    {
        using var fixture = new TemporaryBeatmap();
        string missing = System.IO.Path.Combine(fixture.Root, "missing.osu");
        string outsideRoot = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(fixture.Root)!,
            $"outside-{Guid.NewGuid():N}.osu");
        File.WriteAllText(outsideRoot, "osu file format v14");
        try
        {
            Assert.False(NativeMcOsuValidation.ValidatePath(missing, fixture.Root).IsValid);
            Assert.False(NativeMcOsuValidation.ValidatePath(outsideRoot, fixture.Root).IsValid);
            Assert.False(NativeMcOsuValidation.ValidatePath(fixture.Path, fixture.Root, _ => false).IsValid);
        }
        finally
        {
            File.Delete(outsideRoot);
        }
    }

    [Fact]
    public void ExactAba_RequiresFirstAndThirdEqual_AndMiddleDifferent()
    {
        string root = System.IO.Path.GetTempPath();
        string a = System.IO.Path.Combine(root, "A.osu");
        string b = System.IO.Path.Combine(root, "B.osu");

        Assert.True(NativeMcOsuValidation.IsExactAba([a, b, a]));
        Assert.False(NativeMcOsuValidation.IsExactAba([a, a, a]));
        Assert.False(NativeMcOsuValidation.IsExactAba([a, b]));
    }

    private sealed class TemporaryBeatmap : IDisposable
    {
        internal TemporaryBeatmap()
        {
            Root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"BeatInsight-NativeMcOsu-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            Path = System.IO.Path.Combine(Root, "fixture.osu");
            File.WriteAllText(
                Path,
                "osu file format v14\n\n[Metadata]\nVersion:Test\nBeatmapID:123\n\n[HitObjects]\n64,64,0,1,0,0:0:0:0:\n");
        }

        internal string Root { get; }
        internal string Path { get; }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
