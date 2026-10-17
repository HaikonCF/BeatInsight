using BeatInsight.Integration.Game;
using System.IO;

namespace BeatInsight.Tests.Integration.Game;

public sealed class LazerBackgroundResolverTests : IDisposable
{
    private const int SetId = 1049899;
    private readonly string root;
    private readonly string beatmapPath;

    public LazerBackgroundResolverTests()
    {
        root = Path.Combine(Path.GetTempPath(), $"BeatInsight-LazerBackground-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        beatmapPath = Path.Combine(root, "beatmap");
        File.WriteAllText(beatmapPath,
            "osu file format v14\n[Events]\n0,0,\"images\\BG3.jpg\",0,0\n");
    }

    [Fact]
    public void ExactNamedFileMatch_ResolvesExistingContentBlob()
    {
        string hash = Hash('A');
        string blob = FileAt("background-blob", "image");
        var resolver = Resolver(
            [new("images/BG3.jpg", hash)],
            new Dictionary<string, string?> { [hash] = blob });

        string? result = resolver.Resolve(beatmapPath, SetId);

        Assert.Equal(blob, result);
    }

    [Fact]
    public void SameBasenameInDifferentFolder_DoesNotMatch()
    {
        string hash = Hash('A');
        string blob = FileAt("background-blob", "image");
        var resolver = Resolver(
            [new("other/BG3.jpg", hash)],
            new Dictionary<string, string?> { [hash] = blob });

        Assert.Null(resolver.Resolve(beatmapPath, SetId));
    }

    [Fact]
    public void NoMatchingFile_ReturnsNull()
    {
        var resolver = Resolver([new("video.mp4", Hash('A'))], new Dictionary<string, string?>());

        Assert.Null(resolver.Resolve(beatmapPath, SetId));
    }

    [Fact]
    public void AmbiguousNormalizedMatches_ReturnNull()
    {
        var resolver = Resolver(
            [new("images/BG3.jpg", Hash('A')), new("images\\bg3.JPG", Hash('B'))],
            new Dictionary<string, string?>());

        Assert.Null(resolver.Resolve(beatmapPath, SetId));
    }

    [Fact]
    public void InvalidHash_ReturnsNull()
    {
        var resolver = Resolver(
            [new("images/BG3.jpg", "not-a-hash")],
            new Dictionary<string, string?>());

        Assert.Null(resolver.Resolve(beatmapPath, SetId));
    }

    [Fact]
    public void MissingBlob_ReturnsNull()
    {
        string hash = Hash('A');
        var resolver = Resolver(
            [new("images/BG3.jpg", hash)],
            new Dictionary<string, string?> { [hash] = Path.Combine(root, "missing") });

        Assert.Null(resolver.Resolve(beatmapPath, SetId));
    }

    [Fact]
    public void MissingBackgroundEvent_ReturnsNullWithoutCatalogLookup()
    {
        File.WriteAllText(beatmapPath, "osu file format v14\n[Events]\n// none\n");
        var catalog = new FakeCatalog([new("images/BG3.jpg", Hash('A'))]);
        var resolver = new LazerBackgroundResolver(
            catalog,
            new FakeContentStore(new Dictionary<string, string?>()));

        string? result = resolver.Resolve(beatmapPath, SetId);

        Assert.Null(result);
        Assert.Equal(0, catalog.ReadCount);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private LazerBackgroundResolver Resolver(
        IReadOnlyList<LazerNamedFile> files,
        IReadOnlyDictionary<string, string?> blobs) =>
        new(new FakeCatalog(files), new FakeContentStore(blobs));

    private string FileAt(string name, string contents)
    {
        string path = Path.Combine(root, name);
        File.WriteAllText(path, contents);
        return path;
    }

    private static string Hash(char value) => new(value, 64);

    private sealed class FakeCatalog(IReadOnlyList<LazerNamedFile> files) : ILazerBeatmapSetFileCatalog
    {
        private int readCount;
        internal int ReadCount => Volatile.Read(ref readCount);

        public IReadOnlyList<LazerNamedFile> ReadFiles(int beatmapSetOnlineId)
        {
            Assert.Equal(SetId, beatmapSetOnlineId);
            Interlocked.Increment(ref readCount);
            return files;
        }
    }

    private sealed class FakeContentStore(IReadOnlyDictionary<string, string?> blobs) : ILazerContentStore
    {
        public string? ResolveExistingBlob(string hash) =>
            blobs.TryGetValue(hash, out string? path) ? path : null;
    }
}
