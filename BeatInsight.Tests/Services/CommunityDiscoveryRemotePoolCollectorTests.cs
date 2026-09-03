using BeatInsight.Models.Discovery;
using BeatInsight.Services.CommunityDiscovery;

namespace BeatInsight.Tests.Services;

public sealed class CommunityDiscoveryRemotePoolCollectorTests
{
    [Fact]
    public async Task CollectAsync_AggregatesMultiplePagesBeforeTarget()
    {
        CommunityDiscoveryRemotePool pool = await CollectAsync(
            target: 4,
            new Dictionary<string, CommunityDiscoveryRemoteSearchPage>
            {
                [Key("tech", null)] = Page([Remote(1), Remote(2)], "next"),
                [Key("tech", "next")] = Page([Remote(3), Remote(4)]),
            });

        Assert.Equal([1, 2, 3, 4], pool.Seeds.Select(seed => seed.Candidate.BeatmapId));
        Assert.Equal(2, pool.Diagnostics.PagesFetched);
    }

    [Fact]
    public async Task CollectAsync_DeduplicatesCandidatesAcrossPages()
    {
        CommunityDiscoveryRemotePool pool = await CollectAsync(
            target: 3,
            new Dictionary<string, CommunityDiscoveryRemoteSearchPage>
            {
                [Key("stream", null)] = Page([Remote(1), Remote(2)], "next"),
                [Key("stream", "next")] = Page([Remote(2), Remote(3)]),
            },
            searchTag: "stream");

        Assert.Equal([1, 2, 3], pool.Seeds.Select(seed => seed.Candidate.BeatmapId));
        Assert.Equal(3, pool.Diagnostics.AfterDedupe);
    }

    [Fact]
    public async Task CollectAsync_ContinuesPastAPageWithMostlyInvalidCandidates()
    {
        CommunityDiscoveryRemotePool pool = await CollectAsync(
            target: 2,
            new Dictionary<string, CommunityDiscoveryRemoteSearchPage>
            {
                [Key("jump", null)] = Page(
                    [Remote(1, mode: 1), Remote(2, status: "pending")],
                    "next"),
                [Key("jump", "next")] = Page([Remote(3), Remote(4)]),
            },
            searchTag: "jump");

        Assert.Equal([3, 4], pool.Seeds.Select(seed => seed.Candidate.BeatmapId));
        Assert.Equal(2, pool.Diagnostics.PagesFetched);
        Assert.Equal(3, pool.Diagnostics.AfterModeFilter);
        Assert.Equal(2, pool.Diagnostics.AfterStatusFilter);
    }

    [Fact]
    public async Task CollectAsync_StopsWhenEnoughEligibleCandidatesExist()
    {
        int fetchCount = 0;
        var collector = new CommunityDiscoveryRemotePoolCollector();

        CommunityDiscoveryRemotePool pool = await collector.CollectAsync(
            Request(),
            ["tech"],
            targetCandidateCount: 2,
            (tag, cursor, _) =>
            {
                fetchCount++;
                return Task.FromResult(cursor is null
                    ? Page([Remote(1), Remote(2)], "next")
                    : throw new InvalidOperationException(
                        "A second page must not be fetched after the target."));
            },
            hasFamilyEvidenceAsync: null,
            CancellationToken.None);

        Assert.Equal(2, pool.Seeds.Count);
        Assert.Equal(1, fetchCount);
        Assert.Equal(1, pool.Diagnostics.PagesFetched);
    }

    [Fact]
    public async Task CollectAsync_AppliesEvidenceBeforeDecidingToStop()
    {
        var collector = new CommunityDiscoveryRemotePoolCollector();

        CommunityDiscoveryRemotePool pool = await collector.CollectAsync(
            Request(),
            ["tech"],
            targetCandidateCount: 2,
            (tag, cursor, _) => Task.FromResult(cursor is null
                ? Page([Remote(1), Remote(2)], "next")
                : Page([Remote(3), Remote(4)])),
            (seed, _) => Task.FromResult(seed.Candidate.BeatmapId is 2 or 3),
            CancellationToken.None);

        Assert.Equal([2, 3], pool.Seeds.Select(seed => seed.Candidate.BeatmapId));
        Assert.Equal(2, pool.Diagnostics.PagesFetched);
        Assert.Equal(2, pool.Diagnostics.AfterTagEvidenceFilter);
    }

    [Fact]
    public async Task CollectAsync_HonorsCancellationDuringPagination()
    {
        var collector = new CommunityDiscoveryRemotePoolCollector();
        var secondPageStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();

        Task<CommunityDiscoveryRemotePool> collection = collector.CollectAsync(
            Request(),
            ["stream"],
            targetCandidateCount: 2,
            async (_, cursor, cancellationToken) =>
            {
                if (cursor is null)
                {
                    return Page([Remote(1)], "next");
                }

                secondPageStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return Page([]);
            },
            hasFamilyEvidenceAsync: null,
            cancellation.Token);

        await secondPageStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => collection);
    }

    [Fact]
    public async Task CollectAsync_StopsAtEndOfResultsWithoutASecondRequest()
    {
        int fetchCount = 0;
        var collector = new CommunityDiscoveryRemotePoolCollector();

        CommunityDiscoveryRemotePool pool = await collector.CollectAsync(
            Request(),
            ["stream"],
            targetCandidateCount: 3,
            (_, _, _) =>
            {
                fetchCount++;
                return Task.FromResult(Page([Remote(1)]));
            },
            hasFamilyEvidenceAsync: null,
            CancellationToken.None);

        Assert.Single(pool.Seeds);
        Assert.Equal(1, fetchCount);
    }

    [Fact]
    public async Task CollectAsync_UsesTheBoundedDiscoveryPageBudget()
    {
        var collector = new CommunityDiscoveryRemotePoolCollector();
        int fetchCount = 0;

        CommunityDiscoveryRemotePool pool = await collector.CollectAsync(
            Request(),
            ["tech-a", "tech-b", "tech-c"],
            targetCandidateCount: 100,
            (_, cursor, _) =>
            {
                fetchCount++;
                int id = fetchCount;
                return Task.FromResult(Page(
                    [Remote(id)],
                    cursor is null ? "next" : $"next-{id}"));
            },
            hasFamilyEvidenceAsync: null,
            CancellationToken.None);

        Assert.Equal(CommunityDiscoveryRemotePoolCollector.MaxPagesPerDiscovery,
            fetchCount);
        Assert.Equal(fetchCount, pool.Diagnostics.PagesFetched);
    }

    private static async Task<CommunityDiscoveryRemotePool> CollectAsync(
        int target,
        IReadOnlyDictionary<string, CommunityDiscoveryRemoteSearchPage> pages,
        string searchTag = "tech")
    {
        var collector = new CommunityDiscoveryRemotePoolCollector();

        return await collector.CollectAsync(
            Request(),
            [searchTag],
            target,
            (tag, cursor, _) => Task.FromResult(pages[Key(tag, cursor)]),
            hasFamilyEvidenceAsync: null,
            CancellationToken.None);
    }

    private static CommunityDiscoveryRequest Request() => new()
    {
        SamplingFamily = CommunitySamplingFamily.Tech,
        MinStarRating = 4.0,
        MaxStarRating = 6.0,
        IncludeRanked = true,
        IncludeApproved = true,
        IncludeLoved = true,
    };

    private static CommunityDiscoveryRemoteSearchPage Page(
        IReadOnlyList<CommunityBeatmapRemoteCandidate> candidates,
        string? nextCursor = null) =>
        new()
        {
            Candidates = candidates,
            NextCursor = nextCursor,
            RawBeatmapSetCount = candidates.Count,
            RawDifficultyCount = candidates.Count,
        };

    private static CommunityBeatmapRemoteCandidate Remote(
        int id,
        int mode = 0,
        string status = "ranked",
        double stars = 5.0) =>
        new()
        {
            BeatmapId = id,
            BeatmapSetId = id + 100,
            Artist = "Artist",
            Title = "Title",
            DifficultyName = "Difficulty",
            Mapper = "Mapper",
            StarRating = stars,
            Status = status,
            GameMode = mode,
        };

    private static string Key(string tag, string? cursor) =>
        $"{tag}|{cursor ?? "first"}";
}
