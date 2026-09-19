using BeatInsight.Models.Fusion;
using BeatInsight.Models.SkillPressure;

namespace BeatInsight.Models.Sections;

public sealed record SectionAnalysisV2Options(
    double SectionDurationMilliseconds = 5_000,
    double SpikeMinimumDelta = 15,
    double SpikeMinimumRatio = 1.20,
    double SpikeMinimumDifficulty = 40,
    int SpikeNeighborRadius = 2,
    double MinimumTransitionVectorDistance = 0.20,
    double FlickerProtectionVectorDistance = 0.35);

public sealed record SectionLocalSkills(
    double Aim,
    double Speed,
    double Reading,
    double Tech);

public sealed record SectionPatternMetricV2(
    int OccurrenceCount,
    int ObjectCount,
    double Coverage,
    double Confidence,
    double? Intensity);

public sealed record SectionPatternFamilyV2(
    int OccurrenceCount,
    double Coverage,
    double Support,
    IReadOnlyDictionary<string, SectionPatternMetricV2> Patterns)
{
    public static SectionPatternFamilyV2 Empty { get; } = new(
        0,
        0,
        0,
        new Dictionary<string, SectionPatternMetricV2>());
}

public sealed record SectionDifficultySpike(
    bool IsSpike,
    double NeighborBaseline,
    double Delta,
    double Ratio,
    double Strength,
    IReadOnlyList<string> Contributors)
{
    public static SectionDifficultySpike None { get; } = new(
        false, 0, 0, 0, 0, Array.Empty<string>());
}

public sealed record SectionGameplayTransition(
    bool IsTransition,
    GameplayStructureKind? PreviousDominantStructure,
    GameplayStructureKind CurrentDominantStructure,
    double StructureVectorDistance,
    double SkillDelta,
    double PatternDelta,
    double Strength)
{
    public static SectionGameplayTransition First(GameplayStructureKind current) =>
        new(false, null, current, 0, 0, 0, 0);
}

public sealed record GameplaySectionV2Analysis(
    int SectionIndex,
    double StartTime,
    double EndTime,
    IReadOnlyList<int> ObjectIndices,
    int CircleCount,
    int SliderCount,
    int SpinnerCount,
    int OtherObjectCount,
    double ObjectsPerSecond,
    double JumpScore,
    double StreamScore,
    double TechScore,
    double BurstScore,
    GameplayStructureKind DominantStructure,
    GameplayStructureKind? SecondaryStructure,
    SectionLocalSkills BaseSkills,
    SkillPressureProfile SkillPressure,
    SectionPatternFamilyV2 JumpPatterns,
    SectionPatternFamilyV2 StreamPatterns,
    SectionPatternFamilyV2 TechPatterns,
    double LocalDifficultyScore,
    double LocalDifficultyConfidence,
    SectionDifficultySpike Spike,
    SectionGameplayTransition Transition,
    string Summary)
{
    public double Duration => Math.Max(0, EndTime - StartTime);
    public int ObjectCount => ObjectIndices.Count;
}

public sealed record SectionPatternTimelinePoint(
    double Jump,
    double Stream,
    double Tech);

public sealed record SectionSkillTimelinePoint(
    double Aim,
    double Speed,
    double Reading,
    double Tech,
    double? Stamina,
    double? FingerControl,
    double? BurstControl,
    double? AimControl,
    double? ReadingPressure,
    double? RhythmComplexity);

public sealed record SectionAnalysisResult(
    IReadOnlyList<GameplaySectionV2Analysis> Sections,
    double AverageLocalDifficulty,
    double MaximumLocalDifficulty,
    int? MaximumDifficultySectionIndex,
    int DifficultySpikeCount,
    int GameplayTransitionCount,
    IReadOnlyList<GameplayStructureKind> DominantStructureTimeline,
    IReadOnlyList<double> LocalDifficultyTimeline,
    IReadOnlyList<SectionPatternTimelinePoint> PatternTimeline,
    IReadOnlyList<SectionSkillTimelinePoint> SkillTimeline,
    double GlobalConfidence,
    TimeSpan AnalysisDuration)
{
    public int SectionCount => Sections.Count;

    public static SectionAnalysisResult Empty(TimeSpan duration) => new(
        Array.Empty<GameplaySectionV2Analysis>(),
        0,
        0,
        null,
        0,
        0,
        Array.Empty<GameplayStructureKind>(),
        Array.Empty<double>(),
        Array.Empty<SectionPatternTimelinePoint>(),
        Array.Empty<SectionSkillTimelinePoint>(),
        0,
        duration);
}
