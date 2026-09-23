using BeatInsight.Models.AnalysisCache;

namespace BeatInsight.ViewModels;

internal sealed class OverviewLoadCoordinator(OverviewViewModel overview)
{
    private long generation;

    internal long Begin(string message)
    {
        long request = ++generation;
        overview.SetLoading(message);
        return request;
    }

    internal void Invalidate() => ++generation;

    internal bool IsCurrent(long request) => request == generation;

    internal bool Complete(
        long request,
        BeatmapAnalysisSnapshot snapshot,
        bool wasCacheHit = false,
        TimeSpan? loadTime = null,
        string? backgroundPath = null,
        bool includeSecondaryDetails = true)
    {
        if (!IsCurrent(request))
            return false;

        overview.LoadSnapshot(
            snapshot, wasCacheHit, loadTime, backgroundPath,
            includeSecondaryDetails, setReady: false);
        overview.SetReady();
        return true;
    }

    internal bool LoadSnapshot(
        long request,
        BeatmapAnalysisSnapshot snapshot,
        bool wasCacheHit,
        TimeSpan loadTime,
        string? backgroundPath)
    {
        if (!IsCurrent(request))
            return false;

        overview.LoadSnapshot(
            snapshot, wasCacheHit, loadTime, backgroundPath,
            includeSecondaryDetails: false, setReady: false);
        return true;
    }

    internal bool SetReady(long request)
    {
        if (!IsCurrent(request))
            return false;

        overview.SetReady();
        return true;
    }

    internal bool CompleteSecondaryDetails(
        long request,
        BeatmapAnalysisSnapshot snapshot,
        bool wasCacheHit)
    {
        if (!IsCurrent(request))
            return false;

        overview.LoadSecondarySnapshotDetails(snapshot, wasCacheHit);
        return true;
    }

    internal bool Fail(long request, string message)
    {
        if (!IsCurrent(request))
            return false;

        overview.SetError(message);
        return true;
    }
}
