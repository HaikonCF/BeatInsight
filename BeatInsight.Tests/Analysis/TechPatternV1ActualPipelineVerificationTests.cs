using Xunit;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechPatternV1ActualPipelineVerificationTests(ITestOutputHelper output)
{
    [Fact]
    public void V1ActualPipeline_Verification()
    {
        var result = TechPatternV1ActualPipelineVerification.Run();
        output.WriteLine(result);
        Assert.Contains("ACTUAL_V1_PIPELINE_HAS_INTERNAL_EVIDENCE", result);
    }
}
