using BeatInsight.Models.Discovery;
using BeatInsight.Services.CommunityDiscovery;

namespace BeatInsight.Tests.Services;

public sealed class CommunityBeatmapDiscoveryServiceTests
{
    [Fact]
    public async Task FindCandidatesAsync_FiltersToOsuStandardAndRequestedFamily()
    {
        var source = new FakeDiscoverySource(
        [
            Remote(1, "skillset/jumps", votes: 5),
            Remote(2, "skillset/streams", votes: 5),
            Remote(3, "skillset/jumps", votes: 5, gameMode: 1),
        ]);

        IReadOnlyList<CommunityBeatmapCandidate> candidates =
            await CreateService(source).FindCandidatesAsync(Request(
                CommunitySamplingFamily.Jump));

        CommunityBeatmapCandidate candidate = Assert.Single(candidates);
        Assert.Equal(1, candidate.BeatmapId);
        Assert.Equal(CommunitySamplingFamily.Jump, candidate.SamplingFamily);
    }

    [Fact]
    public async Task FindCandidatesAsync_DeduplicatesAndOrdersDeterministically()
    {
        var source = new FakeDiscoverySource(
        [
            Remote(30, "skillset/jumps", votes: 9),
            Remote(10, "skillset/jumps", votes: 1),
            Remote(10, "skillset/jumps", votes: 99),
            Remote(20, "skillset/jumps", votes: 99),
        ]);

        IReadOnlyList<CommunityBeatmapCandidate> candidates =
            await CreateService(source).FindCandidatesAsync(Request(
                CommunitySamplingFamily.Jump));

        Assert.Equal([10, 20, 30], candidates.Select(x => x.BeatmapId));
        Assert.Single(candidates[0].UserTags);
        Assert.Equal(99, candidates[0].UserTags[0].Votes);
        Assert.Equal(candidates[0].EvidenceScore, candidates[1].EvidenceScore);
    }

    [Fact]
    public async Task FindCandidatesAsync_ExcludesValidatedAndEnrichesLocalState()
    {
        var source = new FakeDiscoverySource(
        [
            Remote(1, "skillset/tech", votes: 10),
            Remote(2, "tech/slider tech", votes: 10),
        ]);
        var localState = new FakeLocalStateSource(
            new Dictionary<int, CommunityBeatmapLocalState>
            {
                [1] = new(true, true, true),
                [2] = new(false, true, false),
            });

        IReadOnlyList<CommunityBeatmapCandidate> candidates =
            await CreateService(source, localState).FindCandidatesAsync(Request(
                CommunitySamplingFamily.Tech));

        CommunityBeatmapCandidate candidate = Assert.Single(candidates);
        Assert.Equal(2, candidate.BeatmapId);
        Assert.True(candidate.AlreadyInMlDataset);
        Assert.False(candidate.AlreadyOwned);
        Assert.False(candidate.HumanValidated);
    }

    [Fact]
    public async Task FindCandidatesAsync_CanReturnHumanValidatedWhenRequested()
    {
        var source = new FakeDiscoverySource(
            [Remote(1, "skillset/streams", votes: 10)]);
        var localState = new FakeLocalStateSource(
            new Dictionary<int, CommunityBeatmapLocalState>
            {
                [1] = new(true, true, true),
            });

        IReadOnlyList<CommunityBeatmapCandidate> candidates =
            await CreateService(source, localState).FindCandidatesAsync(Request(
                CommunitySamplingFamily.Stream,
                excludeAlreadyHumanValidated: false));

        CommunityBeatmapCandidate candidate = Assert.Single(candidates);
        Assert.True(candidate.AlreadyOwned);
        Assert.True(candidate.AlreadyInMlDataset);
        Assert.True(candidate.HumanValidated);
    }

    [Fact]
    public async Task FindCandidatesAsync_AppliesStarAndStatusFilters()
    {
        var source = new FakeDiscoverySource(
        [
            Remote(1, "skillset/streams", votes: 10, stars: 4.5),
            Remote(2, "skillset/streams", votes: 10, stars: 5.5),
            Remote(3, "skillset/streams", votes: 10, status: "loved"),
        ]);

        IReadOnlyList<CommunityBeatmapCandidate> candidates =
            await CreateService(source).FindCandidatesAsync(new CommunityDiscoveryRequest
            {
                SamplingFamily = CommunitySamplingFamily.Stream,
                MinStarRating = 4.0,
                MaxStarRating = 5.0,
                IncludeRanked = true,
                IncludeApproved = false,
                IncludeLoved = false,
            });

        CommunityBeatmapCandidate candidate = Assert.Single(candidates);
        Assert.Equal(1, candidate.BeatmapId);
    }

    [Fact]
    public async Task FindCandidatesAsync_AppliesMaxResultsAfterFilteringAndRanking()
    {
        var source = new FakeDiscoverySource(
        [
            Remote(1, "skillset/streams", votes: 1),
            Remote(2, "skillset/streams", votes: 100, status: "pending"),
            Remote(3, "skillset/streams", votes: 10),
            Remote(4, "skillset/streams", votes: 50),
        ]);

        IReadOnlyList<CommunityBeatmapCandidate> candidates =
            await CreateService(source).FindCandidatesAsync(Request(
                CommunitySamplingFamily.Stream,
                maxResults: 2));

        Assert.Equal([4, 3], candidates.Select(candidate => candidate.BeatmapId));
    }

    [Fact]
    public async Task FindCandidatesAsync_EnrichesOnlyFinalCandidates()
    {
        var source = new EnrichingDiscoverySource(
        [
            SearchRemote(1, "skillset/tech"),
            SearchRemote(2, "skillset/tech"),
            SearchRemote(3, "skillset/tech"),
        ]);

        IReadOnlyList<CommunityBeatmapCandidate> candidates =
            await CreateService(source).FindCandidatesAsync(Request(
                CommunitySamplingFamily.Tech,
                maxResults: 2));

        Assert.Equal([1, 2], candidates.Select(candidate => candidate.BeatmapId));
        Assert.Equal([1, 2], source.EnrichedBeatmapIds);
        Assert.All(candidates, candidate =>
            Assert.True(candidate.CommunityDetailsAvailable));
    }

    [Fact]
    public async Task FindCandidatesAsync_RateLimitedEnrichmentKeepsSearchCandidates()
    {
        var source = new EnrichingDiscoverySource(
        [
            SearchRemote(1, "skillset/tech"),
            SearchRemote(2, "skillset/tech"),
        ], rateLimitedBeatmapId: 1);

        IReadOnlyList<CommunityBeatmapCandidate> candidates =
            await CreateService(source).FindCandidatesAsync(Request(
                CommunitySamplingFamily.Tech,
                maxResults: 2));

        Assert.Equal([1, 2], candidates.Select(candidate => candidate.BeatmapId));
        Assert.Equal([1], source.EnrichedBeatmapIds);
        Assert.All(candidates, candidate =>
            Assert.False(candidate.CommunityDetailsAvailable));
    }

    [Fact]
    public async Task FindCandidatesAsync_DeduplicatesBeforeOptionalEnrichment()
    {
        var source = new EnrichingDiscoverySource(
        [
            SearchRemote(1, "skillset/tech"),
            SearchRemote(1, "tech/slider tech"),
            SearchRemote(2, "skillset/tech"),
        ]);

        _ = await CreateService(source).FindCandidatesAsync(Request(
            CommunitySamplingFamily.Tech,
            maxResults: 2));

        Assert.Equal([1, 2], source.EnrichedBeatmapIds);
    }

    [Fact]
    public async Task FindCandidatesAsync_BoundsEagerCommunityDetails()
    {
        var source = new EnrichingDiscoverySource(
            Enumerable.Range(1, 12)
                .Select(id => SearchRemote(id, "skillset/tech"))
                .ToArray());

        IReadOnlyList<CommunityBeatmapCandidate> candidates =
            await CreateService(source).FindCandidatesAsync(Request(
                CommunitySamplingFamily.Tech,
                maxResults: 12));

        Assert.Equal(12, candidates.Count);
        Assert.Equal(8, source.EnrichedBeatmapIds.Count);
        Assert.Equal(8, candidates.Count(
            candidate => candidate.CommunityDetailsAvailable));
    }

    [Fact]
    public async Task FindCandidatesAsync_HybridRequiresPositiveEvidenceFromTwoFamilies()
    {
        var source = new FakeDiscoverySource(
        [
            Remote(1, "skillset/jumps", votes: 10),
            Remote(2,
                [Tag("skillset/jumps", 10), Tag("skillset/tech", 2)]),
        ]);

        IReadOnlyList<CommunityBeatmapCandidate> candidates =
            await CreateService(source).FindCandidatesAsync(Request(
                CommunitySamplingFamily.Hybrid));

        CommunityBeatmapCandidate candidate = Assert.Single(candidates);
        Assert.Equal(2, candidate.BeatmapId);
        Assert.True(candidate.EvidenceScore > 0.0);
    }

    [Fact]
    public async Task FindCandidatesAsync_HybridAcceptsTwoDistinctSearchTags()
    {
        CommunityBeatmapRemoteCandidate remote = new()
        {
            BeatmapId = 1,
            BeatmapSetId = 1001,
            Artist = "Artist",
            Title = "Title",
            DifficultyName = "Difficulty",
            Mapper = "Mapper",
            StarRating = 5.0,
            Status = "ranked",
            GameMode = 0,
            SearchTagNames = ["skillset/jumps", "skillset/tech"],
        };

        IReadOnlyList<CommunityBeatmapCandidate> candidates =
            await CreateService(new FakeDiscoverySource([remote]))
                .FindCandidatesAsync(Request(CommunitySamplingFamily.Hybrid));

        Assert.Single(candidates);
        Assert.False(candidates[0].CommunityDetailsAvailable);
    }

    [Fact]
    public async Task FindCandidatesAsync_IsReadOnlyForHumanLabels()
    {
        var source = new FakeDiscoverySource(
            [Remote(1, "skillset/reading", votes: 10)]);
        var localState = new FakeLocalStateSource(
            new Dictionary<int, CommunityBeatmapLocalState>
            {
                [1] = new(false, true, false),
            });

        _ = await CreateService(source, localState).FindCandidatesAsync(Request(
            CommunitySamplingFamily.Reading));

        Assert.Equal(1, localState.ReadCount);
        Assert.Equal(new CommunityBeatmapLocalState(false, true, false),
            localState.States[1]);
    }

    [Fact]
    public async Task FindCandidatesAsync_HonorsCancellationBeforeSourceCall()
    {
        var source = new FakeDiscoverySource([]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CreateService(source).FindCandidatesAsync(
                Request(CommunitySamplingFamily.Stream),
                cancellation.Token));

        Assert.False(source.WasCalled);
    }

    [Fact]
    public async Task FindCandidatesAsync_ForwardsCancellationToAnActiveSource()
    {
        var source = new BlockingDiscoverySource();
        using var cancellation = new CancellationTokenSource();

        Task<IReadOnlyList<CommunityBeatmapCandidate>> search =
            CreateService(source).FindCandidatesAsync(
                Request(CommunitySamplingFamily.Stream),
                cancellation.Token);

        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => search);
        Assert.True(source.SawCancellation);
    }

    [Fact]
    public async Task FindCandidatesAsync_ReturnsEmptyForAnEmptySource()
    {
        IReadOnlyList<CommunityBeatmapCandidate> candidates =
            await CreateService(new FakeDiscoverySource([])).FindCandidatesAsync(
                Request(CommunitySamplingFamily.Tech));

        Assert.Empty(candidates);
    }

    private static CommunityBeatmapDiscoveryService CreateService(
        ICommunityBeatmapDiscoverySource source,
        FakeLocalStateSource? localState = null) =>
        new(
            source,
            localState ?? new FakeLocalStateSource(
                new Dictionary<int, CommunityBeatmapLocalState>()));

    private static CommunityDiscoveryRequest Request(
        CommunitySamplingFamily family,
        bool excludeAlreadyHumanValidated = true,
        int maxResults = 30) =>
        new()
        {
            SamplingFamily = family,
            MaxResults = maxResults,
            ExcludeAlreadyHumanValidated = excludeAlreadyHumanValidated,
        };

    private static CommunityBeatmapRemoteCandidate Remote(
        int id,
        string tag,
        int votes,
        int gameMode = 0,
        string status = "ranked",
        double stars = 5.0) =>
        Remote(id, [Tag(tag, votes)], gameMode, status, stars);

    private static CommunityBeatmapRemoteCandidate Remote(
        int id,
        IReadOnlyList<CommunityBeatmapUserTag> tags,
        int gameMode = 0,
        string status = "ranked",
        double stars = 5.0) =>
        new()
        {
            BeatmapId = id,
            BeatmapSetId = id + 1000,
            Artist = "Artist",
            Title = "Title",
            DifficultyName = "Difficulty",
            Mapper = "Mapper",
            StarRating = stars,
            Status = status,
            GameMode = gameMode,
            UserTags = tags,
        };

    private static CommunityBeatmapRemoteCandidate SearchRemote(
        int id,
        string searchTag) => new()
        {
            BeatmapId = id,
            BeatmapSetId = id + 1000,
            Artist = "Artist",
            Title = "Title",
            DifficultyName = "Difficulty",
            Mapper = "Mapper",
            StarRating = 5.0,
            Status = "ranked",
            GameMode = 0,
            SearchTagNames = [searchTag],
        };

    private static CommunityBeatmapUserTag Tag(string name, int votes) =>
        new()
        {
            Name = name,
            Votes = votes,
        };

    private sealed class FakeDiscoverySource(
        IReadOnlyList<CommunityBeatmapRemoteCandidate> candidates) :
        ICommunityBeatmapDiscoverySource
    {
        internal bool WasCalled { get; private set; }

        public Task<IReadOnlyList<CommunityBeatmapRemoteCandidate>>
            FindCandidatesAsync(
                CommunityDiscoveryRequest request,
                CancellationToken cancellationToken)
        {
            WasCalled = true;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(candidates);
        }
    }

    private sealed class FakeLocalStateSource(
        IReadOnlyDictionary<int, CommunityBeatmapLocalState> states) :
        ICommunityBeatmapLocalStateSource
    {
        internal IReadOnlyDictionary<int, CommunityBeatmapLocalState> States =>
            states;

        internal int ReadCount { get; private set; }

        public IReadOnlyDictionary<int, CommunityBeatmapLocalState> GetStates(
            IReadOnlyCollection<int> beatmapIds)
        {
            ReadCount++;

            return states
                .Where(pair => beatmapIds.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
        }
    }

    private sealed class BlockingDiscoverySource :
        ICommunityBeatmapDiscoverySource
    {
        internal TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        internal bool SawCancellation { get; private set; }

        public async Task<IReadOnlyList<CommunityBeatmapRemoteCandidate>>
            FindCandidatesAsync(
                CommunityDiscoveryRequest request,
                CancellationToken cancellationToken)
        {
            Started.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                SawCancellation = true;
                throw;
            }

            return [];
        }
    }

    private sealed class EnrichingDiscoverySource(
        IReadOnlyList<CommunityBeatmapRemoteCandidate> candidates,
        int? rateLimitedBeatmapId = null) :
        ICommunityBeatmapDiscoverySource,
        ICommunityBeatmapCandidateMetadataEnricher
    {
        internal List<int> EnrichedBeatmapIds { get; } = [];

        public Task<IReadOnlyList<CommunityBeatmapRemoteCandidate>>
            FindCandidatesAsync(
                CommunityDiscoveryRequest request,
                CancellationToken cancellationToken) =>
                Task.FromResult(candidates);

        public Task<CommunityCandidateMetadataEnrichmentResult>
            EnrichCandidateAsync(
                CommunityBeatmapRemoteCandidate candidate,
                CancellationToken cancellationToken)
        {
            EnrichedBeatmapIds.Add(candidate.BeatmapId);

            if (candidate.BeatmapId == rateLimitedBeatmapId)
            {
                return Task.FromResult(
                    new CommunityCandidateMetadataEnrichmentResult(
                        candidate,
                        RateLimited: true));
            }

            CommunityBeatmapRemoteCandidate enriched = new()
            {
                BeatmapId = candidate.BeatmapId,
                BeatmapSetId = candidate.BeatmapSetId,
                Artist = candidate.Artist,
                Title = candidate.Title,
                DifficultyName = candidate.DifficultyName,
                Mapper = candidate.Mapper,
                StarRating = candidate.StarRating,
                BPM = candidate.BPM,
                Status = candidate.Status,
                GameMode = candidate.GameMode,
                SearchTagNames = candidate.SearchTagNames,
                UserTags = [Tag("skillset/tech", 3)],
                CommunityDetailsAvailable = true,
            };

            return Task.FromResult(
                new CommunityCandidateMetadataEnrichmentResult(
                    enriched,
                    RateLimited: false));
        }
    }
}
