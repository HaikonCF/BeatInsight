using Xunit;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechV2SliderLoadCalibrationAuditTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData((int)LoadAggregator.LoadA_Sum2, 0.75)]
    [InlineData((int)LoadAggregator.LoadB_Sum3, 0.50)]
    public void TechnicalSliderLoad_UsesConfiguredNormalization(int aggregatorValue, double expected)
    {
        var aggregator = (LoadAggregator)aggregatorValue;
        double actual = TechV2SliderLoadCalibrationAudit.ComputeTechnicalSliderLoad([0.5, 1.0], aggregator);
        Assert.Equal(expected, actual, 6);
    }

    [Fact]
    public void TechnicalSliderLoad_WeightedTop3_IsBoundedAndDeterministic()
    {
        double actual = TechV2SliderLoadCalibrationAudit.ComputeTechnicalSliderLoad([0.9, 0.8, 0.7, 0.1], LoadAggregator.LoadC_WeightedTop3);
        Assert.Equal(1.0, actual, 6);
        Assert.Equal(actual, TechV2SliderLoadCalibrationAudit.ComputeTechnicalSliderLoad([0.9, 0.8, 0.7, 0.1], LoadAggregator.LoadC_WeightedTop3));
    }

    [Fact]
    public void RunPhase232SliderLoadCalibrationAudit()
    {
        string report = TechV2SliderLoadCalibrationAudit.RunFullAudit();
        output.WriteLine(report);
        Assert.Contains("PHASE 2.32", report);
        Assert.NotEmpty(report);
    }
}
