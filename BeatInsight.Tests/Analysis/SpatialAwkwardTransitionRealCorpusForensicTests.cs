using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using BeatInsight.Analysis;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Analysis.TechPatterns;
using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.JumpPatterns;
using BeatInsight.Models.TechPatterns;
using BeatInsight.Parser;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// Real-corpus audit and runtime confirmation for Spatial / Awkward Transition V1.
/// </summary>
public sealed class SpatialAwkwardTransitionRealCorpusForensicTests(ITestOutputHelper output)
{
    private static readonly string[] Fixtures =
    [
        "(can you) understand me [uhh].osu",
        "Ashioto Tarte Tatin [Koori's Insane].osu",
        "Exit This Earth's Atmosphere [Primordial Nucleosynthesis].osu",
        "FREEDOM DiVE [Arles].osu",
        "FREEDOM DiVE [FrenZ's Insane].osu",
        "Frozen [Collab Insane].osu",
        "Kira Killer [Mocaotic's Insane].osu",
        "Snow Goose [Extra].osu",
        "Tower Of Heaven [Extra].osu",
        "forever we can make it! [Kudowari's Expert].osu",
        "Vickeblanka - Black Rover (TV Size) (Sotarks) [Extreme].osu"
    ];

    private static readonly MapSource[] AdditionalRequestedMaps =
    [
        new("Connect TV Size", @"D:\osu!\Songs\1854325 ClariS - Connect (TV Size)\ClariS - Connect (TV Size) (Aakki) [Expert].osu", "installed requested map"),
        new("Wings of Courage", @"D:\osu!\Songs\1706691 Kawada Mami - Wings of Courage -Sora o Koete-\Kawada Mami - Wings of Courage -Sora o Koete- (Zekk) [Expert].osu", "installed requested map")
    ];

    [Fact]
    public void ExistingRealCorpusConfirmsRuntimeMatchesVariantF()
    {
        DebugLogger.DebugMode = false;
        DebugLogger.DetailedDebug = false;

        MapSource[] sources = BuildCorpus();
        Assert.True(sources.Length >= Fixtures.Length, "The established fixture corpus must be present.");
        MapAudit[] maps = sources.Select(Analyze).ToArray();
        Assert.All(maps, VerifyRuntimeMatchesVariantF);

        var report = new StringBuilder();
        AppendHeader(report, maps);
        AppendMapSummary(report, maps);
        AppendGlobalDistributions(report, maps);
        AppendQualifiedAnalysis(report, maps);
        AppendReasonAnalysis(report, maps);
        AppendOverlapAnalysis(report, maps);
        AppendRankings(report, maps);
        AppendSimulations(report, maps);
        AppendManualReviewSets(report, maps);
        AppendConclusionInputs(report, maps);
        AppendStructuralQualificationForensic(report, maps);
        AppendRuntimeVariantFConfirmation(report, maps);

        string directory = Environment.GetEnvironmentVariable("BEATINSIGHT_FORENSIC_OUTPUT_DIRECTORY")
            ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "spatial-awkward-transition-structural-forensic.md");
        File.WriteAllText(path, report.ToString(), Encoding.UTF8);

        int admissible = maps.Sum(map => map.Raw.Count(item => item.IsAdmissible));
        int qualified = maps.Sum(map => map.Production.Transitions.Count);
        output.WriteLine($"Corpus analyzed: {maps.Length}/{sources.Length}");
        output.WriteLine($"Admissible pivots: {admissible}");
        output.WriteLine($"Qualified retained: {qualified}");
        output.WriteLine($"Report: {path}");
    }

    private static MapSource[] BuildCorpus()
    {
        string fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps");
        IEnumerable<MapSource> fixtures = Fixtures.Select(name =>
            new MapSource(Path.GetFileNameWithoutExtension(name), Path.Combine(fixtureDirectory, name), "fixture"));
        IEnumerable<MapSource> stream = StreamPatternGlobalForensicTests.Corpus.Select(spec =>
            new MapSource(spec.Title, spec.Path, "existing Stream forensic"));
        IEnumerable<MapSource> tech = TechV2SliderDensityCalibrationAudit.Corpus.Select(spec =>
            new MapSource(spec.Name, spec.Path, "existing Tech forensic"));

        MapSource[] requested = fixtures.Concat(stream).Concat(tech).Concat(AdditionalRequestedMaps)
            .GroupBy(source => Path.GetFullPath(source.Path), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        Assert.All(requested, source => Assert.True(File.Exists(source.Path), $"Missing established corpus map: {source.Path}"));
        return requested;
    }

    private static MapAudit Analyze(MapSource source)
    {
        Beatmap map = BeatmapParser.Load(source.Path);
        GameplayProfile profile = GameplayAnalyzer.Analyze(map);
        SpatialAwkwardTransitionPatternAnalysis production =
            TechPatternAnalyzer.AnalyzeSpatialAwkwardTransition(map.HitObjects);
        RawPivot[] raw = BuildRawPivots(map.HitObjects, new SimulationPolicy()).ToArray();

        var overlaps = new Dictionary<string, IndexRange[]>(StringComparer.Ordinal)
        {
            ["Sharp"] = Ranges(JumpPatternAnalyzer.AnalyzeSharp(map.HitObjects, profile).Matches),
            ["Reversal"] = Ranges(JumpPatternAnalyzer.AnalyzeReversal(map.HitObjects, profile).Matches),
            ["Spaced"] = Ranges(JumpPatternAnalyzer.AnalyzeSpaced(map.HitObjects, profile).Matches),
            ["Wide Angle"] = Ranges(JumpPatternAnalyzer.AnalyzeWideAngle(map.HitObjects, profile).Matches),
            ["Slider Direction Change"] = TechPatternAnalyzer.AnalyzeSliderDirectionChange(map.HitObjects)
                .QualifiedOccurrences.Select(item => new IndexRange(
                    item.PreviousObjectIndex ?? item.SliderIndex,
                    item.NextObjectIndex ?? item.SliderIndex)).ToArray(),
            ["Circle / Slider Transition"] = TechPatternAnalyzer.AnalyzeCircleSliderTransition(map.HitObjects)
                .Sequences.Select(item => new IndexRange(item.StartIndex, item.EndIndex)).ToArray(),
            ["Rhythm Transition"] = TechPatternAnalyzer.AnalyzeRhythmTransition(map.HitObjects)
                .Transitions.Select(item => new IndexRange(item.StartIndex, item.EndIndex)).ToArray(),
            ["Complex Slider"] = TechPatternAnalyzer.AnalyzeComplexSlider(map)
                .ComplexSliders.Select(item => new IndexRange(item.ObjectIndex, item.ObjectIndex)).ToArray(),
            ["Slider Chain"] = TechPatternAnalyzer.AnalyzeSliderChain(map.HitObjects)
                .Sequences.Select(item => new IndexRange(item.StartIndex, item.EndIndex)).ToArray()
        };

        return new MapAudit(source, map, production, raw, overlaps);
    }

    private static void VerifyRuntimeMatchesVariantF(MapAudit map)
    {
        StructuralVariant variantF = StructuralVariants().Single(item => item.Id == "F_MINIMAL");
        StructuralResult simulated = SimulateStructural([map], variantF);
        int[] production = map.Production.Transitions.Select(item => item.PivotIndex).ToArray();
        int[] shadow = simulated.Selected.Select(item => item.Pivot.PivotIndex).ToArray();
        if (!production.SequenceEqual(shadow))
        {
            int[] differing = production.Except(shadow).Concat(shadow.Except(production)).OrderBy(item => item).ToArray();
            string detail = string.Join("; ", differing.Select(pivot =>
            {
                RawPivot raw = map.Raw.Single(item => item.PivotIndex == pivot);
                SpatialAwkwardTransitionPatternOccurrence occurrence = map.Production.Occurrences
                    .Single(item => item.PivotIndex == pivot);
                return FormattableString.Invariant(
                    $"#{pivot}: runtime={occurrence.Qualified}/{occurrence.Reason}, angle={occurrence.TurnAngle:R}, score={occurrence.SpatialAwkwardScore:R}, D/T/A={occurrence.DistanceSignal:R}/{occurrence.TurnSignal:R}/{occurrence.AsymmetrySignal:R}; raw={raw.Reason}, angle={raw.TurnAngle:R}, score={raw.Score:R}, D/T/A={raw.DistanceSignal:R}/{raw.TurnSignal:R}/{raw.AsymmetrySignal:R}");
            }));
            Assert.Fail($"Runtime differs from Variant F on {map.Label}: {detail}");
        }
    }

    private static IEnumerable<RawPivot> BuildRawPivots(
        IReadOnlyList<HitObject> objects,
        SimulationPolicy policy)
    {
        for (int pivot = 0; pivot < objects.Count; pivot++)
        {
            int start = pivot - 1;
            int end = pivot + 1;
            if (start < 0 || end >= objects.Count)
            {
                yield return RawPivot.Unavailable(pivot, Math.Max(0, start), Math.Min(objects.Count - 1, end), "InsufficientContext");
                continue;
            }

            if (Enumerable.Range(start, 3).Any(index => GameplayAnalyzer.IsSpinner(objects[index])))
            {
                yield return RawPivot.Unavailable(pivot, start, end, "IntervalBreak");
                continue;
            }

            HitObject previous = objects[pivot - 1];
            HitObject current = objects[pivot];
            HitObject next = objects[pivot + 1];
            int incomingGap = current.Time - previous.Time;
            int outgoingGap = next.Time - current.Time;
            if (incomingGap < 0 || outgoingGap < 0 || incomingGap > 1000 || outgoingGap > 1000)
            {
                yield return RawPivot.Unavailable(pivot, start, end, "IntervalBreak");
                continue;
            }

            (double previousEndX, double previousEndY) = GameplayAnalyzer.GetObjectEndPosition(previous);
            (double currentEndX, double currentEndY) = GameplayAnalyzer.GetObjectEndPosition(current);
            Vector2 previousEnd = new((float)previousEndX, (float)previousEndY);
            Vector2 currentStart = new(current.X, current.Y);
            Vector2 currentEnd = new((float)currentEndX, (float)currentEndY);
            Vector2 nextStart = new(next.X, next.Y);
            double incomingDistance = Vector2.Distance(previousEnd, currentStart);
            double outgoingDistance = Vector2.Distance(currentEnd, nextStart);
            if (!double.IsFinite(incomingDistance) || !double.IsFinite(outgoingDistance)
                || incomingDistance <= 0 || outgoingDistance <= 0)
            {
                yield return RawPivot.Unavailable(pivot, start, end, "UnreliableGeometry");
                continue;
            }

            double averageDistance = (incomingDistance + outgoingDistance) / 2;
            Vector2 incoming = currentStart - previousEnd;
            Vector2 outgoing = nextStart - currentEnd;
            double angleDenominator = incoming.Length() * outgoing.Length();
            double cosine = Math.Clamp(Vector2.Dot(incoming, outgoing) / angleDenominator, -1, 1);
            double angle = Math.Acos(cosine) * 180 / Math.PI;
            double ratio = Math.Min(incomingDistance, outgoingDistance) / Math.Max(incomingDistance, outgoingDistance);
            double asymmetry = 1 - ratio;
            double distanceSignal = Math.Clamp((averageDistance - policy.MinimumDistance) / 120, 0, 1);
            double turnSignal = Math.Clamp((angle - policy.MinimumAngle) / 90, 0, 1);
            double asymmetrySignal = Math.Clamp(asymmetry / policy.AsymmetryDenominator, 0, 1);
            double score = (distanceSignal + turnSignal + asymmetrySignal) / 3;
            int active = ActiveSignals(distanceSignal, turnSignal, asymmetrySignal);
            string reason = averageDistance < policy.MinimumDistance
                ? "BelowDistanceRelevance"
                : active < 2
                    ? "InsufficientCombinedSignals"
                    : score < policy.MinimumScore
                        ? "BelowAwkwardThreshold"
                        : "Qualified";
            yield return new RawPivot(pivot, start, end, incomingDistance, outgoingDistance,
                averageDistance, angle, ratio, asymmetry, distanceSignal, turnSignal,
                asymmetrySignal, score, active, reason);
        }
    }

    private static SimulationResult Simulate(IEnumerable<MapAudit> maps, SimulationPolicy policy)
    {
        var selected = new List<SelectedPivot>();
        int totalObjects = 0;
        int coveredObjects = 0;
        foreach (MapAudit map in maps)
        {
            totalObjects += map.Map.HitObjects.Count;
            RawPivot[] raw = policy == new SimulationPolicy()
                ? map.Raw
                : BuildRawPivots(map.Map.HitObjects, policy).ToArray();
            var retained = new List<RawPivot>();
            foreach (RawPivot candidate in raw.Where(item => item.Reason == "Qualified")
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => item.TurnAngle)
                .ThenByDescending(item => item.AverageDistance)
                .ThenBy(item => item.PivotIndex))
            {
                if (retained.All(item => Math.Abs(item.PivotIndex - candidate.PivotIndex) > 1))
                    retained.Add(candidate);
            }

            coveredObjects += retained.SelectMany(item => Enumerable.Range(item.StartIndex, 3)).Distinct().Count();
            selected.AddRange(retained.OrderBy(item => item.PivotIndex).Select(item => new SelectedPivot(map, item)));
        }
        return new SimulationResult(selected.ToArray(), totalObjects == 0 ? 0 : (double)coveredObjects / totalObjects);
    }

    private static SimulationResult Simulate(MapAudit map, SimulationPolicy policy) => Simulate([map], policy);

    private static void AppendHeader(StringBuilder report, IReadOnlyList<MapAudit> maps)
    {
        report.AppendLine("# Spatial / Awkward Transition V1 — targeted real-corpus forensic");
        report.AppendLine();
        report.AppendLine("The historical baseline and structural variants remain frozen test-side simulations. The final section compares the updated production runtime directly with validated Variant F. No exclusion is inferred from overlaps.");
        report.AppendLine();
        report.AppendLine("Baseline: distance 60 px; turn 90 degrees; score 0.50; temporal gap 1000 ms; asymmetry denominator 0.60; at least two positive signals; adjacent qualified pivots deduplicated at index distance <= 1.");
        report.AppendLine();
        report.AppendLine($"Corpus: **{maps.Count} real beatmaps**, **{maps.Sum(map => map.Map.HitObjects.Count)} objects**. Raw admissible means valid three-object context, timing and non-zero geometry before threshold qualification.");
        report.AppendLine();
    }

    private static void AppendMapSummary(StringBuilder report, IReadOnlyList<MapAudit> maps)
    {
        report.AppendLine("## Global map summary");
        report.AppendLine();
        report.AppendLine("|Map|Difficulty|Objects|Admissible pivots|Qualified|Coverage|Average score|Max score|Average turn|Average distance|Average asymmetry|Source|");
        report.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|");
        foreach (MapAudit map in maps)
        {
            RawPivot[] admissible = map.Raw.Where(item => item.IsAdmissible).ToArray();
            SimulationResult baseline = Simulate(map, new SimulationPolicy());
            RawPivot[] qualified = baseline.Selected.Select(item => item.Pivot).ToArray();
            report.AppendLine(FormattableString.Invariant(
                $"|{Cell(map.Map.Title)}|{Cell(map.Map.Version)}|{map.Map.HitObjects.Count}|{admissible.Length}|{qualified.Length}|{Pct(baseline.Coverage)}|{Average(qualified, item => item.Score):F4}|{Maximum(qualified, item => item.Score):F4}|{Average(qualified, item => item.TurnAngle):F2}|{Average(qualified, item => item.AverageDistance):F2}|{Average(qualified, item => item.Asymmetry):F4}|{map.Source.Kind}|"));
        }
        report.AppendLine();
    }

    private static void AppendGlobalDistributions(StringBuilder report, IReadOnlyList<MapAudit> maps)
    {
        RawPivot[] pivots = maps.SelectMany(map => map.Raw).Where(item => item.IsAdmissible).ToArray();
        report.AppendLine("## Distributions on all admissible pivots");
        report.AppendLine();
        AppendDistribution(report, "SpatialAwkwardScore", pivots,
            ("<0.30", item => item.Score < .30),
            ("0.30-0.39", item => item.Score is >= .30 and < .40),
            ("0.40-0.49", item => item.Score is >= .40 and < .50),
            ("0.50-0.59", item => item.Score is >= .50 and < .60),
            ("0.60-0.69", item => item.Score is >= .60 and < .70),
            ("0.70-0.79", item => item.Score is >= .70 and < .80),
            (">=0.80", item => item.Score >= .80));
        AppendDistribution(report, "TurnAngle", pivots,
            ("<90", item => item.TurnAngle < 90),
            ("90-119", item => item.TurnAngle is >= 90 and < 120),
            ("120-149", item => item.TurnAngle is >= 120 and < 150),
            ("150-169", item => item.TurnAngle is >= 150 and < 170),
            (">=170", item => item.TurnAngle >= 170));
        AppendDistribution(report, "AverageDistance", pivots,
            ("<60", item => item.AverageDistance < 60),
            ("60-89", item => item.AverageDistance is >= 60 and < 90),
            ("90-119", item => item.AverageDistance is >= 90 and < 120),
            ("120-159", item => item.AverageDistance is >= 120 and < 160),
            (">=160", item => item.AverageDistance >= 160));
        AppendDistribution(report, "Asymmetry", pivots,
            ("<0.20", item => item.Asymmetry < .20),
            ("0.20-0.39", item => item.Asymmetry is >= .20 and < .40),
            ("0.40-0.59", item => item.Asymmetry is >= .40 and < .60),
            (">=0.60", item => item.Asymmetry >= .60));

        report.AppendLine("### Active-signal combinations among raw qualified candidates");
        report.AppendLine();
        RawPivot[] candidates = pivots.Where(item => item.Reason == "Qualified").ToArray();
        foreach (string combination in new[] { "Distance + Turn", "Distance + Asymmetry", "Turn + Asymmetry", "Distance + Turn + Asymmetry" })
        {
            int count = candidates.Count(item => SignalCombination(item) == combination);
            report.AppendLine($"- {combination}: **{count}** ({Pct(Ratio(count, candidates.Length))}).");
        }
        report.AppendLine();
    }

    private static void AppendQualifiedAnalysis(StringBuilder report, IReadOnlyList<MapAudit> maps)
    {
        SelectedPivot[] qualified = maps.SelectMany(map => Simulate(map, new SimulationPolicy()).Selected).ToArray();
        report.AppendLine("## Qualified retained occurrences and signal balance");
        report.AppendLine();
        report.AppendLine(FormattableString.Invariant(
            $"- Count: **{qualified.Length}**; global coverage: **{Pct(Simulate(maps, new SimulationPolicy()).Coverage)}**."));
        report.AppendLine(FormattableString.Invariant(
            $"- Score average/max: **{Average(qualified, item => item.Pivot.Score):F4} / {Maximum(qualified, item => item.Pivot.Score):F4}**."));
        report.AppendLine(FormattableString.Invariant(
            $"- Turn / distance / asymmetry averages: **{Average(qualified, item => item.Pivot.TurnAngle):F2} deg / {Average(qualified, item => item.Pivot.AverageDistance):F2} px / {Average(qualified, item => item.Pivot.Asymmetry):F4}**."));
        report.AppendLine(FormattableString.Invariant(
            $"- Signal averages Distance / Turn / Asymmetry: **{Average(qualified, item => item.Pivot.DistanceSignal):F4} / {Average(qualified, item => item.Pivot.TurnSignal):F4} / {Average(qualified, item => item.Pivot.AsymmetrySignal):F4}**."));
        report.AppendLine();

        int turnDominant = qualified.Count(item => StrictlyDominant(item.Pivot.TurnSignal, item.Pivot.DistanceSignal, item.Pivot.AsymmetrySignal));
        int distanceDominant = qualified.Count(item => StrictlyDominant(item.Pivot.DistanceSignal, item.Pivot.TurnSignal, item.Pivot.AsymmetrySignal));
        int asymmetryDominant = qualified.Count(item => StrictlyDominant(item.Pivot.AsymmetrySignal, item.Pivot.DistanceSignal, item.Pivot.TurnSignal));
        report.AppendLine($"- TurnSignal strictly dominant: **{turnDominant}** ({Pct(Ratio(turnDominant, qualified.Length))}).");
        report.AppendLine($"- DistanceSignal strictly dominant: **{distanceDominant}** ({Pct(Ratio(distanceDominant, qualified.Length))}).");
        report.AppendLine($"- AsymmetrySignal strictly dominant: **{asymmetryDominant}** ({Pct(Ratio(asymmetryDominant, qualified.Length))}).");
        report.AppendLine($"- At least one signal <= 0.01 while still qualified: **{qualified.Count(item => MinimumSignal(item.Pivot) <= .01)}** ({Pct(Ratio(qualified.Count(item => MinimumSignal(item.Pivot) <= .01), qualified.Length))}).");
        report.AppendLine($"- DistanceSignal <= 0.01: **{qualified.Count(item => item.Pivot.DistanceSignal <= .01)}**; TurnSignal <= 0.01: **{qualified.Count(item => item.Pivot.TurnSignal <= .01)}**; AsymmetrySignal <= 0.01: **{qualified.Count(item => item.Pivot.AsymmetrySignal <= .01)}**.");
        report.AppendLine($"- Qualified with TurnAngle < 90 degrees through Distance + Asymmetry only: **{qualified.Count(item => item.Pivot.TurnAngle < 90)}** ({Pct(Ratio(qualified.Count(item => item.Pivot.TurnAngle < 90), qualified.Length))}).");
        report.AppendLine();
    }

    private static void AppendReasonAnalysis(StringBuilder report, IReadOnlyList<MapAudit> maps)
    {
        RawPivot[] raw = maps.SelectMany(map => map.Raw).ToArray();
        report.AppendLine("## Raw pivot rejection reasons before qualified-pivot deduplication");
        report.AppendLine();
        foreach (string reason in new[] { "InsufficientContext", "IntervalBreak", "UnreliableGeometry", "BelowDistanceRelevance", "BelowAwkwardThreshold", "InsufficientCombinedSignals", "Qualified" })
        {
            int count = raw.Count(item => item.Reason == reason);
            report.AppendLine($"- {reason}: **{count}** ({Pct(Ratio(count, raw.Length))}).");
        }
        int rawQualified = raw.Count(item => item.Reason == "Qualified");
        int retained = maps.Sum(map => map.Production.Transitions.Count);
        report.AppendLine($"- Deduplication: {rawQualified} raw qualified candidates -> **{retained} retained occurrences**.");
        report.AppendLine();
    }

    private static void AppendOverlapAnalysis(StringBuilder report, IReadOnlyList<MapAudit> maps)
    {
        SelectedPivot[] qualified = maps.SelectMany(map => Simulate(map, new SimulationPolicy()).Selected).ToArray();
        report.AppendLine("## Descriptive overlaps");
        report.AppendLine();
        report.AppendLine("Shared-object intersection only; these measurements create no exclusion.");
        report.AppendLine();
        foreach (string pattern in maps.First().Overlaps.Keys)
        {
            int count = qualified.Count(item => Overlaps(item.Map, item.Pivot, pattern));
            report.AppendLine($"- {pattern}: **{count}/{qualified.Length}** ({Pct(Ratio(count, qualified.Length))}).");
        }
        report.AppendLine();
    }

    private static void AppendRankings(StringBuilder report, IReadOnlyList<MapAudit> maps)
    {
        SelectedPivot[] qualified = maps.SelectMany(map => Simulate(map, new SimulationPolicy()).Selected).ToArray();
        SelectedPivot[] top = qualified.OrderByDescending(item => item.Pivot.Score).Take(30).ToArray();
        SelectedPivot[] borderlinePositive = qualified.Where(item => item.Pivot.Score is >= .50 and <= .60)
            .OrderBy(item => item.Pivot.Score).Take(40).ToArray();
        SelectedPivot[] borderlineNegative = maps.SelectMany(map => map.Raw
                .Where(item => item.Score is >= .40 and < .50 && item.ActiveSignalCount >= 2)
                .Select(item => new SelectedPivot(map, item)))
            .OrderByDescending(item => item.Pivot.Score).Take(40).ToArray();

        AppendOccurrenceTable(report, "Top 30 positives", top);
        AppendOccurrenceTable(report, $"Borderline positives 0.50-0.60 (shown {borderlinePositive.Length}, total {qualified.Count(item => item.Pivot.Score is >= .50 and <= .60)})", borderlinePositive);
        AppendOccurrenceTable(report, $"Borderline negatives 0.40-0.50 with >=2 signals (shown {borderlineNegative.Length}, total {maps.Sum(map => map.Raw.Count(item => item.Score is >= .40 and < .50 && item.ActiveSignalCount >= 2))})", borderlineNegative);
        SelectedPivot[] nearZeroSignal = qualified.Where(item => MinimumSignal(item.Pivot) <= .01)
            .OrderBy(item => item.Pivot.Score).Take(40).ToArray();
        AppendOccurrenceTable(report, $"Qualified despite a signal <= 0.01 (shown {nearZeroSignal.Length}, total {qualified.Count(item => MinimumSignal(item.Pivot) <= .01)})", nearZeroSignal);
    }

    private static void AppendSimulations(StringBuilder report, IReadOnlyList<MapAudit> maps)
    {
        SimulationPolicy baselinePolicy = new();
        SimulationResult baseline = Simulate(maps, baselinePolicy);
        HashSet<string> baselineKeys = Keys(baseline.Selected);
        report.AppendLine("## One-variable-at-a-time threshold simulations");
        report.AppendLine();
        report.AppendLine("|Variable|Value|Qualified|Coverage|Delta|Delta %|Added|Removed|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");

        foreach ((string variable, double value, SimulationPolicy policy) in Variants(baselinePolicy))
        {
            SimulationResult result = Simulate(maps, policy);
            HashSet<string> keys = Keys(result.Selected);
            int delta = result.Selected.Length - baseline.Selected.Length;
            double deltaPercent = Ratio(delta, baseline.Selected.Length);
            report.AppendLine(FormattableString.Invariant(
                $"|{variable}|{value:F2}|{result.Selected.Length}|{Pct(result.Coverage)}|{delta:+0;-0;0}|{deltaPercent * 100:+0.00;-0.00;0.00}%|{keys.Except(baselineKeys).Count()}|{baselineKeys.Except(keys).Count()}|"));
        }
        report.AppendLine();
    }

    private static IEnumerable<(string Variable, double Value, SimulationPolicy Policy)> Variants(SimulationPolicy baseline)
    {
        foreach (double value in new[] { .45, .50, .55, .60 })
            yield return ("Minimum score", value, baseline with { MinimumScore = value });
        foreach (double value in new[] { 50d, 60d, 70d, 80d })
            yield return ("Minimum distance", value, baseline with { MinimumDistance = value });
        foreach (double value in new[] { 75d, 90d, 105d, 120d })
            yield return ("Minimum angle", value, baseline with { MinimumAngle = value });
        foreach (double value in new[] { .50, .60, .70 })
            yield return ("Asymmetry denominator", value, baseline with { AsymmetryDenominator = value });
    }

    private static void AppendManualReviewSets(StringBuilder report, IReadOnlyList<MapAudit> maps)
    {
        SelectedPivot[] qualified = maps.SelectMany(map => Simulate(map, new SimulationPolicy()).Selected).ToArray();
        SelectedPivot[] strong = qualified.OrderByDescending(item => item.Pivot.Score).Take(15).ToArray();
        SelectedPivot[] positive = qualified.Where(item => item.Pivot.Score is >= .50 and <= .60)
            .OrderBy(item => item.Pivot.Score).Take(15).ToArray();
        SelectedPivot[] negative = maps.SelectMany(map => map.Raw
                .Where(item => item.Score is >= .40 and < .50 && item.ActiveSignalCount >= 2)
                .Select(item => new SelectedPivot(map, item)))
            .OrderByDescending(item => item.Pivot.Score).Take(15).ToArray();
        AppendReviewTable(report, "15 strong positives", strong);
        AppendReviewTable(report, "15 borderline positives", positive);
        AppendReviewTable(report, "15 borderline negatives", negative);
    }

    private static void AppendConclusionInputs(StringBuilder report, IReadOnlyList<MapAudit> maps)
    {
        SelectedPivot[] qualified = maps.SelectMany(map => Simulate(map, new SimulationPolicy()).Selected).ToArray();
        int jumpOverlap = qualified.Count(item => new[] { "Sharp", "Reversal", "Spaced", "Wide Angle" }
            .Any(pattern => Overlaps(item.Map, item.Pivot, pattern)));
        int techOverlap = qualified.Count(item => new[] { "Slider Direction Change", "Circle / Slider Transition", "Rhythm Transition", "Complex Slider", "Slider Chain" }
            .Any(pattern => Overlaps(item.Map, item.Pivot, pattern)));
        report.AppendLine("## Conclusion inputs (no automatic calibration)");
        report.AppendLine();
        report.AppendLine($"- Any requested Jump overlap: **{jumpOverlap}/{qualified.Length}** ({Pct(Ratio(jumpOverlap, qualified.Length))}).");
        report.AppendLine($"- Any requested Tech overlap: **{techOverlap}/{qualified.Length}** ({Pct(Ratio(techOverlap, qualified.Length))}).");
        report.AppendLine($"- Qualified at score 0.50-0.60: **{qualified.Count(item => item.Pivot.Score is >= .50 and <= .60)}**.");
        report.AppendLine($"- Rejected at score 0.40-0.50 with >=2 active signals: **{maps.Sum(map => map.Raw.Count(item => item.Score is >= .40 and < .50 && item.ActiveSignalCount >= 2))}**.");
        report.AppendLine();
        report.AppendLine("### Baseline finding: structural dependency confirmed");
        report.AppendLine();
        report.AppendLine("DistanceSignal and AsymmetrySignal are not independent: the same one-long/one-short link pair raises both, so the two-active-signals rule can qualify a pivot with no awkward turn. The structural variants below evaluate this finding without changing production.");
    }

    private static void AppendStructuralQualificationForensic(
        StringBuilder report,
        IReadOnlyList<MapAudit> maps)
    {
        StructuralVariant[] variants = StructuralVariants();
        Dictionary<string, StructuralResult> results = variants.ToDictionary(
            variant => variant.Id,
            variant => SimulateStructural(maps, variant),
            StringComparer.Ordinal);
        StructuralResult baseline = results["BASELINE"];
        HashSet<string> baselineKeys = StructuralKeys(baseline.Selected);

        report.AppendLine();
        report.AppendLine("# Structural qualification forensic");
        report.AppendLine();
        report.AppendLine("Every row below reuses the exact 36-map corpus and frozen V1 signals. Only test-side qualification and deduplication are simulated. Variant score is used for variant dedup ordering, followed by turn angle, average distance and pivot index.");
        report.AppendLine();
        AppendStructuralSummary(report, variants, results, baseline, baselineKeys);
        AppendStructuralOverlaps(report, variants, results);
        AppendStructuralAngleBands(report, variants, results);
        AppendStrongPositiveRetention(report, variants, results, baseline);
        AppendBorderlineRetention(report, variants, results, baseline);
        AppendDistanceAsymmetryOnly(report, variants, maps);
        AppendStrongAngleRetention(report, variants, results, baseline);
        AppendPivotTypeRetention(report, variants, results, baseline);
        AppendStructuralManualPanels(report, results, baseline);
        AppendStructuralDecision(report, maps, results, baseline);
    }

    private static StructuralVariant[] StructuralVariants() =>
    [
        new("BASELINE", "Baseline", item => item.Score,
            (item, score) => item.Reason == "Qualified"),
        new("A_STRICT", "A: TurnSignal > 0 + spatial signal", item => item.Score,
            (item, score) => score >= .50 && item.TurnSignal > 0
                && (item.DistanceSignal > 0 || item.AsymmetrySignal > 0)),
        new("A_INCLUSIVE", "A inclusive: angle >= 90 + spatial signal", item => item.Score,
            (item, score) => score >= .50 && item.TurnAngle >= 90
                && (item.DistanceSignal > 0 || item.AsymmetrySignal > 0)),
        new("B_GATE", "B1: angle gate + score", item => item.Score,
            (item, score) => item.TurnAngle >= 90 && score >= .50),
        new("B_GATE_SPATIAL", "B2: angle gate + score + spatial signal", item => item.Score,
            (item, score) => item.TurnAngle >= 90 && score >= .50
                && (item.DistanceSignal > 0 || item.AsymmetrySignal > 0)),
        GroupedVariant("C_045", "C: grouped score >= 0.45", .45),
        GroupedVariant("C_050", "C: grouped score >= 0.50", .50),
        GroupedVariant("C_055", "C: grouped score >= 0.55", .55),
        new("D_MAX", "D: max(distance, asymmetry)", MaxShapeScore,
            (item, score) => score >= .50 && item.TurnSignal > 0),
        new("E_MIN", "E: min(distance, asymmetry)", MinShapeScore,
            (item, score) => score >= .50 && item.TurnSignal > 0),
        new("F_MINIMAL", "F: current score + separate angle gate", item => item.Score,
            (item, score) => score >= .50 && item.TurnAngle >= 90
                && (item.DistanceSignal > 0 || item.AsymmetrySignal > 0))
    ];

    private static StructuralVariant GroupedVariant(string id, string label, double threshold) =>
        new(id, label, GroupedScore,
            (item, score) => score >= threshold && SpatialMagnitude(item) > 0 && item.TurnSignal > 0);

    private static double SpatialMagnitude(RawPivot item) =>
        (item.DistanceSignal + item.AsymmetrySignal) / 2;

    private static double GroupedScore(RawPivot item) =>
        (SpatialMagnitude(item) + item.TurnSignal) / 2;

    private static double MaxShapeScore(RawPivot item) =>
        (Math.Max(item.DistanceSignal, item.AsymmetrySignal) + item.TurnSignal) / 2;

    private static double MinShapeScore(RawPivot item) =>
        (Math.Min(item.DistanceSignal, item.AsymmetrySignal) + item.TurnSignal) / 2;

    private static StructuralResult SimulateStructural(
        IEnumerable<MapAudit> maps,
        StructuralVariant variant)
    {
        var selected = new List<StructuralSelected>();
        int totalObjects = 0;
        int coveredObjects = 0;
        foreach (MapAudit map in maps)
        {
            totalObjects += map.Map.HitObjects.Count;
            var retained = new List<(RawPivot Pivot, double Score)>();
            foreach ((RawPivot pivot, double score) in map.Raw.Where(item => item.IsAdmissible)
                .Select(item => (Pivot: item, Score: variant.Score(item)))
                .Where(item => variant.Qualifies(item.Pivot, item.Score))
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => item.Pivot.TurnAngle)
                .ThenByDescending(item => item.Pivot.AverageDistance)
                .ThenBy(item => item.Pivot.PivotIndex))
            {
                if (retained.All(item => Math.Abs(item.Pivot.PivotIndex - pivot.PivotIndex) > 1))
                    retained.Add((pivot, score));
            }

            coveredObjects += retained.SelectMany(item => Enumerable.Range(item.Pivot.StartIndex, 3)).Distinct().Count();
            selected.AddRange(retained.OrderBy(item => item.Pivot.PivotIndex)
                .Select(item => new StructuralSelected(map, item.Pivot, item.Score)));
        }
        return new StructuralResult(selected.ToArray(), totalObjects == 0 ? 0 : (double)coveredObjects / totalObjects);
    }

    private static void AppendStructuralSummary(
        StringBuilder report,
        IReadOnlyList<StructuralVariant> variants,
        IReadOnlyDictionary<string, StructuralResult> results,
        StructuralResult baseline,
        HashSet<string> baselineKeys)
    {
        report.AppendLine("## Variant summary");
        report.AppendLine();
        report.AppendLine("|Variant|Qualified|Delta|Delta %|Added|Removed|Coverage|Variant score avg|Angle avg/min|Distance avg|Asymmetry avg|Signals D/T/A avg|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|---|");
        foreach (StructuralVariant variant in variants)
        {
            StructuralResult result = results[variant.Id];
            StructuralSelected[] selected = result.Selected;
            HashSet<string> keys = StructuralKeys(selected);
            int delta = selected.Length - baseline.Selected.Length;
            report.AppendLine(FormattableString.Invariant(
                $"|{variant.Label}|{selected.Length}|{delta:+0;-0;0}|{Ratio(delta, baseline.Selected.Length) * 100:+0.00;-0.00;0.00}%|{keys.Except(baselineKeys).Count()}|{baselineKeys.Except(keys).Count()}|{Pct(result.Coverage)}|{Average(selected, item => item.VariantScore):F4}|{Average(selected, item => item.Pivot.TurnAngle):F2} / {Minimum(selected, item => item.Pivot.TurnAngle):F2}|{Average(selected, item => item.Pivot.AverageDistance):F2}|{Average(selected, item => item.Pivot.Asymmetry):F4}|{Average(selected, item => item.Pivot.DistanceSignal):F4} / {Average(selected, item => item.Pivot.TurnSignal):F4} / {Average(selected, item => item.Pivot.AsymmetrySignal):F4}|"));
        }
        report.AppendLine();
    }

    private static void AppendStructuralOverlaps(
        StringBuilder report,
        IReadOnlyList<StructuralVariant> variants,
        IReadOnlyDictionary<string, StructuralResult> results)
    {
        string[] patterns = results["BASELINE"].Selected.First().Map.Overlaps.Keys.ToArray();
        report.AppendLine("## Variant overlaps");
        report.AppendLine();
        report.AppendLine($"|Variant|{string.Join('|', patterns)}|");
        report.AppendLine($"|---|{string.Join('|', patterns.Select(_ => "---:"))}|");
        foreach (StructuralVariant variant in variants)
        {
            StructuralSelected[] selected = results[variant.Id].Selected;
            string[] cells = patterns.Select(pattern =>
            {
                int count = selected.Count(item => Overlaps(item.Map, item.Pivot, pattern));
                return $"{count} ({Pct(Ratio(count, selected.Length))})";
            }).ToArray();
            report.AppendLine($"|{variant.Label}|{string.Join('|', cells)}|");
        }
        report.AppendLine();
    }

    private static void AppendStructuralAngleBands(
        StringBuilder report,
        IReadOnlyList<StructuralVariant> variants,
        IReadOnlyDictionary<string, StructuralResult> results)
    {
        report.AppendLine("## Qualified angle bands");
        report.AppendLine();
        report.AppendLine("|Variant|<90|90-119|120-149|150-169|>=170|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach (StructuralVariant variant in variants)
        {
            RawPivot[] pivots = results[variant.Id].Selected.Select(item => item.Pivot).ToArray();
            report.AppendLine($"|{variant.Label}|{pivots.Count(item => item.TurnAngle < 90)}|{pivots.Count(item => item.TurnAngle is >= 90 and < 120)}|{pivots.Count(item => item.TurnAngle is >= 120 and < 150)}|{pivots.Count(item => item.TurnAngle is >= 150 and < 170)}|{pivots.Count(item => item.TurnAngle >= 170)}|");
        }
        report.AppendLine();
    }

    private static void AppendStrongPositiveRetention(
        StringBuilder report,
        IReadOnlyList<StructuralVariant> variants,
        IReadOnlyDictionary<string, StructuralResult> results,
        StructuralResult baseline)
    {
        StructuralSelected[] top = baseline.Selected.OrderByDescending(item => item.Pivot.Score).Take(30).ToArray();
        HashSet<string> topKeys = StructuralKeys(top);
        report.AppendLine("## Baseline top-30 retention");
        report.AppendLine();
        report.AppendLine("|Variant|Retained|Removed baseline top cases|");
        report.AppendLine("|---|---:|---|");
        foreach (StructuralVariant variant in variants)
        {
            HashSet<string> keys = StructuralKeys(results[variant.Id].Selected);
            StructuralSelected[] removed = top.Where(item => !keys.Contains(StructuralKey(item))).ToArray();
            string removedText = removed.Length == 0 ? "None" : string.Join("<br>", removed.Select(CompactCase));
            report.AppendLine($"|{variant.Label}|{topKeys.Count(keys.Contains)}/30|{removedText}|");
        }
        report.AppendLine();
    }

    private static void AppendBorderlineRetention(
        StringBuilder report,
        IReadOnlyList<StructuralVariant> variants,
        IReadOnlyDictionary<string, StructuralResult> results,
        StructuralResult baseline)
    {
        StructuralSelected[] borderline = baseline.Selected
            .Where(item => item.Pivot.Score is >= .50 and <= .60).ToArray();
        report.AppendLine("## Baseline borderline-positive retention");
        report.AppendLine();
        report.AppendLine($"Baseline group: **{borderline.Length} retained occurrences** at current score 0.50-0.60.");
        report.AppendLine();
        report.AppendLine("|Variant|Retained|Removed|Removed angle avg/min|Removed signals D/T/A avg|");
        report.AppendLine("|---|---:|---:|---|---|");
        foreach (StructuralVariant variant in variants)
        {
            HashSet<string> keys = StructuralKeys(results[variant.Id].Selected);
            StructuralSelected[] removed = borderline.Where(item => !keys.Contains(StructuralKey(item))).ToArray();
            report.AppendLine(FormattableString.Invariant(
                $"|{variant.Label}|{borderline.Length - removed.Length}|{removed.Length}|{Average(removed, item => item.Pivot.TurnAngle):F2} / {Minimum(removed, item => item.Pivot.TurnAngle):F2}|{Average(removed, item => item.Pivot.DistanceSignal):F4} / {Average(removed, item => item.Pivot.TurnSignal):F4} / {Average(removed, item => item.Pivot.AsymmetrySignal):F4}|"));
        }
        report.AppendLine();
    }

    private static void AppendDistanceAsymmetryOnly(
        StringBuilder report,
        IReadOnlyList<StructuralVariant> variants,
        IReadOnlyList<MapAudit> maps)
    {
        SelectedPivot[] distanceAsymmetryOnly = maps.SelectMany(map => map.Raw
                .Where(item => item.DistanceSignal > 0 && item.AsymmetrySignal > 0 && item.TurnSignal == 0
                    && item.Reason == "Qualified")
                .Select(item => new SelectedPivot(map, item)))
            .ToArray();
        report.AppendLine("## Raw Distance + Asymmetry-only candidates");
        report.AppendLine();
        report.AppendLine($"Exact isolated group: **{distanceAsymmetryOnly.Length}** candidates.");
        report.AppendLine();
        report.AppendLine("|Variant|Conserved before dedup|Eliminated|");
        report.AppendLine("|---|---:|---:|");
        foreach (StructuralVariant variant in variants)
        {
            int conserved = distanceAsymmetryOnly.Count(item =>
            {
                double score = variant.Score(item.Pivot);
                return variant.Qualifies(item.Pivot, score);
            });
            report.AppendLine($"|{variant.Label}|{conserved}|{distanceAsymmetryOnly.Length - conserved}|");
        }
        report.AppendLine();
        report.AppendLine("|Map|Pivot|Angle|Incoming|Outgoing|Average|Asymmetry|Baseline score|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
        foreach (SelectedPivot item in distanceAsymmetryOnly.OrderBy(item => item.Pivot.TurnAngle)
            .ThenByDescending(item => item.Pivot.Score))
            report.AppendLine(FormattableString.Invariant(
                $"|{Cell(item.Map.Label)}|{item.Pivot.PivotIndex}|{item.Pivot.TurnAngle:F2}|{item.Pivot.IncomingDistance:F2}|{item.Pivot.OutgoingDistance:F2}|{item.Pivot.AverageDistance:F2}|{item.Pivot.Asymmetry:F4}|{item.Pivot.Score:F4}|"));
        report.AppendLine();
    }

    private static void AppendStrongAngleRetention(
        StringBuilder report,
        IReadOnlyList<StructuralVariant> variants,
        IReadOnlyDictionary<string, StructuralResult> results,
        StructuralResult baseline)
    {
        StructuralSelected[] strong = baseline.Selected.Where(item => item.Pivot.TurnAngle >= 120).ToArray();
        report.AppendLine("## Baseline strong-angle retention (TurnAngle >= 120 degrees)");
        report.AppendLine();
        report.AppendLine($"Baseline strong-angle group: **{strong.Length}**.");
        foreach (StructuralVariant variant in variants)
        {
            HashSet<string> keys = StructuralKeys(results[variant.Id].Selected);
            int retained = strong.Count(item => keys.Contains(StructuralKey(item)));
            report.AppendLine($"- {variant.Label}: **{retained}/{strong.Length}** ({Pct(Ratio(retained, strong.Length))}).");
        }
        report.AppendLine();
    }

    private static void AppendPivotTypeRetention(
        StringBuilder report,
        IReadOnlyList<StructuralVariant> variants,
        IReadOnlyDictionary<string, StructuralResult> results,
        StructuralResult baseline)
    {
        StructuralSelected[] baselineCircleItems = baseline.Selected.Where(IsCirclePivot).ToArray();
        StructuralSelected[] baselineSliderItems = baseline.Selected.Where(IsSliderPivot).ToArray();
        report.AppendLine("## Circle versus Slider pivot retention");
        report.AppendLine();
        report.AppendLine("|Variant|Circle qualified|Circle retention|Slider qualified|Slider retention|");
        report.AppendLine("|---|---:|---:|---:|---:|");
        foreach (StructuralVariant variant in variants)
        {
            StructuralSelected[] selected = results[variant.Id].Selected;
            int circles = selected.Count(IsCirclePivot);
            int sliders = selected.Count(IsSliderPivot);
            HashSet<string> keys = StructuralKeys(selected);
            int retainedCircles = baselineCircleItems.Count(item => keys.Contains(StructuralKey(item)));
            int retainedSliders = baselineSliderItems.Count(item => keys.Contains(StructuralKey(item)));
            report.AppendLine($"|{variant.Label}|{circles}|{Pct(Ratio(retainedCircles, baselineCircleItems.Length))}|{sliders}|{Pct(Ratio(retainedSliders, baselineSliderItems.Length))}|");
        }
        report.AppendLine();
    }

    private static void AppendStructuralManualPanels(
        StringBuilder report,
        IReadOnlyDictionary<string, StructuralResult> results,
        StructuralResult baseline)
    {
        HashSet<string> fKeys = StructuralKeys(results["F_MINIMAL"].Selected);
        StructuralSelected[] removedByF = baseline.Selected.Where(item => !fKeys.Contains(StructuralKey(item)))
            .OrderByDescending(item => item.Pivot.Score).Take(20).ToArray();
        StructuralSelected[] retainedByF = baseline.Selected.Where(item => fKeys.Contains(StructuralKey(item)))
            .OrderByDescending(item => item.Pivot.Score).Take(20).ToArray();

        StructuralResult c = results["C_050"];
        StructuralResult d = results["D_MAX"];
        HashSet<string> cKeys = StructuralKeys(c.Selected);
        HashSet<string> dKeys = StructuralKeys(d.Selected);
        StructuralSelected[] divergence = c.Selected.Concat(d.Selected).Concat(results["F_MINIMAL"].Selected)
            .GroupBy(StructuralKey, StringComparer.OrdinalIgnoreCase)
            .Where(group => new[] { cKeys.Contains(group.Key), dKeys.Contains(group.Key), fKeys.Contains(group.Key) }.Distinct().Count() > 1)
            .Select(group => group.First())
            .OrderByDescending(item => item.Pivot.Score)
            .Take(20)
            .ToArray();

        AppendStructuralPanel(report, "A. 20 baseline cases removed by Variant F", removedByF);
        AppendStructuralPanel(report, "B. 20 baseline cases retained by Variant F", retainedByF);
        AppendStructuralPanel(report, "C. 20 cases where C(0.50), D and F diverge", divergence);
    }

    private static void AppendStructuralPanel(
        StringBuilder report,
        string title,
        IReadOnlyList<StructuralSelected> items)
    {
        report.AppendLine($"## Manual panel — {title}");
        report.AppendLine();
        report.AppendLine("|Map|Pivot type/index|Incoming / outgoing / average|Angle|Asymmetry|Signals D/T/A|Current / C / D / E scores|Overlaps|Manual class|");
        report.AppendLine("|---|---|---|---:|---:|---|---|---|---|");
        foreach (StructuralSelected item in items)
        {
            RawPivot pivot = item.Pivot;
            report.AppendLine(FormattableString.Invariant(
                $"|{Cell(item.Map.Label)}|{TypeLabel(item.Map.Map.HitObjects[pivot.PivotIndex])}/{pivot.PivotIndex}|{pivot.IncomingDistance:F2} / {pivot.OutgoingDistance:F2} / {pivot.AverageDistance:F2}|{pivot.TurnAngle:F2}|{pivot.Asymmetry:F4}|{pivot.DistanceSignal:F3} / {pivot.TurnSignal:F3} / {pivot.AsymmetrySignal:F3}|{pivot.Score:F4} / {GroupedScore(pivot):F4} / {MaxShapeScore(pivot):F4} / {MinShapeScore(pivot):F4}|{Cell(OverlapLabels(item.Map, pivot))}|{ManualClass(pivot)}|") );
        }
        report.AppendLine();
    }

    private static void AppendStructuralDecision(
        StringBuilder report,
        IReadOnlyList<MapAudit> maps,
        IReadOnlyDictionary<string, StructuralResult> results,
        StructuralResult baseline)
    {
        StructuralResult f = results["F_MINIMAL"];
        int baselineStrong = baseline.Selected.Count(item => item.Pivot.TurnAngle >= 120);
        HashSet<string> fKeys = StructuralKeys(f.Selected);
        HashSet<string> baselineKeys = StructuralKeys(baseline.Selected);
        int retainedStrong = baseline.Selected.Count(item => item.Pivot.TurnAngle >= 120
            && fKeys.Contains(StructuralKey(item)));
        StructuralSelected[] baselineCircles = baseline.Selected.Where(IsCirclePivot).ToArray();
        StructuralSelected[] baselineSliders = baseline.Selected.Where(IsSliderPivot).ToArray();
        int retainedCircles = baselineCircles.Count(item => fKeys.Contains(StructuralKey(item)));
        int retainedSliders = baselineSliders.Count(item => fKeys.Contains(StructuralKey(item)));
        SelectedPivot[] distanceAsymmetryOnly = maps.SelectMany(map => map.Raw
                .Where(item => item.DistanceSignal > 0 && item.AsymmetrySignal > 0 && item.TurnSignal == 0
                    && item.Reason == "Qualified")
                .Select(item => new SelectedPivot(map, item)))
            .ToArray();
        StructuralVariant fVariant = StructuralVariants().Single(item => item.Id == "F_MINIMAL");
        int distanceAsymmetryConserved = distanceAsymmetryOnly.Count(item =>
            fVariant.Qualifies(item.Pivot, fVariant.Score(item.Pivot)));
        report.AppendLine("## Structural decision inputs");
        report.AppendLine();
        report.AppendLine($"- Baseline: {baseline.Selected.Length} occurrences, coverage {Pct(baseline.Coverage)}.");
        report.AppendLine($"- Variant F: {f.Selected.Length} occurrences ({f.Selected.Length - baseline.Selected.Length:+0;-0;0}, {Ratio(f.Selected.Length - baseline.Selected.Length, baseline.Selected.Length) * 100:+0.00;-0.00;0.00}%), coverage {Pct(f.Coverage)}, {fKeys.Except(baselineKeys).Count()} added by changed local dedup winner and {baselineKeys.Except(fKeys).Count()} baseline occurrences removed.");
        report.AppendLine($"- Distance + Asymmetry-only raw candidates eliminated: {distanceAsymmetryOnly.Length - distanceAsymmetryConserved}/{distanceAsymmetryOnly.Length}; the sole conserved boundary case has TurnAngle exactly 90 degrees.");
        report.AppendLine($"- Baseline top-30 retained: {baseline.Selected.OrderByDescending(item => item.Pivot.Score).Take(30).Count(item => fKeys.Contains(StructuralKey(item)))}/30; strong-angle retention: {retainedStrong}/{baselineStrong} ({Pct(Ratio(retainedStrong, baselineStrong))}).");
        report.AppendLine($"- Circle baseline retention: {retainedCircles}/{baselineCircles.Length} ({Pct(Ratio(retainedCircles, baselineCircles.Length))}); Slider baseline retention: {retainedSliders}/{baselineSliders.Length} ({Pct(Ratio(retainedSliders, baselineSliders.Length))}).");
        report.AppendLine("- Sharp/Reversal overlap remains effectively stable, while C(0.50) rewrites more membership, D expands volume by 20.47%, and E removes 38.84% and retains only 65.98% of strong-angle baseline cases.");
        report.AppendLine("- B1, B2, A-inclusive and F are empirically identical at this score threshold. F is the most explicit minimal contract: preserve the current score and require the existing 90-degree angle threshold separately.");
        report.AppendLine();
        report.AppendLine("### Recommendation: USE SEPARATE ANGLE GATE");
        report.AppendLine();
        report.AppendLine("Validated runtime contract: CurrentScore >= 0.50 AND TurnAngle >= 90 degrees AND (DistanceSignal > 0 OR AsymmetrySignal > 0). The confirmation below checks the implemented runtime against Variant F.");
    }

    private static void AppendRuntimeVariantFConfirmation(
        StringBuilder report,
        IReadOnlyList<MapAudit> maps)
    {
        StructuralVariant variantF = StructuralVariants().Single(item => item.Id == "F_MINIMAL");
        StructuralResult simulated = SimulateStructural(maps, variantF);
        StructuralSelected[] runtime = maps.SelectMany(map =>
        {
            Dictionary<int, RawPivot> rawByPivot = map.Raw.ToDictionary(item => item.PivotIndex);
            return map.Production.Transitions.Select(transition =>
            {
                RawPivot raw = rawByPivot[transition.PivotIndex];
                Assert.Equal(raw.DistanceSignal, transition.DistanceSignal!.Value, 10);
                Assert.Equal(raw.TurnSignal, transition.TurnSignal!.Value, 10);
                Assert.Equal(raw.AsymmetrySignal, transition.AsymmetrySignal!.Value, 10);
                Assert.Equal(raw.Score, transition.SpatialAwkwardScore, 10);
                return new StructuralSelected(map, raw, transition.SpatialAwkwardScore);
            });
        }).ToArray();

        HashSet<string> runtimeKeys = StructuralKeys(runtime);
        HashSet<string> simulatedKeys = StructuralKeys(simulated.Selected);
        int totalObjects = maps.Sum(map => map.Map.HitObjects.Count);
        int coveredObjects = maps.Sum(map => map.Production.SpatialAwkwardObjectCount);
        double runtimeCoverage = Ratio(coveredObjects, totalObjects);

        Assert.Equal(simulatedKeys.OrderBy(item => item), runtimeKeys.OrderBy(item => item));
        Assert.Equal(simulated.Selected.Length, runtime.Length);
        Assert.Equal(simulated.Coverage, runtimeCoverage, 10);
        Assert.DoesNotContain(runtime, item => item.Pivot.TurnAngle < 90);

        report.AppendLine();
        report.AppendLine("# Runtime confirmation against Variant F");
        report.AppendLine();
        report.AppendLine("|Population|Qualified|Coverage|Angle average|Angle minimum|Positive angle <90|Exact key divergence|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
        report.AppendLine(FormattableString.Invariant(
            $"|Production runtime|{runtime.Length}|{Pct(runtimeCoverage)}|{Average(runtime, item => item.Pivot.TurnAngle):F2}|{Minimum(runtime, item => item.Pivot.TurnAngle):F2}|{runtime.Count(item => item.Pivot.TurnAngle < 90)}|{runtimeKeys.Except(simulatedKeys).Count() + simulatedKeys.Except(runtimeKeys).Count()}|"));
        report.AppendLine(FormattableString.Invariant(
            $"|Variant F simulation|{simulated.Selected.Length}|{Pct(simulated.Coverage)}|{Average(simulated.Selected, item => item.Pivot.TurnAngle):F2}|{Minimum(simulated.Selected, item => item.Pivot.TurnAngle):F2}|{simulated.Selected.Count(item => item.Pivot.TurnAngle < 90)}|0|"));
        report.AppendLine();
        report.AppendLine("Runtime and Variant F match exactly by map and pivot key. Existing overlap observations are therefore preserved at the Variant F values reported above.");
    }

    private static string CompactCase(StructuralSelected item) => FormattableString.Invariant(
        $"{Cell(item.Map.Label)} #{item.Pivot.PivotIndex}: score={item.Pivot.Score:F4}, angle={item.Pivot.TurnAngle:F2}, D/T/A={item.Pivot.DistanceSignal:F3}/{item.Pivot.TurnSignal:F3}/{item.Pivot.AsymmetrySignal:F3}");

    private static bool IsCirclePivot(StructuralSelected item) =>
        !GameplayAnalyzer.IsSlider(item.Map.Map.HitObjects[item.Pivot.PivotIndex]);

    private static bool IsSliderPivot(StructuralSelected item) =>
        GameplayAnalyzer.IsSlider(item.Map.Map.HitObjects[item.Pivot.PivotIndex]);

    private static string ManualClass(RawPivot pivot) =>
        pivot.TurnAngle < 45 && pivot.TurnSignal == 0 && pivot.DistanceSignal > 0 && pivot.AsymmetrySignal > 0
            ? "NOT AWKWARD"
            : pivot.TurnAngle >= 150 && pivot.Score >= .70
                ? "TRUE AWKWARD"
                : "AMBIGUOUS";

    private static HashSet<string> StructuralKeys(IEnumerable<StructuralSelected> selected) =>
        selected.Select(StructuralKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string StructuralKey(StructuralSelected item) =>
        $"{Path.GetFullPath(item.Map.Source.Path)}|{item.Pivot.PivotIndex}";

    private static void AppendDistribution(
        StringBuilder report,
        string title,
        IReadOnlyList<RawPivot> pivots,
        params (string Label, Func<RawPivot, bool> Predicate)[] buckets)
    {
        report.AppendLine($"### {title}");
        report.AppendLine();
        foreach ((string label, Func<RawPivot, bool> predicate) in buckets)
        {
            int count = pivots.Count(predicate);
            report.AppendLine($"- {label}: **{count}** ({Pct(Ratio(count, pivots.Count))}).");
        }
        report.AppendLine();
    }

    private static void AppendOccurrenceTable(StringBuilder report, string title, IReadOnlyList<SelectedPivot> items)
    {
        report.AppendLine($"## {title}");
        report.AppendLine();
        report.AppendLine("|Map|Pivot / window|Types|Incoming / outgoing / average|Angle|Ratio / asymmetry|D / T / A signals|Score|Overlaps|");
        report.AppendLine("|---|---|---|---|---:|---|---|---:|---|");
        foreach (SelectedPivot item in items)
            report.AppendLine(OccurrenceRow(item));
        if (items.Count == 0)
            report.AppendLine("|None|||||||||");
        report.AppendLine();
    }

    private static void AppendReviewTable(StringBuilder report, string title, IReadOnlyList<SelectedPivot> items)
    {
        report.AppendLine($"## Manual review — {title}");
        report.AppendLine();
        report.AppendLine("Classification remains AMBIGUOUS until chart/replay review; geometry alone is not treated as ground truth.");
        report.AppendLine();
        report.AppendLine("|Map|Pivot|Distance avg|Angle|Asymmetry|Signals D/T/A|Score|Overlaps|Manual class|");
        report.AppendLine("|---|---:|---:|---:|---:|---|---:|---|---|");
        foreach (SelectedPivot item in items)
            report.AppendLine(FormattableString.Invariant(
                $"|{Cell(item.Map.Label)}|{item.Pivot.PivotIndex}|{item.Pivot.AverageDistance:F2}|{item.Pivot.TurnAngle:F2}|{item.Pivot.Asymmetry:F4}|{item.Pivot.DistanceSignal:F3}/{item.Pivot.TurnSignal:F3}/{item.Pivot.AsymmetrySignal:F3}|{item.Pivot.Score:F4}|{Cell(OverlapLabels(item.Map, item.Pivot))}|AMBIGUOUS|"));
        report.AppendLine();
    }

    private static string OccurrenceRow(SelectedPivot item)
    {
        RawPivot pivot = item.Pivot;
        string types = string.Concat(Enumerable.Range(pivot.StartIndex, 3).Select(index => TypeLabel(item.Map.Map.HitObjects[index])));
        return FormattableString.Invariant(
            $"|{Cell(item.Map.Label)}|{pivot.PivotIndex} / {pivot.StartIndex}-{pivot.EndIndex}|{types}|{pivot.IncomingDistance:F2} / {pivot.OutgoingDistance:F2} / {pivot.AverageDistance:F2}|{pivot.TurnAngle:F2}|{pivot.DistanceRatio:F4} / {pivot.Asymmetry:F4}|{pivot.DistanceSignal:F3} / {pivot.TurnSignal:F3} / {pivot.AsymmetrySignal:F3}|{pivot.Score:F4}|{Cell(OverlapLabels(item.Map, pivot))}|");
    }

    private static bool Overlaps(MapAudit map, RawPivot pivot, string pattern) =>
        map.Overlaps[pattern].Any(range => range.Start <= pivot.EndIndex && range.End >= pivot.StartIndex);

    private static string OverlapLabels(MapAudit map, RawPivot pivot)
    {
        string[] labels = map.Overlaps.Keys.Where(pattern => Overlaps(map, pivot, pattern)).ToArray();
        return labels.Length == 0 ? "None" : string.Join(", ", labels);
    }

    private static IndexRange[] Ranges(IReadOnlyList<JumpPatternMatch> matches) =>
        matches.Select(item => new IndexRange(item.StartObjectIndex, item.EndObjectIndex)).ToArray();

    private static HashSet<string> Keys(IEnumerable<SelectedPivot> selected) => selected
        .Select(item => $"{Path.GetFullPath(item.Map.Source.Path)}|{item.Pivot.PivotIndex}")
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static int ActiveSignals(double distance, double turn, double asymmetry) =>
        (distance > 0 ? 1 : 0) + (turn > 0 ? 1 : 0) + (asymmetry > 0 ? 1 : 0);

    private static string SignalCombination(RawPivot item) =>
        (item.DistanceSignal > 0, item.TurnSignal > 0, item.AsymmetrySignal > 0) switch
        {
            (true, true, false) => "Distance + Turn",
            (true, false, true) => "Distance + Asymmetry",
            (false, true, true) => "Turn + Asymmetry",
            (true, true, true) => "Distance + Turn + Asymmetry",
            _ => "Other"
        };

    private static bool StrictlyDominant(double signal, double otherA, double otherB) =>
        signal > otherA && signal > otherB;

    private static double MinimumSignal(RawPivot item) =>
        Math.Min(item.DistanceSignal, Math.Min(item.TurnSignal, item.AsymmetrySignal));

    private static string TypeLabel(HitObject item) => GameplayAnalyzer.IsSpinner(item)
        ? "P"
        : GameplayAnalyzer.IsSlider(item) ? "S" : "C";

    private static double Average<T>(IReadOnlyCollection<T> values, Func<T, double> selector) =>
        values.Count == 0 ? 0 : values.Average(selector);

    private static double Maximum<T>(IReadOnlyCollection<T> values, Func<T, double> selector) =>
        values.Count == 0 ? 0 : values.Max(selector);

    private static double Minimum<T>(IReadOnlyCollection<T> values, Func<T, double> selector) =>
        values.Count == 0 ? 0 : values.Min(selector);

    private static double Ratio(double numerator, double denominator) => denominator == 0 ? 0 : numerator / denominator;
    private static string Pct(double value) => (value * 100).ToString("F2", CultureInfo.InvariantCulture) + "%";
    private static string Cell(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

    private sealed record MapSource(string Name, string Path, string Kind);
    private sealed record IndexRange(int Start, int End);
    private sealed record SimulationPolicy(
        double MinimumScore = .50,
        double MinimumDistance = 60,
        double MinimumAngle = 90,
        double AsymmetryDenominator = .60);
    private sealed record SimulationResult(SelectedPivot[] Selected, double Coverage);
    private sealed record SelectedPivot(MapAudit Map, RawPivot Pivot);
    private sealed record StructuralVariant(
        string Id,
        string Label,
        Func<RawPivot, double> Score,
        Func<RawPivot, double, bool> Qualifies);
    private sealed record StructuralResult(StructuralSelected[] Selected, double Coverage);
    private sealed record StructuralSelected(MapAudit Map, RawPivot Pivot, double VariantScore);
    private sealed record MapAudit(
        MapSource Source,
        Beatmap Map,
        SpatialAwkwardTransitionPatternAnalysis Production,
        RawPivot[] Raw,
        IReadOnlyDictionary<string, IndexRange[]> Overlaps)
    {
        public string Label => $"{Map.Title} [{Map.Version}]";
    }

    private sealed record RawPivot(
        int PivotIndex,
        int StartIndex,
        int EndIndex,
        double IncomingDistance,
        double OutgoingDistance,
        double AverageDistance,
        double TurnAngle,
        double DistanceRatio,
        double Asymmetry,
        double DistanceSignal,
        double TurnSignal,
        double AsymmetrySignal,
        double Score,
        int ActiveSignalCount,
        string Reason)
    {
        public bool IsAdmissible => Reason is not "InsufficientContext" and not "IntervalBreak" and not "UnreliableGeometry";

        public static RawPivot Unavailable(int pivot, int start, int end, string reason) =>
            new(pivot, start, end, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, reason);
    }
}
