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
/// Structural forensic and post-change non-regression study for Irregular Stream.
/// Alternative candidate metrics and legacy comparisons remain test-only.
/// </summary>
public sealed class IrregularStreamPatternStructuralForensicTests(ITestOutputHelper output)
{
    private const double SignificantDeltaThreshold = 12;
    private const double CvThreshold = 0.10;
    private const double CandidateSignificantRatio = 0.20;
    private const double AtypicalMedianDistance = 12;

    [Fact]
    public void ExistingCorpus_WritesReadOnlyStructuralComparison()
    {
        Assert.All(StreamPatternGlobalForensicTests.Corpus,
            spec => Assert.True(File.Exists(spec.Path), $"Missing corpus map: {spec.Path}"));

        CorpusMap[] maps = StreamPatternGlobalForensicTests.Corpus.Select(Load).ToArray();
        RunView[] corpus = maps.SelectMany(map => map.Analysis.Runs
                .Where(run => run.ObjectCount >= 6)
                .Select(run => View(map.Label, run)))
            .ToArray();
        Assert.Equal(190, corpus.Length);

        RunView[] references =
        [
            Synthetic("Regular", [80, 80, 80, 80, 80, 80, 80]),
            Synthetic("LowNoise", [79, 81, 80, 79, 82, 80, 81]),
            Synthetic("SingleSpike", [80, 80, 80, 125, 80, 80, 80]),
            Synthetic("RepeatedIrregular", [80, 105, 78, 118, 82, 110, 76]),
            Synthetic("TemporalAlternation", [70, 110, 72, 108, 69, 112, 71]),
            corpus.Single(run => run.Label.StartsWith("Road of Resistance", StringComparison.Ordinal)
                && run.Sequence.StartTime == 25_546 && run.Sequence.EndTime == 26_492) with
            {
                Label = "RoadOfResistanceCandidate"
            }
        ];

        var report = new StringBuilder();
        AppendHeader(report, maps, corpus);
        AppendBaseline(report, corpus, references);
        AppendDistributions(report, corpus);
        AppendMetricDefinitions(report);
        AppendReferenceTable(report, references);
        AppendCandidateSections(report, corpus, references);
        AppendRuleSimulations(report, corpus, references);
        AppendRoadAnalysis(report, references);
        AppendErrorsAndRecommendation(report, corpus, references);
        AppendPostChangeComparison(report, corpus, references);

        string directory = Environment.GetEnvironmentVariable("BEATINSIGHT_FORENSIC_OUTPUT_DIRECTORY")
            ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "irregular-structural-forensic.md");
        File.WriteAllText(path, report.ToString(), Encoding.UTF8);
        output.WriteLine($"Corpus: {corpus.Length} admissible runs across {maps.Length} maps");
        output.WriteLine($"Report: {path}");
    }

    private static CorpusMap Load(StreamPatternGlobalForensicTests.MapSpec spec)
    {
        Beatmap map = BeatmapParser.Load(spec.Path);
        Assert.Equal(spec.Title, map.Title);
        Assert.Equal(spec.Version, map.Version);
        return new($"{spec.Title} [{spec.Version}]", map,
            StreamPatternAnalyzer.AnalyzeIrregularStream(map.HitObjects));
    }

    private static RunView Synthetic(string label, int[] intervals)
    {
        var objects = new List<HitObject> { new() { Type = 1, Time = 0 } };
        foreach (int interval in intervals)
            objects.Add(new HitObject { Type = 1, Time = objects[^1].Time + interval });
        IrregularStreamPatternSequence sequence = Assert.Single(
            StreamPatternAnalyzer.AnalyzeIrregularStream(objects).Runs);
        return View(label, sequence);
    }

    private static RunView View(string label, IrregularStreamPatternSequence sequence) =>
        new(label, sequence, StructuralMetrics.Calculate(sequence.Intervals));

    private static void AppendHeader(StringBuilder report, IReadOnlyList<CorpusMap> maps,
        IReadOnlyList<RunView> corpus)
    {
        report.AppendLine("# Irregular Structural Forensic");
        report.AppendLine();
        report.AppendLine("Structural study plus post-change verification. Alternative candidates and the reconstructed legacy rule exist only in this test runner; the implemented production rule is compared explicitly at the end.");
        report.AppendLine();
        report.AppendLine($"Dataset: **{maps.Count}/18 maps**, **{corpus.Count} admissible maximal rapid-circle runs** (minimum six objects), using the existing Stream segmentation.");
        report.AppendLine();
    }

    private static void AppendBaseline(StringBuilder report, IReadOnlyList<RunView> corpus,
        IReadOnlyList<RunView> references)
    {
        report.AppendLine("## 1. Legacy baseline");
        report.AppendLine();
        report.AppendLine("Current qualification: objects >= 6, CV >= 0.10, absolute interval-delta threshold 12 ms, significant-delta ratio >= 0.30.");
        report.AppendLine();
        report.AppendLine($"Real-corpus baseline positives: **{corpus.Count(CurrentRule)} / {corpus.Count}**.");
        report.AppendLine();
        report.AppendLine("## 2. Isolated-spike problem");
        report.AppendLine();
        RunView spike = Reference(references, "SingleSpike");
        RunView road = Reference(references, "RoadOfResistanceCandidate");
        report.AppendLine(FormattableString.Invariant($"- SingleSpike: deltas=[{Numbers(spike.Sequence.IntervalDeltas)}], CV={spike.Sequence.CoefficientOfVariation:F4}, significant={spike.Sequence.SignificantDeltaCount}/{spike.Sequence.IntervalDeltas.Count} ({spike.Sequence.SignificantDeltaRatio:F4}), legacy qualified={CurrentRule(spike)}, production qualified={spike.Sequence.Qualified}."));
        report.AppendLine(FormattableString.Invariant($"- Road: deltas=[{Numbers(road.Sequence.IntervalDeltas)}], CV={road.Sequence.CoefficientOfVariation:F4}, significant={road.Sequence.SignificantDeltaCount}/{road.Sequence.IntervalDeltas.Count} ({road.Sequence.SignificantDeltaRatio:F4}), legacy qualified={CurrentRule(road)}, production qualified={road.Sequence.Qualified}."));
        report.AppendLine("The spike receives two significant transitions from the entry and exit of one atypical interval. Road also has two transitions, but they are separated and diluted by a longer denominator. The current ratio has no concept of independent events or spatial distribution along the interval series.");
        report.AppendLine();
    }

    private static void AppendDistributions(StringBuilder report, IReadOnlyList<RunView> corpus)
    {
        report.AppendLine("## 3. Real-corpus distributions");
        report.AppendLine();
        Distribution(report, "CV", corpus.Select(x => x.Sequence.CoefficientOfVariation));
        Distribution(report, "SignificantDeltaRatio", corpus.Select(x => x.Sequence.SignificantDeltaRatio));
        Distribution(report, "SignificantDeltaClusterCount", corpus.Select(x => (double)x.Structural.ClusterCount));
        Distribution(report, "ClusterRatio", corpus.Select(x => x.Structural.ClusterRatio));
        Distribution(report, "DistinctIrregularEventCount", corpus.Select(x => (double)x.Structural.DistinctEventCount));
        Distribution(report, "SignificantDirectionChanges", corpus.Select(x => (double)x.Structural.DirectionChanges));
        Distribution(report, "SignificantSpanRatio", corpus.Select(x => x.Structural.SignificantSpanRatio));
        Distribution(report, "SignificantConcentration", corpus.Select(x => x.Structural.SignificantConcentration));
        Distribution(report, "DistributedIrregularity", corpus.Select(x => x.Structural.DistributedIrregularity));
        Distribution(report, "NormalizedMAD", corpus.Select(x => x.Structural.NormalizedMad));
        Distribution(report, "P90-P10 normalized spread", corpus.Select(x => x.Structural.NormalizedPercentileSpread));
        report.AppendLine();
        report.AppendLine("Percentiles use linear interpolation at `(n-1)×p`.");
        report.AppendLine();
    }

    private static void AppendMetricDefinitions(StringBuilder report)
    {
        report.AppendLine("## 4. Candidate metric definitions");
        report.AppendLine();
        report.AppendLine("### Candidate A — significant-delta clusters");
        report.AppendLine();
        report.AppendLine("A cluster is one or more consecutive absolute deltas >=12 ms. `ClusterRatio = ClusterCount / ceil(IntervalDeltaCount/2)`, because `ceil(n/2)` is the maximum number of separated clusters in `n` positions. Simulated minimum cluster counts: 2 and 3.");
        report.AppendLine();
        report.AppendLine("### Candidate B — distinct irregular events");
        report.AppendLine();
        report.AppendLine("Median-deviation version: an interval is atypical when `abs(interval - medianInterval) >= 12 ms`; consecutive atypical intervals form one event. This counts the single 125 ms interval as one event, the separated high intervals of a repeated alternation as several events, and the low interval blocks at both ends of Road as two events.");
        report.AppendLine();
        report.AppendLine("### Candidate C — signed-delta direction");
        report.AppendLine();
        report.AppendLine("`SignedDelta[i] = interval[i+1] - interval[i]`. Positive and negative significant deltas are counted separately. Direction changes are sign changes between successive significant signed deltas after zero/non-significant deltas are removed; the ratio denominator is `SignificantDeltaCount - 1`.");
        report.AppendLine();
        report.AppendLine("### Candidate D — spread");
        report.AppendLine();
        report.AppendLine("`SignificantSpanRatio = (lastSignificantIndex - firstSignificantIndex + 1) / IntervalDeltaCount`; zero when no significant delta exists.");
        report.AppendLine();
        report.AppendLine("### Candidate E — concentration");
        report.AppendLine();
        report.AppendLine("`SignificantConcentration = LargestSignificantClusterSize / SignificantDeltaCount`; `DistributedIrregularity = 1 - concentration`. This definition is intentionally tested as proposed, including its failure mode when every alternating delta is significant and therefore forms one large consecutive cluster.");
        report.AppendLine();
        report.AppendLine("### Candidate F — robust dispersion");
        report.AppendLine();
        report.AppendLine("`MAD = median(abs(interval - medianInterval))`; `NormalizedMAD = MAD / medianInterval`. A complementary diagnostic is `(P90 interval - P10 interval) / medianInterval`. Neither is used as a production rule in this forensic.");
        report.AppendLine();
    }

    private static void AppendReferenceTable(StringBuilder report, IReadOnlyList<RunView> references)
    {
        report.AppendLine("## 5. Reference cases side by side");
        report.AppendLine();
        report.AppendLine("|Case|Intervals|Abs deltas|CV|Sig count/ratio|Clusters/ratio|Distinct events|Signed deltas|+/- sig|Direction changes/ratio|Span ratio|Largest cluster|Concentration|Distributed|Median|MAD|Norm MAD|P90-P10 norm|Current score|Legacy|Production|");
        report.AppendLine("|---|---|---|---:|---:|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|");
        foreach (RunView run in references)
        {
            StructuralMetrics m = run.Structural;
            report.AppendLine(FormattableString.Invariant($"|{run.Label}|{Numbers(run.Sequence.Intervals)}|{Numbers(run.Sequence.IntervalDeltas)}|{run.Sequence.CoefficientOfVariation:F4}|{run.Sequence.SignificantDeltaCount}/{run.Sequence.SignificantDeltaRatio:F4}|{m.ClusterCount}/{m.ClusterRatio:F4}|{m.DistinctEventCount}|{Numbers(m.SignedDeltas)}|{m.PositiveSignificantCount}/{m.NegativeSignificantCount}|{m.DirectionChanges}/{m.DirectionChangeRatio:F4}|{m.SignificantSpanRatio:F4}|{m.LargestClusterSize}|{m.SignificantConcentration:F4}|{m.DistributedIrregularity:F4}|{m.MedianInterval:F2}|{m.Mad:F2}|{m.NormalizedMad:F4}|{m.NormalizedPercentileSpread:F4}|{run.Sequence.IrregularityScore:F4}|{CurrentRule(run)}|{run.Sequence.Qualified}|"));
        }
        report.AppendLine();
    }

    private static void AppendCandidateSections(StringBuilder report, IReadOnlyList<RunView> corpus,
        IReadOnlyList<RunView> references)
    {
        report.AppendLine("## 6. Candidate A results — clusters");
        report.AppendLine();
        MetricSummary(report, corpus, references, x => x.Structural.ClusterCount >= 2, "MinimumSignificantClusters >= 2");
        MetricSummary(report, corpus, references, x => x.Structural.ClusterCount >= 3, "MinimumSignificantClusters >= 3");
        report.AppendLine("A rejects the isolated spike, but also rejects the repeated synthetic patterns because their significant deltas are consecutive and collapse into one cluster.");
        report.AppendLine();

        report.AppendLine("## 7. Candidate B results — median-atypical events");
        report.AppendLine();
        MetricSummary(report, corpus, references, x => x.Structural.DistinctEventCount >= 2, "DistinctIrregularEventCount >= 2");
        report.AppendLine("This is the only single structural metric tested here that separates every required synthetic negative/positive while retaining a plausible Road signal.");
        report.AppendLine();

        report.AppendLine("## 8. Candidate C results — signed directions");
        report.AppendLine();
        MetricSummary(report, corpus, references, x => x.Structural.DirectionChanges >= 2, "SignificantDirectionChanges >= 2");
        report.AppendLine("Direction-change count rejects a single up/down spike and recognizes repeated alternation. It rejects Road because Road has only one change of sign between its two separated regime transitions.");
        report.AppendLine();

        report.AppendLine("## 9. Candidate D results — significant span");
        report.AppendLine();
        MetricSummary(report, corpus, references, x => x.Structural.SignificantSpanRatio >= 0.50, "SignificantSpanRatio >= 0.50");
        report.AppendLine("Span identifies whether transitions cover the run, but cannot by itself reject a spike on short runs or prove that multiple independent events exist.");
        report.AppendLine();

        report.AppendLine("## 10. Candidate E results — concentration");
        report.AppendLine();
        MetricSummary(report, corpus, references, x => x.Structural.DistributedIrregularity >= 0.50, "DistributedIrregularity >= 0.50");
        report.AppendLine("It rejects the isolated spike, but gives zero distributed signal to fully alternating synthetic runs because all significant deltas form one consecutive cluster. It is unsuitable as a primary condition under this cluster definition.");
        report.AppendLine();

        report.AppendLine("## 11. Candidate F results — robust dispersion");
        report.AppendLine();
        MetricSummary(report, corpus, references, x => x.Structural.NormalizedMad >= 0.10, "NormalizedMAD >= 0.10 diagnostic");
        MetricSummary(report, corpus, references, x => x.Structural.NormalizedPercentileSpread >= 0.30, "Normalized P90-P10 spread >= 0.30 diagnostic");
        report.AppendLine("MAD is robust to the single spike, but also remains small on bimodal alternations when one interval family holds the median majority. Percentile spread detects both spike and alternation, so it is not independently spike-resistant.");
        report.AppendLine();

        report.AppendLine("## Appendix A. Exhaustive real-corpus metric export");
        report.AppendLine();
        report.AppendLine("|Map|Start–end|Objects|Intervals|Abs deltas|Mean|Stddev|CV|Sig count|Sig ratio|Avg delta|Max delta|Current score|Clusters|Cluster ratio|Distinct events|Signed deltas|+ sig|- sig|Direction changes|Direction ratio|Span ratio|Largest cluster|Concentration|Distributed|Median|MAD|Norm MAD|P90-P10 norm|");
        report.AppendLine("|---|---|---:|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (RunView run in corpus.OrderBy(x => x.Label).ThenBy(x => x.Sequence.StartTime))
        {
            IrregularStreamPatternSequence s = run.Sequence;
            StructuralMetrics m = run.Structural;
            report.AppendLine(FormattableString.Invariant($"|{Cell(run.Label)}|{Time(s.StartTime)}–{Time(s.EndTime)}|{s.ObjectCount}|{Numbers(s.Intervals)}|{Numbers(s.IntervalDeltas)}|{s.AverageInterval:F2}|{s.StandardDeviation:F2}|{s.CoefficientOfVariation:F4}|{s.SignificantDeltaCount}|{s.SignificantDeltaRatio:F4}|{s.AverageIntervalDelta:F2}|{s.MaximumIntervalDelta:F2}|{s.IrregularityScore:F4}|{m.ClusterCount}|{m.ClusterRatio:F4}|{m.DistinctEventCount}|{Numbers(m.SignedDeltas)}|{m.PositiveSignificantCount}|{m.NegativeSignificantCount}|{m.DirectionChanges}|{m.DirectionChangeRatio:F4}|{m.SignificantSpanRatio:F4}|{m.LargestClusterSize}|{m.SignificantConcentration:F4}|{m.DistributedIrregularity:F4}|{m.MedianInterval:F2}|{m.Mad:F2}|{m.NormalizedMad:F4}|{m.NormalizedPercentileSpread:F4}|"));
        }
        report.AppendLine();
    }

    private static void AppendRuleSimulations(StringBuilder report, IReadOnlyList<RunView> corpus,
        IReadOnlyList<RunView> references)
    {
        report.AppendLine("## 12. Read-only rule simulations");
        report.AppendLine();
        report.AppendLine("|Rule|Definition|Corpus positives / 190|Maps|Regular|LowNoise|SingleSpike|RepeatedIrregular|TemporalAlternation|Road|");
        report.AppendLine("|---|---|---:|---:|---|---|---|---|---|---|");
        foreach (Rule rule in Rules())
        {
            RunView[] positives = corpus.Where(rule.Predicate).ToArray();
            report.AppendLine($"|{rule.Name}|{rule.Definition}|{positives.Length}|{positives.Select(x => x.Label).Distinct().Count()}|{Pass(rule, references, "Regular")}|{Pass(rule, references, "LowNoise")}|{Pass(rule, references, "SingleSpike")}|{Pass(rule, references, "RepeatedIrregular")}|{Pass(rule, references, "TemporalAlternation")}|{Pass(rule, references, "RoadOfResistanceCandidate")}|" );
        }
        report.AppendLine();
        report.AppendLine("## 13. Top real-corpus positives by rule");
        report.AppendLine();
        foreach (Rule rule in Rules())
        {
            report.AppendLine($"### {rule.Name} — top 20 real-corpus positives");
            report.AppendLine();
            RunView[] positives = corpus.Where(rule.Predicate)
                .OrderByDescending(RankingScore).Take(20).ToArray();
            if (positives.Length == 0)
            {
                report.AppendLine("None.");
            }
            else
            {
                report.AppendLine("|Map|Run|CV|Sig ratio|Clusters|Events|Dir changes|Span|Distributed|Norm MAD|P90-P10 norm|Diagnostic rank|");
                report.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
                foreach (RunView run in positives)
                    report.AppendLine(FormattableString.Invariant($"|{Cell(run.Label)}|{Time(run.Sequence.StartTime)}–{Time(run.Sequence.EndTime)}|{run.Sequence.CoefficientOfVariation:F4}|{run.Sequence.SignificantDeltaRatio:F4}|{run.Structural.ClusterCount}|{run.Structural.DistinctEventCount}|{run.Structural.DirectionChanges}|{run.Structural.SignificantSpanRatio:F4}|{run.Structural.DistributedIrregularity:F4}|{run.Structural.NormalizedMad:F4}|{run.Structural.NormalizedPercentileSpread:F4}|{RankingScore(run):F4}|"));
            }
            report.AppendLine();
        }
    }

    private static void AppendRoadAnalysis(StringBuilder report, IReadOnlyList<RunView> references)
    {
        RunView road = Reference(references, "RoadOfResistanceCandidate");
        report.AppendLine("## 14. Road of Resistance diagnostic");
        report.AppendLine();
        report.AppendLine(FormattableString.Invariant($"Intervals `[67,68,67,102,101,102,101,101,102,67,68]` form three sustained regimes: a short low block, a central high block, then a low block. The two significant deltas are separated (indices {string.Join(" and ", road.Structural.SignificantIndices)}), yielding clusters={road.Structural.ClusterCount}, span={road.Structural.SignificantSpanRatio:F4}, median-atypical events={road.Structural.DistinctEventCount}, but only one signed direction change. Candidate B therefore treats Road as structurally repeated; Candidate C intentionally does not. Road remains diagnostic evidence, not ground truth."));
        report.AppendLine();
    }

    private static void AppendErrorsAndRecommendation(StringBuilder report, IReadOnlyList<RunView> corpus,
        IReadOnlyList<RunView> references)
    {
        Rule recommended = Rules().Single(x => x.Name == "Rule B");
        RunView[] positives = corpus.Where(recommended.Predicate).OrderByDescending(RankingScore).ToArray();
        report.AppendLine("## 15. Probable false positives");
        report.AppendLine();
        report.AppendLine("There is no labeled real-map ground truth in this 18-map corpus. Consequently, corpus items cannot be declared true or false automatically; the following are review queues, not labels.");
        report.AppendLine();
        report.AppendLine("- Probable-false-positive review: Rule B positives with exactly two events or a single dominant atypical block should be inspected first: " + List(positives.Where(x => x.Structural.DistinctEventCount == 2).Select(Describe)) + ".");
        report.AppendLine();
        report.AppendLine("## 16. Probable false negatives");
        report.AppendLine();
        report.AppendLine("- Probable-false-negative review: high-CV runs rejected only because they contain one median-atypical event: " + List(corpus.Where(x => x.Sequence.CoefficientOfVariation >= CvThreshold && x.Structural.DistinctEventCount == 1).OrderByDescending(RankingScore).Take(10).Select(Describe)) + ".");
        report.AppendLine();
        report.AppendLine("## 17. Final recommendation");
        report.AppendLine();
        report.AppendLine("**REPLACE_SIGNIFICANT_DELTA_RATIO**");
        report.AppendLine();
        report.AppendLine("Recommended formulation for a future implementation, subject to manual review of the corpus positives:");
        report.AppendLine();
        report.AppendLine("```text");
        report.AppendLine("ObjectCount >= 6");
        report.AppendLine("AND CoefficientOfVariation >= 0.10");
        report.AppendLine("AND DistinctIrregularEventCount >= 2");
        report.AppendLine();
        report.AppendLine("medianInterval = median(Intervals)");
        report.AppendLine("atypical[i] = abs(Intervals[i] - medianInterval) >= 12 ms");
        report.AppendLine("DistinctIrregularEventCount = number of consecutive atypical-interval regions");
        report.AppendLine("```");
        report.AppendLine();
        report.AppendLine($"Initial thresholds: preserve minimum objects **6**, CV **0.10**, and reuse **12 ms** as the median-deviation threshold; replace the 0.30 transition ratio with **at least 2 distinct events**. On the required references this rejects Regular, LowNoise and SingleSpike; accepts RepeatedIrregular and TemporalAlternation; and gives Road a plausible positive signal. On the real corpus it qualifies **{positives.Length}/190 runs across {positives.Select(x => x.Label).Distinct().Count()} map(s)**.");
        report.AppendLine();
        report.AppendLine("Tradeoffs: the median-majority definition detects separated interval regimes but may miss a single sustained atypical block, and short runs provide coarse event counts. Candidate C direction changes is a useful secondary diagnostic for oscillatory irregularity. Rule F (normalized P90-P10 spread >=0.30) also passes the required reference cases but qualifies five Road runs and remains amplitude-sensitive to more extreme isolated spikes; it is therefore retained as a secondary review signal. Cluster concentration and MAD should not be primary gates based on the demonstrated synthetic failures.");
        report.AppendLine();
        report.AppendLine("The forensic runner itself makes no production change; it documents the implemented result and alternative diagnostics.");
    }

    private static void AppendPostChangeComparison(StringBuilder report, IReadOnlyList<RunView> corpus,
        IReadOnlyList<RunView> references)
    {
        RunView[] before = corpus.Where(CurrentRule).ToArray();
        RunView[] after = corpus.Where(run => run.Sequence.Qualified).ToArray();
        Assert.Empty(before);
        Assert.Single(after);

        report.AppendLine();
        report.AppendLine("## 18. Post-change mini-forensic");
        report.AppendLine();
        report.AppendLine($"- Legacy qualification: **{before.Length}/190** runs, maps: {List(before.Select(x => x.Label).Distinct())}.");
        report.AppendLine($"- Structural production qualification: **{after.Length}/190** runs, maps: {List(after.Select(x => x.Label).Distinct())}.");
        report.AppendLine();
        report.AppendLine("|Case|Legacy|Structural production|Distinct events|Reason|");
        report.AppendLine("|---|---|---|---:|---|");
        foreach (string name in new[] { "SingleSpike", "RepeatedIrregular", "TemporalAlternation", "RoadOfResistanceCandidate" })
        {
            RunView run = Reference(references, name);
            report.AppendLine($"|{name}|{CurrentRule(run)}|{run.Sequence.Qualified}|{run.Sequence.DistinctIrregularEventCount}|{run.Sequence.Reason}|");
        }
        report.AppendLine();
        report.AppendLine("Observed delta matches the structural forensic expectation exactly: 0 → 1 real-corpus positives, with only Road of Resistance 00:25.546–00:26.492 newly qualified.");
    }

    private static IReadOnlyList<Rule> Rules() =>
    [
        new("Current", "CV>=0.10 AND sigRatio>=0.30", CurrentRule),
        new("Rule A", "CV>=0.10 AND sigRatio>=0.20 AND clusters>=2",
            x => Cv(x) && x.Sequence.SignificantDeltaRatio >= CandidateSignificantRatio && x.Structural.ClusterCount >= 2),
        new("Rule B", "CV>=0.10 AND distinctEvents>=2",
            x => Cv(x) && x.Structural.DistinctEventCount >= 2),
        new("Rule C", "CV>=0.10 AND sigRatio>=0.20 AND directionChanges>=2",
            x => Cv(x) && x.Sequence.SignificantDeltaRatio >= CandidateSignificantRatio && x.Structural.DirectionChanges >= 2),
        new("Rule D", "CV>=0.10 AND clusters>=2 AND span>=0.50",
            x => Cv(x) && x.Structural.ClusterCount >= 2 && x.Structural.SignificantSpanRatio >= 0.50),
        new("Rule E", "CV>=0.10 AND events>=2 AND (span>=0.50 OR dirChanges>=2)",
            x => Cv(x) && x.Structural.DistinctEventCount >= 2
                && (x.Structural.SignificantSpanRatio >= 0.50 || x.Structural.DirectionChanges >= 2)),
        new("Rule F", "CV>=0.10 AND normalized(P90-P10)>=0.30",
            x => Cv(x) && x.Structural.NormalizedPercentileSpread >= 0.30)
    ];

    private static bool CurrentRule(RunView run) => run.Sequence.ObjectCount >= 6
        && run.Sequence.CoefficientOfVariation >= CvThreshold
        && run.Sequence.SignificantDeltaRatio >= 0.30;
    private static bool Cv(RunView run) => run.Sequence.CoefficientOfVariation >= CvThreshold;
    private static bool Pass(Rule rule, IReadOnlyList<RunView> references, string name) =>
        rule.Predicate(Reference(references, name));
    private static RunView Reference(IReadOnlyList<RunView> references, string name) =>
        references.Single(x => x.Label == name);

    private static void MetricSummary(StringBuilder report, IReadOnlyList<RunView> corpus,
        IReadOnlyList<RunView> references, Func<RunView, bool> predicate, string label)
    {
        RunView[] hits = corpus.Where(predicate).ToArray();
        report.AppendLine($"- **{label}**: corpus {hits.Length}/190 across {hits.Select(x => x.Label).Distinct().Count()} map(s); Regular={predicate(Reference(references, "Regular"))}, LowNoise={predicate(Reference(references, "LowNoise"))}, SingleSpike={predicate(Reference(references, "SingleSpike"))}, RepeatedIrregular={predicate(Reference(references, "RepeatedIrregular"))}, TemporalAlternation={predicate(Reference(references, "TemporalAlternation"))}, Road={predicate(Reference(references, "RoadOfResistanceCandidate"))}.");
    }

    private static double RankingScore(RunView run) =>
        run.Sequence.CoefficientOfVariation
        + run.Structural.DistinctEventCount / 10.0
        + run.Structural.SignificantSpanRatio / 10.0
        + run.Structural.DirectionChanges / 20.0;

    private static void Distribution(StringBuilder report, string name, IEnumerable<double> source)
    {
        double[] values = source.OrderBy(x => x).ToArray();
        report.AppendLine(FormattableString.Invariant($"- **{name}**: min={Percentile(values, 0):F4}, P25={Percentile(values, .25):F4}, median={Percentile(values, .5):F4}, P75={Percentile(values, .75):F4}, P90={Percentile(values, .9):F4}, P95={Percentile(values, .95):F4}, max={Percentile(values, 1):F4}."));
    }

    private static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        double position = (sorted.Count - 1) * p;
        int lower = (int)Math.Floor(position), upper = (int)Math.Ceiling(position);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }

    private static double Median(IEnumerable<double> source)
    {
        double[] values = source.OrderBy(x => x).ToArray();
        if (values.Length == 0) return 0;
        int middle = values.Length / 2;
        return values.Length % 2 == 0 ? (values[middle - 1] + values[middle]) / 2 : values[middle];
    }

    private static string Describe(RunView run) =>
        $"{run.Label} {Time(run.Sequence.StartTime)}–{Time(run.Sequence.EndTime)} (CV={run.Sequence.CoefficientOfVariation:F4}, events={run.Structural.DistinctEventCount})";
    private static string List(IEnumerable<string> source)
    {
        string[] values = source.ToArray();
        return values.Length == 0 ? "None" : string.Join("; ", values);
    }
    private static string Numbers(IEnumerable<double> values) => string.Join(",", values.Select(x => x.ToString("F2", CultureInfo.InvariantCulture)));
    private static string Time(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds).ToString(@"mm\:ss\.fff");
    private static string Cell(string value) => value.Replace("|", "\\|");

    private sealed record CorpusMap(string Label, Beatmap Map, IrregularStreamPatternAnalysis Analysis);
    private sealed record RunView(string Label, IrregularStreamPatternSequence Sequence, StructuralMetrics Structural);
    private sealed record Rule(string Name, string Definition, Func<RunView, bool> Predicate);

    private sealed record StructuralMetrics(
        IReadOnlyList<double> SignedDeltas,
        IReadOnlyList<int> SignificantIndices,
        int ClusterCount,
        int LargestClusterSize,
        double ClusterRatio,
        int DistinctEventCount,
        int PositiveSignificantCount,
        int NegativeSignificantCount,
        int DirectionChanges,
        double DirectionChangeRatio,
        double SignificantSpanRatio,
        double SignificantConcentration,
        double DistributedIrregularity,
        double MedianInterval,
        double Mad,
        double NormalizedMad,
        double NormalizedPercentileSpread)
    {
        public static StructuralMetrics Calculate(IReadOnlyList<double> intervals)
        {
            double[] signed = Enumerable.Range(0, Math.Max(0, intervals.Count - 1))
                .Select(index => intervals[index + 1] - intervals[index]).ToArray();
            int[] significant = signed.Select((value, index) => (value, index))
                .Where(x => Math.Abs(x.value) >= SignificantDeltaThreshold)
                .Select(x => x.index).ToArray();
            int[][] clusters = ConsecutiveClusters(significant);
            int clusterCount = clusters.Length;
            int largestCluster = clusters.Length == 0 ? 0 : clusters.Max(x => x.Length);
            int maximumSeparatedClusters = (int)Math.Ceiling(signed.Length / 2.0);
            double clusterRatio = maximumSeparatedClusters == 0 ? 0 : (double)clusterCount / maximumSeparatedClusters;

            double median = Median(intervals);
            int[] atypical = intervals.Select((value, index) => (value, index))
                .Where(x => Math.Abs(x.value - median) >= AtypicalMedianDistance)
                .Select(x => x.index).ToArray();
            int eventCount = ConsecutiveClusters(atypical).Length;

            double[] significantSigned = signed.Where(x => Math.Abs(x) >= SignificantDeltaThreshold).ToArray();
            int positive = significantSigned.Count(x => x > 0);
            int negative = significantSigned.Count(x => x < 0);
            int directionChanges = significantSigned.Zip(significantSigned.Skip(1))
                .Count(pair => Math.Sign(pair.First) != Math.Sign(pair.Second));
            double directionRatio = significantSigned.Length <= 1
                ? 0 : (double)directionChanges / (significantSigned.Length - 1);
            double spanRatio = significant.Length == 0 || signed.Length == 0
                ? 0 : (double)(significant[^1] - significant[0] + 1) / signed.Length;
            double concentration = significant.Length == 0 ? 0 : (double)largestCluster / significant.Length;
            double distributed = significant.Length == 0 ? 0 : 1 - concentration;

            double mad = Median(intervals.Select(x => Math.Abs(x - median)));
            double normalizedMad = median <= 0 ? 0 : mad / median;
            double[] sortedIntervals = intervals.OrderBy(x => x).ToArray();
            double percentileSpread = median <= 0 ? 0
                : (Percentile(sortedIntervals, .90) - Percentile(sortedIntervals, .10)) / median;
            return new(signed, significant, clusterCount, largestCluster, clusterRatio, eventCount,
                positive, negative, directionChanges, directionRatio, spanRatio, concentration,
                distributed, median, mad, normalizedMad, percentileSpread);
        }

        private static int[][] ConsecutiveClusters(IReadOnlyList<int> indices)
        {
            if (indices.Count == 0) return [];
            var clusters = new List<List<int>> { new() { indices[0] } };
            for (int index = 1; index < indices.Count; index++)
            {
                if (indices[index] == indices[index - 1] + 1)
                    clusters[^1].Add(indices[index]);
                else
                    clusters.Add(new List<int> { indices[index] });
            }
            return clusters.Select(x => x.ToArray()).ToArray();
        }
    }
}
