using BeatInsight.Models.Discovery;
using BeatInsight.Diagnostics;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace BeatInsight.Services.CommunityDiscovery;

/// <summary>
/// Adaptateur osu!web isolé derrière ICommunityBeatmapDiscoverySource.
///
/// L'API v2 expose le catalogue des tags, mais la recherche <c>tag="..."</c>
/// est documentée pour le beatmap listing osu!web/lazer plutôt que comme un
/// endpoint de recherche API v2 dédié. Cet adaptateur est donc le seul point
/// qui dépend de cette forme de réponse web. L'OAuth existant reste fourni par
/// OsuApiService ; aucun secret ni token n'est journalisé ici.
/// </summary>
internal sealed class OsuCommunityBeatmapDiscoverySource :
    ICommunityBeatmapDiscoverySource,
    ICommunityBeatmapCandidateMetadataEnricher
{
    private const string SearchEndpoint =
        "https://osu.ppy.sh/beatmapsets/search";

    // Marge légère contre les exclusions locales. Elle remplace l'ancien
    // pool 3xN qui déclenchait l'enrichissement de trop nombreux candidats.
    private const int MinimumCandidateSlack = 5;
    private const int MaximumCandidateSlack = 10;

    private readonly OsuApiService osuApiService;
    private readonly HttpClient httpClient;
    private readonly ConcurrentDictionary<string, CommunityDiscoveryRemoteSearchPage>
        searchPagesByQuery = new(StringComparer.Ordinal);

    internal OsuCommunityBeatmapDiscoverySource(
        OsuApiService osuApiService,
        HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(osuApiService);

        this.osuApiService = osuApiService;
        this.httpClient = httpClient ?? new HttpClient();
    }

    public async Task<IReadOnlyList<CommunityBeatmapRemoteCandidate>>
        FindCandidatesAsync(
            CommunityDiscoveryRequest request,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.MaxResults <= 0)
        {
            return [];
        }

        cancellationToken.ThrowIfCancellationRequested();

        var requestDiagnostics = new CommunityDiscoveryRequestDiagnostics();
        Stopwatch stopwatch = Stopwatch.StartNew();
        int finalCandidateCount = 0;

        try
        {
            // OsuApiService reste l'unique détenteur de la logique OAuth et
            // des secrets associés. Son cache réutilise le token encore sûr.
            string accessToken = await osuApiService.GetAccessToken(
                cancellationToken,
                requestDiagnostics);
            int remotePoolTarget = GetRemotePoolTarget(request.MaxResults);
            var collector = new CommunityDiscoveryRemotePoolCollector();
            var pageNumbersByTag = new Dictionary<string, int>(
                StringComparer.Ordinal);

            CommunityDiscoveryRemotePool remotePool =
                await collector.CollectAsync(
                    request,
                    CommunitySamplingTagCatalog.GetSearchTags(
                        request.SamplingFamily),
                    remotePoolTarget,
                    (tagName, cursor, token) =>
                    {
                        int pageNumber = pageNumbersByTag.TryGetValue(
                            tagName,
                            out int currentPageNumber)
                            ? currentPageNumber + 1
                            : 1;
                        pageNumbersByTag[tagName] = pageNumber;
                        requestDiagnostics.RecordSearchPageFetched();
                        return SearchByTagPageAsync(
                            tagName,
                            cursor,
                            pageNumber,
                            accessToken,
                            requestDiagnostics,
                            token);
                    },
                    hasFamilyEvidenceAsync: null,
                    cancellationToken,
                    requireEverySearchTag: request.SamplingFamily
                        == CommunitySamplingFamily.Hybrid);

            CommunityBeatmapRemoteCandidate[] candidates = remotePool.Seeds
                .Select(seed => CopyCandidate(
                    seed.Candidate,
                    searchTagNames: seed.SearchTagNames))
                .ToArray();
            finalCandidateCount = candidates.Length;

            CommunityDiscoveryRemotePoolDiagnostics diagnostics =
                remotePool.Diagnostics;
            DebugLogger.Log(
                "COMMUNITY DISCOVERY DEPTH | "
                + $"Pages fetched = {diagnostics.PagesFetched} | "
                + $"Raw beatmapsets = {diagnostics.RawBeatmapSets} | "
                + $"Raw difficulties = {diagnostics.RawDifficulties} | "
                + $"After mode filter = {diagnostics.AfterModeFilter} | "
                + $"After status filter = {diagnostics.AfterStatusFilter} | "
                + $"After star filter = {diagnostics.AfterStarFilter} | "
                + $"After dedupe = {diagnostics.AfterDedupe} | "
                + $"After search-tag filter = {diagnostics.AfterTagEvidenceFilter}");

            return candidates;
        }
        finally
        {
            stopwatch.Stop();
            DebugLogger.Log(
                "COMMUNITY DISCOVERY REQUESTS | "
                + $"Family = {request.SamplingFamily} | "
                + $"HTTP requests total = {requestDiagnostics.HttpRequestsTotal} | "
                + $"OAuth requests = {requestDiagnostics.OAuthRequests} | "
                + $"Search pages fetched = {requestDiagnostics.SearchPagesFetched} | "
                + $"Search requests = {requestDiagnostics.SearchRequests} | "
                + $"Tag requests = {requestDiagnostics.TagRequests} | "
                + $"Cache hits = {requestDiagnostics.CacheHits} | "
                + $"429 count = {requestDiagnostics.RateLimitCount} | "
                + $"Retries = {requestDiagnostics.Retries} | "
                + $"Final candidates = {finalCandidateCount} | "
                + $"Elapsed = {stopwatch.Elapsed.TotalSeconds:F1}s");
        }
    }

    public async Task<CommunityCandidateMetadataEnrichmentResult>
        EnrichCandidateAsync(
        CommunityBeatmapRemoteCandidate candidate,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var diagnostics = new CommunityDiscoveryRequestDiagnostics();
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            IReadOnlyList<CommunityBeatmapUserTag> userTags =
                (await osuApiService.GetBeatmapCommunityTags(
                    candidate.BeatmapId,
                    cancellationToken,
                    throwOnRateLimit: true,
                    diagnostics: diagnostics))
                .Select(tag => new CommunityBeatmapUserTag
                {
                    Name = tag.Name,
                    Votes = tag.Votes,
                })
                .ToArray();

            return new CommunityCandidateMetadataEnrichmentResult(
                CopyCandidate(
                    candidate,
                    userTags: userTags,
                    communityDetailsAvailable: true),
                RateLimited: false);
        }
        catch (OsuCommunityRateLimitException)
        {
            DebugLogger.Log(
                "COMMUNITY DISCOVERY ENRICHMENT RATE LIMIT | "
                + $"BeatmapId = {candidate.BeatmapId} | "
                + "HTTP status = 429 | Details left unavailable");
            return new CommunityCandidateMetadataEnrichmentResult(
                candidate,
                RateLimited: true);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            DebugLogger.Log(
                "COMMUNITY DISCOVERY ENRICHMENT ERROR | "
                + $"BeatmapId = {candidate.BeatmapId} | "
                + $"{ex.GetType().Name} | {ex.Message}");
            DebugLogger.Detailed(ex.ToString());
            return new CommunityCandidateMetadataEnrichmentResult(
                candidate,
                RateLimited: false);
        }
        finally
        {
            stopwatch.Stop();
            DebugLogger.Log(
                "COMMUNITY DISCOVERY ENRICHMENT | "
                + $"BeatmapId = {candidate.BeatmapId} | "
                + $"HTTP requests = {diagnostics.HttpRequestsTotal} | "
                + $"Tag requests = {diagnostics.TagRequests} | "
                + $"Cache hits = {diagnostics.CacheHits} | "
                + $"429 count = {diagnostics.RateLimitCount} | "
                + $"Retries = {diagnostics.Retries} | "
                + $"Elapsed = {stopwatch.Elapsed.TotalSeconds:F1}s");
        }
    }

    private static int GetRemotePoolTarget(int requestedCount)
    {
        int slack = Math.Clamp(
            (int)Math.Ceiling(requestedCount * 0.25),
            MinimumCandidateSlack,
            MaximumCandidateSlack);

        return requestedCount + slack;
    }

    private static CommunityBeatmapRemoteCandidate CopyCandidate(
        CommunityBeatmapRemoteCandidate source,
        IReadOnlyList<CommunityBeatmapUserTag>? userTags = null,
        IReadOnlyList<string>? searchTagNames = null,
        bool? communityDetailsAvailable = null) => new()
        {
            BeatmapId = source.BeatmapId,
            BeatmapSetId = source.BeatmapSetId,
            Artist = source.Artist,
            Title = source.Title,
            DifficultyName = source.DifficultyName,
            Mapper = source.Mapper,
            StarRating = source.StarRating,
            BPM = source.BPM,
            Status = source.Status,
            GameMode = source.GameMode,
            UserTags = userTags ?? source.UserTags,
            SearchTagNames = searchTagNames ?? source.SearchTagNames,
            CommunityDetailsAvailable = communityDetailsAvailable
                ?? source.CommunityDetailsAvailable,
        };

    private async Task<CommunityDiscoveryRemoteSearchPage> SearchByTagPageAsync(
        string tagName,
        string? cursor,
        int pageNumber,
        string accessToken,
        CommunityDiscoveryRequestDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        string cacheKey = $"{tagName}\n{cursor ?? ""}";

        if (searchPagesByQuery.TryGetValue(cacheKey,
                out CommunityDiscoveryRemoteSearchPage? cachedPage))
        {
            diagnostics.RecordCacheHit();
            return cachedPage;
        }

        string query = $"tag=\"{tagName}\"";
        string url = $"{SearchEndpoint}?m=0&q={Uri.EscapeDataString(query)}";

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            url += $"&cursor_string={Uri.EscapeDataString(cursor)}";
        }

        HttpResponseMessage response;

        try
        {
            response = await osuApiService.SendCommunityRequestAsync(
                httpClient,
                () => CreateSearchRequest(url, accessToken),
                OsuCommunityRequestKind.DiscoverySearch,
                diagnostics,
                cancellationToken);
        }
        catch (OsuCommunityRateLimitException)
        {
            DebugLogger.Log(
                "COMMUNITY DISCOVERY RATE LIMIT | "
                + "Stage = search page | "
                + $"Tag = {tagName} | "
                + $"Page = {pageNumber} | "
                + $"Cursor present = {!string.IsNullOrWhiteSpace(cursor)} | "
                + "HTTP status = 429");
            throw;
        }

        using (response)
        {
        response.EnsureSuccessStatusCode();

        await using Stream content = await response.Content.ReadAsStreamAsync(
            cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(
            content,
            cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty(
                "beatmapsets",
                out JsonElement beatmapSets)
            || beatmapSets.ValueKind != JsonValueKind.Array)
        {
            CommunityDiscoveryRemoteSearchPage emptyPage = new()
            {
                NextCursor = NextCursor(document.RootElement),
                RawBeatmapSetCount = 0,
                RawDifficultyCount = 0,
            };

            return searchPagesByQuery.GetOrAdd(cacheKey, emptyPage);
        }

        var results = new List<CommunityBeatmapRemoteCandidate>();
        int rawDifficulties = 0;

        foreach (JsonElement beatmapSet in beatmapSets.EnumerateArray())
        {
            int beatmapSetId = Number(beatmapSet, "id");
            string artist = Text(beatmapSet, "artist");
            string title = Text(beatmapSet, "title");
            string mapper = Text(beatmapSet, "creator");
            string status = Text(beatmapSet, "status");

            if (!beatmapSet.TryGetProperty("beatmaps", out JsonElement beatmaps)
                || beatmaps.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement beatmap in beatmaps.EnumerateArray())
            {
                rawDifficulties++;

                int beatmapId = Number(beatmap, "id");
                int gameMode = Number(beatmap, "mode_int", defaultValue: -1);

                if (beatmapId <= 0 || beatmapSetId <= 0)
                {
                    continue;
                }

                results.Add(new CommunityBeatmapRemoteCandidate
                {
                    BeatmapId = beatmapId,
                    BeatmapSetId = Number(
                        beatmap,
                        "beatmapset_id",
                        beatmapSetId),
                    Artist = artist,
                    Title = title,
                    DifficultyName = Text(beatmap, "version"),
                    Mapper = mapper,
                    StarRating = Number(beatmap, "difficulty_rating", 0.0),
                    BPM = NullableNumber(beatmap, "bpm"),
                    Status = status,
                    GameMode = gameMode,
                });
            }
        }

        var page = new CommunityDiscoveryRemoteSearchPage
        {
            Candidates = results,
            NextCursor = NextCursor(document.RootElement),
            RawBeatmapSetCount = beatmapSets.GetArrayLength(),
            RawDifficultyCount = rawDifficulties,
        };

        return searchPagesByQuery.GetOrAdd(cacheKey, page);
        }
    }

    private static HttpRequestMessage CreateSearchRequest(
        string url,
        string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
            "application/json"));
        request.Headers.UserAgent.ParseAdd("BeatInsight/2.4");

        return request;
    }

    private static string? NextCursor(JsonElement root)
    {
        return root.TryGetProperty("cursor_string", out JsonElement cursor)
            && cursor.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(cursor.GetString())
                ? cursor.GetString()
                : null;
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? ""
            : "";

    private static int Number(
        JsonElement element,
        string name,
        int defaultValue = 0) =>
        element.TryGetProperty(name, out JsonElement property)
        && property.TryGetInt32(out int value)
            ? value
            : defaultValue;

    private static double Number(
        JsonElement element,
        string name,
        double defaultValue) =>
        element.TryGetProperty(name, out JsonElement property)
        && property.TryGetDouble(out double value)
            ? value
            : defaultValue;

    private static double? NullableNumber(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement property)
        && property.TryGetDouble(out double value)
            ? value
            : null;
}
