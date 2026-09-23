using System.Globalization;
using System.IO;
using System.Text;
using BeatInsight.Analysis.StreamPatterns;
using BeatInsight.Models;
using BeatInsight.Models.StreamPatterns;
using BeatInsight.Parser;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// Second, observation-only forensic focused on uncertain Stream dimensions.
/// Alternate thresholds are passed as options to the production analyzers and never persisted.
/// </summary>
public sealed class StreamPatternTargetedSecondForensicTests(ITestOutputHelper output)
{
    private static readonly IrregularStreamPatternOptions IrregularBaseline = new();
    private static readonly StaminaStreamPatternOptions StaminaBaseline = new();
    // Preserve the pre-change 20 px baseline used by this historical forensic.
    private static readonly FlowStreamPatternOptions FlowBaseline = new(MinimumFlowLinkDistance: 20);
    private static readonly AlternatingStreamPatternOptions AlternatingBaseline = new();

    [Fact]
    public void RequestedCorpus_WritesTargetedObservationOnlyReport()
    {
        Assert.All(StreamPatternGlobalForensicTests.Corpus,
            spec => Assert.True(File.Exists(spec.Path), $"Missing corpus map: {spec.Path}"));
        MapData[] maps = StreamPatternGlobalForensicTests.Corpus.Select(Load).ToArray();
        var report = new StringBuilder();
        Header(report, maps);
        Irregular(report, maps);
        Stamina(report, maps);
        Flow(report, maps);
        Alternating(report, maps);
        FlowVersusAlternating(report, maps);
        Recommendations(report);

        string outputDirectory = Environment.GetEnvironmentVariable("BEATINSIGHT_FORENSIC_OUTPUT_DIRECTORY")
            ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(outputDirectory);
        string reportPath = Path.Combine(outputDirectory,
            "stream-pattern-targeted-second-forensic.md");
        File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);
        WriteManualReviewEvidence(maps, outputDirectory);
        output.WriteLine($"Corpus analyzed: {maps.Length}/{StreamPatternGlobalForensicTests.Corpus.Length}");
        output.WriteLine($"Report: {reportPath}");
    }

    private static MapData Load(StreamPatternGlobalForensicTests.MapSpec spec)
    {
        Beatmap map = BeatmapParser.Load(spec.Path);
        Assert.Equal(spec.Title, map.Title);
        Assert.Equal(spec.Version, map.Version);
        return new(spec, map,
            StreamPatternAnalyzer.AnalyzeIrregularStream(map.HitObjects, IrregularBaseline),
            StreamPatternAnalyzer.AnalyzeStaminaStream(map.HitObjects, StaminaBaseline),
            StreamPatternAnalyzer.AnalyzeFlowStream(map.HitObjects, FlowBaseline),
            StreamPatternAnalyzer.AnalyzeAlternatingStream(map.HitObjects, AlternatingBaseline));
    }

    private static void Header(StringBuilder report, IReadOnlyList<MapData> maps)
    {
        report.AppendLine("# Stream Pattern Analysis — second targeted forensic");
        report.AppendLine();
        report.AppendLine("Observation-only analysis of Irregular Stream, Stamina Stream, Flow Stream and Alternating Stream. Production analyzers, formulas and default thresholds are unchanged. Every simulation invokes the production analyzer with temporary option values; no alternate Stream segmentation is introduced.");
        report.AppendLine();
        report.AppendLine($"Corpus: **{maps.Count}/{StreamPatternGlobalForensicTests.Corpus.Length}** requested maps, **{maps.Sum(x => x.Map.HitObjects.Count)}** hitobjects. Road of Resistance is `Short-Lived Rebellion` (BeatmapId 1574314). Percentiles use linear interpolation at `(n-1)×p`.");
        report.AppendLine();
    }

    private static void Irregular(StringBuilder report, IReadOnlyList<MapData> maps)
    {
        var all = maps.SelectMany(map => map.Irregular.Runs
            .Where(run => run.ObjectCount >= IrregularBaseline.MinimumIrregularStreamObjects)
            .Select(run => new IrregularItem(map, run))).ToArray();
        report.AppendLine("## 1. Irregular Stream — exhaustive admissible-run export");
        report.AppendLine();
        report.AppendLine($"Admissible maximal runs: **{all.Length}**. Baseline qualified: **{all.Count(x => x.Run.Qualified)}**.");
        report.AppendLine();
        report.AppendLine("|Map|Start–end|Objects|Intervals ms|Interval deltas ms|Avg|Stddev|CV|Sig count|Sig ratio|Avg delta|Max delta|Score|Qualified / reason|");
        report.AppendLine("|---|---|---:|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|");
        foreach (IrregularItem item in all.OrderBy(x => x.Map.Label).ThenBy(x => x.Run.StartTime))
        {
            IrregularStreamPatternSequence r = item.Run;
            report.AppendLine(FormattableString.Invariant($"|{Cell(item.Map.Label)}|{Time(r.StartTime)}–{Time(r.EndTime)}|{r.ObjectCount}|{Numbers(r.Intervals)}|{Numbers(r.IntervalDeltas)}|{r.AverageInterval:F2}|{r.StandardDeviation:F2}|{r.CoefficientOfVariation:F4}|{r.SignificantDeltaCount}|{r.SignificantDeltaRatio:F4}|{r.AverageIntervalDelta:F2}|{r.MaximumIntervalDelta:F2}|{r.IrregularityScore:F4}|{r.Qualified} / {r.Reason}|"));
        }
        report.AppendLine();
        Distribution(report, "CV", all.Select(x => x.Run.CoefficientOfVariation));
        Distribution(report, "Significant delta ratio", all.Select(x => x.Run.SignificantDeltaRatio));
        Distribution(report, "Average interval delta (ms)", all.Select(x => x.Run.AverageIntervalDelta));
        report.AppendLine();
        IrregularTop(report, "score", all.OrderByDescending(x => x.Run.IrregularityScore));
        IrregularTop(report, "CV", all.OrderByDescending(x => x.Run.CoefficientOfVariation));
        IrregularTop(report, "significant-delta ratio", all.OrderByDescending(x => x.Run.SignificantDeltaRatio));

        report.AppendLine("### Read-only threshold grid");
        report.AppendLine();
        report.AppendLine("The baseline is CV 0.10 / significant ratio 0.30. New positives are exact maximal-run keys absent from that baseline.");
        report.AppendLine();
        report.AppendLine("|CV min|Sig ratio min|Qualified runs|Unique objects|Corpus coverage %|Maps|New positives|");
        report.AppendLine("|---:|---:|---:|---:|---:|---:|---:|");
        var baseline = IrregularKeys(maps, IrregularBaseline);
        foreach (double cv in new[] { 0.10, 0.08, 0.12 })
        foreach (double ratio in new[] { 0.30, 0.25, 0.20, 0.15 })
        {
            var options = IrregularBaseline with
            {
                MinimumCoefficientOfVariation = cv,
                MinimumSignificantDeltaRatio = ratio
            };
            var analyses = maps.Select(map => (map, analysis: StreamPatternAnalyzer.AnalyzeIrregularStream(map.Map.HitObjects, options))).ToArray();
            var keys = analyses.SelectMany(x => x.analysis.Sequences.Select(r => Key(x.map, r.StartIndex, r.EndIndex))).ToHashSet();
            int objects = analyses.Sum(x => x.analysis.IrregularStreamObjectCount);
            report.AppendLine(FormattableString.Invariant($"|{cv:F2}|{ratio:F2}|{keys.Count}|{objects}|{Coverage(objects, maps):F3}|{analyses.Count(x => x.analysis.Sequences.Count > 0)}|{keys.Except(baseline).Count()}|"));
        }
        report.AppendLine();
        report.AppendLine("#### New corpus positives at CV 0.10 / significant ratio 0.20");
        report.AppendLine();
        AppendIrregularNewPositives(report, maps, baseline, IrregularBaseline with { MinimumSignificantDeltaRatio = 0.20 });

        int[] spikeIntervals = [80, 80, 80, 125, 80, 80, 80];
        var synthetic = new List<HitObject> { new() { Type = 1, Time = 0 } };
        foreach (int interval in spikeIntervals)
            synthetic.Add(new HitObject { Type = 1, Time = synthetic[^1].Time + interval });
        IrregularStreamPatternSequence spike = StreamPatternAnalyzer.AnalyzeIrregularStream(synthetic, IrregularBaseline).Runs.Single();
        int belowCv = all.Count(x => x.Run.CoefficientOfVariation <= spike.CoefficientOfVariation);
        int belowRatio = all.Count(x => x.Run.SignificantDeltaRatio <= spike.SignificantDeltaRatio);
        report.AppendLine("### Synthetic isolated-spike control");
        report.AppendLine();
        report.AppendLine(FormattableString.Invariant($"`80 80 80 125 80 80 80` → deltas `{Numbers(spike.IntervalDeltas)}`, avg={spike.AverageInterval:F2}, stddev={spike.StandardDeviation:F2}, CV={spike.CoefficientOfVariation:F4}, significant={spike.SignificantDeltaCount}/{spike.IntervalDeltas.Count} ({spike.SignificantDeltaRatio:F4}), avg delta={spike.AverageIntervalDelta:F2}, max delta={spike.MaximumIntervalDelta:F2}, score={spike.IrregularityScore:F4}, baseline qualified=**{spike.Qualified}**. Its CV is at corpus percentile rank **{100.0 * belowCv / all.Length:F1}%** and its significant-ratio rank is **{100.0 * belowRatio / all.Length:F1}%** (empirical ≤ rank)."));
        report.AppendLine();
    }

    private static void IrregularTop(StringBuilder report, string order, IEnumerable<IrregularItem> ordered)
    {
        report.AppendLine($"### Top 20 by {order}");
        report.AppendLine();
        report.AppendLine("|Map|Start–end|Objects|CV|Sig ratio|Avg delta|Max delta|Score|Reason|");
        report.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---|");
        foreach (IrregularItem item in ordered.Take(20))
            report.AppendLine(FormattableString.Invariant($"|{Cell(item.Map.Label)}|{Time(item.Run.StartTime)}–{Time(item.Run.EndTime)}|{item.Run.ObjectCount}|{item.Run.CoefficientOfVariation:F4}|{item.Run.SignificantDeltaRatio:F4}|{item.Run.AverageIntervalDelta:F2}|{item.Run.MaximumIntervalDelta:F2}|{item.Run.IrregularityScore:F4}|{item.Run.Reason}|"));
        report.AppendLine();
    }

    private static void AppendIrregularNewPositives(StringBuilder report, IReadOnlyList<MapData> maps,
        HashSet<string> baseline, IrregularStreamPatternOptions options)
    {
        var rows = maps.SelectMany(map => StreamPatternAnalyzer.AnalyzeIrregularStream(map.Map.HitObjects, options).Sequences
            .Where(run => !baseline.Contains(Key(map, run.StartIndex, run.EndIndex)))
            .Select(run => new IrregularItem(map, run)))
            .OrderByDescending(x => x.Run.IrregularityScore).ToArray();
        if (rows.Length == 0) report.AppendLine("None.");
        foreach (IrregularItem item in rows)
            report.AppendLine(FormattableString.Invariant($"- {item.Map.Label} {Time(item.Run.StartTime)}–{Time(item.Run.EndTime)}: objects={item.Run.ObjectCount}, intervals=[{Numbers(item.Run.Intervals)}], CV={item.Run.CoefficientOfVariation:F4}, sig={item.Run.SignificantDeltaRatio:F4}, score={item.Run.IrregularityScore:F4}."));
        report.AppendLine();
    }

    private static void Stamina(StringBuilder report, IReadOnlyList<MapData> maps)
    {
        report.AppendLine("## 2. Stamina Stream — sequence and recovery-gap forensic");
        report.AppendLine();
        report.AppendLine("### All baseline-qualified sequences");
        report.AppendLine();
        report.AppendLine("|Map|Sequence|Runs|Rapid objects|All gaps ms|Min gap|Avg gap|Max gap|Active ms|Recovery ms|Active ratio|Duration ms|Map coverage contribution %|");
        report.AppendLine("|---|---|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (MapData map in maps)
        foreach (StaminaStreamPatternSequence s in map.Stamina.Sequences)
        {
            double min = s.RecoveryGaps.Count == 0 ? 0 : s.RecoveryGaps.Min();
            report.AppendLine(FormattableString.Invariant($"|{Cell(map.Label)}|{Time(s.StartTime)}–{Time(s.EndTime)}|{s.RunCount}|{s.TotalRapidObjects}|{Numbers(s.RecoveryGaps)}|{min:F0}|{s.AverageRecoveryGap:F2}|{s.MaximumRecoveryGapObserved:F0}|{s.ActiveRapidDuration:F0}|{s.TotalRecoveryDuration:F0}|{s.ActiveRatio:F4}|{s.Duration:F0}|{100.0 * s.TotalRapidObjects / map.Map.HitObjects.Count:F3}|"));
        }
        report.AppendLine();
        var allGaps = maps.SelectMany(EligibleStaminaGaps).Select(x => x.Gap).ToArray();
        Distribution(report, "All adjacent gaps between eligible runs (ms)", allGaps);
        report.AppendLine();
        report.AppendLine("### Read-only maximum-recovery simulations");
        report.AppendLine();
        report.AppendLine("Lost = baseline qualified sequence with no qualifying variant sequence sharing one of its eligible runs. Split = baseline sequence whose runs occur across at least two qualifying variant sequences.");
        report.AppendLine();
        report.AppendLine("|Max gap ms|Sequences|Unique rapid objects|Corpus coverage %|Maps|Lost baseline seq.|Split baseline seq.|Affected maps|");
        report.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---|");
        foreach (double gap in new[] { 1500.0, 1250.0, 1000.0, 750.0 })
        {
            var variants = maps.Select(map => (map, analysis: StreamPatternAnalyzer.AnalyzeStaminaStream(
                map.Map.HitObjects, StaminaBaseline with { MaximumRecoveryGap = gap }))).ToArray();
            var comparison = CompareStamina(maps, variants);
            int objects = variants.Sum(x => x.analysis.StaminaObjectCount);
            report.AppendLine(FormattableString.Invariant($"|{gap:F0}|{variants.Sum(x => x.analysis.StaminaSequenceCount)}|{objects}|{Coverage(objects, maps):F3}|{variants.Count(x => x.analysis.StaminaSequenceCount > 0)}|{comparison.Lost}|{comparison.Split}|{Cell(string.Join(", ", comparison.AffectedMaps))}|"));
        }
        report.AppendLine();
        report.AppendLine("#### Exact lost/split baseline sequences");
        report.AppendLine();
        foreach (double gap in new[] { 1250.0, 1000.0, 750.0 })
        {
            var variants = maps.Select(map => (map, analysis: StreamPatternAnalyzer.AnalyzeStaminaStream(
                map.Map.HitObjects, StaminaBaseline with { MaximumRecoveryGap = gap }))).ToArray();
            StaminaComparison comparison = CompareStamina(maps, variants);
            report.AppendLine($"- **{gap:F0} ms**: {(comparison.Events.Count == 0 ? "None" : string.Join("; ", comparison.Events))}");
        }
        report.AppendLine();
        report.AppendLine("#### Per-map object/sequence deltas versus 1500 ms");
        report.AppendLine();
        report.AppendLine("|Max gap|Map|Sequence delta|Object delta|Coverage-point delta|");
        report.AppendLine("|---:|---|---:|---:|---:|");
        foreach (double gap in new[] { 1250.0, 1000.0, 750.0 })
        foreach (MapData map in maps)
        {
            StaminaStreamPatternAnalysis variant = StreamPatternAnalyzer.AnalyzeStaminaStream(
                map.Map.HitObjects, StaminaBaseline with { MaximumRecoveryGap = gap });
            int sequenceDelta = variant.StaminaSequenceCount - map.Stamina.StaminaSequenceCount;
            int objectDelta = variant.StaminaObjectCount - map.Stamina.StaminaObjectCount;
            if (sequenceDelta == 0 && objectDelta == 0) continue;
            report.AppendLine(FormattableString.Invariant($"|{gap:F0}|{Cell(map.Label)}|{sequenceDelta}|{objectDelta}|{(variant.Coverage - map.Stamina.Coverage) * 100:F3}|"));
        }
        report.AppendLine();
        report.AppendLine("### Road of Resistance under every recovery cap");
        report.AppendLine();
        foreach (double gap in new[] { 1500.0, 1250.0, 1000.0, 750.0 })
        {
            MapData road = maps.Single(x => x.Spec.Title.StartsWith("Road of Resistance", StringComparison.Ordinal));
            StaminaStreamPatternAnalysis analysis = StreamPatternAnalyzer.AnalyzeStaminaStream(
                road.Map.HitObjects, StaminaBaseline with { MaximumRecoveryGap = gap });
            report.AppendLine($"- **{gap:F0} ms**: {analysis.StaminaSequenceCount} qualified sequence(s), {analysis.StaminaObjectCount} rapid objects, {analysis.Coverage * 100:F3}% map coverage.");
            foreach (StaminaStreamPatternSequence s in analysis.Sequences)
                report.AppendLine(FormattableString.Invariant($"  - {Time(s.StartTime)}–{Time(s.EndTime)}: runs={s.RunCount}, objects={s.TotalRapidObjects}, gaps=[{Numbers(s.RecoveryGaps)}], active={s.ActiveRatio:F4}."));
        }
        report.AppendLine();
    }

    private static StaminaComparison CompareStamina(IReadOnlyList<MapData> maps,
        IReadOnlyList<(MapData map, StaminaStreamPatternAnalysis analysis)> variants)
    {
        int lost = 0, split = 0;
        var affected = new HashSet<string>();
        var events = new List<string>();
        foreach (MapData map in maps)
        {
            StaminaStreamPatternAnalysis variant = variants.Single(x => ReferenceEquals(x.map, map)).analysis;
            if (variant.StaminaSequenceCount != map.Stamina.StaminaSequenceCount
                || variant.StaminaObjectCount != map.Stamina.StaminaObjectCount) affected.Add(map.Label);
            foreach (StaminaStreamPatternSequence baseline in map.Stamina.Sequences)
            {
                var baseRuns = baseline.Runs.Select(r => (r.StartIndex, r.EndIndex)).ToHashSet();
                int overlaps = variant.Sequences.Count(v => v.Runs.Any(r => baseRuns.Contains((r.StartIndex, r.EndIndex))));
                string label = $"{map.Label} {Time(baseline.StartTime)}–{Time(baseline.EndTime)}";
                if (overlaps == 0)
                {
                    lost++;
                    events.Add($"LOST {label}");
                }
                else if (overlaps >= 2)
                {
                    split++;
                    events.Add($"SPLIT×{overlaps} {label}");
                }
            }
        }
        return new(lost, split, affected.OrderBy(x => x).ToArray(), events);
    }

    private static void Flow(StringBuilder report, IReadOnlyList<MapData> maps)
    {
        report.AppendLine("## 3. Flow Stream — threshold-neighborhood forensic");
        report.AppendLine();
        var near = maps.SelectMany(map => map.Flow.Runs.Where(run =>
                run.FlowPivotRatio is >= 0.55 and <= 0.80
                || run.TurnDirectionConsistency is >= 0.65 and <= 0.85
                || run.MinimumDistance is >= 15 and <= 25)
            .Select(run => new FlowItem(map, run)))
            .OrderBy(x => x.Map.Label).ThenBy(x => x.Run.StartTime).ToArray();
        report.AppendLine($"Runs touching at least one requested neighborhood: **{near.Length}**.");
        report.AppendLine();
        report.AppendLine("|Map|Run|Obj|Positions|Distances|Angles|Signs|Flow pivots|Pivot ratio|Direction consistency|Curvature consistency|Score|Reason|");
        report.AppendLine("|---|---|---:|---|---|---|---|---:|---:|---:|---:|---:|---|");
        foreach (FlowItem item in near)
        {
            FlowStreamPatternSequence r = item.Run;
            report.AppendLine(FormattableString.Invariant($"|{Cell(item.Map.Label)}|{Time(r.StartTime)}–{Time(r.EndTime)}|{r.ObjectCount}|{Positions(item.Map, r.StartIndex, r.EndIndex)}|{Numbers(r.Distances)}|{Numbers(r.TurnAngles)}|{string.Join(",", r.TurnSigns)}|{r.FlowPivotCount}/{r.PivotCount}|{r.FlowPivotRatio:F4}|{r.TurnDirectionConsistency:F4}|{r.CurvatureConsistency:F4}|{r.FlowScore:F4}|{r.Reason}|"));
        }
        report.AppendLine();
        report.AppendLine("### Independent read-only simulations");
        report.AppendLine();
        report.AppendLine("|Variant|Qualified runs|Objects|Corpus coverage %|Maps|New positives|Lost positives|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
        var variants = new (string Label, FlowStreamPatternOptions Options)[]
        {
            ("baseline", FlowBaseline),
            ("pivot 0.65", FlowBaseline with { MinimumFlowPivotRatio = 0.65 }),
            ("pivot 0.60", FlowBaseline with { MinimumFlowPivotRatio = 0.60 }),
            ("direction 0.80", FlowBaseline with { MinimumTurnDirectionConsistency = 0.80 }),
            ("distance 18", FlowBaseline with { MinimumFlowLinkDistance = 18 }),
            ("distance 15", FlowBaseline with { MinimumFlowLinkDistance = 15 })
        };
        HashSet<string> baseline = FlowKeys(maps, FlowBaseline);
        foreach ((string label, FlowStreamPatternOptions options) in variants)
        {
            var analyses = maps.Select(map => (map, analysis: StreamPatternAnalyzer.AnalyzeFlowStream(map.Map.HitObjects, options))).ToArray();
            var keys = analyses.SelectMany(x => x.analysis.Sequences.Select(r => Key(x.map, r.StartIndex, r.EndIndex))).ToHashSet();
            int objects = analyses.Sum(x => x.analysis.FlowStreamObjectCount);
            report.AppendLine(FormattableString.Invariant($"|{label}|{keys.Count}|{objects}|{Coverage(objects, maps):F3}|{analyses.Count(x => x.analysis.Sequences.Count > 0)}|{keys.Except(baseline).Count()}|{baseline.Except(keys).Count()}|"));
        }
        report.AppendLine();
        foreach ((string label, FlowStreamPatternOptions options) in variants.Skip(1))
            AppendFlowDelta(report, maps, baseline, label, options);
    }

    private static void AppendFlowDelta(StringBuilder report, IReadOnlyList<MapData> maps,
        HashSet<string> baseline, string label, FlowStreamPatternOptions options)
    {
        var analyses = maps.Select(map => (map, analysis: StreamPatternAnalyzer.AnalyzeFlowStream(map.Map.HitObjects, options))).ToArray();
        var rows = analyses.SelectMany(x => x.analysis.Sequences.Select(run => new FlowItem(x.map, run))).ToArray();
        var keys = rows.Select(x => Key(x.Map, x.Run.StartIndex, x.Run.EndIndex)).ToHashSet();
        report.AppendLine($"#### {label}: exact delta");
        report.AppendLine();
        string[] added = keys.Except(baseline).OrderBy(x => x).ToArray();
        string[] lost = baseline.Except(keys).OrderBy(x => x).ToArray();
        report.AppendLine($"- New: {(added.Length == 0 ? "None" : string.Join("; ", added))}");
        report.AppendLine($"- Lost: {(lost.Length == 0 ? "None" : string.Join("; ", lost))}");
        report.AppendLine();
    }

    private static void Alternating(StringBuilder report, IReadOnlyList<MapData> maps)
    {
        report.AppendLine("## 4. Alternating Stream — rejected-run forensic");
        report.AppendLine();
        var rejected = maps.SelectMany(map => map.Alternating.Runs.Where(run => !run.Qualified)
            .Select(run => new AlternatingItem(map, run))).ToArray();
        AlternatingTop(report, "score", rejected.OrderByDescending(x => x.Run.AlternatingScore));
        AlternatingTop(report, "alternation ratio", rejected.OrderByDescending(x => x.Run.AlternationRatio));
        AlternatingTop(report, "valid-pivot ratio", rejected.OrderByDescending(x => x.Run.ValidAlternatingPivotRatio));
        report.AppendLine("### Rejection reasons (all maximal runs)");
        report.AppendLine();
        foreach (var group in rejected.GroupBy(x => x.Run.Reason).OrderBy(x => x.Key))
            report.AppendLine($"- {group.Key}: **{group.Count()}**");
        report.AppendLine();
        report.AppendLine("### Independent read-only simulations");
        report.AppendLine();
        report.AppendLine("|Variant|Qualified runs|Objects|Corpus coverage %|Maps|New positives|Lost positives|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
        var variants = new (string Label, AlternatingStreamPatternOptions Options)[]
        {
            ("baseline", AlternatingBaseline),
            ("alternation 0.70", AlternatingBaseline with { MinimumAlternationRatio = 0.70 }),
            ("alternation 0.65", AlternatingBaseline with { MinimumAlternationRatio = 0.65 }),
            ("valid pivot 0.65", AlternatingBaseline with { MinimumValidAlternatingPivotRatio = 0.65 })
        };
        HashSet<string> baseline = AlternatingKeys(maps, AlternatingBaseline);
        foreach ((string label, AlternatingStreamPatternOptions options) in variants)
        {
            var analyses = maps.Select(map => (map, analysis: StreamPatternAnalyzer.AnalyzeAlternatingStream(map.Map.HitObjects, options))).ToArray();
            var keys = analyses.SelectMany(x => x.analysis.Sequences.Select(r => Key(x.map, r.StartIndex, r.EndIndex))).ToHashSet();
            int objects = analyses.Sum(x => x.analysis.AlternatingStreamObjectCount);
            report.AppendLine(FormattableString.Invariant($"|{label}|{keys.Count}|{objects}|{Coverage(objects, maps):F3}|{analyses.Count(x => x.analysis.Sequences.Count > 0)}|{keys.Except(baseline).Count()}|{baseline.Except(keys).Count()}|"));
        }
        report.AppendLine();
        report.AppendLine("A lower threshold is not treated as a recommendation here: each newly admitted run still requires visual confirmation of a clear zigzag rather than a metric-only pass.");
        report.AppendLine();
    }

    private static void AlternatingTop(StringBuilder report, string order,
        IEnumerable<AlternatingItem> ordered)
    {
        report.AppendLine($"### Top 20 non-qualified by {order}");
        report.AppendLine();
        report.AppendLine("|Map|Run|Obj|Valid pivots|Valid ratio|Alternations|Possible|Alt ratio|Angles|Signs|Distances|Angle consistency|Score|Reason|");
        report.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---|---|---|---:|---:|---|");
        foreach (AlternatingItem item in ordered.Take(20))
        {
            AlternatingStreamPatternSequence r = item.Run;
            report.AppendLine(FormattableString.Invariant($"|{Cell(item.Map.Label)}|{Time(r.StartTime)}–{Time(r.EndTime)}|{r.ObjectCount}|{r.ValidAlternatingPivotCount}/{r.PivotCount}|{r.ValidAlternatingPivotRatio:F4}|{r.AlternationCount}|{r.PossibleAlternations}|{r.AlternationRatio:F4}|{Numbers(r.TurnAngles)}|{string.Join(",", r.TurnSigns)}|{Numbers(r.Distances)}|{r.AlternatingAngleConsistency:F4}|{r.AlternatingScore:F4}|{r.Reason}|"));
        }
        report.AppendLine();
    }

    private static void FlowVersusAlternating(StringBuilder report, IReadOnlyList<MapData> maps)
    {
        report.AppendLine("## 5. Flow versus Alternating — all score-neighborhood runs");
        report.AppendLine();
        var pairs = maps.SelectMany(map => map.Flow.Runs.Zip(map.Alternating.Runs)
            .Where(pair => pair.First.FlowScore >= 0.60 || pair.Second.AlternatingScore >= 0.60)
            .Select(pair => (map, Flow: pair.First, Alt: pair.Second)))
            .OrderByDescending(x => Math.Max(x.Flow.FlowScore, x.Alt.AlternatingScore)).ToArray();
        report.AppendLine($"Exact runs with FlowScore >= 0.60 or AlternatingScore >= 0.60: **{pairs.Length}** (including short maximal runs; their rejection reason remains explicit). Same-sign-dominant geometry should favor Flow; sign alternation should favor Alternating.");
        report.AppendLine();
        report.AppendLine("|Map|Run|Obj|Turn signs|Flow pivot|Flow direction|Flow score/reason|Alt valid|Alt ratio|Alt score/reason|");
        report.AppendLine("|---|---|---:|---|---:|---:|---|---:|---:|---|");
        foreach (var p in pairs)
            report.AppendLine(FormattableString.Invariant($"|{Cell(p.map.Label)}|{Time(p.Flow.StartTime)}–{Time(p.Flow.EndTime)}|{p.Flow.ObjectCount}|{string.Join(",", p.Flow.TurnSigns)}|{p.Flow.FlowPivotRatio:F4}|{p.Flow.TurnDirectionConsistency:F4}|{p.Flow.FlowScore:F4} / {p.Flow.Reason}|{p.Alt.ValidAlternatingPivotRatio:F4}|{p.Alt.AlternationRatio:F4}|{p.Alt.AlternatingScore:F4} / {p.Alt.Reason}|"));
        report.AppendLine();
    }

    private static void Recommendations(StringBuilder report)
    {
        report.AppendLine("## 6. Targeted recommendations");
        report.AppendLine();
        report.AppendLine("This section is intentionally conservative and is finalized against the generated evidence before delivery.");
        report.AppendLine();
        report.AppendLine("|Pattern / threshold studied|Status|Recommendation|");
        report.AppendLine("|---|---|---|");
        report.AppendLine("|Irregular CV 0.10 + significant ratio 0.30 (current)|KEEP|No corpus positive, while the isolated-spike negative already passes; changing only the ratio cannot repair that structural weakness.|");
        report.AppendLine("|Irregular significant ratio 0.25|NEEDS MORE POSITIVE CONTROLS|It remains at zero positives on this corpus, so it provides no new discrimination evidence.|");
        report.AppendLine("|Irregular significant ratio 0.20|NEEDS MANUAL MAP REVIEW|It adds exactly Road 00:25.546–00:26.492; review that run and verified isolated-spike negatives before considering it.|");
        report.AppendLine("|Irregular significant ratio 0.15|NEEDS MORE POSITIVE CONTROLS|It adds nothing beyond 0.20 and has no demonstrated recall advantage.|");
        report.AppendLine("|Irregular CV 0.08 / 0.12|NEEDS MORE POSITIVE CONTROLS|Neither CV change affects the grid outcome; significant-ratio evidence remains the bottleneck.|");
        report.AppendLine("|Stamina recovery 1500 ms (current)|KEEP|Retain pending manual boundary review; it preserves all 11 baseline sequences and 766 objects.|");
        report.AppendLine("|Stamina recovery 1250 ms|NEEDS MANUAL MAP REVIEW|Plausible candidate range starts here: Road retains all 195 objects but splits at 1319 ms; other maps lose or split baseline sequences.|");
        report.AppendLine("|Stamina recovery 1000 ms|NEEDS MANUAL MAP REVIEW|Road remains object-robust but becomes three sequences; verify whether the split semantics match perceived recovery.|");
        report.AppendLine("|Stamina recovery 750 ms|NEEDS MANUAL MAP REVIEW|This loses 153 corpus Stamina objects versus baseline, including 72 Road objects; evidence is too destructive for an automatic recommendation.|");
        report.AppendLine("|Flow pivot 0.70 (current)|KEEP|Baseline remains the precision anchor until the nine 0.65 admissions are visually reviewed.|");
        report.AppendLine("|Flow pivot 0.65|NEEDS MANUAL MAP REVIEW|Adds nine exact runs and is the narrower recall candidate; inspect their positions/signs first.|");
        report.AppendLine("|Flow pivot 0.60|NEEDS MANUAL MAP REVIEW|Adds eleven runs, only two beyond 0.65; those marginal admissions need direct visual proof.|");
        report.AppendLine("|Flow direction 0.75 (current)|KEEP|Raising to 0.80 removes six positives, including all named 0.75 boundary cases, without positive-control evidence that they are false.|");
        report.AppendLine("|Flow direction 0.80|NEEDS MANUAL MAP REVIEW|Review the six exact lost positives; do not tighten globally from metric suspicion alone.|");
        report.AppendLine("|Flow distance 20 px (forensic baseline before validated change)|KEEP|Historical precision anchor used by this report.|");
        report.AppendLine("|Flow distance 18 px|NEEDS MANUAL MAP REVIEW|Adds only Reign of Fear 04:16.486–04:17.062, making it the focused distance candidate.|");
        report.AppendLine("|Flow distance 15 px|NEEDS MANUAL MAP REVIEW|Adds one additional neighboring Reign of Fear run; no broad-corpus support over 18 px.|");
        report.AppendLine("|Alternating alternation 0.75 (current)|KEEP|The sole strong Extreme Fantasy positive remains isolated but structurally unambiguous.|");
        report.AppendLine("|Alternating alternation 0.70 / 0.65|KEEP|Both simulations are identical to baseline: zero new positives, so lowering gives no observed recall benefit.|");
        report.AppendLine("|Alternating valid-pivot 0.70 (current)|KEEP|Retain the current spatial-validity requirement.|");
        report.AppendLine("|Alternating valid-pivot 0.65|KEEP|The simulation adds zero positives; lowering is unsupported, and the corpus lacks additional confirmed zigzag controls.|");
        report.AppendLine();
        report.AppendLine("No production change is made by this forensic.");
    }

    private static void WriteManualReviewEvidence(IReadOnlyList<MapData> maps, string outputDirectory)
    {
        var report = new StringBuilder();
        report.AppendLine("# Manual Stream review — raw Stamina evidence");
        report.AppendLine();
        report.AppendLine("Only adjacent eligible-run gaps in [1000,1500] ms are listed. `Before active ratio` is active rapid duration from the containing candidate start through the previous run end, divided by that elapsed duration. `After active ratio` is the equivalent from the next run start through the candidate end. Gap objects use C=circle, S=slider, P=spinner, O=other.");
        report.AppendLine();
        report.AppendLine("|Band|Map|Candidate|Candidate qualified/reason|Previous run|Previous objects / avg interval|Next run|Next objects / avg interval|Gap|Objects strictly inside gap|Candidate duration|Before active ratio|After active ratio|");
        report.AppendLine("|---|---|---|---|---|---|---|---|---:|---|---:|---:|---:|");
        foreach (MapData map in maps)
        {
            StaminaRapidRun[] runs = map.Stamina.Candidates.SelectMany(x => x.Runs)
                .DistinctBy(x => (x.StartIndex, x.EndIndex)).OrderBy(x => x.StartTime).ToArray();
            foreach ((StaminaRapidRun previous, StaminaRapidRun next) in runs.Zip(runs.Skip(1)))
            {
                double gap = next.StartTime - previous.EndTime;
                if (gap is < 1000 or > 1500) continue;
                StaminaStreamPatternSequence candidate = map.Stamina.Candidates.Single(x =>
                    x.Runs.Any(r => r.StartIndex == previous.StartIndex && r.EndIndex == previous.EndIndex)
                    && x.Runs.Any(r => r.StartIndex == next.StartIndex && r.EndIndex == next.EndIndex));
                StaminaRapidRun[] beforeRuns = candidate.Runs.TakeWhile(r => r.EndIndex <= previous.EndIndex).ToArray();
                StaminaRapidRun[] afterRuns = candidate.Runs.SkipWhile(r => r.StartIndex < next.StartIndex).ToArray();
                double beforeElapsed = previous.EndTime - candidate.StartTime;
                double afterElapsed = candidate.EndTime - next.StartTime;
                double beforeRatio = beforeElapsed <= 0 ? 1 : beforeRuns.Sum(r => r.Duration) / beforeElapsed;
                double afterRatio = afterElapsed <= 0 ? 1 : afterRuns.Sum(r => r.Duration) / afterElapsed;
                HitObject[] gapObjects = map.Map.HitObjects.Where(x => x.Time > previous.EndTime && x.Time < next.StartTime).ToArray();
                string objects = gapObjects.Length == 0 ? "None" : string.Join(";", gapObjects.Select(x =>
                    $"{ObjectKind(x)}@{Time(x.Time)}({x.X},{x.Y})"));
                string band = gap < 1250 ? "1000–1249" : gap < 1350 ? "1250–1349" : "1350–1500";
                report.AppendLine(FormattableString.Invariant($"|{band}|{Cell(map.Label)}|{Time(candidate.StartTime)}–{Time(candidate.EndTime)}|{candidate.Qualified}/{candidate.Reason}|{Time(previous.StartTime)}–{Time(previous.EndTime)}|{previous.ObjectCount} / {previous.AverageInterval:F2}|{Time(next.StartTime)}–{Time(next.EndTime)}|{next.ObjectCount} / {next.AverageInterval:F2}|{gap:F0}|{objects}|{candidate.Duration:F0}|{beforeRatio:F4}|{afterRatio:F4}|"));
            }
        }
        File.WriteAllText(Path.Combine(outputDirectory, "stream-pattern-manual-review-evidence.md"),
            report.ToString(), Encoding.UTF8);
    }

    private static string ObjectKind(HitObject value) => (value.Type & 1) == 1 ? "C"
        : (value.Type & 2) == 2 ? "S"
        : (value.Type & 8) == 8 ? "P"
        : "O";

    private static IEnumerable<(MapData Map, double Gap)> EligibleStaminaGaps(MapData map)
    {
        StaminaRapidRun[] runs = map.Stamina.Candidates.SelectMany(x => x.Runs)
            .DistinctBy(x => (x.StartIndex, x.EndIndex)).OrderBy(x => x.StartTime).ToArray();
        return runs.Zip(runs.Skip(1), (previous, next) => (map, (double)next.StartTime - previous.EndTime));
    }

    private static HashSet<string> IrregularKeys(IReadOnlyList<MapData> maps, IrregularStreamPatternOptions options) =>
        maps.SelectMany(map => StreamPatternAnalyzer.AnalyzeIrregularStream(map.Map.HitObjects, options).Sequences
            .Select(run => Key(map, run.StartIndex, run.EndIndex))).ToHashSet();
    private static HashSet<string> FlowKeys(IReadOnlyList<MapData> maps, FlowStreamPatternOptions options) =>
        maps.SelectMany(map => StreamPatternAnalyzer.AnalyzeFlowStream(map.Map.HitObjects, options).Sequences
            .Select(run => Key(map, run.StartIndex, run.EndIndex))).ToHashSet();
    private static HashSet<string> AlternatingKeys(IReadOnlyList<MapData> maps, AlternatingStreamPatternOptions options) =>
        maps.SelectMany(map => StreamPatternAnalyzer.AnalyzeAlternatingStream(map.Map.HitObjects, options).Sequences
            .Select(run => Key(map, run.StartIndex, run.EndIndex))).ToHashSet();

    private static void Distribution(StringBuilder report, string name, IEnumerable<double> source)
    {
        double[] values = source.OrderBy(x => x).ToArray();
        report.AppendLine(FormattableString.Invariant($"- **{name}** (n={values.Length}): min={Percentile(values, 0):F4}, P25={Percentile(values, .25):F4}, median={Percentile(values, .5):F4}, P75={Percentile(values, .75):F4}, P90={Percentile(values, .9):F4}, P95={Percentile(values, .95):F4}, max={Percentile(values, 1):F4}."));
    }

    private static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        if (sorted.Count == 0) return 0;
        double position = (sorted.Count - 1) * percentile;
        int lower = (int)Math.Floor(position), upper = (int)Math.Ceiling(position);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }

    private static string Positions(MapData map, int start, int end) => string.Join(";",
        map.Map.HitObjects.Skip(start).Take(end - start + 1).Select(x => $"({x.X},{x.Y})"));
    private static string Key(MapData map, int start, int end) =>
        $"{map.Label} {Time(map.Map.HitObjects[start].Time)}–{Time(map.Map.HitObjects[end].Time)} [{start}-{end}]";
    private static double Coverage(int objects, IReadOnlyList<MapData> maps) =>
        100.0 * objects / maps.Sum(x => x.Map.HitObjects.Count);
    private static string Numbers(IEnumerable<double> values) => string.Join(",", values.Select(x => x.ToString("F2", CultureInfo.InvariantCulture)));
    private static string Time(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds).ToString(@"mm\:ss\.fff");
    private static string Cell(string value) => value.Replace("|", "\\|");

    private sealed record MapData(StreamPatternGlobalForensicTests.MapSpec Spec, Beatmap Map,
        IrregularStreamPatternAnalysis Irregular, StaminaStreamPatternAnalysis Stamina,
        FlowStreamPatternAnalysis Flow, AlternatingStreamPatternAnalysis Alternating)
    {
        public string Label => $"{Spec.Title} [{Spec.Version}]";
    }
    private sealed record IrregularItem(MapData Map, IrregularStreamPatternSequence Run);
    private sealed record FlowItem(MapData Map, FlowStreamPatternSequence Run);
    private sealed record AlternatingItem(MapData Map, AlternatingStreamPatternSequence Run);
    private sealed record StaminaComparison(int Lost, int Split, IReadOnlyList<string> AffectedMaps,
        IReadOnlyList<string> Events);
}
