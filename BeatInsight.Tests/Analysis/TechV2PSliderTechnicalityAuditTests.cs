using Xunit;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechV2PSliderTechnicalityAuditTests(ITestOutputHelper output)
{
    [Fact]
    public void RunPSliderTechnicalityAudit()
    {
        string report = TechV2PSliderTechnicalityAudit.RunFullAudit();
        output.WriteLine(report);
        Assert.NotNull(report);
    }
}
