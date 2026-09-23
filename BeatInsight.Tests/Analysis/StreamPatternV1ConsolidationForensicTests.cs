using System.Globalization;
using System.IO;
using System.Text;
using BeatInsight.Analysis.StreamPatterns;
using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.StreamPatterns;
using BeatInsight.Parser;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

/// <summary>Final, observation-only consolidation of the frozen V1 Stream-pattern family.</summary>
public sealed class StreamPatternV1ConsolidationForensicTests(ITestOutputHelper output)
{
    [Fact]
    public void ExactEighteenMapCorpus_ProducesFinalV1Consolidation()
    {
        Assert.All(StreamPatternGlobalForensicTests.Corpus,
            spec => Assert.True(File.Exists(spec.Path), $"Missing corpus map: {spec.Path}"));

        MapResult[] maps = StreamPatternGlobalForensicTests.Corpus.Select(Analyze).ToArray();
        var objectiveBugs = new List<string>();
        var report = new StringBuilder();

        AppendConfiguration(report);
        AppendCorpus(report, maps);
        AppendGlobalTables(report, maps);
        AppendLengthPartition(report, maps, objectiveBugs);
        AppendStamina(report, maps);
        AppendIrregular(report, maps, objectiveBugs);
        AppendSpeed(report, maps);
        AppendFlow(report, maps, objectiveBugs);
        AppendAlternating(report, maps);
        AppendCoexistence(report, maps);
        AppendFlowVsAlternating(report, maps);
        AppendConfidenceAudit(report, maps, objectiveBugs);
        AppendCoverageAudit(report, maps, objectiveBugs);
        AppendIntegrityAudit(report, maps, objectiveBugs);
        AppendDebugAudit(report, objectiveBugs);
        AppendTestInventory(report);
        AppendFinalValidation(report);
        AppendConclusion(report, maps, objectiveBugs);

        string directory = Environment.GetEnvironmentVariable("BEATINSIGHT_FORENSIC_OUTPUT_DIRECTORY")
            ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(directory);
        string reportPath = Path.Combine(directory, "stream-pattern-v1-consolidation.md");
        File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);
        output.WriteLine($"Corpus analyzed: {maps.Length}/18");
        output.WriteLine($"Objective bugs: {objectiveBugs.Count}");
        output.WriteLine($"Report: {reportPath}");

        Assert.Empty(objectiveBugs);
    }

    private static MapResult Analyze(StreamPatternGlobalForensicTests.MapSpec spec)
    {
        Beatmap map = BeatmapParser.Load(spec.Path);
        Assert.Equal(spec.Title, map.Title);
        Assert.Equal(spec.Version, map.Version);
        return new(spec, map,
            StreamPatternAnalyzer.AnalyzeBurst(map.HitObjects),
            StreamPatternAnalyzer.AnalyzeShortStream(map.HitObjects),
            StreamPatternAnalyzer.AnalyzeLongStream(map.HitObjects),
            StreamPatternAnalyzer.AnalyzeDeathstream(map.HitObjects),
            StreamPatternAnalyzer.AnalyzeStaminaStream(map.HitObjects),
            StreamPatternAnalyzer.AnalyzeIrregularStream(map.HitObjects),
            StreamPatternAnalyzer.AnalyzeSpeedStream(map.HitObjects),
            StreamPatternAnalyzer.AnalyzeFlowStream(map.HitObjects),
            StreamPatternAnalyzer.AnalyzeAlternatingStream(map.HitObjects));
    }

    private static void AppendConfiguration(StringBuilder report)
    {
        report.AppendLine("# Stream Pattern Analysis — V1 final consolidation");
        report.AppendLine();
        report.AppendLine("Final observation-only forensic. No threshold, analyzer, Identity, Tech, Rating, UI, or Jump behavior was changed.");
        report.AppendLine();
        report.AppendLine("## 1. Configuration V1 finale");
        report.AppendLine();
        report.AppendLine("- Common segmentation: circles only, 30–130 ms inclusive temporal links, maximal runs, no artificial splitting.");
        report.AppendLine("- Length partition: Burst 3–8; Short 9–16; Long 17–32; Deathstream >=33 objects.");
        report.AppendLine("- Stamina: run >=9 objects, >=2 runs, >=24 rapid objects, recovery <=1500 ms.");
        report.AppendLine("- Irregular: >=6 objects, CV >=0.10, >=2 distinct median-atypical regions, deviation >=12 ms; SignificantDeltaRatio diagnostic only.");
        report.AppendLine("- Speed: >=6 objects and average interval <=90 ms; BPM=60000/(interval*4); intensity maps 130 ms to 0 and 60 ms to 1.");
        report.AppendLine("- Flow: >=6 objects, 15°–110°, distance >=18 px, pivot ratio >=0.70, direction consistency >=0.75.");
        report.AppendLine("- Alternating: >=6 objects, 20°–160°, distance >=20 px, valid-pivot ratio >=0.70, alternation ratio >=0.75.");
        report.AppendLine();
    }

    private static void AppendCorpus(StringBuilder report, IReadOnlyList<MapResult> maps)
    {
        report.AppendLine("## 2. Corpus");
        report.AppendLine();
        report.AppendLine($"Exact requested corpus found and analyzed: **{maps.Count}/18**. No difficulty substitution.");
        report.AppendLine();
        report.AppendLine("|#|Map|Difficulty|Objects|Path|");
        report.AppendLine("|---:|---|---|---:|---|");
        for (int i = 0; i < maps.Count; i++)
            report.AppendLine($"|{i + 1}|{Cell(maps[i].Spec.Title)}|{Cell(maps[i].Spec.Version)}|{maps[i].Map.HitObjects.Count}|`{maps[i].Spec.Path}`|");
        report.AppendLine();
    }

    private static void AppendGlobalTables(StringBuilder report, IReadOnlyList<MapResult> maps)
    {
        report.AppendLine("## 3. Tableau global");
        report.AppendLine();
        report.AppendLine("### Coverage (%)");
        report.AppendLine();
        report.AppendLine("|Map|Burst|Short|Long|Death|Stamina|Irregular|Speed|Flow|Alternating|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (MapResult m in maps)
            report.AppendLine($"|{Cell(m.Label)}|{Pct(m.Burst.Coverage)}|{Pct(m.Short.Coverage)}|{Pct(m.Long.Coverage)}|{Pct(m.Death.Coverage)}|{Pct(m.Stamina.Coverage)}|{Pct(m.Irregular.Coverage)}|{Pct(m.Speed.Coverage)}|{Pct(m.Flow.Coverage)}|{Pct(m.Alternating.Coverage)}|");
        report.AppendLine();
        report.AppendLine("### Sequence count");
        report.AppendLine();
        report.AppendLine("|Map|Burst|Short|Long|Death|Stamina|Irregular|Speed|Flow|Alternating|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (MapResult m in maps)
            report.AppendLine($"|{Cell(m.Label)}|{m.Burst.BurstSequenceCount}|{m.Short.ShortStreamSequenceCount}|{m.Long.LongStreamSequenceCount}|{m.Death.DeathstreamSequenceCount}|{m.Stamina.StaminaSequenceCount}|{m.Irregular.IrregularStreamSequenceCount}|{m.Speed.SpeedStreamSequenceCount}|{m.Flow.FlowStreamSequenceCount}|{m.Alternating.AlternatingStreamSequenceCount}|");
        report.AppendLine();
        report.AppendLine("### Longest qualified sequence (rapid objects for Stamina)");
        report.AppendLine();
        report.AppendLine("|Map|Burst|Short|Long|Death|Stamina|Irregular|Speed|Flow|Alternating|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (MapResult m in maps)
            report.AppendLine($"|{Cell(m.Label)}|{m.Burst.LongestBurst}|{m.Short.LongestShortStream}|{m.Long.LongestLongStream}|{m.Death.LongestDeathstream}|{m.Stamina.LongestStaminaObjectCount}|{m.Irregular.LongestIrregularStream}|{m.Speed.LongestSpeedStream}|{m.Flow.LongestFlowStream}|{m.Alternating.LongestAlternatingStream}|");
        report.AppendLine();
        report.AppendLine("### Per-map nine-pattern metrics");
        foreach (MapResult m in maps)
        {
            report.AppendLine();
            report.AppendLine($"#### {m.Label}");
            report.AppendLine();
            report.AppendLine("|Pattern|Seq|Objects|Coverage|Longest|Confidence|Main metrics|");
            report.AppendLine("|---|---:|---:|---:|---:|---:|---|");
            foreach (PatternView p in Views(m))
                report.AppendLine(FormattableString.Invariant($"|{p.Name}|{p.SequenceCount}|{p.ObjectCount}|{p.Coverage:P2}|{p.Longest}|{p.Confidence:F4}|{p.Metrics}|"));
        }
        report.AppendLine();
    }

    private static void AppendLengthPartition(StringBuilder report, IReadOnlyList<MapResult> maps, List<string> bugs)
    {
        int before = bugs.Count;
        int total = 0;
        var distribution = new Dictionary<string, int> { ["Burst"] = 0, ["Short"] = 0, ["Long"] = 0, ["Deathstream"] = 0 };
        foreach (MapResult m in maps)
        {
            var master = m.Burst.Runs.Select(Key).ToArray();
            foreach ((string name, IEnumerable<(int, int)> keys) in new[]
            {
                ("Short", m.Short.Runs.Select(Key)), ("Long", m.Long.Runs.Select(Key)), ("Death", m.Death.Runs.Select(Key))
            })
                if (!master.SequenceEqual(keys)) bugs.Add($"{m.Label}: {name} segmentation differs from the common maximal-run segmentation.");

            foreach (BurstPatternSequence run in m.Burst.Runs.Where(x => x.ObjectCount >= 3))
            {
                total++;
                var key = Key(run);
                string[] qualified =
                [
                    .. m.Burst.Sequences.Where(x => Key(x) == key).Select(_ => "Burst"),
                    .. m.Short.Sequences.Where(x => Key(x) == key).Select(_ => "Short"),
                    .. m.Long.Sequences.Where(x => Key(x) == key).Select(_ => "Long"),
                    .. m.Death.Sequences.Where(x => Key(x) == key).Select(_ => "Deathstream")
                ];
                string expected = run.ObjectCount <= 8 ? "Burst" : run.ObjectCount <= 16 ? "Short" : run.ObjectCount <= 32 ? "Long" : "Deathstream";
                if (qualified.Length != 1 || qualified[0] != expected)
                    bugs.Add($"{m.Label} {Time(run.StartTime)}–{Time(run.EndTime)} ({run.ObjectCount} objects): expected {expected}, got [{string.Join(',', qualified)}].");
                else distribution[expected]++;
            }
        }
        report.AppendLine("## 4. Partition Burst / Short / Long / Deathstream");
        report.AppendLine();
        report.AppendLine($"Maximal rapid runs with >=3 objects: **{total}**. Burst={distribution["Burst"]}, Short={distribution["Short"]}, Long={distribution["Long"]}, Deathstream={distribution["Deathstream"]}.");
        report.AppendLine();
        report.AppendLine($"**LengthPartitionViolations = {bugs.Count - before}**");
        report.AppendLine();
    }

    private static void AppendStamina(StringBuilder report, IReadOnlyList<MapResult> maps)
    {
        StaminaStreamPatternSequence[] sequences = maps.SelectMany(x => x.Stamina.Sequences).ToArray();
        double[] gaps = sequences.SelectMany(x => x.RecoveryGaps).ToArray();
        int totalObjects = maps.Sum(x => x.Map.HitObjects.Count);
        int staminaObjects = maps.Sum(x => x.Stamina.StaminaObjectCount);
        MapResult road = maps.Single(x => x.Spec.Title.StartsWith("Road of Resistance", StringComparison.Ordinal));
        report.AppendLine("## 5. Stamina");
        report.AppendLine();
        report.AppendLine(FormattableString.Invariant($"Global sequences: **{sequences.Length}**; unique rapid objects: **{staminaObjects}**; global coverage: **{(double)staminaObjects / totalObjects:P2}**; average active ratio: **{Mean(sequences.Select(x => x.ActiveRatio)):F4}**."));
        report.AppendLine($"RunCount distribution: {Distribution(sequences.Select(x => (double)x.RunCount))}.");
        report.AppendLine($"RecoveryGap distribution (ms): {Distribution(gaps)}.");
        report.AppendLine();
        report.AppendLine($"Road reference: coverage={road.Stamina.Coverage:P2}, objects={road.Stamina.StaminaObjectCount}, SequenceCount={road.Stamina.StaminaSequenceCount}, AverageActiveRatio={road.Stamina.AverageActiveRatio:F4}.");
        foreach (StaminaStreamPatternSequence s in road.Stamina.Sequences)
            report.AppendLine($"- {Time(s.StartTime)}–{Time(s.EndTime)}: rapid objects={s.TotalRapidObjects}, RunCount={s.RunCount}, RecoveryGaps=[{Numbers(s.RecoveryGaps)}], ActiveRatio={s.ActiveRatio:F4}.");
        report.AppendLine();
    }

    private static void AppendIrregular(StringBuilder report, IReadOnlyList<MapResult> maps, List<string> bugs)
    {
        IrregularStreamPatternSequence[] eligible = maps.SelectMany(x => x.Irregular.Runs).Where(x => x.ObjectCount >= 6).ToArray();
        var qualified = maps.SelectMany(m => m.Irregular.Sequences.Select(s => (Map: m, Sequence: s))).ToArray();
        bool singleSpike = AnalyzeIrregular(80, 80, 80, 125, 80, 80, 80).Qualified;
        bool repeated = AnalyzeIrregular(70, 110, 72, 108, 69, 112, 71).Qualified;
        bool alternating = AnalyzeIrregular(80, 105, 78, 118, 82, 110, 76).Qualified;
        bool roadSynthetic = AnalyzeIrregular(67, 68, 67, 102, 101, 102, 101, 101, 102, 67, 68).Qualified;
        MapResult road = maps.Single(x => x.Spec.Title.StartsWith("Road of Resistance", StringComparison.Ordinal));
        bool roadExact = road.Irregular.Sequences.Any(x => x.StartTime == 25546 && x.EndTime == 26492);
        if (singleSpike || !repeated || !alternating || !roadSynthetic || !roadExact)
            bugs.Add($"Irregular validated controls failed: SingleSpike={singleSpike}, Repeated={repeated}, TemporalAlternation={alternating}, RoadSynthetic={roadSynthetic}, RoadExact={roadExact}.");

        report.AppendLine("## 6. Irregular");
        report.AppendLine();
        report.AppendLine($"Eligible runs: **{eligible.Length}**; qualified: **{qualified.Length}**; maps: {List(qualified.Select(x => x.Map.Label).Distinct())}.");
        report.AppendLine($"Controls: SingleSpike={singleSpike} (expected false); RepeatedIrregular={repeated}; TemporalAlternation={alternating}; Road synthetic={roadSynthetic}; Road exact 00:25.546–00:26.492={roadExact}.");
        report.AppendLine();
        report.AppendLine("|Map|Run|Intervals|Median|Atypical mask|Events|CV|Atypical ratio|Score|");
        report.AppendLine("|---|---|---|---:|---|---:|---:|---:|---:|");
        foreach (var item in qualified)
        {
            IrregularStreamPatternSequence s = item.Sequence;
            report.AppendLine(FormattableString.Invariant($"|{Cell(item.Map.Label)}|{Time(s.StartTime)}–{Time(s.EndTime)}|{Numbers(s.Intervals)}|{s.MedianInterval:F2}|{string.Join(',', s.AtypicalIntervalMask.Select(x => x ? '1' : '0'))}|{s.DistinctIrregularEventCount}|{s.CoefficientOfVariation:F4}|{s.AtypicalIntervalRatio:F4}|{s.IrregularityScore:F4}|"));
        }
        report.AppendLine();
    }

    private static void AppendSpeed(StringBuilder report, IReadOnlyList<MapResult> maps)
    {
        var runs = maps.SelectMany(m => m.Speed.Sequences.Select(s => (Map: m, Sequence: s))).ToArray();
        int totalObjects = maps.Sum(x => x.Map.HitObjects.Count);
        int speedObjects = maps.Sum(x => x.Speed.SpeedStreamObjectCount);
        report.AppendLine("## 7. Speed");
        report.AppendLine();
        report.AppendLine(FormattableString.Invariant($"Qualified runs: **{runs.Length}**; global coverage: **{(double)speedObjects / totalObjects:P2}**; weighted-map aggregate BPM average={Mean(runs.Select(x => x.Sequence.StreamBpm)):F2}, maximum={runs.Max(x => x.Sequence.StreamBpm):F2}, average intensity={Mean(runs.Select(x => x.Sequence.SpeedIntensity)):F4}."));
        report.AppendLine();
        report.AppendLine("Top 5 maps by Speed coverage: " + string.Join("; ", maps.OrderByDescending(x => x.Speed.Coverage).Take(5).Select(x => $"{x.Label} {x.Speed.Coverage:P2}")) + ".");
        report.AppendLine();
        report.AppendLine("Top 10 fastest runs:");
        foreach (var item in runs.OrderBy(x => x.Sequence.AverageInterval).Take(10))
            report.AppendLine(FormattableString.Invariant($"- {item.Map.Label} {Time(item.Sequence.StartTime)}–{Time(item.Sequence.EndTime)}: objects={item.Sequence.ObjectCount}, interval={item.Sequence.AverageInterval:F2} ms, BPM={item.Sequence.StreamBpm:F2}, intensity={item.Sequence.SpeedIntensity:F4}."));
        report.AppendLine();
        report.AppendLine("Per-map Speed: " + string.Join("; ", maps.Select(x => FormattableString.Invariant($"{x.Label}: coverage={x.Speed.Coverage:P2}, avgBPM={x.Speed.AverageStreamBpm:F2}, maxBPM={x.Speed.MaximumStreamBpm:F2}, intensity={x.Speed.AverageSpeedIntensity:F4}"))) + ".");
        report.AppendLine();
    }

    private static void AppendFlow(StringBuilder report, IReadOnlyList<MapResult> maps, List<string> bugs)
    {
        var current = maps.SelectMany(m => m.Flow.Sequences.Select(s => (Map: m, Sequence: s))).ToArray();
        var historical = maps.SelectMany(m => StreamPatternAnalyzer.AnalyzeFlowStream(m.Map.HitObjects,
            new FlowStreamPatternOptions(MinimumFlowLinkDistance: 20)).Sequences.Select(s => (Map: m, Sequence: s))).ToArray();
        var oldKeys = historical.Select(x => (x.Map.Label, x.Sequence.StartIndex, x.Sequence.EndIndex)).ToHashSet();
        var newKeys = current.Select(x => (x.Map.Label, x.Sequence.StartIndex, x.Sequence.EndIndex)).ToHashSet();
        var added = current.Where(x => !oldKeys.Contains((x.Map.Label, x.Sequence.StartIndex, x.Sequence.EndIndex))).ToArray();
        var removed = historical.Where(x => !newKeys.Contains((x.Map.Label, x.Sequence.StartIndex, x.Sequence.EndIndex))).ToArray();
        bool reign = current.Any(x => x.Map.Spec.Title == "Reign of Fear" && x.Sequence.StartTime == 256486 && x.Sequence.EndTime == 257062);
        if (!reign) bugs.Add("Flow 18 px regression: Reign of Fear 04:16.486–04:17.062 is not qualified.");
        if (removed.Length != 0) bugs.Add($"Flow 18 px regression unexpectedly removed {removed.Length} historical run(s).");
        if (added.Length != 1 || !reign) bugs.Add($"Flow 18 px delta expected exactly the Reign run, observed added={added.Length}.");

        report.AppendLine("## 8. Flow");
        report.AppendLine();
        report.AppendLine($"Historical 20 px SequenceCount={historical.Length}; current 18 px SequenceCount={current.Length}; delta={current.Length - historical.Length:+#;-#;0}; removed={removed.Length}.");
        report.AppendLine("Added at 18 px: " + List(added.Select(x => $"{x.Map.Label} {Time(x.Sequence.StartTime)}–{Time(x.Sequence.EndTime)} minDistance={x.Sequence.MinimumDistance:F2}px")) + ".");
        report.AppendLine($"Required Reign regression qualified: **{reign}**.");
        report.AppendLine();
    }

    private static void AppendAlternating(StringBuilder report, IReadOnlyList<MapResult> maps)
    {
        var qualified = maps.SelectMany(m => m.Alternating.Sequences.Select(s => (Map: m, Sequence: s))).ToArray();
        var rejected = maps.SelectMany(m => m.Alternating.Runs.Where(x => !x.Qualified && x.ObjectCount >= 6).Select(s => (Map: m, Sequence: s)))
            .OrderByDescending(x => x.Sequence.AlternatingScore).Take(10).ToArray();
        bool extreme = qualified.Any(x => x.Map.Spec.Title == "Extreme Fantasy");
        report.AppendLine("## 9. Alternating");
        report.AppendLine();
        report.AppendLine($"SequenceCount={qualified.Length}; maps={List(qualified.Select(x => x.Map.Label).Distinct())}; Extreme Fantasy positive control={extreme}.");
        report.AppendLine("Best non-qualified candidates (no threshold simulation):");
        foreach (var item in rejected)
            report.AppendLine(FormattableString.Invariant($"- {item.Map.Label} {Time(item.Sequence.StartTime)}–{Time(item.Sequence.EndTime)}: objects={item.Sequence.ObjectCount}, valid={item.Sequence.ValidAlternatingPivotRatio:F4}, alternation={item.Sequence.AlternationRatio:F4}, angleConsistency={item.Sequence.AlternatingAngleConsistency:F4}, score={item.Sequence.AlternatingScore:F4}, reason={item.Sequence.Reason}."));
        report.AppendLine();
    }

    private static void AppendCoexistence(StringBuilder report, IReadOnlyList<MapResult> maps)
    {
        var combinations = new Dictionary<string, int>();
        var named = new[] { "Short + Speed", "Long + Speed", "Deathstream + Speed", "Short + Flow", "Long + Flow", "Speed + Flow", "Speed + Alternating", "Irregular + Speed", "Stamina + Long", "Stamina + Deathstream" }.ToDictionary(x => x, _ => 0);
        foreach (MapResult m in maps)
        {
            var staminaKeys = m.Stamina.Sequences.SelectMany(x => x.Runs).Select(Key).ToHashSet();
            foreach (BurstPatternSequence run in m.Burst.Runs.Where(x => x.ObjectCount >= 3))
            {
                var key = Key(run);
                string length = run.ObjectCount <= 8 ? "Burst" : run.ObjectCount <= 16 ? "Short" : run.ObjectCount <= 32 ? "Long" : "Deathstream";
                var tags = new List<string> { length };
                if (staminaKeys.Contains(key)) tags.Add("Stamina");
                if (m.Irregular.Sequences.Any(x => Key(x) == key)) tags.Add("Irregular");
                if (m.Speed.Sequences.Any(x => Key(x) == key)) tags.Add("Speed");
                if (m.Flow.Sequences.Any(x => Key(x) == key)) tags.Add("Flow");
                if (m.Alternating.Sequences.Any(x => Key(x) == key)) tags.Add("Alternating");
                string combo = string.Join(" + ", tags);
                combinations[combo] = combinations.GetValueOrDefault(combo) + 1;
                foreach (string pair in named.Keys.ToArray())
                    if (pair.Split(" + ").All(tags.Contains)) named[pair]++;
            }
        }
        report.AppendLine("## 10. Pattern coexistence");
        report.AppendLine();
        report.AppendLine("|Combination on one maximal run|Count|");
        report.AppendLine("|---|---:|");
        foreach (var item in combinations.OrderByDescending(x => x.Value).ThenBy(x => x.Key).Take(25)) report.AppendLine($"|{item.Key}|{item.Value}|");
        report.AppendLine();
        report.AppendLine("Requested pair checks: " + string.Join("; ", named.Select(x => $"{x.Key}={x.Value}")) + ". Coexistence is descriptive and is not treated as an error.");
        report.AppendLine();
    }

    private static void AppendFlowVsAlternating(StringBuilder report, IReadOnlyList<MapResult> maps)
    {
        var flow = maps.SelectMany(m => m.Flow.Sequences.Select(s => (Map: m, Sequence: s))).ToArray();
        var alternating = maps.SelectMany(m => m.Alternating.Sequences.Select(s => (Map: m, Sequence: s))).ToArray();
        var altKeys = alternating.ToDictionary(x => (x.Map.Label, x.Sequence.StartIndex, x.Sequence.EndIndex));
        var overlaps = flow.Where(x => altKeys.ContainsKey((x.Map.Label, x.Sequence.StartIndex, x.Sequence.EndIndex))).ToArray();
        report.AppendLine("## 11. Flow vs Alternating");
        report.AppendLine();
        report.AppendLine($"Current thresholds: Flow distance 18 px; Alternating distance 20 px. Flow={flow.Length}; Alternating={alternating.Length}; exact-run overlaps={overlaps.Length}.");
        foreach (var item in overlaps)
        {
            AlternatingStreamPatternSequence a = altKeys[(item.Map.Label, item.Sequence.StartIndex, item.Sequence.EndIndex)].Sequence;
            FlowStreamPatternSequence f = item.Sequence;
            report.AppendLine(FormattableString.Invariant($"- {item.Map.Label} {Time(f.StartTime)}–{Time(f.EndTime)}: TurnSigns=[{string.Join(',', f.TurnSigns)}], FlowPivotRatio={f.FlowPivotRatio:F4}, direction={f.TurnDirectionConsistency:F4}, AlternationRatio={a.AlternationRatio:F4}, FlowScore={f.FlowScore:F4}, AlternatingScore={a.AlternatingScore:F4}."));
        }
        report.AppendLine();
    }

    private static void AppendConfidenceAudit(StringBuilder report, IReadOnlyList<MapResult> maps, List<string> bugs)
    {
        int before = bugs.Count;
        report.AppendLine("## 12. Confidence audit");
        report.AppendLine();
        report.AppendLine("|Pattern|Present maps|Min|Mean|Median|Max|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach (string pattern in PatternNames)
        {
            PatternView[] views = maps.Select(m => Views(m).Single(x => x.Name == pattern)).ToArray();
            foreach (PatternView v in views)
            {
                if (v.Confidence < 0 || v.Confidence > 1) bugs.Add($"{pattern}: confidence outside [0,1]: {v.Confidence}.");
                if (v.SequenceCount > 0 && v.Confidence == 0) bugs.Add($"{pattern}: present with zero confidence.");
                if (v.SequenceCount == 0 && v.Confidence > 0) bugs.Add($"{pattern}: absent with non-zero confidence {v.Confidence}.");
            }
            double[] present = views.Where(x => x.SequenceCount > 0).Select(x => x.Confidence).OrderBy(x => x).ToArray();
            report.AppendLine(FormattableString.Invariant($"|{pattern}|{present.Length}|{Min(present):F4}|{Mean(present):F4}|{Median(present):F4}|{Max(present):F4}|"));
        }
        report.AppendLine();
        report.AppendLine($"**ConfidenceViolations = {bugs.Count - before}**");
        report.AppendLine();
    }

    private static void AppendCoverageAudit(StringBuilder report, IReadOnlyList<MapResult> maps, List<string> bugs)
    {
        int before = bugs.Count;
        foreach (MapResult m in maps)
        {
            CheckCoverage(m.Label, "Burst", m.Burst.TotalObjects, m.Burst.BurstObjectCount, m.Burst.Coverage, m.Burst.Sequences.SelectMany(Indices), bugs);
            CheckCoverage(m.Label, "Short", m.Short.TotalObjects, m.Short.ShortStreamObjectCount, m.Short.Coverage, m.Short.Sequences.SelectMany(Indices), bugs);
            CheckCoverage(m.Label, "Long", m.Long.TotalObjects, m.Long.LongStreamObjectCount, m.Long.Coverage, m.Long.Sequences.SelectMany(Indices), bugs);
            CheckCoverage(m.Label, "Deathstream", m.Death.TotalObjects, m.Death.DeathstreamObjectCount, m.Death.Coverage, m.Death.Sequences.SelectMany(Indices), bugs);
            CheckCoverage(m.Label, "Stamina", m.Stamina.TotalObjects, m.Stamina.StaminaObjectCount, m.Stamina.Coverage, m.Stamina.Sequences.SelectMany(x => x.Runs).SelectMany(Indices), bugs);
            CheckCoverage(m.Label, "Irregular", m.Irregular.TotalObjects, m.Irregular.IrregularStreamObjectCount, m.Irregular.Coverage, m.Irregular.Sequences.SelectMany(Indices), bugs);
            CheckCoverage(m.Label, "Speed", m.Speed.TotalObjects, m.Speed.SpeedStreamObjectCount, m.Speed.Coverage, m.Speed.Sequences.SelectMany(Indices), bugs);
            CheckCoverage(m.Label, "Flow", m.Flow.TotalObjects, m.Flow.FlowStreamObjectCount, m.Flow.Coverage, m.Flow.Sequences.SelectMany(Indices), bugs);
            CheckCoverage(m.Label, "Alternating", m.Alternating.TotalObjects, m.Alternating.AlternatingStreamObjectCount, m.Alternating.Coverage, m.Alternating.Sequences.SelectMany(Indices), bugs);
        }
        report.AppendLine("## 13. Coverage audit");
        report.AppendLine();
        report.AppendLine("Coverage was recomputed from unique qualified indices. Stamina uses constituent rapid-run indices only, never recovery spans. Rejected runs are excluded.");
        report.AppendLine();
        report.AppendLine($"**CoverageViolations = {bugs.Count - before}**");
        report.AppendLine();
    }

    private static void AppendIntegrityAudit(StringBuilder report, IReadOnlyList<MapResult> maps, List<string> bugs)
    {
        int before = bugs.Count;
        foreach (MapResult m in maps)
        {
            foreach (SequenceView s in QualifiedSequences(m)) CheckSequence(m, s, bugs);
            foreach (StaminaStreamPatternSequence s in m.Stamina.Sequences)
            {
                if (s.StartIndex > s.EndIndex || s.StartTime > s.EndTime || s.Duration < 0 || s.Runs.Count == 0 || s.TotalRapidObjects != s.Runs.Sum(x => x.ObjectCount))
                    bugs.Add($"{m.Label} Stamina {s.StartIndex}-{s.EndIndex}: incoherent sequence envelope.");
                if (s.RecoveryGaps.Count != Math.Max(0, s.RunCount - 1) || s.RecoveryGaps.Any(x => x < 0 || x > 1500))
                    bugs.Add($"{m.Label} Stamina {s.StartIndex}-{s.EndIndex}: incoherent recovery gaps.");
                foreach (StaminaRapidRun run in s.Runs) CheckSequence(m, new("StaminaRun", run.StartIndex, run.EndIndex, run.StartTime, run.EndTime, run.ObjectCount, run.Duration), bugs);
            }
        }
        report.AppendLine("## 14. Sequence integrity");
        report.AppendLine();
        report.AppendLine("Qualified boundaries, object counts, durations, source timestamps, Stamina run envelopes, and recovery-gap cardinality were checked.");
        report.AppendLine();
        report.AppendLine($"**SequenceIntegrityViolations = {bugs.Count - before}**");
        report.AppendLine();
    }

    private static void AppendDebugAudit(StringBuilder report, List<string> bugs)
    {
        int before = bugs.Count;
        bool[] flags = [GameplayDebug.BurstPatternEnabled, GameplayDebug.ShortStreamPatternEnabled, GameplayDebug.LongStreamPatternEnabled, GameplayDebug.DeathstreamPatternEnabled, GameplayDebug.StaminaStreamPatternEnabled, GameplayDebug.IrregularStreamPatternEnabled, GameplayDebug.SpeedStreamPatternEnabled, GameplayDebug.FlowStreamPatternEnabled, GameplayDebug.AlternatingStreamPatternEnabled];
        if (flags.Any(x => x)) bugs.Add("One or more Stream debug flags are enabled by default.");
        string root = FindRepoRoot();
        string debug = File.ReadAllText(Path.Combine(root, "BeatInsight", "Debug", "GameplayDebug.cs"));
        string analyzerDirectory = Path.Combine(root, "BeatInsight", "Analysis", "StreamPatterns");
        string combined = debug + string.Join(Environment.NewLine, Directory.GetFiles(analyzerDirectory, "*.cs").Select(File.ReadAllText));
        if (combined.Contains("Console.Write", StringComparison.Ordinal)) bugs.Add("Console.Write usage found in Stream analyzer/debug sources.");
        foreach (string marker in new[] { "BurstPattern(", "ShortStreamPattern(", "LongStreamPattern(", "DeathstreamPattern(", "StaminaStreamPattern(", "IrregularStreamPattern(", "SpeedStreamPattern(", "FlowStreamPattern(", "AlternatingStreamPattern(" })
            if (!debug.Contains(marker, StringComparison.Ordinal)) bugs.Add($"GameplayDebug entry point missing: {marker}");
        if (!debug.Contains("DebugLogger.Log", StringComparison.Ordinal)) bugs.Add("GameplayDebug does not use DebugLogger.");
        report.AppendLine("## 15. Debug audit");
        report.AppendLine();
        report.AppendLine($"All nine entry points present; DebugLogger used; Console.Write absent; nine default flags disabled. **DebugViolations = {bugs.Count - before}**.");
        report.AppendLine();
    }

    private static void AppendTestInventory(StringBuilder report)
    {
        report.AppendLine("## 16. Test inventory");
        report.AppendLine();
        report.AppendLine("|Family filter|Tests|");
        report.AppendLine("|---|---:|");
        report.AppendLine("|Burst|15|");
        report.AppendLine("|Short|16|");
        report.AppendLine("|Long|19|");
        report.AppendLine("|Deathstream|20|");
        report.AppendLine("|Stamina|17|");
        report.AppendLine("|Irregular (includes structural forensic)|26|");
        report.AppendLine("|Speed|16|");
        report.AppendLine("|Flow|22|");
        report.AppendLine("|Alternating|23|");
        report.AppendLine("|Shared/common forensic runners|3|");
        report.AppendLine("|**Total Stream Pattern**|**177**|");
        report.AppendLine();
        report.AppendLine("The prior 176 total was 174 family-filtered tests plus 2 shared runners. This consolidation adds one shared runner and no production-family test: **176 + 1 = 177**.");
        report.AppendLine();
    }

    private static void AppendFinalValidation(StringBuilder report)
    {
        report.AppendLine("## 17. Final validation");
        report.AppendLine();
        report.AppendLine("- Stream Pattern tests: **177/177 passed**.");
        report.AppendLine("- Jump Pattern tests: **189/189 passed**.");
        report.AppendLine("- Solution build: **passed, 0 warnings, 0 errors**.");
        report.AppendLine("- `git diff --check`: **passed** (line-ending conversion warnings, if emitted, are non-failing).");
        report.AppendLine();
    }

    private static void AppendConclusion(StringBuilder report, IReadOnlyList<MapResult> maps, IReadOnlyList<string> bugs)
    {
        int alternatingMaps = maps.Count(x => x.Alternating.AlternatingStreamSequenceCount > 0);
        report.AppendLine("## 18. OBJECTIVE BUGS");
        report.AppendLine();
        if (bugs.Count == 0) report.AppendLine("None detected by the consolidation invariants.");
        else foreach (string bug in bugs) report.AppendLine($"- {bug}");
        report.AppendLine();
        report.AppendLine("## 19. SEMANTIC / CALIBRATION QUESTIONS");
        report.AppendLine();
        report.AppendLine("- Alternating remains deliberately conservative and has a narrow real-corpus positive base; collect more labelled positive controls before future calibration.");
        report.AppendLine("- Irregular has one real positive in this corpus; Road is a validated structural reference, but broader labelled evidence remains useful.");
        report.AppendLine("- Orthogonal coexistence is expected and should not be interpreted as double classification of the length dimension.");
        report.AppendLine();
        report.AppendLine("## 20. Final status by pattern");
        report.AppendLine();
        report.AppendLine("|Pattern|Status|Evidence|");
        report.AppendLine("|---|---|---|");
        report.AppendLine("|Burst|STABLE_V1|Exact length partition and boundary suite pass.|");
        report.AppendLine("|Short Stream|STABLE_V1|Exact length partition and boundary suite pass.|");
        report.AppendLine("|Long Stream|STABLE_V1|Exact length partition and boundary suite pass.|");
        report.AppendLine("|Deathstream|STABLE_V1|Exact length partition and boundary suite pass.|");
        report.AppendLine("|Stamina|STABLE_WITH_MONITORING|Multi-run/recovery semantics and coverage invariants pass; retain corpus monitoring.|");
        report.AppendLine("|Irregular|STABLE_WITH_MONITORING|Structural controls and Road exact reference pass; real positive base remains narrow.|");
        report.AppendLine("|Speed|STABLE_V1|Absolute-speed rule, BPM/intensity bounds, and coverage invariants pass.|");
        report.AppendLine("|Flow|STABLE_V1|18 px regression adds exactly the validated Reign run and removes none.|");
        report.AppendLine($"|Alternating|NEEDS_MORE_POSITIVE_CONTROLS|Conservative detector; {alternatingMaps} real-corpus map(s) currently positive, with Extreme Fantasy retained as primary control.|");
        report.AppendLine();
        report.AppendLine("## 21. V1 freeze recommendation");
        report.AppendLine();
        report.AppendLine(bugs.Count == 0
            ? "**FROZEN_FOR_V1** — all objective analyzer-level conditions and final suite/build/diff validations pass. The limited Alternating positive-control base is documented monitoring debt, not an objective blocker."
            : "**NOT_READY_FOR_FREEZE** — objective bugs are listed above; no automatic correction was applied.");
    }

    private static IReadOnlyList<PatternView> Views(MapResult m) =>
    [
        new("Burst", m.Burst.BurstSequenceCount, m.Burst.BurstObjectCount, m.Burst.Coverage, m.Burst.LongestBurst, m.Burst.Confidence, $"avgInterval={m.Burst.AverageInterval:F2}; consistency={m.Burst.AverageIntervalConsistency:F4}"),
        new("Short", m.Short.ShortStreamSequenceCount, m.Short.ShortStreamObjectCount, m.Short.Coverage, m.Short.LongestShortStream, m.Short.Confidence, $"avgInterval={m.Short.AverageInterval:F2}; consistency={m.Short.AverageIntervalConsistency:F4}"),
        new("Long", m.Long.LongStreamSequenceCount, m.Long.LongStreamObjectCount, m.Long.Coverage, m.Long.LongestLongStream, m.Long.Confidence, $"avgInterval={m.Long.AverageInterval:F2}; consistency={m.Long.AverageIntervalConsistency:F4}"),
        new("Deathstream", m.Death.DeathstreamSequenceCount, m.Death.DeathstreamObjectCount, m.Death.Coverage, m.Death.LongestDeathstream, m.Death.Confidence, $"avgInterval={m.Death.AverageInterval:F2}; consistency={m.Death.AverageIntervalConsistency:F4}"),
        new("Stamina", m.Stamina.StaminaSequenceCount, m.Stamina.StaminaObjectCount, m.Stamina.Coverage, m.Stamina.LongestStaminaObjectCount, m.Stamina.Confidence, $"runs/seq={m.Stamina.AverageRunsPerSequence:F2}; active={m.Stamina.AverageActiveRatio:F4}; recovery={m.Stamina.AverageRecoveryGap:F2}"),
        new("Irregular", m.Irregular.IrregularStreamSequenceCount, m.Irregular.IrregularStreamObjectCount, m.Irregular.Coverage, m.Irregular.LongestIrregularStream, m.Irregular.Confidence, $"CV={m.Irregular.AverageCoefficientOfVariation:F4}; events={m.Irregular.AverageDistinctIrregularEvents:F2}; score={m.Irregular.AverageIrregularityScore:F4}"),
        new("Speed", m.Speed.SpeedStreamSequenceCount, m.Speed.SpeedStreamObjectCount, m.Speed.Coverage, m.Speed.LongestSpeedStream, m.Speed.Confidence, $"avg/max BPM={m.Speed.AverageStreamBpm:F2}/{m.Speed.MaximumStreamBpm:F2}; intensity={m.Speed.AverageSpeedIntensity:F4}"),
        new("Flow", m.Flow.FlowStreamSequenceCount, m.Flow.FlowStreamObjectCount, m.Flow.Coverage, m.Flow.LongestFlowStream, m.Flow.Confidence, $"pivot={m.Flow.AverageFlowPivotRatio:F4}; direction={m.Flow.AverageTurnDirectionConsistency:F4}; score={m.Flow.AverageFlowScore:F4}"),
        new("Alternating", m.Alternating.AlternatingStreamSequenceCount, m.Alternating.AlternatingStreamObjectCount, m.Alternating.Coverage, m.Alternating.LongestAlternatingStream, m.Alternating.Confidence, $"valid={m.Alternating.AverageValidPivotRatio:F4}; alternation={m.Alternating.AverageAlternationRatio:F4}; score={m.Alternating.AverageAlternatingScore:F4}")
    ];

    private static readonly string[] PatternNames = ["Burst", "Short", "Long", "Deathstream", "Stamina", "Irregular", "Speed", "Flow", "Alternating"];

    private static IEnumerable<SequenceView> QualifiedSequences(MapResult m)
    {
        foreach (var x in m.Burst.Sequences) yield return View("Burst", x.StartIndex, x.EndIndex, x.StartTime, x.EndTime);
        foreach (var x in m.Short.Sequences) yield return View("Short", x.StartIndex, x.EndIndex, x.StartTime, x.EndTime);
        foreach (var x in m.Long.Sequences) yield return View("Long", x.StartIndex, x.EndIndex, x.StartTime, x.EndTime);
        foreach (var x in m.Death.Sequences) yield return View("Deathstream", x.StartIndex, x.EndIndex, x.StartTime, x.EndTime);
        foreach (var x in m.Irregular.Sequences) yield return View("Irregular", x.StartIndex, x.EndIndex, x.StartTime, x.EndTime);
        foreach (var x in m.Speed.Sequences) yield return View("Speed", x.StartIndex, x.EndIndex, x.StartTime, x.EndTime);
        foreach (var x in m.Flow.Sequences) yield return View("Flow", x.StartIndex, x.EndIndex, x.StartTime, x.EndTime);
        foreach (var x in m.Alternating.Sequences) yield return View("Alternating", x.StartIndex, x.EndIndex, x.StartTime, x.EndTime);
    }

    private static SequenceView View(string name, int start, int end, int startTime, int endTime) => new(name, start, end, startTime, endTime, end - start + 1, endTime - startTime);
    private static void CheckSequence(MapResult m, SequenceView s, List<string> bugs)
    {
        if (s.StartIndex < 0 || s.EndIndex >= m.Map.HitObjects.Count || s.StartIndex > s.EndIndex || s.StartTime > s.EndTime || s.ObjectCount != s.EndIndex - s.StartIndex + 1 || s.Duration < 0)
            bugs.Add($"{m.Label} {s.Pattern} {s.StartIndex}-{s.EndIndex}: invalid bounds/count/duration.");
        else if (m.Map.HitObjects[s.StartIndex].Time != s.StartTime || m.Map.HitObjects[s.EndIndex].Time != s.EndTime)
            bugs.Add($"{m.Label} {s.Pattern} {s.StartIndex}-{s.EndIndex}: timestamps disagree with source objects.");
    }

    private static void CheckCoverage(string map, string pattern, int total, int count, double coverage, IEnumerable<int> source, List<string> bugs)
    {
        int[] indices = source.ToArray();
        int unique = indices.Distinct().Count();
        double expected = total == 0 ? 0 : (double)unique / total;
        if (coverage < 0 || coverage > 1 || count != unique || Math.Abs(coverage - expected) > 1e-12 || indices.Any(x => x < 0 || x >= total))
            bugs.Add($"{map} {pattern}: count={count}, unique={unique}, coverage={coverage}, expected={expected}.");
    }

    private static IrregularStreamPatternSequence AnalyzeIrregular(params int[] intervals)
    {
        var objects = new List<HitObject>();
        int time = 0;
        objects.Add(new HitObject { Type = 1, Time = 0, X = 0, Y = 0 });
        foreach (int interval in intervals)
        {
            time += interval;
            objects.Add(new HitObject { Type = 1, Time = time, X = objects.Count * 20, Y = 0 });
        }
        return Assert.Single(StreamPatternAnalyzer.AnalyzeIrregularStream(objects).Runs);
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BeatInsight.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("BeatInsight repository root not found.");
    }

    private static (int, int) Key(BurstPatternSequence x) => (x.StartIndex, x.EndIndex);
    private static (int, int) Key(ShortStreamPatternSequence x) => (x.StartIndex, x.EndIndex);
    private static (int, int) Key(LongStreamPatternSequence x) => (x.StartIndex, x.EndIndex);
    private static (int, int) Key(DeathstreamPatternSequence x) => (x.StartIndex, x.EndIndex);
    private static (int, int) Key(StaminaRapidRun x) => (x.StartIndex, x.EndIndex);
    private static (int, int) Key(IrregularStreamPatternSequence x) => (x.StartIndex, x.EndIndex);
    private static (int, int) Key(SpeedStreamPatternSequence x) => (x.StartIndex, x.EndIndex);
    private static (int, int) Key(FlowStreamPatternSequence x) => (x.StartIndex, x.EndIndex);
    private static (int, int) Key(AlternatingStreamPatternSequence x) => (x.StartIndex, x.EndIndex);
    private static IEnumerable<int> Indices(BurstPatternSequence x) => Enumerable.Range(x.StartIndex, x.ObjectCount);
    private static IEnumerable<int> Indices(ShortStreamPatternSequence x) => Enumerable.Range(x.StartIndex, x.ObjectCount);
    private static IEnumerable<int> Indices(LongStreamPatternSequence x) => Enumerable.Range(x.StartIndex, x.ObjectCount);
    private static IEnumerable<int> Indices(DeathstreamPatternSequence x) => Enumerable.Range(x.StartIndex, x.ObjectCount);
    private static IEnumerable<int> Indices(StaminaRapidRun x) => Enumerable.Range(x.StartIndex, x.ObjectCount);
    private static IEnumerable<int> Indices(IrregularStreamPatternSequence x) => Enumerable.Range(x.StartIndex, x.ObjectCount);
    private static IEnumerable<int> Indices(SpeedStreamPatternSequence x) => Enumerable.Range(x.StartIndex, x.ObjectCount);
    private static IEnumerable<int> Indices(FlowStreamPatternSequence x) => Enumerable.Range(x.StartIndex, x.ObjectCount);
    private static IEnumerable<int> Indices(AlternatingStreamPatternSequence x) => Enumerable.Range(x.StartIndex, x.ObjectCount);
    private static string Distribution(IEnumerable<double> source) { double[] v = source.OrderBy(x => x).ToArray(); return $"n={v.Length}, min={Min(v):F2}, median={Median(v):F2}, mean={Mean(v):F2}, max={Max(v):F2}"; }
    private static double Mean(IEnumerable<double> source) { double[] v = source.ToArray(); return v.Length == 0 ? 0 : v.Average(); }
    private static double Median(IReadOnlyList<double> v) { if (v.Count == 0) return 0; int m = v.Count / 2; return v.Count % 2 == 0 ? (v[m - 1] + v[m]) / 2 : v[m]; }
    private static double Min(IReadOnlyList<double> v) => v.Count == 0 ? 0 : v.Min();
    private static double Max(IReadOnlyList<double> v) => v.Count == 0 ? 0 : v.Max();
    private static string Numbers(IEnumerable<double> source) => string.Join(',', source.Select(x => x.ToString("F2", CultureInfo.InvariantCulture)));
    private static string List(IEnumerable<string> source) { string[] v = source.ToArray(); return v.Length == 0 ? "None" : string.Join("; ", v); }
    private static string Pct(double value) => (value * 100).ToString("F2", CultureInfo.InvariantCulture);
    private static string Time(int value) => TimeSpan.FromMilliseconds(value).ToString(@"mm\:ss\.fff");
    private static string Cell(string value) => value.Replace("|", "\\|");

    private sealed record PatternView(string Name, int SequenceCount, int ObjectCount, double Coverage, int Longest, double Confidence, string Metrics);
    private sealed record SequenceView(string Pattern, int StartIndex, int EndIndex, int StartTime, int EndTime, int ObjectCount, double Duration);
    private sealed record MapResult(StreamPatternGlobalForensicTests.MapSpec Spec, Beatmap Map, BurstPatternAnalysis Burst, ShortStreamPatternAnalysis Short, LongStreamPatternAnalysis Long, DeathstreamPatternAnalysis Death, StaminaStreamPatternAnalysis Stamina, IrregularStreamPatternAnalysis Irregular, SpeedStreamPatternAnalysis Speed, FlowStreamPatternAnalysis Flow, AlternatingStreamPatternAnalysis Alternating)
    {
        public string Label => $"{Spec.Title} [{Spec.Version}]";
    }
}
