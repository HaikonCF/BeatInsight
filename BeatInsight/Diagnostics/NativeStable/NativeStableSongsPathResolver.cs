using BeatInsight.Services.Library;
using System.IO;

namespace BeatInsight.Diagnostics.NativeStable;

/// <summary>
/// EXPERIMENTAL — V3 PROOF OF CONCEPT — NOT PRODUCTION INTEGRATION.
/// Reuses the existing manual preference, then inspects stable configuration.
/// </summary>
internal sealed class NativeStableSongsPathResolver
{
    private readonly SongsPathResolver existingResolver;

    internal NativeStableSongsPathResolver()
        : this(new SongsPathResolver())
    {
    }

    internal NativeStableSongsPathResolver(SongsPathResolver existingResolver)
    {
        this.existingResolver = existingResolver;
    }

    internal NativeStableSongsPathResult Resolve(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        string? manual = existingResolver.Resolve(providerSongsPath: null);
        if (manual is not null)
        {
            return new(manual, NativeStableSongsPathSource.ManualPreference);
        }

        string? installation = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(installation) || !Directory.Exists(installation))
        {
            return NativeStableSongsPathResult.Unavailable;
        }

        string? configPath = FindConfigurationFile(installation);
        string? configured = configPath is null
            ? null
            : ReadBeatmapDirectory(configPath);

        if (!string.IsNullOrWhiteSpace(configured))
        {
            try
            {
                string candidate = Path.IsPathFullyQualified(configured)
                    ? configured
                    : Path.Combine(installation, configured);
                string fullCandidate = Path.GetFullPath(candidate);

                if (Directory.Exists(fullCandidate))
                {
                    return new(fullCandidate, NativeStableSongsPathSource.StableConfiguration);
                }
            }
            catch (ArgumentException)
            {
            }
            catch (NotSupportedException)
            {
            }
            catch (PathTooLongException)
            {
            }
        }

        string defaultSongs = Path.Combine(installation, "Songs");
        return Directory.Exists(defaultSongs)
            ? new(Path.GetFullPath(defaultSongs), NativeStableSongsPathSource.InstallationDefault)
            : NativeStableSongsPathResult.Unavailable;
    }

    private static string? FindConfigurationFile(string installation)
    {
        string expected = Path.Combine(
            installation,
            $"osu!.{Environment.UserName}.cfg");
        if (File.Exists(expected))
        {
            return expected;
        }

        try
        {
            string[] candidates = Directory
                .EnumerateFiles(installation, "osu!.*.cfg", SearchOption.TopDirectoryOnly)
                .Where(path => !string.Equals(
                    Path.GetFileName(path),
                    "osu!.cfg",
                    StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToArray();

            return candidates.Length == 1 ? candidates[0] : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ReadBeatmapDirectory(string configPath)
    {
        try
        {
            foreach (string line in File.ReadLines(configPath))
            {
                int separator = line.IndexOf('=');
                if (separator <= 0 ||
                    !line[..separator].Trim().Equals(
                        "BeatmapDirectory",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string value = line[(separator + 1)..].Trim();
                return value.Length == 0 ? null : value;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }
}

internal enum NativeStableSongsPathSource
{
    Unavailable,
    ManualPreference,
    StableConfiguration,
    InstallationDefault
}

internal sealed record NativeStableSongsPathResult(
    string? Path,
    NativeStableSongsPathSource Source)
{
    internal static NativeStableSongsPathResult Unavailable { get; } =
        new(null, NativeStableSongsPathSource.Unavailable);
}
