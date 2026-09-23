using BeatInsight.Models.Persistence;
using Xunit;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// Phase 2.8 test-only runner. It resolves the five human reference maps
/// (from the bundled fixture corpus and/or BeatInsight's existing local
/// analysis cache), prints the full StrongTech forensic report, and asserts
/// only bookkeeping invariants of the forensic tool itself. It never asserts
/// what the "right" Tech V2 decision should be for any reference map, and it
/// never mutates the production decision engine.
/// </summary>
public sealed class TechV2StrongTechForensicTests
{
    private readonly ITestOutputHelper output;

    public TechV2StrongTechForensicTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void StrongTechForensic_ExplainsReferenceMapsWithoutTouchingProduction()
    {
        IReadOnlyList<MlDatasetSample> localCacheSamples =
            TechV2StrongTechForensicAudit.LoadLocalCacheSamples();

        IReadOnlyList<TechV2StrongTechMapForensic> maps = TechV2StrongTechForensicAudit
            .ReferenceMaps
            .Select(definition => TechV2StrongTechForensicAudit.Resolve(
                definition,
                localCacheSamples))
            .ToArray();

        IReadOnlyList<TechV2StrongTechGuardOutcome> guardOutcomes = TechV2StrongTechForensicAudit
            .CandidateGuards
            .Select(guard => TechV2StrongTechForensicAudit.SimulateGuard(guard, maps))
            .ToArray();

        output.WriteLine(TechV2StrongTechForensicAudit.FormatReport(maps, guardOutcomes));

        Assert.Equal(TechV2StrongTechForensicAudit.ReferenceMaps.Count, maps.Count);

        foreach (TechV2StrongTechMapForensic map in maps)
        {
            Assert.All(
                map.StrongTechWindows,
                window => Assert.True(
                    window.StrongFamilyCount >= 2
                    || window.Geometry >= 0.80
                    || window.Movement >= 0.80
                    || window.Slider >= 0.80
                    || window.Motif >= 0.80));
        }

        Assert.Equal(TechV2StrongTechForensicAudit.CandidateGuards.Count, guardOutcomes.Count);
        Assert.All(
            guardOutcomes,
            outcome => Assert.True(
                outcome.KeptByMap.Count <= maps.Count(map => map.Found)));
    }
}
