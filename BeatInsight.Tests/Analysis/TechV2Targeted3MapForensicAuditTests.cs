using Xunit;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechV2Targeted3MapForensicAuditTests(ITestOutputHelper output)
{
    [Fact]
    public void RunTargeted3MapForensicAudit()
    {
        string report = TechV2Targeted3MapForensicAudit.RunFullAudit();
        output.WriteLine(report);
        Assert.NotNull(report);
    }
}
