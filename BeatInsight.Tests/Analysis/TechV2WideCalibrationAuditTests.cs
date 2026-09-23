using BeatInsight.Analysis;
using BeatInsight.Models;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

/// <summary>Phase 2.5 fixture-only calibration report; no category outcome is asserted.</summary>
public sealed class TechV2WideCalibrationAuditTests(ITestOutputHelper output)
{
    private static readonly (string FixtureName, TechV2CalibrationCategory Category)[] Fixtures =
    [
        ("Exit This Earth's Atmosphere [Primordial Nucleosynthesis].osu", TechV2CalibrationCategory.TechReference),
        ("Frozen [Collab Insane].osu", TechV2CalibrationCategory.TechReference),
        ("Kira Killer [Mocaotic's Insane].osu", TechV2CalibrationCategory.TechReference),
        ("(can you) understand me [uhh].osu", TechV2CalibrationCategory.NonTechReference),
        ("Tower Of Heaven [Extra].osu", TechV2CalibrationCategory.NonTechReference),
        ("Ashioto Tarte Tatin [Koori's Insane].osu", TechV2CalibrationCategory.BorderlineOrUnknown),
        ("forever we can make it! [Kudowari's Expert].osu", TechV2CalibrationCategory.BorderlineOrUnknown),
        ("FREEDOM DiVE [Arles].osu", TechV2CalibrationCategory.BorderlineOrUnknown),
        ("FREEDOM DiVE [FrenZ's Insane].osu", TechV2CalibrationCategory.BorderlineOrUnknown),
        ("Snow Goose [Extra].osu", TechV2CalibrationCategory.BorderlineOrUnknown),
    ];

    [Fact]
    public void ReportWideFixturePack_UsesCurrentEngineAndKeepsAllMetricsFinite()
    {
        IReadOnlyList<TechV2WideCalibrationMap> maps = LoadMaps();

        Assert.Equal(Fixtures.Length, maps.Count);
        Assert.All(maps, map =>
        {
            Assert.InRange(map.BeforeGuardPresence.PresencePercent, 0, 100);
            Assert.InRange(map.AfterGuardPresence.PresencePercent, 0, 100);
            Assert.All(map.CandidateRuns, run =>
            {
                Assert.True(double.IsFinite(run.AverageGeometry));
                Assert.True(double.IsFinite(run.AverageMovement));
                Assert.True(double.IsFinite(run.AverageSlider));
                Assert.True(double.IsFinite(run.AverageMotif));
            });
        });

        output.WriteLine(TechV2WideCalibrationAudit.FormatReport(maps));
    }

    [Fact]
    public void OfflineThresholdSimulation_IsDeterministicAndDoesNotChangeCurrentGuardOutput()
    {
        IReadOnlyList<TechV2WideCalibrationMap> first = LoadMaps();
        IReadOnlyList<TechV2WideCalibrationMap> second = LoadMaps();

        foreach (TechV2WideCalibrationMap firstMap in first)
        {
            TechV2WideCalibrationMap secondMap = Assert.Single(
                second,
                map => map.FixtureName == firstMap.FixtureName);
            AssertPresenceEqual(firstMap.AfterGuardPresence, secondMap.AfterGuardPresence);
            Assert.Equal(
                firstMap.CandidateRuns.Select(RunSignature),
                secondMap.CandidateRuns.Select(RunSignature));
            foreach (double threshold in TechV2WideCalibrationAudit.GeometryThresholds)
            {
                TechV2WideCalibrationSimulation left = firstMap.Simulations[threshold];
                TechV2WideCalibrationSimulation right = secondMap.Simulations[threshold];
                Assert.Equal(left.Threshold, right.Threshold);
                Assert.Equal(left.RunsKept, right.RunsKept);
                Assert.Equal(left.RunsRejected, right.RunsRejected);
                AssertPresenceEqual(left.Presence, right.Presence);
                Assert.InRange(left.Presence.PresencePercent, 0, 100);
            }
        }
    }

    private static IReadOnlyList<TechV2WideCalibrationMap> LoadMaps() =>
        Fixtures.Select(fixture =>
        {
            Beatmap map = FixtureLoader.Load(fixture.FixtureName);
            return TechV2WideCalibrationAudit.Analyze(fixture.FixtureName, fixture.Category, map);
        }).ToArray();

    private static void AssertPresenceEqual(
        TechV2PresenceResult expected,
        TechV2PresenceResult actual)
    {
        Assert.Equal(expected.TotalTechDuration, actual.TotalTechDuration);
        Assert.Equal(expected.PlayableDuration, actual.PlayableDuration);
        Assert.Equal(expected.PresenceRatio, actual.PresenceRatio);
        Assert.Equal(expected.PresencePercent, actual.PresencePercent);
        Assert.Equal(expected.WindowCount, actual.WindowCount);
        Assert.Equal(expected.TechWindowCount, actual.TechWindowCount);
        Assert.Equal(expected.Regions, actual.Regions);
    }

    private static string RunSignature(TechV2WideCalibrationRun run) => string.Join(
        ":",
        run.StartTime,
        run.EndTime,
        run.WindowCount,
        run.AverageGeometry,
        run.AverageMovement,
        run.AverageSlider,
        run.AverageMotif,
        run.KeptByCurrentGeometryGuard,
        string.Join(",", run.WindowStartIndices));
}
