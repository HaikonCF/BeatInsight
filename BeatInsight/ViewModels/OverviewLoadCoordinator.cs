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
        string? backgroundPath = null)
    {
        if (!IsCurrent(request))
            return false;

        overview.LoadSnapshot(snapshot, wasCacheHit, loadTime, backgroundPath);
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
