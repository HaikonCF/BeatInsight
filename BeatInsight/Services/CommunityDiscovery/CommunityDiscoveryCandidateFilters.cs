using BeatInsight.Models.Discovery;

namespace BeatInsight.Services.CommunityDiscovery;

/// <summary>
/// Filtres distants partagés par la collecte paginée et le service de
/// découverte. Les garder ici évite que les deux étapes divergent sur les
/// règles osu!standard, statut ou plage de stars.
/// </summary>
internal static class CommunityDiscoveryCandidateFilters
{
    internal static bool IsAllowedStatus(
        string status,
        CommunityDiscoveryRequest request)
    {
        return status.Trim().ToLowerInvariant() switch
        {
            "ranked" => request.IncludeRanked,
            "approved" => request.IncludeApproved,
            "loved" => request.IncludeLoved,
            _ => false,
        };
    }

    internal static bool IsWithinStarRange(
        double starRating,
        CommunityDiscoveryRequest request)
    {
        return (!request.MinStarRating.HasValue
                    || starRating >= request.MinStarRating.Value)
               && (!request.MaxStarRating.HasValue
                    || starRating <= request.MaxStarRating.Value);
    }
}
