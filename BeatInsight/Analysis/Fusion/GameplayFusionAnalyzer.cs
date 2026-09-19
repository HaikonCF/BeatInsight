using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Analysis.StreamPatterns;
using BeatInsight.Analysis.TechPatterns;
using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.Fusion;
using BeatInsight.Models.JumpPatterns;

namespace BeatInsight.Analysis.Fusion;

/// <summary>
/// Read-only Fusion V1. Existing skill scores are the only bases. Pattern analyzers supply
/// one small, capped multiplier and never feed structure selection, Identity, Rating, or
/// each other. Historical TechScore is used once; TechV2 is intentionally not an input.
/// </summary>
public static class GameplayFusionAnalyzer
{
    public const double MaximumPatternBonus = 0.10;
    public const double MaximumReadingPatternBonus = 0.02;

    private const int JumpPatternFamilySize = 13;
    private const int StreamPatternFamilySize = 9;
    private const int TechPatternFamilySize = 7;
    private const int ReadingMappedPatternCount = 5;

    public static GameplayFusionProfile Analyze(Beatmap beatmap)
    {
        ArgumentNullException.ThrowIfNull(beatmap);
        GameplayFusionProfile result = Analyze(BuildInput(beatmap));
        GameplayDebug.GameplayFusion(result);
        return result;
    }

    public static GameplayFusionProfile Analyze(GameplayFusionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.PatternEvidence);

        double aim = Score(input.AimScore);
        double speed = Score(input.SpeedScore);
        double reading = Score(input.ReadingScore);
        double tech = Score(input.TechScore);
        double? stamina = input.StaminaScore.HasValue ? Score(input.StaminaScore.Value) : null;

        double jumpSupport = AggregateFamily(input.PatternEvidence, FusionPatternFamily.Jump, JumpPatternFamilySize);
        double streamSupport = AggregateFamily(input.PatternEvidence, FusionPatternFamily.Stream, StreamPatternFamilySize);
        double techSupport = AggregateFamily(input.PatternEvidence, FusionPatternFamily.Tech, TechPatternFamilySize);
        double readingSupport = AggregateMapped(input.PatternEvidence, evidence => evidence.ReadingWeight, ReadingMappedPatternCount);
        double staminaSupport = AggregateMapped(input.PatternEvidence, evidence => evidence.StaminaWeight, 1);

        double jumpStructure = Signal(input.JumpStructureSignal) * 100;
        double streamStructure = Signal(input.StreamStructureSignal) * 100;
        double techStructure = Signal(input.TechStructureSignal) * 100;
        StructureDecision structures = DecideStructures(jumpStructure, streamStructure, techStructure);

        double finalAim = ApplyBonus(aim, jumpSupport, MaximumPatternBonus);
        double finalSpeed = ApplyBonus(speed, streamSupport, MaximumPatternBonus);
        double finalReading = ApplyBonus(reading, readingSupport, MaximumReadingPatternBonus);
        double finalTech = ApplyBonus(tech, techSupport, MaximumPatternBonus);
        double? finalStamina = stamina.HasValue
            ? ApplyBonus(stamina.Value, staminaSupport, MaximumPatternBonus)
            : null;

        var flags = new List<GameplayFusionDiagnosticFlag>();
        if (techSupport >= .65 && tech < 25) flags.Add(GameplayFusionDiagnosticFlag.HighTechPatternsLowTechSkill);
        if (streamStructure >= 60 && speed < 25) flags.Add(GameplayFusionDiagnosticFlag.HighStreamStructureLowSpeed);
        if (jumpStructure >= 60 && aim < 25) flags.Add(GameplayFusionDiagnosticFlag.HighJumpStructureLowAim);
        if (reading >= 60 && readingSupport < .15) flags.Add(GameplayFusionDiagnosticFlag.HighReadingWithoutReadingPatterns);
        if (structures.Dominant == GameplayStructureKind.ClassicMixed) flags.Add(GameplayFusionDiagnosticFlag.MixedStructure);

        double top = Math.Max(jumpStructure, Math.Max(streamStructure, techStructure));
        double second = new[] { jumpStructure, streamStructure, techStructure }.OrderByDescending(value => value).Skip(1).First();
        double margin = top <= 0 ? 0 : Math.Clamp((top - second) / top, 0, 1);
        double signalQuantity = Math.Clamp(
            ((top / 100) + ((jumpSupport + streamSupport + techSupport) / 3)) / 2,
            0,
            1);
        double patternEvidence = (jumpSupport + streamSupport + techSupport) / 3;
        double confidence = Signal((margin + signalQuantity + patternEvidence) / 3);

        return new GameplayFusionProfile(
            jumpStructure,
            streamStructure,
            techStructure,
            structures.MixedScore,
            aim,
            speed,
            reading,
            tech,
            stamina,
            jumpSupport,
            streamSupport,
            techSupport,
            readingSupport,
            staminaSupport,
            finalAim,
            finalSpeed,
            finalReading,
            finalTech,
            finalStamina,
            structures.Dominant,
            structures.Secondary,
            confidence,
            flags);
    }

    /// <summary>
    /// Per-pattern strength = weighted mean of available normalized signals:
    /// 50% Coverage, 30% Confidence, 20% Intensity. Missing intensity is omitted from
    /// both numerator and denominator. Family support is top-weighted and saturated:
    /// 70% strongest + 25% active mean + 5% breadth.
    /// </summary>
    public static double CalculatePatternSupport(
        IReadOnlyList<GameplayFusionPatternEvidence> evidence,
        FusionPatternFamily family) => family switch
        {
            FusionPatternFamily.Jump => AggregateFamily(evidence, family, JumpPatternFamilySize),
            FusionPatternFamily.Stream => AggregateFamily(evidence, family, StreamPatternFamilySize),
            FusionPatternFamily.Tech => AggregateFamily(evidence, family, TechPatternFamilySize),
            _ => 0
        };

    private static GameplayFusionInput BuildInput(Beatmap beatmap)
    {
        GameplayProfile profile = beatmap.GameplayProfile ?? new GameplayProfile();
        IReadOnlyList<HitObject> objects = beatmap.HitObjects;
        var evidence = new List<GameplayFusionPatternEvidence>();

        foreach (IGrouping<JumpPatternType, JumpPatternMatch> group in JumpPatternAnalyzer
                     .Analyze(objects, profile)
                     .GroupBy(match => match.Type))
        {
            JumpPatternMatch[] matches = group.ToArray();
            int covered = matches
                .SelectMany(match => Enumerable.Range(match.StartObjectIndex, match.ObjectCount))
                .Distinct()
                .Count();
            evidence.Add(new(
                group.Key.ToString(),
                FusionPatternFamily.Jump,
                objects.Count == 0 ? 0 : (double)covered / objects.Count,
                matches.Average(match => Signal(match.Confidence))));
        }

        AddStreamEvidence(objects, evidence);
        AddTechEvidence(beatmap, evidence);

        // Identity already exposes the current structural scores on [0,100].
        // Divide once to satisfy the Fusion input contract; skill scores remain on [0,100].
        // TechV2Presence, BeatInsightRating, and osu! Star Rating are deliberately absent.
        return new GameplayFusionInput(
            profile.AimScore,
            profile.SpeedScore,
            profile.ReadScore,
            profile.TechScore,
            null,
            Score(profile.Identity.JumpScore) / 100,
            Score(profile.Identity.StreamScore) / 100,
            Score(profile.Identity.TechScore) / 100,
            evidence);
    }

    private static void AddStreamEvidence(
        IReadOnlyList<HitObject> objects,
        ICollection<GameplayFusionPatternEvidence> evidence)
    {
        var burst = StreamPatternAnalyzer.AnalyzeBurst(objects);
        Add(evidence, "Burst", FusionPatternFamily.Stream, burst.Coverage, burst.Confidence);
        var shortStream = StreamPatternAnalyzer.AnalyzeShortStream(objects);
        Add(evidence, "Short Stream", FusionPatternFamily.Stream, shortStream.Coverage, shortStream.Confidence);
        var longStream = StreamPatternAnalyzer.AnalyzeLongStream(objects);
        Add(evidence, "Long Stream", FusionPatternFamily.Stream, longStream.Coverage, longStream.Confidence);
        var deathstream = StreamPatternAnalyzer.AnalyzeDeathstream(objects);
        Add(evidence, "Deathstream", FusionPatternFamily.Stream, deathstream.Coverage, deathstream.Confidence);
        var stamina = StreamPatternAnalyzer.AnalyzeStaminaStream(objects);
        Add(evidence, "Stamina Stream", FusionPatternFamily.Stream, stamina.Coverage, stamina.Confidence,
            stamina.AverageActiveRatio, staminaWeight: 1);
        var irregular = StreamPatternAnalyzer.AnalyzeIrregularStream(objects);
        Add(evidence, "Irregular Stream", FusionPatternFamily.Stream, irregular.Coverage, irregular.Confidence,
            irregular.AverageIrregularityScore, readingWeight: 1);
        var speed = StreamPatternAnalyzer.AnalyzeSpeedStream(objects);
        Add(evidence, "Speed Stream", FusionPatternFamily.Stream, speed.Coverage, speed.Confidence,
            speed.AverageSpeedIntensity);
        var flow = StreamPatternAnalyzer.AnalyzeFlowStream(objects);
        Add(evidence, "Flow Stream", FusionPatternFamily.Stream, flow.Coverage, flow.Confidence,
            flow.AverageFlowScore);
        var alternating = StreamPatternAnalyzer.AnalyzeAlternatingStream(objects);
        Add(evidence, "Alternating Stream", FusionPatternFamily.Stream, alternating.Coverage, alternating.Confidence,
            alternating.AverageAlternatingScore, readingWeight: .75);
    }

    private static void AddTechEvidence(
        Beatmap beatmap,
        ICollection<GameplayFusionPatternEvidence> evidence)
    {
        IReadOnlyList<HitObject> objects = beatmap.HitObjects;
        var chain = TechPatternAnalyzer.AnalyzeSliderChain(objects);
        Add(evidence, "Slider Chain", FusionPatternFamily.Tech, chain.Coverage, chain.Confidence, chain.AverageSliderRatio);
        var complex = TechPatternAnalyzer.AnalyzeComplexSlider(beatmap);
        Add(evidence, "Complex Slider", FusionPatternFamily.Tech, complex.Coverage, complex.Confidence, complex.AverageComplexityScore);
        var direction = TechPatternAnalyzer.AnalyzeSliderDirectionChange(beatmap);
        Add(evidence, "Slider Direction Change", FusionPatternFamily.Tech, direction.Coverage, direction.Confidence, direction.AverageDirectionChangeScore);
        var circleSlider = TechPatternAnalyzer.AnalyzeCircleSliderTransition(objects);
        Add(evidence, "Circle / Slider Transition", FusionPatternFamily.Tech, circleSlider.Coverage, circleSlider.Confidence,
            circleSlider.AverageTransitionScore, readingWeight: .5);
        var rhythm = TechPatternAnalyzer.AnalyzeRhythmTransition(objects);
        Add(evidence, "Rhythm Transition", FusionPatternFamily.Tech, rhythm.Coverage, rhythm.Confidence,
            rhythm.AverageTransitionScore, readingWeight: 1);
        var spatial = TechPatternAnalyzer.AnalyzeSpatialAwkwardTransition(objects);
        Add(evidence, "Spatial / Awkward Transition", FusionPatternFamily.Tech, spatial.Coverage, spatial.Confidence,
            spatial.AverageSpatialAwkwardScore, readingWeight: .5);
        var overlap = TechPatternAnalyzer.AnalyzeOverlap(objects);
        Add(evidence, "Overlap", FusionPatternFamily.Tech, overlap.Coverage, overlap.Confidence, overlap.AverageOverlapStrength);
    }

    private static void Add(
        ICollection<GameplayFusionPatternEvidence> evidence,
        string name,
        FusionPatternFamily family,
        double coverage,
        double confidence,
        double? intensity = null,
        double readingWeight = 0,
        double staminaWeight = 0) => evidence.Add(new(
            name,
            family,
            Signal(coverage),
            Signal(confidence),
            intensity.HasValue ? Signal(intensity.Value) : null,
            Signal(readingWeight),
            Signal(staminaWeight)));

    private static double AggregateFamily(
        IReadOnlyList<GameplayFusionPatternEvidence> evidence,
        FusionPatternFamily family,
        int familySize) => Aggregate(
            evidence.Where(item => item.Family == family),
            _ => 1,
            familySize);

    private static double AggregateMapped(
        IReadOnlyList<GameplayFusionPatternEvidence> evidence,
        Func<GameplayFusionPatternEvidence, double> weight,
        int mappedPatternCount) => Aggregate(evidence, weight, mappedPatternCount);

    private static double Aggregate(
        IEnumerable<GameplayFusionPatternEvidence> evidence,
        Func<GameplayFusionPatternEvidence, double> mappingWeight,
        int familySize)
    {
        double[] strengths = evidence
            .Where(item => Signal(item.Coverage) > 0 && Signal(mappingWeight(item)) > 0)
            .Select(item => PatternStrength(item) * Signal(mappingWeight(item)))
            .Where(value => value > 0)
            .ToArray();
        if (strengths.Length == 0) return 0;

        double breadth = Math.Clamp(strengths.Length / (double)Math.Max(1, familySize), 0, 1);
        return Signal(.70 * strengths.Max() + .25 * strengths.Average() + .05 * breadth);
    }

    private static double PatternStrength(GameplayFusionPatternEvidence evidence)
    {
        double numerator = .50 * Signal(evidence.Coverage) + .30 * Signal(evidence.Confidence);
        double denominator = .80;
        if (evidence.Intensity.HasValue)
        {
            numerator += .20 * Signal(evidence.Intensity.Value);
            denominator += .20;
        }
        return Signal(numerator / denominator);
    }

    private static StructureDecision DecideStructures(double jump, double stream, double tech)
    {
        var ranked = new[]
        {
            new StructureScore(GameplayStructureKind.Jump, jump),
            new StructureScore(GameplayStructureKind.Stream, stream),
            new StructureScore(GameplayStructureKind.Tech, tech)
        }.OrderByDescending(item => item.Score).ToArray();

        double top = ranked[0].Score;
        double second = ranked[1].Score;
        double lowSignal = 100 - top;
        double balance = top <= 0 ? 100 : 100 * (1 - ((top - second) / top));
        double mixed = Score(Math.Max(lowSignal, balance));
        bool mixedDominant = top < 20 || top - second <= 5;
        GameplayStructureKind dominant = mixedDominant
            ? GameplayStructureKind.ClassicMixed
            : ranked[0].Kind;
        GameplayStructureKind? secondary = mixedDominant
            ? top >= 10 ? ranked[0].Kind : null
            : second >= 15 ? ranked[1].Kind : null;
        return new(dominant, secondary, mixed);
    }

    private static double ApplyBonus(double baseScore, double support, double maximumBonus) =>
        Score(baseScore * (1 + Signal(support) * maximumBonus));

    private static double Score(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;
    private static double Signal(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    private sealed record StructureScore(GameplayStructureKind Kind, double Score);
    private sealed record StructureDecision(
        GameplayStructureKind Dominant,
        GameplayStructureKind? Secondary,
        double MixedScore);
}
