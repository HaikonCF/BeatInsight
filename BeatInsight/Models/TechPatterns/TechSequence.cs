using System.Numerics;
namespace BeatInsight.Models.TechPatterns;
public sealed record SliderContext(string SliderType, double SliderLength, int SliderSlides, int SliderControlPointCount, double EntryDistance, double ExitDistance, double EntryInterval, double ExitInterval, double ExitTurnAngle, bool HasReliableEndPosition, double SliderInitialDirection = 0, double EntryTurnAngle = 0, bool HasReliableStartDirection = false);
public sealed record TechSequence(int StartIndex, int EndIndex, double StartTime, double EndTime, IReadOnlyList<Vector2> Positions, IReadOnlyList<double> Distances, IReadOnlyList<double> Angles, IReadOnlyList<double> Intervals, IReadOnlyList<bool> SliderFlags, SliderContext? SliderContext = null);
