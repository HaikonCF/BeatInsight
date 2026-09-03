using BeatInsight.Services.Download;
using System.IO;

namespace BeatInsight.Tests.Services.Download;

/// <summary>
/// Aucun test ne touche le réseau : IBeatmapDownloadProvider est
/// entièrement simulé, exactement comme demandé par le ticket V2.4.3.
/// </summary>
public sealed class BeatmapDownloadServiceTests : IDisposable
{
    private readonly string directory;

    public BeatmapDownloadServiceTests()
    {
        directory = Path.Combine(
            Path.GetTempPath(),
            "beatinsight-download-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private sealed class FakeProvider : IBeatmapDownloadProvider
    {
        private readonly Func<Stream, CancellationToken, Task<BeatmapDownloadProviderResult>>
            behavior;

        internal FakeProvider(
            Func<Stream, CancellationToken, Task<BeatmapDownloadProviderResult>> behavior)
        {
            this.behavior = behavior;
        }

        public string ProviderName => "Fake";

        public Task<BeatmapDownloadProviderResult> DownloadAsync(
            int beatmapSetId,
            Stream destination,
            CancellationToken cancellationToken) =>
            behavior(destination, cancellationToken);
    }

    private static readonly byte[] ValidOszBytes =
        [0x50, 0x4B, 0x03, 0x04, 0x01, 0x02];

    [Fact]
    public async Task ValidOsz_DownloadsToPartThenAtomicRename()
    {
        var provider = new FakeProvider((stream, ct) =>
        {
            stream.Write(ValidOszBytes);
            return Task.FromResult(
                BeatmapDownloadProviderResult.BytesWrittenResult(ValidOszBytes.Length));
        });
        var service = new BeatmapDownloadService(provider, directory);

        BeatmapDownloadResult result = await service.DownloadAsync(123);

        Assert.Equal(BeatmapDownloadOutcome.Success, result.Outcome);
        Assert.Equal(
            Path.Combine(directory, "123.osz"),
            result.LocalOszFilePath);
        Assert.True(File.Exists(result.LocalOszFilePath));
        Assert.False(File.Exists(Path.Combine(directory, "123.osz.part")));
    }

    [Fact]
    public async Task Cancellation_DeletesPartialFile()
    {
        var provider = new FakeProvider((stream, ct) =>
        {
            stream.Write([0x50, 0x4B]);
            stream.Flush();
            throw new OperationCanceledException(ct);
        });
        var service = new BeatmapDownloadService(provider, directory);

        using CancellationTokenSource cts = new();
        cts.Cancel();

        BeatmapDownloadResult result = await service.DownloadAsync(456, cts.Token);

        Assert.Equal(BeatmapDownloadOutcome.Cancelled, result.Outcome);
        Assert.False(File.Exists(Path.Combine(directory, "456.osz.part")));
        Assert.False(File.Exists(Path.Combine(directory, "456.osz")));
    }

    [Fact]
    public async Task HtmlResponse_IsRejected()
    {
        byte[] html = "<!DOCTYPE html><html></html>"u8.ToArray();
        var provider = new FakeProvider((stream, ct) =>
        {
            stream.Write(html);
            return Task.FromResult(
                BeatmapDownloadProviderResult.BytesWrittenResult(html.Length));
        });
        var service = new BeatmapDownloadService(provider, directory);

        BeatmapDownloadResult result = await service.DownloadAsync(789);

        Assert.Equal(BeatmapDownloadOutcome.InvalidDownloadedFile, result.Outcome);
        Assert.False(File.Exists(Path.Combine(directory, "789.osz")));
        Assert.False(File.Exists(Path.Combine(directory, "789.osz.part")));
    }

    [Fact]
    public async Task EmptyFile_IsRejected()
    {
        var provider = new FakeProvider((stream, ct) =>
            Task.FromResult(BeatmapDownloadProviderResult.BytesWrittenResult(0)));
        var service = new BeatmapDownloadService(provider, directory);

        BeatmapDownloadResult result = await service.DownloadAsync(321);

        Assert.Equal(BeatmapDownloadOutcome.InvalidDownloadedFile, result.Outcome);
    }

    [Fact]
    public async Task RateLimited_MapsToTypedFailure()
    {
        var provider = new FakeProvider((stream, ct) =>
            Task.FromResult(
                BeatmapDownloadProviderResult.Failure(
                    BeatmapDownloadProviderOutcome.RateLimited,
                    "429")));
        var service = new BeatmapDownloadService(provider, directory);

        BeatmapDownloadResult result = await service.DownloadAsync(1);

        Assert.Equal(BeatmapDownloadOutcome.RateLimited, result.Outcome);
        Assert.False(File.Exists(Path.Combine(directory, "1.osz.part")));
    }

    [Fact]
    public async Task AuthenticationRequired_MapsCleanly()
    {
        var provider = new FakeProvider((stream, ct) =>
            Task.FromResult(
                BeatmapDownloadProviderResult.Failure(
                    BeatmapDownloadProviderOutcome.AuthenticationRequired)));
        var service = new BeatmapDownloadService(provider, directory);

        BeatmapDownloadResult result = await service.DownloadAsync(2);

        Assert.Equal(BeatmapDownloadOutcome.AuthenticationRequired, result.Outcome);
    }

    [Fact]
    public async Task BrowserFallback_NeverLeavesAPartialFile()
    {
        var provider = new FakeProvider((stream, ct) =>
            Task.FromResult(BeatmapDownloadProviderResult.BrowserFallback));
        var service = new BeatmapDownloadService(provider, directory);

        BeatmapDownloadResult result = await service.DownloadAsync(999);

        Assert.Equal(BeatmapDownloadOutcome.BrowserFallbackOpened, result.Outcome);
        Assert.Null(result.LocalOszFilePath);
        Assert.False(File.Exists(Path.Combine(directory, "999.osz.part")));
        Assert.False(File.Exists(Path.Combine(directory, "999.osz")));
    }

    [Fact]
    public async Task InvalidBeatmapSetId_FailsWithoutTouchingDisk()
    {
        var provider = new FakeProvider((stream, ct) =>
            throw new InvalidOperationException("Provider should not run."));
        var service = new BeatmapDownloadService(provider, directory);

        BeatmapDownloadResult result = await service.DownloadAsync(0);

        Assert.Equal(BeatmapDownloadOutcome.Failed, result.Outcome);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void HasNoDependencyOnHumanLabelStorage()
    {
        System.Reflection.ConstructorInfo ctor =
            typeof(BeatmapDownloadService).GetConstructors(
                System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)
                .Single();

        Assert.DoesNotContain(
            ctor.GetParameters(),
            parameter => parameter.ParameterType.Name.Contains(
                "MlDatasetSampleRepository",
                StringComparison.Ordinal));
    }
}
