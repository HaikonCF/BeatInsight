using BeatInsight.Analysis.Identity;
using BeatInsight.Analysis.Sections;
using BeatInsight.Analysis.SkillPressure;
using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.Identity;
using BeatInsight.Models.Rating;
using BeatInsight.Models.Sections;
using BeatInsight.Models.SkillPressure;

namespace BeatInsight.Analysis.Rating;

/// <summary>
/// Parallel Rating V2. It consumes frozen analytical outputs read-only and never reads
/// osu! Star Rating, historical BeatInsightRating, Community tags, cache or ML labels.
/// </summary>
public static class BeatInsightRatingV2Analyzer
{
    public const double BaseWeight = .30;
    public const double SustainedWeight = .25;
    public const double PeakWeight = .15;
    public const double SkillPressureWeight = .12;
    public const double PatternWeight = .10;
    public const double EnduranceWeight = .08;
    public const double SpecializationCap = 5;

    public static BeatInsightRatingV2 Analyze(Beatmap beatmap)
    {
        ArgumentNullException.ThrowIfNull(beatmap);
        Beatmap analyticalMap = CreateAnalyticalMap(beatmap);
        GameplayProfile gameplay = GameplayAnalyzer.Analyze(analyticalMap);
        SectionAnalysisResult sections = SectionAnalysisV2Analyzer.Analyze(analyticalMap);
        SkillPressureProfile pressure = SkillPressureAnalyzer.Analyze(analyticalMap);
        GameplayIdentityV3Profile identity = GameplayIdentityV3Analyzer.Analyze(analyticalMap);
        double durationSeconds = analyticalMap.HitObjects.Count < 2
            ? 0
            : Math.Max(0, analyticalMap.HitObjects[^1].Time - analyticalMap.HitObjects[0].Time) / 1000.0;
        RatingV2PatternEvidence[] patterns = identity.MainPatterns.Select(pattern => new RatingV2PatternEvidence(
            pattern.Name,
            pattern.Family,
            pattern.Score,
            Signal(.5 * pattern.Coverage + .5 * pattern.Confidence))).ToArray();
        BeatInsightRatingV2 result = Analyze(new BeatInsightRatingV2Input(
            analyticalMap.HitObjects.Count,
            durationSeconds,
            gameplay.AimScore,
            gameplay.SpeedScore,
            gameplay.ReadScore,
            gameplay.TechScore,
            pressure,
            sections,
            patterns,
            identity.GlobalIdentityConfidence));
        GameplayDebug.RatingV2(result, gameplay);
        return result;
    }

    private static Beatmap CreateAnalyticalMap(Beatmap source) => new()
    {
        Title = source.Title,
        Artist = source.Artist,
        Creator = source.Creator,
        Version = source.Version,
        Length = source.Length,
        BPM = source.BPM,
        MaxCombo = source.MaxCombo,
        AR = source.AR,
        OD = source.OD,
        CS = source.CS,
        HP = source.HP,
        TimingPoints = source.TimingPoints,
        HitObjects = source.HitObjects,
        CircleCount = source.CircleCount,
        SliderCount = source.SliderCount,
        SpinnerCount = source.SpinnerCount,
        SliderMultiplier = source.SliderMultiplier,
        SliderTickRate = source.SliderTickRate
    };

    public static BeatInsightRatingV2 Analyze(BeatInsightRatingV2Input input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.SkillPressure);
        ArgumentNullException.ThrowIfNull(input.Sections);
        ArgumentNullException.ThrowIfNull(input.Patterns);
        if (input.ObjectCount <= 0) return BeatInsightRatingV2.Empty;

        double[] fundamentals =
        [
            Score(input.AimScore), Score(input.SpeedScore),
            Score(input.ReadingScore), Score(input.TechScore)
        ];
        double[] rankedSkills = fundamentals.OrderByDescending(value => value).ToArray();
        double baseDifficulty = .50 * rankedSkills[0] + .30 * rankedSkills[1] + .20 * rankedSkills[2];
        SkillPressureMetric[] significantPressure = PressureMetrics(input.SkillPressure)
            .Where(metric => metric.Score!.Value >= 30).ToArray();
        double skillPressure = WeightedTop(significantPressure.Select(metric => metric.Score!.Value));

        GameplaySectionV2Analysis[] playableSections = input.Sections.Sections
            .Where(section => section.ObjectCount > 0).ToArray();
        double[] difficulty = playableSections.Select(section => Score(section.LocalDifficultyScore)).Order().ToArray();
        double meanSection = difficulty.Length == 0 ? 0 : difficulty.Average();
        double maxSection = difficulty.Length == 0 ? 0 : difficulty.Max();
        double percentile90 = Percentile(difficulty, .90);
        double peakDifficulty = .60 * percentile90 + .40 * maxSection;
        double hardThreshold = Math.Max(30, peakDifficulty * .75);
        bool[] hardSections = playableSections.Select(section => section.LocalDifficultyScore >= hardThreshold).ToArray();
        double highSectionRatio = hardSections.Length == 0 ? 0 : hardSections.Count(value => value) / (double)hardSections.Length;
        double hardMean = hardSections.Any(value => value)
            ? playableSections.Where((_, index) => hardSections[index]).Average(section => section.LocalDifficultyScore)
            : 0;
        int hardPassages = CountHardPassages(hardSections);
        double repeatedSignal = hardPassages <= 1
            ? 0
            : Signal(.60 * ((hardPassages - 1) / 3.0) + .40 * highSectionRatio);
        double sustainedDifficulty = Score(.50 * meanSection + .30 * hardMean + .20 * highSectionRatio * 100);

        double[] familyStrengths = input.Patterns
            .GroupBy(pattern => pattern.Family)
            .Select(group => group.Max(pattern => Score(pattern.Strength)))
            .OrderByDescending(value => value).ToArray();
        double patternContribution = familyStrengths.Length == 0
            ? 0
            : Score(.60 * familyStrengths[0]
                + .30 * (familyStrengths.Length > 1 ? familyStrengths[1] : 0)
                + .10 * (familyStrengths.Count(value => value >= 40) / 3.0) * 100);

        double stamina = Value(input.SkillPressure.Stamina);
        double endurancePressure = Value(input.SkillPressure.Endurance);
        double durationSignal = Signal((input.DurationSeconds - 90) / 270);
        double enduranceContribution = Score(100 * durationSignal
            * (.45 * ((.60 * endurancePressure + .40 * stamina) / 100)
                + .35 * sustainedDifficulty / 100
                + .20 * repeatedSignal));

        double otherSkillMean = rankedSkills.Skip(1).Average();
        double specializationContribution = SpecializationCap
            * Signal((rankedSkills[0] - 50) / 40)
            * Signal((rankedSkills[0] - otherSkillMean) / 40);
        double score = Score(
            BaseWeight * baseDifficulty
            + SustainedWeight * sustainedDifficulty
            + PeakWeight * peakDifficulty
            + SkillPressureWeight * skillPressure
            + PatternWeight * patternContribution
            + EnduranceWeight * enduranceContribution
            + specializationContribution);

        double sectionConfidence = playableSections.Length == 0
            ? 0
            : Signal(playableSections.Average(section => section.LocalDifficultyConfidence));
        double sectionQuantity = 1 - Math.Exp(-playableSections.Length / 5.0);
        double pressureConfidence = Signal(input.SkillPressure.Confidence);
        double patternConfidence = input.Patterns.Count == 0
            ? 0
            : Signal(input.Patterns.Average(pattern => Signal(pattern.Confidence)));
        double temporalStability = difficulty.Length < 2 ? .25 : 1 - Signal(StandardDeviation(difficulty) / 40);
        double confidence = Signal(
            .25 * sectionConfidence
            + .20 * pressureConfidence
            + .15 * patternConfidence
            + .15 * sectionQuantity
            + .15 * Signal(input.IdentityConfidence)
            + .10 * temporalStability);

        RatingV2Contributor[] contributors = BuildContributors(
            baseDifficulty, peakDifficulty, sustainedDifficulty, skillPressure,
            patternContribution, enduranceContribution, specializationContribution,
            sectionConfidence, pressureConfidence, patternConfidence, confidence);
        RatingV2DiagnosticFlag[] flags = BuildFlags(
            input, baseDifficulty, meanSection, maxSection, peakDifficulty,
            sustainedDifficulty, skillPressure, patternContribution,
            specializationContribution, highSectionRatio, hardPassages, confidence);

        return new BeatInsightRatingV2(
            score,
            confidence,
            baseDifficulty,
            peakDifficulty,
            sustainedDifficulty,
            skillPressure,
            patternContribution,
            enduranceContribution,
            specializationContribution,
            meanSection,
            maxSection,
            highSectionRatio,
            repeatedSignal,
            hardPassages,
            contributors,
            flags);
    }

    private static RatingV2Contributor[] BuildContributors(
        double baseDifficulty,
        double peakDifficulty,
        double sustainedDifficulty,
        double skillPressure,
        double pattern,
        double endurance,
        double specialization,
        double sectionConfidence,
        double pressureConfidence,
        double patternConfidence,
        double confidence)
    {
        RatingV2Contributor[] contributors =
        [
            new("Base skills", RatingV2ContributorCategory.BaseDifficulty, baseDifficulty, BaseWeight * baseDifficulty, confidence),
            new("Sustained section difficulty", RatingV2ContributorCategory.SectionDifficulty, sustainedDifficulty, SustainedWeight * sustainedDifficulty, sectionConfidence),
            new("Robust peak difficulty", RatingV2ContributorCategory.SectionDifficulty, peakDifficulty, PeakWeight * peakDifficulty, sectionConfidence),
            new("Skill pressure", RatingV2ContributorCategory.SkillPressure, skillPressure, SkillPressureWeight * skillPressure, pressureConfidence),
            new("Pattern complexity", RatingV2ContributorCategory.PatternComplexity, pattern, PatternWeight * pattern, patternConfidence),
            new("Conditional endurance", RatingV2ContributorCategory.Endurance, endurance, EnduranceWeight * endurance, pressureConfidence),
            new("Specialization", RatingV2ContributorCategory.Specialization, specialization, specialization, confidence)
        ];
        return contributors.OrderByDescending(item => item.Contribution).ThenBy(item => item.Name, StringComparer.Ordinal).Take(5).ToArray();
    }

    private static RatingV2DiagnosticFlag[] BuildFlags(
        BeatInsightRatingV2Input input,
        double baseDifficulty,
        double meanSection,
        double maxSection,
        double peakDifficulty,
        double sustainedDifficulty,
        double skillPressure,
        double patternContribution,
        double specialization,
        double highSectionRatio,
        int hardPassages,
        double confidence)
    {
        var flags = new List<RatingV2DiagnosticFlag>();
        if (input.ObjectCount < 20) flags.Add(RatingV2DiagnosticFlag.SparseMap);
        if (hardPassages == 1 && highSectionRatio < .20 && maxSection - meanSection >= 20)
            flags.Add(RatingV2DiagnosticFlag.SingleExtremePeak);
        if (sustainedDifficulty >= 55) flags.Add(RatingV2DiagnosticFlag.SustainedHighDifficulty);
        if (hardPassages >= 2) flags.Add(RatingV2DiagnosticFlag.RepeatedHardPassages);
        if (specialization >= 3) flags.Add(RatingV2DiagnosticFlag.HighSpecialization);
        if (confidence < .50) flags.Add(RatingV2DiagnosticFlag.LowSignalConfidence);
        if (patternContribution >= 55 && baseDifficulty < 30) flags.Add(RatingV2DiagnosticFlag.PatternHeavyLowBaseDifficulty);
        if (skillPressure >= 60 && meanSection < 30) flags.Add(RatingV2DiagnosticFlag.HighSkillPressureLowSectionDifficulty);
        if (peakDifficulty >= 55 && baseDifficulty < 35) flags.Add(RatingV2DiagnosticFlag.HighSectionDifficultyLowGlobalSkills);
        return flags.ToArray();
    }

    private static IEnumerable<SkillPressureMetric> PressureMetrics(SkillPressureProfile profile) =>
        new[]
        {
            profile.Stamina, profile.Endurance, profile.FingerControl, profile.BurstControl,
            profile.AimControl, profile.ReadingPressure, profile.RhythmComplexity
        }.Where(metric => metric.Available && metric.Score.HasValue);

    private static double WeightedTop(IEnumerable<double> source)
    {
        double[] ranked = source.Select(Score).OrderByDescending(value => value).Take(3).ToArray();
        if (ranked.Length == 0) return 0;
        double[] weights = [.50, .30, .20];
        double usedWeight = weights.Take(ranked.Length).Sum();
        return ranked.Select((value, index) => value * weights[index]).Sum() / usedWeight;
    }

    private static int CountHardPassages(IReadOnlyList<bool> values)
    {
        int passages = 0;
        int index = 0;
        while (index < values.Count)
        {
            while (index < values.Count && !values[index]) index++;
            if (index >= values.Count) break;
            int hardWindowCount = 0;
            while (index < values.Count)
            {
                if (values[index])
                {
                    hardWindowCount++;
                    index++;
                    continue;
                }
                if (index + 1 < values.Count && values[index + 1])
                {
                    index++;
                    continue;
                }
                break;
            }
            if (hardWindowCount >= 2) passages++;
        }
        return passages;
    }

    private static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        if (sorted.Count == 0) return 0;
        double position = (sorted.Count - 1) * Signal(percentile);
        int lower = (int)Math.Floor(position), upper = (int)Math.Ceiling(position);
        return lower == upper
            ? sorted[lower]
            : sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }

    private static double StandardDeviation(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        double mean = values.Average();
        return Math.Sqrt(values.Average(value => Math.Pow(value - mean, 2)));
    }

    private static double Value(SkillPressureMetric metric) => metric.Available && metric.Score.HasValue ? Score(metric.Score.Value) : 0;
    private static double Score(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;
    private static double Signal(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
}
