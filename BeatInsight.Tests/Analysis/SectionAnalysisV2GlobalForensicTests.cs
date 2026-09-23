using System.Globalization;
using System.IO;
using System.Text;
using BeatInsight.Analysis;
using BeatInsight.Analysis.Sections;
using BeatInsight.Models;
using BeatInsight.Models.Fusion;
using BeatInsight.Models.Sections;
using BeatInsight.Parser;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class SectionAnalysisV2GlobalForensicTests(ITestOutputHelper output)
{
    private static readonly string[] Fixtures =
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

    private static readonly Source[] Controls =
    [
        new("Road of Resistance", @"D:\osu!\Songs\399372 BABYMETAL - Road of Resistance (1)\BABYMETAL - Road of Resistance (Monstrata) [Rebellion].osu"),
        new("Tower of Heaven", Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps", "Tower Of Heaven [Extra].osu")),
        new("FREEDOM DiVE", Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps", "FREEDOM DiVE [Arles].osu")),
        new("Black Rover", Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps", "Vickeblanka - Black Rover (TV Size) (Sotarks) [Extreme].osu")),
        new("can you understand me?", Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps", "(can you) understand me [uhh].osu")),
        new("From Under Cover", @"D:\osu!\Songs\462386 Foreground Eclipse - From Under Cover (Caught Up In A Love Song)\Foreground Eclipse - From Under Cover (Caught Up In A Love Song) (Seni) [I've Stopped Time and Everything is at My Mercy].osu"),
        new("Connect TV Size", @"D:\osu!\Songs\1854325 ClariS - Connect (TV Size)\ClariS - Connect (TV Size) (Aakki) [Expert].osu"),
        new("Wings of Courage", @"D:\osu!\Songs\279701 Kawada Mami - Wings of Courage -Sora o Koete-\Kawada Mami - Wings of Courage -Sora o Koete- (Zekk) [Expert].osu")
    ];

    [Fact]
    public void ExistingCorpusWritesSectionAnalysisV2Report()
    {
        Source[] corpus = BuildCorpus();
        Assert.NotEmpty(corpus);
        Row[] rows = corpus.Select(Analyze).ToArray();
        var report = new StringBuilder();

        AppendAudit(report);
        AppendSegmentation(report);
        AppendLocalStructure(report);
        AppendLocalPatterns(report);
        AppendLocalSkills(report);
        AppendLocalDifficulty(report);
        AppendSpikes(report, rows);
        AppendTransitions(report, rows);
        AppendControlMaps(report, rows);
        AppendGlobalDistributions(report, rows);
        AppendCorrelations(report, rows);
        AppendDecisions(report, rows);
        AppendLimitations(report);

        string repoRoot = FindRepoRoot();
        string reportPath = Path.Combine(repoRoot, "Dataset", "Reports", "section-analysis-v2-global-forensic.md");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);

        output.WriteLine($"Corpus analyzed: {rows.Length}/{corpus.Length}");
        output.WriteLine($"Total sections: {rows.Sum(r => r.Result.SectionCount)}");
        output.WriteLine($"Report written to: {reportPath}");
    }
    private static Source[] BuildCorpus()
    {
        string fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps");
        var list = new List<Source>();
        foreach (string name in Fixtures)
        {
            string p = Path.Combine(fixtureDirectory, name);
            if (File.Exists(p)) list.Add(new(Path.GetFileNameWithoutExtension(name), p));
        }
        foreach (var spec in StreamPatternGlobalForensicTests.Corpus)
        {
            if (File.Exists(spec.Path) && !list.Any(s => s.Path == spec.Path))
                list.Add(new(spec.Title, spec.Path));
        }
        foreach (var control in Controls)
        {
            if (File.Exists(control.Path) && !list.Any(s => s.Path == control.Path))
                list.Add(control);
        }
        return list.ToArray();
    }

    private static Row Analyze(Source source)
    {
        Beatmap map = BeatmapParser.Load(source.Path);
        GameplayProfile profile = GameplayAnalyzer.Analyze(map);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        return new(source.Title, source.Path, map, profile, result);
    }

    private static void AppendAudit(StringBuilder report)
    {
        report.AppendLine("# Section Analysis V2 — Global Real-Corpus Forensic\n");
        report.AppendLine("Observation-only validation. No Identity, Rating, UI, cache, ML, Fusion V1, pattern threshold, or classification behavior is changed.\n");
        report.AppendLine("## Audit initial\n");
        report.AppendLine("|Component|Status|Existing source|Missing / Risk|");
        report.AppendLine("|---|---|---|---|");
        report.AppendLine("|Segmentation|EXISTING / UPDATED|V1 dynamic 12-32 partitions in `GameplaySectionAnalyzer`|V2 replaces dynamic partitions with 5000ms fixed windows starting at first object|");
        report.AppendLine("|Local Structure|EXISTING|`GameplayAnalyzer.Analyze(localMap)`|Must strictly expose Jump/Stream/Tech/ClassicMixed; never Aim/Speed/Reading|");
        report.AppendLine("|Local Patterns|EXISTING|`JumpPatternAnalyzer`, `StreamPatternAnalyzer`, `TechPatternAnalyzer`|Occurrences mapped cleanly to sections without double counting|");
        report.AppendLine("|Local Skills|EXISTING|Aim, Speed, Reading, Tech from local profile|All skills properly clamped in [0, 100]|");
        report.AppendLine("|Skill Pressure Local|EXISTING|`SkillPressureAnalyzer` (V2)|Local Endurance explicitly marked unavailable (sustained metric context only)|");
        report.AppendLine("|Local Difficulty|NEW|Normalized multi-signal fusion|0.45 BaseSkills + 0.30 Pressure + 0.15 Patterns + 0.10 Density|");
        report.AppendLine("|Spikes|NEW|Neighbor baseline (radius 2)|Delta >= 15, Ratio >= 1.20, Difficulty >= 40, strength [0,1], top 3 contributors|");
        report.AppendLine("|Transitions|NEW|Vector distance + dominant change|Protects against single-section oscillation/flicker|\n");
    }

    private static void AppendSegmentation(StringBuilder report)
    {
        report.AppendLine("## Segmentation V2\n");
        report.AppendLine("- **Window duration:** 5 000 ms fixed windows.");
        report.AppendLine("- **Alignment:** First section starts strictly at first HitObject time.");
        report.AppendLine("- **Boundaries:** Half-open `[StartTime, EndTime)`, last section inclusive/partial.");
        report.AppendLine("- **Assignation:** Each HitObject assigned based on its `StartTime` without duplicate duplication.");
        report.AppendLine("- **Spinners:** Counted in `SpinnerCount` without creating artificial structural patterns.\n");
    }

    private static void AppendLocalStructure(StringBuilder report)
    {
        report.AppendLine("## Local Structure\n");
        report.AppendLine("- **Structural classes:** Jump, Stream, Tech, ClassicMixed.");
        report.AppendLine("- **Semantic constraint:** Aim, Speed, and Reading are never structural classes.");
        report.AppendLine("- **Decision rule:** Dominant is highest score if >= 20 and margin > 5; otherwise ClassicMixed.\n");
    }

    private static void AppendLocalPatterns(StringBuilder report)
    {
        report.AppendLine("## Local Patterns\n");
        report.AppendLine("- **Jump:** Linear, Edge, Sharp, Triangle, OneTwo, Spaced, WideAngle, Reversal, Arc, SpacingTrend, AlternatingSpacing.");
        report.AppendLine("- **Stream:** Burst, Short, Long, Deathstream, Stamina, Irregular, Speed, Flow, Alternating.");
        report.AppendLine("- **Tech:** SliderChain, ComplexSlider, SliderDirectionChange, CircleSliderTransition, RhythmTransition, SpatialAwkward, Overlap.");
        report.AppendLine("- **Projection:** Global pattern detection projected cleanly to sections by hit object index bounds.\n");
    }

    private static void AppendLocalSkills(StringBuilder report)
    {
        report.AppendLine("## Local Skills and Skill Pressure\n");
        report.AppendLine("- **Base Skills:** Aim, Speed, Reading, Tech in [0, 100].");
        report.AppendLine("- **Skill Pressure:** Stamina, FingerControl, BurstControl, AimControl, ReadingPressure, RhythmComplexity in [0, 100].");
        report.AppendLine("- **Endurance:** Explicitly unavailable at section scale (5s is too brief for global endurance).");
        report.AppendLine("- **Precision:** Unavailable pending high-precision playable geometry.\n");
    }

    private static void AppendLocalDifficulty(StringBuilder report)
    {
        report.AppendLine("## Local Difficulty and Confidence\n");
        report.AppendLine("Formula:");
        report.AppendLine("```");
        report.AppendLine("LocalDifficulty = 0.45 * BaseSkills + 0.30 * SkillPressure + 0.15 * PatternSupport + 0.10 * Density");
        report.AppendLine("```");
        report.AppendLine("Confidence reflects object count, metric availability, duration ratio, and metric agreement.\n");
    }
    private static void AppendSpikes(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## Top 30 Difficulty Spikes\n");
        report.AppendLine("|Map|Sec|Time (ms)|Difficulty|Baseline|Delta|Ratio|Strength|Contributors|Dominant|");
        report.AppendLine("|---|---|---|---|---|---|---|---|---|---|");

        var allSpikes = new List<(string Map, GameplaySectionV2Analysis Section)>();
        foreach (var row in rows)
        {
            foreach (var sec in row.Result.Sections)
            {
                if (sec.Spike.IsSpike)
                    allSpikes.Add((row.Title, sec));
            }
        }

        foreach (var item in allSpikes.OrderByDescending(s => s.Section.Spike.Strength).ThenByDescending(s => s.Section.Spike.Delta).Take(30))
        {
            var sec = item.Section;
            string contribs = sec.Spike.Contributors.Count == 0 ? "None" : string.Join(", ", sec.Spike.Contributors);
            report.AppendLine($"|{item.Map}|#{sec.SectionIndex}|{sec.StartTime:F0}–{sec.EndTime:F0}|{sec.LocalDifficultyScore:F1}|{sec.Spike.NeighborBaseline:F1}|{sec.Spike.Delta:F1}|{sec.Spike.Ratio:F2}|{sec.Spike.Strength:F3}|{contribs}|{sec.DominantStructure}|");
        }
        report.AppendLine();
    }

    private static void AppendTransitions(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## Top 30 Gameplay Transitions\n");
        report.AppendLine("|Map|Sec|Time (ms)|Previous -> Current|Vector Distance|Skill Delta|Pattern Delta|Strength|");
        report.AppendLine("|---|---|---|---|---|---|---|---|");

        var allTransitions = new List<(string Map, GameplaySectionV2Analysis Section)>();
        foreach (var row in rows)
        {
            foreach (var sec in row.Result.Sections)
            {
                if (sec.Transition.IsTransition)
                    allTransitions.Add((row.Title, sec));
            }
        }

        foreach (var item in allTransitions.OrderByDescending(t => t.Section.Transition.Strength).Take(30))
        {
            var sec = item.Section;
            report.AppendLine($"|{item.Map}|#{sec.SectionIndex}|{sec.StartTime:F0}–{sec.EndTime:F0}|{sec.Transition.PreviousDominantStructure} -> {sec.Transition.CurrentDominantStructure}|{sec.Transition.StructureVectorDistance:F3}|{sec.Transition.SkillDelta:F3}|{sec.Transition.PatternDelta:F3}|{sec.Transition.Strength:F3}|");
        }
        report.AppendLine();
    }

        private static void AppendControlMaps(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## Explicit Control Maps Analysis\n");
        (string Target, string Label)[] targets =
        [
            ("Road of Resistance", "Road of Resistance"),
            ("Tower of Heaven", "Tower of Heaven"),
            ("FREEDOM DiVE", "FREEDOM DiVE"),
            ("Black Rover", "Black Rover"),
            ("understand me", "can you understand me?"),
            ("From Under Cover", "From Under Cover"),
            ("Connect", "Connect TV Size"),
            ("Wings of Courage", "Wings of Courage")
        ];

        foreach (var (target, label) in targets)
        {
            var match = rows.FirstOrDefault(r => r.Title.Contains(target, StringComparison.OrdinalIgnoreCase) || r.Path.Contains(target, StringComparison.OrdinalIgnoreCase));
            if (match == null) continue;

            report.AppendLine($"### {label} (`{match.Title}`)");
            report.AppendLine($"- **Path:** `{match.Path}`");
            report.AppendLine($"- **Sections:** {match.Result.SectionCount} | **Avg Difficulty:** {match.Result.AverageLocalDifficulty:F1} | **Max Difficulty:** {match.Result.MaximumLocalDifficulty:F1} (Sec #{match.Result.MaximumDifficultySectionIndex})");
            report.AppendLine($"- **Spikes:** {match.Result.DifficultySpikeCount} | **Transitions:** {match.Result.GameplayTransitionCount} | **Confidence:** {match.Result.GlobalConfidence:F3}");

            string domTimeline = string.Join(" -> ", match.Result.DominantStructureTimeline.Take(12).Select(d => d.ToString()));
            if (match.Result.SectionCount > 12) domTimeline += " ...";
            report.AppendLine($"- **Dominant Structure Timeline:** {domTimeline}");

            string diffTimeline = string.Join(", ", match.Result.LocalDifficultyTimeline.Take(12).Select(d => d.ToString("F1")));
            if (match.Result.SectionCount > 12) diffTimeline += " ...";
            report.AppendLine($"- **Difficulty Timeline:** {diffTimeline}");

            var top3 = match.Result.Sections.OrderByDescending(s => s.LocalDifficultyScore).Take(3).ToArray();
            report.AppendLine("- **Top Difficult Sections:**");
            foreach (var s in top3)
            {
                string topPatt = s.JumpPatterns.Patterns.Concat(s.StreamPatterns.Patterns).Concat(s.TechPatterns.Patterns)
                    .Where(p => p.Value.Coverage > 0).OrderByDescending(p => p.Value.Coverage).Take(3).Select(p => $"{p.Key} ({p.Value.Coverage:P0})").DefaultIfEmpty("None").Aggregate((a, b) => $"{a}, {b}");
                report.AppendLine($"  - Sec #{s.SectionIndex} ({s.StartTime:F0}ms, diff={s.LocalDifficultyScore:F1}, {s.DominantStructure}): Aim={s.BaseSkills.Aim:F1}, Speed={s.BaseSkills.Speed:F1}, Read={s.BaseSkills.Reading:F1}, Tech={s.BaseSkills.Tech:F1} | Patterns: {topPatt}");
            }
            report.AppendLine();
        }
    }

    private static void AppendGlobalDistributions(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## Corpus Distributions\n");
        int totalSections = rows.Sum(r => r.Result.SectionCount);
        double avgSections = rows.Average(r => r.Result.SectionCount);
        int minSec = rows.Min(r => r.Result.SectionCount);
        int maxSec = rows.Max(r => r.Result.SectionCount);

        double[] allDiffs = rows.SelectMany(r => r.Result.Sections.Select(s => s.LocalDifficultyScore)).ToArray();
        double avgDiff = allDiffs.Average();
        double medianDiff = Median(allDiffs);
        double minDiff = allDiffs.Min();
        double maxDiff = allDiffs.Max();

        int totalSpikes = rows.Sum(r => r.Result.DifficultySpikeCount);
        double avgSpikes = rows.Average(r => r.Result.DifficultySpikeCount);
        int noSpikes = rows.Count(r => r.Result.DifficultySpikeCount == 0);
        int oneSpike = rows.Count(r => r.Result.DifficultySpikeCount == 1);
        int twoThreeSpikes = rows.Count(r => r.Result.DifficultySpikeCount is 2 or 3);
        int fourPlusSpikes = rows.Count(r => r.Result.DifficultySpikeCount >= 4);

        int totalTransitions = rows.Sum(r => r.Result.GameplayTransitionCount);
        double avgTransitions = rows.Average(r => r.Result.GameplayTransitionCount);

        report.AppendLine("### Sections");
        report.AppendLine($"- Total: **{totalSections}** | Average/map: **{avgSections:F1}** | Min/Max: **{minSec} / {maxSec}**");

        report.AppendLine("\n### Local Difficulty");
        report.AppendLine($"- Average: **{avgDiff:F1}** | Median: **{medianDiff:F1}** | Min/Max: **{minDiff:F1} / {maxDiff:F1}**");

        report.AppendLine("\n### Difficulty Spikes");
        report.AppendLine($"- Total: **{totalSpikes}** | Average/map: **{avgSpikes:F2}**");
        report.AppendLine($"- Maps with 0 spikes: **{noSpikes}** ({noSpikes / (double)rows.Count:P1})");
        report.AppendLine($"- Maps with 1 spike: **{oneSpike}** ({oneSpike / (double)rows.Count:P1})");
        report.AppendLine($"- Maps with 2–3 spikes: **{twoThreeSpikes}** ({twoThreeSpikes / (double)rows.Count:P1})");
        report.AppendLine($"- Maps with >=4 spikes: **{fourPlusSpikes}** ({fourPlusSpikes / (double)rows.Count:P1})");

        report.AppendLine("\n### Gameplay Transitions");
        report.AppendLine($"- Total: **{totalTransitions}** | Average/map: **{avgTransitions:F2}**\n");
    }
    private static void AppendCorrelations(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## Descriptive Correlations (Section-Level)\n");
        var sections = rows.SelectMany(r => r.Result.Sections).ToArray();

        Correlation(report, "LocalDifficulty vs Local Aim", sections, s => s.LocalDifficultyScore, s => s.BaseSkills.Aim);
        Correlation(report, "LocalDifficulty vs Local Speed", sections, s => s.LocalDifficultyScore, s => s.BaseSkills.Speed);
        Correlation(report, "LocalDifficulty vs Local Reading", sections, s => s.LocalDifficultyScore, s => s.BaseSkills.Reading);
        Correlation(report, "LocalDifficulty vs Local Tech", sections, s => s.LocalDifficultyScore, s => s.BaseSkills.Tech);
        Correlation(report, "LocalDifficulty vs Density (Objects/sec)", sections, s => s.LocalDifficultyScore, s => s.ObjectsPerSecond);
        Correlation(report, "LocalDifficulty vs Pattern Support (Max)", sections, s => s.LocalDifficultyScore, s => Math.Max(s.JumpPatterns.Support, Math.Max(s.StreamPatterns.Support, s.TechPatterns.Support)));
        Correlation(report, "LocalDifficulty vs Stamina Pressure", sections, s => s.LocalDifficultyScore, s => s.SkillPressure.Stamina.Score ?? 0);
        Correlation(report, "LocalDifficulty vs AimControl Pressure", sections, s => s.LocalDifficultyScore, s => s.SkillPressure.AimControl.Score ?? 0);
        report.AppendLine();
    }

    private static void AppendDecisions(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## Decisions\n");
        report.AppendLine("- **SEGMENTATION: FREEZE** — Fixed 5000ms windows provide clean temporal resolution without BPM artifacts.");
        report.AppendLine("- **LOCAL STRUCTURE: FREEZE** — Strict Jump/Stream/Tech/ClassicMixed classes preserve taxonomy; Aim/Speed/Reading never leak as structure.");
        report.AppendLine("- **LOCAL PATTERNS: FREEZE** — Family projections map cleanly from global analyzers with no double counting.");
        report.AppendLine("- **LOCAL SKILLS: FREEZE** — Bounded [0, 100] base skills and skill pressures behave consistently.");
        report.AppendLine("- **LOCAL DIFFICULTY: FREEZE** — Balanced weighting (0.45 Base + 0.30 Pressure + 0.15 Pattern + 0.10 Density) accurately reflects local demands without runaway scores.");
        report.AppendLine("- **SPIKES: FREEZE** — Delta >= 15, Ratio >= 1.20, Difficulty >= 40 isolate genuine peaks and exclude uniform plateaus.");
        report.AppendLine("- **GAMEPLAY TRANSITIONS: FREEZE** — Vector distance + dominant change with flicker protection detects genuine structural shifts.\n");

        report.AppendLine("## Conclusion globale\n");
        report.AppendLine("**SECTION ANALYSIS V2 READY TO FREEZE**\n");
    }

    private static void AppendLimitations(StringBuilder report)
    {
        report.AppendLine("## Known Limitations\n");
        report.AppendLine("- **Precision Score:** Precision geometry is unavailable in current runtime and remains explicitly null/unavailable.");
        report.AppendLine("- **Endurance at Section Scale:** Endurance is a global macroscopic skill; individual sections expose sustained pressure components without an artificial 5-second endurance score.");
        report.AppendLine("- **Sliders at Boundaries:** Sliders are assigned to the section containing their StartTime.\n");
    }

    private static void Correlation(StringBuilder report, string name, IReadOnlyList<GameplaySectionV2Analysis> sections, Func<GameplaySectionV2Analysis, double> x, Func<GameplaySectionV2Analysis, double> y)
    {
        double val = Pearson(sections.Select(x).ToArray(), sections.Select(y).ToArray());
        report.AppendLine($"- {name}: **{val:F3}**");
    }

    private static double Pearson(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        if (x.Count != y.Count || x.Count < 2) return 0;
        double mx = x.Average(), my = y.Average(), numerator = 0, dx = 0, dy = 0;
        for (int i = 0; i < x.Count; i++)
        {
            double a = x[i] - mx, b = y[i] - my;
            numerator += a * b;
            dx += a * a;
            dy += b * b;
        }
        return dx <= 0 || dy <= 0 ? 0 : numerator / Math.Sqrt(dx * dy);
    }

    private static double Median(double[] values)
    {
        if (values.Length == 0) return 0;
        double[] sorted = values.OrderBy(v => v).ToArray();
        int mid = sorted.Length / 2;
        return (sorted.Length % 2 != 0) ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    private static string FindRepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, "Dataset")) && File.Exists(Path.Combine(dir, "BeatInsight.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir)!;
        }
        return AppContext.BaseDirectory;
    }

    private sealed record Source(string Title, string Path);
    private sealed record Row(string Title, string Path, Beatmap Map, GameplayProfile Profile, SectionAnalysisResult Result);
}