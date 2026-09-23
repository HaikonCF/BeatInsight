using Xunit;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechPatternV1RecoveryAuditCorrectPipelineTests(ITestOutputHelper output)
{
    [Fact]
    public void RecoveryV1_CorrectPipeline_Audit()
    {
        var result = TechPatternV1RecoveryAuditCorrectPipeline.Run();
        output.WriteLine(result);
        Assert.Contains("RECOVERY_V1_SAFE", result);
    }
}
