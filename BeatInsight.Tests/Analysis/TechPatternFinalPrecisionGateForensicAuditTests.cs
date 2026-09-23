using Xunit;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechPatternFinalPrecisionGateForensicAuditTests(ITestOutputHelper output)
{
    [Fact]
    public void FinalPrecisionGate_BaselineVsRecovery_ForensicComparison()
    {
        var result = TechPatternFinalPrecisionGateForensicAudit.Run();
        output.WriteLine(result);
        Assert.Contains("FORENSIC AUDIT", result);
    }
}
