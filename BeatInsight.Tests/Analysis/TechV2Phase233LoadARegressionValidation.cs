using System.Globalization;
using System.IO;
using System.Text;

namespace BeatInsight.Tests.Analysis;

internal enum Phase233Class { ExpectedTech, MixedLightTech, HardNegative }

internal sealed record Phase233Result(
    string Name, Phase233Class Category, double Baseline, double LoadA, double Delta,
    int Strong, int Sustained, double AvgSliderEvidence, double PctSliderEvidence);

internal static class TechV2Phase233LoadARegressionValidation
{
    private static readonly (string Name, Phase233Class Category, string? Fixture)[] Targets =
    [
        ("Silhouette", Phase233Class.HardNegative, null),
        ("Move That Body", Phase233Class.MixedLightTech, null),
        ("Premonition", Phase233Class.MixedLightTech, null),
        ("GLORIOUS DAYS", Phase233Class.ExpectedTech, null),
        ("Kira Killer", Phase233Class.ExpectedTech, "Kira Killer [Mocaotic's Insane].osu"),
        ("The World Ends Now", Phase233Class.ExpectedTech, null),
        ("Frozen", Phase233Class.ExpectedTech, "Frozen [Collab Insane].osu"),
        ("Exit", Phase233Class.ExpectedTech, "Exit This Earth's Atmosphere [Primordial Nucleosynthesis].osu"),
        ("Connect", Phase233Class.MixedLightTech, null),
        ("Road of Resistance", Phase233Class.HardNegative, null),
        ("Tower of Heaven", Phase233Class.HardNegative, "Tower Of Heaven [Extra].osu"),
        ("(can you) understand me?", Phase233Class.HardNegative, "(can you) understand me [uhh].osu"),
        ("Wings of Courage", Phase233Class.HardNegative, null),
    ];

    internal static IReadOnlyList<Phase233Result> Run()
    {
        var results = new List<Phase233Result>();
        foreach (var target in Targets)
        {
            string? path = Resolve(target.Name, target.Fixture);
            if (path is null) continue;
            var baseline = TechV2SliderLoadCalibrationAudit.Evaluate(target.Name, target.Name, path, target.Category == Phase233Class.ExpectedTech, LoadAggregator.Baseline);
            var loadA = TechV2SliderLoadCalibrationAudit.Evaluate(target.Name, target.Name, path, target.Category == Phase233Class.ExpectedTech, LoadAggregator.LoadA_Sum2);
            results.Add(new Phase233Result(target.Name, target.Category, baseline.PresencePercent, loadA.PresencePercent,
                loadA.PresencePercent - baseline.PresencePercent, loadA.StrongCount, loadA.SustainedCount,
                loadA.AvgSliderEvidence, loadA.PctSliderGte045));
        }
        return results;
    }

    private static string? Resolve(string name, string? fixture)
    {
        if (fixture is not null)
        {
            string local = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps", fixture);
            if (File.Exists(local)) return local;
        }
        var known = TechV2SliderLoadCalibrationAudit.Corpus.FirstOrDefault(c => c.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(known.Path) && File.Exists(known.Path)) return known.Path;
        if (Directory.Exists(@"D:\osu!\Songs"))
            return Directory.EnumerateFiles(@"D:\osu!\Songs", "*.osu", SearchOption.AllDirectories)
                .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p).Contains(name, StringComparison.OrdinalIgnoreCase));
        return null;
    }

    internal static string FormatReport(IReadOnlyList<Phase233Result> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("TECH V2 PHASE 2.33 LOAD_A REGRESSION VALIDATION (TEST-ONLY / SHADOW)");
        sb.AppendLine("TechnicalSliderLoad = clamp(sum(PTechnicalityScore) / 2.0, 0, 1)");
        sb.AppendLine("Map | Class | Baseline | LOAD_A | Delta | Strong | Sustained | Avg SliderEvidence | % >= 0.45");
        foreach (var r in results)
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} | {1} | {2:F2}% | {3:F2}% | {4:+0.00;-0.00;0.00}% | {5} | {6} | {7:F3} | {8:F1}%", r.Name, r.Category, r.Baseline, r.LoadA, r.Delta, r.Strong, r.Sustained, r.AvgSliderEvidence, r.PctSliderEvidence));
        int hard = results.Count(r => r.Category == Phase233Class.HardNegative);
        int hardPreserved = results.Count(r => r.Category == Phase233Class.HardNegative && r.LoadA <= 10.0);
        int tech = results.Count(r => r.Category == Phase233Class.ExpectedTech);
        int techPreserved = results.Count(r => r.Category == Phase233Class.ExpectedTech && r.LoadA > 0);
        sb.AppendLine($"LOAD_A REGRESSION STATUS = {(hardPreserved == hard && techPreserved == tech ? "SAFE_TO_ENABLE" : "NEEDS_MORE_CALIBRATION")}");
        sb.AppendLine($"FALSE_POSITIVE_REGRESSIONS = {hard - hardPreserved}");
        sb.AppendLine($"HARD_NEGATIVES_PRESERVED = {hardPreserved}/{hard}");
        sb.AppendLine($"TRUE_TECH_PRESERVED = {techPreserved}/{tech}");
        sb.AppendLine("TECH V2 PHASE 2.33 LOAD_A REGRESSION VALIDATION COMPLETE");
        sb.AppendLine("NO RUNTIME CHANGE"); sb.AppendLine("NO ML"); sb.AppendLine("NO COMMIT");
        return sb.ToString();
    }
}
