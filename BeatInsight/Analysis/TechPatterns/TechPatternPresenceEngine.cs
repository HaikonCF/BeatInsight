using BeatInsight.Models;
using BeatInsight.Models.TechPatterns;
using BeatInsight.Diagnostics;

namespace BeatInsight.Analysis.TechPatterns;

public sealed record TechPatternRegion(double StartTime, double EndTime, IReadOnlyList<TechPatternMotif> Motifs)
{
    public double Duration => Math.Max(0, EndTime - StartTime);
    public IReadOnlyList<TechPatternType> PatternTypes => Motifs.SelectMany(m => m.Matches).Select(m => m.Type).Distinct().Order().ToArray();
    public IReadOnlyList<string> Reasons => Motifs.Select(m => m.ConfirmationReason).Distinct().ToArray();
}

public sealed record TechPatternPresenceResult(IReadOnlyList<TechPatternMatch> RecognizedPatterns,
    IReadOnlyList<TechPatternMotif> Motifs, IReadOnlyList<TechPatternRegion> Regions,
    double TotalTechDuration, double PlayableDuration, double PresenceRatio)
{
    public double PresencePercent => PresenceRatio * 100;
    public int RegionCount => Regions.Count;
    public int ConfirmedMotifCount => Motifs.Count(m => m.Status == PatternEvidenceStatus.Confirmed);
}

public static class TechRegionAssembler
{
    // Only overlap or exact contact. No context propagation and no artificial temporal gap.
    private const double AdjacencyToleranceMilliseconds = 0;

    public static IReadOnlyList<TechPatternRegion> Assemble(IEnumerable<TechPatternMotif> motifs, double start, double end)
    {
        var regions = new List<TechPatternRegion>();
        if (!double.IsFinite(start) || !double.IsFinite(end) || end <= start) return regions;
        foreach (var motif in motifs.Where(m => m.Status == PatternEvidenceStatus.Confirmed)
            .OrderBy(m => m.StartTime).ThenBy(m => m.EndTime))
        {
            if (!double.IsFinite(motif.StartTime) || !double.IsFinite(motif.EndTime)) continue;
            double from = Math.Max(start, motif.StartTime), to = Math.Min(end, motif.EndTime);
            if (to <= from) continue;
            if (regions.Count > 0 && from <= regions[^1].EndTime + AdjacencyToleranceMilliseconds)
            {
                var previous = regions[^1];
                regions[^1] = previous with { EndTime = Math.Max(previous.EndTime, to), Motifs = previous.Motifs.Append(motif).ToArray() };
            }
            else regions.Add(new(from, to, new[] { motif }));
        }
        return regions;
    }
}

/// <summary>Isolated V1 entry point. No Identity, Rating, legacy V2 or ML consumer is changed.</summary>
public static class TechPatternPresenceEngine
{
    private static readonly ITechPatternDetector[] Detectors =
    [
        new TechnicalSliderEntryDetector(), new TechnicalSliderExitDetector(),
        new SliderToSliderTransitionDetector(), new IrregularRhythmTransitionDetector(),
        new DirectionObservationDetector(), new CoupledAimControlDetector(),
    ];

    public static TechPatternPresenceResult Analyze(Beatmap map,
        PatternConfirmationPolicy policy = PatternConfirmationPolicy.BaselineV1)
    {
        var context = new LocalPatternContext(map);
        var matches = Detectors.SelectMany(d => d.Detect(context)).OrderBy(m => m.StartIndex).ThenBy(m => m.Type).ToArray();
        var motifs = PatternEvidenceResolver.Resolve(matches, policy);
        double start = context.Events.FirstOrDefault()?.StartTime ?? 0;
        double end = context.Events.LastOrDefault()?.EffectiveEndTime ?? start;
        double duration = Math.Max(0, end - start);
        var regions = TechRegionAssembler.Assemble(motifs, start, end);
        double tech = regions.Sum(r => r.Duration);
        return new(matches, motifs, regions, tech, duration, duration > 0 ? Math.Clamp(tech / duration, 0, 1) : 0);
    }

    // Explicit opt-in. Analyze itself never writes a log.
    public static void Debug(TechPatternPresenceResult result, bool enabled = false)
    {
        if (!enabled) return;
        foreach (var match in result.RecognizedPatterns)
            DebugLogger.Detailed(FormattableString.Invariant(
                $"TECH PATTERN MATCH | {match.PatternId} | {match.StartTime:F1}-{match.EndTime:F1}ms | Events={string.Join(",", match.SupportingEventIndices)} | Similarity={match.Similarity:F3} | {string.Join("; ", match.Reasons)}"));
        DebugLogger.Detailed($"Pattern counts={string.Join(", ", result.RecognizedPatterns.GroupBy(m => m.Type).Select(g => $"{g.Key}:{g.Count()}"))} | Confirmed={result.ConfirmedMotifCount} | Regions={result.RegionCount} | Presence={result.PresencePercent:F2}%");
    }
}
