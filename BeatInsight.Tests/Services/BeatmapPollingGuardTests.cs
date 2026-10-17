using BeatInsight.Services;
using System.IO;

namespace BeatInsight.Tests.Services;

public sealed class BeatmapPollingGuardTests
{
    [Fact]
    public async Task DeferredResults_OnlyLatestMapCanWin()
    {
        const string mapA = @"D:\osu!\Songs\A\a.osu";
        const string mapB = @"D:\osu!\Songs\B\b.osu";
        const string mapC = @"D:\osu!\Songs\C\c.osu";
        var applied = new List<string>();

        async Task CompleteAsync(
            string sourcePath,
            bool cancelled,
            int delayMilliseconds)
        {
            await Task.Delay(delayMilliseconds);

            if (BeatmapPollingGuard.ShouldApplyDeferredResult(
                    sourcePath,
                    mapC,
                    cancelled))
            {
                applied.Add(sourcePath);
            }
        }

        await Task.WhenAll(
            CompleteAsync(mapA, cancelled: true, delayMilliseconds: 30),
            CompleteAsync(mapB, cancelled: true, delayMilliseconds: 20),
            CompleteAsync(mapC, cancelled: false, delayMilliseconds: 10));

        Assert.Equal([mapC], applied);
    }

    [Fact]
    public async Task NullOrMissingPath_DoesNotInvokeLoader()
    {
        int loadCount = 0;

        string? nullResult = await BeatmapPollingGuard.TryLoadAsync<string>(
            null,
            _ =>
            {
                loadCount++;
                return Task.FromResult("loaded");
            });

        string? missingResult = await BeatmapPollingGuard.TryLoadAsync(
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.osu"),
            _ =>
            {
                loadCount++;
                return Task.FromResult("loaded");
            });

        Assert.Null(nullResult);
        Assert.Null(missingResult);
        Assert.Equal(0, loadCount);
    }

    [Fact]
    public async Task FileDisappearingDuringLoad_DoesNotEscape()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"BeatInsight-PollingGuard-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "map.osu");

        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, "osu file format v14");

        try
        {
            string? result = await BeatmapPollingGuard.TryLoadAsync<string>(
                path,
                candidate =>
                {
                    File.Delete(candidate);
                    return Task.FromResult(File.ReadAllText(candidate));
                });

            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DirectoryMissingDuringLoad_DoesNotEscape()
    {
        string path = Path.GetTempFileName();

        try
        {
            string? result = await BeatmapPollingGuard.TryLoadAsync<string>(
                path,
                _ => throw new DirectoryNotFoundException("Songs folder disappeared."));

            Assert.Null(result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ValidPathAfterInvalidPath_LoadsAndCanBecomeCurrentPath()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"BeatInsight-PollingGuard-{Guid.NewGuid():N}");
        string validPath = Path.Combine(directory, "map.osu");
        string invalidPath = Path.Combine(directory, "stale.osu");
        string? currentMapPath = null;

        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(validPath, "valid beatmap");

        try
        {
            string? missing = await BeatmapPollingGuard.TryLoadAsync<string>(
                invalidPath,
                candidate => File.ReadAllTextAsync(candidate));

            if (missing is not null)
            {
                currentMapPath = invalidPath;
            }

            Assert.Null(currentMapPath);

            string? loaded = await BeatmapPollingGuard.TryLoadAsync<string>(
                validPath,
                candidate => File.ReadAllTextAsync(candidate));

            if (loaded is not null)
            {
                currentMapPath = validPath;
            }

            Assert.Equal("valid beatmap", loaded);
            Assert.Equal(validPath, currentMapPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task UnexpectedLoaderError_IsNotSwallowed()
    {
        string path = Path.GetTempFileName();

        try
        {
            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => BeatmapPollingGuard.TryLoadAsync<string>(
                    path,
                    _ => throw new InvalidOperationException("Unexpected failure.")));

            Assert.Equal("Unexpected failure.", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
