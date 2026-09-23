using System.Globalization;
using System.IO;
using System.Text;
using BeatInsight.Analysis;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Models;
using BeatInsight.Models.JumpPatterns;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

/// <summary>Fixture-only forensic report. It observes the Alternating Spacing policy without changing it.</summary>
public sealed class AlternatingSpacingJumpForensicTests(ITestOutputHelper output)
{
    private const double MinimumDistance = 60;
    private const double MinimumDelta = 10;
    private static readonly string[] FixtureNames =
    [
        "(can you) understand me [uhh].osu", "Ashioto Tarte Tatin [Koori's Insane].osu",
        "Exit This Earth's Atmosphere [Primordial Nucleosynthesis].osu", "FREEDOM DiVE [Arles].osu",
        "FREEDOM DiVE [FrenZ's Insane].osu", "Frozen [Collab Insane].osu", "Kira Killer [Mocaotic's Insane].osu",
        "Snow Goose [Extra].osu", "Tower Of Heaven [Extra].osu", "forever we can make it! [Kudowari's Expert].osu",
        "Vickeblanka - Black Rover (TV Size) (Sotarks) [Extreme].osu"
    ];

    [Fact]
    public void RequestedFixtures_ReportOnlyValidatedAlternatingSpacing()
    {
        var report = new StringBuilder("# Alternating Spacing Jump forensic\n\n");
        report.AppendLine("Policy observed unchanged: circle-only, shared Stream mask, Jump links, 60px minimum, five links, meaningful delta >=10px, alternation >=0.75, contrast >=0.20, amplitude coherence >=0.50.");
        report.AppendLine();
        report.AppendLine("|Map|Sequences|Objects|Coverage %|Longest|Avg distance|Avg high|Avg low|Avg contrast|Avg alternation|Avg oscillation|Expanding first|Contracting first|Confidence|Jump links|Alternating links|Link ratio %|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        var details = new StringBuilder();

        foreach (string fixture in FixtureNames)
        {
            Beatmap map = FixtureLoader.Load(fixture);
            GameplayProfile profile = map.GameplayProfile;
            AlternatingSpacingJumpAnalysis result = JumpPatternAnalyzer.AnalyzeAlternatingSpacing(map.HitObjects, profile);
            IReadOnlyList<JumpPatternMatch> allMatches = JumpPatternAnalyzer.Analyze(map.HitObjects, profile);
            bool[] streamMask = BuildStreamMask(map.HitObjects.Count, profile.StreamSequences);
            var objects = result.Sequences.SelectMany(s => Enumerable.Range(s.StartObjectIndex, s.ObjectCount)).ToHashSet();
            var links = result.Sequences.SelectMany(s => Enumerable.Range(s.StartObjectIndex, s.LinkCount).Select(i => (i, i + 1))).ToHashSet();
            int jumpLinks = CountQualifiedJumpLinks(map.HitObjects, streamMask);

            Assert.Equal(objects.Count, result.AlternatingSpacingObjectCount);
            Assert.Equal(objects.Count / (double)map.HitObjects.Count, result.Coverage, 12);
            Assert.DoesNotContain(objects, i => streamMask[i]);
            Assert.All(result.Sequences, s => AssertInvariant(s, map.HitObjects, streamMask));
            Assert.InRange(result.Confidence, 0, 1);

            report.AppendLine(FormattableString.Invariant($"|{Path.GetFileNameWithoutExtension(fixture)}|{result.AlternatingSpacingSequenceCount}|{result.AlternatingSpacingObjectCount}|{result.Coverage * 100:F3}|{result.LongestSequence}|{result.AverageDistance:F2}|{result.AverageHighSpacing:F2}|{result.AverageLowSpacing:F2}|{result.AverageSpacingContrast:F4}|{result.AverageAlternationConsistency:F4}|{result.AverageOscillationMagnitudeConsistency:F4}|{result.ExpandingFirstSequenceCount}|{result.ContractingFirstSequenceCount}|{result.Confidence:F4}|{jumpLinks}|{links.Count}|{(jumpLinks == 0 ? 0 : links.Count * 100d / jumpLinks):F3}|"));
            AppendDetails(details, map, result, allMatches, jumpLinks, links.Count);
            AppendOscillationOnlyRejections(details, map, streamMask);
        }

        report.Append(details);
        string reportPath = Path.Combine(AppContext.BaseDirectory, "alternating-spacing-jump-forensic.md");
        File.WriteAllText(reportPath, report.ToString());
        output.WriteLine(report.ToString());
        output.WriteLine($"Report: {reportPath}");
    }

    private static void AppendDetails(StringBuilder text, Beatmap map, AlternatingSpacingJumpAnalysis result,
        IReadOnlyList<JumpPatternMatch> allMatches, int jumpLinks, int alternatingLinks)
    {
        text.AppendLine($"\n## {map.Title} [{map.Version}]\n");
        text.AppendLine($"Qualified Jump links={jumpLinks}; unique Alternating links={alternatingLinks}; ratio={(jumpLinks == 0 ? 0 : (double)alternatingLinks / jumpLinks):P2}.");
        if (result.Sequences.Count == 0) text.AppendLine("No validated Alternating Spacing sequence.");
        foreach (AlternatingSpacingJumpSequence s in result.Sequences.OrderBy(s => s.StartObjectIndex))
        {
            string signs = string.Join(", ", s.Deltas.Select(d => Math.Abs(d) < MinimumDelta ? "0" : d > 0 ? "+" : "-"));
            string meaningful = string.Join(", ", s.Deltas.Select(d => Math.Abs(d) < MinimumDelta
                ? $"{d.ToString("+0.00;-0.00", CultureInfo.InvariantCulture)} (neutral)"
                : d.ToString("+0.00;-0.00", CultureInfo.InvariantCulture)));
            text.AppendLine($"Sequence [{s.StartObjectIndex}..{s.EndObjectIndex}] {Time(s.StartTime)} -> {Time(s.EndTime)}; start={s.Start}; objects={s.ObjectCount}; distances=[{Values(s.Distances)}]; deltas=[{Values(s.Deltas)}]; signs=[{signs}]; significantDeltas=[{meaningful}]; high={s.HighSpacingAverage:F3}; low={s.LowSpacingAverage:F3}; ratio={s.HighLowRatio:F4}; contrast={s.SpacingContrast:F4}; alternation={s.AlternationConsistency:F4}; oscillation={s.OscillationMagnitudeConsistency:F4}; confidence={s.Confidence:F4}; coexistence={Coexistence(s, allMatches)}");
        }
    }

    private static void AssertInvariant(AlternatingSpacingJumpSequence s, IReadOnlyList<HitObject> objects, bool[] streamMask)
    {
        Assert.True(s.LinkCount >= 5);
        Assert.Equal(s.LinkCount - 1, s.Deltas.Count);
        Assert.InRange(s.AlternationConsistency, .75, 1);
        Assert.True(s.SpacingContrast >= .20);
        Assert.True(s.OscillationMagnitudeConsistency >= .50);
        Assert.InRange(s.Confidence, 0, 1);
        for (int i = s.StartObjectIndex; i <= s.EndObjectIndex; i++) Assert.False(streamMask[i]);
        for (int i = 0; i < s.LinkCount; i++)
        {
            Assert.True(GameplayAnalyzer.IsJumpLink(objects[s.StartObjectIndex + i], objects[s.StartObjectIndex + i + 1], out double distance));
            Assert.True(distance >= MinimumDistance);
            Assert.Equal(distance, s.Distances[i], 9);
        }
    }

    private static int CountQualifiedJumpLinks(IReadOnlyList<HitObject> objects, bool[] streamMask)
    {
        int count = 0;
        for (int i = 0; i + 1 < objects.Count; i++)
            if ((objects[i].Type & 1) != 0 && (objects[i + 1].Type & 1) != 0 && !streamMask[i] && !streamMask[i + 1]
                && GameplayAnalyzer.IsJumpLink(objects[i], objects[i + 1], out double distance) && distance >= MinimumDistance) count++;
        return count;
    }

    private static string Coexistence(AlternatingSpacingJumpSequence sequence, IReadOnlyList<JumpPatternMatch> matches)
    {
        JumpPatternType[] types = [JumpPatternType.OneTwo, JumpPatternType.Spaced, JumpPatternType.Linear, JumpPatternType.Arc,
            JumpPatternType.WideAngle, JumpPatternType.Sharp, JumpPatternType.Accelerating, JumpPatternType.Decelerating];
        return string.Join(" + ", matches.Where(m => types.Contains(m.Type) && Math.Min(sequence.EndObjectIndex, m.EndObjectIndex) >= Math.Max(sequence.StartObjectIndex, m.StartObjectIndex))
            .Select(m => m.Type.ToString()).Distinct().DefaultIfEmpty("None"));
    }

    // Test-only five-link forensic windows. This does not reproduce or alter runtime segmentation;
    // it isolates the amplitude-coherence guard among otherwise qualifying local observations.
    private static void AppendOscillationOnlyRejections(StringBuilder text, Beatmap map, bool[] streamMask)
    {
        var links = new List<(int Index, double Distance)>();
        var rejected = new List<string>();
        void Flush()
        {
            for (int start = 0; start + 5 <= links.Count; start++)
            {
                double[] distances = links.Skip(start).Take(5).Select(link => link.Distance).ToArray();
                double[] deltas = distances.Zip(distances.Skip(1), (a, b) => b - a).ToArray();
                int[] signs = deltas.Select(Sign).ToArray();
                double alternation = signs.Zip(signs.Skip(1), (a, b) => a != 0 && b != 0 && a != b).Count(x => x) / 3d;
                double[] magnitudes = deltas.Where(d => Sign(d) != 0).Select(Math.Abs).ToArray();
                double contrast = magnitudes.Length == 0 ? 0 : magnitudes.Average() / distances.Average();
                double coherence = magnitudes.Length == 0 ? 0 : magnitudes.Min() / magnitudes.Max();
                if (alternation >= .75 && contrast >= .20 && coherence < .50)
                {
                    int from = links[start].Index;
                    int to = links[start + 4].Index + 1;
                    rejected.Add($"[{from}..{to}] {Time(map.HitObjects[from].Time)} -> {Time(map.HitObjects[to].Time)}; distances=[{Values(distances)}]; alternation={alternation:F4}; contrast={contrast:F4}; oscillation={coherence:F4}");
                }
            }
            links.Clear();
        }

        for (int i = 0; i + 1 < map.HitObjects.Count; i++)
        {
            if ((map.HitObjects[i].Type & 1) != 0 && (map.HitObjects[i + 1].Type & 1) != 0 && !streamMask[i] && !streamMask[i + 1]
                && GameplayAnalyzer.IsJumpLink(map.HitObjects[i], map.HitObjects[i + 1], out double distance) && distance >= MinimumDistance)
                links.Add((i, distance));
            else
                Flush();
        }
        Flush();

        text.AppendLine(rejected.Count == 0
            ? "Oscillation-only rejections: none in five-link qualified Jump windows."
            : $"Oscillation-only rejections (alternation/contrast pass, coherence fails): {string.Join("; ", rejected)}");
    }

    private static bool[] BuildStreamMask(int count, IReadOnlyList<PatternSequence> sequences)
    {
        var mask = new bool[count];
        foreach (PatternSequence s in sequences)
            for (int i = Math.Max(0, s.StartObjectIndex); i <= Math.Min(count - 1, s.EndObjectIndex); i++) mask[i] = true;
        return mask;
    }

    private static string Values(IEnumerable<double> values) => string.Join(", ", values.Select(v => v.ToString("F3", CultureInfo.InvariantCulture)));
    private static string Time(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds).ToString(@"mm\:ss\.fff");
    private static int Sign(double delta) => Math.Abs(delta) >= MinimumDelta ? Math.Sign(delta) : 0;
}
