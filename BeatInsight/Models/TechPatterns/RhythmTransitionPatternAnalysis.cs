namespace BeatInsight.Models.TechPatterns;

public sealed record RhythmTransitionPatternOptions(
    int RhythmWindowSize = 2,
    double MinimumAbsoluteIntervalChange = 20,
    double MinimumRelativeIntervalChange = 0.25,
    double MinimumRhythmSideConsistency = 0.75,
    double MaximumRhythmInterval = 1000);

public enum RhythmTransitionDirection
{
    Faster,
    Slower
}

/// <summary>One local pivot comparing start-time intervals on each side.</summary>
public sealed record RhythmTransitionPatternOccurrence(
    int PivotIndex,
    int StartIndex,
    int EndIndex,
    int StartTime,
    int EndTime,
    IReadOnlyList<double> BeforeIntervals,
    IReadOnlyList<double> AfterIntervals,
    double? BeforeAverageInterval,
    double? AfterAverageInterval,
    double? BeforeConsistency,
    double? AfterConsistency,
    double? AbsoluteIntervalChange,
    double? RelativeIntervalChange,
    RhythmTransitionDirection? Direction,
    double RhythmTransitionScore,
    bool Qualified,
    string Reason);

/// <summary>
/// Descriptive local rhythm-regime transitions. Intervals always use object start times;
/// slider duration, nominal BPM, geometry and object type do not contribute.
/// </summary>
public sealed record RhythmTransitionPatternAnalysis(
    int TotalObjects,
    int RhythmTransitionCount,
    int RhythmTransitionObjectCount,
    int FasterTransitionCount,
    int SlowerTransitionCount,
    double AverageAbsoluteIntervalChange,
    double AverageRelativeIntervalChange,
    double AverageBeforeConsistency,
    double AverageAfterConsistency,
    double AverageTransitionScore,
    double MaximumTransitionScore,
    double Confidence,
    IReadOnlyList<RhythmTransitionPatternOccurrence> Occurrences)
{
    public IReadOnlyList<RhythmTransitionPatternOccurrence> Transitions =>
        Occurrences.Where(occurrence => occurrence.Qualified).ToArray();

    public double Coverage => TotalObjects == 0
        ? 0
        : (double)RhythmTransitionObjectCount / TotalObjects;
}
