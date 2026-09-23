using Xunit;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechPatternRecoveryV1DiagnosticAuditTests(ITestOutputHelper output)
{
    [Fact]
    public void RecoveryV1_Diagnostic_Investigation()
    {
        var result = TechPatternRecoveryV1DiagnosticAudit.Run();
        output.WriteLine(result);
        Assert.Contains("DIAGNOSTIC", result);
    }
}
