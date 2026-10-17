using System.Security.Cryptography;
using System.IO;
using System.Security;

namespace BeatInsight.Diagnostics.NativeStable;

/// <summary>
/// EXPERIMENTAL — V3 PROOF OF CONCEPT — NOT PRODUCTION INTEGRATION.
/// </summary>
internal static class NativeStableCurrentMapValidator
{
    internal static NativeStableMapValidationResult Validate(
        int processId,
        string songsPath,
        string beatmapFolder,
        string beatmapFileName,
        string? checksum,
        bool verifyChecksum,
        DateTimeOffset observedAt)
    {
        if (processId <= 0 ||
            string.IsNullOrWhiteSpace(songsPath) ||
            string.IsNullOrWhiteSpace(beatmapFolder) ||
            string.IsNullOrWhiteSpace(beatmapFileName) ||
            !beatmapFileName.EndsWith(".osu", StringComparison.OrdinalIgnoreCase) ||
            Path.IsPathRooted(beatmapFolder) ||
            Path.IsPathRooted(beatmapFileName) ||
            !Directory.Exists(songsPath))
        {
            return NativeStableMapValidationResult.Invalid;
        }

        if (checksum is not null && !IsMd5(checksum))
        {
            return NativeStableMapValidationResult.Invalid;
        }

        try
        {
            string songsRoot = Path.GetFullPath(songsPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string candidate = Path.GetFullPath(
                Path.Combine(songsRoot, beatmapFolder, beatmapFileName));
            string relative = Path.GetRelativePath(songsRoot, candidate);

            if (Path.IsPathRooted(relative) ||
                relative.Equals("..", StringComparison.Ordinal) ||
                relative.StartsWith(
                    ".." + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal) ||
                relative.StartsWith(
                    ".." + Path.AltDirectorySeparatorChar,
                    StringComparison.Ordinal) ||
                !File.Exists(candidate))
            {
                return NativeStableMapValidationResult.Invalid;
            }

            string? normalizedChecksum = checksum?.ToLowerInvariant();
            if (verifyChecksum && normalizedChecksum is not null)
            {
                using FileStream stream = File.OpenRead(candidate);
                string actual = Convert.ToHexString(MD5.HashData(stream)).ToLowerInvariant();
                if (!actual.Equals(normalizedChecksum, StringComparison.Ordinal))
                {
                    return NativeStableMapValidationResult.Invalid;
                }
            }

            return new(new(
                processId,
                normalizedChecksum,
                beatmapFolder,
                beatmapFileName,
                candidate,
                observedAt),
                IsValid: true);
        }
        catch (ArgumentException)
        {
            return NativeStableMapValidationResult.Invalid;
        }
        catch (NotSupportedException)
        {
            return NativeStableMapValidationResult.Invalid;
        }
        catch (IOException)
        {
            return NativeStableMapValidationResult.Invalid;
        }
        catch (UnauthorizedAccessException)
        {
            return NativeStableMapValidationResult.Invalid;
        }
        catch (SecurityException)
        {
            return NativeStableMapValidationResult.Invalid;
        }
    }

    internal static bool IsMd5(string value) =>
        value.Length == 32 && value.All(Uri.IsHexDigit);
}

internal sealed class NativeStableObservationTracker
{
    private int? processId;
    private string? identity;

    internal long Generation { get; private set; }

    internal NativeStableCurrentMapObservation? Current { get; private set; }

    internal void BeginProcess(int newProcessId)
    {
        if (processId == newProcessId)
        {
            return;
        }

        processId = newProcessId;
        identity = null;
        Current = null;
    }

    internal NativeStableObservationChange Apply(
        NativeStableMapValidationResult validation)
    {
        if (!validation.IsValid || validation.Observation is null)
        {
            MarkUnavailable();
            return new(Current, Generation, Changed: false, IsAvailable: false);
        }

        BeginProcess(validation.Observation.ProcessId);
        string nextIdentity = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{validation.Observation.Checksum}|{validation.Observation.AbsoluteOsuPath}");
        bool changed = !string.Equals(identity, nextIdentity, StringComparison.OrdinalIgnoreCase);

        if (changed)
        {
            Generation++;
            identity = nextIdentity;
            Current = validation.Observation;
        }

        return new(Current, Generation, changed, IsAvailable: true);
    }

    internal void MarkUnavailable()
    {
        identity = null;
        Current = null;
    }

    internal void ResetProcess()
    {
        processId = null;
        MarkUnavailable();
    }
}

internal sealed record NativeStableCurrentMapObservation(
    int ProcessId,
    string? Checksum,
    string BeatmapFolder,
    string BeatmapFileName,
    string AbsoluteOsuPath,
    DateTimeOffset ObservedAt);

internal sealed record NativeStableMapValidationResult(
    NativeStableCurrentMapObservation? Observation,
    bool IsValid)
{
    internal static NativeStableMapValidationResult Invalid { get; } =
        new(null, IsValid: false);
}

internal sealed record NativeStableObservationChange(
    NativeStableCurrentMapObservation? Observation,
    long Generation,
    bool Changed,
    bool IsAvailable);
