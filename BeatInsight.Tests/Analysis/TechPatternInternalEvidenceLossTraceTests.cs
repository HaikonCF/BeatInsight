using Xunit;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechPatternInternalEvidenceLossTraceTests(ITestOutputHelper output)
{
    [Fact]
    public void InternalEvidence_LossTrace()
    {
        var result = TechPatternInternalEvidenceLossTrace.Run();
        output.WriteLine(result);
        Assert.Contains("INTERNAL_EVIDENCE_LOSS_POINT", result);
    }
}
