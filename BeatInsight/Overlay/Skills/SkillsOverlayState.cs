using BeatInsight.Models.SkillPressure;

namespace BeatInsight.Overlay.Skills;

public sealed record SkillsOverlayMetricState(
    string Name,
    double? Value);

public sealed record SkillsOverlayState(
    bool HasContent,
    IReadOnlyList<SkillsOverlayMetricState> Metrics)
{
    public static SkillsOverlayState Empty { get; } = new(false, []);
}

public static class SkillsSnapshotProjector
{
    public static SkillsOverlayState Project(OverlaySnapshot? snapshot)
    {
        if (snapshot is not { HasAvailableGame: true, Analysis: { } analysis })
        {
            return SkillsOverlayState.Empty;
        }

        var gameplay = analysis.GameplayProfile;
        var skillPressure = analysis.SkillPressureV2;

        return new SkillsOverlayState(
            true,
            [
                new("Aim", Skill(skillPressure?.AimControl, gameplay?.AimScore)),
                new("Speed", Percent(analysis.GlobalTripletSpeed?.SpeedPercent)),
                new("Reading", Skill(skillPressure?.ReadingPressure, gameplay?.ReadingScore)),
                new("Tech", Percent(gameplay?.TechScore)),
                new("Jump", Ratio(gameplay?.JumpRatio)),
                new("Stream", Ratio(gameplay?.StreamRatio)),
                new("Burst", Ratio(gameplay?.BurstRatio)),
                new("Stamina", Skill(skillPressure?.Stamina, null)),
                new("Local Speed Pressure", Percent(analysis.LocalSpeedPressure?.PressurePercent))
            ]);
    }

    private static double? Skill(SkillPressureMetric? metric, double? fallback)
    {
        double? score = metric is { Available: true, Score: { } value }
            ? value
            : null;
        return Percent(score) ?? Percent(fallback);
    }

    private static double? Ratio(double? value) =>
        value is >= 0 and <= 1 && double.IsFinite(value.Value)
            ? value.Value * 100
            : null;

    private static double? Percent(double? value) =>
        value is >= 0 and <= 100 && double.IsFinite(value.Value)
            ? value
            : null;
}
