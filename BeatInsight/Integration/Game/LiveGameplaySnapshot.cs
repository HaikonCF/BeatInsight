using System.Globalization;
using BeatInsight.Models.AnalysisCache;
using BeatInsight.Models.Sections;

namespace BeatInsight.Integration.Game;

/// <summary>
/// Provider-neutral live view of one already analyzed beatmap. A snapshot only
/// exists while an active gameplay clock and the selected difficulty agree.
/// </summary>
public sealed record LiveGameplaySnapshot(
    bool IsGameplay,
    TimeSpan CurrentTime,
    TimeSpan BeatmapDuration,
    double Progress,
    BeatmapAnalysisSnapshot BeatmapAnalysis,
    int BeatmapId,
    int BeatmapSetId,
    string ProcessIdentity,
    DateTimeOffset ObservedAtUtc);

internal enum LiveGameplayCompositionFailure
{
    None,
    GameUnavailable,
    GameplayPositionUnavailable,
    AnalysisUnavailable,
    CurrentTimeInvalid,
    GameProcessIdentityMissing,
    PositionProcessIdentityMissing,
    ProcessIdentityMismatch,
    GameBeatmapIdInvalid,
    GameBeatmapSetIdInvalid,
    AnalysisBeatmapIdInvalid,
    AnalysisBeatmapSetIdInvalid,
    BeatmapIdMismatch,
    BeatmapSetIdMismatch,
    BeatmapDurationInvalid,
    SectionsUnavailable,
    TimelineInvalid,
    ProgressInvalid,
    GameStateChangedDuringPositionRead
}

/// <summary>
/// Lightweight composition only. It never parses, analyzes, discovers, or
/// retains a previous result.
/// </summary>
public static class LiveGameplaySnapshotComposer
{
    public static LiveGameplaySnapshot? TryCompose(
        GameStateSnapshot game,
        GameplayPositionSnapshot position,
        BeatmapAnalysisSnapshot? analysis) =>
        TryCompose(game, position, analysis, out _);

    internal static LiveGameplaySnapshot? TryCompose(
        GameStateSnapshot game,
        GameplayPositionSnapshot position,
        BeatmapAnalysisSnapshot? analysis,
        out LiveGameplayCompositionFailure failure)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(position);

        failure = LiveGameplayCompositionFailure.None;
        if (!game.IsAvailable)
            return Fail(LiveGameplayCompositionFailure.GameUnavailable, out failure);
        if (!position.IsGameplay || position.CurrentTime is not { } currentTime)
            return Fail(LiveGameplayCompositionFailure.GameplayPositionUnavailable, out failure);
        if (analysis is null)
            return Fail(LiveGameplayCompositionFailure.AnalysisUnavailable, out failure);
        if (currentTime < TimeSpan.Zero)
            return Fail(LiveGameplayCompositionFailure.CurrentTimeInvalid, out failure);
        if (string.IsNullOrWhiteSpace(game.ProcessIdentity))
            return Fail(LiveGameplayCompositionFailure.GameProcessIdentityMissing, out failure);
        if (string.IsNullOrWhiteSpace(position.ProcessIdentity))
            return Fail(LiveGameplayCompositionFailure.PositionProcessIdentityMissing, out failure);
        if (!string.Equals(
            game.ProcessIdentity,
            position.ProcessIdentity,
            StringComparison.Ordinal))
        {
            return Fail(LiveGameplayCompositionFailure.ProcessIdentityMismatch, out failure);
        }

        if (!TryPositiveId(game.CurrentBeatmapId, out int beatmapId))
            return Fail(LiveGameplayCompositionFailure.GameBeatmapIdInvalid, out failure);
        if (!TryPositiveId(game.CurrentBeatmapSetId, out int beatmapSetId))
            return Fail(LiveGameplayCompositionFailure.GameBeatmapSetIdInvalid, out failure);
        if (analysis.Metadata.BeatmapId is not > 0)
            return Fail(LiveGameplayCompositionFailure.AnalysisBeatmapIdInvalid, out failure);
        if (analysis.Metadata.BeatmapSetId is not > 0)
            return Fail(LiveGameplayCompositionFailure.AnalysisBeatmapSetIdInvalid, out failure);
        if (analysis.Metadata.BeatmapId.Value != beatmapId)
            return Fail(LiveGameplayCompositionFailure.BeatmapIdMismatch, out failure);
        if (analysis.Metadata.BeatmapSetId.Value != beatmapSetId)
            return Fail(LiveGameplayCompositionFailure.BeatmapSetIdMismatch, out failure);
        if (analysis.Metadata.LengthTicks <= 0)
            return Fail(LiveGameplayCompositionFailure.BeatmapDurationInvalid, out failure);

        TimeSpan duration = TimeSpan.FromTicks(analysis.Metadata.LengthTicks);
        IReadOnlyList<GameplaySectionV2Analysis>? sections =
            analysis.SectionAnalysisV2?.Sections;
        if (sections is null || sections.Count == 0 || sections[0] is null)
            return Fail(LiveGameplayCompositionFailure.SectionsUnavailable, out failure);

        double currentMilliseconds = currentTime.TotalMilliseconds;
        double durationMilliseconds = duration.TotalMilliseconds;
        double timelineStartMilliseconds = sections[0].StartTime;
        double timelineEndMilliseconds = timelineStartMilliseconds + durationMilliseconds;
        if (!double.IsFinite(currentMilliseconds) ||
            !double.IsFinite(durationMilliseconds) ||
            !double.IsFinite(timelineStartMilliseconds) ||
            !double.IsFinite(timelineEndMilliseconds) ||
            durationMilliseconds <= 0 ||
            timelineStartMilliseconds < 0 ||
            timelineEndMilliseconds <= timelineStartMilliseconds)
        {
            return Fail(LiveGameplayCompositionFailure.TimelineInvalid, out failure);
        }

        double progress = currentMilliseconds <= timelineStartMilliseconds
            ? 0
            : currentMilliseconds >= timelineEndMilliseconds
                ? 1
                : (currentMilliseconds - timelineStartMilliseconds) / durationMilliseconds;
        if (!double.IsFinite(progress))
            return Fail(LiveGameplayCompositionFailure.ProgressInvalid, out failure);
        progress = Math.Clamp(progress, 0, 1);

        return new(
            true,
            currentTime,
            duration,
            progress,
            analysis,
            beatmapId,
            beatmapSetId,
            game.ProcessIdentity,
            position.ObservedAtUtc);
    }

    private static LiveGameplaySnapshot? Fail(
        LiveGameplayCompositionFailure reason,
        out LiveGameplayCompositionFailure failure)
    {
        failure = reason;
        return null;
    }

    private static bool TryPositiveId(string? value, out int id) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
}
