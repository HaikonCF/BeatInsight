using BeatInsight.Models.Discovery;
using BeatInsight.Services.CommunityDiscovery;
using BeatInsight.Services.Ml;
using System.IO;
using System.Windows;

namespace BeatInsight.Tests.Services;

public sealed class CommunityDiscoveryUiOrchestrationTests
{
    [Fact]
    public void RequestFactory_SelectedFamilyAndResultCount_AreForwarded()
    {
        bool created = CommunityDiscoveryUiRequestFactory.TryCreate(
            CommunitySamplingFamily.Reading,
            maxResults: 30,
            minStarText: null,
            maxStarText: null,
            out CommunityDiscoveryRequest? request,
            out string error);

        Assert.True(created, error);
        CommunityDiscoveryRequest actual = Assert.IsType<CommunityDiscoveryRequest>(
            request);
        Assert.Equal(CommunitySamplingFamily.Reading, actual.SamplingFamily);
        Assert.Equal(30, actual.MaxResults);
        Assert.Null(actual.MinStarRating);
        Assert.Null(actual.MaxStarRating);
    }

    [Fact]
    public void RequestFactory_OptionalStarFilters_AreForwarded()
    {
        bool created = CommunityDiscoveryUiRequestFactory.TryCreate(
            CommunitySamplingFamily.Tech,
            maxResults: 20,
            minStarText: "4.25",
            maxStarText: "6.75",
            out CommunityDiscoveryRequest? request,
            out string error);

        Assert.True(created, error);
        CommunityDiscoveryRequest actual = Assert.IsType<CommunityDiscoveryRequest>(
            request);
        Assert.Equal(4.25, actual.MinStarRating);
        Assert.Equal(6.75, actual.MaxStarRating);
    }

    [Fact]
    public void RequestFactory_InvalidStarRange_IsRejectedBeforeSearch()
    {
        bool created = CommunityDiscoveryUiRequestFactory.TryCreate(
            CommunitySamplingFamily.Jump,
            maxResults: 20,
            minStarText: "7",
            maxStarText: "4",
            out CommunityDiscoveryRequest? request,
            out string error);

        Assert.False(created);
        Assert.Null(request);
        Assert.Equal("Min ★ cannot exceed Max ★.", error);
    }

    [Fact]
    public void ReviewResolver_OnlyAllowsOwnedCandidateWithExistingLocalPath()
    {
        string path = Path.GetTempFileName();

        try
        {
            CommunityDiscoveryReviewTarget allowed =
                CommunityDiscoveryReviewResolver.Resolve(
                    Candidate(alreadyOwned: true),
                    path);

            CommunityDiscoveryReviewTarget rejected =
                CommunityDiscoveryReviewResolver.Resolve(
                    Candidate(alreadyOwned: false),
                    path);

            Assert.True(allowed.CanLoad);
            Assert.Equal(path, allowed.SourceFilePath);
            Assert.False(rejected.CanLoad);
            Assert.Null(rejected.SourceFilePath);
            Assert.Equal("Map not installed locally.", rejected.Status);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CandidateView_ExposesLocalAndHumanStateWithoutHumanLabels()
    {
        CommunityBeatmapCandidate candidate = Candidate(
            alreadyOwned: true,
            alreadyInDataset: true,
            humanValidated: true);

        CommunityDiscoveryCandidateViewModel view =
            CommunityDiscoveryCandidateViewFactory.Create(candidate);

        Assert.Equal("Already owned: Yes", view.AlreadyOwned);
        Assert.Equal("In ML Dataset: Yes", view.AlreadyInMlDataset);
        Assert.Equal("Human validated: Yes", view.HumanValidated);
        Assert.Contains("skillset/tech (12)", view.CommunityTags);
        Assert.DoesNotContain(
            typeof(CommunityDiscoveryCandidateViewModel).GetProperties(),
            property => property.Name.Contains("HumanLabel", StringComparison.Ordinal));
    }

    [Fact]
    public void CandidateView_EnrichedCandidate_ShowsNumericEvidenceAndTags()
    {
        CommunityBeatmapCandidate candidate = Candidate();

        CommunityDiscoveryCandidateViewModel view =
            CommunityDiscoveryCandidateViewFactory.Create(candidate);

        Assert.Equal("Search match: Tech", view.SamplingFamily);
        Assert.Equal(
            $"Community evidence: {candidate.EvidenceScore:F2}",
            view.EvidenceScore);
        Assert.Equal("Tags: skillset/tech (12)", view.CommunityTags);
    }

    [Fact]
    public void CandidateView_NonEnrichedCandidate_ShowsUnavailableNotZero()
    {
        CommunityBeatmapCandidate candidate = new()
        {
            BeatmapId = 123,
            BeatmapSetId = 456,
            Artist = "Artist",
            Title = "Title",
            DifficultyName = "Insane",
            Mapper = "Mapper",
            StarRating = 5.25,
            Status = "ranked",
            UserTags = [],
            CommunityDetailsAvailable = false,
            SamplingFamily = CommunitySamplingFamily.Tech,
            EvidenceScore = 0.0,
        };

        CommunityDiscoveryCandidateViewModel view =
            CommunityDiscoveryCandidateViewFactory.Create(candidate);

        Assert.Equal("Search match: Tech", view.SamplingFamily);
        Assert.Equal("Community evidence: unavailable", view.EvidenceScore);
        Assert.DoesNotContain("0.00", view.EvidenceScore);
        Assert.Equal("Tags: unavailable", view.CommunityTags);
    }

    [Fact]
    public void CandidateView_OwnedCandidate_ShowsLoadForReviewNotDownload()
    {
        CommunityBeatmapCandidate candidate = Candidate(alreadyOwned: true);

        CommunityDiscoveryCandidateViewModel view =
            CommunityDiscoveryCandidateViewFactory.Create(candidate);

        Assert.True(view.IsOwned);
        Assert.Equal(Visibility.Visible, view.LoadForReviewButtonVisibility);
        Assert.Equal(Visibility.Collapsed, view.DownloadButtonVisibility);
    }

    [Fact]
    public void CandidateView_NotOwnedCandidate_ShowsDownloadNotLoadForReview()
    {
        CommunityBeatmapCandidate candidate = Candidate(alreadyOwned: false);

        CommunityDiscoveryCandidateViewModel view =
            CommunityDiscoveryCandidateViewFactory.Create(candidate);

        Assert.False(view.IsOwned);
        Assert.Equal(Visibility.Collapsed, view.LoadForReviewButtonVisibility);
        Assert.Equal(Visibility.Visible, view.DownloadButtonVisibility);
    }

    [Fact]
    public void CandidateView_DownloadOperationRunning_DisablesDownloadButton()
    {
        CommunityBeatmapCandidate candidate = Candidate(alreadyOwned: false);
        CommunityDiscoveryCandidateViewModel view =
            CommunityDiscoveryCandidateViewFactory.Create(candidate);

        Assert.True(view.IsDownloadButtonEnabled);

        view.IsDownloadOperationRunning = true;

        Assert.False(view.IsDownloadButtonEnabled);
        Assert.Equal(Visibility.Visible, view.CancelButtonVisibility);
    }

    [Fact]
    public void CandidateView_ConfirmedImport_FlipsToLoadForReview()
    {
        CommunityBeatmapCandidate candidate = Candidate(alreadyOwned: false);
        CommunityDiscoveryCandidateViewModel view =
            CommunityDiscoveryCandidateViewFactory.Create(candidate);

        view.IsOwned = true;
        view.AlreadyOwned = "Already owned: Yes";

        Assert.Equal(Visibility.Visible, view.LoadForReviewButtonVisibility);
        Assert.Equal(Visibility.Collapsed, view.DownloadButtonVisibility);
        Assert.Equal("Already owned: Yes", view.AlreadyOwned);
    }

    [Fact]
    public void DiscoveryReview_NeverAutoAdvancesIntoFastOrCalibrationQueue()
    {
        Assert.Null(LabelingQueueNavigationPolicy.GetSkipTarget(
            LabelingQueueKind.DiscoveryReview));
        Assert.False(LabelingQueueNavigationPolicy.ShouldAutoAdvanceAfterValidation(
            LabelingQueueKind.DiscoveryReview));

        // Le comportement historique hors file est conservé.
        Assert.Equal(
            LabelingQueueKind.FastUnlabeled,
            LabelingQueueNavigationPolicy.GetSkipTarget(LabelingQueueKind.None));
    }

    private static CommunityBeatmapCandidate Candidate(
        bool alreadyOwned = false,
        bool alreadyInDataset = false,
        bool humanValidated = false) =>
        new()
        {
            BeatmapId = 123,
            BeatmapSetId = 456,
            Artist = "Artist",
            Title = "Title",
            DifficultyName = "Insane",
            Mapper = "Mapper",
            StarRating = 5.25,
            Status = "ranked",
            UserTags =
            [
                new CommunityBeatmapUserTag
                {
                    Name = "skillset/tech",
                    Votes = 12,
                },
            ],
            SamplingFamily = CommunitySamplingFamily.Tech,
            EvidenceScore = 1.25,
            AlreadyOwned = alreadyOwned,
            AlreadyInMlDataset = alreadyInDataset,
            HumanValidated = humanValidated,
        };
}
