using BeatInsight.Analysis;
using System.IO;

namespace BeatInsight.Tests.Analysis;

public sealed class TechReviewSessionTests
{
    [Fact]
    public void LoadReviewFiles_ComputesGlobalCount_AndDeterministicOrdering()
    {
        using TemporaryDirectory directory = new();
        TechReviewStore store = new(directory.Path);

        store.Save(Document(
            "b-map",
            "Beta",
            "Diff B",
            Candidate("b-map", 300, 350),
            Candidate("b-map", 100, 200, label: TechReviewHumanLabel.Tech)));

        store.Save(Document(
            "a-map",
            "Alpha",
            "Diff A",
            Candidate("a-map", 500, 700),
            Candidate("a-map", 100, 200)));

        TechReviewSession session = TechReviewSession.Load(store);

        Assert.Equal(4, session.GetFilteredCandidateCount());

        TechReviewCandidateCursor? first = session.GetCurrentCandidate();
        Assert.NotNull(first);
        Assert.Equal("a-map", first!.StableBeatmapId);
        Assert.Equal(100, first.Candidate.StartTime);

        Assert.True(session.MoveNext());
        TechReviewCandidateCursor second = Assert.IsType<TechReviewCandidateCursor>(
            session.GetCurrentCandidate());
        Assert.Equal("a-map", second.StableBeatmapId);
        Assert.Equal(500, second.Candidate.StartTime);
    }

    [Fact]
    public void FilteringUnreviewed_ReturnsOnlyUnreviewed()
    {
        using TemporaryDirectory directory = new();
        TechReviewStore store = new(directory.Path);

        store.Save(Document(
            "map",
            "Map",
            "Diff",
            Candidate("map", 100, 200, label: TechReviewHumanLabel.Tech),
            Candidate("map", 210, 320, label: TechReviewHumanLabel.Unreviewed),
            Candidate("map", 330, 420, label: TechReviewHumanLabel.NotTech)));

        TechReviewSession session = TechReviewSession.Load(store);
        session.SetLabelFilter(TechReviewLabelFilter.UnreviewedOnly);

        Assert.Equal(1, session.GetFilteredCandidateCount());

        TechReviewCandidateCursor cursor = Assert.IsType<TechReviewCandidateCursor>(
            session.GetCurrentCandidate());
        Assert.Equal(TechReviewHumanLabel.Unreviewed, cursor.Candidate.HumanLabel);
    }

    [Fact]
    public void ApplyingLabels_PersistsThroughStore_AndCanModifyExistingLabel()
    {
        using TemporaryDirectory directory = new();
        TechReviewStore store = new(directory.Path);

        store.Save(Document(
            "map",
            "Map",
            "Diff",
            Candidate("map", 100, 200),
            Candidate("map", 210, 320)));

        TechReviewSession session = TechReviewSession.Load(store);

        Assert.True(session.ApplyLabel(TechReviewHumanLabel.Tech));

        TechReviewDocument afterTech = Assert.IsType<TechReviewDocument>(store.Load("map"));
        Assert.Equal(TechReviewHumanLabel.Tech, afterTech.Candidates[0].HumanLabel);

        session.MovePrevious();
        Assert.True(session.ApplyLabel(TechReviewHumanLabel.NotTech));

        TechReviewDocument afterNotTech = Assert.IsType<TechReviewDocument>(store.Load("map"));
        Assert.Equal(TechReviewHumanLabel.NotTech, afterNotTech.Candidates[0].HumanLabel);

        session.MoveNext();
        Assert.True(session.ApplyLabel(TechReviewHumanLabel.Unsure));

        TechReviewDocument afterUnsure = Assert.IsType<TechReviewDocument>(store.Load("map"));
        Assert.Equal(TechReviewHumanLabel.Unsure, afterUnsure.Candidates[1].HumanLabel);
    }

    [Fact]
    public void NextUnreviewedCandidate_IsSelectedAfterApply()
    {
        using TemporaryDirectory directory = new();
        TechReviewStore store = new(directory.Path);

        store.Save(Document(
            "map",
            "Map",
            "Diff",
            Candidate("map", 100, 200),
            Candidate("map", 210, 320),
            Candidate("map", 330, 420)));

        TechReviewSession session = TechReviewSession.Load(store);

        Assert.Equal(100, session.GetCurrentCandidate()!.Candidate.StartTime);

        session.ApplyLabel(TechReviewHumanLabel.Tech);
        Assert.Equal(210, session.GetCurrentCandidate()!.Candidate.StartTime);

        session.ApplyLabel(TechReviewHumanLabel.NotTech);
        Assert.Equal(330, session.GetCurrentCandidate()!.Candidate.StartTime);
    }

    [Fact]
    public void ReviewedCounters_AreUpdatedAfterLabelChanges()
    {
        using TemporaryDirectory directory = new();
        TechReviewStore store = new(directory.Path);

        store.Save(Document(
            "map",
            "Map",
            "Diff",
            Candidate("map", 100, 200),
            Candidate("map", 210, 320),
            Candidate("map", 330, 420)));

        TechReviewSession session = TechReviewSession.Load(store);

        TechReviewProgressCounters initial = session.GetProgressCounters();
        Assert.Equal(0, initial.Reviewed);
        Assert.Equal(3, initial.Total);
        Assert.Equal(3, initial.Remaining);

        session.ApplyLabel(TechReviewHumanLabel.Tech);
        session.ApplyLabel(TechReviewHumanLabel.NotTech);
        session.ApplyLabel(TechReviewHumanLabel.Unsure);

        TechReviewProgressCounters updated = session.GetProgressCounters();
        Assert.Equal(3, updated.Reviewed);
        Assert.Equal(1, updated.Tech);
        Assert.Equal(1, updated.NotTech);
        Assert.Equal(1, updated.Unsure);
        Assert.Equal(0, updated.Remaining);
    }

    [Fact]
    public void MapFilter_RestrictsCandidatesToSelectedMap()
    {
        using TemporaryDirectory directory = new();
        TechReviewStore store = new(directory.Path);

        store.Save(Document("map-a", "A", "D1", Candidate("map-a", 100, 200)));
        store.Save(Document("map-b", "B", "D2", Candidate("map-b", 300, 400)));

        TechReviewSession session = TechReviewSession.Load(store);

        session.SetMapFilter("map-b");

        Assert.Equal(1, session.GetFilteredCandidateCount());
        Assert.Equal("map-b", session.GetCurrentCandidate()!.StableBeatmapId);
    }

    private static TechReviewDocument Document(
        string stableBeatmapId,
        string mapName,
        string difficulty,
        params TechReviewCandidate[] candidates) =>
        new()
        {
            StableBeatmapId = stableBeatmapId,
            MapName = mapName,
            Difficulty = difficulty,
            Candidates = candidates.ToList()
        };

    private static TechReviewCandidate Candidate(
        string stableBeatmapId,
        double start,
        double end,
        TechReviewPriority priority = TechReviewPriority.High,
        TechReviewHumanLabel label = TechReviewHumanLabel.Unreviewed) =>
        new(
            TechReviewAssistant.CreateCandidateId(stableBeatmapId, start, end),
            stableBeatmapId,
            "Map",
            "Diff",
            start,
            end,
            priority,
            0.5,
            1,
            label);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "BeatInsight-TechReviewSession-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
