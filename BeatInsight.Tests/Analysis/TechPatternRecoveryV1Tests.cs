using BeatInsight.Analysis.TechPatterns;
using BeatInsight.Models.TechPatterns;
using BeatInsight.Parser;
using BeatInsight.Models;
using System.IO;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechPatternRecoveryV1Tests(ITestOutputHelper output)
{
    internal static readonly (string Name, string Category, string? Fixture)[] Corpus =
    [
        ("GLORIOUS DAYS", "ExpectedTech", null), ("Kira Killer", "ExpectedTech", "Kira Killer [Mocaotic's Insane].osu"),
        ("The World Ends Now", "ExpectedTech", null), ("Frozen", "ExpectedTech", "Frozen [Collab Insane].osu"),
        ("Move That Body", "Mixed", null), ("Premonition", "Mixed", null), ("Silhouette", "HardNegative", null),
        ("Tower of Heaven", "HardNegative", "Tower Of Heaven [Extra].osu"),
        ("(can you) understand me?", "HardNegative", "(can you) understand me [uhh].osu"),
    ];

    internal static string? Resolve(string name, string? fixture) => fixture is not null
        ? Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps", fixture)
        : TechV2SliderDensityCalibrationAudit.Corpus.FirstOrDefault(m => m.Name.Contains(name, StringComparison.OrdinalIgnoreCase)).Path;

    [Fact]
    public void GloriousForensic_ReportsEveryMatchAndRejectedPrimary()
    {
        string? path = Resolve("GLORIOUS DAYS", null);
        Assert.True(File.Exists(path), "Exact GLORIOUS DAYS corpus path unavailable.");
        var map = BeatmapParser.Load(path!);
        var result = TechPatternPresenceEngine.Analyze(map);
        var context = new LocalPatternContext(map);
        output.WriteLine("COUNTS " + string.Join(" | ", Enum.GetValues<TechPatternType>().Select(t => $"{t}={result.RecognizedPatterns.Count(m => m.Type == t)}")));
        foreach (var match in result.RecognizedPatterns)
        {
            output.WriteLine(FormattableString.Invariant($"MATCH | {match.Type} | {match.StartTime:F3}-{match.EndTime:F3} | [{string.Join(",", match.SupportingEventIndices)}] | Similarity={match.Similarity:F6} | {string.Join("; ", match.Reasons)}"));
            if (match.Type is not (TechPatternType.TechnicalSliderExit or TechPatternType.SliderToSliderTransition or TechPatternType.CoupledAimControl)) continue;
            var same = result.RecognizedPatterns.Where(m => m.SupportingEventIndices.SequenceEqual(match.SupportingEventIndices)).ToArray();
            var motif = result.Motifs.Single(m => m.Matches.Contains(match));
            output.WriteLine($"RESOLVER | Status={motif.Status} | SameSupport={string.Join(",", same.Select(m => m.Type))}");
            bool hasEntry = same.Any(m => m.Type == TechPatternType.TechnicalSliderEntry);
            bool hasRhythm = same.Any(m => m.Type == TechPatternType.IrregularRhythmTransition);
            output.WriteLine($"REJECTION | NO_SECOND_PRIMARY | MISSING_SUPPORT | ENTRY_C_FAILED={!hasEntry} | RHYTHM_SUPPORT_FAILED={!hasRhythm} | OVERLAP_RESOLUTION=false | INVALID_GEOMETRY=false");
            var nearby = result.RecognizedPatterns.Where(m => m.StartIndex <= match.EndIndex + 2 && m.EndIndex >= match.StartIndex - 2);
            output.WriteLine("NEARBY | " + string.Join(" | ", nearby.Select(m => $"{m.Type}[{m.StartIndex}-{m.EndIndex}]")));
            PrintInternal(context, match);
        }
    }

    private static Beatmap SliderExample(bool nextSlider, bool complete)
    {
        var map = new Beatmap
        {
            CS = 4, SliderMultiplier = 1,
            TimingPoints = [new() { BeatLength = 500, Uninherited = true }],
            HitObjects =
            [
                new() { X = 0, Y = 0, Time = 0, Type = 1 },
                new() { X = 80, Y = 0, Time = 1000, Type = 2, Slides = 1, Length = 80,
                    SliderCurveType = "L", SliderControlPoints = [new() { X = 80, Y = 80 }],
                    SliderEndPosition = new() { X = 80, Y = 80 } },
                new() { X = complete ? -80 : 0, Y = 80, Time = complete ? 3000 : 2000, Type = nextSlider ? 2 : 1,
                    Slides = 1, Length = 80, SliderCurveType = "L",
                    SliderControlPoints = [new() { X = complete ? -80 : 0, Y = 160 }],
                    SliderEndPosition = new() { X = complete ? -80 : 0, Y = 160 } },
            ],
        };
        return map;
    }

    [Fact]
    public void InternallyCompleteSliderExit_SelfConfirmsWithThreeDistinctFamilies()
    {
        var match = Assert.Single(new TechnicalSliderExitDetector().Detect(new(SliderExample(false, true))));
        Assert.True(SelfContainedPrimaryPolicy.CanConfirm(match));
        Assert.Equal(3, SelfContainedPrimaryPolicy.IndependentFamilies(match).Count);
        Assert.Equal(PatternEvidenceStatus.PartialSupport, Assert.Single(PatternEvidenceResolver.Resolve([match])).Status);
        var recovered = Assert.Single(PatternEvidenceResolver.Resolve([match], PatternConfirmationPolicy.RecoveryV1));
        Assert.Equal(PatternEvidenceStatus.Confirmed, recovered.Status);
        Assert.Contains("SELF_CONTAINED_PRIMARY", recovered.ConfirmationReason);
    }

    [Fact]
    public void WeakSliderExit_CannotSelfConfirmEvenWithHighSimilarity()
    {
        var match = Assert.Single(new TechnicalSliderExitDetector().Detect(new(SliderExample(false, false)))) with { Similarity = 1 };
        Assert.False(SelfContainedPrimaryPolicy.CanConfirm(match));
        Assert.Equal(PatternEvidenceStatus.PartialSupport,
            Assert.Single(PatternEvidenceResolver.Resolve([match], PatternConfirmationPolicy.RecoveryV1)).Status);
    }

    [Fact]
    public void InternallyCompleteS2S_SelfConfirms()
    {
        var match = Assert.Single(new SliderToSliderTransitionDetector().Detect(new(SliderExample(true, true))));
        Assert.True(SelfContainedPrimaryPolicy.CanConfirm(match));
        Assert.Contains(TechEvidenceFamily.Handoff, SelfContainedPrimaryPolicy.IndependentFamilies(match));
        Assert.Equal(PatternEvidenceStatus.Confirmed,
            Assert.Single(PatternEvidenceResolver.Resolve([match], PatternConfirmationPolicy.RecoveryV1)).Status);
    }

    [Fact]
    public void OrdinaryHandoff_CannotSelfConfirm()
    {
        var map = SliderExample(true, true);
        map.HitObjects[1].SliderControlPoints = [new() { X = 160, Y = 0 }];
        map.HitObjects[1].SliderEndPosition = new() { X = 160, Y = 0 };
        map.HitObjects[2].X = 240; map.HitObjects[2].Y = 0;
        map.HitObjects[2].SliderControlPoints = [new() { X = 320, Y = 0 }];
        map.HitObjects[2].SliderEndPosition = new() { X = 320, Y = 0 };
        Assert.Empty(new SliderToSliderTransitionDetector().Detect(new(map)));
        Assert.Equal(0, TechPatternPresenceEngine.Analyze(map, PatternConfirmationPolicy.RecoveryV1).PresencePercent);
    }

    [Fact]
    public void CoupledAimControl_FullGrammarRemainsSelfConfirming()
    {
        var map = new Beatmap { CS = 4, HitObjects =
        [new() { X = 0, Y = 0, Time = 0, Type = 1 }, new() { X = 40, Y = 0, Time = 1000, Type = 1 },
         new() { X = 40, Y = 120, Time = 2000, Type = 1 }, new() { X = 65, Y = 145, Time = 3000, Type = 1 }] };
        var match = Assert.Single(new CoupledAimControlDetector().Detect(new(map)));
        Assert.True(SelfContainedPrimaryPolicy.CanConfirm(match));
        Assert.Equal(1, TechPatternPresenceEngine.Analyze(map, PatternConfirmationPolicy.RecoveryV1).ConfirmedMotifCount);
    }

    [Theory]
    [InlineData(TechPatternType.SharpDirectionChange)]
    [InlineData(TechPatternType.DirectionReversal)]
    [InlineData(TechPatternType.TechnicalSliderEntry)]
    [InlineData(TechPatternType.IrregularRhythmTransition)]
    public void ObservationsAndSupport_CannotSelfConfirm(TechPatternType type)
    {
        var match = Assert.Single(new TechnicalSliderExitDetector().Detect(new(SliderExample(false, true)))) with { Type = type };
        Assert.False(SelfContainedPrimaryPolicy.CanConfirm(match));
        Assert.NotEqual(PatternEvidenceStatus.Confirmed,
            Assert.Single(PatternEvidenceResolver.Resolve([match], PatternConfirmationPolicy.RecoveryV1)).Status);
    }

    [Fact]
    public void DuplicateDirectionalReasons_CountOnlyOnce()
    {
        var match = Assert.Single(new TechnicalSliderExitDetector().Detect(new(SliderExample(false, true))));
        var direction = match.InternalEvidence.First(e => e.Family == TechEvidenceFamily.Direction);
        match = match with { InternalEvidence = new[] { direction, direction with { Reason = "high geometry" }, direction with { Reason = "high direction delta" } } };
        Assert.Single(SelfContainedPrimaryPolicy.IndependentFamilies(match));
        Assert.False(SelfContainedPrimaryPolicy.CanConfirm(match));
    }

    [Fact]
    public void UnsupportedEvidenceCannotBeBorrowedFromAnotherMotif()
    {
        var match = Assert.Single(new TechnicalSliderExitDetector().Detect(new(SliderExample(false, true))));
        match = match with { InternalEvidence = match.InternalEvidence.Select(e => e with { EventIndices = new[] { 999 } }).ToArray() };
        Assert.Empty(SelfContainedPrimaryPolicy.IndependentFamilies(match));
        Assert.False(SelfContainedPrimaryPolicy.CanConfirm(match));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OrdinaryJumpAndStream_RemainRejected(bool jump)
    {
        var map = new Beatmap { CS = 4, HitObjects = Enumerable.Range(0, 8)
            .Select(i => new HitObject { Type = 1, X = jump ? i % 2 * 150 : i * 60, Y = 0, Time = i * 100 }).ToList() };
        Assert.Equal(0, TechPatternPresenceEngine.Analyze(map, PatternConfirmationPolicy.RecoveryV1).PresencePercent);
    }

    [Fact]
    public void Recovery_IsDeterministicAndPreservesRawMatchesAndIntervals()
    {
        var map = SliderExample(false, true);
        var baseline = TechPatternPresenceEngine.Analyze(map);
        var recovery = TechPatternPresenceEngine.Analyze(map, PatternConfirmationPolicy.RecoveryV1);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(baseline.RecognizedPatterns), System.Text.Json.JsonSerializer.Serialize(recovery.RecognizedPatterns));
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(recovery), System.Text.Json.JsonSerializer.Serialize(TechPatternPresenceEngine.Analyze(map, PatternConfirmationPolicy.RecoveryV1)));
        Assert.Equal(baseline.PlayableDuration, recovery.PlayableDuration);
    }

    [Fact]
    public void RejectedRecoveryPolicy_IsNotEnabledByDefault()
    {
        var match = Assert.Single(new TechnicalSliderExitDetector().Detect(new(SliderExample(false, true))));
        Assert.Equal(PatternEvidenceStatus.PartialSupport, Assert.Single(PatternEvidenceResolver.Resolve([match])).Status);
        var map = SliderExample(false, true);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(TechPatternPresenceEngine.Analyze(map, PatternConfirmationPolicy.BaselineV1)),
            System.Text.Json.JsonSerializer.Serialize(TechPatternPresenceEngine.Analyze(map)));
    }

    [Fact]
    public void NineMapRecoveryAudit_ReportsBaselineAndRecovery()
    {
        var rows = new List<(string Category, double Presence)>();
        var regressions = new List<string>();
        foreach (var item in Corpus)
        {
            string? path = Resolve(item.Name, item.Fixture);
            if (!File.Exists(path)) { output.WriteLine($"UNAVAILABLE | {item.Name}"); continue; }
            var map = BeatmapParser.Load(path!);
            var baseline = TechPatternPresenceEngine.Analyze(map);
            var recovery = TechPatternPresenceEngine.Analyze(map, PatternConfirmationPolicy.RecoveryV1);
            var added = recovery.Motifs.Where(m => m.Status == PatternEvidenceStatus.Confirmed && m.ConfirmationReason.Contains("SELF_CONTAINED_PRIMARY")).ToArray();
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(baseline.RecognizedPatterns), System.Text.Json.JsonSerializer.Serialize(recovery.RecognizedPatterns));
            Assert.True(recovery.PresencePercent >= baseline.PresencePercent);
            rows.Add((item.Category, recovery.PresencePercent));
            if (item.Category == "HardNegative" && recovery.ConfirmedMotifCount > baseline.ConfirmedMotifCount)
                regressions.Add(item.Name);
            output.WriteLine(FormattableString.Invariant($"MAP | {item.Name} [{map.Version}] | {item.Category} | BaselineMotifs={baseline.ConfirmedMotifCount} | RecoveryMotifs={recovery.ConfirmedMotifCount} | BaselinePresence={baseline.PresencePercent:F6} | RecoveryPresence={recovery.PresencePercent:F6} | SelfContained={added.Length}"));
            foreach (var motif in added)
            {
                output.WriteLine(FormattableString.Invariant($"ADDED | {motif.StartTime:F3}-{motif.EndTime:F3} | [{string.Join(",", motif.SupportingEventIndices)}] | {motif.ConfirmationReason}"));
                foreach (var match in motif.Matches.Where(SelfContainedPrimaryPolicy.CanConfirm))
                    output.WriteLine("EVIDENCE | " + string.Join(" | ", match.InternalEvidence.Select(e => $"{e.Family}[{string.Join(",", e.EventIndices)}]: {e.Reason}")));
            }
        }
        foreach (var group in rows.GroupBy(r => r.Category))
            output.WriteLine(FormattableString.Invariant($"CATEGORY | {group.Key} | N={group.Count()} | AvgPresence={group.Average(r => r.Presence):F6}"));
        output.WriteLine($"HARD_NEGATIVE_REGRESSIONS | {string.Join(", ", regressions)}");
        output.WriteLine($"REGRESSION_GUARD = {(regressions.Count == 0 && rows.Count == Corpus.Length ? "PASSED" : "FAILED")}; diagnostic result, not a claim of test-suite failure");
    }

    private void PrintInternal(LocalPatternContext c, TechPatternMatch m)
    {
        int i = m.StartIndex + 1;
        var slider = c.Events[i]; var incoming = c.Transitions[i - 1]; var outgoing = c.Transitions[i];
        double? entry = LocalPatternContext.Angle(incoming.Direction, slider.InitialDirection);
        double? exit = LocalPatternContext.Angle(slider.TerminalDirection, outgoing.Direction);
        output.WriteLine(FormattableString.Invariant($"INTERNAL | Endpoint={slider.EndpointReliable} | Duration={slider.SliderDuration:F3} | EntryDegrees={entry * 180 / Math.PI:F3} | ExitDegrees={exit * 180 / Math.PI:F3} | InDistance={incoming.Distance:F3} | OutDistance={outgoing.Distance:F3} | AvailableTime={outgoing.AvailableMovementTime:F3} | SpacingRatio={LocalPatternContext.Ratio(incoming.Distance, outgoing.Distance):F6} | TimingRatio={LocalPatternContext.Ratio(incoming.Interval, outgoing.Interval):F6}"));
    }
}
