using System.Globalization;
using System.Text.RegularExpressions;
using BeatInsight.Models.Fusion;
using BeatInsight.Models.Identity;

namespace BeatInsight.ViewModels;

public sealed class PatternDisplayItemViewModel
{
    public PatternDisplayItemViewModel(GameplayIdentityV3Pattern pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        Name = FormatName(pattern.Name, pattern.Family);
        Family = pattern.Family;
        Coverage = IsPositiveFinite(pattern.Coverage) ? pattern.Coverage : null;
        Confidence = IsPositiveFinite(pattern.Confidence) ? pattern.Confidence : null;
        Intensity = pattern.Intensity is double intensity && IsPositiveFinite(intensity)
            ? intensity
            : null;
        Score = IsPositiveFinite(pattern.Score) ? pattern.Score : null;
    }

    public string Name { get; }
    public FusionPatternFamily Family { get; }
    public double? Coverage { get; }
    public double? Confidence { get; }
    public double? Intensity { get; }
    public double? Score { get; }
    public bool HasCoverage => Coverage.HasValue;
    public bool HasConfidence => Confidence.HasValue;
    public bool HasIntensity => Intensity.HasValue;
    public bool IsDetected => HasCoverage || HasConfidence || HasIntensity || Score.HasValue;
    public double NumericSortValue => Coverage ?? 0;
    public string CoverageDisplay => Coverage.HasValue
        ? $"Coverage {(Coverage.Value * 100).ToString("F1", CultureInfo.InvariantCulture)}%"
        : string.Empty;
    public string ConfidenceDisplay => Confidence.HasValue
        ? $"Confidence {Confidence.Value.ToString("F2", CultureInfo.InvariantCulture)}"
        : string.Empty;
    public string IntensityDisplay => Intensity.HasValue
        ? $"Intensity {Intensity.Value.ToString("F2", CultureInfo.InvariantCulture)}"
        : string.Empty;
    public string DominantValueDisplay => Coverage.HasValue
        ? $"{(Coverage.Value * 100).ToString("F1", CultureInfo.InvariantCulture)}%"
        : string.Empty;

    private static bool IsPositiveFinite(double value) =>
        double.IsFinite(value) && value > 0;

    private static string FormatName(string name, FusionPatternFamily family)
    {
        string normalized = name switch
        {
            "OneTwo" => "1-2",
            "BackAndForth" => "Back and Forth",
            "SpatialAwkward" => "Spatial / Awkward",
            _ => Regex.Replace(name ?? string.Empty, "(?<=[a-z0-9])(?=[A-Z])", " ")
        };

        if (family == FusionPatternFamily.Jump
            && !normalized.EndsWith("Jump", StringComparison.OrdinalIgnoreCase)
            && normalized.Length > 0)
        {
            normalized += " Jump";
        }

        return normalized;
    }
}
