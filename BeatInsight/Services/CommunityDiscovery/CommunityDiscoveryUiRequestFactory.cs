using BeatInsight.Models.Discovery;
using System.Globalization;

namespace BeatInsight.Services.CommunityDiscovery;

/// <summary>
/// Traduit les contrôles compacts de l'UI ML Lab en requête de découverte.
/// Cette classe ne filtre ni ne classe aucun candidat : ces responsabilités
/// restent dans <see cref="CommunityBeatmapDiscoveryService"/>.
/// </summary>
internal static class CommunityDiscoveryUiRequestFactory
{
    internal static bool TryCreate(
        CommunitySamplingFamily samplingFamily,
        int maxResults,
        string? minStarText,
        string? maxStarText,
        out CommunityDiscoveryRequest? request,
        out string error)
    {
        request = null;
        error = string.Empty;

        if (!TryParseOptionalStarRating(minStarText, out double? minimum)
            || !TryParseOptionalStarRating(maxStarText, out double? maximum))
        {
            error = "Enter a valid star rating.";
            return false;
        }

        if (minimum is double min && maximum is double max && min > max)
        {
            error = "Min ★ cannot exceed Max ★.";
            return false;
        }

        request = new CommunityDiscoveryRequest
        {
            SamplingFamily = samplingFamily,
            MaxResults = maxResults,
            MinStarRating = minimum,
            MaxStarRating = maximum,
        };

        return true;
    }

    private static bool TryParseOptionalStarRating(
        string? value,
        out double? result)
    {
        result = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        const NumberStyles style = NumberStyles.Float;

        if (!double.TryParse(
                value,
                style,
                CultureInfo.CurrentCulture,
                out double parsed)
            && !double.TryParse(
                value,
                style,
                CultureInfo.InvariantCulture,
                out parsed))
        {
            return false;
        }

        if (!double.IsFinite(parsed) || parsed < 0.0)
        {
            return false;
        }

        result = parsed;
        return true;
    }
}
