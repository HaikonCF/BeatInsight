using BeatInsight.Analysis.Fusion;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Analysis.Sections;
using BeatInsight.Analysis.SkillPressure;
using BeatInsight.Analysis.StreamPatterns;
using BeatInsight.Analysis.TechPatterns;
using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.Fusion;
using BeatInsight.Models.Identity;
using BeatInsight.Models.JumpPatterns;
using BeatInsight.Models.Sections;
using BeatInsight.Models.SkillPressure;

namespace BeatInsight.Analysis.Identity;

/// <summary>
/// Read-only Gameplay Identity V3. Structural identity, skill pressure, patterns and
/// section context remain separate evidence layers. Historical Identity is untouched.
/// </summary>
public static class GameplayIdentityV3Analyzer
{
    public const double MinimumSecondaryScore = 25;
    public const double MinimumSecondaryRelativeStrength = .55;
    public const double MaximumMixedMargin = 8;
    public const double MinimumRepresentedSectionRatio = .15;
    public const double MinimumMainSkillScore = 30;
    public const double MinimumMainPatternScore = 40;
    public const double MinimumMainPatternRelativeStrength = .55;
    public const int MaximumMainSkillCount = 3;
    public const int MaximumMainPatternCount = 5;

    public static GameplayIdentityV3Profile Analyze(Beatmap beatmap)
    {
        ArgumentNullException.ThrowIfNull(beatmap);
        GameplayProfile global = GameplayAnalyzer.Analyze(beatmap);
        Beatmap analyticalMap = CreateAnalyticalMap(beatmap, global);
        SectionAnalysisResult sections = SectionAnalysisV2Analyzer.Analyze(analyticalMap);
        SkillPressureProfile pressure = SkillPressureAnalyzer.Analyze(analyticalMap);
        return AnalyzePrepared(beatmap, analyticalMap, global, sections, pressure);
    }

    public static GameplayIdentityV3Profile Analyze(
        Beatmap beatmap,
        GameplayProfile global,
        SectionAnalysisResult sections,
        SkillPressureProfile pressure)
    {
        ArgumentNullException.ThrowIfNull(beatmap);
        ArgumentNullException.ThrowIfNull(global);
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(pressure);
        Beatmap analyticalMap = CreateAnalyticalMap(beatmap, global);
        return AnalyzePrepared(beatmap, analyticalMap, global, sections, pressure);
    }

    private static GameplayIdentityV3Profile AnalyzePrepared(
        Beatmap beatmap,
        Beatmap analyticalMap,
        GameplayProfile global,
        SectionAnalysisResult sections,
        SkillPressureProfile pressure)
    {
        IReadOnlyList<GameplayIdentityV3PatternInput> patterns = BuildPatterns(analyticalMap, global);
        GameplayIdentityV3Profile result = Analyze(new GameplayIdentityV3Input(
            beatmap.HitObjects.Count,
            Score(global.Identity.JumpScore),
            Score(global.Identity.StreamScore),
            Score(global.Identity.TechScore),
            pressure,
            patterns,
            sections));
        GameplayDebug.IdentityV3(result);
        return result;
    }

    public static GameplayIdentityV3Profile Analyze(GameplayIdentityV3Input input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.SkillPressure);
        ArgumentNullException.ThrowIfNull(input.Patterns);
        ArgumentNullException.ThrowIfNull(input.Sections);

        GameplayIdentityV3SectionContext sectionContext = BuildSectionContext(input.Sections);
        GameplayIdentityV3Pattern[] allPatterns = input.Patterns
            .Select(ToPattern)
            .OrderByDescending(pattern => pattern.Score)
            .ThenBy(pattern => pattern.Name, StringComparer.Ordinal)
            .ToArray();
        double strongestPatternScore = allPatterns.FirstOrDefault()?.Score ?? 0;
        double mainPatternCutoff = Math.Max(
            MinimumMainPatternScore,
            strongestPatternScore * MinimumMainPatternRelativeStrength);
        GameplayIdentityV3Pattern[] mainPatterns = allPatterns
            .Where(pattern => pattern.Score >= mainPatternCutoff)
            .Take(MaximumMainPatternCount)
            .ToArray();

        double jumpPattern = FamilySupport(allPatterns, FusionPatternFamily.Jump);
        double streamPattern = FamilySupport(allPatterns, FusionPatternFamily.Stream);
        double techPattern = FamilySupport(allPatterns, FusionPatternFamily.Tech);
        double jump = StructureScore(input.GlobalJumpStructure, sectionContext.JumpSectionRatio, jumpPattern);
        double stream = StructureScore(input.GlobalStreamStructure, sectionContext.StreamSectionRatio, streamPattern);
        double tech = StructureScore(input.GlobalTechStructure, sectionContext.TechSectionRatio, techPattern);

        StructureItem[] ranked =
        [
            new(GameplayStructureKind.Jump, jump),
            new(GameplayStructureKind.Stream, stream),
            new(GameplayStructureKind.Tech, tech)
        ];
        ranked = ranked.OrderByDescending(item => item.Score).ThenBy(item => item.Kind).ToArray();
        double top = ranked[0].Score;
        double second = ranked[1].Score;
        double margin = top - second;
        double sectionDiversity = sectionContext.RealStructuralDiversity;
        bool meaningfulClosePair = second >= MinimumSecondaryScore
            && margin <= MaximumMixedMargin
            && second / Math.Max(top, 1) >= MinimumSecondaryRelativeStrength;
        bool diverseSections = sectionContext.RepresentedStructuralFamilyCount >= 2;
        bool neutralOnlyFalseMixedGuard = margin > 20
            && sectionContext.RepresentedStructuralFamilyCount < 2;
        bool isMixed = top < MinimumSecondaryScore
            || meaningfulClosePair
            || (!neutralOnlyFalseMixedGuard && diverseSections);
        GameplayStructureKind primary = isMixed ? GameplayStructureKind.ClassicMixed : ranked[0].Kind;
        GameplayStructureKind? secondary = null;
        if (isMixed)
        {
            if (top >= MinimumSecondaryScore)
                secondary = ranked[0].Kind;
        }
        else if (second >= MinimumSecondaryScore
                 && second / Math.Max(top, 1) >= MinimumSecondaryRelativeStrength)
        {
            secondary = ranked[1].Kind;
        }

        double closeness = second < MinimumSecondaryScore
            ? 0
            : Signal(1 - margin / MaximumMixedMargin);
        double classicMixed = Score(100 * Math.Max(
            Signal(1 - top / 100),
            Math.Max(closeness, sectionDiversity)));

        GameplayIdentityV3Skill[] mainSkills = SelectSkills(input.SkillPressure);
        double skillConfidence = mainSkills.Length == 0 ? 0 : Signal(mainSkills.Average(skill => skill.Confidence));
        double patternConfidence = mainPatterns.Length == 0
            ? 0
            : Signal(mainPatterns.Average(pattern => .5 * pattern.Coverage + .5 * pattern.Confidence));
        double structureConfidence = StructureConfidence(
            input,
            sectionContext,
            primary,
            ranked,
            margin,
            sectionDiversity,
            jumpPattern,
            streamPattern,
            techPattern);
        double globalConfidence = Signal(.50 * structureConfidence + .30 * skillConfidence + .20 * patternConfidence);
        GameplayIdentityV3DiagnosticFlag[] flags = BuildFlags(
            input,
            primary,
            secondary,
            ranked,
            sectionContext,
            sectionDiversity,
            mainSkills,
            mainPatterns,
            globalConfidence);

        return new GameplayIdentityV3Profile(
            jump,
            stream,
            tech,
            classicMixed,
            primary,
            secondary,
            mainSkills,
            mainPatterns,
            sectionContext,
            structureConfidence,
            skillConfidence,
            patternConfidence,
            globalConfidence,
            flags);
    }

    private static GameplayIdentityV3SectionContext BuildSectionContext(SectionAnalysisResult result)
    {
        int count = result.SectionCount;
        double Ratio(GameplayStructureKind kind) => count == 0
            ? 0
            : result.Sections.Count(section => section.DominantStructure == kind) / (double)count;
        var compressed = new List<GameplayStructureKind>();
        foreach (GameplayStructureKind structure in result.DominantStructureTimeline)
            if (compressed.Count == 0 || compressed[^1] != structure) compressed.Add(structure);
        double jumpRatio = Ratio(GameplayStructureKind.Jump);
        double streamRatio = Ratio(GameplayStructureKind.Stream);
        double techRatio = Ratio(GameplayStructureKind.Tech);
        double[] structuralRatios = [jumpRatio, streamRatio, techRatio];
        int representedFamilies = structuralRatios.Count(value => value >= MinimumRepresentedSectionRatio);
        double realStructuralDiversity = representedFamilies < 2
            ? 0
            : Signal(1 - structuralRatios.Max());
        return new GameplayIdentityV3SectionContext(
            jumpRatio,
            streamRatio,
            techRatio,
            Ratio(GameplayStructureKind.ClassicMixed),
            representedFamilies,
            realStructuralDiversity,
            result.GameplayTransitionCount,
            result.DifficultySpikeCount,
            compressed);
    }

    private static GameplayIdentityV3Skill[] SelectSkills(SkillPressureProfile pressure)
    {
        (string Name, SkillPressureMetric Metric, bool Partial)[] candidates =
        [
            ("Stamina", pressure.Stamina, false),
            ("Endurance", pressure.Endurance, true),
            ("FingerControl", pressure.FingerControl, false),
            ("BurstControl", pressure.BurstControl, false),
            ("AimControl", pressure.AimControl, false),
            ("Precision", pressure.Precision, false),
            ("ReadingPressure", pressure.ReadingPressure, false),
            ("RhythmComplexity", pressure.RhythmComplexity, true)
        ];
        return candidates
            .Where(item => item.Metric.Available && item.Metric.Score.HasValue && item.Metric.Score.Value >= MinimumMainSkillScore)
            .Select(item => new GameplayIdentityV3Skill(
                item.Name,
                Score(item.Metric.Score!.Value),
                Signal(item.Metric.Confidence),
                item.Partial || !item.Metric.Reliable
                    ? GameplayIdentityV3EvidenceStatus.Partial
                    : GameplayIdentityV3EvidenceStatus.Reliable))
            .OrderByDescending(skill => skill.Score)
            .ThenBy(skill => skill.Name, StringComparer.Ordinal)
            .Take(MaximumMainSkillCount)
            .ToArray();
    }

    private static GameplayIdentityV3Pattern ToPattern(GameplayIdentityV3PatternInput input)
    {
        double coverage = Signal(input.Coverage);
        double confidence = Signal(input.Confidence);
        double numerator = .50 * coverage + .30 * confidence;
        double denominator = .80;
        double? intensity = input.Intensity.HasValue ? Signal(input.Intensity.Value) : null;
        if (intensity.HasValue) { numerator += .20 * intensity.Value; denominator = 1; }
        return new GameplayIdentityV3Pattern(
            input.Name,
            input.Family,
            Score(100 * numerator / denominator),
            coverage,
            confidence,
            intensity);
    }

    private static double FamilySupport(IEnumerable<GameplayIdentityV3Pattern> patterns, FusionPatternFamily family)
    {
        double[] strengths = patterns.Where(pattern => pattern.Family == family && pattern.Score > 0)
            .Select(pattern => pattern.Score / 100).ToArray();
        if (strengths.Length == 0) return 0;
        int familySize = family switch
        {
            FusionPatternFamily.Jump => 11,
            FusionPatternFamily.Stream => 9,
            FusionPatternFamily.Tech => 7,
            _ => 1
        };
        return Signal(.70 * strengths.Max() + .25 * strengths.Average() + .05 * Signal(strengths.Length / (double)familySize));
    }

    private static double StructureScore(double global, double section, double pattern) =>
        Score(.60 * Score(global) + .25 * Signal(section) * 100 + .15 * Signal(pattern) * 100);

    private static double StructureConfidence(
        GameplayIdentityV3Input input,
        GameplayIdentityV3SectionContext sections,
        GameplayStructureKind primary,
        IReadOnlyList<StructureItem> ranked,
        double margin,
        double diversity,
        double jumpPattern,
        double streamPattern,
        double techPattern)
    {
        double dominance = primary == GameplayStructureKind.ClassicMixed
            ? Math.Max(diversity, ranked[1].Score >= MinimumSecondaryScore ? Signal(1 - margin / MaximumMixedMargin) : 0)
            : Signal(margin / 30);
        double[] global = [Score(input.GlobalJumpStructure) / 100, Score(input.GlobalStreamStructure) / 100, Score(input.GlobalTechStructure) / 100];
        double[] local = [sections.JumpSectionRatio, sections.StreamSectionRatio, sections.TechSectionRatio];
        double agreement = 1 - Signal(global.Zip(local, (left, right) => Math.Abs(left - right)).Average());
        double availability = 1 - Math.Exp(-Math.Max(0, input.ObjectCount) / 50.0);
        double patternAgreement = primary switch
        {
            GameplayStructureKind.Jump => jumpPattern,
            GameplayStructureKind.Stream => streamPattern,
            GameplayStructureKind.Tech => techPattern,
            _ => new[] { jumpPattern, streamPattern, techPattern }.OrderByDescending(value => value).Take(2).Average()
        };
        return Signal(.35 * dominance + .30 * agreement + .20 * availability + .15 * patternAgreement);
    }

    private static GameplayIdentityV3DiagnosticFlag[] BuildFlags(
        GameplayIdentityV3Input input,
        GameplayStructureKind primary,
        GameplayStructureKind? secondary,
        IReadOnlyList<StructureItem> ranked,
        GameplayIdentityV3SectionContext sections,
        double diversity,
        IReadOnlyList<GameplayIdentityV3Skill> skills,
        IReadOnlyList<GameplayIdentityV3Pattern> patterns,
        double confidence)
    {
        var flags = new List<GameplayIdentityV3DiagnosticFlag>();
        if (ranked[0].Score < 30) flags.Add(GameplayIdentityV3DiagnosticFlag.WeakPrimaryStructure);
        if (secondary.HasValue && ranked[1].Score >= 40) flags.Add(GameplayIdentityV3DiagnosticFlag.StrongSecondaryStructure);
        if (primary == GameplayStructureKind.ClassicMixed && diversity >= .45) flags.Add(GameplayIdentityV3DiagnosticFlag.HighlyMixedStructure);
        if (diversity >= .55 || sections.GameplayTransitionCount >= 3) flags.Add(GameplayIdentityV3DiagnosticFlag.HighSectionVariability);
        if (HasSkillMismatch(primary, skills)) flags.Add(GameplayIdentityV3DiagnosticFlag.SkillStructureMismatch);
        if (patterns.Count > 0 && primary != GameplayStructureKind.ClassicMixed && patterns[0].Family != ToFamily(primary))
            flags.Add(GameplayIdentityV3DiagnosticFlag.PatternStructureMismatch);
        if (confidence < .50) flags.Add(GameplayIdentityV3DiagnosticFlag.LowConfidence);
        if (input.ObjectCount < 20) flags.Add(GameplayIdentityV3DiagnosticFlag.SparseMap);
        return flags.ToArray();
    }

    private static bool HasSkillMismatch(GameplayStructureKind primary, IReadOnlyList<GameplayIdentityV3Skill> skills)
    {
        if (skills.Count == 0 || primary == GameplayStructureKind.ClassicMixed) return false;
        string top = skills[0].Name;
        return primary switch
        {
            GameplayStructureKind.Jump => top is "Stamina" or "BurstControl",
            GameplayStructureKind.Stream => top == "AimControl",
            GameplayStructureKind.Tech => top == "Stamina",
            _ => false
        };
    }

    private static FusionPatternFamily ToFamily(GameplayStructureKind structure) => structure switch
    {
        GameplayStructureKind.Jump => FusionPatternFamily.Jump,
        GameplayStructureKind.Stream => FusionPatternFamily.Stream,
        GameplayStructureKind.Tech => FusionPatternFamily.Tech,
        _ => FusionPatternFamily.Jump
    };

    private static IReadOnlyList<GameplayIdentityV3PatternInput> BuildPatterns(Beatmap map, GameplayProfile profile)
    {
        IReadOnlyList<HitObject> objects = map.HitObjects;
        var patterns = new List<GameplayIdentityV3PatternInput>();
        foreach (IGrouping<JumpPatternType, JumpPatternMatch> group in JumpPatternAnalyzer.Analyze(objects, profile).GroupBy(match => match.Type))
        {
            JumpPatternMatch[] matches = group.ToArray();
            int covered = matches.SelectMany(match => Enumerable.Range(match.StartObjectIndex, match.ObjectCount)).Distinct().Count();
            patterns.Add(new(group.Key.ToString(), FusionPatternFamily.Jump,
                objects.Count == 0 ? 0 : covered / (double)objects.Count,
                matches.Average(match => Signal(match.Confidence))));
        }

        var burst = StreamPatternAnalyzer.AnalyzeBurst(objects);
        Add(patterns, "Burst", FusionPatternFamily.Stream, burst.Coverage, burst.Confidence, burst.BurstSequenceCount == 0 ? 0 : Signal((130 - burst.AverageInterval) / 100));
        var shortStream = StreamPatternAnalyzer.AnalyzeShortStream(objects);
        Add(patterns, "ShortStream", FusionPatternFamily.Stream, shortStream.Coverage, shortStream.Confidence);
        var longStream = StreamPatternAnalyzer.AnalyzeLongStream(objects);
        Add(patterns, "LongStream", FusionPatternFamily.Stream, longStream.Coverage, longStream.Confidence);
        var deathstream = StreamPatternAnalyzer.AnalyzeDeathstream(objects);
        Add(patterns, "Deathstream", FusionPatternFamily.Stream, deathstream.Coverage, deathstream.Confidence);
        var stamina = StreamPatternAnalyzer.AnalyzeStaminaStream(objects);
        Add(patterns, "StaminaStream", FusionPatternFamily.Stream, stamina.Coverage, stamina.Confidence, stamina.AverageActiveRatio);
        var irregular = StreamPatternAnalyzer.AnalyzeIrregularStream(objects);
        Add(patterns, "IrregularStream", FusionPatternFamily.Stream, irregular.Coverage, irregular.Confidence, irregular.AverageIrregularityScore);
        var speed = StreamPatternAnalyzer.AnalyzeSpeedStream(objects);
        Add(patterns, "SpeedStream", FusionPatternFamily.Stream, speed.Coverage, speed.Confidence, speed.AverageSpeedIntensity);
        var flow = StreamPatternAnalyzer.AnalyzeFlowStream(objects);
        Add(patterns, "FlowStream", FusionPatternFamily.Stream, flow.Coverage, flow.Confidence, flow.AverageFlowScore);
        var alternating = StreamPatternAnalyzer.AnalyzeAlternatingStream(objects);
        Add(patterns, "AlternatingStream", FusionPatternFamily.Stream, alternating.Coverage, alternating.Confidence, alternating.AverageAlternatingScore);

        var chain = TechPatternAnalyzer.AnalyzeSliderChain(objects);
        Add(patterns, "SliderChain", FusionPatternFamily.Tech, chain.Coverage, chain.Confidence, chain.AverageSliderRatio);
        var complex = TechPatternAnalyzer.AnalyzeComplexSlider(map);
        Add(patterns, "ComplexSlider", FusionPatternFamily.Tech, complex.Coverage, complex.Confidence, complex.AverageComplexityScore);
        var direction = TechPatternAnalyzer.AnalyzeSliderDirectionChange(map);
        Add(patterns, "SliderDirectionChange", FusionPatternFamily.Tech, direction.Coverage, direction.Confidence, direction.AverageDirectionChangeScore);
        var circleSlider = TechPatternAnalyzer.AnalyzeCircleSliderTransition(objects);
        Add(patterns, "CircleSliderTransition", FusionPatternFamily.Tech, circleSlider.Coverage, circleSlider.Confidence, circleSlider.AverageTransitionScore);
        var rhythm = TechPatternAnalyzer.AnalyzeRhythmTransition(objects);
        Add(patterns, "RhythmTransition", FusionPatternFamily.Tech, rhythm.Coverage, rhythm.Confidence, rhythm.AverageTransitionScore);
        var spatial = TechPatternAnalyzer.AnalyzeSpatialAwkwardTransition(objects);
        Add(patterns, "SpatialAwkward", FusionPatternFamily.Tech, spatial.Coverage, spatial.Confidence, spatial.AverageSpatialAwkwardScore);
        var overlap = TechPatternAnalyzer.AnalyzeOverlap(objects);
        Add(patterns, "Overlap", FusionPatternFamily.Tech, overlap.Coverage, overlap.Confidence, overlap.AverageOverlapStrength);
        return patterns;
    }

    private static void Add(ICollection<GameplayIdentityV3PatternInput> patterns, string name, FusionPatternFamily family, double coverage, double confidence, double? intensity = null)
    {
        if (coverage <= 0 && confidence <= 0) return;
        patterns.Add(new(name, family, Signal(coverage), Signal(confidence), intensity.HasValue ? Signal(intensity.Value) : null));
    }

    private static Beatmap CreateAnalyticalMap(Beatmap source, GameplayProfile profile) => new()
    {
        Title = source.Title, Artist = source.Artist, Creator = source.Creator, Version = source.Version,
        OsuStarRating = source.OsuStarRating, BeatInsightRating = source.BeatInsightRating,
        Length = source.Length, BPM = source.BPM, MaxCombo = source.MaxCombo,
        AR = source.AR, OD = source.OD, CS = source.CS, HP = source.HP,
        TimingPoints = source.TimingPoints, HitObjects = source.HitObjects,
        CircleCount = source.CircleCount, SliderCount = source.SliderCount, SpinnerCount = source.SpinnerCount,
        SliderMultiplier = source.SliderMultiplier, SliderTickRate = source.SliderTickRate,
        GameplayProfile = profile
    };

    private static double Score(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;
    private static double Signal(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
    private sealed record StructureItem(GameplayStructureKind Kind, double Score);
}
