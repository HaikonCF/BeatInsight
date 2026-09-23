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
/// Observation-only real-corpus forensic for the frozen first-pass Stream Pattern thresholds.
/// It writes a reproducible Markdown report and never mutates production policy or beatmaps.
/// </summary>
public sealed class StreamPatternGlobalForensicTests(ITestOutputHelper output)
{
    internal static readonly MapSpec[] Corpus =
    [
        new("FREEDOM DiVE [METAL DIMENSIONS]", "Hyper", @"D:\osu!\Songs\905158 xi remixed by cosMo@bousouP - FREEDOM DiVE [METAL DIMENSIONS]\xi remixed by cosMo@bousouP - FREEDOM DiVE [METAL DIMENSIONS] (Cherry Blossom) [Hyper].osu"),
        new("Ange du Blanc Pur", "Insane", @"D:\osu!\Songs\1188740 ke-ji feat Nanahira - Ange du Blanc Pur\ke-ji feat. Nanahira - Ange du Blanc Pur (Astronic) [Insane].osu"),
        new("Glorious Crown", "Hobbes2's Insane", @"D:\osu!\Songs\558694 xi - Glorious Crown\xi - Glorious Crown (Monstrata) [Hobbes2's Insane].osu"),
        new("Ashes of the Dawn", "Insane", @"D:\osu!\Songs\786141 DragonForce - Ashes of the Dawn\DragonForce - Ashes of the Dawn (pw384) [Insane].osu"),
        new("d.m.c (Band Ver.)", "Insane", @"D:\osu!\Songs\52203 Rungran - dmc (Band Ver)\Rungran - d.m.c (Band Ver.) (Sieg) [Insane].osu"),
        new("BLACK or WHITE?", "INFINITE", @"D:\osu!\Songs\248876 BlackYooh vs siromaru - BLACK or WHITE\BlackYooh vs. siromaru - BLACK or WHITE (BluOxy) [INFINITE].osu"),
        new("Die (feat. Casey Lee Williams)", "Ambient's Insane", @"D:\osu!\Songs\288824 Jeff Williams - Die (feat Casey Lee Williams)\Jeff Williams - Die (feat. Casey Lee Williams) (LunarFox) [Ambient's Insane].osu"),
        new("Victims of Contingency", "LMT's Insane", @"D:\osu!\Songs\540457 EPICA - Victims of Contingency\EPICA - Victims Of Contingency (DakiniBrave) [LMT's Insane].osu"),
        new("BSPower Explosion", "Another", @"D:\osu!\Songs\44967 Memme - BSPower Explosion\Memme - BSPower Explosion (AngelHoney) [Another].osu"),
        new("Akasha", "Insane", @"D:\osu!\Songs\403427 xi - Akasha\xi - Akasha (Atsuro) [Insane].osu"),
        new("Extreme Fantasy", "ExtrA", @"D:\osu!\Songs\49459 Memme - Extreme Fantasy\Memme - Extreme Fantasy (AngelHoney) [ExtrA].osu"),
        new("CHOIR JAIL", "Amnesia", @"D:\osu!\Songs\172688 Suzuki Konomi - CHOIR JAIL\Suzuki Konomi - CHOIR JAIL (Natsu) [Amnesia].osu"),
        new("ANiMA", "Extra", @"D:\osu!\Songs\624995 xi - ANiMA\xi - ANiMA (sdafsf) [Extra].osu"),
        new("Reign of Fear", "III", @"D:\osu!\Songs\347136 Apocalyptica - Reign of Fear\Apocalyptica - Reign of Fear (pishifat) [III].osu"),
        new("NNRT", "Another", @"D:\osu!\Songs\47330 Levaslater - NNRT\Levaslater - NNRT (Reisen Udongein) [Another].osu"),
        new("End Time", "eXtra", @"D:\osu!\Songs\73474 Cres - End Time\Cres - End Time (Maddy) [eXtra].osu"),
        new("AiAe", "Expert", @"D:\osu!\Songs\257793 Yuyoyuppe - AiAe\Yuyoyuppe - AiAe (Fort) [Expert].osu"),
        new("Road of Resistance (GoldenWolf Metal Mix)", "Short-Lived Rebellion", @"D:\osu!\Songs\747066 R3 Music Box - Road of Resistance (GoldenWolf Metal Mix)\R3 Music Box - Road of Resistance (GoldenWolf Metal Mix) (Naotoshi) [Short-Lived Rebellion].osu")
    ];

    [Fact]
    public void RequestedRealCorpus_WritesObservationOnlyNinePatternReport()
    {
        Assert.All(Corpus, spec => Assert.True(File.Exists(spec.Path), $"Missing corpus map: {spec.Path}"));
        MapResult[] maps = Corpus.Select(Analyze).ToArray();
        var report = new StringBuilder();
        AppendHeader(report);
        AppendCorpus(report, maps);
        AppendGlobalTables(report, maps);
        AppendObjectiveRankings(report, maps);
        AppendGlobalDiagnostics(report, maps);
        foreach (MapResult map in maps)
            AppendMap(report, map);

        string reportPath = Path.Combine(AppContext.BaseDirectory, "stream-pattern-global-forensic.md");
        File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);
        output.WriteLine($"Corpus analyzed: {maps.Length}/{Corpus.Length}");
        output.WriteLine($"Report: {reportPath}");
    }

    private static MapResult Analyze(MapSpec spec)
    {
        Beatmap map = BeatmapParser.Load(spec.Path);
        Assert.Equal(spec.Title, map.Title);
        Assert.Equal(spec.Version, map.Version);
        return new(spec, map,
            StreamPatternAnalyzer.AnalyzeBurst(map.HitObjects),
            StreamPatternAnalyzer.AnalyzeShortStream(map.HitObjects),
            StreamPatternAnalyzer.AnalyzeLongStream(map.HitObjects),
            StreamPatternAnalyzer.AnalyzeDeathstream(map.HitObjects),
            StreamPatternAnalyzer.AnalyzeStaminaStream(map.HitObjects),
            StreamPatternAnalyzer.AnalyzeIrregularStream(map.HitObjects),
            StreamPatternAnalyzer.AnalyzeSpeedStream(map.HitObjects),
            StreamPatternAnalyzer.AnalyzeFlowStream(map.HitObjects),
            StreamPatternAnalyzer.AnalyzeAlternatingStream(map.HitObjects));
    }

    private static void AppendHeader(StringBuilder report)
    {
        report.AppendLine("# Stream Pattern Analysis — global real-corpus forensic");
        report.AppendLine();
        report.AppendLine("Observation-only run. Production formulas and thresholds are unchanged. No automatic calibration is applied.");
        report.AppendLine();
        report.AppendLine("Current policy: maximal circle-only runs, temporal links 30–130 ms inclusive; lengths 3–8 / 9–16 / 17–32 / >=33; Stamina 9 objects/run, 2 runs, 24 objects, recovery <=1500 ms; Irregular CV >=0.10 and at least 2 median-atypical interval regions at 12 ms; Speed average interval <=90 ms; Flow 15°–110°, 18 px, pivot ratio >=0.70, dominant direction >=0.75; Alternating 20°–160°, 20 px, valid-pivot ratio >=0.70, alternation >=0.75.");
        report.AppendLine();
    }

    private static void AppendCorpus(StringBuilder report, IReadOnlyList<MapResult> maps)
    {
        report.AppendLine("## 1. Corpus réellement trouvé et analysé");
        report.AppendLine();
        report.AppendLine($"Toutes les cartes demandées ont été trouvées: **{maps.Count}/{Corpus.Length}**. Road of Resistance utilise la difficulté standard la plus haute indexée, **Short-Lived Rebellion** (BeatmapId 1574314). Les copies de dossiers suffixées `(1)` ne sont pas utilisées.");
        report.AppendLine();
        report.AppendLine("|#|Map|Difficulty|Objects|Circles|Sliders|Dense slider separators|Source|");
        report.AppendLine("|---:|---|---|---:|---:|---:|---:|---|");
        for (int index = 0; index < maps.Count; index++)
        {
            MapResult item = maps[index];
            int circles = item.Map.HitObjects.Count(IsCircle);
            int sliders = item.Map.HitObjects.Count(IsSlider);
            report.AppendLine($"|{index + 1}|{Cell(item.Spec.Title)}|{Cell(item.Spec.Version)}|{item.Map.HitObjects.Count}|{circles}|{sliders}|{DenseSliderSeparators(item.Map.HitObjects)}|`{item.Spec.Path}`|");
        }
        report.AppendLine();
        report.AppendLine("## 2. Maps manquantes");
        report.AppendLine();
        report.AppendLine("Aucune.");
        report.AppendLine();
    }

    private static void AppendGlobalDiagnostics(StringBuilder report, IReadOnlyList<MapResult> maps)
    {
        report.AppendLine("## 5. Diagnostics transversaux des seuils gelés");
        report.AppendLine();
        int[] lengths = maps.SelectMany(map => map.Burst.Runs.Select(run => run.ObjectCount)).ToArray();
        report.AppendLine($"- Runs maximaux: {lengths.Length}; 2 objets={lengths.Count(x => x == 2)}, Burst 3–8={lengths.Count(x => x is >= 3 and <= 8)}, Short 9–16={lengths.Count(x => x is >= 9 and <= 16)}, Long 17–32={lengths.Count(x => x is >= 17 and <= 32)}, Death >=33={lengths.Count(x => x >= 33)}.");
        report.AppendLine($"- Sliders: {maps.Sum(map => map.Map.HitObjects.Count(IsSlider))}; séparateurs slider entre deux cercles distants de <=260 ms: {maps.Sum(map => DenseSliderSeparators(map.Map.HitObjects))}.");

        var irregular = maps.SelectMany(map => map.Irregular.Runs.Where(run => run.ObjectCount >= 6)
            .Select(run => (Map: map, Run: run))).ToArray();
        var maxCv = irregular.OrderByDescending(item => item.Run.CoefficientOfVariation).FirstOrDefault();
        var maxSignificant = irregular.OrderByDescending(item => item.Run.SignificantDeltaRatio).FirstOrDefault();
        report.AppendLine(FormattableString.Invariant($"- Irregular eligible runs={irregular.Length}; CV>=0.10: {irregular.Count(item => item.Run.CoefficientOfVariation >= 0.10)}; distinct events>=2: {irregular.Count(item => item.Run.DistinctIrregularEventCount >= 2)}; qualified: {irregular.Count(item => item.Run.Qualified)}. SignificantDeltaRatio remains diagnostic only."));
        if (maxCv.Run is not null)
            report.AppendLine(FormattableString.Invariant($"- Maximum Irregular CV: {maxCv.Map.Label} {Time(maxCv.Run.StartTime)}–{Time(maxCv.Run.EndTime)}, CV={maxCv.Run.CoefficientOfVariation:F4}, events={maxCv.Run.DistinctIrregularEventCount}, significant={maxCv.Run.SignificantDeltaRatio:F4}, intervals=[{Numbers(maxCv.Run.Intervals)}], reason={maxCv.Run.Reason}."));
        if (maxSignificant.Run is not null)
            report.AppendLine(FormattableString.Invariant($"- Maximum Irregular significant ratio: {maxSignificant.Map.Label} {Time(maxSignificant.Run.StartTime)}–{Time(maxSignificant.Run.EndTime)}, CV={maxSignificant.Run.CoefficientOfVariation:F4}, significant={maxSignificant.Run.SignificantDeltaRatio:F4}, intervals=[{Numbers(maxSignificant.Run.Intervals)}], reason={maxSignificant.Run.Reason}."));
        foreach (var item in irregular.Where(item => item.Run.CoefficientOfVariation >= 0.10)
            .OrderByDescending(item => item.Run.CoefficientOfVariation))
            report.AppendLine(FormattableString.Invariant($"  - CV-pass {item.Map.Label} {Time(item.Run.StartTime)}–{Time(item.Run.EndTime)}: objects={item.Run.ObjectCount}, CV={item.Run.CoefficientOfVariation:F4}, median={item.Run.MedianInterval:F2}, atypical={item.Run.AtypicalIntervalCount}, events={item.Run.DistinctIrregularEventCount}, significant={item.Run.SignificantDeltaRatio:F4}, reason={item.Run.Reason}."));

        var speed = maps.SelectMany(map => map.Speed.Runs.Where(run => run.ObjectCount >= 6)
            .Select(run => (Map: map, Run: run))).ToArray();
        report.AppendLine($"- Speed eligible runs={speed.Length}; qualified <=90 ms={speed.Count(item => item.Run.Qualified)}; near rejected (90,95] ms={speed.Count(item => !item.Run.Qualified && item.Run.AverageInterval <= 95)}.");

        var flow = maps.SelectMany(map => map.Flow.Runs.Where(run => run.ObjectCount >= 6)
            .Select(run => (Map: map, Run: run))).ToArray();
        var alternating = maps.SelectMany(map => map.Alternating.Runs.Where(run => run.ObjectCount >= 6)
            .Select(run => (Map: map, Run: run))).ToArray();
        report.AppendLine($"- Flow eligible runs={flow.Length}; reasons: {Reasons(flow.Select(item => item.Run.Reason))}.");
        report.AppendLine($"- Alternating eligible runs={alternating.Length}; reasons: {Reasons(alternating.Select(item => item.Run.Reason))}.");
        var flowDistanceOnly = flow.Where(item => item.Run.Reason == "InsufficientSpatialMovement"
            && FlowQualifiesIgnoringDistance(item.Run)).ToArray();
        var alternatingDistanceOnly = alternating.Where(item => item.Run.Reason == "InsufficientSpatialMovement"
            && AlternatingQualifiesIgnoringDistance(item.Run)).ToArray();
        report.AppendLine($"- Flow distance-only exclusions (geometry would otherwise qualify): {flowDistanceOnly.Length}; Alternating distance-only exclusions: {alternatingDistanceOnly.Length}.");
        foreach (var item in flowDistanceOnly.OrderByDescending(item => item.Run.MinimumDistance).Take(5))
            report.AppendLine(FormattableString.Invariant($"  - Flow distance-only {item.Map.Label} {Time(item.Run.StartTime)}–{Time(item.Run.EndTime)}: objects={item.Run.ObjectCount}, minDistance={item.Run.MinimumDistance:F2}px."));
        foreach (var item in alternatingDistanceOnly.OrderByDescending(item => item.Run.MinimumDistance).Take(5))
            report.AppendLine(FormattableString.Invariant($"  - Alternating distance-only {item.Map.Label} {Time(item.Run.StartTime)}–{Time(item.Run.EndTime)}: objects={item.Run.ObjectCount}, minDistance={item.Run.MinimumDistance:F2}px."));

        int qualifiedLongGaps = maps.Sum(map => map.Stamina.Sequences.Sum(sequence =>
            sequence.RecoveryGaps.Count(gap => gap >= 1200)));
        var separatedGaps = maps.SelectMany(map => EligibleStaminaGaps(map)
            .Where(gap => gap.Gap > 1500 && gap.Gap <= 2000)
            .Select(gap => (Map: map, gap.PreviousEnd, gap.NextStart, gap.Gap)))
            .OrderBy(item => item.Gap).ToArray();
        report.AppendLine($"- Stamina qualified sequences={maps.Sum(map => map.Stamina.StaminaSequenceCount)}; recovery gaps in [1200,1500]={qualifiedLongGaps}; separated eligible-run gaps in (1500,2000]={separatedGaps.Length}.");
        foreach (var item in separatedGaps.Take(8))
            report.AppendLine(FormattableString.Invariant($"  - separated {item.Map.Label}: {Time(item.PreviousEnd)}–{Time(item.NextStart)}, gap={item.Gap:F0}ms."));
        report.AppendLine();
    }

    private static bool FlowQualifiesIgnoringDistance(FlowStreamPatternSequence run)
    {
        FlowStreamPivot[] pivots = run.Pivots.Where(pivot => pivot.TurnAngle is >= 15 and <= 110
            && pivot.TurnSign != 0).ToArray();
        double ratio = run.PivotCount == 0 ? 0 : (double)pivots.Length / run.PivotCount;
        int dominant = Math.Max(pivots.Count(pivot => pivot.TurnSign > 0),
            pivots.Count(pivot => pivot.TurnSign < 0));
        double direction = pivots.Length == 0 ? 0 : (double)dominant / pivots.Length;
        return ratio >= 0.70 && direction >= 0.75;
    }

    private static bool AlternatingQualifiesIgnoringDistance(AlternatingStreamPatternSequence run)
    {
        AlternatingStreamPivot[] pivots = run.Pivots.Where(pivot => pivot.TurnAngle is >= 20 and <= 160
            && pivot.TurnSign != 0).ToArray();
        double ratio = run.PivotCount == 0 ? 0 : (double)pivots.Length / run.PivotCount;
        int possible = Math.Max(0, pivots.Length - 1);
        int alternating = Enumerable.Range(0, possible)
            .Count(index => pivots[index].TurnSign != pivots[index + 1].TurnSign);
        double alternationRatio = possible == 0 ? 0 : (double)alternating / possible;
        return ratio >= 0.70 && alternationRatio >= 0.75;
    }

    private static IEnumerable<(int PreviousEnd, int NextStart, double Gap)> EligibleStaminaGaps(MapResult map)
    {
        StaminaRapidRun[] runs = map.Stamina.Candidates.SelectMany(candidate => candidate.Runs)
            .DistinctBy(run => (run.StartIndex, run.EndIndex)).OrderBy(run => run.StartTime).ToArray();
        return runs.Zip(runs.Skip(1), (previous, next) =>
            (previous.EndTime, next.StartTime, (double)next.StartTime - previous.EndTime));
    }

    private static string Reasons(IEnumerable<string> reasons) => string.Join(", ", reasons
        .GroupBy(reason => reason).OrderBy(group => group.Key)
        .Select(group => $"{group.Key}={group.Count()}"));

    private static void AppendGlobalTables(StringBuilder report, IReadOnlyList<MapResult> maps)
    {
        report.AppendLine("## 3. Tableau global des neuf patterns");
        report.AppendLine();
        report.AppendLine("### Coverage (%)");
        report.AppendLine();
        report.AppendLine("|Map|Burst|Short|Long|Death|Stamina|Irregular|Speed|Flow|Alternating|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (MapResult map in maps)
            report.AppendLine(FormattableString.Invariant($"|{Cell(map.Label)}|{Pct(map.Burst.Coverage)}|{Pct(map.Short.Coverage)}|{Pct(map.Long.Coverage)}|{Pct(map.Death.Coverage)}|{Pct(map.Stamina.Coverage)}|{Pct(map.Irregular.Coverage)}|{Pct(map.Speed.Coverage)}|{Pct(map.Flow.Coverage)}|{Pct(map.Alternating.Coverage)}|"));
        report.AppendLine();
        report.AppendLine("### Sequence count");
        report.AppendLine();
        report.AppendLine("|Map|Burst|Short|Long|Death|Stamina|Irregular|Speed|Flow|Alternating|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (MapResult map in maps)
            report.AppendLine($"|{Cell(map.Label)}|{map.Burst.BurstSequenceCount}|{map.Short.ShortStreamSequenceCount}|{map.Long.LongStreamSequenceCount}|{map.Death.DeathstreamSequenceCount}|{map.Stamina.StaminaSequenceCount}|{map.Irregular.IrregularStreamSequenceCount}|{map.Speed.SpeedStreamSequenceCount}|{map.Flow.FlowStreamSequenceCount}|{map.Alternating.AlternatingStreamSequenceCount}|");
        report.AppendLine();
    }

    private static void AppendObjectiveRankings(StringBuilder report, IReadOnlyList<MapResult> maps)
    {
        report.AppendLine("## 4. Résumé objectif orienté anomalies");
        report.AppendLine();
        AppendRanking(report, "Stamina coverage", maps, map => map.Stamina.Coverage);
        AppendRanking(report, "Speed coverage", maps, map => map.Speed.Coverage);
        AppendRanking(report, "Irregular coverage", maps, map => map.Irregular.Coverage);
        AppendRanking(report, "Flow coverage", maps, map => map.Flow.Coverage);
        AppendRanking(report, "Alternating coverage", maps, map => map.Alternating.Coverage);
        report.AppendLine($"- Deathstreams: {List(maps.Where(map => map.Death.DeathstreamSequenceCount > 0).Select(map => $"{map.Label} ({map.Death.DeathstreamSequenceCount})"))}");
        report.AppendLine($"- Aucun pattern spatial qualifié: {List(maps.Where(map => map.Flow.FlowStreamSequenceCount == 0 && map.Alternating.AlternatingStreamSequenceCount == 0).Select(map => map.Label))}");
        report.AppendLine($"- Flow et Alternating qualifiés sur le même run: {List(maps.SelectMany(map => SpatialOverlaps(map).Select(run => $"{map.Label} {Time(run.StartTime)}–{Time(run.EndTime)}")))}");
        report.AppendLine();
    }

    private static void AppendRanking(StringBuilder report, string label, IReadOnlyList<MapResult> maps,
        Func<MapResult, double> selector)
    {
        string values = string.Join(", ", maps.OrderByDescending(selector).Take(5)
            .Select(map => $"{map.Label} {selector(map) * 100:F2}%"));
        report.AppendLine($"- Top 5 {label}: {values}");
    }

    private static void AppendMap(StringBuilder report, MapResult map)
    {
        report.AppendLine($"## {Cell(map.Label)}");
        report.AppendLine();
        AppendMapSummary(report, map);
        AppendLengthDetails(report, map);
        AppendStaminaDetails(report, map.Stamina);
        AppendIrregularDetails(report, map.Irregular);
        AppendSpeedDetails(report, map.Speed);
        AppendFlowDetails(report, map.Flow);
        AppendAlternatingDetails(report, map.Alternating);
        AppendSpatialComparison(report, map);
    }

    private static void AppendMapSummary(StringBuilder report, MapResult map)
    {
        report.AppendLine("|Pattern|Sequences|Objects|Coverage %|Longest|Average length|Confidence|Main metrics|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---|");
        report.AppendLine(FormattableString.Invariant($"|Burst|{map.Burst.BurstSequenceCount}|{map.Burst.BurstObjectCount}|{Pct(map.Burst.Coverage)}|{map.Burst.LongestBurst}|{map.Burst.AverageBurstLength:F2}|{map.Burst.Confidence:F4}|avg interval {map.Burst.AverageInterval:F2}; timing consistency {map.Burst.AverageIntervalConsistency:F4}|"));
        report.AppendLine(FormattableString.Invariant($"|Short|{map.Short.ShortStreamSequenceCount}|{map.Short.ShortStreamObjectCount}|{Pct(map.Short.Coverage)}|{map.Short.LongestShortStream}|{map.Short.AverageShortStreamLength:F2}|{map.Short.Confidence:F4}|avg interval {map.Short.AverageInterval:F2}; timing consistency {map.Short.AverageIntervalConsistency:F4}|"));
        report.AppendLine(FormattableString.Invariant($"|Long|{map.Long.LongStreamSequenceCount}|{map.Long.LongStreamObjectCount}|{Pct(map.Long.Coverage)}|{map.Long.LongestLongStream}|{map.Long.AverageLongStreamLength:F2}|{map.Long.Confidence:F4}|avg interval {map.Long.AverageInterval:F2}; timing consistency {map.Long.AverageIntervalConsistency:F4}|"));
        report.AppendLine(FormattableString.Invariant($"|Deathstream|{map.Death.DeathstreamSequenceCount}|{map.Death.DeathstreamObjectCount}|{Pct(map.Death.Coverage)}|{map.Death.LongestDeathstream}|{map.Death.AverageDeathstreamLength:F2}|{map.Death.Confidence:F4}|avg interval {map.Death.AverageInterval:F2}; timing consistency {map.Death.AverageIntervalConsistency:F4}|"));
        report.AppendLine(FormattableString.Invariant($"|Stamina|{map.Stamina.StaminaSequenceCount}|{map.Stamina.StaminaObjectCount}|{Pct(map.Stamina.Coverage)}|{map.Stamina.LongestStaminaObjectCount}|{map.Stamina.AverageObjectsPerSequence:F2}|{map.Stamina.Confidence:F4}|longest {map.Stamina.LongestStaminaDuration:F0} ms; runs/seq {map.Stamina.AverageRunsPerSequence:F2}; recovery {map.Stamina.AverageRecoveryGap:F2}; active {map.Stamina.AverageActiveRatio:F4}|"));
        report.AppendLine(FormattableString.Invariant($"|Irregular|{map.Irregular.IrregularStreamSequenceCount}|{map.Irregular.IrregularStreamObjectCount}|{Pct(map.Irregular.Coverage)}|{map.Irregular.LongestIrregularStream}|{map.Irregular.AverageIrregularStreamLength:F2}|{map.Irregular.Confidence:F4}|CV {map.Irregular.AverageCoefficientOfVariation:F4}; events {map.Irregular.AverageDistinctIrregularEvents:F2}; atypical {map.Irregular.AverageAtypicalIntervalRatio:F4}; significant diagnostic {map.Irregular.AverageSignificantDeltaRatio:F4}; score {map.Irregular.AverageIrregularityScore:F4}|"));
        report.AppendLine(FormattableString.Invariant($"|Speed|{map.Speed.SpeedStreamSequenceCount}|{map.Speed.SpeedStreamObjectCount}|{Pct(map.Speed.Coverage)}|{map.Speed.LongestSpeedStream}|{map.Speed.AverageSpeedStreamLength:F2}|{map.Speed.Confidence:F4}|avg/fastest {map.Speed.AverageInterval:F2}/{map.Speed.FastestAverageInterval:F2} ms; BPM avg/max {map.Speed.AverageStreamBpm:F2}/{map.Speed.MaximumStreamBpm:F2}; intensity {map.Speed.AverageSpeedIntensity:F4}|"));
        report.AppendLine(FormattableString.Invariant($"|Flow|{map.Flow.FlowStreamSequenceCount}|{map.Flow.FlowStreamObjectCount}|{Pct(map.Flow.Coverage)}|{map.Flow.LongestFlowStream}|{map.Flow.AverageFlowStreamLength:F2}|{map.Flow.Confidence:F4}|pivot {map.Flow.AverageFlowPivotRatio:F4}; direction {map.Flow.AverageTurnDirectionConsistency:F4}; angle {map.Flow.AverageTurnAngle:F2}; spacing {map.Flow.AverageSpacingConsistency:F4}; score {map.Flow.AverageFlowScore:F4}|"));
        report.AppendLine(FormattableString.Invariant($"|Alternating|{map.Alternating.AlternatingStreamSequenceCount}|{map.Alternating.AlternatingStreamObjectCount}|{Pct(map.Alternating.Coverage)}|{map.Alternating.LongestAlternatingStream}|{map.Alternating.AverageAlternatingStreamLength:F2}|{map.Alternating.Confidence:F4}|valid {map.Alternating.AverageValidPivotRatio:F4}; alternation {map.Alternating.AverageAlternationRatio:F4}; angle {map.Alternating.AverageTurnAngle:F2}; spacing {map.Alternating.AverageSpacingConsistency:F4}; score {map.Alternating.AverageAlternatingScore:F4}|"));
        report.AppendLine();
    }

    private static void AppendLengthDetails(StringBuilder report, MapResult map)
    {
        report.AppendLine("### Burst / Short / Long / Deathstream");
        int[] lengths = map.Burst.Runs.Select(run => run.ObjectCount).ToArray();
        report.AppendLine($"Rapid maximal runs={lengths.Length}; objects/run distribution: 2={lengths.Count(x => x == 2)}, 3–8={lengths.Count(x => x is >= 3 and <= 8)}, 9–16={lengths.Count(x => x is >= 9 and <= 16)}, 17–32={lengths.Count(x => x is >= 17 and <= 32)}, >=33={lengths.Count(x => x >= 33)}; dense slider separators={DenseSliderSeparators(map.Map.HitObjects)}.");
        AppendLengthSequences(report, "Burst", map.Burst.Sequences.Select(x => new LengthView(x.StartTime, x.EndTime, x.ObjectCount, x.AverageInterval, x.IntervalConsistency)));
        AppendLengthSequences(report, "Short", map.Short.Sequences.Select(x => new LengthView(x.StartTime, x.EndTime, x.ObjectCount, x.AverageInterval, x.IntervalConsistency)));
        AppendLengthSequences(report, "Long", map.Long.Sequences.Select(x => new LengthView(x.StartTime, x.EndTime, x.ObjectCount, x.AverageInterval, x.IntervalConsistency)));
        AppendLengthSequences(report, "Deathstream", map.Death.Sequences.Select(x => new LengthView(x.StartTime, x.EndTime, x.ObjectCount, x.AverageInterval, x.IntervalConsistency)));
        report.AppendLine();
    }

    private static void AppendLengthSequences(StringBuilder report, string label, IEnumerable<LengthView> source)
    {
        LengthView[] sequences = source.OrderByDescending(x => x.ObjectCount).Take(5).ToArray();
        if (sequences.Length == 0) return;
        report.AppendLine($"- {label} top: {string.Join("; ", sequences.Select(x => FormattableString.Invariant($"{Time(x.StartTime)}–{Time(x.EndTime)} objects={x.ObjectCount} avg={x.AverageInterval:F2}ms consistency={x.Consistency:F4} reason=Qualified")))}");
    }

    private static void AppendStaminaDetails(StringBuilder report, StaminaStreamPatternAnalysis result)
    {
        report.AppendLine("### Stamina");
        foreach (StaminaStreamPatternSequence sequence in result.Sequences
            .OrderByDescending(sequence => sequence.TotalRapidObjects).Take(5))
            report.AppendLine(FormattableString.Invariant($"- {Time(sequence.StartTime)}–{Time(sequence.EndTime)} objects={sequence.TotalRapidObjects}; runs={sequence.RunCount}; duration={sequence.Duration:F0}ms; gaps=[{Numbers(sequence.RecoveryGaps)}]; avgGap={sequence.AverageRecoveryGap:F2}; maxGap={sequence.MaximumRecoveryGapObserved:F2}; active={sequence.ActiveRatio:F4}; avgInterval={sequence.AverageInterval:F2}; reason={sequence.Reason}"));
        foreach (StaminaStreamPatternSequence candidate in result.Candidates.Where(x => !x.Qualified)
            .OrderByDescending(x => x.TotalRapidObjects).Take(3))
            report.AppendLine(FormattableString.Invariant($"- Near/rejected {Time(candidate.StartTime)}–{Time(candidate.EndTime)} objects={candidate.TotalRapidObjects}; runs={candidate.RunCount}; gaps=[{Numbers(candidate.RecoveryGaps)}]; active={candidate.ActiveRatio:F4}; reason={candidate.Reason}"));
        int longGaps = result.Sequences.Sum(sequence => sequence.RecoveryGaps.Count(gap => gap >= 1200));
        report.AppendLine($"- Qualified recovery gaps in [1200,1500] ms: {longGaps}.");
        report.AppendLine();
    }

    private static void AppendIrregularDetails(StringBuilder report, IrregularStreamPatternAnalysis result)
    {
        report.AppendLine("### Irregular Stream");
        foreach (IrregularStreamPatternSequence sequence in result.Sequences
            .OrderByDescending(sequence => sequence.IrregularityScore).ThenByDescending(sequence => sequence.ObjectCount).Take(5))
            report.AppendLine(FormattableString.Invariant($"- {Time(sequence.StartTime)}–{Time(sequence.EndTime)} objects={sequence.ObjectCount}; avg={sequence.AverageInterval:F2}ms; median={sequence.MedianInterval:F2}; intervals=[{Numbers(sequence.Intervals)}]; atypical={sequence.AtypicalIntervalCount}/{sequence.LinkCount} ({sequence.AtypicalIntervalRatio:F4}); events={sequence.DistinctIrregularEventCount}; CV={sequence.CoefficientOfVariation:F4}; significant diagnostic={sequence.SignificantDeltaRatio:F4}; score={sequence.IrregularityScore:F4}; assessment={IrregularAssessment(sequence)}; reason={sequence.Reason}"));
        foreach (IrregularStreamPatternSequence run in result.Runs.Where(x => !x.Qualified && x.ObjectCount >= 6)
            .OrderBy(x => IrregularDeficit(x)).Take(3))
            report.AppendLine(FormattableString.Invariant($"- Borderline rejected {Time(run.StartTime)}–{Time(run.EndTime)} objects={run.ObjectCount}; intervals=[{Numbers(run.Intervals)}]; median={run.MedianInterval:F2}; CV={run.CoefficientOfVariation:F4}; atypical={run.AtypicalIntervalRatio:F4}; events={run.DistinctIrregularEventCount}; significant diagnostic={run.SignificantDeltaRatio:F4}; reason={run.Reason}"));
        report.AppendLine();
    }

    private static string IrregularAssessment(IrregularStreamPatternSequence sequence)
    {
        if (sequence.AtypicalIntervalCount == 1) return "spike-driven";
        if (sequence.CoefficientOfVariation < 0.12 || sequence.DistinctIrregularEventCount < 2) return "borderline";
        return "distributed-irregularity";
    }

    private static double IrregularDeficit(IrregularStreamPatternSequence sequence) =>
        Math.Max(0, 0.10 - sequence.CoefficientOfVariation)
        + Math.Max(0, 2 - sequence.DistinctIrregularEventCount);

    private static void AppendSpeedDetails(StringBuilder report, SpeedStreamPatternAnalysis result)
    {
        report.AppendLine("### Speed Stream");
        foreach (SpeedStreamPatternSequence sequence in result.Sequences
            .OrderByDescending(sequence => sequence.ObjectCount).ThenBy(sequence => sequence.AverageInterval).Take(5))
            report.AppendLine(FormattableString.Invariant($"- {Time(sequence.StartTime)}–{Time(sequence.EndTime)} objects={sequence.ObjectCount}; avg={sequence.AverageInterval:F2}ms; BPM={sequence.StreamBpm:F2}; intensity={sequence.SpeedIntensity:F4}; timingConsistency={sequence.IntervalConsistency:F4}; reason={sequence.Reason}"));
        foreach (SpeedStreamPatternSequence run in result.Runs.Where(x => !x.Qualified && x.ObjectCount >= 6)
            .OrderBy(run => Math.Abs(run.AverageInterval - 90)).Take(3))
            report.AppendLine(FormattableString.Invariant($"- Near/rejected {Time(run.StartTime)}–{Time(run.EndTime)} objects={run.ObjectCount}; avg={run.AverageInterval:F2}ms; BPM={run.StreamBpm:F2}; reason={run.Reason}"));
        report.AppendLine();
    }

    private static void AppendFlowDetails(StringBuilder report, FlowStreamPatternAnalysis result)
    {
        report.AppendLine("### Flow Stream");
        foreach (FlowStreamPatternSequence sequence in result.Sequences
            .OrderByDescending(sequence => sequence.FlowScore).ThenByDescending(sequence => sequence.ObjectCount).Take(5))
            AppendFlowLine(report, "", sequence);
        foreach (FlowStreamPatternSequence run in result.Runs.Where(x => !x.Qualified && x.ObjectCount >= 6)
            .OrderBy(run => FlowDeficit(run)).Take(3))
            AppendFlowLine(report, "Borderline/rejected ", run);
        report.AppendLine($"- InsufficientSpatialMovement rejects: {result.Runs.Count(run => run.Reason == "InsufficientSpatialMovement")}.");
        report.AppendLine();
    }

    private static void AppendFlowLine(StringBuilder report, string prefix, FlowStreamPatternSequence sequence) =>
        report.AppendLine(FormattableString.Invariant($"- {prefix}{Time(sequence.StartTime)}–{Time(sequence.EndTime)} objects={sequence.ObjectCount}; avgInterval={sequence.AverageInterval:F2}ms; distance avg/min/max={sequence.AverageDistance:F2}/{sequence.MinimumDistance:F2}/{sequence.MaximumDistance:F2}px; angles=[{Numbers(sequence.TurnAngles)}]; signs=[{string.Join(",", sequence.TurnSigns)}]; pivot={sequence.FlowPivotCount}/{sequence.PivotCount} ({sequence.FlowPivotRatio:F4}); direction={sequence.TurnDirectionConsistency:F4}; curvature={sequence.CurvatureConsistency:F4}; spacing={sequence.SpacingConsistency:F4}; score={sequence.FlowScore:F4}; reason={sequence.Reason}"));

    private static double FlowDeficit(FlowStreamPatternSequence sequence) =>
        Math.Max(0, 0.70 - sequence.FlowPivotRatio) + Math.Max(0, 0.75 - sequence.TurnDirectionConsistency)
        + (sequence.Reason == "InsufficientSpatialMovement" ? 1 : 0);

    private static void AppendAlternatingDetails(StringBuilder report, AlternatingStreamPatternAnalysis result)
    {
        report.AppendLine("### Alternating Stream");
        foreach (AlternatingStreamPatternSequence sequence in result.Sequences
            .OrderByDescending(sequence => sequence.AlternatingScore).ThenByDescending(sequence => sequence.ObjectCount).Take(5))
            AppendAlternatingLine(report, "", sequence);
        foreach (AlternatingStreamPatternSequence run in result.Runs.Where(x => !x.Qualified && x.ObjectCount >= 6)
            .OrderBy(run => AlternatingDeficit(run)).Take(3))
            AppendAlternatingLine(report, "Borderline/rejected ", run);
        report.AppendLine($"- InsufficientSpatialMovement rejects: {result.Runs.Count(run => run.Reason == "InsufficientSpatialMovement")}.");
        report.AppendLine();
    }

    private static void AppendAlternatingLine(StringBuilder report, string prefix,
        AlternatingStreamPatternSequence sequence) =>
        report.AppendLine(FormattableString.Invariant($"- {prefix}{Time(sequence.StartTime)}–{Time(sequence.EndTime)} objects={sequence.ObjectCount}; avgInterval={sequence.AverageInterval:F2}ms; distance avg/min/max={sequence.AverageDistance:F2}/{sequence.MinimumDistance:F2}/{sequence.MaximumDistance:F2}px; angles=[{Numbers(sequence.TurnAngles)}]; signs=[{string.Join(",", sequence.TurnSigns)}]; valid={sequence.ValidAlternatingPivotCount}/{sequence.PivotCount} ({sequence.ValidAlternatingPivotRatio:F4}); alternations={sequence.AlternationCount}/{sequence.PossibleAlternations} ({sequence.AlternationRatio:F4}); angleConsistency={sequence.AlternatingAngleConsistency:F4}; spacing={sequence.SpacingConsistency:F4}; score={sequence.AlternatingScore:F4}; reason={sequence.Reason}"));

    private static double AlternatingDeficit(AlternatingStreamPatternSequence sequence) =>
        Math.Max(0, 0.70 - sequence.ValidAlternatingPivotRatio) + Math.Max(0, 0.75 - sequence.AlternationRatio)
        + (sequence.Reason == "InsufficientSpatialMovement" ? 1 : 0);

    private static void AppendSpatialComparison(StringBuilder report, MapResult map)
    {
        report.AppendLine("### Flow vs Alternating sur les mêmes runs");
        var pairs = map.Flow.Runs.Zip(map.Alternating.Runs)
            .Where(pair => pair.First.Qualified || pair.Second.Qualified
                || pair.First.FlowPivotRatio >= 0.60 || pair.Second.ValidAlternatingPivotRatio >= 0.60)
            .OrderByDescending(pair => Math.Max(pair.First.FlowScore, pair.Second.AlternatingScore))
            .Take(8)
            .ToArray();
        if (pairs.Length == 0)
            report.AppendLine("- Aucun run spatial qualifié ou proche des seuils.");
        foreach ((FlowStreamPatternSequence flow, AlternatingStreamPatternSequence alternating) in pairs)
            report.AppendLine(FormattableString.Invariant($"- {Time(flow.StartTime)}–{Time(flow.EndTime)} objects={flow.ObjectCount}; Flow={flow.Qualified} pivot={flow.FlowPivotRatio:F4} direction={flow.TurnDirectionConsistency:F4} score={flow.FlowScore:F4} reason={flow.Reason}; Alternating={alternating.Qualified} valid={alternating.ValidAlternatingPivotRatio:F4} alternation={alternating.AlternationRatio:F4} score={alternating.AlternatingScore:F4} reason={alternating.Reason}."));
        report.AppendLine();
    }

    private static IEnumerable<FlowStreamPatternSequence> SpatialOverlaps(MapResult map)
    {
        var alternating = map.Alternating.Sequences.Select(run => (run.StartIndex, run.EndIndex)).ToHashSet();
        return map.Flow.Sequences.Where(run => alternating.Contains((run.StartIndex, run.EndIndex)));
    }

    private static int DenseSliderSeparators(IReadOnlyList<HitObject> objects)
    {
        int count = 0;
        for (int index = 1; index + 1 < objects.Count; index++)
            if (IsSlider(objects[index]) && IsCircle(objects[index - 1]) && IsCircle(objects[index + 1])
                && objects[index + 1].Time - objects[index - 1].Time <= 260)
                count++;
        return count;
    }

    private static bool IsCircle(HitObject value) => (value.Type & 1) == 1;
    private static bool IsSlider(HitObject value) => (value.Type & 2) == 2;
    private static string Numbers(IEnumerable<double> values) => string.Join(",", values.Select(value => value.ToString("F2", CultureInfo.InvariantCulture)));
    private static string Pct(double value) => (value * 100).ToString("F2", CultureInfo.InvariantCulture);
    private static string Time(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds).ToString(@"mm\:ss\.fff");
    private static string Cell(string value) => value.Replace("|", "\\|");
    private static string List(IEnumerable<string> values)
    {
        string[] materialized = values.ToArray();
        return materialized.Length == 0 ? "None" : string.Join(", ", materialized);
    }

    private static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        double[] sorted = values.OrderBy(value => value).ToArray();
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2 : sorted[middle];
    }

    internal sealed record MapSpec(string Title, string Version, string Path);
    private sealed record LengthView(int StartTime, int EndTime, int ObjectCount, double AverageInterval,
        double Consistency);
    private sealed record MapResult(
        MapSpec Spec,
        Beatmap Map,
        BurstPatternAnalysis Burst,
        ShortStreamPatternAnalysis Short,
        LongStreamPatternAnalysis Long,
        DeathstreamPatternAnalysis Death,
        StaminaStreamPatternAnalysis Stamina,
        IrregularStreamPatternAnalysis Irregular,
        SpeedStreamPatternAnalysis Speed,
        FlowStreamPatternAnalysis Flow,
        AlternatingStreamPatternAnalysis Alternating)
    {
        public string Label => $"{Spec.Title} [{Spec.Version}]";
    }
}
