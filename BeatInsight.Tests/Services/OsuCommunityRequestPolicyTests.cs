using BeatInsight;
using BeatInsight.Services.CommunityDiscovery;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace BeatInsight.Tests.Services;

public sealed class OsuCommunityRequestPolicyTests
{
    [Fact]
    public async Task GetAccessToken_ReusesTokenBeforeRefreshTime()
    {
        var handler = new SequenceHandler(
        [
            JsonResponse("{\"access_token\":\"first\",\"expires_in\":3600}"),
        ]);
        var time = new TestTimeProvider(DateTimeOffset.UnixEpoch);
        var service = CreateApiService(handler, time);

        string first = await service.GetAccessToken();
        string second = await service.GetAccessToken();

        Assert.Equal("first", first);
        Assert.Equal("first", second);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetAccessToken_ConcurrentCallsUseOneRemoteRequest()
    {
        var handler = new SequenceHandler(
        [
            JsonResponse("{\"access_token\":\"shared\",\"expires_in\":3600}"),
        ]);
        var service = CreateApiService(
            handler,
            new TestTimeProvider(DateTimeOffset.UnixEpoch));

        string[] tokens = await Task.WhenAll(
            Enumerable.Range(0, 5)
                .Select(_ => service.GetAccessToken()));

        Assert.All(tokens, token => Assert.Equal("shared", token));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetAccessToken_RefreshesAfterExpiryMargin()
    {
        var handler = new SequenceHandler(
        [
            JsonResponse("{\"access_token\":\"first\",\"expires_in\":3600}"),
            JsonResponse("{\"access_token\":\"second\",\"expires_in\":3600}"),
        ]);
        var time = new TestTimeProvider(DateTimeOffset.UnixEpoch);
        var service = CreateApiService(handler, time);

        _ = await service.GetAccessToken();
        time.Advance(TimeSpan.FromMinutes(59));
        string refreshed = await service.GetAccessToken();

        Assert.Equal("second", refreshed);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task SendAsync_RespectsRetryAfter()
    {
        var handler = new SequenceHandler(
        [
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Headers = { RetryAfter = new RetryConditionHeaderValue(
                    TimeSpan.FromSeconds(7)) },
            },
            new HttpResponseMessage(HttpStatusCode.OK),
        ]);
        var delays = new List<TimeSpan>();
        var diagnostics = new CommunityDiscoveryRequestDiagnostics();
        var policy = CreatePolicy(delays);

        using HttpResponseMessage response = await policy.SendAsync(
            new HttpClient(handler),
            CreateGetRequest,
            OsuCommunityRequestKind.DiscoverySearch,
            diagnostics,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([TimeSpan.FromSeconds(7)], delays);
        Assert.Equal(2, handler.RequestCount);
        Assert.Equal(1, diagnostics.RateLimitCount);
        Assert.Equal(1, diagnostics.Retries);
    }

    [Fact]
    public async Task SendAsync_UsesBoundedExponentialBackoffWithoutRetryAfter()
    {
        var handler = new SequenceHandler(
        [
            new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            new HttpResponseMessage(HttpStatusCode.TooManyRequests),
        ]);
        var delays = new List<TimeSpan>();
        var policy = CreatePolicy(delays);

        await Assert.ThrowsAsync<OsuCommunityRateLimitException>(() =>
            policy.SendAsync(
                new HttpClient(handler),
                CreateGetRequest,
                OsuCommunityRequestKind.BeatmapTags,
                diagnostics: null,
                CancellationToken.None));

        Assert.Equal(
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(4)],
            delays);
        Assert.Equal(4, handler.RequestCount);
    }

    [Fact]
    public async Task SendAsync_CancellationInterruptsBackoff()
    {
        var handler = new SequenceHandler(
            [new HttpResponseMessage(HttpStatusCode.TooManyRequests)]);
        var backoffStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var policy = new OsuCommunityRequestPolicy(
            minimumRequestInterval: TimeSpan.Zero,
            delayAsync: async (_, cancellationToken) =>
            {
                backoffStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            });
        using var cancellation = new CancellationTokenSource();

        Task<HttpResponseMessage> send = policy.SendAsync(
            new HttpClient(handler),
            CreateGetRequest,
            OsuCommunityRequestKind.DiscoverySearch,
            diagnostics: null,
            cancellation.Token);

        await backoffStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
    }

    [Fact]
    public async Task SendAsync_DoesNotRetryNonRateLimitClientErrors()
    {
        var handler = new SequenceHandler(
            [new HttpResponseMessage(HttpStatusCode.BadRequest)]);
        var delays = new List<TimeSpan>();
        var policy = CreatePolicy(delays);

        using HttpResponseMessage response = await policy.SendAsync(
            new HttpClient(handler),
            CreateGetRequest,
            OsuCommunityRequestKind.DiscoverySearch,
            diagnostics: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, handler.RequestCount);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task SendAsync_DiscoveryRequestEventuallySucceedsAfter429()
    {
        var handler = new SequenceHandler(
        [
            new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            new HttpResponseMessage(HttpStatusCode.OK),
        ]);
        var diagnostics = new CommunityDiscoveryRequestDiagnostics();
        var policy = CreatePolicy([]);

        using HttpResponseMessage response = await policy.SendAsync(
            new HttpClient(handler),
            CreateGetRequest,
            OsuCommunityRequestKind.DiscoverySearch,
            diagnostics,
            CancellationToken.None);

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(2, diagnostics.HttpRequestsTotal);
        Assert.Equal(1, diagnostics.RateLimitCount);
    }

    [Fact]
    public async Task GetBeatmapCommunityTags_UsesBeatmapIdCache()
    {
        var handler = new RoutingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/oauth/token" => JsonResponse(
                "{\"access_token\":\"token\",\"expires_in\":3600}"),
            "/api/v2/tags" => JsonResponse(
                "{\"tags\":[{\"id\":1,\"name\":\"skillset/tech\",\"description\":\"\",\"ruleset_id\":0}]}"),
            "/beatmaps/42" => HtmlResponse(
                "{\"id\":42,\"top_tag_ids\":[{\"tag_id\":1,\"count\":3}]}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var diagnostics = new CommunityDiscoveryRequestDiagnostics();
        var service = CreateApiService(handler, TimeProvider.System);

        List<OsuApiService.OsuTagVote> first =
            await service.GetBeatmapCommunityTags(42, diagnostics: diagnostics);
        List<OsuApiService.OsuTagVote> second =
            await service.GetBeatmapCommunityTags(42, diagnostics: diagnostics);

        Assert.Single(first);
        Assert.Single(second);
        Assert.Equal(1, handler.RequestsTo("/beatmaps/42"));
        Assert.Equal(1, diagnostics.CacheHits);
    }

    private static OsuApiService CreateApiService(
        HttpMessageHandler handler,
        TimeProvider timeProvider)
    {
        return new OsuApiService(
            new HttpClient(handler),
            timeProvider,
            new OsuCommunityRequestPolicy(
                timeProvider,
                TimeSpan.Zero,
                static (_, _) => Task.CompletedTask));
    }

    private static OsuCommunityRequestPolicy CreatePolicy(
        List<TimeSpan> delays) =>
        new(
            minimumRequestInterval: TimeSpan.Zero,
            delayAsync: (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

    private static HttpRequestMessage CreateGetRequest() =>
        new(HttpMethod.Get, "https://osu.ppy.sh/test");

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage HtmlResponse(string html) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(html, Encoding.UTF8, "text/html"),
        };

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        internal void Advance(TimeSpan elapsed) => current += elapsed;
    }

    private sealed class SequenceHandler(
        IReadOnlyList<HttpResponseMessage> responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> remaining = new(responses);

        internal int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(remaining.Dequeue());
        }
    }

    private sealed class RoutingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) :
        HttpMessageHandler
    {
        private readonly List<string> requestPaths = [];

        internal int RequestsTo(string path) =>
            requestPaths.Count(requestPath => requestPath == path);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            requestPaths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(responseFactory(request));
        }
    }
}
