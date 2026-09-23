using BeatInsight.Analysis;
using BeatInsight.Services.Persistence;
using Microsoft.Data.Sqlite;
using System.IO;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

/// <summary>Phase 2.11 synthetic checks for region-only anchor propagation.</summary>
public sealed class TechV2AnchorPropagationTests
{
    [Fact]
    public void BuildPresence_OneStrongWithManySupports_LimitsToOneImmediateSupportPerSide()
    {
        TechV2WindowDecision[] decisions =
        [
            Support(0), Support(4), Strong(8), Support(12), Support(16),
        ];

        TechV2PresenceResult result = TechV2ExperimentalDecisionEngine.BuildPresence(decisions, 0, 2_000);
        TechV2Region region = Assert.Single(result.Regions);

        Assert.Equal(3, region.WindowCount);
        Assert.Equal(1, region.SupportAbsorbedBefore);
        Assert.Equal(1, region.SupportAbsorbedAfter);
        Assert.True(region.AnchorPropagationLimited);
        Assert.False(region.LongPropagationAllowed);
    }

    [Fact]
    public void BuildPresence_TwoStrongWithSupports_AllowsLongPropagation()
    {
        TechV2WindowDecision[] decisions =
        [
            Support(0), Strong(4), Support(8), Support(12), Strong(16), Support(20),
        ];

        TechV2Region region = Assert.Single(TechV2ExperimentalDecisionEngine.BuildPresence(decisions, 0, 3_000).Regions);

        Assert.Equal(decisions.Length, region.WindowCount);
        Assert.Equal(2, region.StrongTechWindowCount);
        Assert.True(region.LongPropagationAllowed);
        Assert.False(region.AnchorPropagationLimited);
    }

    [Fact]
    public void BuildPresence_OneStrongWithSustainedTech_AllowsLongPropagation()
    {
        TechV2WindowDecision[] decisions =
        [
            Support(0), Strong(4), SustainedSupport(8), Support(12), Support(16),
        ];

        TechV2Region region = Assert.Single(TechV2ExperimentalDecisionEngine.BuildPresence(decisions, 0, 3_000).Regions);

        Assert.Equal(decisions.Length, region.WindowCount);
        Assert.Equal(1, region.StrongTechWindowCount);
        Assert.Equal(1, region.SustainedTechWindowCount);
        Assert.True(region.LongPropagationAllowed);
        Assert.False(region.AnchorPropagationLimited);
    }

    [Fact]
    public void BuildPresence_PureSustainedTechRegion_IsUnchanged()
    {
        TechV2WindowDecision[] decisions =
        [SustainedSupport(0), SustainedSupport(4), SustainedSupport(8), SustainedSupport(12)];

        TechV2Region region = Assert.Single(TechV2ExperimentalDecisionEngine.BuildPresence(decisions, 0, 2_000).Regions);

        Assert.Equal(4, region.WindowCount);
        Assert.Equal(0, region.StrongTechWindowCount);
        Assert.Equal(4, region.SustainedTechWindowCount);
        Assert.False(region.AnchorPropagationLimited);
    }

    [Fact]
    public void BuildPresence_DisconnectedSupports_AreNeverAbsorbed()
    {
        TechV2WindowDecision[] decisions = [Strong(0), Support(4), Support(20)];

        TechV2Region region = Assert.Single(TechV2ExperimentalDecisionEngine.BuildPresence(decisions, 0, 3_000).Regions);

        Assert.Equal(2, region.WindowCount);
        Assert.Equal(1, region.SupportAbsorbedAfter);
        Assert.Equal(200, region.EndTime);
    }

    [Fact]
    public void BuildPresence_DoesNotMutateStrongTechDecisions_AndIsDeterministic()
    {
        TechV2WindowDecision[] decisions = [Support(0), Strong(4), Support(8), Support(12)];
        TechV2WindowDecision[] snapshot = decisions.ToArray();

        TechV2PresenceResult first = TechV2ExperimentalDecisionEngine.BuildPresence(decisions, 0, 2_000);
        TechV2PresenceResult second = TechV2ExperimentalDecisionEngine.BuildPresence(decisions, 0, 2_000);

        Assert.Equal(snapshot, decisions);
        Assert.Equal(first.Regions, second.Regions);
        Assert.Equal(first.PresencePercent, second.PresencePercent);
        Assert.True(decisions.Single(decision => decision.IsStrongTech).IsStrongTech);
    }

    private static TechV2WindowDecision Strong(int startIndex) => Decision(startIndex, true, false, false);
    private static TechV2WindowDecision Support(int startIndex) => Decision(startIndex, false, true, false);
    private static TechV2WindowDecision SustainedSupport(int startIndex) => Decision(startIndex, false, true, true);

    private static TechV2WindowDecision Decision(int startIndex, bool strong, bool support, bool sustained) => new(
        startIndex,
        startIndex + 11,
        startIndex * 25,
        (startIndex * 25) + 100,
        strong,
        .5,
        .5,
        .5,
        .2,
        strong ? 2 : 0,
        .425,
        support,
        sustained);
}

public sealed class TechV2AnchorPropagationFixtureAuditTests(ITestOutputHelper output)
{
    [Fact]
    public void Run_ReportsHumanFixturesWithUnchangedLocalDecisionCounts()
    {
        string source = MlDatasetSampleRepository.DefaultDatabasePath;
        if (!File.Exists(source))
        {
            output.WriteLine("Local dataset cache unavailable; no human fixture report generated.");
            return;
        }

        string copy = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase211-{Guid.NewGuid():N}.db");
        File.Copy(source, copy);
        TechV2AnchorPropagationFixtureReport report;
        try
        {
            report = TechV2AnchorPropagationFixtureAudit.Run(copy);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(copy);
        }

        output.WriteLine(TechV2AnchorPropagationFixtureAudit.FormatReport(report));
        Assert.Equal(TechV2HumanGroundedStrongTechAudit.References.Count, report.Maps.Count);
        Assert.All(report.Maps.Where(map => map.Resolved), map =>
        {
            Assert.InRange(map.Previous.PresencePercent, 0, 100);
            Assert.InRange(map.Current.PresencePercent, 0, 100);
            Assert.True(map.StrongTechCount >= 0);
            Assert.True(map.TechSupportCount >= 0);
            Assert.True(map.SustainedTechCount >= 0);
        });
    }
}
