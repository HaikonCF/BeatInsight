using System.IO;
using System.Text;
using BeatInsight.Analysis.Identity;
using BeatInsight.Models;
using BeatInsight.Models.Fusion;
using BeatInsight.Models.Identity;
using BeatInsight.Parser;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class GameplayIdentityV3GlobalForensicTests(ITestOutputHelper output)
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
    public void ExistingCorpusWritesGameplayIdentityV3Report()
    {
        Source[] corpus = BuildCorpus();
        Assert.NotEmpty(corpus);
        Row[] rows = corpus.Select(Analyze).ToArray();
        Assert.DoesNotContain(rows, row => row.Identity.PrimaryStructure.ToString() is "Aim" or "Speed" or "Reading" or "Stamina");
        var report = new StringBuilder();
        AppendHeader(report, rows);
        AppendImplementation(report);
        AppendMaps(report, rows);
        AppendControls(report, rows);
        AppendDistributions(report, rows);
        AppendMixed(report, rows);
        AppendDecisions(report, rows);

        string root = FindRepoRoot();
        string path = Path.Combine(root, "Dataset", "Reports", "gameplay-identity-v3-global-forensic.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, report.ToString(), Encoding.UTF8);
        output.WriteLine($"Corpus: {rows.Length} maps; report: {path}");
    }

    private static Source[] BuildCorpus()
    {
        string fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps");
        var sources = new List<Source>();
        foreach (string fixture in Fixtures)
        {
            string path = Path.Combine(fixtures, fixture);
            if (File.Exists(path)) sources.Add(new(Path.GetFileNameWithoutExtension(fixture), path));
        }
        foreach (var map in StreamPatternGlobalForensicTests.Corpus)
            if (File.Exists(map.Path) && !sources.Any(source => source.Path == map.Path)) sources.Add(new(map.Title, map.Path));
        foreach (var map in TechV2SliderDensityCalibrationAudit.Corpus)
            if (File.Exists(map.Path) && !sources.Any(source => source.Path == map.Path)) sources.Add(new(map.Name, map.Path));
        foreach (Source control in Controls)
            if (File.Exists(control.Path) && !sources.Any(source => source.Path == control.Path)) sources.Add(control);
        return sources.ToArray();
    }

    private static Row Analyze(Source source)
    {
        Beatmap map = BeatmapParser.Load(source.Path);
        string legacyPrimary = map.GameplayProfile.Identity.Primary;
        string legacySecondary = map.GameplayProfile.Identity.Secondary;
        double rating = map.BeatInsightRating;
        GameplayIdentityV3Profile identity = GameplayIdentityV3Analyzer.Analyze(map);
        Assert.Equal(legacyPrimary, map.GameplayProfile.Identity.Primary);
        Assert.Equal(legacySecondary, map.GameplayProfile.Identity.Secondary);
        Assert.Equal(rating, map.BeatInsightRating);
        return new(DisplayName(map, source.Name), source.Path, map, identity, legacyPrimary, legacySecondary);
    }

    private static void AppendHeader(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("# Gameplay Identity V3 — Targeted Calibration Confirmation\n");
        report.AppendLine("Post-calibration validation. Historical Identity, Rating, Fusion V1, Section Analysis V2, Skill Pressure V2, Pattern Analysis, TechV2, UI, cache and ML remain unchanged. No map was downloaded.\n");
        report.AppendLine($"Corpus: **{rows.Count} maps**, **{rows.Sum(row => row.Map.HitObjects.Count)} objects**.\n");
    }

    private static void AppendImplementation(StringBuilder report)
    {
        report.AppendLine("## Implementation contract\n");
        report.AppendLine("- Structure: `60% global structural signal + 25% section presence + 15% family pattern support`.");
        report.AppendLine("- Primary/Secondary are restricted by type to Jump, Stream, Tech and ClassicMixed.");
        report.AppendLine("- Secondary requires score >= 25 and relative strength >= 0.55.");
        report.AppendLine("- ClassicMixed requires a significant close pair (margin <= 8, secondary >= 25, relative >= 0.55), at least two Jump/Stream/Tech section families represented at >= 15%, or no clear signal. Neutral/Mixed sections are tracked separately and never count as a structural family.");
        report.AppendLine("- MainSkills: score >= 30, at most 3; Precision excluded when unavailable; Endurance and RhythmComplexity retain PARTIAL status.");
        report.AppendLine("- MainPatterns: exact `0.50 Coverage + 0.30 Confidence + 0.20 Intensity`, renormalized when intensity is missing; score >= max(40, strongest * 0.55), at most 5.");
        report.AppendLine("- Confidence: `0.50 Structure + 0.30 Skill + 0.20 Pattern`; diagnostics never alter the result.\n");
    }

    private static void AppendMaps(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## Per-map results\n");
        report.AppendLine("|Map|Jump|Stream|Tech|Mixed|Primary|Secondary|Main Skills|Main Patterns|Sections J/S/T/N|Represented|Tr|Sp|Confidence|Flags|");
        report.AppendLine("|---|---:|---:|---:|---:|---|---|---|---|---|---:|---:|---:|---:|---|");
        foreach (Row row in rows.OrderByDescending(row => row.Identity.GlobalIdentityConfidence))
        {
            GameplayIdentityV3Profile id = row.Identity;
            string skills = id.MainSkills.Count == 0 ? "None" : string.Join(", ", id.MainSkills.Select(skill => $"{skill.Name} {skill.Score:F1} {skill.Status}"));
            string patterns = id.MainPatterns.Count == 0 ? "None" : string.Join(", ", id.MainPatterns.Select(pattern => $"{pattern.Family}/{pattern.Name} {pattern.Score:F1}"));
            string sections = $"{id.SectionContext.JumpSectionRatio:P0}/{id.SectionContext.StreamSectionRatio:P0}/{id.SectionContext.TechSectionRatio:P0}/{id.SectionContext.MixedSectionRatio:P0}";
            report.AppendLine($"|{Cell(row.Name)}|{id.JumpStructureScore:F1}|{id.StreamStructureScore:F1}|{id.TechStructureScore:F1}|{id.ClassicMixedScore:F1}|{id.PrimaryStructure}|{id.SecondaryStructure?.ToString() ?? "None"}|{skills}|{patterns}|{sections}|{id.SectionContext.RepresentedStructuralFamilyCount}|{id.SectionContext.GameplayTransitionCount}|{id.SectionContext.DifficultySpikeCount}|{id.GlobalIdentityConfidence:F3}|{(id.Flags.Count == 0 ? "None" : string.Join(", ", id.Flags))}|");
        }
        report.AppendLine();
    }

    private static void AppendControls(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## Historical controls\n");
        string[] controls = ["Black Rover", "understand me", "Tower Of Heaven", "Road of Resistance", "Wings of Courage", "Connect", "From Under Cover"];
        foreach (string control in controls)
        {
            Source? explicitControl = Controls.FirstOrDefault(source => source.Name == control);
            Row? row = explicitControl is null
                ? rows.FirstOrDefault(candidate => candidate.Name.Contains(control, StringComparison.OrdinalIgnoreCase) || candidate.Path.Contains(control, StringComparison.OrdinalIgnoreCase))
                : rows.FirstOrDefault(candidate => candidate.Path == explicitControl.Path);
            if (row is null) continue;
            GameplayIdentityV3Profile id = row.Identity;
            double[] scores = [id.JumpStructureScore, id.StreamStructureScore, id.TechStructureScore];
            Array.Sort(scores); Array.Reverse(scores);
            report.AppendLine($"### {row.Name}\n");
            report.AppendLine($"Legacy remains `{row.LegacyPrimary}/{row.LegacySecondary}`. V3 scores: Jump {id.JumpStructureScore:F1}, Stream {id.StreamStructureScore:F1}, Tech {id.TechStructureScore:F1}, Mixed {id.ClassicMixedScore:F1}; margin {scores[0] - scores[1]:F1}; Primary **{id.PrimaryStructure}**, Secondary **{id.SecondaryStructure?.ToString() ?? "None"}**.");
            report.AppendLine($"Main skills: {(id.MainSkills.Count == 0 ? "None" : string.Join(", ", id.MainSkills.Select(skill => $"{skill.Name} {skill.Score:F1}")))}. Main patterns: {(id.MainPatterns.Count == 0 ? "None" : string.Join(", ", id.MainPatterns.Select(pattern => $"{pattern.Name} {pattern.Score:F1}")))}.");
            report.AppendLine($"Sections J/S/T/Neutral-Mixed: {id.SectionContext.JumpSectionRatio:P1}/{id.SectionContext.StreamSectionRatio:P1}/{id.SectionContext.TechSectionRatio:P1}/{id.SectionContext.MixedSectionRatio:P1}; represented structural families {id.SectionContext.RepresentedStructuralFamilyCount}; real diversity {id.SectionContext.RealStructuralDiversity:F3}; timeline: {string.Join(" -> ", id.SectionContext.CompressedStructureTimeline)}; transitions {id.SectionContext.GameplayTransitionCount}; spikes {id.SectionContext.DifficultySpikeCount}; confidence {id.GlobalIdentityConfidence:F3}.\n");
        }
    }

    private static void AppendDistributions(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## Global distributions\n");
        report.AppendLine("Primary before: " + Distribution(rows.Select(BaselinePrimary)) + ".");
        report.AppendLine("Primary after: " + Distribution(rows.Select(row => row.Identity.PrimaryStructure)) + ".");
        int beforeMixed = rows.Count(row => BaselinePrimary(row) == GameplayStructureKind.ClassicMixed);
        int afterMixed = rows.Count(row => row.Identity.PrimaryStructure == GameplayStructureKind.ClassicMixed);
        report.AppendLine($"ClassicMixed before/after: **{beforeMixed}/{afterMixed}**.");
        string[] changed = rows.Where(row => BaselinePrimary(row) != row.Identity.PrimaryStructure)
            .Select(row => $"{row.Name}: {BaselinePrimary(row)}->{row.Identity.PrimaryStructure}").ToArray();
        report.AppendLine($"Changed maps ({changed.Length}): {(changed.Length == 0 ? "None" : string.Join("; ", changed))}.");
        report.AppendLine("Secondary: " + string.Join("; ", rows.GroupBy(row => row.Identity.SecondaryStructure?.ToString() ?? "None").OrderBy(group => group.Key).Select(group => $"{group.Key}={group.Count()}")) + ".");
        report.AppendLine($"MainSkills count: mean **{rows.Average(row => row.Identity.MainSkills.Count):F2}**, min **{rows.Min(row => row.Identity.MainSkills.Count)}**, max **{rows.Max(row => row.Identity.MainSkills.Count)}**.");
        int[] patternCounts = rows.Select(row => row.Identity.MainPatterns.Count).Order().ToArray();
        report.AppendLine($"MainPatterns before: mean **5.00**, median **5.00**, distribution 0=0; 1=0; 2=0; 3=0; 4=0; 5={rows.Count}.");
        report.AppendLine($"MainPatterns after: mean **{patternCounts.Average():F2}**, median **{Median(patternCounts.Select(value => (double)value).ToArray()):F2}**, distribution {CountDistribution(patternCounts)}; maps at 5 **{patternCounts.Count(value => value == 5)}**; maps at 0 **{patternCounts.Count(value => value == 0)}**.");
        double[] confidence = rows.Select(row => row.Identity.GlobalIdentityConfidence).Order().ToArray();
        report.AppendLine($"Confidence: mean **{confidence.Average():F3}**, median **{Median(confidence):F3}**, min **{confidence.Min():F3}**, max **{confidence.Max():F3}**.");
        report.AppendLine($"Confidence buckets: 0-.40={confidence.Count(value => value < .40)}; .40-.60={confidence.Count(value => value >= .40 && value < .60)}; .60-.80={confidence.Count(value => value >= .60 && value < .80)}; .80-1={confidence.Count(value => value >= .80)}.\n");
    }

    private static void AppendMixed(StringBuilder report, IReadOnlyList<Row> rows)
    {
        report.AppendLine("## ClassicMixed forensic\n");
        Row[] mixed = rows.Where(row => row.Identity.PrimaryStructure == GameplayStructureKind.ClassicMixed).ToArray();
        if (mixed.Length == 0) { report.AppendLine("No ClassicMixed result.\n"); return; }
        report.AppendLine("|Map|Top1|Top2|Margin|Section diversity|Transitions|Pattern families|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---|");
        foreach (Row row in mixed)
        {
            double[] scores = [row.Identity.JumpStructureScore, row.Identity.StreamStructureScore, row.Identity.TechStructureScore];
            Array.Sort(scores); Array.Reverse(scores);
            double diversity = row.Identity.SectionContext.RealStructuralDiversity;
            string families = string.Join(", ", row.Identity.MainPatterns.Select(pattern => pattern.Family).Distinct());
            report.AppendLine($"|{Cell(row.Name)}|{scores[0]:F1}|{scores[1]:F1}|{scores[0] - scores[1]:F1}|{diversity:F3}|{row.Identity.SectionContext.GameplayTransitionCount}|{(string.IsNullOrEmpty(families) ? "None" : families)}|");
        }
        report.AppendLine();
    }

    private static void AppendDecisions(StringBuilder report, IReadOnlyList<Row> rows)
    {
        double mixedRatio = rows.Count(row => row.Identity.PrimaryStructure == GameplayStructureKind.ClassicMixed) / (double)rows.Count;
        double meanConfidence = rows.Average(row => row.Identity.GlobalIdentityConfidence);
        Row[] suspectMixed = rows.Where(row =>
        {
            if (row.Identity.PrimaryStructure != GameplayStructureKind.ClassicMixed) return false;
            double[] scores = [row.Identity.JumpStructureScore, row.Identity.StreamStructureScore, row.Identity.TechStructureScore];
            Array.Sort(scores); Array.Reverse(scores);
            int representedFamilies = new[]
            {
                row.Identity.SectionContext.JumpSectionRatio,
                row.Identity.SectionContext.StreamSectionRatio,
                row.Identity.SectionContext.TechSectionRatio
            }.Count(value => value >= .15);
            return scores[0] - scores[1] > 20 && representedFamilies < 2;
        }).ToArray();
        Row[] baselineSuspectMixed = rows.Where(row =>
        {
            if (BaselinePrimary(row) != GameplayStructureKind.ClassicMixed) return false;
            double[] scores = [row.Identity.JumpStructureScore, row.Identity.StreamStructureScore, row.Identity.TechStructureScore];
            Array.Sort(scores); Array.Reverse(scores);
            return scores[0] - scores[1] > 20 && row.Identity.SectionContext.RepresentedStructuralFamilyCount < 2;
        }).ToArray();
        bool patternCapSaturated = rows.All(row => row.Identity.MainPatterns.Count == GameplayIdentityV3Analyzer.MaximumMainPatternCount);
        string structure = suspectMixed.Length > 0 || mixedRatio > .75 ? "INVESTIGATE" : "FREEZE";
        string skills = rows.Any(row => row.Identity.MainSkills.Count > 3) ? "STRUCTURAL ISSUE" : "FREEZE";
        string patterns = rows.Any(row => row.Identity.MainPatterns.Count > 5)
            ? "STRUCTURAL ISSUE"
            : patternCapSaturated ? "INVESTIGATE" : "FREEZE";
        string sections = rows.All(row => row.Identity.SectionContext.CompressedStructureTimeline.Count > 0) ? "FREEZE" : "INVESTIGATE";
        string confidence = meanConfidence < .50 ? "INVESTIGATE" : "FREEZE";
        bool ready = new[] { structure, skills, patterns, sections, confidence }.All(decision => decision == "FREEZE");
        report.AppendLine("## Decisions\n");
        report.AppendLine($"- STRUCTURE: **{structure}** — ClassicMixed {mixedRatio:P1}; {suspectMixed.Length} Mixed results have >20 points top1/top2 margin with fewer than two represented structural families. Neutral/Mixed sections are tracked separately and no longer count as structural diversity. No forbidden structural type is representable.");
        report.AppendLine($"  Baseline suspect false-Mixed corrected: **{baselineSuspectMixed.Count(row => row.Identity.PrimaryStructure != GameplayStructureKind.ClassicMixed)}/{baselineSuspectMixed.Length}** ({string.Join("; ", baselineSuspectMixed.Select(row => row.Name))}).");
        report.AppendLine($"- SKILL SELECTION: **{skills}** — thresholded, unavailable Precision excluded, maximum three.");
        report.AppendLine($"- PATTERN SELECTION: **{patterns}** — exact weighted strength is unchanged; absolute 40 plus relative 0.55 selection leaves {rows.Count(row => row.Identity.MainPatterns.Count == GameplayIdentityV3Analyzer.MaximumMainPatternCount)}/{rows.Count} maps at the cap and {rows.Count(row => row.Identity.MainPatterns.Count == 0)} at zero.");
        report.AppendLine($"- SECTION INTEGRATION: **{sections}** — ratios, transitions, spikes and compressed timeline are descriptive inputs.");
        report.AppendLine($"- CONFIDENCE: **{confidence}** — corpus mean {meanConfidence:F3}.\n");
        report.AppendLine($"## Global conclusion\n\n**{(ready ? "GAMEPLAY IDENTITY V3 READY TO FREEZE" : "TARGETED CALIBRATION REQUIRED")}**\n");
        report.AppendLine("Targeted calibration only: local Neutral/Mixed sections were removed as automatic hybrid evidence, the 15% represented-family rule was applied, and MainPatterns now use absolute 40 plus relative 0.55 eligibility. StructureScore, PatternStrength, Skill selection, Section Analysis V2 and every Pattern analyzer remain unchanged. Known limitations: Section Analysis remains fixed-window; Endurance and RhythmComplexity remain PARTIAL; Precision remains unavailable; corpus labels are controls, not forced truth.");
    }

    private static GameplayStructureKind BaselinePrimary(Row row)
    {
        GameplayIdentityV3Profile id = row.Identity;
        (GameplayStructureKind Kind, double Score)[] ranked =
        [
            (GameplayStructureKind.Jump, id.JumpStructureScore),
            (GameplayStructureKind.Stream, id.StreamStructureScore),
            (GameplayStructureKind.Tech, id.TechStructureScore)
        ];
        ranked = ranked.OrderByDescending(item => item.Score).ThenBy(item => item.Kind).ToArray();
        double margin = ranked[0].Score - ranked[1].Score;
        bool close = ranked[1].Score >= 25 && margin <= 8;
        double maximumRatio = new[] { id.SectionContext.JumpSectionRatio, id.SectionContext.StreamSectionRatio, id.SectionContext.TechSectionRatio }.Max();
        bool oldDiversity = id.SectionContext.MixedSectionRatio >= .35
            || (maximumRatio <= .60
                && new[] { id.SectionContext.JumpSectionRatio, id.SectionContext.StreamSectionRatio, id.SectionContext.TechSectionRatio }.Count(value => value >= .30) >= 2);
        return ranked[0].Score < 25 || close || oldDiversity ? GameplayStructureKind.ClassicMixed : ranked[0].Kind;
    }

    private static string Distribution(IEnumerable<GameplayStructureKind> values) =>
        string.Join("; ", values.GroupBy(value => value).OrderBy(group => group.Key).Select(group => $"{group.Key}={group.Count()}"));

    private static string CountDistribution(IEnumerable<int> values) =>
        string.Join("; ", Enumerable.Range(0, 6).Select(count => $"{count}={values.Count(value => value == count)}"));

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BeatInsight.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("BeatInsight root not found.");
    }
    private static string DisplayName(Beatmap map, string fallback) => string.IsNullOrWhiteSpace(map.Title) ? fallback : $"{map.Title} [{map.Version}]";
    private static string Cell(string value) => value.Replace("|", "/");
    private static double Median(IReadOnlyList<double> values) => values.Count % 2 == 1 ? values[values.Count / 2] : (values[values.Count / 2 - 1] + values[values.Count / 2]) / 2;
    private sealed record Source(string Name, string Path);
    private sealed record Row(string Name, string Path, Beatmap Map, GameplayIdentityV3Profile Identity, string LegacyPrimary, string LegacySecondary);
}
