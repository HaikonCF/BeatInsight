using System.IO;
using System.Text;
using BeatInsight.Models;
using BeatInsight.Parser;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class GlobalTripletSpeedPreImplementationSimulationTests(ITestOutputHelper output)
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

    [Fact]
    public void RealCorpusComparesMappingsGapAndSpinnerPoliciesBeforeRuntimeImplementation()
    {
        MapRow[] maps = BuildCorpus().Select(source =>
        {
            Beatmap map = BeatmapParser.Load(source.Path);
            return new MapRow(source.Name, map, map.HitObjects.ToArray());
        }).ToArray();
        Assert.NotEmpty(maps);

        var report = new StringBuilder();
        report.AppendLine("# Global Triplet Speed — pre-implementation simulation\n");
        report.AppendLine($"Corpus: **{maps.Length} maps**. Runtime remains untouched. Every result is a simple mean of equally weighted sliding triplets.\n");
        report.AppendLine("## Mapping grid (all positive triplets; circle + slider + spinner)\n");
        report.AppendLine("|Slow|Fast|Gamma|Mean|Median|Min|Max|StdDev|Zero maps|>=90 maps|");
        report.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (double slow in new[] { 200d, 250d, 300d })
            foreach (double fast in new[] { 40d, 50d, 60d })
                foreach (double gamma in new[] { .8, 1d, 1.2 })
                {
                    double[] values = maps.Select(map => Simulate(map.Objects, slow, fast, gamma, null, false, true).Speed).Order().ToArray();
                    report.AppendLine($"|{slow:F0}|{fast:F0}|{gamma:F1}|{values.Average():F2}|{Median(values):F2}|{values.Min():F2}|{values.Max():F2}|{StdDev(values):F2}|{values.Count(value => value < .0001)}|{values.Count(value => value >= 90)}|");
                }

        report.AppendLine("\n## Gap policy at linear 250/50\n");
        report.AppendLine("|Policy|Mean|Median|Min|Max|StdDev|Mean valid %|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
        GapRow(report, maps, "A all positive gaps", null, false);
        GapRow(report, maps, "B exclude >500 ms", 500, false);
        GapRow(report, maps, "C exclude >750 ms", 750, false);
        GapRow(report, maps, "D exclude >1000 ms", 1000, false);
        GapRow(report, maps, "E >750 ms contributes zero", 750, true);

        report.AppendLine("\n## Spinner policy at linear 250/50, exclude >750 ms\n");
        report.AppendLine("|Policy|Mean score|Mean valid triplets|Mean excluded triplets|Maps changed|Mean absolute delta|Max absolute delta|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
        Simulation[] withSpinner = maps.Select(map => Simulate(map.Objects, 250, 50, 1, 750, false, true)).ToArray();
        Simulation[] spinnerBreak = maps.Select(map => Simulate(map.Objects, 250, 50, 1, 750, false, false)).ToArray();
        SpinnerRow(report, "Include spinner", withSpinner, withSpinner);
        SpinnerRow(report, "Spinner breaks / is not measured", spinnerBreak, withSpinner);

        report.AppendLine("\n## Per-map comparison for selected candidate (linear 250/50, exclude >750 ms, spinner break)\n");
        report.AppendLine("|Map|Objects|Triplets|Valid|Excluded|Average interval ms|Speed %|Old Speed|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
        for (int index = 0; index < maps.Length; index++)
        {
            MapRow map = maps[index];
            Simulation result = spinnerBreak[index];
            report.AppendLine($"|{Cell(map.Name)}|{map.Objects.Length}|{result.Total}|{result.Valid}|{result.Excluded}|{result.AverageInterval:F2}|{result.Speed:F2}|{map.Map.GameplayProfile.SpeedScore:F2}|");
        }

        string path = Path.Combine(FindRepoRoot(), "Dataset", "Reports", "global-triplet-speed-preimplementation-simulation.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, report.ToString(), Encoding.UTF8);
        output.WriteLine($"Simulated {maps.Length} maps; report: {path}");
    }

    private static void GapRow(StringBuilder report, IReadOnlyList<MapRow> maps, string name, double? maximumGap, bool zeroLongGaps)
    {
        Simulation[] results = maps.Select(map => Simulate(map.Objects, 250, 50, 1, maximumGap, zeroLongGaps, true)).ToArray();
        double[] values = results.Select(result => result.Speed).Order().ToArray();
        report.AppendLine($"|{name}|{values.Average():F2}|{Median(values):F2}|{values.Min():F2}|{values.Max():F2}|{StdDev(values):F2}|{results.Average(result => result.Total == 0 ? 0 : 100d * result.Valid / result.Total):F2}|");
    }

    private static void SpinnerRow(StringBuilder report, string name, IReadOnlyList<Simulation> values, IReadOnlyList<Simulation> baseline)
    {
        double[] delta = values.Select((value, index) => Math.Abs(value.Speed - baseline[index].Speed)).ToArray();
        report.AppendLine($"|{name}|{values.Average(value => value.Speed):F2}|{values.Average(value => value.Valid):F2}|{values.Average(value => value.Excluded):F2}|{delta.Count(value => value > .0001)}|{delta.Average():F3}|{delta.Max():F3}|");
    }

    private static Simulation Simulate(IReadOnlyList<HitObject> objects, double slow, double fast, double gamma, double? maximumGap, bool zeroLongGaps, bool includeSpinner)
    {
        int total = Math.Max(0, objects.Count - 2), valid = 0;
        double speedSum = 0, intervalSum = 0;
        for (int index = 0; index + 2 < objects.Count; index++)
        {
            HitObject first = objects[index], second = objects[index + 1], third = objects[index + 2];
            if (!includeSpinner && (IsSpinner(first) || IsSpinner(second) || IsSpinner(third))) continue;
            double delta1 = second.Time - (double)first.Time, delta2 = third.Time - (double)second.Time;
            if (!double.IsFinite(delta1) || !double.IsFinite(delta2) || delta1 <= 0 || delta2 <= 0) continue;
            bool longGap = maximumGap.HasValue && (delta1 > maximumGap || delta2 > maximumGap);
            if (longGap && !zeroLongGaps) continue;
            double interval = (delta1 + delta2) / 2;
            double normalized = Math.Clamp((slow - interval) / (slow - fast), 0, 1);
            double localSpeed = longGap ? 0 : 100 * Math.Pow(normalized, gamma);
            valid++;
            speedSum += localSpeed;
            intervalSum += interval;
        }
        return new(total, valid, total - valid, valid == 0 ? 0 : speedSum / valid, valid == 0 ? 0 : intervalSum / valid);
    }

    private static bool IsSpinner(HitObject hitObject) => (hitObject.Type & 8) != 0;
    private static double Median(IReadOnlyList<double> sorted) => sorted.Count == 0 ? 0 : sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
    private static double StdDev(IEnumerable<double> source) { double[] values = source.ToArray(); if (values.Length == 0) return 0; double mean = values.Average(); return Math.Sqrt(values.Average(value => Math.Pow(value - mean, 2))); }
    private static string Cell(string value) => value.Replace("|", "\\|");

    private static Source[] BuildCorpus()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps");
        var sources = Fixtures.Select(name => new Source(Path.GetFileNameWithoutExtension(name), Path.Combine(directory, name))).Where(source => File.Exists(source.Path)).ToList();
        foreach (var item in StreamPatternGlobalForensicTests.Corpus)
            if (File.Exists(item.Path) && !sources.Any(source => source.Path == item.Path)) sources.Add(new(item.Title, item.Path));
        foreach (var item in TechV2SliderDensityCalibrationAudit.Corpus)
            if (File.Exists(item.Path) && !sources.Any(source => source.Path == item.Path)) sources.Add(new(item.Name, item.Path));
        return sources.ToArray();
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BeatInsight.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("BeatInsight repository root not found.");
    }

    private sealed record Source(string Name, string Path);
    private sealed record MapRow(string Name, Beatmap Map, HitObject[] Objects);
    private sealed record Simulation(int Total, int Valid, int Excluded, double Speed, double AverageInterval);
}
