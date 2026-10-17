using System.IO;

namespace BeatInsight.Diagnostics.NativeMcOsu;

internal sealed record NativeMcOsuProcessGeneration(int ProcessId, DateTime ProcessStartUtc);

internal sealed record NativeMcOsuPathValidation(
    bool IsValid,
    string? NormalizedPath,
    string? Reason);

internal static class NativeMcOsuValidation
{
    internal static Classification ClassifyCandidateCount(int count, bool truncated) => count switch
    {
        < 0 => throw new ArgumentOutOfRangeException(nameof(count)),
        0 when truncated => Classification.Unknown,
        0 => Classification.None,
        1 => Classification.Unique,
        _ => Classification.Ambiguous
    };

    internal static bool IsSameGeneration(
        NativeMcOsuProcessGeneration expected,
        NativeMcOsuProcessGeneration observed) =>
        expected.ProcessId == observed.ProcessId &&
        expected.ProcessStartUtc == observed.ProcessStartUtc;

    internal static NativeMcOsuPathValidation ValidatePath(
        string? path,
        string? songsRoot,
        Func<string, bool>? parserProbe = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new(false, null, "empty path");
        }

        string normalized;
        try
        {
            normalized = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new(false, null, "invalid path syntax");
        }

        if (!string.Equals(Path.GetExtension(normalized), ".osu", StringComparison.OrdinalIgnoreCase))
        {
            return new(false, normalized, "not an .osu file");
        }

        if (!File.Exists(normalized))
        {
            return new(false, normalized, "file does not exist");
        }

        if (!string.IsNullOrWhiteSpace(songsRoot))
        {
            string root = Path.GetFullPath(songsRoot);
            string relative = Path.GetRelativePath(root, normalized);
            if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                return new(false, normalized, "path is outside the configured Songs root");
            }
        }

        if (parserProbe is not null)
        {
            try
            {
                if (!parserProbe(normalized))
                {
                    return new(false, normalized, "parser rejected the file");
                }
            }
            catch
            {
                return new(false, normalized, "parser threw while reading the file");
            }
        }

        return new(true, normalized, null);
    }

    internal static bool IsExactAba(IReadOnlyList<string> paths)
    {
        if (paths.Count != 3)
        {
            return false;
        }

        string a = Path.GetFullPath(paths[0]);
        string b = Path.GetFullPath(paths[1]);
        string a2 = Path.GetFullPath(paths[2]);
        return string.Equals(a, a2, StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
