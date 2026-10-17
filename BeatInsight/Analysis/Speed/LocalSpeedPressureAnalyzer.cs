using BeatInsight.Models;
using BeatInsight.Models.Speed;

namespace BeatInsight.Analysis.Speed;

/// <summary>
/// V0 local speed pressure. Triplet eligibility and LocalSpeed values come
/// directly from the production Global Triplet Speed analyzer.
/// </summary>
public static class LocalSpeedPressureAnalyzer
{
    public const double QualificationThreshold = 50;
    public const double PersistenceScale = 4;
    public const double PrimaryWeight = 0.85;
    public const double SecondaryWeight = 0.10;
    public const double TertiaryWeight = 0.05;

    public static LocalSpeedPressureAnalysis Analyze(Beatmap beatmap)
    {
        ArgumentNullException.ThrowIfNull(beatmap);
        return Analyze(beatmap.HitObjects);
    }

    public static LocalSpeedPressureAnalysis Analyze(IReadOnlyList<HitObject> objects)
    {
        ArgumentNullException.ThrowIfNull(objects);

        GlobalTripletSpeedAnalysis source = GlobalTripletSpeedAnalyzer.Analyze(objects, includeDiagnostics: true);
        GlobalTripletSpeedDiagnostic[] qualifying = source.Diagnostics
            .Where(item => item.Valid && item.LocalSpeedPercent >= QualificationThreshold)
            .OrderBy(item => item.StartIndex)
            .ToArray();

        if (qualifying.Length == 0)
            return LocalSpeedPressureAnalysis.Empty;

        var grouped = new List<List<GlobalTripletSpeedDiagnostic>>();
        foreach (GlobalTripletSpeedDiagnostic diagnostic in qualifying)
        {
            if (grouped.Count == 0 || diagnostic.StartIndex != grouped[^1][^1].StartIndex + 1)
                grouped.Add([]);

            grouped[^1].Add(diagnostic);
        }

        LocalSpeedPressureSection[] sections = grouped
            .Select(group => CreateSection(objects, group))
            .ToArray();
        double[] strongest = sections
            .Select(section => section.SectionPressure)
            .OrderByDescending(value => value)
            .Take(3)
            .ToArray();
        double s1 = strongest.ElementAtOrDefault(0);
        double s2 = strongest.ElementAtOrDefault(1);
        double s3 = strongest.ElementAtOrDefault(2);
        double pressure = Math.Clamp(
            PrimaryWeight * s1 + SecondaryWeight * s2 + TertiaryWeight * s3,
            0,
            100);

        return new LocalSpeedPressureAnalysis(pressure, qualifying.Length, sections.Length, sections);
    }

    private static LocalSpeedPressureSection CreateSection(
        IReadOnlyList<HitObject> objects,
        IReadOnlyList<GlobalTripletSpeedDiagnostic> diagnostics)
    {
        int startIndex = diagnostics[0].StartIndex;
        int endIndex = diagnostics[^1].EndIndex;
        double intensity = diagnostics.Average(item => item.LocalSpeedPercent);
        double persistence = 1 - Math.Exp(-diagnostics.Count / PersistenceScale);

        return new LocalSpeedPressureSection(
            startIndex,
            endIndex,
            objects[startIndex].Time,
            objects[endIndex].Time,
            diagnostics.Count,
            intensity,
            persistence,
            intensity * persistence);
    }
}
