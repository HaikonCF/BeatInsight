using BeatInsight.Diagnostics;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;


namespace BeatInsight
{
    internal class OsuApiService
    {
        private readonly HttpClient client = new HttpClient();

        private const string ClientId = "66257";

       

        private string? accessToken;

                public async Task<string> GetAccessToken()
        {
            var values = new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["client_secret"] = OsuSecrets.ClientSecret,
                ["grant_type"] = "client_credentials",
                ["scope"] = "public"
            };

            using var content = new FormUrlEncodedContent(values);

            using HttpResponseMessage response =
                await client.PostAsync(
                    "https://osu.ppy.sh/oauth/token",
                    content);

            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();

            using JsonDocument document =
                JsonDocument.Parse(json);

            

            accessToken = document.RootElement
                .GetProperty("access_token")
                .GetString();

            if (string.IsNullOrWhiteSpace(accessToken))
                throw new Exception("osu! OAuth : access token vide.");

            return accessToken;
        }

        public async Task<List<OsuTagVote>> GetBeatmapCommunityTags(int beatmapId)
        {
            List<OsuTagVote> result = new();

            try
            {
                // ============================================================
                // CATALOGUE DES TAGS
                // ============================================================

                List<OsuTag> catalog =
                    await GetOsuTags();


                // ============================================================
                // PAGE WEB DE LA BEATMAP
                // ============================================================

                string url =
                    $"https://osu.ppy.sh/beatmaps/{beatmapId}";

                using HttpRequestMessage request =
                    new(
                        HttpMethod.Get,
                        url);

                request.Headers.Add(
                    "User-Agent",
                    "BeatInsight/1.0");

                request.Headers.Add(
                    "Accept",
                    "text/html");

                using HttpResponseMessage response =
                    await client.SendAsync(request);

                response.EnsureSuccessStatusCode();

                string html =
                    await response.Content.ReadAsStringAsync();


                // ============================================================
                // RECHERCHE DE LA DIFFICULTÉ
                // ============================================================

                string idMarker =
                    $"\"id\":{beatmapId},";

                int beatmapIndex =
                    html.IndexOf(
                        idMarker,
                        StringComparison.Ordinal);

                if (beatmapIndex < 0)
                {
                    DebugLogger.Log(
                        $"COMMUNITY TAGS | Beatmap {beatmapId} introuvable.");

                    return result;
                }

                DebugLogger.Detailed(
                    $"COMMUNITY TAGS | Beatmap {beatmapId} trouvée à {beatmapIndex}");


                // ============================================================
                // RECHERCHE DE top_tag_ids APRÈS CET ID
                // ============================================================

                int topTagIndex =
                    html.IndexOf(
                        "\"top_tag_ids\":",
                        beatmapIndex,
                        StringComparison.Ordinal);

                if (topTagIndex < 0)
                {
                    DebugLogger.Log(
                        $"COMMUNITY TAGS | top_tag_ids introuvable | Beatmap={beatmapId}");

                    return result;
                }

                DebugLogger.Detailed(
                    $"COMMUNITY TAGS | top_tag_ids trouvé à {topTagIndex}");


                // ============================================================
                // EXTRACTION DU TABLEAU
                // ============================================================

                int arrayStart =
                    html.IndexOf(
                        '[',
                        topTagIndex);

                if (arrayStart < 0)
                    return result;

                int arrayEnd =
                    html.IndexOf(
                        ']',
                        arrayStart);

                if (arrayEnd < 0)
                    return result;

                string tagsJson =
                    html.Substring(
                        arrayStart,
                        arrayEnd - arrayStart + 1);

                using JsonDocument document =
                    JsonDocument.Parse(tagsJson);


                // ============================================================
                // ID → NOM + VOTES
                // ============================================================

                foreach (JsonElement element in
                         document.RootElement.EnumerateArray())
                {
                    if (!element.TryGetProperty(
                            "tag_id",
                            out JsonElement tagIdElement))
                        continue;

                    if (!element.TryGetProperty(
                            "count",
                            out JsonElement countElement))
                        continue;

                    int tagId =
                        tagIdElement.GetInt32();

                    int votes =
                        countElement.GetInt32();

                    DebugLogger.Detailed(
                        $"COMMUNITY TAG DEBUG | " +
                        $"TagId={tagId} | " +
                        $"Votes={votes} | " +
                        $"CatalogMatch={catalog.Any(x => x.Id == tagId)}");

                    OsuTag? tag =
                        catalog.FirstOrDefault(
                            x => x.Id == tagId);

                    if (tag == null)
                        continue;

                    result.Add(
                        new OsuTagVote
                        {
                            TagId = tagId,
                            Name = tag.Name,
                            Votes = votes
                        });
                }


                // ============================================================
                // DEBUG
                // ============================================================

                DebugLogger.Log(
                    $"COMMUNITY TAGS API OK | " +
                    $"Beatmap={beatmapId} | " +
                    $"Tags={result.Count}");

                foreach (OsuTagVote tag in result)
                {
                    DebugLogger.Detailed(
                        $"COMMUNITY TAG | {tag.Name} | Votes={tag.Votes}");
                }

                return result;
            }
            catch (Exception ex)
            {
                DebugLogger.Log(
                    $"COMMUNITY TAGS ERROR | " +
                    $"Beatmap={beatmapId} | " +
                    $"{ex.Message}");

                DebugLogger.Detailed(
                    ex.ToString());

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
            if (string.IsNullOrWhiteSpace(accessToken))
                await GetAccessToken();

            using HttpRequestMessage request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://osu.ppy.sh/api/v2/beatmaps/{beatmapId}");

            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer",
                    accessToken);

            using HttpResponseMessage response =
                await client.SendAsync(request);

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }

        

        private List<OsuTag>? osuTags;
        public async Task<List<OsuTag>> GetOsuTags()
        {
            if (osuTags != null)
                return osuTags;

            string token = await GetAccessToken();

            using HttpRequestMessage request =
                new HttpRequestMessage(
                    HttpMethod.Get,
                    "https://osu.ppy.sh/api/v2/tags");

            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer",
                    token);

            request.Headers.Add(
                "Accept",
                "application/json");

            using HttpResponseMessage response =
                await client.SendAsync(request);

            string json =
                await response.Content.ReadAsStringAsync();

            response.EnsureSuccessStatusCode();

            using JsonDocument document =
                JsonDocument.Parse(json);

            osuTags = new List<OsuTag>();

            foreach (JsonElement element in
                     document.RootElement
                         .GetProperty("tags")
                         .EnumerateArray())
            {
                OsuTag tag = new OsuTag
                {
                    Id = element.GetProperty("id").GetInt32(),

                    Name = element.GetProperty("name").GetString()
                        ?? "",

                    Description =
                        element.GetProperty("description").GetString()
                        ?? ""
                };

                if (element.TryGetProperty(
                        "ruleset_id",
                        out JsonElement rulesetElement) &&
                    rulesetElement.ValueKind != JsonValueKind.Null)
                {
                    tag.RulesetId =
                        rulesetElement.GetInt32();
                }
                osuTags.Add(tag);
            }
            DebugLogger.Log(
                $"OSU TAG CATALOG OK | Tags={osuTags.Count}");

            return osuTags;
        }
    }
}
