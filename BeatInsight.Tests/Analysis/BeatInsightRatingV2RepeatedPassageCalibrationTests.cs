using System.IO;
using System.Text;
using BeatInsight.Analysis.Sections;
using BeatInsight.Models;
using BeatInsight.Models.Sections;
using BeatInsight.Parser;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class BeatInsightRatingV2RepeatedPassageCalibrationTests(ITestOutputHelper output)
{
    private static readonly string[] Fixtures =
    [
        "(can you) understand me [uhh].osu", "Ashioto Tarte Tatin [Koori's Insane].osu",
        "Exit This Earth's Atmosphere [Primordial Nucleosynthesis].osu", "FREEDOM DiVE [Arles].osu",
        "FREEDOM DiVE [FrenZ's Insane].osu", "Frozen [Collab Insane].osu", "Kira Killer [Mocaotic's Insane].osu",
        "Snow Goose [Extra].osu", "Tower Of Heaven [Extra].osu", "forever we can make it! [Kudowari's Expert].osu",
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
    public void RealCorpusSimulatesRepeatedPassageCalibrationBeforeRuntimeChange()
    {
        Row[] rows = BuildCorpus().Select(Analyze).ToArray();
        Assert.NotEmpty(rows);
        var report = new StringBuilder();
        report.AppendLine("# BeatInsight Rating V2 — Repeated-passage targeted calibration\n");
        report.AppendLine("Test-only simulation after the first runtime forensic showed current `RepeatedHardPassages` on 35/38 maps. No runtime threshold is changed by this report.\n");
        report.AppendLine("|Variant|Rule|Mean passages|0|1|2|3+|Repeated (>=2)|");
        report.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");
        foreach ((string name, string rule, Func<bool[], int> count) in Variants())
        {
            int[] values = rows.Select(row => count(row.Hard)).ToArray();
            report.AppendLine($"|{name}|{rule}|{values.Average():F2}|{values.Count(value => value == 0)}|{values.Count(value => value == 1)}|{values.Count(value => value == 2)}|{values.Count(value => value >= 3)}|{values.Count(value => value >= 2)}|");
        }
        report.AppendLine("\n## Controls\n");
        report.AppendLine("|Map|Hard windows|A current|B min2|C merge gap1|D merge gap1 + min2|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach (string name in new[] { "Tower Of Heaven", "Road of Resistance", "FREEDOM DiVE", "understand me", "Black Rover", "Wings", "Connect", "From Under Cover", "Exit This Earth" })
        {
            Row? row = FindControl(rows, name);
            if (row is null) continue;
            int[] values = Variants().Select(variant => variant.Count(row.Hard)).ToArray();
            report.AppendLine($"|{row.Name.Replace("|", "/")}|{row.Hard.Count(value => value)}|{values[0]}|{values[1]}|{values[2]}|{values[3]}|");
        }
        report.AppendLine("\n## Recommendation\n");
        report.AppendLine("Select **D — merge one-window gaps, then require at least two hard windows per passage**. A 5-second threshold flicker no longer creates separate passages; a passage represents at least 10 seconds of hard play. PeakDifficulty still preserves isolated extremes, while SustainedDifficulty still preserves broad plateaus. This changes only Rating V2 passage/repetition and its conditional Endurance component.\n");
        string path = Path.Combine(FindRepoRoot(), "Dataset", "Reports", "beatinsight-rating-v2-repeated-passage-calibration.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, report.ToString(), Encoding.UTF8);
        output.WriteLine(path);
    }

    private static IEnumerable<(string Name, string Rule, Func<bool[], int> Count)> Variants()
    {
        yield return ("A", "Every raw hard cluster", values => Count(values, false, 1));
        yield return ("B", "Raw clusters, minimum 2 windows", values => Count(values, false, 2));
        yield return ("C", "Merge gaps <=1 window", values => Count(values, true, 1));
        yield return ("D", "Merge gaps <=1, minimum 2 hard windows", values => Count(values, true, 2));
    }

    private static int Count(bool[] hard, bool mergeOneGap, int minimumHardWindows)
    {
        int passages = 0, index = 0;
        while (index < hard.Length)
        {
            while (index < hard.Length && !hard[index]) index++;
            if (index >= hard.Length) break;
            int hardCount = 0;
            while (index < hard.Length)
            {
                if (hard[index]) { hardCount++; index++; continue; }
                if (mergeOneGap && index + 1 < hard.Length && hard[index + 1]) { index++; continue; }
                break;
            }
            if (hardCount >= minimumHardWindows) passages++;
        }
        return passages;
    }

    private static Row Analyze(Source source)
    {
        Beatmap map = BeatmapParser.Load(source.Path);
        SectionAnalysisResult sections = SectionAnalysisV2Analyzer.Analyze(map);
        GameplaySectionV2Analysis[] playable = sections.Sections.Where(section => section.ObjectCount > 0).ToArray();
        double[] sorted = playable.Select(section => Math.Clamp(section.LocalDifficultyScore, 0, 100)).Order().ToArray();
        double max = sorted.Length == 0 ? 0 : sorted.Max();
        double peak = .60 * Percentile(sorted, .90) + .40 * max;
        double threshold = Math.Max(30, peak * .75);
        return new(DisplayName(map, source.Name), source.Path, playable.Select(section => section.LocalDifficultyScore >= threshold).ToArray());
    }

    private static Source[] BuildCorpus()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps");
        var sources = Fixtures.Select(name => new Source(Path.GetFileNameWithoutExtension(name), Path.Combine(directory, name))).Where(source => File.Exists(source.Path)).ToList();
        foreach (var map in StreamPatternGlobalForensicTests.Corpus)
            if (File.Exists(map.Path) && !sources.Any(source => source.Path == map.Path)) sources.Add(new(map.Title, map.Path));
        foreach (var map in TechV2SliderDensityCalibrationAudit.Corpus)
            if (File.Exists(map.Path) && !sources.Any(source => source.Path == map.Path)) sources.Add(new(map.Name, map.Path));
        foreach (Source control in Controls)
            if (File.Exists(control.Path) && !sources.Any(source => source.Path == control.Path)) sources.Add(control);
        return sources.ToArray();
    }

    private static Row? FindControl(IReadOnlyList<Row> rows, string name)
    {
        Source? explicitControl = Controls.FirstOrDefault(source => source.Name == name);
        return explicitControl is null
            ? rows.FirstOrDefault(row => row.Name.Contains(name, StringComparison.OrdinalIgnoreCase) || row.Path.Contains(name, StringComparison.OrdinalIgnoreCase))
            : rows.FirstOrDefault(row => row.Path == explicitControl.Path);
    }
    private static double Percentile(IReadOnlyList<double> sorted, double p) { if (sorted.Count == 0) return 0; double position = (sorted.Count - 1) * Math.Clamp(p, 0, 1); int lower = (int)Math.Floor(position), upper = (int)Math.Ceiling(position); return lower == upper ? sorted[lower] : sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower); }
    private static string DisplayName(Beatmap map, string fallback) => string.IsNullOrWhiteSpace(map.Title) ? fallback : $"{map.Title} [{map.Version}]";
    private static string FindRepoRoot() { DirectoryInfo? directory = new(AppContext.BaseDirectory); while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BeatInsight.slnx"))) directory = directory.Parent; return directory?.FullName ?? throw new DirectoryNotFoundException(); }
    private sealed record Source(string Name, string Path);
    private sealed record Row(string Name, string Path, bool[] Hard);
}
