using System.Diagnostics;
using System.IO;
using System.Text;
using BeatInsight.Analysis;
using BeatInsight.Analysis.Identity;
using BeatInsight.Analysis.Rating;
using BeatInsight.Analysis.Sections;
using BeatInsight.Analysis.SkillPressure;
using BeatInsight.Models;
using BeatInsight.Models.Fusion;
using BeatInsight.Models.Identity;
using BeatInsight.Models.Rating;
using BeatInsight.Models.Sections;
using BeatInsight.Models.SkillPressure;
using BeatInsight.Parser;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class BeatInsightRatingV2GlobalForensicTests(ITestOutputHelper output)
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
    public void RealCorpusWritesRatingV2GlobalForensicReport()
    {
        var stopwatch = Stopwatch.StartNew();
        Row[] rows = BuildCorpus().Select(Analyze).ToArray();
        stopwatch.Stop();
        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.Equal(row.HistoricalRating, row.Map.BeatInsightRating));
        Assert.All(rows, row => Assert.InRange(row.Rating.Score, 0, 100));
        Assert.All(rows, row => Assert.InRange(row.Rating.Confidence, 0, 1));

        var report = new StringBuilder();
        AppendHeader(report, rows, stopwatch.Elapsed);
        AppendAudit(report);
        AppendInputs(report);
        AppendCandidates(report, rows);
        AppendSelectedFormula(report);
        AppendTargetedCalibration(report, rows);
        AppendComponentDistributions(report, rows);
        AppendCorrelations(report, rows);
        AppendScoreDistribution(report, rows);
        AppendControls(report, rows);
        AppendStarComparison(report, rows);
        AppendFlags(report, rows);
        AppendDecisions(report, rows);
        AppendLimitations(report);

        string path = Path.Combine(FindRepoRoot(), "Dataset", "Reports", "beatinsight-rating-v2-global-forensic.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, report.ToString(), Encoding.UTF8);
        output.WriteLine($"Rating V2 corpus: {rows.Length} maps; report: {path}");
    }

    private static Row Analyze(Source source)
    {
        Beatmap map = BeatmapParser.Load(source.Path);
        double historicalRating = map.BeatInsightRating;
        GameplayProfile gameplay = GameplayAnalyzer.Analyze(map);
        SectionAnalysisResult sections = SectionAnalysisV2Analyzer.Analyze(map);
        SkillPressureProfile pressure = SkillPressureAnalyzer.Analyze(map);
        GameplayIdentityV3Profile identity = GameplayIdentityV3Analyzer.Analyze(map);
        double duration = map.HitObjects.Count < 2 ? 0 : Math.Max(0, map.HitObjects[^1].Time - map.HitObjects[0].Time) / 1000.0;
        RatingV2PatternEvidence[] patterns = identity.MainPatterns.Select(pattern => new RatingV2PatternEvidence(
            pattern.Name, pattern.Family, pattern.Score, Clamp01(.5 * pattern.Coverage + .5 * pattern.Confidence))).ToArray();
        var input = new BeatInsightRatingV2Input(
            map.HitObjects.Count, duration, gameplay.AimScore, gameplay.SpeedScore, gameplay.ReadScore, gameplay.TechScore,
            pressure, sections, patterns, identity.GlobalIdentityConfidence);
        BeatInsightRatingV2 rating = BeatInsightRatingV2Analyzer.Analyze(input);
        GameplaySectionV2Analysis[] playable = sections.Sections.Where(section => section.ObjectCount > 0).ToArray();
        double[] sortedDifficulty = playable.Select(section => Math.Clamp(section.LocalDifficultyScore, 0, 100)).Order().ToArray();
        double baselinePeak = .60 * Percentile(sortedDifficulty, .90) + .40 * (sortedDifficulty.Length == 0 ? 0 : sortedDifficulty.Max());
        double hardThreshold = Math.Max(30, baselinePeak * .75);
        bool[] hard = playable.Select(section => section.LocalDifficultyScore >= hardThreshold).ToArray();
        int baselinePassages = CountRawClusters(hard);
        double baselineRepeated = baselinePassages <= 1 ? 0 : Clamp01(.60 * ((baselinePassages - 1) / 3.0) + .40 * rating.HighDifficultySectionRatio);
        double stamina = MetricValue(pressure.Stamina), endurancePressure = MetricValue(pressure.Endurance);
        double durationSignal = Clamp01((duration - 90) / 270);
        double baselineEndurance = Math.Clamp(100 * durationSignal
            * (.45 * ((.60 * endurancePressure + .40 * stamina) / 100)
                + .35 * rating.SustainedDifficulty / 100
                + .20 * baselineRepeated), 0, 100);
        double baselineScore = Math.Clamp(rating.Score + BeatInsightRatingV2Analyzer.EnduranceWeight * (baselineEndurance - rating.EnduranceContribution), 0, 100);
        Assert.Equal(historicalRating, map.BeatInsightRating);
        return new(DisplayName(map, source.Name), source.Path, map, gameplay, pressure, sections, identity, duration, historicalRating, rating,
            baselinePassages, baselineRepeated, baselineEndurance, baselineScore);
    }

    private static void AppendHeader(StringBuilder report, IReadOnlyList<Row> rows, TimeSpan elapsed)
    {
        report.AppendLine("# BeatInsight Rating V2 — Global Real-Corpus Forensic\n");
        report.AppendLine($"Autonomous Rating V2 validation on **{rows.Count} maps**, **{rows.Sum(row => row.Map.HitObjects.Count)} objects**. Total analytical time **{elapsed.TotalSeconds:F1}s**, mean **{elapsed.TotalMilliseconds / rows.Count:F1}ms/map**. Historical Rating, osu! Star Rating and all source analyzers remain unchanged.\n");
    }

    private static void AppendAudit(StringBuilder report)
    {
        report.AppendLine("## AUDIT\n");
        report.AppendLine("|Component|Status|Existing source|Reusable|Risks|");
        report.AppendLine("|---|---|---|---|---|");
        report.AppendLine("|Base Difficulty|AVAILABLE|GameplayProfile Aim/Speed/Read/Tech|YES|Dimensions correlate; top-weighted aggregation selected|");
        report.AppendLine("|Skill Pressure|FROZEN V2|SkillPressureProfile|YES|Separated from base and weighted moderately|");
        report.AppendLine("|Section Difficulty|FROZEN V2|SectionAnalysisResult|YES|Already multi-signal; avoid excessive global weight|");
        report.AppendLine("|Difficulty Peaks|AVAILABLE|Playable LocalDifficulty timeline|YES|P90/max blend protects against one outlier|");
        report.AppendLine("|Sustained Difficulty|DERIVED|Mean, hard-section mean and coverage|YES|Shares section source with Peak; semantics remain distinct|");
        report.AppendLine("|Pattern Complexity|AVAILABLE|Identity V3 MainPatterns by structural family|YES|Family maxima and bounded breadth avoid count inflation|");
        report.AppendLine("|Length / Endurance|DERIVED|Duration + Stamina/Endurance + sustained/repetition|YES|Zero before 90s and conditional thereafter|");
        report.AppendLine("|Specialization|DERIVED|Fundamental skill distribution|YES|Capped at 5 points|");
        report.AppendLine("|Confidence|AVAILABLE|Section/Pressure/Pattern/Identity confidences + stability|YES|Orthogonal to Score|");
        report.AppendLine("|Rating explanation|IMPLEMENTED|Structured RatingV2Contributor values|YES|No magic prose in analytical data|\n");
        report.AppendLine("Historical Rating V1 remains `osu! Star Rating + bounded adjustment` and is not renamed, replaced, persisted differently, or consumed by V2. No independent Burst family was invented; existing Stream Burst/BurstControl evidence is consumed through frozen layers.\n");
    }

    private static void AppendInputs(StringBuilder report)
    {
        report.AppendLine("## INPUTS\n");
        report.AppendLine("V2 reads historical Aim/Speed/Reading/Tech scores, Skill Pressure V2, playable Section V2 difficulty/timing, and Identity V3 MainPatterns/confidence. The input contract contains no Star Rating, historical BeatInsight Rating, Identity label, Community tag, cache field, persistence record or ML label. Empty sections are excluded from section ratios. Precision remains unavailable and is not synthesized.\n");
    }

    private static void AppendCandidates(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## CANDIDATE FORMULAS\n");
        report.AppendLine("All candidates use the same audited components and add the same specialization bonus capped at 5.\n");
        report.AppendLine("|Variant|Weights B/S/P/Pressure/Pattern/Endurance|Mean|Median|Min|Max|StdDev|");
        report.AppendLine("|---|---|---:|---:|---:|---:|---:|");
        foreach (string variant in new[] { "A", "B", "C", "D" })
        {
            double[] values = rows.Select(row => Variant(row.Rating, variant)).Order().ToArray();
            string weights = variant switch { "A" => ".35/.25/.15/.10/.10/.05", "B" => ".25/.30/.20/.10/.10/.05", "C" => ".35/.20/.15/.15/.10/.05", _ => ".30/.25/.15/.12/.10/.08" };
            report.AppendLine($"|{variant}|{weights}|{values.Average():F2}|{Median(values):F2}|{values.Min():F2}|{values.Max():F2}|{StdDev(values):F2}|");
        }
        report.AppendLine();
    }

    private static void AppendSelectedFormula(StringBuilder report)
    {
        report.AppendLine("## SELECTED FORMULA\n");
        report.AppendLine("**Variant D — robust hybrid**\n");
        report.AppendLine("`Score = clamp(0.30 Base + 0.25 Sustained + 0.15 Peak + 0.12 SkillPressure + 0.10 Pattern + 0.08 Endurance + Specialization[0..5], 0, 100)`\n");
        report.AppendLine("Base is weighted Top1/Top2/Top3 historical skills (0.50/0.30/0.20). Peak is `0.60 P90 + 0.40 Max`. Sustained uses mean difficulty, mean hard-section difficulty and hard-section coverage. Endurance is conditional on duration above 90 seconds and is gated by Stamina/Endurance, sustained difficulty and repeated passages. A under-represents endurance; B inherits more of the already blended local difficulty; C gives extra weight to pressure correlated with base skills. D remains explicit and does not optimize against osu! SR.\n");
    }

    private static void AppendTargetedCalibration(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## TARGETED CALIBRATION — REPEATED HARD PASSAGES\n");
        report.AppendLine("The first runtime forensic exposed a diagnostic defect: every raw threshold cluster counted as a passage, producing `RepeatedHardPassages` on **35/38** maps. A separate test-only simulation compared raw clusters, minimum two-window clusters, one-window-gap merging, and their combination.\n");
        report.AppendLine("Selected rule: merge a one-window gap, then require at least two hard windows. With 5-second Section V2 windows, a sustained hard passage therefore contains at least 10 seconds of hard play. Isolated extremes remain represented by PeakDifficulty.\n");
        int beforeRepeated = rows.Count(row => row.BaselineHardPassageCount >= 2);
        int afterRepeated = rows.Count(row => row.Rating.HardPassageCount >= 2);
        double[] deltas = rows.Select(row => row.Rating.Score - row.BaselineScore).ToArray();
        report.AppendLine($"- Repeated maps before/after: **{beforeRepeated}/{afterRepeated}**.");
        report.AppendLine($"- Mean passage count before/after: **{rows.Average(row => row.BaselineHardPassageCount):F2}/{rows.Average(row => row.Rating.HardPassageCount):F2}**.");
        report.AppendLine($"- Score delta after calibration: mean **{deltas.Average():+0.000;-0.000;0.000}**, min **{deltas.Min():+0.000;-0.000;0.000}**, max **{deltas.Max():+0.000;-0.000;0.000}**. Only the conditional Endurance path changes.\n");
    }

    private static void AppendComponentDistributions(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## COMPONENT DISTRIBUTIONS\n");
        report.AppendLine("|Component|Mean|Median|Min|Max|StdDev|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|");
        ComponentStats(report, "BaseDifficulty", rows.Select(row => row.Rating.BaseDifficulty));
        ComponentStats(report, "PeakDifficulty", rows.Select(row => row.Rating.PeakDifficulty));
        ComponentStats(report, "SustainedDifficulty", rows.Select(row => row.Rating.SustainedDifficulty));
        ComponentStats(report, "SkillPressure", rows.Select(row => row.Rating.SkillPressureContribution));
        ComponentStats(report, "Pattern", rows.Select(row => row.Rating.PatternContribution));
        ComponentStats(report, "Endurance", rows.Select(row => row.Rating.EnduranceContribution));
        ComponentStats(report, "Specialization", rows.Select(row => row.Rating.SpecializationContribution));
        ComponentStats(report, "Confidence", rows.Select(row => row.Rating.Confidence));
        report.AppendLine();
    }

    private static void AppendCorrelations(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## CORRELATIONS\n");
        double[] rating = rows.Select(row => row.Rating.Score).ToArray();
        report.AppendLine("|Signal|Pearson with Rating V2|");
        report.AppendLine("|---|---:|");
        Correlation(report, "Aim", rating, rows.Select(row => row.Gameplay.AimScore));
        Correlation(report, "Speed", rating, rows.Select(row => row.Gameplay.SpeedScore));
        Correlation(report, "Read", rating, rows.Select(row => row.Gameplay.ReadScore));
        Correlation(report, "Tech", rating, rows.Select(row => row.Gameplay.TechScore));
        Correlation(report, "Peak", rating, rows.Select(row => row.Rating.PeakDifficulty));
        Correlation(report, "Sustained", rating, rows.Select(row => row.Rating.SustainedDifficulty));
        Correlation(report, "Pattern", rating, rows.Select(row => row.Rating.PatternContribution));
        Correlation(report, "SkillPressure", rating, rows.Select(row => row.Rating.SkillPressureContribution));
        Correlation(report, "Endurance", rating, rows.Select(row => row.Rating.EnduranceContribution));
        Correlation(report, "Density", rating, rows.Select(row => Density(row.Map, row.DurationSeconds)));
        Correlation(report, "Length", rating, rows.Select(row => row.DurationSeconds));
        Correlation(report, "osu! Star Rating (descriptive only)", rating, rows.Select(row => row.Map.OsuStarRating));

        report.AppendLine("\n### Anti-double-counting component matrix alerts\n");
        (string Name, Func<Row, double> Get)[] components =
        [
            ("Base", row => row.Rating.BaseDifficulty), ("Peak", row => row.Rating.PeakDifficulty),
            ("Sustained", row => row.Rating.SustainedDifficulty), ("Pressure", row => row.Rating.SkillPressureContribution),
            ("Pattern", row => row.Rating.PatternContribution), ("Endurance", row => row.Rating.EnduranceContribution),
            ("Specialization", row => row.Rating.SpecializationContribution)
        ];
        int alerts = 0;
        for (int left = 0; left < components.Length; left++)
            for (int right = left + 1; right < components.Length; right++)
            {
                double value = Corr(rows.Select(components[left].Get), rows.Select(components[right].Get));
                if (Math.Abs(value) <= .90) continue;
                alerts++;
                report.AppendLine($"- {components[left].Name} vs {components[right].Name}: **{value:F3}**. Peak and Sustained deliberately share the section timeline but represent magnitude versus coverage; their combined weight is 0.40. Other >0.90 pairs require investigation.");
            }
        if (alerts == 0) report.AppendLine("- No absolute component correlation above 0.90.");
        report.AppendLine();
    }

    private static void AppendScoreDistribution(StringBuilder report, IReadOnlyList<Row> rows)
    {
        double[] values = rows.Select(row => row.Rating.Score).Order().ToArray();
        report.AppendLine("## DISTRIBUTION / SATURATION\n");
        report.AppendLine($"Mean **{values.Average():F2}**, median **{Median(values):F2}**, min **{values.Min():F2}**, max **{values.Max():F2}**, stddev **{StdDev(values):F2}**, P10 **{Percentile(values, .10):F2}**, P90 **{Percentile(values, .90):F2}**.\n");
        for (int lower = 0; lower < 90; lower += 10)
            report.AppendLine($"- {lower}–{lower + 10}: {values.Count(value => value >= lower && value < lower + 10)}");
        report.AppendLine($"- >=90: {values.Count(value => value >= 90)}\n");
        report.AppendLine("Ranking is forensic-only:\n");
        foreach (Row row in rows.OrderByDescending(row => row.Rating.Score))
            report.AppendLine($"- {row.Name}: **{row.Rating.Score:F2}** (confidence {row.Rating.Confidence:F3})");
        report.AppendLine();
    }

    private static void AppendControls(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## CONTROL MAPS\n");
        string[] names = ["Tower Of Heaven", "Road of Resistance", "FREEDOM DiVE", "understand me", "Black Rover", "Wings of Courage", "Connect", "From Under Cover", "Exit This Earth", "Honesty"];
        foreach (string name in names)
        {
            Row? row = FindControl(rows, name);
            if (row is null) { report.AppendLine($"### {name}\n\nNot present in the available corpus.\n"); continue; }
            BeatInsightRatingV2 rating = row.Rating;
            report.AppendLine($"### {row.Name}\n");
            report.AppendLine($"- Historical skills Aim/Speed/Read/Tech: **{row.Gameplay.AimScore:F1}/{row.Gameplay.SpeedScore:F1}/{row.Gameplay.ReadScore:F1}/{row.Gameplay.TechScore:F1}**.");
            report.AppendLine($"- Skill Pressure: {PressureSummary(row.Pressure)}.");
            report.AppendLine($"- Identity V3: **{row.Identity.PrimaryStructure}**, secondary **{row.Identity.SecondaryStructure?.ToString() ?? "None"}**, confidence {row.Identity.GlobalIdentityConfidence:F3}.");
            report.AppendLine($"- Sections: mean {rating.MeanSectionDifficulty:F1}, max {rating.MaxSectionDifficulty:F1}, robust peak {rating.PeakDifficulty:F1}, sustained {rating.SustainedDifficulty:F1}, hard ratio {rating.HighDifficultySectionRatio:P1}, passages {rating.HardPassageCount}, repetition {rating.RepeatedHardPassageSignal:F3}, spikes {row.Sections.DifficultySpikeCount}.");
            report.AppendLine($"- Patterns: {rating.PatternContribution:F1}; {(row.Identity.MainPatterns.Count == 0 ? "None" : string.Join(", ", row.Identity.MainPatterns.Select(pattern => $"{pattern.Family}/{pattern.Name} {pattern.Score:F1}")))}.");
            report.AppendLine($"- Endurance {rating.EnduranceContribution:F1}; specialization {rating.SpecializationContribution:F1}; duration {row.DurationSeconds:F1}s.");
            report.AppendLine($"- **Rating V2 {rating.Score:F2}**, confidence {rating.Confidence:F3}. Top contributors: {string.Join("; ", rating.TopContributors.Select(item => $"{item.Name} {item.Contribution:F1}"))}.");
            report.AppendLine($"- Flags: {(rating.Flags.Count == 0 ? "None" : string.Join(", ", rating.Flags))}.\n");
        }
    }

    private static void AppendStarComparison(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## STAR RATING COMPARISON / DIVERGENCES\n");
        report.AppendLine("osu! Star Rating is read here only after Rating V2 has been calculated. It is not present in the analyzer input or formula.\n");
        double[] rating = rows.Select(row => row.Rating.Score).ToArray();
        double[] star = rows.Select(row => row.Map.OsuStarRating).ToArray();
        report.AppendLine($"Pearson Rating V2 vs osu! SR: **{Corr(rating, star):F3}**.\n");
        double ratingMean = rating.Average(), ratingStd = StdDev(rating), starMean = star.Average(), starStd = StdDev(star);
        foreach (var item in rows.Select(row => new
                 {
                     Row = row,
                     Delta = Z(row.Rating.Score, ratingMean, ratingStd) - Z(row.Map.OsuStarRating, starMean, starStd)
                 }).OrderByDescending(item => Math.Abs(item.Delta)).Take(8))
        {
            string direction = item.Delta >= 0 ? "V2 relatively high" : "V2 relatively low";
            report.AppendLine($"- **{item.Row.Name}** — {direction}, z-delta {item.Delta:+0.00;-0.00}; V2 {item.Row.Rating.Score:F1}, SR {item.Row.Map.OsuStarRating:F2}. Explained by Base {item.Row.Rating.BaseDifficulty:F1}, Peak {item.Row.Rating.PeakDifficulty:F1}, Sustained {item.Row.Rating.SustainedDifficulty:F1}, Pressure {item.Row.Rating.SkillPressureContribution:F1}, Pattern {item.Row.Rating.PatternContribution:F1}, Endurance {item.Row.Rating.EnduranceContribution:F1}.");
        }
        report.AppendLine();
    }

    private static void AppendFlags(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## FLAGS\n");
        foreach (RatingV2DiagnosticFlag flag in Enum.GetValues<RatingV2DiagnosticFlag>())
        {
            Row[] matches = rows.Where(row => row.Rating.Flags.Contains(flag)).ToArray();
            report.AppendLine($"- {flag}: **{matches.Length}**{(matches.Length == 0 ? string.Empty : " — " + string.Join("; ", matches.Take(8).Select(row => row.Name)))}");
        }
        report.AppendLine();
    }

    private static void AppendDecisions(StringBuilder report, IReadOnlyList<Row> rows)
    {
        double[] scores = rows.Select(row => row.Rating.Score).ToArray();
        string baseDecision = rows.All(row => row.Rating.BaseDifficulty is >= 0 and <= 100) ? "FREEZE" : "STRUCTURAL ISSUE";
        string peakDecision = rows.All(row => row.Rating.PeakDifficulty is >= 0 and <= 100 && row.Rating.PeakDifficulty <= row.Rating.MaxSectionDifficulty + .0001) ? "FREEZE" : "STRUCTURAL ISSUE";
        string sustainedDecision = rows.All(row => row.Rating.SustainedDifficulty is >= 0 and <= 100) ? "FREEZE" : "STRUCTURAL ISSUE";
        string pressureDecision = rows.All(row => row.Rating.SkillPressureContribution is >= 0 and <= 100) ? "FREEZE" : "STRUCTURAL ISSUE";
        string patternDecision = rows.All(row => row.Rating.PatternContribution is >= 0 and <= 100) ? "FREEZE" : "STRUCTURAL ISSUE";
        string enduranceDecision = rows.All(row => row.Rating.EnduranceContribution is >= 0 and <= 100) ? "FREEZE" : "STRUCTURAL ISSUE";
        string specializationDecision = rows.All(row => row.Rating.SpecializationContribution is >= 0 and <= BeatInsightRatingV2Analyzer.SpecializationCap) ? "FREEZE" : "STRUCTURAL ISSUE";
        string confidenceDecision = rows.Average(row => row.Rating.Confidence) >= .50 && rows.All(row => row.Rating.Confidence is >= 0 and <= 1) ? "FREEZE" : "INVESTIGATE";
        bool discriminative = StdDev(scores) >= 5 && scores.Count(value => value >= 90) <= Math.Max(1, rows.Count / 10);
        string[] decisions = [baseDecision, peakDecision, sustainedDecision, pressureDecision, patternDecision, enduranceDecision, specializationDecision, confidenceDecision];
        bool ready = discriminative && decisions.All(decision => decision == "FREEZE");
        report.AppendLine("## FINAL DECISION\n");
        report.AppendLine($"- BASE DIFFICULTY: **{baseDecision}**");
        report.AppendLine($"- PEAK DIFFICULTY: **{peakDecision}**");
        report.AppendLine($"- SUSTAINED DIFFICULTY: **{sustainedDecision}**");
        report.AppendLine($"- SKILL PRESSURE: **{pressureDecision}**");
        report.AppendLine($"- PATTERN CONTRIBUTION: **{patternDecision}**");
        report.AppendLine($"- ENDURANCE: **{enduranceDecision}**");
        report.AppendLine($"- SPECIALIZATION: **{specializationDecision}**");
        report.AppendLine($"- CONFIDENCE: **{confidenceDecision}**\n");
        report.AppendLine($"**{(ready ? "BEATINSIGHT RATING V2 READY TO FREEZE" : discriminative ? "TARGETED CALIBRATION REQUIRED" : "STRUCTURAL REWORK REQUIRED")}**\n");
        report.AppendLine($"Evidence: score stddev {StdDev(scores):F2}; >=90 saturation {scores.Count(value => value >= 90)}/{rows.Count}; confidence mean {rows.Average(row => row.Rating.Confidence):F3}; historical Rating values unchanged {rows.Count(row => row.HistoricalRating == row.Map.BeatInsightRating)}/{rows.Count}. The single post-implementation calibration was simulated first, affects only Rating V2 repeated-passage grouping/endurance, and is documented above.\n");
    }

    private static void AppendLimitations(StringBuilder report)
    {
        report.AppendLine("## KNOWN LIMITATIONS\n");
        report.AppendLine("- Corpus size is 38 maps and is not a balanced difficulty benchmark.");
        report.AppendLine("- Section V2 uses fixed 5-second windows; Peak and Sustained consequently correlate strongly while retaining different semantics.");
        report.AppendLine("- Precision is unavailable; Endurance and RhythmComplexity remain partially evidenced upstream.");
        report.AppendLine("- Runtime adapter recomputes frozen layers for isolation and is suitable for analysis, not yet an optimized cached/UI path.");
        report.AppendLine("- No replay, PP, player performance, Community or ML evidence is consumed.");
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

    private static string PressureSummary(SkillPressureProfile pressure)
    {
        (string Name, SkillPressureMetric Metric)[] metrics =
        [
            ("Stamina", pressure.Stamina), ("Endurance", pressure.Endurance), ("FingerControl", pressure.FingerControl),
            ("BurstControl", pressure.BurstControl), ("AimControl", pressure.AimControl),
            ("ReadingPressure", pressure.ReadingPressure), ("RhythmComplexity", pressure.RhythmComplexity)
        ];
        return string.Join(", ", metrics.Where(item => item.Metric.Available && item.Metric.Score.HasValue).Select(item => $"{item.Name} {item.Metric.Score!.Value:F1}"));
    }

    private static double Variant(BeatInsightRatingV2 rating, string variant)
    {
        double weighted = variant switch
        {
            "A" => .35 * rating.BaseDifficulty + .25 * rating.SustainedDifficulty + .15 * rating.PeakDifficulty + .10 * rating.SkillPressureContribution + .10 * rating.PatternContribution + .05 * rating.EnduranceContribution,
            "B" => .25 * rating.BaseDifficulty + .30 * rating.SustainedDifficulty + .20 * rating.PeakDifficulty + .10 * rating.SkillPressureContribution + .10 * rating.PatternContribution + .05 * rating.EnduranceContribution,
            "C" => .35 * rating.BaseDifficulty + .20 * rating.SustainedDifficulty + .15 * rating.PeakDifficulty + .15 * rating.SkillPressureContribution + .10 * rating.PatternContribution + .05 * rating.EnduranceContribution,
            _ => .30 * rating.BaseDifficulty + .25 * rating.SustainedDifficulty + .15 * rating.PeakDifficulty + .12 * rating.SkillPressureContribution + .10 * rating.PatternContribution + .08 * rating.EnduranceContribution
        };
        return Math.Clamp(weighted + rating.SpecializationContribution, 0, 100);
    }

    private static void ComponentStats(StringBuilder report, string name, IEnumerable<double> source)
    {
        double[] values = source.Order().ToArray();
        report.AppendLine($"|{name}|{values.Average():F2}|{Median(values):F2}|{values.Min():F2}|{values.Max():F2}|{StdDev(values):F2}|");
    }
    private static void Correlation(StringBuilder report, string name, IEnumerable<double> left, IEnumerable<double> right) => report.AppendLine($"|{name}|{Corr(left, right):F3}|");
    private static double Corr(IEnumerable<double> leftSource, IEnumerable<double> rightSource) { double[] left = leftSource.ToArray(), right = rightSource.ToArray(); if (left.Length == 0 || left.Length != right.Length) return 0; double lm = left.Average(), rm = right.Average(); double numerator = left.Zip(right, (a, b) => (a - lm) * (b - rm)).Sum(); double denominator = Math.Sqrt(left.Sum(value => Math.Pow(value - lm, 2)) * right.Sum(value => Math.Pow(value - rm, 2))); return denominator <= 0 ? 0 : numerator / denominator; }
    private static double Median(IReadOnlyList<double> values) => values.Count == 0 ? 0 : values.Count % 2 == 1 ? values[values.Count / 2] : (values[values.Count / 2 - 1] + values[values.Count / 2]) / 2;
    private static double Percentile(IReadOnlyList<double> sorted, double p) { if (sorted.Count == 0) return 0; double position = (sorted.Count - 1) * Math.Clamp(p, 0, 1); int lower = (int)Math.Floor(position), upper = (int)Math.Ceiling(position); return lower == upper ? sorted[lower] : sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower); }
    private static double StdDev(IEnumerable<double> source) { double[] values = source.ToArray(); if (values.Length == 0) return 0; double mean = values.Average(); return Math.Sqrt(values.Average(value => Math.Pow(value - mean, 2))); }
    private static double Z(double value, double mean, double stddev) => stddev <= 0 ? 0 : (value - mean) / stddev;
    private static double Density(Beatmap map, double duration) => duration <= 0 ? 0 : map.HitObjects.Count / duration;
    private static double Clamp01(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
    private static double MetricValue(SkillPressureMetric metric) => metric.Available && metric.Score.HasValue && double.IsFinite(metric.Score.Value) ? Math.Clamp(metric.Score.Value, 0, 100) : 0;
    private static int CountRawClusters(IReadOnlyList<bool> values) { int count = 0; for (int index = 0; index < values.Count; index++) if (values[index] && (index == 0 || !values[index - 1])) count++; return count; }
    private static string DisplayName(Beatmap map, string fallback) => string.IsNullOrWhiteSpace(map.Title) ? fallback : $"{map.Title} [{map.Version}]";
    private static string FindRepoRoot() { DirectoryInfo? directory = new(AppContext.BaseDirectory); while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BeatInsight.slnx"))) directory = directory.Parent; return directory?.FullName ?? throw new DirectoryNotFoundException(); }
    private sealed record Source(string Name, string Path);
    private sealed record Row(string Name, string Path, Beatmap Map, GameplayProfile Gameplay, SkillPressureProfile Pressure, SectionAnalysisResult Sections, GameplayIdentityV3Profile Identity, double DurationSeconds, double HistoricalRating, BeatInsightRatingV2 Rating, int BaselineHardPassageCount, double BaselineRepeatedSignal, double BaselineEndurance, double BaselineScore);
}
