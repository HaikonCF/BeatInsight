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

/// <summary>
/// Extension facultative d'une source de découverte pour charger les détails
/// communautaires uniquement après la sélection locale des candidats affichés.
/// L'enrichissement n'est jamais une condition de succès de la recherche.
/// </summary>
internal interface ICommunityBeatmapCandidateMetadataEnricher
{
    Task<CommunityCandidateMetadataEnrichmentResult> EnrichCandidateAsync(
        CommunityBeatmapRemoteCandidate candidate,
        CancellationToken cancellationToken);
}

internal readonly record struct CommunityCandidateMetadataEnrichmentResult(
    CommunityBeatmapRemoteCandidate Candidate,
    bool RateLimited);
