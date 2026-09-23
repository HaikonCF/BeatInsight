using BeatInsight.Analysis;
using BeatInsight.Models;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechV2TowerForensicAuditTests(ITestOutputHelper output)
{
    [Fact]
    public void ReportFixtures_ReproducesPhase22AndDoesNotMutateDecisions()
    {
        IReadOnlyDictionary<string, TechV2ForensicMapAudit> audits = LoadAudits();

        Assert.All(audits.Values, audit =>
        {
            Assert.InRange(audit.AfterPresence.PresencePercent, 0, 100);
            Assert.Equal(
                audit.StrongTechWindowCount + audit.SustainedTechWindowCount,
                audit.FinalTechWindowCount);
        });

        output.WriteLine(TechV2TowerForensicAudit.FormatReport(audits));
    }

    [Fact]
    public void SustainedRunExtraction_IsDeterministic()
    {
        Beatmap map = FixtureLoader.Load("Tower Of Heaven [Extra].osu");
        TechV2ForensicMapAudit first = TechV2TowerForensicAudit.Analyze("Tower", map);
        TechV2ForensicMapAudit second = TechV2TowerForensicAudit.Analyze("Tower", map);

        Assert.Equal(
            first.SustainedRuns.Select(run =>
                (run.StartTime, run.EndTime, run.WindowCount, run.TechSupportCount)),
            second.SustainedRuns.Select(run =>
                (run.StartTime, run.EndTime, run.WindowCount, run.TechSupportCount)));
        Assert.Equal(
            first.SustainedWindows.Select(window =>
                (window.Decision.StartIndex, window.Decision.EndIndex, window.Decision.RawScore)),
            second.SustainedWindows.Select(window =>
                (window.Decision.StartIndex, window.Decision.EndIndex, window.Decision.RawScore)));
    }

    [Fact]
    public void Distribution_RepetitionAndVariabilityAreDeterministic()
    {
        double[] values = [100, 100, 120, 140, 140];

        TechV2ForensicDistribution first =
            TechV2TowerForensicAudit.Distribution(values, 20);
        TechV2ForensicDistribution second =
            TechV2TowerForensicAudit.Distribution(values, 20);

        Assert.Equal(first, second);
        Assert.Equal(5, first.Count);
        Assert.Equal(0.8, first.RepeatedBinRatio, 6);
        Assert.Equal(0.4, first.DominantBinRatio, 6);
        Assert.True(first.StandardDeviation > 0);
        Assert.True(first.CoefficientOfVariation > 0);
    }

    [Fact]
    public void CandidateGuardSimulation_DoesNotMutateEngineOutput()
    {
        IReadOnlyDictionary<string, TechV2ForensicMapAudit> audits = LoadAudits();
        TechV2ForensicMapAudit tower = audits["Tower"];
        IReadOnlyList<TechV2ForensicRun> snapshot = tower.SustainedRuns.ToArray();

        IReadOnlyList<TechV2ForensicGuard> first =
            TechV2TowerForensicAudit.CreateCandidateGuards(audits);
        IReadOnlyList<TechV2ForensicGuard> second =
            TechV2TowerForensicAudit.CreateCandidateGuards(audits);

        Assert.Equal(snapshot, tower.SustainedRuns);
        Assert.Equal(first.Select(guard => guard.Name), second.Select(guard => guard.Name));
        Assert.Equal(
            first.Select(guard => string.Join(
                ";",
                guard.Results.OrderBy(result => result.Key).Select(result => $"{result.Key}={result.Value}"))),
            second.Select(guard => string.Join(
                ";",
                guard.Results.OrderBy(result => result.Key).Select(result => $"{result.Key}={result.Value}"))));
        Assert.All(first, guard => Assert.True(guard.Results.ContainsKey("Tower")));
    }

    private static IReadOnlyDictionary<string, TechV2ForensicMapAudit> LoadAudits() =>
        new Dictionary<string, TechV2ForensicMapAudit>(StringComparer.Ordinal)
        {
            ["Exit"] = Load("Exit", "Exit This Earth's Atmosphere [Primordial Nucleosynthesis].osu"),
            ["Kira"] = Load("Kira", "Kira Killer [Mocaotic's Insane].osu"),
            ["Frozen"] = Load("Frozen", "Frozen [Collab Insane].osu"),
            ["CanYou"] = Load("CanYou", "(can you) understand me [uhh].osu"),
            ["Tower"] = Load("Tower", "Tower Of Heaven [Extra].osu"),
        };

    private static TechV2ForensicMapAudit Load(string key, string fixtureName) =>
        TechV2TowerForensicAudit.Analyze(key, FixtureLoader.Load(fixtureName));
}
