using BeatInsight.Models.Discovery;
using BeatInsight.Diagnostics;

namespace BeatInsight.Services.CommunityDiscovery;

/// <summary>
/// Applique le filtrage, le dédoublonnage, le ranking et l'enrichissement
/// local aux résultats distants. L'enrichissement communautaire détaillé est
/// facultatif et ne cible que les résultats déjà retenus pour l'affichage.
/// </summary>
internal sealed class CommunityBeatmapDiscoveryService
{
    // Les résultats restent valides sur la seule requête tag osu!web. Les
    // détails avec votes sont donc limités aux premières cartes affichées,
    // plutôt que de rendre une recherche /20 dépendante de 20 appels HTML.
    private const int MaxEagerCommunityDetails = 8;

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
                .Where(candidate => CommunityDiscoveryCandidateFilters
                    .IsAllowedStatus(candidate.Remote.Status, request))
                .Where(candidate => CommunityDiscoveryCandidateFilters
                    .IsWithinStarRange(
                    candidate.Remote.StarRating,
                    request))
                .Where(candidate => HasRequestedFamilyEvidence(
                    candidate,
                    request.SamplingFamily))
                .ToList();

        IReadOnlyDictionary<int, CommunityBeatmapLocalState> localStates =
            localStateSource.GetStates(
                normalized.Select(candidate => candidate.Remote.BeatmapId)
                    .ToArray());

        cancellationToken.ThrowIfCancellationRequested();

        NormalizedRemoteCandidate[] selected = normalized
            .Where(candidate => !request.ExcludeAlreadyHumanValidated
                || !localStates.GetValueOrDefault(
                    candidate.Remote.BeatmapId).HumanValidated)
            .OrderByDescending(candidate => GetCommunityEvidenceScore(
                candidate,
                request.SamplingFamily))
            .ThenByDescending(candidate => CommunitySamplingTagCatalog
                .CountSearchTagMatches(
                    candidate.SearchTagNames,
                    request.SamplingFamily))
            .ThenBy(candidate => candidate.Remote.BeatmapId)
            .Take(request.MaxResults)
            .ToArray();

        NormalizedRemoteCandidate[] enriched =
            await EnrichSelectedCandidatesAsync(selected, cancellationToken);

        CommunityBeatmapCandidate[] finalCandidates = enriched
            .Select(candidate => CreateCandidate(
                candidate,
                request.SamplingFamily,
                localStates.GetValueOrDefault(candidate.Remote.BeatmapId)))
            .ToArray();

        DebugLogger.Log(
            $"COMMUNITY DISCOVERY DEPTH | Final returned = {finalCandidates.Length}");

        return finalCandidates;
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

                IReadOnlyList<string> searchTagNames = group
                    .SelectMany(candidate => candidate.SearchTagNames)
                    .Where(tagName => !string.IsNullOrWhiteSpace(tagName))
                    .Select(tagName => tagName.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(tagName => tagName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new NormalizedRemoteCandidate(
                    remote,
                    tags,
                    searchTagNames,
                    group.Any(candidate => candidate.CommunityDetailsAvailable));
            });
    }

    private async Task<NormalizedRemoteCandidate[]>
        EnrichSelectedCandidatesAsync(
            IReadOnlyList<NormalizedRemoteCandidate> selected,
            CancellationToken cancellationToken)
    {
        if (source is not ICommunityBeatmapCandidateMetadataEnricher enricher)
        {
            return selected.ToArray();
        }

        var enriched = selected.ToArray();

        int eagerDetailCount = Math.Min(
            enriched.Length,
            MaxEagerCommunityDetails);
        DebugLogger.Log(
            "COMMUNITY DISCOVERY ENRICHMENT PLAN | "
            + $"Selected = {enriched.Length} | "
            + $"Eager details = {eagerDetailCount}");

        for (int index = 0; index < eagerDetailCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                CommunityCandidateMetadataEnrichmentResult result =
                    await enricher.EnrichCandidateAsync(
                        enriched[index].Remote,
                        cancellationToken);
                enriched[index] = MergeEnrichment(enriched[index], result.Candidate);

                if (result.RateLimited)
                {
                    // Les candidats restants restent affichables grâce à leur
                    // provenance de recherche ; inutile de prolonger la
                    // pression sur osu! après un 429 d'enrichissement.
                    break;
                }
            }
            catch (OsuCommunityRateLimitException)
            {
                DebugLogger.Log(
                    "COMMUNITY DISCOVERY ENRICHMENT | "
                    + "Rate limited; remaining details left unavailable.");
                break;
            }
        }

        return enriched;
    }

    private static NormalizedRemoteCandidate MergeEnrichment(
        NormalizedRemoteCandidate original,
        CommunityBeatmapRemoteCandidate enriched) => new(
            enriched,
            MergeTags(original.UserTags, enriched.UserTags),
            original.SearchTagNames,
            original.CommunityDetailsAvailable
                || enriched.CommunityDetailsAvailable);

    private static IReadOnlyList<CommunityBeatmapUserTag> MergeTags(
        IReadOnlyList<CommunityBeatmapUserTag> first,
        IReadOnlyList<CommunityBeatmapUserTag> second) => first
            .Concat(second)
            .Where(tag => !string.IsNullOrWhiteSpace(tag.Name))
            .GroupBy(tag => tag.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => new CommunityBeatmapUserTag
            {
                Name = group.Key,
                Votes = group.Max(tag => Math.Max(0, tag.Votes)),
            })
            .OrderBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool HasRequestedFamilyEvidence(
        NormalizedRemoteCandidate candidate,
        CommunitySamplingFamily family) =>
            CommunitySamplingTagCatalog.MatchesFamily(candidate.UserTags, family)
            || CommunitySamplingTagCatalog.SearchTagsMatchFamily(
                candidate.SearchTagNames,
                family);

    private static double GetCommunityEvidenceScore(
        NormalizedRemoteCandidate candidate,
        CommunitySamplingFamily family) => CommunitySamplingTagCatalog
            .GetEvidenceScore(
                CommunitySamplingTagCatalog.CalculateFamilyEvidence(
                    candidate.UserTags),
                family);

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
            CommunityDetailsAvailable = normalized.CommunityDetailsAvailable,
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
        IReadOnlyList<CommunityBeatmapUserTag> UserTags,
        IReadOnlyList<string> SearchTagNames,
        bool CommunityDetailsAvailable);
}
