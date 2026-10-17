using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using BeatInsight.Models.Fusion;
using BeatInsight.Models.Sections;

namespace BeatInsight.Integration.Game;

public sealed record LivePatternItem(
    string AnalyzerName,
    string DisplayName,
    FusionPatternFamily Family,
    double Coverage,
    double Strength);

public sealed record LivePatternsSnapshot(
    GameplaySectionV2Analysis Section,
    IReadOnlyList<LivePatternItem> Patterns);

/// <summary>
/// Projects analyzer-owned section metrics into the compact live-pattern view.
/// Results are cached by analyzer section identity so gameplay ticks within the
/// same section reuse the exact same immutable snapshot and collection.
/// </summary>
public sealed class LivePatternsResolver
{
    private const double MinimumStrength = 40;
    private const double RelativeStrength = .55;
    private const int MaximumPatterns = 3;
    private readonly ConditionalWeakTable<GameplaySectionV2Analysis, LivePatternsSnapshot> cache = new();

    public LivePatternsSnapshot? Resolve(CurrentSectionSnapshot? currentSection) =>
        currentSection is null
            ? null
            : cache.GetValue(currentSection.Section, Build);

    private static LivePatternsSnapshot Build(GameplaySectionV2Analysis section)
    {
        LivePatternItem[] eligible = Enumerate(section)
            .Where(item => double.IsFinite(item.Metric.Coverage) && item.Metric.Coverage > 0)
            .Where(item => !string.Equals(item.Name, "SpacingTrend", StringComparison.Ordinal))
            .Select(item => new LivePatternItem(
                item.Name,
                LivePatternDisplayNames.Format(item.Name, item.Family),
                item.Family,
                Math.Clamp(item.Metric.Coverage, 0, 1),
                PatternStrength(item.Metric) * 100))
            .ToArray();

        double strongest = eligible.Select(item => item.Strength).DefaultIfEmpty().Max();
        double cutoff = Math.Max(MinimumStrength, strongest * RelativeStrength);
        LivePatternItem[] selected = eligible
            .Where(item => item.Strength >= cutoff)
            .OrderByDescending(item => item.Strength)
            .ThenBy(item => item.AnalyzerName, StringComparer.Ordinal)
            .ThenBy(item => item.Family)
            .Take(MaximumPatterns)
            .ToArray();

        return new(section, new ReadOnlyCollection<LivePatternItem>(selected));
    }

    private static IEnumerable<(string Name, FusionPatternFamily Family, SectionPatternMetricV2 Metric)> Enumerate(
        GameplaySectionV2Analysis section)
    {
        switch (section.DominantStructure)
        {
            case GameplayStructureKind.Jump:
                foreach (var item in EnumerateFamily(section.JumpPatterns, FusionPatternFamily.Jump))
                    yield return item;
                break;
            case GameplayStructureKind.Stream:
                foreach (var item in EnumerateFamily(section.StreamPatterns, FusionPatternFamily.Stream))
                    yield return item;
                break;
            case GameplayStructureKind.Tech:
                foreach (var item in EnumerateFamily(section.TechPatterns, FusionPatternFamily.Tech))
                    yield return item;
                break;
            case GameplayStructureKind.ClassicMixed:
                foreach (var item in EnumerateFamily(section.JumpPatterns, FusionPatternFamily.Jump))
                    yield return item;
                foreach (var item in EnumerateFamily(section.StreamPatterns, FusionPatternFamily.Stream))
                    yield return item;
                foreach (var item in EnumerateFamily(section.TechPatterns, FusionPatternFamily.Tech))
                    yield return item;
                break;
        }
    }

    private static IEnumerable<(string Name, FusionPatternFamily Family, SectionPatternMetricV2 Metric)> EnumerateFamily(
        SectionPatternFamilyV2 patterns,
        FusionPatternFamily family)
    {
        foreach ((string name, SectionPatternMetricV2 metric) in patterns.Patterns)
            yield return (name, family, metric);
    }

    // Exact Section Analysis V2 pattern-strength weighting. Kept here because
    // the analyzer implementation is intentionally frozen for this pass.
    private static double PatternStrength(SectionPatternMetricV2 metric)
    {
        double numerator = .50 * Signal(metric.Coverage) + .30 * Signal(metric.Confidence);
        double denominator = .80;
        if (metric.Intensity.HasValue)
        {
            numerator += .20 * Signal(metric.Intensity.Value);
            denominator = 1;
        }

        return Signal(numerator / denominator);
    }

    private static double Signal(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
}

public static class LivePatternDisplayNames
{
    public static string Format(string analyzerName, FusionPatternFamily family) =>
        (analyzerName, family) switch
        {
            ("OneTwo", FusionPatternFamily.Jump) => "1-2 Jump",
            ("Spaced", FusionPatternFamily.Jump) => "Spaced Jump",
            ("WideAngle", FusionPatternFamily.Jump) => "Wide Angle",
            ("AlternatingSpacing", FusionPatternFamily.Jump) => "Alternating Spacing",
            ("SliderChain", FusionPatternFamily.Tech) => "Slider Chain",
            ("ComplexSlider", FusionPatternFamily.Tech) => "Complex Slider",
            ("SliderDirectionChange", FusionPatternFamily.Tech) => "Slider Direction Change",
            ("CircleSliderTransition", FusionPatternFamily.Tech) => "Circle-Slider Transition",
            ("RhythmTransition", FusionPatternFamily.Tech) => "Rhythm Transition",
            ("SpatialAwkward", FusionPatternFamily.Tech) => "Spatial / Awkward",
            _ => SplitPascalCase(analyzerName)
        };

    private static string SplitPascalCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var result = new System.Text.StringBuilder(value.Length + 8);
        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            if (index > 0 && char.IsUpper(current) && !char.IsUpper(value[index - 1]))
                result.Append(' ');
            result.Append(current);
        }

        return result.ToString();
    }
}
