using BeatInsight.Analysis.Speed;
using BeatInsight.Models;
using BeatInsight.Models.Speed;

namespace BeatInsight.Tests.Analysis;

public sealed class LocalSpeedPressureAnalyzerTests
{
    [Fact]
    public void NoQualifyingFastPassageReturnsZero()
    {
        LocalSpeedPressureAnalysis result = Analyze(Times(3, 180));

        Assert.Equal(0, result.PressurePercent);
        Assert.Equal(0, result.QualifyingTripletCount);
        Assert.Equal(0, result.QualifyingSectionCount);
        Assert.Empty(result.Sections);
    }

    [Fact]
    public void OneIsolatedSeventyMillisecondTripletIsWeightedAsSingleSection() =>
        AssertPressure(Analyze(Times(3, 70)), 16.92, 1, 1);

    [Fact]
    public void ThreeSeparatedThreeObjectBurstsFillAllThreeWeights() =>
        AssertPressure(Analyze(SeparatedPassages(3, 3, 70)), 19.91, 3, 3);

    [Fact]
    public void OneFourObjectFastBurstHasTwoConsecutiveTriplets()
    {
        LocalSpeedPressureAnalysis result = Analyze(Times(4, 70));

        AssertPressure(result, 30.10, 2, 1);
        LocalSpeedPressureSection section = Assert.Single(result.Sections);
        Assert.Equal((0, 3), (section.StartObjectIndex, section.EndObjectIndex));
        Assert.Equal(4, section.ParticipatingObjectCount);
        Assert.Equal((0d, 210d, 210d), (section.StartTime, section.EndTime, section.Duration));
        Assert.Equal(90, section.Intensity, 10);
        Assert.Equal(1 - Math.Exp(-2d / 4), section.Persistence, 10);
        Assert.Equal(section.Intensity * section.Persistence, section.SectionPressure, 10);
    }

    [Fact]
    public void ThreeSeparatedFourObjectBurstsFillAllThreeWeights() =>
        AssertPressure(Analyze(SeparatedPassages(3, 4, 70)), 35.41, 6, 3);

    [Fact]
    public void OneSustainedTwentyObjectFastPassageUsesPersistenceWithoutMapDilution() =>
        AssertPressure(Analyze(Times(20, 70)), 75.65, 18, 1);

    [Fact]
    public void TwoSustainedTwentyObjectFastPassagesUseTopTwoWeights() =>
        AssertPressure(Analyze(SeparatedPassages(2, 20, 70)), 84.55, 36, 2);

    [Fact]
    public void ModerateOneHundredEightyMillisecondSequenceDoesNotQualify() =>
        AssertPressure(Analyze(Times(20, 180)), 0, 0, 0);

    [Fact]
    public void SameFastPassageIsIndependentOfTrailingSlowMapLength()
    {
        LocalSpeedPressureAnalysis shortMap = Analyze(FastPassageWithSlowTail(100));
        LocalSpeedPressureAnalysis longMap = Analyze(FastPassageWithSlowTail(1000));

        AssertPressure(shortMap, 59.43, 6, 1);
        AssertPressure(longMap, 59.43, 6, 1);
        Assert.Equal(shortMap.PressurePercent, longMap.PressurePercent, 10);
    }

    [Fact]
    public void CircleAndSliderStartsUseTheSameTemporalSemantics()
    {
        HitObject[] circles = Objects(Times(4, 70));
        HitObject[] mixed = Objects(Times(4, 70));
        mixed[1].Type = 2;
        mixed[2].Type = 2;

        Assert.Equal(Analyze(circles).PressurePercent, Analyze(mixed).PressurePercent, 10);
        Assert.Equal(2, Analyze(mixed).QualifyingTripletCount);
    }

    [Fact]
    public void SpinnerAndInvalidTimingUseGlobalSpeedExclusions()
    {
        HitObject[] spinner = Objects(0, 70, 140);
        spinner[1].Type = 8;

        Assert.Equal(0, Analyze(spinner).PressurePercent);
        Assert.Equal(0, Analyze(Objects(0, 0, 70)).PressurePercent);
    }

    private static void AssertPressure(
        LocalSpeedPressureAnalysis result,
        double expected,
        int triplets,
        int sections)
    {
        Assert.InRange(result.PressurePercent, expected - 0.01, expected + 0.01);
        Assert.Equal(triplets, result.QualifyingTripletCount);
        Assert.Equal(sections, result.QualifyingSectionCount);
        Assert.Equal(sections, result.Sections.Count);
    }

    private static int[] Times(int objectCount, int interval) =>
        Enumerable.Range(0, objectCount).Select(index => index * interval).ToArray();

    private static int[] SeparatedPassages(int passageCount, int objectCount, int interval)
    {
        var times = new List<int>();
        int start = 0;
        for (int passage = 0; passage < passageCount; passage++)
        {
            times.AddRange(Enumerable.Range(0, objectCount).Select(index => start + index * interval));
            start = times[^1] + 1000;
        }
        return times.ToArray();
    }

    private static int[] FastPassageWithSlowTail(int slowObjectCount)
    {
        var times = Times(8, 70).ToList();
        int next = times[^1] + 1000;
        for (int index = 0; index < slowObjectCount; index++, next += 1000)
            times.Add(next);
        return times.ToArray();
    }

    private static LocalSpeedPressureAnalysis Analyze(params int[] times) => Analyze(Objects(times));
    private static LocalSpeedPressureAnalysis Analyze(IReadOnlyList<HitObject> objects) =>
        LocalSpeedPressureAnalyzer.Analyze(objects);
    private static HitObject[] Objects(params int[] times) =>
        times.Select(time => new HitObject { Time = time, Type = 1 }).ToArray();
}
