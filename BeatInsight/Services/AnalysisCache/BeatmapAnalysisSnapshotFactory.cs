using BeatInsight.Analysis.Identity;
using System.IO;
using BeatInsight.Analysis.Rating;
using BeatInsight.Analysis.Sections;
using BeatInsight.Analysis.SkillPressure;
using BeatInsight.Analysis.Speed;
using BeatInsight.Models;
using BeatInsight.Models.AnalysisCache;
using BeatInsight.Parser;

namespace BeatInsight.Services.AnalysisCache;

/// <summary>Runs the existing frozen analyzers and copies their outputs into a portable snapshot.</summary>
public static class BeatmapAnalysisSnapshotFactory
{
    public static Task<BeatmapAnalysisSnapshot> AnalyzeAsync(string sourcePath, CancellationToken cancellationToken = default) =>
        Task.Run(() => Analyze(sourcePath, cancellationToken), cancellationToken);

    public static BeatmapAnalysisSnapshot Analyze(string sourcePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        cancellationToken.ThrowIfCancellationRequested();
        Beatmap map = BeatmapParser.Load(sourcePath);
        cancellationToken.ThrowIfCancellationRequested();

        GameplayProfile gameplay = map.GameplayProfile;
        var sections = SectionAnalysisV2Analyzer.Analyze(map);
        cancellationToken.ThrowIfCancellationRequested();
        var pressure = SkillPressureAnalyzer.Analyze(map);
        cancellationToken.ThrowIfCancellationRequested();
        var identity = GameplayIdentityV3Analyzer.Analyze(map);
        cancellationToken.ThrowIfCancellationRequested();
        var rating = BeatInsightRatingV2Analyzer.Analyze(map);
        var tripletSpeed = GlobalTripletSpeedAnalyzer.Analyze(map);
        (int? beatmapId, int? beatmapSetId) = ReadStableIds(sourcePath);

        return new BeatmapAnalysisSnapshot(
            new BeatmapMetadataSnapshot(
                beatmapId, beatmapSetId, map.Artist, map.Title, map.Version, map.Creator,
                map.Length.Ticks, map.BPM, map.MaxCombo, map.AR, map.OD, map.CS, map.HP,
                map.CircleCount, map.SliderCount, map.SpinnerCount,
                map.OsuStarRating, map.BeatInsightRating),
            new GameplayProfileSnapshot(
                gameplay.AimScore, gameplay.SpeedScore, gameplay.ReadScore, gameplay.TechScore,
                gameplay.StreamRatio, gameplay.JumpRatio, gameplay.BurstRatio,
                gameplay.TechPresence, gameplay.TechV2Presence, gameplay.ReadCoverage,
                gameplay.SpeedCoverage, gameplay.Identity.Primary, gameplay.Identity.Secondary,
                gameplay.Identity.Pattern, gameplay.Identity.Confidence,
                gameplay.Identity.Traits.ToArray()),
            identity,
            pressure,
            sections,
            rating,
            tripletSpeed,
            identity.MainPatterns.ToArray());
    }

    private static (int? BeatmapId, int? BeatmapSetId) ReadStableIds(string sourcePath)
    {
        int? beatmapId = null, beatmapSetId = null;
        foreach (string line in File.ReadLines(sourcePath))
        {
            if (line.StartsWith("BeatmapID:", StringComparison.Ordinal))
                beatmapId = ParsePositive(line["BeatmapID:".Length..]);
            else if (line.StartsWith("BeatmapSetID:", StringComparison.Ordinal))
                beatmapSetId = ParsePositive(line["BeatmapSetID:".Length..]);
            else if (line.Equals("[HitObjects]", StringComparison.Ordinal))
                break;
        }
        return (beatmapId, beatmapSetId);
    }

    private static int? ParsePositive(string value) =>
        int.TryParse(value.Trim(), out int parsed) && parsed > 0 ? parsed : null;
}
