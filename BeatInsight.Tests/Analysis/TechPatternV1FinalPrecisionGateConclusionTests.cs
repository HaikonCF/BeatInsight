using Xunit;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechPatternV1FinalPrecisionGateConclusionTests(ITestOutputHelper output)
{
    [Fact]
    public void FinalPrecisionGate_Conclusion()
    {
        var result = TechPatternV1FinalPrecisionGateConclusion.Run();
        output.WriteLine(result);
        Assert.Contains("CONCLUSION", result);
    }
}
