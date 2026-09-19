namespace BeatInsight.Analysis;

internal sealed record TechAnnotationRegion(
    double StartTime,
    double EndTime,
    bool IsTech);

internal sealed record TechAnnotationSet(
    bool AnnotationComplete,
    IReadOnlyList<TechAnnotationRegion> Regions);

internal enum TechWindowLabel
{
    Unknown,
    NotTech,
    Tech,
}

internal sealed record TechWindowLabelResult(
    TechWindowLabel Label,
    double TechOverlapRatio,
    bool IsBoundaryWindow);

internal static class TechWindowAnnotationLabeler
{
    internal const double TechOverlapLabelRatio = 0.50;

    internal static TechWindowLabelResult LabelWindow(
        double windowStartTime,
        double windowEndTime,
        TechAnnotationSet annotations)
    {
        ArgumentNullException.ThrowIfNull(annotations);

        if (windowEndTime <= windowStartTime)
            throw new ArgumentOutOfRangeException(
                nameof(windowEndTime),
                "A window must have a strictly positive time range.");

        ValidateRegions(annotations.Regions);

        double duration = windowEndTime - windowStartTime;
        double techOverlapRatio = CalculateUnionOverlapRatio(
            windowStartTime,
            windowEndTime,
            annotations.Regions.Where(region => region.IsTech));

        // A partial overlap is retained for dataset auditing. It is not a
        // runtime threshold and does not participate in feature extraction.
        bool isBoundaryWindow = techOverlapRatio > 0 && techOverlapRatio < 1;

        if (techOverlapRatio >= TechOverlapLabelRatio)
        {
            return new TechWindowLabelResult(
                TechWindowLabel.Tech,
                techOverlapRatio,
                isBoundaryWindow);
        }

        double nonTechOverlapRatio = CalculateUnionOverlapRatio(
            windowStartTime,
            windowEndTime,
            annotations.Regions.Where(region => !region.IsTech));

        if (nonTechOverlapRatio >= TechOverlapLabelRatio
            || annotations.AnnotationComplete)
        {
            return new TechWindowLabelResult(
                TechWindowLabel.NotTech,
                techOverlapRatio,
                isBoundaryWindow);
        }

        return new TechWindowLabelResult(
            TechWindowLabel.Unknown,
            techOverlapRatio,
            isBoundaryWindow);
    }

    private static double CalculateUnionOverlapRatio(
        double windowStartTime,
        double windowEndTime,
        IEnumerable<TechAnnotationRegion> regions)
    {
        List<(double Start, double End)> overlaps = regions
            .Select(region => (
                Start: Math.Max(windowStartTime, region.StartTime),
                End: Math.Min(windowEndTime, region.EndTime)))
            .Where(overlap => overlap.End > overlap.Start)
            .OrderBy(overlap => overlap.Start)
            .ToList();

        if (overlaps.Count == 0)
            return 0;

        double totalOverlap = 0;
        (double Start, double End) current = overlaps[0];

        foreach ((double Start, double End) next in overlaps.Skip(1))
        {
            if (next.Start <= current.End)
            {
                current = (current.Start, Math.Max(current.End, next.End));
                continue;
            }

            totalOverlap += current.End - current.Start;
            current = next;
        }

        totalOverlap += current.End - current.Start;

        return Math.Clamp(
            totalOverlap / (windowEndTime - windowStartTime),
            0,
            1);
    }

    private static void ValidateRegions(
        IReadOnlyList<TechAnnotationRegion> regions)
    {
        foreach (TechAnnotationRegion region in regions)
        {
            if (region.EndTime <= region.StartTime)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(regions),
                    "An annotation region must have a strictly positive time range.");
            }
        }
    }
}
