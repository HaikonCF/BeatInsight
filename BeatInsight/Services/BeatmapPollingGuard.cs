using System.IO;

namespace BeatInsight.Services;

internal static class BeatmapPollingGuard
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
