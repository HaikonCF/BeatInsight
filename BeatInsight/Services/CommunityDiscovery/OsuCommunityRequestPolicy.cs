using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace BeatInsight.Services.CommunityDiscovery;

/// <summary>
/// Catégories comptées uniquement pour le diagnostic d'une découverte.
/// Aucune information d'authentification ou URL authentifiée n'est stockée.
/// </summary>
internal enum OsuCommunityRequestKind
{
    OAuthToken,
    DiscoverySearch,
    TagCatalog,
    BeatmapTags,
}

internal sealed class CommunityDiscoveryRequestDiagnostics
{
    internal int HttpRequestsTotal { get; private set; }

    internal int OAuthRequests { get; private set; }

    internal int SearchRequests { get; private set; }

    internal int SearchPagesFetched { get; private set; }

    internal int TagRequests { get; private set; }

    internal int CacheHits { get; private set; }

    internal int RateLimitCount { get; private set; }

    internal int Retries { get; private set; }

    internal void RecordHttpRequest(OsuCommunityRequestKind kind)
    {
        HttpRequestsTotal++;

        switch (kind)
        {
            case OsuCommunityRequestKind.OAuthToken:
                OAuthRequests++;
                break;
            case OsuCommunityRequestKind.DiscoverySearch:
                SearchRequests++;
                break;
            case OsuCommunityRequestKind.TagCatalog:
            case OsuCommunityRequestKind.BeatmapTags:
                TagRequests++;
                break;
        }
    }

    internal void RecordCacheHit() => CacheHits++;

    internal void RecordSearchPageFetched() => SearchPagesFetched++;

    internal void RecordRateLimit() => RateLimitCount++;

    internal void RecordRetry() => Retries++;
}

/// <summary>
/// Échec explicite après épuisement des retries HTTP 429. Le type permet à
/// l'UI de distinguer une limite osu! d'une erreur réseau générale sans
/// exposer de détail technique à l'utilisateur.
/// </summary>
internal sealed class OsuCommunityRateLimitException : HttpRequestException
{
    internal OsuCommunityRateLimitException(
        OsuCommunityRequestKind requestKind,
        TimeSpan? retryAfter)
        : base("osu! rate limit retries were exhausted.", null,
            HttpStatusCode.TooManyRequests)
    {
        RequestKind = requestKind;
        RetryAfter = retryAfter;
    }

    internal OsuCommunityRequestKind RequestKind { get; }

    internal TimeSpan? RetryAfter { get; }
}

/// <summary>
/// Sérialise les requêtes osu! communautaires, espace les départs et ne
/// réessaie que les 429. Les backoffs sont toujours annulables.
/// </summary>
internal sealed class OsuCommunityRequestPolicy
{
    private const int MaxRetries = 3;
    private static readonly TimeSpan MinimumRequestInterval =
        TimeSpan.FromMilliseconds(250);

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly TimeProvider timeProvider;
    private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;
    private readonly TimeSpan minimumRequestInterval;
    private DateTimeOffset nextRequestAtUtc = DateTimeOffset.MinValue;

    internal OsuCommunityRequestPolicy(
        TimeProvider? timeProvider = null,
        TimeSpan? minimumRequestInterval = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.minimumRequestInterval = minimumRequestInterval
            ?? MinimumRequestInterval;
        this.delayAsync = delayAsync
            ?? ((delay, token) => Task.Delay(delay, token));
    }

    internal async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        Func<HttpRequestMessage> createRequest,
        OsuCommunityRequestKind requestKind,
        CommunityDiscoveryRequestDiagnostics? diagnostics,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(createRequest);

        await gate.WaitAsync(cancellationToken);

        try
        {
            for (int retry = 0; ; retry++)
            {
                await WaitForRequestSlotAsync(cancellationToken);

                using HttpRequestMessage request = createRequest();
                diagnostics?.RecordHttpRequest(requestKind);

                HttpResponseMessage response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                nextRequestAtUtc = timeProvider.GetUtcNow()
                    .Add(minimumRequestInterval);

                if (response.StatusCode != HttpStatusCode.TooManyRequests)
                {
                    return response;
                }

                diagnostics?.RecordRateLimit();
                TimeSpan retryDelay = GetRetryDelay(response.Headers.RetryAfter,
                    retry);
                response.Dispose();

                if (retry >= MaxRetries)
                {
                    throw new OsuCommunityRateLimitException(
                        requestKind,
                        retryDelay);
                }

                diagnostics?.RecordRetry();
                await delayAsync(retryDelay, cancellationToken);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task WaitForRequestSlotAsync(
        CancellationToken cancellationToken)
    {
        TimeSpan delay = nextRequestAtUtc - timeProvider.GetUtcNow();

        if (delay > TimeSpan.Zero)
        {
            await delayAsync(delay, cancellationToken);
        }
    }

    private TimeSpan GetRetryDelay(
        RetryConditionHeaderValue? retryAfter,
        int retry)
    {
        if (retryAfter?.Delta is TimeSpan delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter?.Date is DateTimeOffset date)
        {
            TimeSpan untilDate = date - timeProvider.GetUtcNow();

            if (untilDate > TimeSpan.Zero)
            {
                return untilDate;
            }
        }

        return TimeSpan.FromSeconds(Math.Pow(2, retry));
    }
}
