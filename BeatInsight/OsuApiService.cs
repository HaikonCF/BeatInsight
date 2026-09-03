using BeatInsight.Diagnostics;
using BeatInsight.Services.CommunityDiscovery;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace BeatInsight
{
    internal class OsuApiService
    {
        private const string ClientId = "66257";
        private const string TokenEndpoint = "https://osu.ppy.sh/oauth/token";
        private const string TagCatalogEndpoint = "https://osu.ppy.sh/api/v2/tags";

        private readonly HttpClient client;
        private readonly TimeProvider timeProvider;
        private readonly OsuCommunityRequestPolicy communityRequestPolicy;
        private readonly SemaphoreSlim accessTokenGate = new(1, 1);
        private readonly SemaphoreSlim osuTagsGate = new(1, 1);
        private readonly ConcurrentDictionary<int, IReadOnlyList<OsuTagVote>>
            communityTagsByBeatmapId = new();

        private string? accessToken;
        private DateTimeOffset accessTokenRefreshAtUtc = DateTimeOffset.MinValue;
        private List<OsuTag>? osuTags;

        internal OsuApiService(
            HttpClient? client = null,
            TimeProvider? timeProvider = null,
            OsuCommunityRequestPolicy? communityRequestPolicy = null)
        {
            this.client = client ?? new HttpClient();
            this.timeProvider = timeProvider ?? TimeProvider.System;
            this.communityRequestPolicy = communityRequestPolicy
                ?? new OsuCommunityRequestPolicy(this.timeProvider);
        }

        public async Task<string> GetAccessToken(
            CancellationToken cancellationToken = default,
            CommunityDiscoveryRequestDiagnostics? diagnostics = null)
        {
            if (HasReusableAccessToken())
            {
                diagnostics?.RecordCacheHit();
                return accessToken!;
            }

            await accessTokenGate.WaitAsync(cancellationToken);

            try
            {
                if (HasReusableAccessToken())
                {
                    diagnostics?.RecordCacheHit();
                    return accessToken!;
                }

                using HttpResponseMessage response =
                    await SendCommunityRequestAsync(
                        client,
                        CreateOAuthTokenRequest,
                        OsuCommunityRequestKind.OAuthToken,
                        diagnostics,
                        cancellationToken);
                response.EnsureSuccessStatusCode();

                await using Stream content = await response.Content
                    .ReadAsStreamAsync(cancellationToken);
                using JsonDocument document = await JsonDocument.ParseAsync(
                    content,
                    cancellationToken: cancellationToken);

                string? token = document.RootElement
                    .GetProperty("access_token")
                    .GetString();

                if (string.IsNullOrWhiteSpace(token))
                {
                    throw new InvalidOperationException(
                        "osu! OAuth returned an empty access token.");
                }

                accessToken = token;
                accessTokenRefreshAtUtc = CalculateRefreshTime(
                    document.RootElement);

                return accessToken;
            }
            finally
            {
                accessTokenGate.Release();
            }
        }

        public async Task<List<OsuTagVote>> GetBeatmapCommunityTags(
            int beatmapId,
            CancellationToken cancellationToken = default,
            bool throwOnRateLimit = false,
            CommunityDiscoveryRequestDiagnostics? diagnostics = null)
        {
            if (communityTagsByBeatmapId.TryGetValue(
                    beatmapId,
                    out IReadOnlyList<OsuTagVote>? cachedTags))
            {
                diagnostics?.RecordCacheHit();
                return CloneTagVotes(cachedTags);
            }

            var result = new List<OsuTagVote>();

            try
            {
                List<OsuTag> catalog = await GetOsuTags(
                    cancellationToken,
                    diagnostics);

                using HttpResponseMessage response =
                    await SendCommunityRequestAsync(
                        client,
                        () => CreateBeatmapTagsRequest(beatmapId),
                        OsuCommunityRequestKind.BeatmapTags,
                        diagnostics,
                        cancellationToken);
                response.EnsureSuccessStatusCode();

                string html = await response.Content.ReadAsStringAsync(
                    cancellationToken);
                string idMarker = $"\"id\":{beatmapId},";
                int beatmapIndex = html.IndexOf(
                    idMarker,
                    StringComparison.Ordinal);

                if (beatmapIndex < 0)
                {
                    DebugLogger.Log(
                        $"COMMUNITY TAGS | Beatmap {beatmapId} introuvable.");
                    return CacheTagVotes(beatmapId, result);
                }

                int topTagIndex = html.IndexOf(
                    "\"top_tag_ids\":",
                    beatmapIndex,
                    StringComparison.Ordinal);

                if (topTagIndex < 0)
                {
                    DebugLogger.Log(
                        "COMMUNITY TAGS | top_tag_ids introuvable | "
                        + $"Beatmap={beatmapId}");
                    return CacheTagVotes(beatmapId, result);
                }

                int arrayStart = html.IndexOf('[', topTagIndex);

                if (arrayStart < 0)
                {
                    return CacheTagVotes(beatmapId, result);
                }

                int arrayEnd = html.IndexOf(']', arrayStart);

                if (arrayEnd < 0)
                {
                    return CacheTagVotes(beatmapId, result);
                }

                string tagsJson = html.Substring(
                    arrayStart,
                    arrayEnd - arrayStart + 1);
                using JsonDocument document = JsonDocument.Parse(tagsJson);

                foreach (JsonElement element in
                         document.RootElement.EnumerateArray())
                {
                    if (!element.TryGetProperty(
                            "tag_id",
                            out JsonElement tagIdElement)
                        || !element.TryGetProperty(
                            "count",
                            out JsonElement countElement))
                    {
                        continue;
                    }

                    int tagId = tagIdElement.GetInt32();
                    int votes = countElement.GetInt32();
                    OsuTag? tag = catalog.FirstOrDefault(x => x.Id == tagId);

                    if (tag is not null)
                    {
                        result.Add(new OsuTagVote
                        {
                            TagId = tagId,
                            Name = tag.Name,
                            Votes = votes,
                        });
                    }
                }

                DebugLogger.Log(
                    $"COMMUNITY TAGS API OK | Beatmap={beatmapId} | "
                    + $"Tags={result.Count}");

                return CacheTagVotes(beatmapId, result);
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OsuCommunityRateLimitException) when (throwOnRateLimit)
            {
                throw;
            }
            catch (Exception ex)
            {
                DebugLogger.Log(
                    $"COMMUNITY TAGS ERROR | Beatmap={beatmapId} | "
                    + ex.Message);
                DebugLogger.Detailed(ex.ToString());

                return result;
            }
        }

        public class OsuTagVote
        {
            public int TagId { get; set; }

            public string Name { get; set; } = "";

            public int Votes { get; set; }
        }

        public async Task<string> GetBeatmap(int beatmapId)
        {
            string token = await GetAccessToken();

            using HttpRequestMessage request = new(
                HttpMethod.Get,
                $"https://osu.ppy.sh/api/v2/beatmaps/{beatmapId}");
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                token);

            using HttpResponseMessage response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }

        public async Task<List<OsuTag>> GetOsuTags(
            CancellationToken cancellationToken = default,
            CommunityDiscoveryRequestDiagnostics? diagnostics = null)
        {
            if (osuTags is not null)
            {
                diagnostics?.RecordCacheHit();
                return osuTags;
            }

            await osuTagsGate.WaitAsync(cancellationToken);

            try
            {
                if (osuTags is not null)
                {
                    diagnostics?.RecordCacheHit();
                    return osuTags;
                }

                string token = await GetAccessToken(
                    cancellationToken,
                    diagnostics);

                using HttpResponseMessage response =
                    await SendCommunityRequestAsync(
                        client,
                        () => CreateTagCatalogRequest(token),
                        OsuCommunityRequestKind.TagCatalog,
                        diagnostics,
                        cancellationToken);
                response.EnsureSuccessStatusCode();

                await using Stream content = await response.Content
                    .ReadAsStreamAsync(cancellationToken);
                using JsonDocument document = await JsonDocument.ParseAsync(
                    content,
                    cancellationToken: cancellationToken);

                osuTags = document.RootElement
                    .GetProperty("tags")
                    .EnumerateArray()
                    .Select(element => new OsuTag
                    {
                        Id = element.GetProperty("id").GetInt32(),
                        Name = element.GetProperty("name").GetString() ?? "",
                        Description = element.GetProperty("description")
                            .GetString() ?? "",
                        RulesetId = element.TryGetProperty(
                            "ruleset_id",
                            out JsonElement rulesetElement)
                            && rulesetElement.ValueKind != JsonValueKind.Null
                                ? rulesetElement.GetInt32()
                                : null,
                    })
                    .ToList();

                DebugLogger.Log($"OSU TAG CATALOG OK | Tags={osuTags.Count}");

                return osuTags;
            }
            finally
            {
                osuTagsGate.Release();
            }
        }

        internal Task<HttpResponseMessage> SendCommunityRequestAsync(
            HttpClient requestClient,
            Func<HttpRequestMessage> createRequest,
            OsuCommunityRequestKind requestKind,
            CommunityDiscoveryRequestDiagnostics? diagnostics,
            CancellationToken cancellationToken) =>
            communityRequestPolicy.SendAsync(
                requestClient,
                createRequest,
                requestKind,
                diagnostics,
                cancellationToken);

        private bool HasReusableAccessToken()
        {
            return !string.IsNullOrWhiteSpace(accessToken)
                && timeProvider.GetUtcNow() < accessTokenRefreshAtUtc;
        }

        private DateTimeOffset CalculateRefreshTime(JsonElement response)
        {
            if (!response.TryGetProperty(
                    "expires_in",
                    out JsonElement expiresInElement)
                || !expiresInElement.TryGetInt32(out int expiresInSeconds)
                || expiresInSeconds <= 0)
            {
                return timeProvider.GetUtcNow();
            }

            TimeSpan lifetime = TimeSpan.FromSeconds(expiresInSeconds);
            TimeSpan refreshMargin = lifetime <= TimeSpan.FromMinutes(2)
                ? TimeSpan.FromTicks(lifetime.Ticks / 10)
                : TimeSpan.FromMinutes(1);

            return timeProvider.GetUtcNow().Add(lifetime - refreshMargin);
        }

        private static HttpRequestMessage CreateOAuthTokenRequest()
        {
            var values = new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["client_secret"] = OsuSecrets.ClientSecret,
                ["grant_type"] = "client_credentials",
                ["scope"] = "public",
            };

            return new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(values),
            };
        }

        private static HttpRequestMessage CreateBeatmapTagsRequest(int beatmapId)
        {
            var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://osu.ppy.sh/beatmaps/{beatmapId}");
            request.Headers.UserAgent.ParseAdd("BeatInsight/1.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
                "text/html"));

            return request;
        }

        private static HttpRequestMessage CreateTagCatalogRequest(string token)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, TagCatalogEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
                "application/json"));

            return request;
        }

        private List<OsuTagVote> CacheTagVotes(
            int beatmapId,
            IReadOnlyList<OsuTagVote> result)
        {
            IReadOnlyList<OsuTagVote> snapshot = result
                .Select(CloneTagVote)
                .ToArray();
            communityTagsByBeatmapId.TryAdd(beatmapId, snapshot);

            return CloneTagVotes(snapshot);
        }

        private static List<OsuTagVote> CloneTagVotes(
            IReadOnlyList<OsuTagVote> tags) =>
            tags.Select(CloneTagVote).ToList();

        private static OsuTagVote CloneTagVote(OsuTagVote tag) => new()
        {
            TagId = tag.TagId,
            Name = tag.Name,
            Votes = tag.Votes,
        };
    }
}
