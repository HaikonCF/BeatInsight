using BeatInsight.Models.Discovery;
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
    ICommunityBeatmapDiscoverySource
{
    private const string SearchEndpoint =
        "https://osu.ppy.sh/beatmapsets/search";

    private readonly OsuApiService osuApiService;
    private readonly HttpClient httpClient;

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

        // Une seule acquisition de token par découverte. OsuApiService reste
        // l'unique détenteur de la logique OAuth et des secrets associés.
        string accessToken = await osuApiService.GetAccessToken();
        int sourceLimit = Math.Max(request.MaxResults, request.MaxResults * 4);
        var byBeatmapId = new Dictionary<int, RemoteSeed>();

        foreach (string tagName in CommunitySamplingTagCatalog.GetSearchTags(
                     request.SamplingFamily))
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<RemoteSeed> results = await SearchByTagAsync(
                tagName,
                accessToken,
                cancellationToken);

            foreach (RemoteSeed result in results)
            {
                if (!byBeatmapId.ContainsKey(result.Candidate.BeatmapId))
                {
                    byBeatmapId.Add(result.Candidate.BeatmapId, result);
                }

                if (byBeatmapId.Count >= sourceLimit)
                {
                    break;
                }
            }

            if (byBeatmapId.Count >= sourceLimit)
            {
                break;
            }
        }

        var candidates = new List<CommunityBeatmapRemoteCandidate>();

        foreach (RemoteSeed seed in byBeatmapId.Values
                     .OrderBy(seed => seed.Candidate.BeatmapId))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Les votes proviennent de la lecture communautaire existante.
            // Si osu!web ne renvoie temporairement aucune preuve détaillée,
            // le tag de recherche est conservé avec zéro vote : il permet au
            // service de reconnaître la famille sans inventer un décompte.
            IReadOnlyList<CommunityBeatmapUserTag> userTags =
                (await osuApiService.GetBeatmapCommunityTags(
                    seed.Candidate.BeatmapId))
                .Select(tag => new CommunityBeatmapUserTag
                {
                    Name = tag.Name,
                    Votes = tag.Votes,
                })
                .ToArray();

            if (userTags.Count == 0)
            {
                userTags =
                [
                    new CommunityBeatmapUserTag
                    {
                        Name = seed.SearchTag,
                        Votes = 0,
                    },
                ];
            }

            candidates.Add(new CommunityBeatmapRemoteCandidate
            {
                BeatmapId = seed.Candidate.BeatmapId,
                BeatmapSetId = seed.Candidate.BeatmapSetId,
                Artist = seed.Candidate.Artist,
                Title = seed.Candidate.Title,
                DifficultyName = seed.Candidate.DifficultyName,
                Mapper = seed.Candidate.Mapper,
                StarRating = seed.Candidate.StarRating,
                BPM = seed.Candidate.BPM,
                Status = seed.Candidate.Status,
                GameMode = seed.Candidate.GameMode,
                UserTags = userTags,
            });
        }

        return candidates;
    }

    private async Task<IReadOnlyList<RemoteSeed>> SearchByTagAsync(
        string tagName,
        string accessToken,
        CancellationToken cancellationToken)
    {
        string query = $"tag=\"{tagName}\"";
        string url = $"{SearchEndpoint}?m=0&q={Uri.EscapeDataString(query)}";

        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
            "application/json"));
        request.Headers.UserAgent.ParseAdd("BeatInsight/2.4");

        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            cancellationToken);
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
            return [];
        }

        var results = new List<RemoteSeed>();

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
                int beatmapId = Number(beatmap, "id");
                int gameMode = Number(beatmap, "mode_int", defaultValue: -1);

                if (gameMode != 0 || beatmapId <= 0 || beatmapSetId <= 0)
                {
                    continue;
                }

                results.Add(new RemoteSeed(
                    new CommunityBeatmapRemoteCandidate
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
                    },
                    tagName));
            }
        }

        return results;
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

    private sealed record RemoteSeed(
        CommunityBeatmapRemoteCandidate Candidate,
        string SearchTag);
}
