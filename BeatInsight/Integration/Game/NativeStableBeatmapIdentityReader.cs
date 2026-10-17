using System.Globalization;
using System.IO;

namespace BeatInsight.Integration.Game;

internal sealed record NativeStableBeatmapIdentity(
    int? BeatmapId,
    int? BeatmapSetId);

internal interface INativeStableBeatmapIdentityReader
{
    NativeStableBeatmapIdentity Read(string beatmapPath);
}

/// <summary>
/// Reads only the [Metadata] identity fields from the already resolved stable
/// .osu file. Gameplay sections and hit objects are never parsed here.
/// </summary>
internal sealed class NativeStableBeatmapIdentityReader
    : INativeStableBeatmapIdentityReader
{
    private const string MetadataSection = "Metadata";
    private const string BeatmapIdPrefix = "BeatmapID:";
    private const string BeatmapSetIdPrefix = "BeatmapSetID:";

    public NativeStableBeatmapIdentity Read(string beatmapPath)
    {
        try
        {
            using var reader = new StreamReader(beatmapPath);
            string? section = null;
            bool visitedMetadata = false;
            int? beatmapId = null;
            int? beatmapSetId = null;
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    if (visitedMetadata)
                        break;

                    section = line.Trim('[', ']');
                    continue;
                }

                if (!string.Equals(section, MetadataSection, StringComparison.Ordinal))
                    continue;

                visitedMetadata = true;
                if (line.StartsWith(BeatmapIdPrefix, StringComparison.Ordinal))
                {
                    beatmapId = ParsePositive(line[BeatmapIdPrefix.Length..]);
                }
                else if (line.StartsWith(BeatmapSetIdPrefix, StringComparison.Ordinal))
                {
                    beatmapSetId = ParsePositive(line[BeatmapSetIdPrefix.Length..]);
                }
            }

            return new(beatmapId, beatmapSetId);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new(null, null);
        }
    }

    private static int? ParsePositive(string value) =>
        int.TryParse(
            value.Trim(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int parsed) && parsed > 0
                ? parsed
                : null;
}
