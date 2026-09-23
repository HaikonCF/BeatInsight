using Xunit;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechV2SliderDensityCalibrationAuditTests(ITestOutputHelper output)
{
    [Fact]
    public void RunSliderDensityCalibrationAudit()
    {
        string report = TechV2SliderDensityCalibrationAudit.RunAudit();
        output.WriteLine(report);
        Assert.NotNull(report);
    }
}
