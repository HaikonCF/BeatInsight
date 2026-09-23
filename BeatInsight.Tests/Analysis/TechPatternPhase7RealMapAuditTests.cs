using Xunit;
using Xunit.Abstractions;
namespace BeatInsight.Tests.Analysis;
public sealed class TechPatternPhase7RealMapAuditTests(ITestOutputHelper output)
{
    [Fact] public void RunRealMapPatternAudit(){var report=TechPatternPhase7RealMapAudit.Run(); output.WriteLine(report); Assert.Contains("PHASE 7",report);}
}
