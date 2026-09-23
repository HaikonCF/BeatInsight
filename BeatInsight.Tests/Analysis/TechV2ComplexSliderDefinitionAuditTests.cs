using Xunit;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechV2ComplexSliderDefinitionAuditTests(ITestOutputHelper output)
{
    [Fact]
    public void RunComplexSliderDefinitionAudit()
    {
        string report = TechV2ComplexSliderDefinitionAudit.RunFullAudit();
        output.WriteLine(report);
        Assert.NotNull(report);
    }
}
