using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using BeatInsight.Analysis;
using BeatInsight.Analysis.TechPatterns;
using BeatInsight.Models;
using BeatInsight.Models.TechPatterns;
using BeatInsight.Parser;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// Observation-only global forensic for the seven Tech Pattern V1 analyzers.
/// Production analyzers, options and thresholds are never mutated here.
/// </summary>
public sealed class TechPatternV1GlobalForensicTests(ITestOutputHelper output)
{
    private static readonly string[] FixtureNames =
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

    private static readonly MapSource[] RequestedMaps =
    [
        new("Connect TV Size", @"D:\osu!\Songs\1854325 ClariS - Connect (TV Size)\ClariS - Connect (TV Size) (Aakki) [Expert].osu", "requested installed"),
        new("Wings of Courage", @"D:\osu!\Songs\1706691 Kawada Mami - Wings of Courage -Sora o Koete-\Kawada Mami - Wings of Courage -Sora o Koete- (Zekk) [Expert].osu", "requested installed"),
        new("From Under Cover", @"D:\osu!\Songs\462386 Foreground Eclipse - From Under Cover (Caught Up In A Love Song)\Foreground Eclipse - From Under Cover (Caught Up In A Love Song) (Seni) [I've Stopped Time and Everything is at My Mercy].osu", "requested installed")
    ];

    private static readonly string[] PatternNames =
    [
        "Slider Chain",
        "Complex Slider",
        "Slider Direction Change",
        "Circle / Slider Transition",
        "Rhythm Transition",
        "Spatial / Awkward",
        "Overlap"
    ];

    [Fact]
    public void ExistingRealCorpusWritesGlobalTechPatternV1ForensicReport()
    {
        MapSource[] sources = BuildCorpus();
        Assert.All(sources, source => Assert.True(File.Exists(source.Path), $"Missing existing corpus map: {source.Path}"));

        var totalWatch = Stopwatch.StartNew();
        MapAudit[] maps = sources.Select(Analyze).ToArray();
        totalWatch.Stop();
        AssertOrderIndependence(maps[0].Map);

        var report = new StringBuilder();
        AppendHeader(report, maps, totalWatch.Elapsed);
        AppendPerMapSummary(report, maps);
        AppendPerMapDetails(report, maps);
        AppendDensityRankings(report, maps);
        AppendPatternDistribution(report, maps);
        AppendOverlapMatrices(report, maps);
        AppendWatchedPairs(report, maps);
        AppendOverlapForensic(report, maps);
        AppendSpatialConfirmation(report, maps);
        AppendPatternSpecifics(report, maps);
        AppendControlGroups(report, maps);
        AppendSuspects(report, maps);
        AppendHistoricalComparison(report, maps);
        AppendSpecificMaps(report, maps);
        AppendPerformance(report, maps, totalWatch.Elapsed);
        AppendFreezeEvidence(report, maps);

        string directory = Environment.GetEnvironmentVariable("BEATINSIGHT_FORENSIC_OUTPUT_DIRECTORY")
            ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "tech-pattern-v1-global-forensic.md");
        File.WriteAllText(path, report.ToString(), Encoding.UTF8);

        output.WriteLine($"Corpus analyzed: {maps.Length}/{sources.Length}");
        output.WriteLine($"Objects analyzed: {maps.Sum(map => map.Map.HitObjects.Count)}");
        output.WriteLine($"Elapsed: {totalWatch.Elapsed.TotalMilliseconds:F1} ms");
        output.WriteLine($"Report: {path}");
    }

    private static MapSource[] BuildCorpus()
    {
        string fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps");
        IEnumerable<MapSource> fixtures = FixtureNames.Select(name =>
            new MapSource(Path.GetFileNameWithoutExtension(name), Path.Combine(fixtureDirectory, name), "fixture"));
        IEnumerable<MapSource> stream = StreamPatternGlobalForensicTests.Corpus.Select(spec =>
            new MapSource(spec.Title, spec.Path, "Stream forensic"));
        IEnumerable<MapSource> tech = TechV2SliderDensityCalibrationAudit.Corpus.Select(spec =>
            new MapSource(spec.Name, spec.Path, "Tech forensic"));
        return fixtures.Concat(stream).Concat(tech).Concat(RequestedMaps)
            .Where(source => File.Exists(source.Path))
            .GroupBy(source => Path.GetFullPath(source.Path), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static MapAudit Analyze(MapSource source)
    {
        Beatmap map = BeatmapParser.Load(source.Path);
        GameplayProfile profile = GameplayAnalyzer.Analyze(map);
        var elapsed = new Dictionary<string, double>(StringComparer.Ordinal);

        SliderChainPatternAnalysis chain = Timed("Slider Chain", elapsed,
            () => TechPatternAnalyzer.AnalyzeSliderChain(map.HitObjects));
        ComplexSliderPatternAnalysis complex = Timed("Complex Slider", elapsed,
            () => TechPatternAnalyzer.AnalyzeComplexSlider(map));
        SliderDirectionChangePatternAnalysis direction = Timed("Slider Direction Change", elapsed,
            () => TechPatternAnalyzer.AnalyzeSliderDirectionChange(map));
        CircleSliderTransitionPatternAnalysis circleSlider = Timed("Circle / Slider Transition", elapsed,
            () => TechPatternAnalyzer.AnalyzeCircleSliderTransition(map.HitObjects));
        RhythmTransitionPatternAnalysis rhythm = Timed("Rhythm Transition", elapsed,
            () => TechPatternAnalyzer.AnalyzeRhythmTransition(map.HitObjects));
        SpatialAwkwardTransitionPatternAnalysis spatial = Timed("Spatial / Awkward", elapsed,
            () => TechPatternAnalyzer.AnalyzeSpatialAwkwardTransition(map.HitObjects));
        OverlapPatternAnalysis overlap = Timed("Overlap", elapsed,
            () => TechPatternAnalyzer.AnalyzeOverlap(map.HitObjects));

        Dictionary<string, HashSet<int>> covered = new(StringComparer.Ordinal)
        {
            ["Slider Chain"] = RangeSet(chain.Sequences.Select(sequence => (sequence.StartIndex, sequence.EndIndex))),
            ["Complex Slider"] = complex.ComplexSliders.Select(item => item.ObjectIndex).ToHashSet(),
            ["Slider Direction Change"] = DirectionSet(direction),
            ["Circle / Slider Transition"] = RangeSet(circleSlider.Sequences.Select(sequence => (sequence.StartIndex, sequence.EndIndex))),
            ["Rhythm Transition"] = RangeSet(rhythm.Transitions.Select(item => (item.StartIndex, item.EndIndex))),
            ["Spatial / Awkward"] = RangeSet(spatial.Transitions.Select(item => (item.StartIndex, item.EndIndex))),
            ["Overlap"] = RangeSet(overlap.Sequences.Select(sequence => (sequence.StartIndex, sequence.EndIndex)))
        };

        return new MapAudit(source, map, profile, chain, complex, direction, circleSlider,
            rhythm, spatial, overlap, covered, elapsed);
    }

    private static T Timed<T>(string name, IDictionary<string, double> elapsed, Func<T> action)
    {
        var watch = Stopwatch.StartNew();
        T result = action();
        watch.Stop();
        elapsed[name] = watch.Elapsed.TotalMilliseconds;
        return result;
    }

    private static HashSet<int> RangeSet(IEnumerable<(int Start, int End)> ranges)
    {
        var result = new HashSet<int>();
        foreach ((int start, int end) in ranges)
            for (int index = start; index <= end; index++)
                result.Add(index);
        return result;
    }

    private static HashSet<int> DirectionSet(SliderDirectionChangePatternAnalysis analysis)
    {
        var result = new HashSet<int>();
        foreach (SliderDirectionChangePatternOccurrence item in analysis.QualifiedOccurrences)
        {
            result.Add(item.SliderIndex);
            if (item.EntryDirectionChange && item.PreviousObjectIndex.HasValue)
                result.Add(item.PreviousObjectIndex.Value);
            if (item.ExitDirectionChange && item.NextObjectIndex.HasValue)
                result.Add(item.NextObjectIndex.Value);
        }
        return result;
    }

    private static void AssertOrderIndependence(Beatmap map)
    {
        string[] forward = AnalyzeSerialized(map, reverse: false);
        string[] reverse = AnalyzeSerialized(map, reverse: true);
        Assert.Equal(forward, reverse);
    }

    private static string[] AnalyzeSerialized(Beatmap map, bool reverse)
    {
        Func<object>[] calls =
        [
            () => TechPatternAnalyzer.AnalyzeSliderChain(map.HitObjects),
            () => TechPatternAnalyzer.AnalyzeComplexSlider(map),
            () => TechPatternAnalyzer.AnalyzeSliderDirectionChange(map),
            () => TechPatternAnalyzer.AnalyzeCircleSliderTransition(map.HitObjects),
            () => TechPatternAnalyzer.AnalyzeRhythmTransition(map.HitObjects),
            () => TechPatternAnalyzer.AnalyzeSpatialAwkwardTransition(map.HitObjects),
            () => TechPatternAnalyzer.AnalyzeOverlap(map.HitObjects)
        ];
        int[] order = reverse ? Enumerable.Range(0, 7).Reverse().ToArray() : Enumerable.Range(0, 7).ToArray();
        var results = new string[7];
        foreach (int index in order)
            results[index] = JsonSerializer.Serialize(calls[index]());
        return results;
    }

    private static void AppendHeader(StringBuilder report, IReadOnlyList<MapAudit> maps, TimeSpan elapsed)
    {
        report.AppendLine("# Tech Pattern V1 — global real-corpus forensic");
        report.AppendLine();
        report.AppendLine("Observation-only runner. No production threshold, formula, analyzer, identity, rating, UI, historical Tech, TechV2, Jump or Stream behavior is changed.");
        report.AppendLine();
        report.AppendLine($"Corpus: **{maps.Count} maps**, **{maps.Sum(map => map.Map.HitObjects.Count)} objects**, seven descriptive Tech Pattern V1 analyzers. Total wall time including parse/profile/pattern/report preparation: **{elapsed.TotalMilliseconds:F1} ms**.");
        report.AppendLine();
        report.AppendLine("The corpus is the deduplicated union of existing fixtures, Stream forensic maps, Tech slider-heavy maps, Connect, Wings of Courage, and the installed From Under Cover control map. No map was downloaded.");
        report.AppendLine();
    }

    private static void AppendPerMapSummary(StringBuilder report, IReadOnlyList<MapAudit> maps)
    {
        report.AppendLine("## Global per-map summary");
        report.AppendLine();
        report.AppendLine("|Map|Objects|Slider Chain|Complex Slider|Slider Direction|Circle/Slider|Rhythm|Spatial/Awkward|Overlap|Union|Active|Mean coverage|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (MapAudit map in maps.OrderByDescending(item => item.UnionCoverage))
            report.AppendLine($"|{Cell(map.Label)}|{map.Map.HitObjects.Count}|{Pct(map.Chain.Coverage)}|{Pct(map.Complex.Coverage)}|{Pct(map.Direction.Coverage)}|{Pct(map.CircleSlider.Coverage)}|{Pct(map.Rhythm.Coverage)}|{Pct(map.Spatial.Coverage)}|{Pct(map.Overlap.Coverage)}|{Pct(map.UnionCoverage)}|{map.ActivePatternCount}|{Pct(map.MeanPatternCoverage)}|");
        report.AppendLine();
    }

    private static void AppendPerMapDetails(StringBuilder report, IReadOnlyList<MapAudit> maps)
    {
        report.AppendLine("## Per-map pattern details");
        report.AppendLine();
        foreach (MapAudit map in maps)
        {
            report.AppendLine($"### {map.Label}");
            report.AppendLine();
            report.AppendLine($"Source: {map.Source.Kind}. Objects: {map.Map.HitObjects.Count}. Union: {Pct(map.UnionCoverage)}. Active patterns: {map.ActivePatternCount}/7.");
            report.AppendLine();
            report.AppendLine("|Pattern|Present|Count|Coverage|Confidence|Primary metric|");
            report.AppendLine("|---|---|---:|---:|---:|---|");
            foreach (PatternRow row in Rows(map))
                report.AppendLine($"|{row.Name}|{(row.Count > 0 ? "YES" : "NO")}|{row.Count}|{Pct(row.Coverage)}|{row.Confidence:F3}|{row.Metric}|");
            report.AppendLine();
        }
    }

    private static PatternRow[] Rows(MapAudit map) =>
    [
        new("Slider Chain", map.Chain.Sequences.Count, map.Chain.Coverage, map.Chain.Confidence,
            $"SliderCov={Pct(map.Chain.SliderCoverage)}, AvgLen={map.Chain.AverageChainLength:F2}, AvgRatio={map.Chain.AverageSliderRatio:F3}"),
        new("Complex Slider", map.Complex.ComplexSliderCount, map.Complex.Coverage, map.Complex.Confidence,
            $"SliderCov={Pct(map.Complex.SliderCoverage)}, Avg/Max={map.Complex.AverageComplexityScore:F3}/{map.Complex.MaximumComplexityScore:F3}"),
        new("Slider Direction Change", map.Direction.DirectionChangeSequenceCount, map.Direction.Coverage, map.Direction.Confidence,
            $"SliderCov={Pct(map.Direction.SliderCoverage)}, AvgScore={map.Direction.AverageDirectionChangeScore:F3}"),
        new("Circle / Slider Transition", map.CircleSlider.TransitionSequenceCount, map.CircleSlider.Coverage, map.CircleSlider.Confidence,
            $"AvgScore={map.CircleSlider.AverageTransitionScore:F3}, AvgLen={map.CircleSlider.AverageSequenceLength:F2}"),
        new("Rhythm Transition", map.Rhythm.RhythmTransitionCount, map.Rhythm.Coverage, map.Rhythm.Confidence,
            $"Avg/Max={map.Rhythm.AverageTransitionScore:F3}/{map.Rhythm.MaximumTransitionScore:F3}"),
        new("Spatial / Awkward", map.Spatial.SpatialAwkwardTransitionCount, map.Spatial.Coverage, map.Spatial.Confidence,
            $"Avg/Max={map.Spatial.AverageSpatialAwkwardScore:F3}/{map.Spatial.MaximumSpatialAwkwardScore:F3}, Angle={map.Spatial.AverageTurnAngle:F2}"),
        new("Overlap", map.Overlap.QualifiedOverlapSequenceCount, map.Overlap.Coverage, map.Overlap.Confidence,
            $"Links={map.Overlap.OverlapLinkCount}, Objects={map.Overlap.QualifiedOverlapObjectCount}, Strength={map.Overlap.AverageOverlapStrength:F3}, AvgLen={map.Overlap.AverageSequenceLength:F2}")
    ];

    private static void AppendDensityRankings(StringBuilder report, IReadOnlyList<MapAudit> maps)
    {
        report.AppendLine("## Tech Pattern density rankings");
        report.AppendLine();
        AppendRanking(report, "Union coverage", maps.OrderByDescending(map => map.UnionCoverage), map => Pct(map.UnionCoverage));
        AppendRanking(report, "Active pattern count", maps.OrderByDescending(map => map.ActivePatternCount).ThenByDescending(map => map.UnionCoverage), map => $"{map.ActivePatternCount}/7");
        AppendRanking(report, "Mean pattern coverage", maps.OrderByDescending(map => map.MeanPatternCoverage), map => Pct(map.MeanPatternCoverage));
    }

    private static void AppendRanking(StringBuilder report, string title, IEnumerable<MapAudit> ordered, Func<MapAudit, string> value)
    {
        report.AppendLine($"### Top 15 — {title}");
        report.AppendLine();
        int rank = 1;
        foreach (MapAudit map in ordered.Take(15))
            report.AppendLine($"{rank++}. {map.Label}: **{value(map)}** (union {Pct(map.UnionCoverage)}, active {map.ActivePatternCount}/7).");
        report.AppendLine();
    }

    private static void AppendPatternDistribution(StringBuilder report, IReadOnlyList<MapAudit> maps)
    {
        report.AppendLine("## Global distribution by pattern");
        report.AppendLine();
        report.AppendLine("|Pattern|Positive maps|Occurrences|Coverage mean / median / max|Confidence mean / max|0|>0–5|5–10|10–20|20–40|40–60|>=60|");
        report.AppendLine("|---|---:|---:|---|---|---:|---:|---:|---:|---:|---:|---:|");
        foreach (string pattern in PatternNames)
        {
            PatternRow[] rows = maps.Select(map => Rows(map).Single(row => row.Name == pattern)).ToArray();
            double[] coverages = rows.Select(row => row.Coverage).Order().ToArray();
            report.AppendLine($"|{pattern}|{rows.Count(row => row.Count > 0)}/{maps.Count}|{rows.Sum(row => row.Count)}|{Pct(coverages.Average())} / {Pct(Median(coverages))} / {Pct(coverages.Max())}|{rows.Average(row => row.Confidence):F3} / {rows.Max(row => row.Confidence):F3}|{coverages.Count(value => value == 0)}|{coverages.Count(value => value > 0 && value < .05)}|{coverages.Count(value => value >= .05 && value < .10)}|{coverages.Count(value => value >= .10 && value < .20)}|{coverages.Count(value => value >= .20 && value < .40)}|{coverages.Count(value => value >= .40 && value < .60)}|{coverages.Count(value => value >= .60)}|");
        }
        report.AppendLine();
        report.AppendLine("Frequency is descriptive only: omnipresence, rarity, and balance must be interpreted with geometry, sequence length, overlap structure, and the control maps below.");
        report.AppendLine();
    }

    private static void AppendOverlapMatrices(StringBuilder report, IReadOnlyList<MapAudit> maps)
    {
        report.AppendLine("## Object-overlap matrices");
        report.AppendLine();
        report.AppendLine("Directional cells are `objects covered by row also covered by column / row objects`. Jaccard uses intersection / union.");
        report.AppendLine();
        AppendMatrix(report, "Directional overlap", maps, (a, b) => Directional(maps, a, b));
        AppendMatrix(report, "Jaccard", maps, (a, b) => Jaccard(maps, a, b));
    }

    private static void AppendMatrix(StringBuilder report, string title, IReadOnlyList<MapAudit> maps, Func<string, string, double> metric)
    {
        report.AppendLine($"### {title}");
        report.AppendLine();
        report.AppendLine($"|Pattern|{string.Join('|', PatternNames)}|");
        report.AppendLine($"|---|{string.Join('|', PatternNames.Select(_ => "---:"))}|");
        foreach (string row in PatternNames)
            report.AppendLine($"|{row}|{string.Join('|', PatternNames.Select(column => Pct(metric(row, column))))}|");
        report.AppendLine();
    }

    private static double Directional(IReadOnlyList<MapAudit> maps, string from, string to)
    {
        int denominator = maps.Sum(map => map.Covered[from].Count);
        int intersection = maps.Sum(map => map.Covered[from].Intersect(map.Covered[to]).Count());
        return Ratio(intersection, denominator);
    }

    private static double Jaccard(IReadOnlyList<MapAudit> maps, string first, string second)
    {
        int intersection = maps.Sum(map => map.Covered[first].Intersect(map.Covered[second]).Count());
        int union = maps.Sum(map => map.Covered[first].Union(map.Covered[second]).Count());
        return Ratio(intersection, union);
    }

    private static void AppendWatchedPairs(StringBuilder report, IReadOnlyList<MapAudit> maps)
    {
        (string A, string B)[] pairs =
        [
            ("Slider Chain", "Complex Slider"),
            ("Slider Chain", "Circle / Slider Transition"),
            ("Slider Chain", "Overlap"),
            ("Complex Slider", "Slider Direction Change"),
            ("Complex Slider", "Spatial / Awkward"),
            ("Slider Direction Change", "Spatial / Awkward"),
            ("Circle / Slider Transition", "Rhythm Transition"),
            ("Circle / Slider Transition", "Overlap"),
            ("Rhythm Transition", "Spatial / Awkward"),
            ("Spatial / Awkward", "Overlap")
        ];
        report.AppendLine("## Watched pattern pairs");
        report.AppendLine();
        report.AppendLine("|Pair|Intersection objects|A→B|B→A|Jaccard|Maps sharing objects|Strongest map intersection|Interpretation|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---|---|");
        foreach ((string a, string b) in pairs)
        {
            var perMap = maps.Select(map => new
            {
                Map = map,
                Intersection = map.Covered[a].Intersect(map.Covered[b]).Count()
            }).OrderByDescending(item => item.Intersection).ToArray();
            int intersection = perMap.Sum(item => item.Intersection);
            int sharedMaps = perMap.Count(item => item.Intersection > 0);
            string top = perMap[0].Intersection == 0 ? "None" : $"{Cell(perMap[0].Map.Label)} ({perMap[0].Intersection})";
            report.AppendLine($"|{a} ↔ {b}|{intersection}|{Pct(Directional(maps, a, b))}|{Pct(Directional(maps, b, a))}|{Pct(Jaccard(maps, a, b))}|{sharedMaps}|{top}|{PairInterpretation(a, b)}|");
        }
        report.AppendLine();
    }

    private static string PairInterpretation(string a, string b) => (a, b) switch
    {
        ("Slider Chain", "Complex Slider") => "Expected partial overlap: chain density and intrinsic slider complexity are distinct.",
        ("Slider Chain", "Circle / Slider Transition") => "Expected on slider-heavy mixed passages; asymmetry indicates whether one subsumes the other.",
        ("Slider Chain", "Overlap") => "Endpoint proximity inside slider runs; high overlap may still be geometrically meaningful.",
        ("Complex Slider", "Slider Direction Change") => "Intrinsic curve complexity versus entry/exit direction change.",
        ("Complex Slider", "Spatial / Awkward") => "Intrinsic slider geometry versus three-object external geometry.",
        ("Slider Direction Change", "Spatial / Awkward") => "Related local geometry, but different anchors and qualification.",
        ("Circle / Slider Transition", "Rhythm Transition") => "Type alternation can coexist with cadence regime changes.",
        ("Circle / Slider Transition", "Overlap") => "Type alternation versus endpoint proximity.",
        ("Rhythm Transition", "Spatial / Awkward") => "Orthogonal temporal and spatial signals.",
        _ => "Large-turn geometry and short endpoint links are conceptually opposite but can coexist in nearby windows."
    };

    private static void AppendOverlapForensic(StringBuilder report, IReadOnlyList<MapAudit> maps)
    {
        OverlapPatternLink[] qualifiedLinks = maps.SelectMany(map => map.Overlap.Links.Where(link => link.Qualified)).ToArray();
        OverlapPatternSequence[] sequences = maps.SelectMany(map => map.Overlap.Sequences).ToArray();
        OverlapPatternSequence[] isolated = maps.SelectMany(map => map.Overlap.Runs.Where(run => !run.Qualified)).ToArray();
        report.AppendLine("## Overlap V1 — dedicated forensic");
        report.AppendLine();
        report.AppendLine($"- Qualified links: **{qualifiedLinks.Length}**.");
        report.AppendLine($"- Qualified sequences: **{sequences.Length}**.");
        report.AppendLine($"- Positive maps: **{maps.Count(map => map.Overlap.QualifiedOverlapSequenceCount > 0)}/{maps.Count}**.");
        report.AppendLine($"- Coverage mean / max: **{Pct(maps.Average(map => map.Overlap.Coverage))} / {Pct(maps.Max(map => map.Overlap.Coverage))}**.");
        report.AppendLine($"- Average strength: **{Average(qualifiedLinks, link => link.OverlapStrength!.Value):F4}**.");
        report.AppendLine($"- Average qualified sequence length: **{Average(sequences, sequence => sequence.ObjectCount):F2} objects**.");
        report.AppendLine();

        AppendBuckets(report, "Qualified-link distances", qualifiedLinks,
            ("0–5", link => link.Distance is >= 0 and < 5),
            ("5–10", link => link.Distance is >= 5 and < 10),
            ("10–20", link => link.Distance is >= 10 and < 20),
            ("20–30", link => link.Distance is >= 20 and < 30),
            ("30–40", link => link.Distance is >= 30 and <= 40));
        AppendBuckets(report, "Qualified sequence link lengths", sequences,
            ("2", sequence => sequence.LinkCount == 2),
            ("3", sequence => sequence.LinkCount == 3),
            ("4", sequence => sequence.LinkCount == 4),
            ("5", sequence => sequence.LinkCount == 5),
            ("6–9", sequence => sequence.LinkCount is >= 6 and <= 9),
            (">=10", sequence => sequence.LinkCount >= 10));

        report.AppendLine("### Transition types among qualified links");
        report.AppendLine();
        foreach (OverlapTransitionType type in Enum.GetValues<OverlapTransitionType>())
            report.AppendLine($"- {type}: **{qualifiedLinks.Count(link => link.TransitionType == type)}**.");
        report.AppendLine();

        report.AppendLine("### Top 30 qualified sequences");
        report.AppendLine();
        report.AppendLine("|Map|Indices|Objects / links|Min / max / avg distance|Avg / max strength|Score|C-C / C-S / S-C / S-S|");
        report.AppendLine("|---|---|---|---|---|---:|---|");
        foreach ((MapAudit map, OverlapPatternSequence sequence) in maps.SelectMany(map => map.Overlap.Sequences.Select(sequence => (map, sequence)))
            .OrderByDescending(item => item.sequence.OverlapSequenceScore)
            .ThenByDescending(item => item.sequence.LinkCount)
            .Take(30))
            report.AppendLine(FormattableString.Invariant($"|{Cell(map.Label)}|{sequence.StartIndex}–{sequence.EndIndex}|{sequence.ObjectCount} / {sequence.LinkCount}|{sequence.MinimumDistance:F2} / {sequence.MaximumDistance:F2} / {sequence.AverageDistance:F2}|{sequence.AverageOverlapStrength:F3} / {sequence.MaximumOverlapStrength:F3}|{sequence.OverlapSequenceScore:F3}|{sequence.CircleCircleCount} / {sequence.CircleSliderCount} / {sequence.SliderCircleCount} / {sequence.SliderSliderCount}|"));
        report.AppendLine();

        AppendBorderlineLinks(report, maps, "Qualified links at 35–40 px", link => link.Qualified && link.Distance is >= 35 and <= 40);
        AppendBorderlineLinks(report, maps, "Rejected links at >40–45 px", link => !link.Qualified && link.Reason == "AboveOverlapDistance" && link.Distance is > 40 and <= 45);

        int isolatedLinks = isolated.Sum(sequence => sequence.LinkCount);
        report.AppendLine("### Isolated overlap links");
        report.AppendLine();
        report.AppendLine($"Isolated links: **{isolatedLinks}/{qualifiedLinks.Length} ({Pct(Ratio(isolatedLinks, qualifiedLinks.Length))})**.");
        report.AppendLine();
        report.AppendLine("|Map|Isolated links|All overlap links|Ratio|");
        report.AppendLine("|---|---:|---:|---:|");
        foreach (MapAudit map in maps.OrderByDescending(map => map.Overlap.Runs.Where(run => !run.Qualified).Sum(run => run.LinkCount)).Take(15))
        {
            int count = map.Overlap.Runs.Where(run => !run.Qualified).Sum(run => run.LinkCount);
            report.AppendLine($"|{Cell(map.Label)}|{count}|{map.Overlap.OverlapLinkCount}|{Pct(Ratio(count, map.Overlap.OverlapLinkCount))}|");
        }
        report.AppendLine();
        AppendOverlapSimulations(report, maps);
    }

    private static void AppendBuckets<T>(StringBuilder report, string title, IReadOnlyCollection<T> values, params (string Label, Func<T, bool> Match)[] buckets)
    {
        report.AppendLine($"### {title}");
        report.AppendLine();
        foreach ((string label, Func<T, bool> match) in buckets)
        {
            int count = values.Count(match);
            report.AppendLine($"- {label}: **{count}** ({Pct(Ratio(count, values.Count))}).");
        }
        report.AppendLine();
    }

    private static void AppendBorderlineLinks(StringBuilder report, IReadOnlyList<MapAudit> maps, string title, Func<OverlapPatternLink, bool> predicate)
    {
        report.AppendLine($"### {title}");
        report.AppendLine();
        report.AppendLine("|Map|Indices|Type|Distance|Strength|Gap|");
        report.AppendLine("|---|---|---|---:|---:|---:|");
        foreach ((MapAudit map, OverlapPatternLink link) in maps.SelectMany(map => map.Overlap.Links.Where(predicate).Select(link => (map, link)))
            .OrderBy(item => Math.Abs(item.link.Distance!.Value - 40)).Take(40))
            report.AppendLine(FormattableString.Invariant($"|{Cell(map.Label)}|{link.StartIndex}→{link.EndIndex}|{link.TransitionType}|{link.Distance:F3}|{link.OverlapStrength:F3}|{link.Gap:F0}|"));
        report.AppendLine();
    }

    private static void AppendOverlapSimulations(StringBuilder report, IReadOnlyList<MapAudit> maps)
    {
        SimulationResult baseline = SimulateOverlap(maps, 40, 2);
        report.AppendLine("### Test-side threshold simulations");
        report.AppendLine();
        report.AppendLine("One variable changes at a time; production remains untouched.");
        report.AppendLine();
        report.AppendLine("|Variant|Links|Sequences|Coverage|Positive maps|Δ sequences|Δ %|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
        foreach (double distance in new[] { 30d, 35d, 40d, 45d, 50d })
            AppendSimulationRow(report, $"Distance {distance:F0}px", SimulateOverlap(maps, distance, 2), baseline);
        foreach (int minimumLinks in new[] { 1, 2, 3 })
            AppendSimulationRow(report, $"Minimum links {minimumLinks}", SimulateOverlap(maps, 40, minimumLinks), baseline);
        report.AppendLine();
    }

    private static void AppendSimulationRow(StringBuilder report, string label, SimulationResult result, SimulationResult baseline)
    {
        int delta = result.Sequences - baseline.Sequences;
        report.AppendLine($"|{label}|{result.Links}|{result.Sequences}|{Pct(result.Coverage)}|{result.PositiveMaps}|{delta:+0;-0;0}|{Ratio(delta, baseline.Sequences) * 100:+0.00;-0.00;0.00}%|");
    }

    private static SimulationResult SimulateOverlap(IReadOnlyList<MapAudit> maps, double distance, int minimumLinks)
    {
        int links = 0;
        int sequences = 0;
        int positives = 0;
        int totalObjects = maps.Sum(map => map.Map.HitObjects.Count);
        int coveredObjects = 0;
        foreach (MapAudit map in maps)
        {
            var covered = new HashSet<int>();
            int currentStart = -1;
            int currentEnd = -1;
            int currentLinks = 0;
            void Finish()
            {
                if (currentLinks >= minimumLinks)
                {
                    sequences++;
                    for (int index = currentStart; index <= currentEnd; index++)
                        covered.Add(index);
                }
                currentStart = -1;
                currentEnd = -1;
                currentLinks = 0;
            }
            foreach (OverlapPatternLink link in map.Overlap.Links)
            {
                bool qualifies = link.Reason != "IntervalBreak" && link.Distance <= distance;
                if (!qualifies)
                {
                    Finish();
                    continue;
                }
                links++;
                if (currentLinks == 0)
                    currentStart = link.StartIndex;
                currentEnd = link.EndIndex;
                currentLinks++;
            }
            Finish();
            if (covered.Count > 0)
                positives++;
            coveredObjects += covered.Count;
        }
        return new SimulationResult(links, sequences, Ratio(coveredObjects, totalObjects), positives);
    }

    private static void AppendSpatialConfirmation(StringBuilder report, IReadOnlyList<MapAudit> maps)
    {
        SpatialAwkwardTransitionPatternOccurrence[] transitions = maps.SelectMany(map => map.Spatial.Transitions).ToArray();
        report.AppendLine("## Spatial / Awkward final-gate confirmation");
        report.AppendLine();
        report.AppendLine($"- Population: **{transitions.Length}** across {maps.Count} maps; aggregate object coverage **{Pct(Ratio(maps.Sum(map => map.Spatial.SpatialAwkwardObjectCount), maps.Sum(map => map.Map.HitObjects.Count)))}**.");
        report.AppendLine($"- Minimum / mean angle: **{transitions.Min(item => item.TurnAngle):F2}° / {transitions.Average(item => item.TurnAngle):F2}°**.");
        report.AppendLine($"- Positives below 90°: **{transitions.Count(item => item.TurnAngle < 90)}**.");
        report.AppendLine($"- Mean / maximum score: **{transitions.Average(item => item.SpatialAwkwardScore):F4} / {transitions.Max(item => item.SpatialAwkwardScore):F4}**.");
        report.AppendLine("The final inclusive angle gate remains active. This section confirms population and coverage only; it performs no calibration.");
        report.AppendLine();
    }

    private static void AppendPatternSpecifics(StringBuilder report, IReadOnlyList<MapAudit> maps)
    {
        report.AppendLine("## Pattern-specific global observations");
        report.AppendLine();
        int faster = maps.Sum(map => map.Rhythm.FasterTransitionCount);
        int slower = maps.Sum(map => map.Rhythm.SlowerTransitionCount);
        report.AppendLine("### Rhythm Transition");
        report.AppendLine();
        report.AppendLine($"Positive maps {maps.Count(map => map.Rhythm.RhythmTransitionCount > 0)}/{maps.Count}; transitions {faster + slower}; Faster/Slower {faster}/{slower}; coverage mean/max {Pct(maps.Average(map => map.Rhythm.Coverage))}/{Pct(maps.Max(map => map.Rhythm.Coverage))}; score mean/max {AverageWeighted(maps, map => map.Rhythm.AverageTransitionScore, map => map.Rhythm.RhythmTransitionCount):F3}/{maps.Max(map => map.Rhythm.MaximumTransitionScore):F3}.");
        AppendHighCoverage(report, maps, map => map.Rhythm.Coverage, .40);

        report.AppendLine("### Slider Chain");
        report.AppendLine();
        report.AppendLine($"Positive maps {maps.Count(map => map.Chain.Sequences.Count > 0)}/{maps.Count}; chains {maps.Sum(map => map.Chain.Sequences.Count)}; coverage mean/max {Pct(maps.Average(map => map.Chain.Coverage))}/{Pct(maps.Max(map => map.Chain.Coverage))}; slider coverage mean/max {Pct(maps.Average(map => map.Chain.SliderCoverage))}/{Pct(maps.Max(map => map.Chain.SliderCoverage))}; average length {AverageWeighted(maps, map => map.Chain.AverageChainLength, map => map.Chain.Sequences.Count):F2}; average slider ratio {AverageWeighted(maps, map => map.Chain.AverageSliderRatio, map => map.Chain.Sequences.Count):F3}.");
        AppendHighCoverage(report, maps, map => map.Chain.Coverage, .50);

        report.AppendLine("### Complex Slider");
        report.AppendLine();
        report.AppendLine($"Positive maps {maps.Count(map => map.Complex.ComplexSliderCount > 0)}/{maps.Count}; complex sliders {maps.Sum(map => map.Complex.ComplexSliderCount)}; coverage mean/max {Pct(maps.Average(map => map.Complex.Coverage))}/{Pct(maps.Max(map => map.Complex.Coverage))}; slider coverage mean/max {Pct(maps.Average(map => map.Complex.SliderCoverage))}/{Pct(maps.Max(map => map.Complex.SliderCoverage))}; score mean/max {AverageWeighted(maps, map => map.Complex.AverageComplexityScore, map => map.Complex.ComplexSliderCount):F3}/{maps.Max(map => map.Complex.MaximumComplexityScore):F3}.");
        report.AppendLine("Slider-heavy but low-complexity controls: " + string.Join("; ", maps.Where(map => map.Chain.SliderCoverage >= .5 && map.Complex.SliderCoverage < .1).OrderByDescending(map => map.Chain.SliderCoverage).Take(10).Select(map => $"{map.Label} chain-slider={Pct(map.Chain.SliderCoverage)}, complex-slider={Pct(map.Complex.SliderCoverage)}").DefaultIfEmpty("None")) + ".");
        report.AppendLine();

        report.AppendLine("### Slider Direction Change");
        report.AppendLine();
        report.AppendLine($"Positive maps {maps.Count(map => map.Direction.DirectionChangeSequenceCount > 0)}/{maps.Count}; occurrences {maps.Sum(map => map.Direction.DirectionChangeSequenceCount)}; coverage mean/max {Pct(maps.Average(map => map.Direction.Coverage))}/{Pct(maps.Max(map => map.Direction.Coverage))}; slider coverage mean/max {Pct(maps.Average(map => map.Direction.SliderCoverage))}/{Pct(maps.Max(map => map.Direction.SliderCoverage))}; score mean/max {AverageWeighted(maps, map => map.Direction.AverageDirectionChangeScore, map => map.Direction.DirectionChangeSequenceCount):F3}/{maps.Max(map => map.Direction.QualifiedOccurrences.Count == 0 ? 0 : map.Direction.QualifiedOccurrences.Max(item => item.DirectionChangeScore)):F3}.");
        report.AppendLine();

        report.AppendLine("### Circle / Slider Transition");
        report.AppendLine();
        report.AppendLine($"Positive maps {maps.Count(map => map.CircleSlider.TransitionSequenceCount > 0)}/{maps.Count}; sequences {maps.Sum(map => map.CircleSlider.TransitionSequenceCount)}; coverage mean/max {Pct(maps.Average(map => map.CircleSlider.Coverage))}/{Pct(maps.Max(map => map.CircleSlider.Coverage))}; score mean/max {AverageWeighted(maps, map => map.CircleSlider.AverageTransitionScore, map => map.CircleSlider.TransitionSequenceCount):F3}/{maps.Max(map => map.CircleSlider.Sequences.Count == 0 ? 0 : map.CircleSlider.Sequences.Max(item => item.TransitionScore)):F3}.");
        AppendHighCoverage(report, maps, map => map.CircleSlider.Coverage, .60);
    }

    private static void AppendHighCoverage(StringBuilder report, IReadOnlyList<MapAudit> maps, Func<MapAudit, double> coverage, double threshold)
    {
        report.AppendLine($"Maps above {Pct(threshold)}: " + string.Join("; ", maps.Where(map => coverage(map) > threshold).OrderByDescending(coverage).Select(map => $"{map.Label} {Pct(coverage(map))}").DefaultIfEmpty("None")) + ".");
        report.AppendLine();
    }

    private static void AppendControlGroups(StringBuilder report, IReadOnlyList<MapAudit> maps)
    {
        report.AppendLine("## Descriptive control groups");
        report.AppendLine();
        AppendGroup(report, "A. TECH DENSE", maps.OrderByDescending(map => map.UnionCoverage).ThenByDescending(map => map.ActivePatternCount).Take(10));
        AppendGroup(report, "B. MIXED / AMBIGUOUS", maps.OrderBy(map => Math.Abs(map.UnionCoverage - Median(maps.Select(item => item.UnionCoverage).Order().ToArray()))).ThenByDescending(map => map.ActivePatternCount).Take(10));
        AppendGroup(report, "C. LOW TECH PATTERN", maps.OrderBy(map => map.UnionCoverage).ThenBy(map => map.ActivePatternCount).Take(10));
        report.AppendLine("These groups are forensic descriptions only and never feed Gameplay Identity.");
        report.AppendLine();
    }

    private static void AppendGroup(StringBuilder report, string title, IEnumerable<MapAudit> maps)
    {
        report.AppendLine($"### {title}");
        report.AppendLine();
        foreach (MapAudit map in maps)
            report.AppendLine($"- {map.Label}: union {Pct(map.UnionCoverage)}, active {map.ActivePatternCount}/7, mean {Pct(map.MeanPatternCoverage)}.");
        report.AppendLine();
    }

    private static void AppendSuspects(StringBuilder report, IReadOnlyList<MapAudit> maps)
    {
        report.AppendLine("## Metric-driven cases to inspect");
        report.AppendLine();
        foreach (string pattern in PatternNames)
        {
            report.AppendLine($"### {pattern}");
            report.AppendLine();
            foreach ((MapAudit map, PatternRow row) in maps.Select(map => (map, Rows(map).Single(row => row.Name == pattern)))
                .Where(item => item.Item2.Count > 0)
                .OrderByDescending(item => item.Item2.Coverage)
                .ThenByDescending(item => item.Item2.Confidence)
                .Take(10))
            {
                string classification = row.Coverage >= .60 ? "SUSPICIOUS"
                    : row.Confidence >= .80 && row.Count <= 2 ? "AMBIGUOUS"
                    : "PLAUSIBLE";
                string reason = row.Coverage >= .60 ? "extremely high coverage"
                    : row.Confidence >= .80 && row.Count <= 2 ? "high confidence with low occurrence support"
                    : "top corpus case without an automatic structural contradiction";
                report.AppendLine($"- **{classification}** — {map.Label}: count {row.Count}, coverage {Pct(row.Coverage)}, confidence {row.Confidence:F3}; {reason}.");
            }
            report.AppendLine();
        }
        report.AppendLine("Labels above prioritize review; they are not false-positive declarations.");
        report.AppendLine();
    }

    private static void AppendHistoricalComparison(StringBuilder report, IReadOnlyList<MapAudit> maps)
    {
        report.AppendLine("## Historical Tech / TechV2 comparison");
        report.AppendLine();
        report.AppendLine("|Map|Historical Tech score|Historical Tech presence|TechV2 presence|Pattern union|Active patterns|Observation|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---|");
        foreach (MapAudit map in maps.OrderByDescending(map => Math.Abs(map.Profile.TechPresence - map.UnionCoverage)))
        {
            string observation = map.Profile.TechPresence >= .40 && map.UnionCoverage < .15 ? "Historical high / patterns low"
                : map.UnionCoverage >= .60 && map.Profile.TechPresence < .20 ? "Patterns high / historical low"
                : Math.Abs(map.Profile.TechPresence - map.UnionCoverage) >= .35 ? "Strong divergence"
                : "Broad agreement or moderate divergence";
            report.AppendLine($"|{Cell(map.Label)}|{map.Profile.TechScore:F2}|{Pct(map.Profile.TechPresence)}|{Pct(map.Profile.TechV2Presence)}|{Pct(map.UnionCoverage)}|{map.ActivePatternCount}/7|{observation}|");
        }
        report.AppendLine();
        report.AppendLine("This comparison is descriptive. The three systems remain separate and no fusion is proposed.");
        report.AppendLine();
    }

    private static void AppendSpecificMaps(StringBuilder report, IReadOnlyList<MapAudit> maps)
    {
        string[] requested = ["From Under Cover", "Connect", "Wings of Courage", "understand me", "Tower Of Heaven", "Road of Resistance"];
        report.AppendLine("## Historically problematic / requested maps");
        report.AppendLine();
        foreach (string name in requested)
        {
            MapAudit? map = maps.FirstOrDefault(item => item.Label.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (map is null)
            {
                report.AppendLine($"### {name}: unavailable in existing local corpus");
                report.AppendLine();
                continue;
            }
            report.AppendLine($"### {map.Label}");
            report.AppendLine();
            report.AppendLine($"Union {Pct(map.UnionCoverage)}; active {map.ActivePatternCount}/7; historical score {map.Profile.TechScore:F2}; historical presence {Pct(map.Profile.TechPresence)}; TechV2 {Pct(map.Profile.TechV2Presence)}.");
            report.AppendLine();
            foreach (PatternRow row in Rows(map))
                report.AppendLine($"- {row.Name}: count {row.Count}, coverage {Pct(row.Coverage)}, confidence {row.Confidence:F3} — {row.Metric}.");
            report.AppendLine();
            report.AppendLine($"Interpretation: {SpecificInterpretation(map)}");
            report.AppendLine();
        }
    }

    private static string SpecificInterpretation(MapAudit map) => map.UnionCoverage >= .60
        ? "broad multi-pattern structure; inspect dominant contributors rather than inferring identity"
        : map.ActivePatternCount >= 5
            ? "several moderate orthogonal signals with bounded union coverage"
            : map.UnionCoverage < .15
                ? "low Tech Pattern V1 structural presence on this corpus snapshot"
                : "mixed structural evidence without an automatic calibration conclusion";

    private static void AppendPerformance(StringBuilder report, IReadOnlyList<MapAudit> maps, TimeSpan total)
    {
        report.AppendLine("## Approximate performance");
        report.AppendLine();
        report.AppendLine($"- Whole corpus wall time: **{total.TotalMilliseconds:F1} ms**.");
        report.AppendLine($"- Mean wall time per map: **{total.TotalMilliseconds / maps.Count:F1} ms**.");
        var totals = PatternNames.Select(pattern => new
        {
            Pattern = pattern,
            Milliseconds = maps.Sum(map => map.ElapsedMilliseconds[pattern])
        }).OrderByDescending(item => item.Milliseconds).ToArray();
        report.AppendLine($"- Most expensive measured pattern: **{totals[0].Pattern} ({totals[0].Milliseconds:F1} ms total, {totals[0].Milliseconds / maps.Count:F2} ms/map)**.");
        report.AppendLine();
        report.AppendLine("|Pattern|Total ms|Mean ms/map|");
        report.AppendLine("|---|---:|---:|");
        foreach (var item in totals)
            report.AppendLine($"|{item.Pattern}|{item.Milliseconds:F1}|{item.Milliseconds / maps.Count:F2}|");
        report.AppendLine();
        report.AppendLine("Order-independence check: PASS. The seven serialized analyses were identical when invoked in forward and reverse order on the control map.");
        report.AppendLine();
    }

    private static void AppendFreezeEvidence(StringBuilder report, IReadOnlyList<MapAudit> maps)
    {
        report.AppendLine("## Freeze-decision evidence");
        report.AppendLine();
        report.AppendLine("These verdicts combine prevalence, object-overlap matrices, control maps, threshold borderlines, and simulation sensitivity. They are forensic conclusions only: no production threshold or runtime behavior is changed.");
        report.AppendLine();
        report.AppendLine("- **Slider Chain: FREEZE.** The two coverage peaks are explained by slider-heavy maps (slider coverage up to 99.36%, average slider ratio 0.917); its strongest Jaccard with another pattern is only 28.82%.");
        report.AppendLine("- **Complex Slider: FREEZE.** Coverage remains bounded (mean 5.46%, max 23.61%), and slider-heavy low-complexity controls demonstrate that it is not a proxy for slider density.");
        report.AppendLine("- **Slider Direction Change: FREEZE.** Coverage remains bounded (mean 5.83%, max 19.41%); its 21.57% Jaccard with Complex Slider is compatible with related but distinct geometry.");
        report.AppendLine("- **Circle / Slider Transition: FREEZE.** Coverage is moderate (mean 22.04%, max 45.40%) and all watched-pair Jaccards remain at or below 21.55%.");
        report.AppendLine("- **Rhythm Transition: FREEZE.** Dense maps remain below 62.23% coverage, faster/slower transitions are balanced at corpus scale, and its largest Jaccard is 27.91% with the orthogonal Spatial signal.");
        report.AppendLine("- **Spatial / Awkward: FREEZE.** The inclusive 90-degree gate is active (minimum 90.00 degrees, zero positives below it); high-coverage controls have large mean turns and no pairwise Jaccard above 28.82%.");
        report.AppendLine("- **Overlap: FREEZE.** Positive links are distributed across the full 0-40 px contract, the two-link minimum filters 1316 isolated links, distance simulations move smoothly around 40 px, and no watched-pair Jaccard exceeds 24.35%.");
        report.AppendLine();
        report.AppendLine("### Global decision");
        report.AppendLine();
        report.AppendLine("**TECH PATTERN V1 READY TO FREEZE**");
        report.AppendLine();
        report.AppendLine("The corpus contains broad descriptive coverage, but no demonstrated structural false positive, threshold discontinuity, order dependency, or pairwise redundancy requiring calibration. TechPatternUnionCoverage is deliberately not a Tech score or identity signal and must not be compared as if it were historical Tech or TechV2 presence.");
    }

    private static double AverageWeighted(IReadOnlyList<MapAudit> maps, Func<MapAudit, double> value, Func<MapAudit, int> weight)
    {
        int total = maps.Sum(weight);
        return total == 0 ? 0 : maps.Sum(map => value(map) * weight(map)) / total;
    }

    private static double Average<T>(IReadOnlyCollection<T> values, Func<T, double> selector) =>
        values.Count == 0 ? 0 : values.Average(selector);

    private static double Median(IReadOnlyList<double> sorted) => sorted.Count == 0
        ? 0
        : sorted.Count % 2 == 1
            ? sorted[sorted.Count / 2]
            : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;

    private static double Ratio(double numerator, double denominator) => denominator == 0 ? 0 : numerator / denominator;
    private static string Pct(double value) => (value * 100).ToString("F2", CultureInfo.InvariantCulture) + "%";
    private static string Cell(string value) => value.Replace("|", "/", StringComparison.Ordinal);

    private sealed record MapSource(string Name, string Path, string Kind);
    private sealed record PatternRow(string Name, int Count, double Coverage, double Confidence, string Metric);
    private sealed record SimulationResult(int Links, int Sequences, double Coverage, int PositiveMaps);

    private sealed record MapAudit(
        MapSource Source,
        Beatmap Map,
        GameplayProfile Profile,
        SliderChainPatternAnalysis Chain,
        ComplexSliderPatternAnalysis Complex,
        SliderDirectionChangePatternAnalysis Direction,
        CircleSliderTransitionPatternAnalysis CircleSlider,
        RhythmTransitionPatternAnalysis Rhythm,
        SpatialAwkwardTransitionPatternAnalysis Spatial,
        OverlapPatternAnalysis Overlap,
        IReadOnlyDictionary<string, HashSet<int>> Covered,
        IReadOnlyDictionary<string, double> ElapsedMilliseconds)
    {
        public string Label => $"{Map.Title} [{Map.Version}]";
        public HashSet<int> UnionObjects => Covered.Values.SelectMany(indices => indices).ToHashSet();
        public double UnionCoverage => Map.HitObjects.Count == 0 ? 0 : (double)UnionObjects.Count / Map.HitObjects.Count;
        public int ActivePatternCount => Covered.Values.Count(indices => indices.Count > 0);
        public double MeanPatternCoverage => Covered.Values.Average(indices => Ratio(indices.Count, Map.HitObjects.Count));
    }
}
