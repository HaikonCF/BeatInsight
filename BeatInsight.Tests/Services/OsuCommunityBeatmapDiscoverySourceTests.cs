using BeatInsight;
using BeatInsight.Models.Discovery;
using BeatInsight.Services.CommunityDiscovery;
using System.Net;
using System.Net.Http;
using System.Text;

namespace BeatInsight.Tests.Services;

public sealed class OsuCommunityBeatmapDiscoverySourceTests
{
    [Fact]
    public async Task FindCandidatesAsync_BuildsSearchPoolWithoutTagEnrichment()
    {
        var apiHandler = new RoutingHandler(request => request.RequestUri!
            .AbsolutePath == "/oauth/token"
            ? JsonResponse("{\"access_token\":\"token\",\"expires_in\":3600}")
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        var searchHandler = new RoutingHandler(_ => JsonResponse(SearchPage(25)));
        var policy = new OsuCommunityRequestPolicy(
            minimumRequestInterval: TimeSpan.Zero,
            delayAsync: static (_, _) => Task.CompletedTask);
        var api = new OsuApiService(
            new HttpClient(apiHandler),
            TimeProvider.System,
            policy);
        var source = new OsuCommunityBeatmapDiscoverySource(
            api,
            new HttpClient(searchHandler));

        IReadOnlyList<CommunityBeatmapRemoteCandidate> candidates =
            await source.FindCandidatesAsync(new CommunityDiscoveryRequest
            {
                SamplingFamily = CommunitySamplingFamily.Tech,
                MaxResults = 20,
            }, CancellationToken.None);

        Assert.Equal(25, candidates.Count);
        Assert.Equal(1, apiHandler.RequestCount);
        Assert.Equal(1, searchHandler.RequestCount);
        Assert.All(candidates, candidate =>
        {
            Assert.Empty(candidate.UserTags);
            Assert.False(candidate.CommunityDetailsAvailable);
            Assert.Equal(["skillset/tech"], candidate.SearchTagNames);
        });
    }

    private static string SearchPage(int difficultyCount)
    {
        string beatmaps = string.Join(",", Enumerable.Range(1, difficultyCount)
            .Select(id => $$"""{"id":{{id}},"beatmapset_id":9000,"mode_int":0,"version":"Diff","difficulty_rating":5.0,"bpm":180}"""));

        return $$"""
            {
              "beatmapsets": [
                {
                  "id": 9000,
                  "artist": "Artist",
                  "title": "Title",
                  "creator": "Mapper",
                  "status": "ranked",
                  "beatmaps": [{{beatmaps}}]
                }
              ]
            }
            """;
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed class RoutingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) :
        HttpMessageHandler
    {
        internal int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(responseFactory(request));
        }
    }
}
