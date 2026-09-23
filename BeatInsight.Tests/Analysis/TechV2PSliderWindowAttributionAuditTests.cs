using Xunit;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechV2PSliderWindowAttributionAuditTests(ITestOutputHelper output)
{
    [Fact]
    public void RunPSliderWindowAttributionAudit()
    {
        string report = TechV2PSliderWindowAttributionAudit.RunFullAudit();
        output.WriteLine(report);
        Assert.NotNull(report);
    }
}
