using BeatInsight.Services.Persistence;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// Phase 2.7 test-only runners. The fixture test guards the discovery
/// tool's own bookkeeping against the small bundled corpus. The local
/// library test reuses BeatInsight's existing local analysis cache
/// (<see cref="MlDatasetSampleRepository"/>) to discover hard-negative
/// candidates from the user's real, already-analyzed beatmaps. Neither
/// test asserts anything about Tech V2's correctness itself, and neither
/// reaches the network or the full osu! Songs folder directly.
/// </summary>
public sealed class TechV2HardNegativeDiscoveryAuditTests
{
    private readonly ITestOutputHelper output;

    public TechV2HardNegativeDiscoveryAuditTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void Scan_ReportsBoundedSuspectListsFromLocalFixtures()
    {
        string fixturesDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Maps");

        TechV2HardNegativeDiscoveryReport report =
            TechV2HardNegativeDiscoveryAudit.Scan(fixturesDirectory);

        output.WriteLine(TechV2HardNegativeDiscoveryAudit.FormatReport(report));

        Assert.True(report.MapsScanned > 0);
        Assert.True(report.MapsUsable <= report.MapsScanned);
        Assert.True(
            report.StrongTechSuspects.Count
                <= TechV2HardNegativeDiscoveryAudit.MaxCandidatesPerGroup);
        Assert.True(
            report.SustainedTechSuspects.Count
                <= TechV2HardNegativeDiscoveryAudit.MaxCandidatesPerGroup);
        Assert.All(
            report.StrongTechSuspects,
            candidate => Assert.True(
                candidate.StrongTechCount > 0
                && !string.Equals(candidate.Primary, "Tech", StringComparison.Ordinal)
                && !string.Equals(candidate.Secondary, "Tech", StringComparison.Ordinal)));
        Assert.All(
            report.SustainedTechSuspects,
            candidate => Assert.True(
                candidate.StrongTechCount == 0
                && candidate.SustainedTechCount > 0
                && !string.Equals(candidate.Primary, "Tech", StringComparison.Ordinal)
                && !string.Equals(candidate.Secondary, "Tech", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Reuses BeatInsight's real local analysis cache
    /// (<see cref="MlDatasetSampleRepository.DefaultDatabasePath"/>), which
    /// is populated only by the existing <c>MlDatasetBuilder</c> scan of the
    /// user's configured osu! Songs folder. This test does not walk the
    /// Songs folder itself and does not touch the network. On a machine
    /// where that cache does not exist yet (fresh checkout, CI), the scan
    /// degrades to zero discovered maps instead of failing, since the real
    /// local library is inherently machine-specific.
    /// </summary>
    [Fact]
    public void ScanLocalLibrary_ReportsBoundedSuspectListsFromRealAnalysisCache()
    {
        TechV2HardNegativeDiscoveryReport report =
            TechV2HardNegativeDiscoveryAudit.ScanLocalLibrary();

        output.WriteLine(TechV2HardNegativeDiscoveryAudit.FormatReport(report));

        Assert.True(report.MapsScanned <= report.LocalMapsDiscovered);
        Assert.True(
            report.MapsScanned
                <= TechV2HardNegativeDiscoveryAudit.DefaultLocalLibrarySampleSize);
        Assert.True(report.MapsUsable <= report.MapsScanned);
        Assert.True(
            report.StrongTechSuspects.Count
                <= TechV2HardNegativeDiscoveryAudit.MaxCandidatesPerGroup);
        Assert.True(
            report.SustainedTechSuspects.Count
                <= TechV2HardNegativeDiscoveryAudit.MaxCandidatesPerGroup);
        Assert.True(report.StrongTechSuspects.Count <= report.GroupARawCount);
        Assert.True(report.SustainedTechSuspects.Count <= report.GroupBRawCount);
        Assert.All(
            report.StrongTechSuspects,
            candidate => Assert.True(
                candidate.StrongTechCount > 0
                && !string.Equals(candidate.Primary, "Tech", StringComparison.Ordinal)
                && !string.Equals(candidate.Secondary, "Tech", StringComparison.Ordinal)));
        Assert.All(
            report.SustainedTechSuspects,
            candidate => Assert.True(
                candidate.StrongTechCount == 0
                && candidate.SustainedTechCount > 0
                && !string.Equals(candidate.Primary, "Tech", StringComparison.Ordinal)
                && !string.Equals(candidate.Secondary, "Tech", StringComparison.Ordinal)));
    }
}
