namespace BeatInsight.Diagnostics;

public sealed record SectionAnalysisV2ComponentTiming(
    string Name,
    int CallCount,
    TimeSpan TotalDuration,
    TimeSpan MinimumDuration,
    TimeSpan MaximumDuration,
    long AllocatedBytes);

public sealed record SectionAnalysisV2PerformanceProfile(
    TimeSpan TotalDuration,
    int SectionCount,
    TimeSpan AverageSectionDuration,
    TimeSpan MinimumSectionDuration,
    TimeSpan MaximumSectionDuration,
    long TotalAllocatedBytes,
    long AverageSectionAllocatedBytes,
    long MinimumSectionAllocatedBytes,
    long MaximumSectionAllocatedBytes,
    int LocalBeatmapCount,
    int LocalHitObjectListCount,
    int LocalHitObjectReferenceCount,
    IReadOnlyList<SectionAnalysisV2ComponentTiming> Components);
