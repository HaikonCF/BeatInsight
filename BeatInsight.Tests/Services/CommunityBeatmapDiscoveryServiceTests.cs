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
    public async Task FindCandidatesAsync_ReturnsEmptyForAnEmptySource()
    {
        IReadOnlyList<CommunityBeatmapCandidate> candidates =
            await CreateService(new FakeDiscoverySource([])).FindCandidatesAsync(
                Request(CommunitySamplingFamily.Tech));

        Assert.Empty(candidates);
    }

    private static CommunityBeatmapDiscoveryService CreateService(
        FakeDiscoverySource source,
        FakeLocalStateSource? localState = null) =>
        new(
            source,
            localState ?? new FakeLocalStateSource(
                new Dictionary<int, CommunityBeatmapLocalState>()));

    private static CommunityDiscoveryRequest Request(
        CommunitySamplingFamily family,
        bool excludeAlreadyHumanValidated = true) =>
        new()
        {
            SamplingFamily = family,
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
}
