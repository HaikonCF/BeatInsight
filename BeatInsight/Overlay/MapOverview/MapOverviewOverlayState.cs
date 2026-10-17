namespace BeatInsight.Overlay.MapOverview;

public sealed record MapOverviewOverlayState(
    bool HasContent,
    string? Title,
    string? Difficulty,
    string? Artist,
    string? Mapper,
    double? StarRating,
    double? BeatInsightRating,
    string? GameplayIdentity,
    double? Bpm,
    TimeSpan? Length,
    int? MaxCombo,
    double? ApproachRate,
    double? OverallDifficulty,
    double? CircleSize,
    double? HpDrain,
    int? ObjectCount)
{
    public static MapOverviewOverlayState Empty { get; } = new(
        false, null, null, null, null, null, null, null, null, null,
        null, null, null, null, null, null);
}

public static class MapOverviewSnapshotProjector
{
    public static MapOverviewOverlayState Project(OverlaySnapshot? snapshot)
    {
        if (snapshot is not { HasAvailableGame: true, Analysis: { } analysis } ||
            analysis.Metadata is not { } metadata)
        {
            return MapOverviewOverlayState.Empty;
        }

        double? bpm = ValidPositive(metadata.PrimaryBpm)
            ?? ValidPositive(metadata.Bpm);
        int? maxCombo = Positive(metadata.OsuMaxCombo)
            ?? Positive(metadata.MaxCombo);
        TimeSpan? length = metadata.LengthTicks > 0
            ? TimeSpan.FromTicks(metadata.LengthTicks)
            : null;
        int totalObjects = Math.Max(0, metadata.CircleCount)
            + Math.Max(0, metadata.SliderCount)
            + Math.Max(0, metadata.SpinnerCount);

        return new(
            true,
            Text(metadata.Title),
            Text(metadata.Version),
            Text(metadata.Artist),
            Text(metadata.Creator),
            ValidNonNegative(metadata.OsuStarRating),
            analysis.RatingV2 is { } rating
                ? ValidNonNegative(rating.Score)
                : null,
            analysis.IdentityV3 is { } identity
                ? identity.PrimaryStructure.ToString()
                : null,
            bpm,
            length,
            maxCombo,
            ValidNonNegative(metadata.ApproachRate),
            ValidNonNegative(metadata.OverallDifficulty),
            ValidNonNegative(metadata.CircleSize),
            ValidNonNegative(metadata.HpDrain),
            totalObjects);
    }

    private static string? Text(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static double? ValidPositive(double? value) =>
        value is > 0 && double.IsFinite(value.Value) ? value : null;

    private static double? ValidNonNegative(double value) =>
        value >= 0 && double.IsFinite(value) ? value : null;

    private static int? Positive(int? value) => value is > 0 ? value : null;
}
