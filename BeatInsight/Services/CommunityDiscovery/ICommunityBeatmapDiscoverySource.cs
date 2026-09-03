using BeatInsight.Models.Discovery;

namespace BeatInsight.Services.CommunityDiscovery;

/// <summary>
/// Frontière réseau de la découverte. Les tests injectent une source factice,
/// tandis que l'adaptateur osu!web reste le seul endroit qui connaît la forme
/// de la recherche distante.
/// </summary>
internal interface ICommunityBeatmapDiscoverySource
{
    Task<IReadOnlyList<CommunityBeatmapRemoteCandidate>> FindCandidatesAsync(
        CommunityDiscoveryRequest request,
        CancellationToken cancellationToken);
}
