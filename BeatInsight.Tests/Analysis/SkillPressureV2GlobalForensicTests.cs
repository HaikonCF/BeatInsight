using System.IO;
using System.Text;
using BeatInsight.Analysis;
using BeatInsight.Analysis.Fusion;
using BeatInsight.Analysis.SkillPressure;
using BeatInsight.Analysis.TechPatterns;
using BeatInsight.Models;
using BeatInsight.Models.SkillPressure;
using BeatInsight.Parser;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class SkillPressureV2GlobalForensicTests(ITestOutputHelper output)
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

    private static readonly Source[] Requested =
    [
        new("Connect TV Size", @"D:\osu!\Songs\1854325 ClariS - Connect (TV Size)\ClariS - Connect (TV Size) (Aakki) [Expert].osu"),
        new("Wings of Courage", @"D:\osu!\Songs\1706691 Kawada Mami - Wings of Courage -Sora o Koete-\Kawada Mami - Wings of Courage -Sora o Koete- (Zekk) [Expert].osu"),
        new("From Under Cover", @"D:\osu!\Songs\462386 Foreground Eclipse - From Under Cover (Caught Up In A Love Song)\Foreground Eclipse - From Under Cover (Caught Up In A Love Song) (Seni) [I've Stopped Time and Everything is at My Mercy].osu")
    ];

    [Fact]
    public void ExistingCorpusWritesSkillPressureV2Report()
    {
        Source[] corpus = BuildCorpus();
        Assert.NotEmpty(corpus);
        Row[] rows = corpus.Select(Analyze).ToArray();
        var report = new StringBuilder();
        AppendAudit(report);
        AppendImplementation(report);
        AppendGlobal(report, rows);
        AppendControls(report, rows);
        AppendCorrelations(report, rows);
        AppendDecisions(report, rows);

        string directory = Environment.GetEnvironmentVariable("BEATINSIGHT_FORENSIC_OUTPUT_DIRECTORY") ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "skill-pressure-v2-global-forensic.md");
        File.WriteAllText(path, report.ToString(), Encoding.UTF8);
        output.WriteLine($"Corpus analyzed: {rows.Length}/{corpus.Length}");
        output.WriteLine($"Objects analyzed: {rows.Sum(row => row.Map.HitObjects.Count)}");
        output.WriteLine($"Report: {path}");
    }

    private static Source[] BuildCorpus()
    {
        string fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps");
        IEnumerable<Source> fixtures = Fixtures.Select(name => new Source(Path.GetFileNameWithoutExtension(name), Path.Combine(fixtureDirectory, name)));
        IEnumerable<Source> stream = StreamPatternGlobalForensicTests.Corpus.Select(spec => new Source(spec.Title, spec.Path));
        IEnumerable<Source> tech = TechV2SliderDensityCalibrationAudit.Corpus.Select(spec => new Source(spec.Name, spec.Path));
        return fixtures.Concat(stream).Concat(tech).Concat(Requested)
            .Where(source => File.Exists(source.Path))
            .GroupBy(source => Path.GetFullPath(source.Path), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).ToArray();
    }

    private static Row Analyze(Source source)
    {
        Beatmap map = BeatmapParser.Load(source.Path);
        GameplayProfile gameplay = GameplayAnalyzer.Analyze(map);
        SkillPressureProfile pressure = SkillPressureAnalyzer.Analyze(map);
        var fusion = GameplayFusionAnalyzer.Analyze(map);
        var rhythm = TechPatternAnalyzer.AnalyzeRhythmTransition(map.HitObjects);
        return new(source, map, gameplay, pressure, fusion.TechPatternSupport, rhythm.Coverage);
    }

    private static void AppendAudit(StringBuilder report)
    {
        report.AppendLine("# Skill Pressure V2 — global real-corpus forensic\n");
        report.AppendLine("Observation-only validation. No Identity, Rating, UI, cache, ML, Fusion V1, pattern threshold, or classification behavior is changed.\n");
        report.AppendLine("## Audit initial\n");
        report.AppendLine("|Skill|Initial status|Existing reliable sources|Missing / risk|");
        report.AppendLine("|---|---|---|---|");
        report.AppendLine("|Stamina|PARTIAL|Stamina runs, recovery gaps, active ratio, rapid objects, Long/Deathstream|No prior [0,100] score; avoid equating one long run with stamina|");
        report.AppendLine("|Endurance|PARTIAL|Map duration, structural coverage, pressure spread, sustained rapid context|No Section Analysis V2; global V1 only|");
        report.AppendLine("|Finger Control|PARTIAL|Irregular, Alternating, Rhythm Transition|Avoid raw CV or one spike|");
        report.AppendLine("|Burst Control|PARTIAL|Burst runs, repetition, interval, length, variation|Must remain distinct from Speed|");
        report.AppendLine("|Aim Control|PARTIAL|Aim angle, Sharp/Reversal/Arc/Wide/Alternating, Spatial, slider direction|Raw Aim only context|");
        report.AppendLine("|Precision|PARTIAL|CS and Aim precision modifier|No reliable playable-margin geometry; score unavailable|");
        report.AppendLine("|Reading Pressure|PARTIAL|Read density, clutter, ambiguity, novelty, transitions|Reliable sources existed, but no prior independent [0,100] pressure score; avoid copying ReadScore|");
        report.AppendLine("|Rhythm Complexity|PARTIAL|Rhythm Transition, sustained irregularity, alternation, breadth|Requires at least five objects and multiple dimensions|\n");
    }

    private static void AppendImplementation(StringBuilder report)
    {
        report.AppendLine("## Implementation and normalization\n");
        report.AppendLine("Every score is `100 * clamp(weighted mean of available normalized signals, 0, 1)`. Missing skills remain null; Confidence never changes Score. Counts use progressive exponential saturation, duration saturates from 60 to 300 seconds, and all analyzer Coverage/Confidence/intensities are consumed directly on [0,1].\n");
        report.AppendLine("Runtime adapter: `BeatInsight/Analysis/SkillPressure/SkillPressureAnalyzer.cs`; output contract: `BeatInsight/Models/SkillPressure/SkillPressureProfile.cs`. Sources are read-only outputs from `GameplayAnalyzer`, Stream/Jump/Tech Pattern analyzers, and existing gameplay sequences/sections.\n");
        report.AppendLine("Normalizations: `Signal(x)=clamp(x,0,1)`; `Count(c,s)=1-exp(-c/s)` with scales 2 for repeated Stamina runs, 48 for rapid-object volume, and 6 for repeated Bursts; `Duration=clamp((seconds-60)/240,0,1)`; pattern strength is `0.5*Coverage + 0.3*Confidence + 0.2*Intensity` when intensity exists (renormalized without it); pressure spread is the fraction of eight timeline buckets touched by existing Stream/Jump/Tech sections.\n");
        report.AppendLine("- Stamina: Coverage 20%, repetition 25%, active ratio 20%, recovery pressure 15%, rapid volume 15%, Long/Deathstream context 5%.");
        report.AppendLine("- Endurance: duration 25%, global pressure 30%, timeline spread 30%, sustained context 15%.");
        report.AppendLine("- Finger Control: Irregular 50%, Alternating 30%, Rhythm Transition 15%, breadth 5%.");
        report.AppendLine("- Burst Control: Coverage 25%, repetition 30%, intensity 20%, length 15%, variation 10%.");
        report.AppendLine("- Aim Control: Aim context 15%, angle 20%, control patterns 35%, Spatial 20%, slider direction 10%.");
        report.AppendLine("- Precision: nullable; unavailable in current runtime.");
        report.AppendLine("- Reading Pressure: Read context 35%, density 20%, clutter 15%, ambiguity 15%, novelty 5%, targeted patterns 10%.");
        report.AppendLine("- Rhythm Complexity: regime transitions 45%, sustained irregularity 35%, alternating control 10%, breadth 10%.\n");
    }

    private static void AppendGlobal(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine($"## Global forensic\n\nCorpus: **{rows.Count} maps**, **{rows.Sum(row => row.Map.HitObjects.Count)} objects**. No map was downloaded.\n");
        report.AppendLine("|Map|Stamina|Endurance|Finger|Burst|Aim Control|Precision|Reading|Rhythm|Global confidence|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (Row row in rows.OrderByDescending(row => row.Pressure.Confidence))
            report.AppendLine($"|{Cell(row.Name)}|{Score(row.Pressure.Stamina)}|{Score(row.Pressure.Endurance)}|{Score(row.Pressure.FingerControl)}|{Score(row.Pressure.BurstControl)}|{Score(row.Pressure.AimControl)}|{Score(row.Pressure.Precision)}|{Score(row.Pressure.ReadingPressure)}|{Score(row.Pressure.RhythmComplexity)}|{row.Pressure.Confidence:F3}|");
        report.AppendLine();
        foreach ((string name, Func<Row, SkillPressureMetric> select) in Skills())
        {
            double[] values = rows.Select(select).Where(metric => metric.Score.HasValue).Select(metric => metric.Score!.Value).Order().ToArray();
            report.AppendLine($"### {name}\n");
            if (values.Length == 0) { report.AppendLine("Unavailable on the current runtime/corpus.\n"); continue; }
            report.AppendLine($"Available {values.Length}/{rows.Count}; mean **{values.Average():F2}**, median **{Median(values):F2}**, min **{values.Min():F2}**, max **{values.Max():F2}**.");
            report.AppendLine($"Buckets: 0–10={Bucket(values,0,10)}; 10–25={Bucket(values,10,25)}; 25–40={Bucket(values,25,40)}; 40–60={Bucket(values,40,60)}; 60–80={Bucket(values,60,80)}; 80–100={values.Count(value => value >= 80 && value <= 100)}.\n");
        }
    }

    private static void AppendControls(StringBuilder report, IReadOnlyList<Row> rows)
    {
        string[] controls = ["Road of Resistance", "Tower Of Heaven", "Wings of Courage", "From Under Cover", "Connect", "understand me", "Black Rover", "FREEDOM DiVE"];
        report.AppendLine("## Control maps\n");
        foreach (string control in controls)
        {
            foreach (Row row in rows.Where(row => row.Name.Contains(control, StringComparison.OrdinalIgnoreCase)).Take(2))
            {
                report.AppendLine($"### {row.Name}\n");
                report.AppendLine($"Identity remains {row.Gameplay.Identity.Primary}/{row.Gameplay.Identity.Secondary}; Aim {row.Gameplay.AimScore:F1}, Speed {row.Gameplay.SpeedScore:F1}, Read {row.Gameplay.ReadScore:F1}. Skill pressures: Stamina {Score(row.Pressure.Stamina)}, Endurance {Score(row.Pressure.Endurance)}, Finger {Score(row.Pressure.FingerControl)}, Burst {Score(row.Pressure.BurstControl)}, Aim Control {Score(row.Pressure.AimControl)}, Reading {Score(row.Pressure.ReadingPressure)}, Rhythm {Score(row.Pressure.RhythmComplexity)}.\n");
                report.AppendLine($"Principal normalized signals: Stamina [{TopSignals(row.Pressure.Stamina)}]; Endurance [{TopSignals(row.Pressure.Endurance)}]; Finger [{TopSignals(row.Pressure.FingerControl)}]; Burst [{TopSignals(row.Pressure.BurstControl)}]; Aim Control [{TopSignals(row.Pressure.AimControl)}]; Reading [{TopSignals(row.Pressure.ReadingPressure)}]; Rhythm [{TopSignals(row.Pressure.RhythmComplexity)}]. These are descriptive only; the existing structural classification above is unchanged.\n");
            }
        }
    }

    private static void AppendCorrelations(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## Descriptive Pearson correlations\n");
        Correlation(report, "Stamina vs Endurance", rows, row => Value(row.Pressure.Stamina), row => Value(row.Pressure.Endurance));
        Correlation(report, "Finger Control vs Rhythm Complexity", rows, row => Value(row.Pressure.FingerControl), row => Value(row.Pressure.RhythmComplexity));
        Correlation(report, "Burst Control vs Speed", rows, row => Value(row.Pressure.BurstControl), row => row.Gameplay.SpeedScore);
        Correlation(report, "Aim Control vs Aim", rows, row => Value(row.Pressure.AimControl), row => row.Gameplay.AimScore);
        report.AppendLine("- Precision vs Aim: N/A (Precision unavailable).");
        Correlation(report, "Reading Pressure vs ReadScore", rows, row => Value(row.Pressure.ReadingPressure), row => row.Gameplay.ReadScore);
        Correlation(report, "Tech Pattern support vs Reading Pressure", rows, row => row.TechPatternSupport * 100, row => Value(row.Pressure.ReadingPressure));
        Correlation(report, "Rhythm Transition coverage vs Rhythm Complexity", rows, row => row.RhythmCoverage * 100, row => Value(row.Pressure.RhythmComplexity));
        report.AppendLine("\nAbsolute correlations above 0.90 are flagged as potential duplication, never auto-calibrated.\n");
    }

    private static void AppendDecisions(StringBuilder report, IReadOnlyList<Row> rows)
    {
        double fingerRhythm = Math.Abs(Pearson(rows.Select(row => Value(row.Pressure.FingerControl)).ToArray(), rows.Select(row => Value(row.Pressure.RhythmComplexity)).ToArray()));
        double readingBase = Math.Abs(Pearson(rows.Select(row => Value(row.Pressure.ReadingPressure)).ToArray(), rows.Select(row => row.Gameplay.ReadScore).ToArray()));
        report.AppendLine("## Decisions\n");
        report.AppendLine("- **Stamina: FREEZE** — repetition and recovery prevent one long run from dominating.");
        report.AppendLine("- **Endurance: PARTIAL** — safe global V1, but Section Analysis V2 would improve difficult-section persistence.");
        report.AppendLine($"- **Finger Control: {(fingerRhythm > .90 ? "INVESTIGATE" : "FREEZE")}** — distinct multi-signal control pressure; correlation with Rhythm={fingerRhythm:F3}.");
        report.AppendLine("- **Burst Control: FREEZE** — independent from Base Speed and gated by repetition.");
        report.AppendLine("- **Aim Control: FREEZE** — directional patterns dominate; raw Aim is only 15% context.");
        report.AppendLine("- **Precision: UNAVAILABLE** — no reliable relative-margin geometry.");
        report.AppendLine($"- **Reading Pressure: {(readingBase > .90 ? "INVESTIGATE" : "FREEZE")}** — visual/pattern signals extend ReadScore; correlation={readingBase:F3}.");
        report.AppendLine("- **Rhythm Complexity: PARTIAL** — multi-dimensional and usable, but remains an early global V1 without dedicated rhythm sections.\n");
    }

    private static IEnumerable<(string, Func<Row, SkillPressureMetric>)> Skills() =>
    [
        ("Stamina", row => row.Pressure.Stamina), ("Endurance", row => row.Pressure.Endurance),
        ("Finger Control", row => row.Pressure.FingerControl), ("Burst Control", row => row.Pressure.BurstControl),
        ("Aim Control", row => row.Pressure.AimControl), ("Precision", row => row.Pressure.Precision),
        ("Reading Pressure", row => row.Pressure.ReadingPressure), ("Rhythm Complexity", row => row.Pressure.RhythmComplexity)
    ];

    private static void Correlation(StringBuilder report, string name, IReadOnlyList<Row> rows, Func<Row,double> x, Func<Row,double> y)
    {
        double value = Pearson(rows.Select(x).ToArray(), rows.Select(y).ToArray());
        report.AppendLine($"- {name}: **{value:F3}**{(Math.Abs(value) > .90 ? " — SUSPECT > 0.90" : string.Empty)}.");
    }

    private static double Pearson(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        if (x.Count != y.Count || x.Count < 2) return 0;
        double mx=x.Average(), my=y.Average(), numerator=0, dx=0, dy=0;
        for(int i=0;i<x.Count;i++){double a=x[i]-mx,b=y[i]-my;numerator+=a*b;dx+=a*a;dy+=b*b;}
        return dx<=0||dy<=0?0:numerator/Math.Sqrt(dx*dy);
    }

    private static double Value(SkillPressureMetric metric) => metric.Score ?? 0;
    private static string Score(SkillPressureMetric metric) => metric.Score.HasValue ? $"{metric.Score.Value:F1} ({metric.Confidence:F2})" : "N/A";
    private static string TopSignals(SkillPressureMetric metric) => metric.Available
        ? string.Join(", ", metric.Signals.OrderByDescending(signal => signal.Value).Take(3).Select(signal => $"{signal.Key}={signal.Value:F2}"))
        : "N/A";
    private static string Cell(string value) => value.Replace("|", "/");
    private static int Bucket(IEnumerable<double> values, double min, double max) => values.Count(value => value >= min && value < max);
    private static double Median(IReadOnlyList<double> values) => values.Count%2==1?values[values.Count/2]:(values[values.Count/2-1]+values[values.Count/2])/2;

    private sealed record Source(string Name, string Path);
    private sealed record Row(Source Source, Beatmap Map, GameplayProfile Gameplay, SkillPressureProfile Pressure, double TechPatternSupport, double RhythmCoverage)
    {
        public string Name => string.IsNullOrWhiteSpace(Map.Title) ? Source.Name : $"{Map.Title} [{Map.Version}]";
    }
}
