using BeatInsight.Services.Download;
using System.IO;

namespace BeatInsight.Tests.Services.Download;

public sealed class BrowserOpenBeatmapDownloadProviderTests
{
    [Fact]
    public async Task DownloadAsync_OpensBeatmapsetDownloadUrl_WithoutWritingBytes()
    {
        string? openedUrl = null;
        var provider = new BrowserOpenBeatmapDownloadProvider(url => openedUrl = url);

        using MemoryStream destination = new();

        BeatmapDownloadProviderResult result = await provider.DownloadAsync(
            123,
            destination,
            CancellationToken.None);

        Assert.Equal(
            BeatmapDownloadProviderOutcome.BrowserFallbackOpened,
            result.Outcome);
        Assert.Equal(
            "https://osu.ppy.sh/beatmapsets/123/download",
            openedUrl);
        Assert.Equal(0, destination.Length);
    }

    [Fact]
    public async Task DownloadAsync_InvalidBeatmapSetId_DoesNotOpenBrowser()
    {
        bool opened = false;
        var provider = new BrowserOpenBeatmapDownloadProvider(_ => opened = true);

        using MemoryStream destination = new();

        BeatmapDownloadProviderResult result = await provider.DownloadAsync(
            0,
            destination,
            CancellationToken.None);

        Assert.Equal(BeatmapDownloadProviderOutcome.Failed, result.Outcome);
        Assert.False(opened);
    }

    [Fact]
    public async Task DownloadAsync_OpenUrlThrows_MapsToProviderUnavailable()
    {
        var provider = new BrowserOpenBeatmapDownloadProvider(
            _ => throw new InvalidOperationException("no browser"));

        using MemoryStream destination = new();

        BeatmapDownloadProviderResult result = await provider.DownloadAsync(
            123,
            destination,
            CancellationToken.None);

        Assert.Equal(
            BeatmapDownloadProviderOutcome.ProviderUnavailable,
            result.Outcome);
    }
}
