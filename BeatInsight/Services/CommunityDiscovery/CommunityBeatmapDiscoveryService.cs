using BeatInsight.Models.Discovery;

namespace BeatInsight.Services.CommunityDiscovery;

/// <summary>
/// Applique le filtrage, le dédoublonnage, le ranking et l'enrichissement
/// local aux résultats distants. Cette classe ne fait aucun accès HTTP ni
/// aucune écriture SQLite, ce qui la rend déterministe et testable.
/// </summary>
internal sealed class CommunityBeatmapDiscoveryService
{
    private readonly ICommunityBeatmapDiscoverySource source;
    private readonly ICommunityBeatmapLocalStateSource localStateSource;

    internal CommunityBeatmapDiscoveryService(
        ICommunityBeatmapDiscoverySource source,
        ICommunityBeatmapLocalStateSource localStateSource)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(localStateSource);

        this.source = source;
        this.localStateSource = localStateSource;
    }

    internal async Task<IReadOnlyList<CommunityBeatmapCandidate>>
        FindCandidatesAsync(
            CommunityDiscoveryRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.MaxResults < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.MaxResults));
        }

        if (request.MinStarRating is double minimum
            && request.MaxStarRating is double maximum
            && minimum > maximum)
        {
            throw new ArgumentException(
                "MinStarRating cannot exceed MaxStarRating.",
                nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (request.MaxResults == 0)
        {
            return [];
        }

        IReadOnlyList<CommunityBeatmapRemoteCandidate> remoteCandidates =
            await source.FindCandidatesAsync(request, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        List<NormalizedRemoteCandidate> normalized =
            Deduplicate(remoteCandidates)
                .Where(candidate => candidate.Remote.GameMode == 0)
                .Where(candidate => IsAllowedStatus(candidate.Remote.Status, request))
                .Where(candidate => IsWithinStarRange(
                    candidate.Remote.StarRating,
                    request))
                .Where(candidate => CommunitySamplingTagCatalog.MatchesFamily(
                    candidate.UserTags,
                    request.SamplingFamily))
                .ToList();

        IReadOnlyDictionary<int, CommunityBeatmapLocalState> localStates =
            localStateSource.GetStates(
                normalized.Select(candidate => candidate.Remote.BeatmapId)
                    .ToArray());

        cancellationToken.ThrowIfCancellationRequested();

        return normalized
            .Select(candidate => CreateCandidate(
                candidate,
                request.SamplingFamily,
                localStates.GetValueOrDefault(candidate.Remote.BeatmapId)))
            .Where(candidate => !request.ExcludeAlreadyHumanValidated
                || !candidate.HumanValidated)
            .OrderByDescending(candidate => candidate.EvidenceScore)
            .ThenBy(candidate => candidate.BeatmapId)
            .Take(request.MaxResults)
            .ToArray();
    }

    private static IEnumerable<NormalizedRemoteCandidate> Deduplicate(
        IEnumerable<CommunityBeatmapRemoteCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .Where(candidate => candidate.BeatmapId > 0)
            .GroupBy(candidate => candidate.BeatmapId)
            .Select(group =>
            {
                CommunityBeatmapRemoteCandidate remote = group
                    .OrderBy(candidate => candidate.BeatmapSetId)
                    .ThenBy(candidate => candidate.Artist, StringComparer.Ordinal)
                    .ThenBy(candidate => candidate.Title, StringComparer.Ordinal)
                    .ThenBy(candidate => candidate.DifficultyName, StringComparer.Ordinal)
                    .First();

                IReadOnlyList<CommunityBeatmapUserTag> tags = group
                    .SelectMany(candidate => candidate.UserTags)
                    .Where(tag => !string.IsNullOrWhiteSpace(tag.Name))
                    .GroupBy(tag => tag.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(tagGroup => new CommunityBeatmapUserTag
                    {
                        Name = tagGroup
                            .Select(tag => tag.Name.Trim())
                            .OrderBy(name => name, StringComparer.Ordinal)
                            .First(),
                        Votes = tagGroup.Max(tag => Math.Max(0, tag.Votes)),
                    })
                    .OrderBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new NormalizedRemoteCandidate(remote, tags);
            });
    }

    private static bool IsAllowedStatus(
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

    private static bool IsWithinStarRange(
        double starRating,
        CommunityDiscoveryRequest request)
    {
        return (!request.MinStarRating.HasValue
                    || starRating >= request.MinStarRating.Value)
               && (!request.MaxStarRating.HasValue
                    || starRating <= request.MaxStarRating.Value);
    }

    private static CommunityBeatmapCandidate CreateCandidate(
        NormalizedRemoteCandidate normalized,
        CommunitySamplingFamily family,
        CommunityBeatmapLocalState localState)
    {
        IReadOnlyDictionary<CommunitySamplingFamily, double> evidence =
            CommunitySamplingTagCatalog.CalculateFamilyEvidence(
                normalized.UserTags);

        CommunityBeatmapRemoteCandidate remote = normalized.Remote;

        return new CommunityBeatmapCandidate
        {
            BeatmapId = remote.BeatmapId,
            BeatmapSetId = remote.BeatmapSetId,
            Artist = remote.Artist,
            Title = remote.Title,
            DifficultyName = remote.DifficultyName,
            Mapper = remote.Mapper,
            StarRating = remote.StarRating,
            BPM = remote.BPM,
            Status = remote.Status,
            UserTags = normalized.UserTags,
            SamplingFamily = family,
            EvidenceScore = CommunitySamplingTagCatalog.GetEvidenceScore(
                evidence,
                family),
            AlreadyOwned = localState.AlreadyOwned,
            AlreadyInMlDataset = localState.AlreadyInMlDataset,
            HumanValidated = localState.HumanValidated,
        };
    }

    private sealed record NormalizedRemoteCandidate(
        CommunityBeatmapRemoteCandidate Remote,
        IReadOnlyList<CommunityBeatmapUserTag> UserTags);
}
