using System.Diagnostics;
using System.IO;
using System.Text;
using BeatInsight.Analysis;
using BeatInsight.Analysis.Identity;
using BeatInsight.Analysis.Rating;
using BeatInsight.Analysis.Speed;
using BeatInsight.Models;
using BeatInsight.Models.Speed;
using BeatInsight.Parser;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class GlobalTripletSpeedGlobalForensicTests(ITestOutputHelper output)
{
    private static readonly string[] Fixtures =
    [
        "(can you) understand me [uhh].osu", "Ashioto Tarte Tatin [Koori's Insane].osu",
        "Exit This Earth's Atmosphere [Primordial Nucleosynthesis].osu", "FREEDOM DiVE [Arles].osu",
        "FREEDOM DiVE [FrenZ's Insane].osu", "Frozen [Collab Insane].osu",
        "Kira Killer [Mocaotic's Insane].osu", "Snow Goose [Extra].osu", "Tower Of Heaven [Extra].osu",
        "forever we can make it! [Kudowari's Expert].osu",
        "Vickeblanka - Black Rover (TV Size) (Sotarks) [Extreme].osu"
    ];
    private static readonly Source[] Controls =
    [
        new("Road of Resistance", @"D:\osu!\Songs\399372 BABYMETAL - Road of Resistance (1)\BABYMETAL - Road of Resistance (Monstrata) [Rebellion].osu"),
        new("From Under Cover", @"D:\osu!\Songs\462386 Foreground Eclipse - From Under Cover (Caught Up In A Love Song)\Foreground Eclipse - From Under Cover (Caught Up In A Love Song) (Seni) [I've Stopped Time and Everything is at My Mercy].osu"),
        new("Connect TV Size", @"D:\osu!\Songs\1854325 ClariS - Connect (TV Size)\ClariS - Connect (TV Size) (Aakki) [Expert].osu"),
        new("Wings of Courage", @"D:\osu!\Songs\1706691 Kawada Mami - Wings of Courage -Sora o Koete-\Kawada Mami - Wings of Courage -Sora o Koete- (Zekk) [Expert].osu")
    ];

    [Fact]
    public void RealCorpusWritesGlobalTripletSpeedForensicReport()
    {
        var stopwatch = Stopwatch.StartNew();
        Row[] rows = BuildCorpus().Select(Analyze).ToArray();
        stopwatch.Stop();
        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.InRange(row.Speed.SpeedPercent, 0, 100));
        Assert.All(rows, row => Assert.True(double.IsFinite(row.Speed.SpeedPercent)));
        Assert.All(rows, row => Assert.Equal(row.HistoricalRating, row.Map.BeatInsightRating));

        var report = new StringBuilder();
        report.AppendLine("# Global Triplet Speed forensic\n");
        report.AppendLine($"Runtime confirmation on **{rows.Length} existing maps**, {rows.Sum(row => row.Map.HitObjects.Count):N0} hit objects, in {stopwatch.Elapsed.TotalSeconds:F1}s. No download, label or map-specific tuning.\n");
        AppendAudit(report);
        AppendDefinition(report);
        AppendMappingSimulation(report, rows);
        AppendGapComparison(report, rows);
        AppendSpinnerComparison(report, rows);
        AppendSelectedMapping(report);
        AppendDistribution(report, rows);
        AppendCorpus(report, rows);
        AppendControls(report, rows);
        AppendOldComparison(report, rows);
        AppendLimitationsAndDecision(report, rows);

        string path = Path.Combine(FindRepoRoot(), "Dataset", "Reports", "global-triplet-speed-forensic.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, report.ToString(), Encoding.UTF8);
        output.WriteLine($"Global Triplet Speed corpus: {rows.Length} maps; report: {path}");
    }

    private static Row Analyze(Source source)
    {
        Beatmap map = BeatmapParser.Load(source.Path);
        double historicalRating = map.BeatInsightRating;
        GameplayProfile gameplay = GameplayAnalyzer.Analyze(map);
        GlobalTripletSpeedAnalysis speed = GlobalTripletSpeedAnalyzer.Analyze(map);
        var identity = GameplayIdentityV3Analyzer.Analyze(map);
        var rating = BeatInsightRatingV2Analyzer.Analyze(map);
        return new(source.Name, source.Path, map, speed, gameplay.SpeedScore,
            identity.PrimaryStructure.ToString(), rating.Score, historicalRating);
    }

    private static void AppendAudit(StringBuilder report)
    {
        report.AppendLine("## AUDIT\n");
        report.AppendLine("|Component|Status|Source|Current meaning|Used by|");
        report.AppendLine("|---|---|---|---|---|");
        report.AppendLine("|GameplayProfile.SpeedScore|EXISTING / FROZEN|GameplayAnalyzer.AnalyzeSpeed|Circle-only skill pressure: fast-section cadence intensity × weighted presence|Historical UI, Identity traits, Fusion V1, Rating V2|");
        report.AppendLine("|Speed Stream Pattern|EXISTING / FROZEN|StreamPatternAnalyzer.AnalyzeSpeedStream|Absolute sustained tapping speed on maximal circle-only Stream runs|Pattern/Identity/Fusion evidence|");
        report.AppendLine("|Global Triplet Speed|NEW / PARALLEL|GlobalTripletSpeedAnalyzer|Equally weighted mean temporal speed across valid sliding triplets|Future UI V2 only; not wired in this pass|");
        report.AppendLine("|Rating V1|UNCHANGED|GameplayAnalyzer historical rating path|osu! SR plus bounded historical adjustment|Existing rating display|");
        report.AppendLine("|Rating V2|UNCHANGED|BeatInsightRatingV2Analyzer|Independent multi-signal difficulty rating|Parallel analysis/debug|");
        report.AppendLine("|Fusion V1|UNCHANGED|GameplayFusionAnalyzer|Historical skills plus bounded pattern support|Identity V3 input|");
        report.AppendLine("|Current UI Speed|UNCHANGED|GameplayProfile.SpeedScore binding|Historical skill-pressure Speed|Analyzer workspace|\n");
    }

    private static void AppendDefinition(StringBuilder report)
    {
        report.AppendLine("## DEFINITION\n");
        report.AppendLine("For every sliding window `(i, i+1, i+2)`: `Delta1 = Start[i+1]-Start[i]`, `Delta2 = Start[i+2]-Start[i+1]`, `TripletInterval = (Delta1+Delta2)/2`. The final score is strictly `Sum(LocalSpeed)/ValidTripletCount`.\n");
        report.AppendLine("Circle and slider StartTime values are measured. Slider EndTime is never synthesized. A triplet containing a spinner is excluded as a tapping break. Non-increasing timestamps and unsupported types are excluded. Consistency is descriptive only and never changes SpeedPercent.\n");
    }

    private static void AppendMappingSimulation(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## MAPPING SIMULATIONS\n");
        report.AppendLine("All simulations use the same equal-weight triplets; no runtime formula is used to select a map.\n");
        report.AppendLine("|Slow|Fast|Gamma|Mean|Median|Min|Max|StdDev|>=90|");
        report.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (double slow in new[] { 200d, 250d, 300d })
            foreach (double fast in new[] { 40d, 50d, 60d })
                foreach (double gamma in new[] { .8, 1d, 1.2 })
                {
                    double[] values = rows.Select(row => Simulate(row.Map.HitObjects, slow, fast, gamma, null, false, false).Speed).Order().ToArray();
                    report.AppendLine($"|{slow:F0}|{fast:F0}|{gamma:F1}|{values.Average():F2}|{Median(values):F2}|{values.Min():F2}|{values.Max():F2}|{StdDev(values):F2}|{values.Count(value => value >= 90)}|");
                }
        report.AppendLine();
    }

    private static void AppendGapComparison(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## GAP POLICY COMPARISON\n");
        report.AppendLine("|Policy|Mean|Median|Min|Max|StdDev|Mean valid %|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
        GapRow(report, rows, "A all positive gaps", null, false);
        GapRow(report, rows, "B exclude >500 ms", 500, false);
        GapRow(report, rows, "C exclude >750 ms", 750, false);
        GapRow(report, rows, "D exclude >1000 ms", 1000, false);
        GapRow(report, rows, "E >750 ms contributes zero", 750, true);
        report.AppendLine("\nThe no-cap policy is selected: long pauses do not collapse the distribution, and the linear mapping already gives slow triplets zero without removing them from the requested global mean.\n");
    }

    private static void AppendSpinnerComparison(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## SPINNER POLICY\n");
        Simulation[] included = rows.Select(row => Simulate(row.Map.HitObjects, 250, 50, 1, null, false, true)).ToArray();
        Simulation[] broken = rows.Select(row => Simulate(row.Map.HitObjects, 250, 50, 1, null, false, false)).ToArray();
        double[] delta = broken.Select((value, index) => Math.Abs(value.Speed - included[index].Speed)).ToArray();
        report.AppendLine($"Including spinners versus treating them as breaks changes **{delta.Count(value => value > .0001)}/{rows.Count}** maps; mean absolute delta **{delta.Average():F3}**, maximum **{delta.Max():F3}**. The effect is small, but spinner-break matches tapping semantics and is selected. Circle and slider starts remain included.\n");
    }

    private static void AppendSelectedMapping(StringBuilder report)
    {
        report.AppendLine("## SELECTED MAPPING\n");
        report.AppendLine("`LocalSpeedPercent = clamp((250 - TripletIntervalMs) / (250 - 50), 0, 1) × 100`\n");
        report.AppendLine("- SlowInterval: **250 ms**; FastInterval: **50 ms**; curve: **linear (gamma 1.0)**.");
        report.AppendLine("- Gap policy: **all positive gaps remain valid** and naturally saturate toward zero.");
        report.AppendLine("- Object policy: **circles + sliders included by StartTime; spinner triplets excluded**.");
        report.AppendLine("- Aggregation: **plain arithmetic mean; every valid triplet has weight 1**.\n");
    }

    private static void AppendDistribution(StringBuilder report, IReadOnlyList<Row> rows)
    {
        double[] values = rows.Select(row => row.Speed.SpeedPercent).Order().ToArray();
        report.AppendLine("## CORPUS DISTRIBUTION\n");
        report.AppendLine($"Mean **{values.Average():F2}**, median **{Median(values):F2}**, min **{values.Min():F2}**, max **{values.Max():F2}**, stddev **{StdDev(values):F2}**.\n");
        for (int lower = 0; lower < 100; lower += 10)
            report.AppendLine($"- {lower}–{lower + 10}: {values.Count(value => value >= lower && (lower == 90 ? value <= 100 : value < lower + 10))}");
        report.AppendLine($"\nSlowest observed controls: {string.Join("; ", rows.OrderBy(row => row.Speed.SpeedPercent).Take(5).Select(row => $"{row.Name} {row.Speed.SpeedPercent:F2}"))}.\n");
    }

    private static void AppendCorpus(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## PER-MAP FORENSIC\n");
        report.AppendLine("|Map|Objects|Triplets|Valid|Excluded|Avg ms|Median ms|Min–Max ms|Speed %|Old Speed|BPM metadata|Identity V3|Rating V2|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|");
        foreach (Row row in rows.OrderByDescending(row => row.Speed.SpeedPercent))
            report.AppendLine($"|{Cell(DisplayName(row))}|{row.Map.HitObjects.Count}|{row.Speed.TotalTripletCount}|{row.Speed.ValidTripletCount}|{row.Speed.ExcludedTripletCount}|{row.Speed.AverageTripletIntervalMs:F2}|{row.Speed.MedianTripletIntervalMs:F2}|{row.Speed.MinTripletIntervalMs:F2}–{row.Speed.MaxTripletIntervalMs:F2}|{row.Speed.SpeedPercent:F2}|{row.OldSpeed:F2}|{row.Map.BPM:F2}|{row.Identity}|{row.RatingV2:F2}|");
        report.AppendLine();
    }

    private static void AppendControls(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## CONTROL MAPS\n");
        string[] controls = ["Tower Of Heaven", "Road of Resistance", "FREEDOM DiVE [Arles]", "understand me", "Black Rover", "Wings of Courage", "Connect", "From Under Cover", "Honesty"];
        foreach (string control in controls)
        {
            Row? row = control == "Road of Resistance"
                ? rows.FirstOrDefault(item => item.Path == Controls[0].Path)
                : rows.FirstOrDefault(item => DisplayName(item).Contains(control, StringComparison.OrdinalIgnoreCase) || item.Path.Contains(control, StringComparison.OrdinalIgnoreCase));
            report.AppendLine(row is null
                ? $"- {control}: **not present in the available corpus**."
                : $"- {DisplayName(row)}: **{row.Speed.SpeedPercent:F2}%**, avg/median {row.Speed.AverageTripletIntervalMs:F2}/{row.Speed.MedianTripletIntervalMs:F2} ms, valid {row.Speed.ValidTripletCount}/{row.Speed.TotalTripletCount}, old Speed {row.OldSpeed:F2}, Identity {row.Identity}, Rating V2 {row.RatingV2:F2}.");
        }
        report.AppendLine();
    }

    private static void AppendOldComparison(StringBuilder report, IReadOnlyList<Row> rows)
    {
        double correlation = Corr(rows.Select(row => row.Speed.SpeedPercent), rows.Select(row => row.OldSpeed));
        double meanDifference = rows.Average(row => row.Speed.SpeedPercent - row.OldSpeed);
        report.AppendLine("## OLD VS NEW SPEED\n");
        report.AppendLine($"Pearson **{correlation:F3}**; mean `(New - Old)` **{meanDifference:+0.00;-0.00;0.00}** points. Old is circle-only fast-section skill pressure; New is average map timing across circle/slider triplets. Divergence is expected and is not calibrated away.\n");
        foreach (Row row in rows.OrderByDescending(row => Math.Abs(row.Speed.SpeedPercent - row.OldSpeed)).Take(10))
            report.AppendLine($"- {DisplayName(row)}: New {row.Speed.SpeedPercent:F2}, Old {row.OldSpeed:F2}, delta {row.Speed.SpeedPercent - row.OldSpeed:+0.00;-0.00;0.00}. New includes ordinary circle/slider timing across the whole map; Old rewards only qualifying fast circle sections and their presence.");
        report.AppendLine();
    }

    private static void AppendLimitationsAndDecision(StringBuilder report, IReadOnlyList<Row> rows)
    {
        double[] values = rows.Select(row => row.Speed.SpeedPercent).ToArray();
        bool ready = StdDev(values) >= 10 && values.Max() - values.Min() >= 40 && values.Count(value => value >= 90) == 0 && values.Count(value => value <= 1) == 0;
        report.AppendLine("## KNOWN LIMITATIONS\n");
        report.AppendLine("- This is descriptive object-start timing, not mechanical tapping difficulty, peak speed or pattern speed.");
        report.AppendLine("- Two rhythmically different triplets with the same average interval receive the same Speed; consistency is diagnostic only.");
        report.AppendLine("- Spinner triplets are absent from the mean, while musical pauses between tapping objects remain and lower it.");
        report.AppendLine("- The local corpus is useful for discrimination checks but is not a balanced benchmark.\n");
        report.AppendLine("## FINAL DECISION\n");
        report.AppendLine($"**GLOBAL TRIPLET SPEED: {(ready ? "READY TO FREEZE" : "TARGETED CALIBRATION REQUIRED")}**\n");
        report.AppendLine($"Evidence: stddev {StdDev(values):F2}; range {values.Min():F2}–{values.Max():F2}; >=90 saturation {values.Count(value => value >= 90)}/{values.Length}; near-zero {values.Count(value => value <= 1)}/{values.Length}. The mapping is monotonic, linear, global, and has no percentile, peak, coverage or map-name adjustment.");
    }

    private static void GapRow(StringBuilder report, IReadOnlyList<Row> rows, string name, double? maximumGap, bool zeroLongGaps)
    {
        Simulation[] results = rows.Select(row => Simulate(row.Map.HitObjects, 250, 50, 1, maximumGap, zeroLongGaps, false)).ToArray();
        double[] values = results.Select(result => result.Speed).Order().ToArray();
        report.AppendLine($"|{name}|{values.Average():F2}|{Median(values):F2}|{values.Min():F2}|{values.Max():F2}|{StdDev(values):F2}|{results.Average(result => result.Total == 0 ? 0 : 100d * result.Valid / result.Total):F2}|");
    }

    private static Simulation Simulate(IReadOnlyList<HitObject> objects, double slow, double fast, double gamma, double? maximumGap, bool zeroLongGaps, bool includeSpinner)
    {
        int total = Math.Max(0, objects.Count - 2), valid = 0;
        double sum = 0;
        for (int index = 0; index + 2 < objects.Count; index++)
        {
            HitObject a = objects[index], b = objects[index + 1], c = objects[index + 2];
            if (!includeSpinner && (((a.Type | b.Type | c.Type) & 8) != 0)) continue;
            double d1 = b.Time - (double)a.Time, d2 = c.Time - (double)b.Time;
            if (d1 <= 0 || d2 <= 0) continue;
            bool longGap = maximumGap.HasValue && (d1 > maximumGap || d2 > maximumGap);
            if (longGap && !zeroLongGaps) continue;
            double normalized = Math.Clamp((slow - (d1 + d2) / 2) / (slow - fast), 0, 1);
            sum += longGap ? 0 : 100 * Math.Pow(normalized, gamma);
            valid++;
        }
        return new(total, valid, valid == 0 ? 0 : sum / valid);
    }

    private static Source[] BuildCorpus()
    {
        string fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps");
        var sources = Fixtures.Select(name => new Source(Path.GetFileNameWithoutExtension(name), Path.Combine(fixtureDirectory, name))).Where(source => File.Exists(source.Path)).ToList();
        foreach (var item in StreamPatternGlobalForensicTests.Corpus)
            if (File.Exists(item.Path) && !sources.Any(source => source.Path == item.Path)) sources.Add(new(item.Title, item.Path));
        foreach (var item in TechV2SliderDensityCalibrationAudit.Corpus)
            if (File.Exists(item.Path) && !sources.Any(source => source.Path == item.Path)) sources.Add(new(item.Name, item.Path));
        foreach (Source control in Controls)
            if (File.Exists(control.Path) && !sources.Any(source => source.Path == control.Path)) sources.Add(control);
        return sources.ToArray();
    }

    private static string DisplayName(Row row) => string.IsNullOrWhiteSpace(row.Map.Title) ? row.Name : $"{row.Map.Title} [{row.Map.Version}]";
    private static string Cell(string value) => value.Replace("|", "\\|");
    private static double Median(IReadOnlyList<double> sorted) => sorted.Count == 0 ? 0 : sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
    private static double StdDev(IEnumerable<double> source) { double[] values = source.ToArray(); if (values.Length == 0) return 0; double mean = values.Average(); return Math.Sqrt(values.Average(value => Math.Pow(value - mean, 2))); }
    private static double Corr(IEnumerable<double> leftSource, IEnumerable<double> rightSource) { double[] left = leftSource.ToArray(), right = rightSource.ToArray(); double lm = left.Average(), rm = right.Average(); double numerator = left.Zip(right, (a, b) => (a - lm) * (b - rm)).Sum(); double denominator = Math.Sqrt(left.Sum(value => Math.Pow(value - lm, 2)) * right.Sum(value => Math.Pow(value - rm, 2))); return denominator == 0 ? 0 : numerator / denominator; }
    private static string FindRepoRoot() { DirectoryInfo? directory = new(AppContext.BaseDirectory); while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BeatInsight.slnx"))) directory = directory.Parent; return directory?.FullName ?? throw new DirectoryNotFoundException(); }

    private sealed record Source(string Name, string Path);
    private sealed record Simulation(int Total, int Valid, double Speed);
    private sealed record Row(string Name, string Path, Beatmap Map, GlobalTripletSpeedAnalysis Speed, double OldSpeed, string Identity, double RatingV2, double HistoricalRating);
}
