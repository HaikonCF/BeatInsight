using System.IO;

namespace BeatInsight.Integration.Game;

internal static class GameStateBeatmapIdentity
{
    internal static bool AreSame(
        GameStateSnapshot? current,
        GameStateSnapshot candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (current is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(current.CurrentBeatmapPath) &&
            !string.IsNullOrWhiteSpace(candidate.CurrentBeatmapPath))
        {
            return string.Equals(
                NormalizePath(current.CurrentBeatmapPath),
                NormalizePath(candidate.CurrentBeatmapPath),
                StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(current.CurrentBeatmapId) &&
            !string.IsNullOrWhiteSpace(candidate.CurrentBeatmapId))
        {
            return string.Equals(
                current.CurrentBeatmapId,
                candidate.CurrentBeatmapId,
                StringComparison.Ordinal);
        }

        // Last-resort identity for providers that cannot expose a path or id.
        // It deliberately excludes LastUpdateUtc, which changes every refresh.
        return string.Equals(
            Fallback(current),
            Fallback(candidate),
            StringComparison.Ordinal);
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path.Trim()
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static string Fallback(GameStateSnapshot snapshot) => string.Join(
        '\u001f',
        snapshot.ProviderName,
        snapshot.ClientName ?? string.Empty,
        snapshot.ProcessIdentity ?? string.Empty);
}
