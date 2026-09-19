using System.IO;

namespace BeatInsight.Services;

internal static class TosuBeatmapPollingGuard
{
    internal static bool ShouldApplyDeferredResult(
        string sourcePath,
        string? currentMapPath,
        bool cancellationRequested) =>
        !cancellationRequested
        && !string.IsNullOrWhiteSpace(sourcePath)
        && string.Equals(
            sourcePath,
            currentMapPath,
            StringComparison.OrdinalIgnoreCase);

    internal static string? ResolveExistingPath(
        string? songsPath,
        string? beatmapFolder,
        string? beatmapFile)
    {
        string? path = ResolvePath(
            songsPath,
            beatmapFolder,
            beatmapFile);

        return path is not null && File.Exists(path) ? path : null;
    }

    internal static string? ResolvePath(
        string? songsPath,
        string? beatmapFolder,
        string? beatmapFile)
    {
        if (string.IsNullOrWhiteSpace(songsPath)
            || string.IsNullOrWhiteSpace(beatmapFolder)
            || string.IsNullOrWhiteSpace(beatmapFile))
        {
            return null;
        }

        try
        {
            return Path.Combine(
                songsPath,
                beatmapFolder,
                beatmapFile);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    internal static async Task<T?> TryLoadAsync<T>(
        string? path,
        Func<string, Task<T>> loader)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(loader);

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return await loader(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }
}
