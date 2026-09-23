using Xunit;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechPatternPhase13ConsolidatedShadowDecisionAuditTests(ITestOutputHelper output)
{
    [Fact]
    public void Phase13_ConsolidatedShadowDecision_Audit()
    {
        var result = TechPatternPhase13ConsolidatedShadowDecisionAudit.Run();
        output.WriteLine(result);
        Assert.Contains("PHASE 13", result);
        Assert.Contains("CONSOLIDATED_DECISION_VIABLE", result);
        Assert.Contains("EXPECTED_TECH_RATE", result);
        Assert.Contains("MIXED_RATE", result);
        Assert.Contains("HARD_NEGATIVE_RATE", result);
        Assert.Contains("READY_FOR_TEMPORAL_TECH_PRESENCE_PROTOTYPE", result);
    }
}
