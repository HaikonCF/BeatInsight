using BeatInsight.Analysis.TechPatterns;
using BeatInsight.Models;
using BeatInsight.Models.TechPatterns;
using Xunit.Abstractions;
using System.IO;

namespace BeatInsight.Tests.Analysis;

public sealed class TechPatternV1ConsolidationTests(ITestOutputHelper output)
{
    private static Beatmap Map(params (int X, int Y)[] points) => new()
    {
        CS = 4, SliderMultiplier = 1,
        TimingPoints = [new() { Time = 0, BeatLength = 500, Uninherited = true }],
        HitObjects = points.Select((p, i) => new HitObject { X = p.X, Y = p.Y, Time = i * 1000, Type = 1 }).ToList(),
    };

    private static Beatmap Coupled() => Map((0, 0), (40, 0), (40, 120), (65, 145));

    private static Beatmap SliderMotif()
    {
        var map = Map((0, 0), (80, 0), (0, 80));
        map.HitObjects[1].Type = 2;
        map.HitObjects[1].SliderCurveType = "L";
        map.HitObjects[1].Length = 80;
        map.HitObjects[1].Slides = 1;
        map.HitObjects[1].SliderControlPoints = [new() { X = 80, Y = 80 }];
        map.HitObjects[1].SliderEndPosition = new() { X = 80, Y = 80 };
        map.HitObjects[2].Type = 2;
        map.HitObjects[2].SliderCurveType = "L";
        map.HitObjects[2].Length = 80;
        map.HitObjects[2].Slides = 1;
        map.HitObjects[2].SliderControlPoints = [new() { X = 0, Y = 160 }];
        map.HitObjects[2].SliderEndPosition = new() { X = 0, Y = 160 };
        return map;
    }

    [Fact]
    public void CircleOnlyCoupledAimControl_ConfirmsWithProvenance()
    {
        var result = TechPatternPresenceEngine.Analyze(Coupled());
        Assert.Single(result.Motifs, m => m.Status == PatternEvidenceStatus.Confirmed);
        var match = Assert.Single(result.RecognizedPatterns, m => m.Type == TechPatternType.CoupledAimControl);
        Assert.Equal(new[] { 0, 1, 2, 3 }, match.SupportingEventIndices);
        Assert.NotEmpty(match.Reasons);
        Assert.Equal(100, result.PresencePercent, 6);
    }

    [Theory]
    [InlineData(20, -30, 1, 0, 1)]
    [InlineData(0, 0, 1, 90, 1)]
    [InlineData(0, 0, 2, 0, 1)]
    [InlineData(0, 0, 1, 0, 2)]
    public void Grammar_IsInvariantForMeaningfulTranslationRotationScaleAndTempo(int tx, int ty, int scale, int rotation, int tempo)
    {
        var baseline = TechPatternPresenceEngine.Analyze(Coupled());
        var map = Coupled();
        foreach (var o in map.HitObjects)
        {
            int x = o.X, y = o.Y;
            o.X = (rotation == 90 ? -y : x) * scale + tx;
            o.Y = (rotation == 90 ? x : y) * scale + ty;
            o.Time *= tempo;
        }
        var actual = TechPatternPresenceEngine.Analyze(map);
        Assert.Equal(baseline.ConfirmedMotifCount, actual.ConfirmedMotifCount);
        Assert.Equal(baseline.PresencePercent, actual.PresencePercent, 6);
    }

    [Fact]
    public void StrongAngleRegularJumps_AndSpacedStream_DoNotConfirm()
    {
        var jump = Map((0, 0), (150, 0), (0, 0), (150, 0), (0, 0));
        var stream = Map((0, 0), (70, 10), (140, 20), (210, 30), (280, 40));
        Assert.Equal(0, TechPatternPresenceEngine.Analyze(jump).PresencePercent);
        Assert.Equal(0, TechPatternPresenceEngine.Analyze(stream).PresencePercent);
    }

    [Fact]
    public void RhythmOnlyAndSpacingOnly_DoNotConfirm()
    {
        var rhythm = Map((0, 0), (80, 0), (160, 0), (240, 0));
        rhythm.HitObjects[2].Time = 1300;
        Assert.Equal(0, TechPatternPresenceEngine.Analyze(rhythm).PresencePercent);
        Assert.Equal(0, TechPatternPresenceEngine.Analyze(Map((0, 0), (20, 0), (140, 0), (170, 0))).PresencePercent);
    }

    [Fact]
    public void SliderExitAndHandoff_ConfirmOneMotifNotThreeProofs()
    {
        var result = TechPatternPresenceEngine.Analyze(SliderMotif());
        Assert.Contains(result.RecognizedPatterns, m => m.Type == TechPatternType.TechnicalSliderExit);
        Assert.Contains(result.RecognizedPatterns, m => m.Type == TechPatternType.SliderToSliderTransition);
        Assert.Contains(result.RecognizedPatterns, m => m.Type == TechPatternType.TechnicalSliderEntry);
        Assert.Equal(1, result.ConfirmedMotifCount);
        Assert.Equal(1, result.RegionCount);
        Assert.Equal(2400, result.TotalTechDuration, 6);
    }

    [Fact]
    public void OrdinarySliderFlowAndDecorativeP_DoNotConfirm()
    {
        var map = SliderMotif();
        map.HitObjects[1].SliderControlPoints = [new() { X = 160, Y = 0 }];
        map.HitObjects[1].SliderEndPosition = new() { X = 160, Y = 0 };
        map.HitObjects[2].X = 240; map.HitObjects[2].Y = 0;
        map.HitObjects[2].SliderControlPoints = [new() { X = 320, Y = 0 }];
        map.HitObjects[2].SliderEndPosition = new() { X = 320, Y = 0 };
        Assert.Equal(0, TechPatternPresenceEngine.Analyze(map).PresencePercent);
        map.HitObjects[1].SliderCurveType = "P";
        Assert.Equal(0, TechPatternPresenceEngine.Analyze(map).PresencePercent);
    }

    [Fact]
    public void PArc_HasGeometricTangentsWithoutAutomaticTechEvidence()
    {
        var map = Map((-80, 0), (0, 0), (100, 180));
        var slider = map.HitObjects[1];
        slider.Type = 2; slider.Slides = 1; slider.SliderCurveType = "P";
        slider.Length = 50 * Math.PI;
        slider.SliderControlPoints = [new() { X = 50, Y = 50 }, new() { X = 100, Y = 0 }];
        slider.SliderEndPosition = new() { X = 100, Y = 0 };
        var facts = new LocalPatternContext(map);
        Assert.True(facts.Events[1].EndpointReliable);
        Assert.NotNull(facts.Events[1].InitialDirection);
        Assert.NotNull(facts.Events[1].TerminalDirection);
        Assert.Equal(0, TechPatternPresenceEngine.Analyze(map).PresencePercent);
        slider.Length = 20; // Parsed endpoint is no longer reliable for a truncated arc.
        Assert.False(new LocalPatternContext(map).Events[1].EndpointReliable);
    }

    [Fact]
    public void Preprocessing_UsesTimingSvRepeatsAndLeavesInputUntouched()
    {
        var map = SliderMotif();
        map.TimingPoints.Add(new() { Time = 500, BeatLength = -50, Uninherited = false });
        map.HitObjects[1].Slides = 2;
        map.HitObjects[1].SliderEndPosition = new() { X = 80, Y = 0 };
        var events = GameplayEventBuilder.Build(map);
        Assert.Equal(400, events[1].SliderDuration);
        Assert.Equal(1400, events[1].EffectiveEndTime);
        Assert.Equal(1, events[1].SliderRepeats);
        Assert.Equal(-1, events[1].TerminalDirection!.Value.Y);
    }

    [Fact]
    public void ContainedExplanations_AreResolvedWithoutGrowingTheMotif()
    {
        var coupled = TechPatternPresenceEngine.Analyze(Coupled()).RecognizedPatterns.Single(m => m.Type == TechPatternType.CoupledAimControl);
        var partial = coupled with { Type = TechPatternType.TechnicalSliderEntry, EndIndex = 2, EndTime = 2000, SupportingEventIndices = new[] { 0, 1, 2 } };
        var motif = Assert.Single(PatternEvidenceResolver.Resolve([coupled, partial, partial]));
        Assert.Equal(2, motif.Matches.Count);
        Assert.Equal(3000, motif.EndTime);
        Assert.Equal(PatternEvidenceStatus.Confirmed, motif.Status);
    }

    [Fact]
    public void NeutralSurroundingObjects_DoNotEnlargeSupportedInterval()
    {
        var map = Coupled();
        map.HitObjects.Insert(0, new() { Type = 1, X = -40, Time = -1000 });
        map.HitObjects.Add(new() { Type = 1, X = 90, Y = 170, Time = 4000 });
        var region = Assert.Single(TechPatternPresenceEngine.Analyze(map).Regions);
        Assert.Equal(0, region.StartTime);
        Assert.Equal(3000, region.EndTime);
    }

    [Fact]
    public void MissingGeometryAndTinyMovement_DoNotInventEvidence()
    {
        var zero = new LocalPatternContext(Map((0, 0), (0, 0), (0, 0), (0, 0)));
        Assert.All(zero.SignedTurns, angle => Assert.Null(angle));
        Assert.Empty(new CoupledAimControlDetector().Detect(zero));
        Assert.Equal(0, TechPatternPresenceEngine.Analyze(Map((0, 0), (1, 0), (1, 3), (2, 4))).PresencePercent);
        Assert.Equal(0, TechPatternPresenceEngine.Analyze(new Beatmap()).PresencePercent);
    }

    [Fact]
    public void EntryC_CannotQualifyFromRhythmSpacingOrCurveTypeAlone()
    {
        var map = SliderMotif();
        map.HitObjects[0].X = 80; map.HitObjects[0].Y = -80;
        map.HitObjects[2].Time = 4000;
        Assert.Empty(new TechnicalSliderEntryDetector().Detect(new(map)));
    }

    [Fact]
    public void DuplicateEvidenceAndOverlappingIntervals_DoNotMultiplyPresence()
    {
        var result = TechPatternPresenceEngine.Analyze(SliderMotif());
        var motifs = PatternEvidenceResolver.Resolve(result.RecognizedPatterns.Concat(result.RecognizedPatterns));
        Assert.Equal(result.ConfirmedMotifCount, motifs.Count(m => m.Status == PatternEvidenceStatus.Confirmed));
        var regions = TechRegionAssembler.Assemble(motifs.Concat(motifs), 0, 2400);
        Assert.Equal(2400, Assert.Single(regions).Duration);
    }

    [Fact]
    public void IndependentRegions_AreNotBridgedBySupport()
    {
        var motif = TechPatternPresenceEngine.Analyze(Coupled()).Motifs.Single(m => m.Status == PatternEvidenceStatus.Confirmed);
        var later = motif with { StartTime = 5000, EndTime = 8000 };
        var support = motif with { StartTime = 2500, EndTime = 6000, Status = PatternEvidenceStatus.PartialSupport };
        Assert.Equal(2, TechRegionAssembler.Assemble([motif, later, support], 0, 8000).Count);
    }

    [Fact]
    public void Analysis_IsDeterministicAndDoesNotMutateIdentityOrScores()
    {
        var map = SliderMotif();
        map.GameplayProfile = new() { TechScore = 29, TechPresence = .45, Identity = new() { Primary = "Jump", Confidence = 73 } };
        var before = System.Text.Json.JsonSerializer.Serialize(map);
        var first = System.Text.Json.JsonSerializer.Serialize(TechPatternPresenceEngine.Analyze(map));
        Assert.Equal(first, System.Text.Json.JsonSerializer.Serialize(TechPatternPresenceEngine.Analyze(map)));
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(map));
    }

    [Fact]
    public void LocalRealMapAudit_ReportsPatternsAndExactRegions()
    {
        (string Name, string Category, string? Fixture)[] maps =
        [
            ("GLORIOUS DAYS", "ExpectedTech", null), ("Kira Killer", "ExpectedTech", "Kira Killer [Mocaotic's Insane].osu"),
            ("The World Ends Now", "ExpectedTech", null), ("Frozen", "ExpectedTech", "Frozen [Collab Insane].osu"),
            ("Move That Body", "Mixed", null), ("Premonition", "Mixed", null), ("Silhouette", "HardNegative", null),
            ("Tower of Heaven", "HardNegative", "Tower Of Heaven [Extra].osu"),
            ("(can you) understand me?", "HardNegative", "(can you) understand me [uhh].osu"),
        ];
        var results = new List<(string Category, double Presence)>();
        foreach (var map in maps)
        {
            string? path = map.Fixture is not null ? Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps", map.Fixture)
                : TechV2SliderDensityCalibrationAudit.Corpus.FirstOrDefault(m => m.Name.Contains(map.Name, StringComparison.OrdinalIgnoreCase)).Path;
            if (path is null || !File.Exists(path)) { output.WriteLine($"{map.Name} | UNAVAILABLE"); continue; }
            var beatmap = BeatInsight.Parser.BeatmapParser.Load(path);
            var result = TechPatternPresenceEngine.Analyze(beatmap);
            Assert.InRange(result.PresencePercent, 0, 100);
            Assert.True(double.IsFinite(result.PresencePercent));
            results.Add((map.Category, result.PresencePercent));
            output.WriteLine(FormattableString.Invariant($"MAP | {map.Name} [{beatmap.Version}] | {map.Category} | Patterns={result.RecognizedPatterns.Count} | Motifs={result.ConfirmedMotifCount} | Regions={result.RegionCount} | TechMs={result.TotalTechDuration:F2} | PlayableMs={result.PlayableDuration:F2} | Presence={result.PresencePercent:F3}%"));
            output.WriteLine("COUNTS | " + string.Join(" | ", result.RecognizedPatterns.GroupBy(m => m.Type).Select(g => $"{g.Key}={g.Count()}")));
            output.WriteLine("CONFIRMATIONS | " + string.Join(" | ", result.Motifs.Where(m => m.Status == PatternEvidenceStatus.Confirmed).GroupBy(m => m.ConfirmationReason).Select(g => $"{g.Key}={g.Count()}")));
            foreach (var motif in result.Motifs.Where(m => m.Status == PatternEvidenceStatus.Confirmed)
                .OrderByDescending(m => m.Matches.Max(x => x.Similarity)).ThenBy(m => m.StartTime).Take(3))
                output.WriteLine($"TOP | {TimeSpan.FromMilliseconds(motif.StartTime):mm\\:ss\\.fff} -> {TimeSpan.FromMilliseconds(motif.EndTime):mm\\:ss\\.fff} | {motif.ConfirmationReason} | {string.Join("; ", motif.Matches.SelectMany(m => m.Reasons).Distinct())}");
        }
        foreach (var category in results.GroupBy(r => r.Category))
            output.WriteLine(FormattableString.Invariant($"CATEGORY | {category.Key} | N={category.Count()} | AvgPresence={category.Average(x => x.Presence):F3}%"));
        // Deliberately no assertion that these map-wide references label every local region.
    }
}
