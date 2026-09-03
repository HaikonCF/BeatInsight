using BeatInsight.Models.Discovery;

namespace BeatInsight.Services.CommunityDiscovery;

/// <summary>
/// Une page normalisée de la recherche osu!web. Les compteurs bruts restent
/// séparés des difficultés exploitables afin de pouvoir diagnostiquer les
/// pertes dues aux filtres sans journaliser le contenu des requêtes OAuth.
/// </summary>
internal sealed class CommunityDiscoveryRemoteSearchPage
{
    internal IReadOnlyList<CommunityBeatmapRemoteCandidate> Candidates { get; init; } = [];

    internal string? NextCursor { get; init; }

    internal int RawBeatmapSetCount { get; init; }

    internal int RawDifficultyCount { get; init; }
}

internal readonly record struct CommunityDiscoveryRemoteSeed(
    CommunityBeatmapRemoteCandidate Candidate,
    IReadOnlyList<string> SearchTagNames);

internal sealed class CommunityDiscoveryRemotePoolDiagnostics
{
    internal int PagesFetched { get; set; }

    internal int RawBeatmapSets { get; set; }

    internal int RawDifficulties { get; set; }

    internal int AfterModeFilter { get; set; }

    internal int AfterStatusFilter { get; set; }

    internal int AfterStarFilter { get; set; }

    internal int AfterTagEvidenceFilter { get; set; }

    internal int AfterDedupe { get; set; }
}

internal sealed class CommunityDiscoveryRemotePool
{
    internal required IReadOnlyList<CommunityDiscoveryRemoteSeed> Seeds { get; init; }

    internal required CommunityDiscoveryRemotePoolDiagnostics Diagnostics { get; init; }
}

/// <summary>
/// Agrège les pages de recherche légères avant la sélection locale. La limite
/// demandée par l'UI reçoit seulement une petite marge pour les exclusions
/// locales ; les détails de tags sont enrichis plus tard et facultativement.
/// </summary>
internal sealed class CommunityDiscoveryRemotePoolCollector
{
    // Une recherche Discovery doit rester une collecte légère. On privilégie
    // le stop précoce après filtres plutôt qu'un balayage exhaustif des tags.
    internal const int MaxPagesPerSearchTag = 3;
    internal const int MaxPagesPerDiscovery = 6;

    internal async Task<CommunityDiscoveryRemotePool> CollectAsync(
        CommunityDiscoveryRequest request,
        IReadOnlyList<string> searchTags,
        int targetCandidateCount,
        Func<string, string?, CancellationToken,
            Task<CommunityDiscoveryRemoteSearchPage>> fetchPageAsync,
        Func<CommunityDiscoveryRemoteSeed, CancellationToken, Task<bool>>?
            hasFamilyEvidenceAsync,
        CancellationToken cancellationToken,
        bool requireEverySearchTag = false)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(searchTags);
        ArgumentNullException.ThrowIfNull(fetchPageAsync);

        if (targetCandidateCount <= 0 || searchTags.Count == 0)
        {
            return new CommunityDiscoveryRemotePool
            {
                Seeds = [],
                Diagnostics = new CommunityDiscoveryRemotePoolDiagnostics(),
            };
        }

        var diagnostics = new CommunityDiscoveryRemotePoolDiagnostics();
        var byBeatmapId = new Dictionary<int, CommunityDiscoveryRemoteSeed>();
        var eligibleBeatmapIds = new List<int>();
        var fetchedSearchTags = new HashSet<string>(StringComparer.Ordinal);
        bool reachedTarget = false;

        foreach (string searchTag in searchTags)
        {
            string? cursor = null;
            var seenCursors = new HashSet<string>(StringComparer.Ordinal);

            for (int pageIndex = 0;
                 pageIndex < MaxPagesPerSearchTag
                    && diagnostics.PagesFetched < MaxPagesPerDiscovery
                    && !reachedTarget;
                 pageIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                CommunityDiscoveryRemoteSearchPage page =
                    await fetchPageAsync(searchTag, cursor, cancellationToken);

                fetchedSearchTags.Add(searchTag);

                diagnostics.PagesFetched++;
                diagnostics.RawBeatmapSets += page.RawBeatmapSetCount;
                diagnostics.RawDifficulties += page.RawDifficultyCount;

                foreach (CommunityBeatmapRemoteCandidate candidate in page.Candidates)
                {
                    if (candidate.GameMode != 0)
                    {
                        continue;
                    }

                    diagnostics.AfterModeFilter++;

                    if (!CommunityDiscoveryCandidateFilters.IsAllowedStatus(
                            candidate.Status,
                            request))
                    {
                        continue;
                    }

                    diagnostics.AfterStatusFilter++;

                    if (!CommunityDiscoveryCandidateFilters.IsWithinStarRange(
                            candidate.StarRating,
                            request))
                    {
                        continue;
                    }

                    diagnostics.AfterStarFilter++;

                    if (candidate.BeatmapId <= 0)
                    {
                        continue;
                    }

                    if (byBeatmapId.TryGetValue(
                            candidate.BeatmapId,
                            out CommunityDiscoveryRemoteSeed existing))
                    {
                        byBeatmapId[candidate.BeatmapId] = existing with
                        {
                            SearchTagNames = existing.SearchTagNames
                                .Append(searchTag)
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
                                .ToArray(),
                        };
                        continue;
                    }

                    var seed = new CommunityDiscoveryRemoteSeed(
                        candidate,
                        [searchTag]);
                    byBeatmapId.Add(candidate.BeatmapId, seed);

                    diagnostics.AfterDedupe++;

                    bool hasFamilyEvidence = hasFamilyEvidenceAsync is null
                        || await hasFamilyEvidenceAsync(
                            seed,
                            cancellationToken);

                    if (!hasFamilyEvidence)
                    {
                        continue;
                    }

                    diagnostics.AfterTagEvidenceFilter++;
                    eligibleBeatmapIds.Add(seed.Candidate.BeatmapId);

                    if (eligibleBeatmapIds.Count >= targetCandidateCount)
                    {
                        reachedTarget = !requireEverySearchTag
                            || fetchedSearchTags.Count >= searchTags.Count;
                        break;
                    }
                }

                bool hasEnoughCandidates = eligibleBeatmapIds.Count
                    >= targetCandidateCount;
                if (reachedTarget
                    || hasEnoughCandidates
                    || string.IsNullOrWhiteSpace(page.NextCursor)
                    || !seenCursors.Add(page.NextCursor))
                {
                    break;
                }

                cursor = page.NextCursor;
            }

            if (reachedTarget)
            {
                break;
            }

            if (diagnostics.PagesFetched >= MaxPagesPerDiscovery)
            {
                break;
            }
        }

        return new CommunityDiscoveryRemotePool
        {
            Seeds = eligibleBeatmapIds
                .Select(beatmapId => byBeatmapId[beatmapId])
                .OrderBy(seed => seed.Candidate.BeatmapId)
                .ToArray(),
            Diagnostics = diagnostics,
        };
    }
}
