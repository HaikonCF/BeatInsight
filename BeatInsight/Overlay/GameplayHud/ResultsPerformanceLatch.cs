using BeatInsight.Integration.Game;

namespace BeatInsight.Overlay.GameplayHud;

internal sealed class ResultsPerformanceLatch
{
    private static readonly TimeSpan RetryRewindTolerance = TimeSpan.FromMilliseconds(250);

    private PerformanceIdentity? identity;
    private LivePerformanceSnapshot? lastValidPerformance;
    private LiveGameplayStatsSnapshot? lastGameplayStats;
    private TimeSpan? lastGameplayPosition;
    private OverlayGameState lastKnownRoute = OverlayGameState.Unknown;

    public LivePerformanceSnapshot? Select(
        OverlayGameState route,
        GameStateSnapshot game,
        LiveGameplayStatsSnapshot? liveStats,
        TimeSpan? gameplayPosition,
        LivePerformanceSnapshot? livePerformance)
    {
        if (!TryCreateIdentity(game, out PerformanceIdentity currentIdentity))
        {
            Clear();
            return route == OverlayGameState.Gameplay ? livePerformance : null;
        }

        if (identity is null || !identity.Value.Matches(currentIdentity))
        {
            ClearAttempt();
            identity = currentIdentity;
        }

        switch (route)
        {
            case OverlayGameState.Gameplay:
                if (lastKnownRoute != OverlayGameState.Gameplay ||
                    IsRetry(lastGameplayStats, liveStats) ||
                    IsRewind(lastGameplayPosition, gameplayPosition))
                {
                    ClearAttempt();
                }

                if (StatsMatchIdentity(liveStats, currentIdentity))
                {
                    lastGameplayStats = liveStats;
                    if (livePerformance is not null)
                        lastValidPerformance = livePerformance;
                }

                if (gameplayPosition is not null)
                    lastGameplayPosition = gameplayPosition;
                lastKnownRoute = OverlayGameState.Gameplay;
                return livePerformance;

            case OverlayGameState.Results:
                lastKnownRoute = OverlayGameState.Results;
                return lastValidPerformance;

            case OverlayGameState.SongSelect:
            case OverlayGameState.Idle:
                Clear();
                lastKnownRoute = route;
                return null;

            default:
                return null;
        }
    }

    private void Clear()
    {
        identity = null;
        ClearAttempt();
        lastKnownRoute = OverlayGameState.Unknown;
    }

    private void ClearAttempt()
    {
        lastValidPerformance = null;
        lastGameplayStats = null;
        lastGameplayPosition = null;
    }

    private static bool TryCreateIdentity(
        GameStateSnapshot game,
        out PerformanceIdentity identity)
    {
        string? process = Normalize(game.ProcessIdentity);
        string? path = Normalize(game.CurrentBeatmapPath);
        string? beatmapId = Normalize(game.CurrentBeatmapId);
        string? beatmapSetId = Normalize(game.CurrentBeatmapSetId);
        if (!game.IsAvailable || process is null ||
            path is null && beatmapId is null && beatmapSetId is null)
        {
            identity = default;
            return false;
        }

        identity = new(process, path, beatmapId, beatmapSetId);
        return true;
    }

    private static bool StatsMatchIdentity(
        LiveGameplayStatsSnapshot? stats,
        PerformanceIdentity identity) =>
        stats is not null &&
        string.Equals(stats.ProcessIdentity, identity.ProcessIdentity, StringComparison.Ordinal) &&
        Compatible(stats.BeatmapId, identity.BeatmapId) &&
        Compatible(stats.BeatmapSetId, identity.BeatmapSetId);

    private static bool IsRetry(
        LiveGameplayStatsSnapshot? previous,
        LiveGameplayStatsSnapshot? current)
    {
        if (previous is null || current is null)
            return false;

        return current.CurrentTime + RetryRewindTolerance < previous.CurrentTime ||
            JudgedObjects(current) < JudgedObjects(previous) ||
            current.Score is { } currentScore &&
            previous.Score is { } previousScore &&
            currentScore < previousScore;
    }

    private static bool IsRewind(TimeSpan? previous, TimeSpan? current) =>
        previous is { } before && current is { } now &&
        now + RetryRewindTolerance < before;

    private static long JudgedObjects(LiveGameplayStatsSnapshot stats) =>
        (long)stats.Count300 + stats.Count100 + stats.Count50 + stats.CountMiss;

    private static bool Compatible(string? left, string? right) =>
        string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right) ||
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private readonly record struct PerformanceIdentity(
        string ProcessIdentity,
        string? BeatmapPath,
        string? BeatmapId,
        string? BeatmapSetId)
    {
        public bool Matches(PerformanceIdentity other) =>
            string.Equals(ProcessIdentity, other.ProcessIdentity, StringComparison.Ordinal) &&
            string.Equals(BeatmapPath, other.BeatmapPath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(BeatmapId, other.BeatmapId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(BeatmapSetId, other.BeatmapSetId, StringComparison.OrdinalIgnoreCase);
    }
}
