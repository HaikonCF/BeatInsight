using System.IO;
using System.Text;

namespace BeatInsight.Integration.Game;

internal static class BeatmapBackgroundResolver
{
    private static readonly HashSet<string> SupportedExtensions = new(
        [".jpg", ".jpeg", ".png"],
        StringComparer.OrdinalIgnoreCase);

    internal static string? Resolve(string? beatmapPath)
    {
        string? filename = ExtractBackgroundFileName(beatmapPath);
        if (filename is null || string.IsNullOrWhiteSpace(beatmapPath))
        {
            return null;
        }

        try
        {
            string fullBeatmapPath = Path.GetFullPath(beatmapPath);
            string? beatmapDirectory = Path.GetDirectoryName(fullBeatmapPath);
            if (string.IsNullOrWhiteSpace(beatmapDirectory))
            {
                return null;
            }

            return ResolveCandidate(beatmapDirectory, filename);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    internal static string? ExtractBackgroundFileName(string? beatmapPath)
    {
        if (string.IsNullOrWhiteSpace(beatmapPath))
        {
            return null;
        }

        try
        {
            string fullBeatmapPath = Path.GetFullPath(beatmapPath);
            if (!File.Exists(fullBeatmapPath))
            {
                return null;
            }

            bool inEvents = false;
            foreach (string rawLine in File.ReadLines(fullBeatmapPath))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal) &&
                    line.EndsWith("]", StringComparison.Ordinal))
                {
                    if (inEvents)
                    {
                        return null;
                    }

                    inEvents = string.Equals(
                        line,
                        "[Events]",
                        StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inEvents || string.IsNullOrWhiteSpace(line) ||
                    line.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!TryReadBackgroundFilename(line, out string filename))
                {
                    continue;
                }

                return filename;
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
        }

        return null;
    }

    private static bool TryReadBackgroundFilename(
        string line,
        out string filename)
    {
        filename = string.Empty;
        if (!TryReadFirstFields(line, 3, out string[] fields) ||
            !string.Equals(fields[0], "0", StringComparison.Ordinal) ||
            !string.Equals(fields[1], "0", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(fields[2]))
        {
            return false;
        }

        filename = fields[2];
        return true;
    }

    private static bool TryReadFirstFields(
        string line,
        int count,
        out string[] fields)
    {
        var values = new List<string>(count);
        var value = new StringBuilder();
        bool quoted = false;

        for (int index = 0; index < line.Length; index++)
        {
            char character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    value.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }

                continue;
            }

            if (character == ',' && !quoted)
            {
                values.Add(value.ToString().Trim());
                value.Clear();
                if (values.Count == count)
                {
                    fields = values.ToArray();
                    return true;
                }

                continue;
            }

            value.Append(character);
        }

        if (quoted)
        {
            fields = [];
            return false;
        }

        values.Add(value.ToString().Trim());
        fields = values.ToArray();
        return fields.Length >= count;
    }

    private static string? ResolveCandidate(
        string beatmapDirectory,
        string filename)
    {
        string extension = Path.GetExtension(filename);
        if (!SupportedExtensions.Contains(extension))
        {
            return null;
        }

        string directory = Path.GetFullPath(beatmapDirectory);
        string candidate = Path.GetFullPath(Path.Combine(directory, filename));
        string directoryPrefix = Path.EndsInDirectorySeparator(directory)
            ? directory
            : directory + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return File.Exists(candidate) ? candidate : null;
    }
}
