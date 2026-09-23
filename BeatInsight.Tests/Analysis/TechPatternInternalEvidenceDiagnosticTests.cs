using Xunit;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechPatternInternalEvidenceDiagnosticTests(ITestOutputHelper output)
{
    [Fact]
    public void InternalEvidence_Diagnostic()
    {
        var result = TechPatternInternalEvidenceDiagnostic.Run();
        output.WriteLine(result);
        Assert.Contains("DIAGNOSTIC", result);
    }
}
