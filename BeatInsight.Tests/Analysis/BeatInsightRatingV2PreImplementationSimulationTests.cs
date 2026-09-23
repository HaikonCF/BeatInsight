using System.Diagnostics;
using System.IO;
using System.Text;
using BeatInsight.Analysis;
using BeatInsight.Analysis.Identity;
using BeatInsight.Analysis.Sections;
using BeatInsight.Analysis.SkillPressure;
using BeatInsight.Models;
using BeatInsight.Models.Fusion;
using BeatInsight.Models.Identity;
using BeatInsight.Models.Sections;
using BeatInsight.Models.SkillPressure;
using BeatInsight.Parser;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class BeatInsightRatingV2PreImplementationSimulationTests(ITestOutputHelper output)
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
    public void RealCorpusSimulatesCandidateFormulasBeforeRuntimeImplementation()
    {
        var stopwatch = Stopwatch.StartNew();
        Row[] rows = BuildCorpus().Select(Analyze).ToArray();
        stopwatch.Stop();
        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.Equal(row.StarRating, row.Map.OsuStarRating));

        var report = new StringBuilder();
        report.AppendLine("# BeatInsight Rating V2 — Pre-implementation simulation\n");
        report.AppendLine($"Test-only simulation on **{rows.Length} maps** and **{rows.Sum(row => row.Map.HitObjects.Count)} objects**. Runtime Rating V1 and every source analyzer are untouched. Total elapsed: **{stopwatch.Elapsed.TotalSeconds:F1}s**, mean **{stopwatch.Elapsed.TotalMilliseconds / rows.Length:F1}ms/map**.\n");
        AppendAudit(report);
        AppendBaseComparison(report, rows);
        AppendFormulaComparison(report, rows);
        AppendCorrelations(report, rows);
        AppendControls(report, rows);
        AppendSelection(report, rows);

        string path = Path.Combine(FindRepoRoot(), "Dataset", "Reports", "beatinsight-rating-v2-preimplementation-simulation.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, report.ToString(), Encoding.UTF8);
        output.WriteLine($"Rating V2 pre-implementation simulation: {path}");
    }

    private static void AppendAudit(StringBuilder report)
    {
        report.AppendLine("## AUDIT\n");
        report.AppendLine("|Component|Status|Existing source|Reusable|Risks|");
        report.AppendLine("|---|---|---|---|---|");
        report.AppendLine("|Base Difficulty|Existing signals|GameplayProfile Aim/Speed/Read/Tech|Yes|Correlated dimensions; avoid indiscriminate averaging|");
        report.AppendLine("|Skill Pressure|Frozen V2|SkillPressureProfile|Yes|AimControl/ReadingPressure/BurstControl overlap with base skills|");
        report.AppendLine("|Section Difficulty|Frozen V2|SectionAnalysisResult.LocalDifficulty|Yes|Already blends local skills, pressure, patterns and density|");
        report.AppendLine("|Difficulty Peaks|Existing|LocalDifficulty timeline and spikes|Yes|Raw maximum is outlier-sensitive|");
        report.AppendLine("|Sustained Difficulty|Derivable|Playable section timeline|Yes|Must distinguish plateau from one isolated peak|");
        report.AppendLine("|Pattern Complexity|Existing|Identity V3 MainPatterns grouped into Jump/Stream/Tech|Yes|Raw count would inflate the score|");
        report.AppendLine("|Length / Endurance|Existing inputs|Duration, Stamina, Endurance, sustained/repetition|Yes|Length alone is not difficulty|");
        report.AppendLine("|Specialization|Derivable|Fundamental skill distribution|Yes|Must remain a small capped bonus|");
        report.AppendLine("|Confidence|Existing confidences|Sections, Pressure, Identity, Patterns|Yes|Must not alter Score|");
        report.AppendLine("|Rating explanation|Missing|Structured contributors required|Yes|Avoid generated prose as data|");
        report.AppendLine("\nHistorical Rating V1 is `osu! Star Rating + bounded BeatInsight adjustment`; it remains available and unchanged. Burst exists as Stream Burst evidence and BurstControl pressure, not as an independent fourth pattern family.\n");
    }

    private static void AppendBaseComparison(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## BASE DIFFICULTY CANDIDATES\n");
        report.AppendLine("|Candidate|Definition|Mean|Median|Min|Max|StdDev|");
        report.AppendLine("|---|---|---:|---:|---:|---:|---:|");
        AppendStats(report, "A", "Mean(Aim, Speed, Read, Tech)", rows.Select(row => row.Components.BaseMean));
        AppendStats(report, "B", "0.50 Top1 + 0.30 Top2 + 0.20 Top3", rows.Select(row => row.Components.BaseTopWeighted));
        AppendStats(report, "C", "RMS(Aim, Speed, Read, Tech)", rows.Select(row => row.Components.BaseRms));
        AppendStats(report, "D", "0.70 weighted base + 0.30 pressure", rows.Select(row => row.Components.BasePressureBlend));
        report.AppendLine("\nCandidate B is preferred for runtime BaseDifficulty: it preserves specialization without consuming Skill Pressure twice. Candidate D is retained only as a comparison because pressure has its own global component.\n");
    }

    private static void AppendFormulaComparison(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## CANDIDATE FORMULAS\n");
        report.AppendLine("|Variant|Weights Base/Sustained/Peak/Pressure/Pattern/Endurance|Mean|Median|Min|Max|StdDev|<30|30-50|50-70|>=70|");
        report.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (string variant in new[] { "A", "B", "C", "D" })
        {
            double[] values = rows.Select(row => Variant(row.Components, variant)).Order().ToArray();
            string weights = variant switch
            {
                "A" => ".35/.25/.15/.10/.10/.05",
                "B" => ".25/.30/.20/.10/.10/.05",
                "C" => ".35/.20/.15/.15/.10/.05",
                _ => ".30/.25/.15/.12/.10/.08"
            };
            report.AppendLine($"|{variant}|{weights}|{values.Average():F2}|{Median(values):F2}|{values.Min():F2}|{values.Max():F2}|{StdDev(values):F2}|{values.Count(value => value < 30)}|{values.Count(value => value >= 30 && value < 50)}|{values.Count(value => value >= 50 && value < 70)}|{values.Count(value => value >= 70)}|");
        }
        report.AppendLine();
    }

    private static void AppendCorrelations(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## CORRELATIONS\n");
        report.AppendLine("Pearson correlations are descriptive; osu! Star Rating never enters a candidate formula.\n");
        report.AppendLine("|Variant|Aim|Speed|Read|Tech|Peak|Sustained|Pattern|Pressure|Endurance|Length|osu! SR|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (string variant in new[] { "A", "B", "C", "D" })
        {
            double[] rating = rows.Select(row => Variant(row.Components, variant)).ToArray();
            report.AppendLine($"|{variant}|{Corr(rating, rows.Select(row => row.Gameplay.AimScore)):F3}|{Corr(rating, rows.Select(row => row.Gameplay.SpeedScore)):F3}|{Corr(rating, rows.Select(row => row.Gameplay.ReadScore)):F3}|{Corr(rating, rows.Select(row => row.Gameplay.TechScore)):F3}|{Corr(rating, rows.Select(row => row.Components.Peak)):F3}|{Corr(rating, rows.Select(row => row.Components.Sustained)):F3}|{Corr(rating, rows.Select(row => row.Components.Pattern)):F3}|{Corr(rating, rows.Select(row => row.Components.Pressure)):F3}|{Corr(rating, rows.Select(row => row.Components.Endurance)):F3}|{Corr(rating, rows.Select(row => row.DurationSeconds)):F3}|{Corr(rating, rows.Select(row => row.StarRating)):F3}|");
        }
        report.AppendLine("\nComponent redundancy for selected D:\n");
        string[] names = ["Base", "Peak", "Sustained", "Pressure", "Pattern", "Endurance"];
        Func<Row, double>[] selectors =
        [
            row => row.Components.BaseTopWeighted, row => row.Components.Peak,
            row => row.Components.Sustained, row => row.Components.Pressure,
            row => row.Components.Pattern, row => row.Components.Endurance
        ];
        for (int left = 0; left < names.Length; left++)
            for (int right = left + 1; right < names.Length; right++)
            {
                double correlation = Corr(rows.Select(selectors[left]), rows.Select(selectors[right]));
                if (Math.Abs(correlation) >= .90)
                    report.AppendLine($"- {names[left]} vs {names[right]}: **{correlation:F3}** — inspect for overlap.");
            }
        report.AppendLine();
    }

    private static void AppendControls(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## CONTROL MAPS\n");
        string[] names = ["Tower Of Heaven", "Road of Resistance", "FREEDOM DiVE", "understand me", "Black Rover", "Wings of Courage", "Connect", "From Under Cover", "Exit This Earth", "Honesty"];
        report.AppendLine("|Map|Aim/Speed/Read/Tech|Mean/Peak/Sustained|Hard passages|Pressure|Pattern|Endurance|Specialization|A|B|C|D|SR|");
        report.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (string name in names)
        {
            Row? row = FindControl(rows, name);
            if (row is null) continue;
            Components c = row.Components;
            report.AppendLine($"|{Cell(row.Name)}|{row.Gameplay.AimScore:F0}/{row.Gameplay.SpeedScore:F0}/{row.Gameplay.ReadScore:F0}/{row.Gameplay.TechScore:F0}|{c.MeanSection:F1}/{c.Peak:F1}/{c.Sustained:F1}|{c.HardPassages}|{c.Pressure:F1}|{c.Pattern:F1}|{c.Endurance:F1}|{c.Specialization:F1}|{Variant(c, "A"):F1}|{Variant(c, "B"):F1}|{Variant(c, "C"):F1}|{Variant(c, "D"):F1}|{row.StarRating:F2}|");
        }
        report.AppendLine();
    }

    private static void AppendSelection(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## SELECTED FORMULA\n");
        report.AppendLine("Selected for implementation: **Variant D — robust hybrid** (`.30 Base + .25 Sustained + .15 Peak + .12 Pressure + .10 Pattern + .08 Endurance`, followed by a specialization bonus capped at 5 points). It gives sustained play more weight than an isolated maximum, keeps patterns moderate, and leaves enough explicit room for endurance/repetition without letting duration score by itself.");
        report.AppendLine("\nRejected: A underweights endurance/repetition; B is more section-driven and inherits more of LocalDifficulty's already blended inputs; C gives pressure extra weight despite its correlations with historical skills. Gap-free bounded formulas are preferred over corpus-fit thresholds.\n");
        double[] selected = rows.Select(row => Variant(row.Components, "D")).Order().ToArray();
        report.AppendLine($"Selected distribution: mean {selected.Average():F2}, median {Median(selected):F2}, min {selected.Min():F2}, max {selected.Max():F2}, stddev {StdDev(selected):F2}; saturation >=90: {selected.Count(value => value >= 90)}/{selected.Length}. Runtime implementation may proceed without map-specific tuning.\n");
    }

    private static Row Analyze(Source source)
    {
        Beatmap map = BeatmapParser.Load(source.Path);
        double starRating = map.OsuStarRating;
        GameplayProfile gameplay = GameplayAnalyzer.Analyze(map);
        SectionAnalysisResult sections = SectionAnalysisV2Analyzer.Analyze(map);
        SkillPressureProfile pressure = SkillPressureAnalyzer.Analyze(map);
        GameplayIdentityV3Profile identity = GameplayIdentityV3Analyzer.Analyze(map);
        double duration = map.HitObjects.Count < 2 ? 0 : (map.HitObjects[^1].Time - map.HitObjects[0].Time) / 1000.0;
        Components components = BuildComponents(gameplay, pressure, sections, identity, duration);
        return new(DisplayName(map, source.Name), source.Path, map, gameplay, pressure, sections, identity, duration, starRating, components);
    }

    private static Components BuildComponents(GameplayProfile gameplay, SkillPressureProfile pressure, SectionAnalysisResult sections, GameplayIdentityV3Profile identity, double durationSeconds)
    {
        double[] fundamentals = [Score(gameplay.AimScore), Score(gameplay.SpeedScore), Score(gameplay.ReadScore), Score(gameplay.TechScore)];
        double baseMean = fundamentals.Average();
        double[] ranked = fundamentals.OrderByDescending(value => value).ToArray();
        double baseTop = .50 * ranked[0] + .30 * ranked[1] + .20 * ranked[2];
        double baseRms = Math.Sqrt(fundamentals.Average(value => value * value));
        double pressureCore = WeightedTop(PressureMetrics(pressure).Select(metric => metric.Score!.Value));
        double baseBlend = .70 * baseTop + .30 * pressureCore;

        GameplaySectionV2Analysis[] playable = sections.Sections.Where(section => section.ObjectCount > 0).ToArray();
        double[] difficulty = playable.Select(section => Score(section.LocalDifficultyScore)).Order().ToArray();
        double mean = difficulty.Length == 0 ? 0 : difficulty.Average();
        double maximum = difficulty.Length == 0 ? 0 : difficulty.Max();
        double p90 = Percentile(difficulty, .90);
        double peak = .60 * p90 + .40 * maximum;
        double hardThreshold = Math.Max(30, peak * .75);
        bool[] hard = playable.Select(section => section.LocalDifficultyScore >= hardThreshold).ToArray();
        double hardRatio = hard.Length == 0 ? 0 : hard.Count(value => value) / (double)hard.Length;
        double hardMean = hard.Any(value => value)
            ? playable.Where((_, index) => hard[index]).Average(section => section.LocalDifficultyScore)
            : 0;
        int passages = CountClusters(hard);
        double repeated = passages <= 1 ? 0 : Signal(.60 * ((passages - 1) / 3.0) + .40 * hardRatio);
        double sustained = Score(.50 * mean + .30 * hardMean + .20 * hardRatio * 100);

        double[] families = identity.MainPatterns.GroupBy(pattern => pattern.Family)
            .Select(group => group.Max(pattern => pattern.Score)).OrderByDescending(value => value).ToArray();
        double pattern = families.Length == 0 ? 0
            : Score(.60 * families[0]
                + .30 * (families.Length > 1 ? families[1] : 0)
                + .10 * (families.Count(value => value >= 40) / 3.0) * 100);

        double stamina = Value(pressure.Stamina), endurancePressure = Value(pressure.Endurance);
        double durationSignal = Signal((durationSeconds - 90) / 270);
        double endurance = Score(100 * durationSignal
            * (.45 * ((.60 * endurancePressure + .40 * stamina) / 100)
                + .35 * sustained / 100
                + .20 * repeated));

        double othersMean = ranked.Skip(1).Average();
        double specialization = 5 * Signal((ranked[0] - 50) / 40) * Signal((ranked[0] - othersMean) / 40);
        return new(baseMean, baseTop, baseRms, baseBlend, mean, maximum, peak, sustained,
            hardRatio, passages, repeated, pressureCore, pattern, endurance, specialization);
    }

    private static double Variant(Components c, string variant)
    {
        double weighted = variant switch
        {
            "A" => .35 * c.BaseTopWeighted + .25 * c.Sustained + .15 * c.Peak + .10 * c.Pressure + .10 * c.Pattern + .05 * c.Endurance,
            "B" => .25 * c.BaseTopWeighted + .30 * c.Sustained + .20 * c.Peak + .10 * c.Pressure + .10 * c.Pattern + .05 * c.Endurance,
            "C" => .35 * c.BaseTopWeighted + .20 * c.Sustained + .15 * c.Peak + .15 * c.Pressure + .10 * c.Pattern + .05 * c.Endurance,
            _ => .30 * c.BaseTopWeighted + .25 * c.Sustained + .15 * c.Peak + .12 * c.Pressure + .10 * c.Pattern + .08 * c.Endurance
        };
        return Score(weighted + c.Specialization);
    }

    private static IEnumerable<SkillPressureMetric> PressureMetrics(SkillPressureProfile profile) =>
        new[] { profile.Stamina, profile.Endurance, profile.FingerControl, profile.BurstControl, profile.AimControl, profile.ReadingPressure, profile.RhythmComplexity }
            .Where(metric => metric.Available && metric.Score.HasValue && metric.Score.Value >= 30);

    private static double WeightedTop(IEnumerable<double> values)
    {
        double[] ranked = values.Select(Score).OrderByDescending(value => value).Take(3).ToArray();
        if (ranked.Length == 0) return 0;
        double[] weights = [.50, .30, .20];
        double weight = weights.Take(ranked.Length).Sum();
        return ranked.Select((value, index) => value * weights[index]).Sum() / weight;
    }

    private static int CountClusters(IReadOnlyList<bool> values)
    {
        int count = 0;
        for (int index = 0; index < values.Count; index++)
            if (values[index] && (index == 0 || !values[index - 1])) count++;
        return count;
    }

    private static Source[] BuildCorpus()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps");
        var sources = Fixtures.Select(name => new Source(Path.GetFileNameWithoutExtension(name), Path.Combine(directory, name)))
            .Where(source => File.Exists(source.Path)).ToList();
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

    private static void AppendStats(StringBuilder report, string candidate, string definition, IEnumerable<double> source)
    {
        double[] values = source.Order().ToArray();
        report.AppendLine($"|{candidate}|{definition}|{values.Average():F2}|{Median(values):F2}|{values.Min():F2}|{values.Max():F2}|{StdDev(values):F2}|");
    }

    private static double Corr(IEnumerable<double> leftSource, IEnumerable<double> rightSource)
    {
        double[] left = leftSource.ToArray(), right = rightSource.ToArray();
        if (left.Length == 0 || left.Length != right.Length) return 0;
        double leftMean = left.Average(), rightMean = right.Average();
        double numerator = left.Zip(right, (a, b) => (a - leftMean) * (b - rightMean)).Sum();
        double denominator = Math.Sqrt(left.Sum(value => Math.Pow(value - leftMean, 2)) * right.Sum(value => Math.Pow(value - rightMean, 2)));
        return denominator <= 0 ? 0 : numerator / denominator;
    }

    private static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        if (sorted.Count == 0) return 0;
        double position = (sorted.Count - 1) * Signal(percentile);
        int lower = (int)Math.Floor(position), upper = (int)Math.Ceiling(position);
        if (lower == upper) return sorted[lower];
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }

    private static double Median(IReadOnlyList<double> sorted) => sorted.Count == 0 ? 0 : sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
    private static double StdDev(IReadOnlyList<double> values) { if (values.Count == 0) return 0; double mean = values.Average(); return Math.Sqrt(values.Average(value => Math.Pow(value - mean, 2))); }
    private static double Value(SkillPressureMetric metric) => metric.Available && metric.Score.HasValue ? Score(metric.Score.Value) : 0;
    private static double Score(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;
    private static double Signal(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
    private static string DisplayName(Beatmap map, string fallback) => string.IsNullOrWhiteSpace(map.Title) ? fallback : $"{map.Title} [{map.Version}]";
    private static string Cell(string value) => value.Replace("|", "/");
    private static string FindRepoRoot() { DirectoryInfo? directory = new(AppContext.BaseDirectory); while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BeatInsight.slnx"))) directory = directory.Parent; return directory?.FullName ?? throw new DirectoryNotFoundException(); }

    private sealed record Source(string Name, string Path);
    private sealed record Row(string Name, string Path, Beatmap Map, GameplayProfile Gameplay, SkillPressureProfile Pressure, SectionAnalysisResult Sections, GameplayIdentityV3Profile Identity, double DurationSeconds, double StarRating, Components Components);
    private sealed record Components(double BaseMean, double BaseTopWeighted, double BaseRms, double BasePressureBlend, double MeanSection, double MaxSection, double Peak, double Sustained, double HighRatio, int HardPassages, double Repeated, double Pressure, double Pattern, double Endurance, double Specialization);
}
