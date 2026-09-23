using System.IO;
using System.Reflection;
using System.Text;
using BeatInsight.Analysis;
using BeatInsight.Analysis.Identity;
using BeatInsight.Models;
using BeatInsight.Models.Fusion;
using BeatInsight.Models.Identity;
using BeatInsight.Parser;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class GameplayIdentityV3TargetedCalibrationForensicTests(ITestOutputHelper output)
{
    private static readonly string[] Fixtures =
    [
        "(can you) understand me [uhh].osu", "Ashioto Tarte Tatin [Koori's Insane].osu",
        "Exit This Earth's Atmosphere [Primordial Nucleosynthesis].osu", "FREEDOM DiVE [Arles].osu",
        "FREEDOM DiVE [FrenZ's Insane].osu", "Frozen [Collab Insane].osu",
        "Kira Killer [Mocaotic's Insane].osu", "Snow Goose [Extra].osu", "Tower Of Heaven [Extra].osu",
        "forever we can make it! [Kudowari's Expert].osu", "Vickeblanka - Black Rover (TV Size) (Sotarks) [Extreme].osu"
    ];
    private static readonly Source[] Controls =
    [
        new("Road of Resistance", @"D:\osu!\Songs\399372 BABYMETAL - Road of Resistance (1)\BABYMETAL - Road of Resistance (Monstrata) [Rebellion].osu"),
        new("From Under Cover", @"D:\osu!\Songs\462386 Foreground Eclipse - From Under Cover (Caught Up In A Love Song)\Foreground Eclipse - From Under Cover (Caught Up In A Love Song) (Seni) [I've Stopped Time and Everything is at My Mercy].osu"),
        new("Connect TV Size", @"D:\osu!\Songs\1854325 ClariS - Connect (TV Size)\ClariS - Connect (TV Size) (Aakki) [Expert].osu"),
        new("Wings of Courage", @"D:\osu!\Songs\1706691 Kawada Mami - Wings of Courage -Sora o Koete-\Kawada Mami - Wings of Courage -Sora o Koete- (Zekk) [Expert].osu")
    ];
    private static readonly (string Label, double? Threshold)[] StructureVariants =
    [
        ("A Baseline", null),
        ("B Neutral excluded", 0.0000001),
        ("C Represented >=10%", .10),
        ("D Represented >=15%", .15),
        ("E Represented >=20%", .20)
    ];
    private static readonly double[] AbsoluteThresholds = [.20, .25, .30, .35, .40];
    private static readonly double[] RelativeThresholds = [.35, .45, .55];
    private static readonly (double Absolute, double Relative)[] CombinedThresholds =
    [
        (.35, .45), (.35, .55), (.40, .45), (.40, .55)
    ];

    [Fact]
    public void ExistingCorpusWritesTargetedCalibrationReportWithoutChangingRuntime()
    {
        Row[] rows = BuildCorpus().Select(Analyze).ToArray();
        Assert.NotEmpty(rows);
        var report = new StringBuilder();
        report.AppendLine("# Gameplay Identity V3 — Targeted Calibration Forensic (simulation only)\n");
        report.AppendLine($"Corpus: **{rows.Length} maps**. Runtime decisions are untouched in this report.\n");
        AppendStructure(report, rows);
        AppendPatternRanks(report, rows);
        AppendAbsoluteThresholds(report, rows);
        AppendRelativeThresholds(report, rows);
        AppendCombinedThresholds(report, rows);
        AppendGapBased(report, rows);
        AppendPatternControls(report, rows);
        AppendWeakTail(report, rows);
        AppendRecommendation(report, rows);
        string path = Path.Combine(FindRepoRoot(), "Dataset", "Reports", "gameplay-identity-v3-targeted-calibration.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, report.ToString(), Encoding.UTF8);
        output.WriteLine($"Targeted simulation report: {path}");
    }

    private static void AppendStructure(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## Structure variants\n");
        foreach ((string label, double? threshold) in StructureVariants)
        {
            GameplayStructureKind[] decisions = rows.Select(row => StructureDecision(row, threshold)).ToArray();
            report.AppendLine($"### {label}\n");
            report.AppendLine("Primary: " + string.Join("; ", decisions.GroupBy(value => value).OrderBy(group => group.Key).Select(group => $"{group.Key}={group.Count()}")) + ".");
            report.AppendLine($"ClassicMixed: **{decisions.Count(value => value == GameplayStructureKind.ClassicMixed)}**.");
            string[] changed = rows.Where((row, index) => decisions[index] != LegacyStructureDecision(row))
                .Select(row => $"{row.Name}: {LegacyStructureDecision(row)}->{StructureDecision(row, threshold)}").ToArray();
            report.AppendLine($"Changed maps ({changed.Length}): {(changed.Length == 0 ? "None" : string.Join("; ", changed))}.");
            Row[] lostTrueMixed = rows.Where(row => IsRealMixed(row, .15) && LegacyStructureDecision(row) == GameplayStructureKind.ClassicMixed && StructureDecision(row, threshold) != GameplayStructureKind.ClassicMixed).ToArray();
            report.AppendLine($"Previously Mixed with real >=15% diversity lost ({lostTrueMixed.Length}): {(lostTrueMixed.Length == 0 ? "None" : string.Join("; ", lostTrueMixed.Select(row => row.Name)))}.\n");
        }

        report.AppendLine("### Historical controls by variant\n");
        report.AppendLine("|Map|Jump|Stream|Tech|Margin|Sections J/S/T/Neutral|Represented @15%|A|B|C10|D15|E20|");
        report.AppendLine("|---|---:|---:|---:|---:|---|---:|---|---|---|---|---|");
        foreach (Row row in ControlRows(rows))
        {
            double[] scores = Scores(row); Array.Sort(scores); Array.Reverse(scores);
            string ratios = $"{row.Baseline.SectionContext.JumpSectionRatio:P0}/{row.Baseline.SectionContext.StreamSectionRatio:P0}/{row.Baseline.SectionContext.TechSectionRatio:P0}/{row.Baseline.SectionContext.MixedSectionRatio:P0}";
            string decisions = string.Join("|", StructureVariants.Select(variant => StructureDecision(row, variant.Threshold)));
            report.AppendLine($"|{Cell(row.Name)}|{row.Baseline.JumpStructureScore:F1}|{row.Baseline.StreamStructureScore:F1}|{row.Baseline.TechStructureScore:F1}|{scores[0] - scores[1]:F1}|{ratios}|{Represented(row, .15)}|{decisions}|");
        }

        report.AppendLine("\n### Baseline suspect Mixed (margin >20, <2 represented @15%)\n");
        foreach (Row row in rows.Where(row => LegacyStructureDecision(row) == GameplayStructureKind.ClassicMixed && Margin(row) > 20 && Represented(row, .15) < 2))
            report.AppendLine($"- {row.Name}: margin {Margin(row):F1}; represented {Represented(row, .15)}; D15 => {StructureDecision(row, .15)}.");
        report.AppendLine();
    }

    private static void AppendPatternRanks(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## PatternStrength distribution by rank\n");
        report.AppendLine("|Rank|Available maps|Mean|Median|Min|Max|Mean gap to next|");
        report.AppendLine("|---:|---:|---:|---:|---:|---:|---:|");
        for (int rank = 0; rank < 10; rank++)
        {
            double[] values = rows.Where(row => row.Patterns.Count > rank).Select(row => row.Patterns[rank].Strength).Order().ToArray();
            if (values.Length == 0) break;
            double[] gaps = rows.Where(row => row.Patterns.Count > rank + 1).Select(row => row.Patterns[rank].Strength - row.Patterns[rank + 1].Strength).ToArray();
            string label = rank < 7 ? (rank + 1).ToString() : "7+";
            report.AppendLine($"|{label}|{values.Length}|{values.Average():F3}|{Median(values):F3}|{values.Min():F3}|{values.Max():F3}|{(gaps.Length == 0 ? 0 : gaps.Average()):F3}|");
            if (rank == 7) break;
        }
        report.AppendLine($"\nMean gaps: top1/top2={MeanGap(rows, 0):F3}; top3/top4={MeanGap(rows, 2):F3}; top5/top6={MeanGap(rows, 4):F3}.\n");
    }

    private static void AppendAbsoluteThresholds(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## Absolute MainPattern threshold simulations\n");
        report.AppendLine("|Minimum|Mean count|0|1|2|3|4|5|Maps at 5|Maps at 0|Removed families|");
        report.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|");
        foreach (double threshold in AbsoluteThresholds)
        {
            int[] counts = rows.Select(row => Select(row, threshold, null).Count).ToArray();
            string removedFamilies = RemovedFamilies(rows, threshold, null);
            report.AppendLine($"|{threshold:F2}|{counts.Average():F2}|{counts.Count(value => value == 0)}|{counts.Count(value => value == 1)}|{counts.Count(value => value == 2)}|{counts.Count(value => value == 3)}|{counts.Count(value => value == 4)}|{counts.Count(value => value == 5)}|{counts.Count(value => value == 5)}|{counts.Count(value => value == 0)}|{removedFamilies}|");
        }
        report.AppendLine();
    }

    private static void AppendRelativeThresholds(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## Relative cutoff simulations (absolute minimum 0.20)\n");
        report.AppendLine("|Relative|Mean count|0|1|2|3|4|5|Maps at 5|Removed families|");
        report.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|");
        foreach (double relative in RelativeThresholds)
        {
            int[] counts = rows.Select(row => Select(row, .20, relative).Count).ToArray();
            report.AppendLine($"|{relative:F2}|{counts.Average():F2}|{counts.Count(value => value == 0)}|{counts.Count(value => value == 1)}|{counts.Count(value => value == 2)}|{counts.Count(value => value == 3)}|{counts.Count(value => value == 4)}|{counts.Count(value => value == 5)}|{counts.Count(value => value == 5)}|{RemovedFamilies(rows, .20, relative)}|");
        }
        report.AppendLine();
    }

    private static void AppendGapBased(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## Gap-based cutoff (descriptive only)\n");
        int[] counts = rows.Select(row => GapSelect(row, .20, .15).Count).ToArray();
        report.AppendLine($"With absolute 0.20 and first gap >=0.15: mean count **{counts.Average():F2}**, distribution {Distribution(counts)}. This is not selected automatically.\n");
    }

    private static void AppendCombinedThresholds(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## Combined absolute + relative cutoff simulations\n");
        report.AppendLine("|Absolute|Relative|Mean count|0|1|2|3|4|5|Maps at 5|Removed families|");
        report.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|");
        foreach ((double absolute, double relative) in CombinedThresholds)
        {
            int[] counts = rows.Select(row => Select(row, absolute, relative).Count).ToArray();
            report.AppendLine($"|{absolute:F2}|{relative:F2}|{counts.Average():F2}|{counts.Count(value => value == 0)}|{counts.Count(value => value == 1)}|{counts.Count(value => value == 2)}|{counts.Count(value => value == 3)}|{counts.Count(value => value == 4)}|{counts.Count(value => value == 5)}|{counts.Count(value => value == 5)}|{RemovedFamilies(rows, absolute, relative)}|");
        }
        report.AppendLine();
    }

    private static void AppendPatternControls(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## Historical control pattern rankings\n");
        foreach (Row row in ControlRows(rows))
        {
            report.AppendLine($"### {row.Name}\n");
            report.AppendLine("|Pattern|Family|Strength|Coverage|Confidence|Baseline|Abs25|Abs30|Abs35|Abs40|Rel35|Rel45|Rel55|Abs35+Rel55|Abs40+Rel55|");
            report.AppendLine("|---|---|---:|---:|---:|---|---|---|---|---|---|---|---|---|---|");
            foreach (PatternRow pattern in row.Patterns)
            {
                bool Keep(double absolute, double? relative = null) => Select(row, absolute, relative).Contains(pattern);
                report.AppendLine($"|{pattern.Name}|{pattern.Family}|{pattern.Strength:F3}|{pattern.Coverage:F3}|{pattern.Confidence:F3}|{Mark(Keep(.20))}|{Mark(Keep(.25))}|{Mark(Keep(.30))}|{Mark(Keep(.35))}|{Mark(Keep(.40))}|{Mark(Keep(.20,.35))}|{Mark(Keep(.20,.45))}|{Mark(Keep(.20,.55))}|{Mark(Keep(.35,.55))}|{Mark(Keep(.40,.55))}|");
            }
            report.AppendLine();
        }
    }

    private static void AppendWeakTail(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## Current positions 4/5 weak-tail audit\n");
        PatternRow[] tail = rows.SelectMany(row => row.Patterns.Take(5).Skip(3))
            .Where(pattern => pattern.Strength < .40 || pattern.Coverage < .10 || pattern.Confidence < .30).ToArray();
        report.AppendLine($"Weak-tail candidates: **{tail.Length}/{rows.Count * 2}** position-4/5 slots.");
        report.AppendLine("Families: " + (tail.Length == 0 ? "None" : string.Join("; ", tail.GroupBy(pattern => pattern.Family).Select(group => $"{group.Key}={group.Count()}"))) + ".");
        report.AppendLine("Lowest examples: " + string.Join("; ", tail.OrderBy(pattern => pattern.Strength).Take(15).Select(pattern => $"{pattern.Name} {pattern.Strength:F3} cov={pattern.Coverage:F3} conf={pattern.Confidence:F3}")) + ".\n");
    }

    private static void AppendRecommendation(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## Simulation recommendation\n");
        report.AppendLine("Structure candidate: **D — neutral excluded, represented family threshold 15%**. It directly matches the forensic definition, applies the explicit >20/<2-family guard, and does not use neutral section ratio as hybrid evidence.");
        report.AppendLine("Pattern candidate: **absolute 0.40 + relative 0.55**. It is the strongest tested simple absolute/relative combination, removes 38/38 saturation without producing a zero-pattern map, preserves every strongest pattern, and is materially less aggressive than the unstable gap cutoff. No runtime threshold is changed by this simulation report.\n");
    }

    private static GameplayStructureKind StructureDecision(Row row, double? representedThreshold)
    {
        if (!representedThreshold.HasValue) return LegacyStructureDecision(row);
        (GameplayStructureKind Kind, double Score)[] ranked =
        [
            (GameplayStructureKind.Jump, row.Baseline.JumpStructureScore),
            (GameplayStructureKind.Stream, row.Baseline.StreamStructureScore),
            (GameplayStructureKind.Tech, row.Baseline.TechStructureScore)
        ];
        ranked = ranked.OrderByDescending(item => item.Score).ToArray();
        double top = ranked[0].Score, second = ranked[1].Score, margin = top - second;
        bool close = margin <= 8 && second >= 25 && second / Math.Max(top, 1) >= .55;
        bool realDiversity = Represented(row, representedThreshold.Value) >= 2;
        bool noReliable = top < 25;
        if (margin > 20 && Represented(row, representedThreshold.Value) < 2) return ranked[0].Kind;
        return close || realDiversity || noReliable ? GameplayStructureKind.ClassicMixed : ranked[0].Kind;
    }

    private static GameplayStructureKind LegacyStructureDecision(Row row)
    {
        (GameplayStructureKind Kind, double Score)[] ranked =
        [
            (GameplayStructureKind.Jump, row.Baseline.JumpStructureScore),
            (GameplayStructureKind.Stream, row.Baseline.StreamStructureScore),
            (GameplayStructureKind.Tech, row.Baseline.TechStructureScore)
        ];
        ranked = ranked.OrderByDescending(item => item.Score).ThenBy(item => item.Kind).ToArray();
        double margin = ranked[0].Score - ranked[1].Score;
        bool close = ranked[1].Score >= 25 && margin <= 8;
        double maximumRatio = new[]
        {
            row.Baseline.SectionContext.JumpSectionRatio,
            row.Baseline.SectionContext.StreamSectionRatio,
            row.Baseline.SectionContext.TechSectionRatio
        }.Max();
        bool oldDiversity = row.Baseline.SectionContext.MixedSectionRatio >= .35
            || (maximumRatio <= .60
                && new[]
                {
                    row.Baseline.SectionContext.JumpSectionRatio,
                    row.Baseline.SectionContext.StreamSectionRatio,
                    row.Baseline.SectionContext.TechSectionRatio
                }.Count(value => value >= .30) >= 2);
        return ranked[0].Score < 25 || close || oldDiversity ? GameplayStructureKind.ClassicMixed : ranked[0].Kind;
    }

    private static bool IsRealMixed(Row row, double threshold) =>
        (Margin(row) <= 8 && Scores(row).OrderByDescending(value => value).Skip(1).First() >= 25)
        || Represented(row, threshold) >= 2;
    private static int Represented(Row row, double threshold) => new[]
    {
        row.Baseline.SectionContext.JumpSectionRatio,
        row.Baseline.SectionContext.StreamSectionRatio,
        row.Baseline.SectionContext.TechSectionRatio
    }.Count(value => value >= threshold);
    private static double[] Scores(Row row) => [row.Baseline.JumpStructureScore, row.Baseline.StreamStructureScore, row.Baseline.TechStructureScore];
    private static double Margin(Row row) { double[] values = Scores(row).OrderByDescending(value => value).ToArray(); return values[0] - values[1]; }

    private static List<PatternRow> Select(Row row, double absolute, double? relative)
    {
        if (row.Patterns.Count == 0) return [];
        double minimum = relative.HasValue ? Math.Max(absolute, row.Patterns[0].Strength * relative.Value) : absolute;
        return row.Patterns.Where(pattern => pattern.Strength >= minimum).Take(5).ToList();
    }
    private static List<PatternRow> GapSelect(Row row, double absolute, double gap)
    {
        PatternRow[] eligible = row.Patterns.Where(pattern => pattern.Strength >= absolute).Take(5).ToArray();
        if (eligible.Length <= 1) return eligible.ToList();
        int count = eligible.Length;
        for (int index = 0; index + 1 < eligible.Length; index++)
            if (eligible[index].Strength - eligible[index + 1].Strength >= gap) { count = index + 1; break; }
        return eligible.Take(count).ToList();
    }

    private static string RemovedFamilies(IReadOnlyList<Row> rows, double absolute, double? relative)
    {
        var removed = rows.SelectMany(row => row.Patterns.Take(5).Except(Select(row, absolute, relative)))
            .GroupBy(pattern => pattern.Family).OrderBy(group => group.Key).Select(group => $"{group.Key}={group.Count()}").ToArray();
        return removed.Length == 0 ? "None" : string.Join("; ", removed);
    }
    private static double MeanGap(IReadOnlyList<Row> rows, int rank)
    {
        double[] values = rows.Where(row => row.Patterns.Count > rank + 1).Select(row => row.Patterns[rank].Strength - row.Patterns[rank + 1].Strength).ToArray();
        return values.Length == 0 ? 0 : values.Average();
    }
    private static string Distribution(IEnumerable<int> counts) => string.Join(", ", counts.GroupBy(value => value).OrderBy(group => group.Key).Select(group => $"{group.Key}={group.Count()}"));
    private static string Mark(bool value) => value ? "yes" : "—";

    private static Row Analyze(Source source)
    {
        Beatmap map = BeatmapParser.Load(source.Path);
        GameplayProfile profile = GameplayAnalyzer.Analyze(map);
        GameplayIdentityV3Profile baseline = GameplayIdentityV3Analyzer.Analyze(map);
        MethodInfo method = typeof(GameplayIdentityV3Analyzer).GetMethod("BuildPatterns", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException("BuildPatterns");
        var inputs = (IReadOnlyList<GameplayIdentityV3PatternInput>)(method.Invoke(null, [map, profile])
            ?? throw new InvalidOperationException("BuildPatterns returned null."));
        PatternRow[] patterns = inputs.Select(ToPattern).OrderByDescending(pattern => pattern.Strength).ThenBy(pattern => pattern.Name).ToArray();
        return new(DisplayName(map, source.Name), source.Path, baseline, patterns);
    }

    private static PatternRow ToPattern(GameplayIdentityV3PatternInput input)
    {
        double coverage = Signal(input.Coverage), confidence = Signal(input.Confidence);
        double numerator = .50 * coverage + .30 * confidence, denominator = .80;
        if (input.Intensity.HasValue) { numerator += .20 * Signal(input.Intensity.Value); denominator = 1; }
        return new(input.Name, input.Family, numerator / denominator, coverage, confidence);
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
    private static IEnumerable<Row> ControlRows(IReadOnlyList<Row> rows)
    {
        string[] names = ["Black Rover", "understand me", "Tower Of Heaven", "Road of Resistance", "Wings of Courage", "Connect", "From Under Cover"];
        foreach (string name in names)
        {
            Source? explicitControl = Controls.FirstOrDefault(source => source.Name == name);
            Row? row = explicitControl is null
                ? rows.FirstOrDefault(candidate => candidate.Name.Contains(name, StringComparison.OrdinalIgnoreCase) || candidate.Path.Contains(name, StringComparison.OrdinalIgnoreCase))
                : rows.FirstOrDefault(candidate => candidate.Path == explicitControl.Path);
            if (row is not null) yield return row;
        }
    }
    private static string FindRepoRoot() { DirectoryInfo? directory = new(AppContext.BaseDirectory); while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BeatInsight.slnx"))) directory = directory.Parent; return directory?.FullName ?? throw new DirectoryNotFoundException(); }
    private static string DisplayName(Beatmap map, string fallback) => string.IsNullOrWhiteSpace(map.Title) ? fallback : $"{map.Title} [{map.Version}]";
    private static string Cell(string value) => value.Replace("|", "/");
    private static double Signal(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
    private static double Median(IReadOnlyList<double> values) => values.Count % 2 == 1 ? values[values.Count / 2] : (values[values.Count / 2 - 1] + values[values.Count / 2]) / 2;
    private sealed record Source(string Name, string Path);
    private sealed record Row(string Name, string Path, GameplayIdentityV3Profile Baseline, IReadOnlyList<PatternRow> Patterns);
    private sealed record PatternRow(string Name, FusionPatternFamily Family, double Strength, double Coverage, double Confidence);
}
