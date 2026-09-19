using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Analysis.StreamPatterns;
using BeatInsight.Analysis.TechPatterns;
using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.JumpPatterns;
using BeatInsight.Models.SkillPressure;

namespace BeatInsight.Analysis.SkillPressure;

/// <summary>
/// Diagnostic Skill Pressure V2. Existing analyzers are read only. Shared metrics are used
/// with different, documented roles and reduced weights; no score feeds another score.
/// </summary>
public static class SkillPressureAnalyzer
{
    public static SkillPressureProfile Analyze(Beatmap beatmap)
    {
        ArgumentNullException.ThrowIfNull(beatmap);
        SkillPressureProfile result = Analyze(BuildInput(beatmap));
        GameplayDebug.SkillPressure(result);
        return result;
    }

    public static SkillPressureProfile Analyze(SkillPressureInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.ObjectCount <= 0)
        {
            SkillPressureMetric unavailable = SkillPressureMetric.Unavailable("No hit objects are available.");
            return new(unavailable, unavailable, unavailable, unavailable, unavailable, unavailable,
                unavailable, unavailable, 0, 0, ["EmptyMap"]);
        }
        double quantity = DataQuantity(input.ObjectCount);

        SkillPressureMetric stamina = Metric(
            input,
            quantity,
            "Stamina measures repeated local rapid effort, not global map endurance.",
            (input.StaminaCoverage, .20, "Coverage"),
            (input.StaminaRepetition, .25, "Repetition"),
            (input.StaminaActiveRatio, .20, "ActiveRatio"),
            (input.StaminaRecoveryPressure, .15, "RecoveryPressure"),
            (input.RapidObjectVolume, .15, "RapidVolume"),
            (input.LongRapidSupport, .05, "LongRapidContext"));

        double duration = Math.Clamp((Finite(input.DurationSeconds) - 60) / 240, 0, 1);
        SkillPressureMetric endurance = Metric(
            input,
            quantity,
            "Endurance is global duration and pressure spread; Stamina evidence is only a limited sustained-context input.",
            (duration, .25, "Duration"),
            (input.GlobalPressureCoverage, .30, "GlobalPressure"),
            (input.PressureSectionSpread, .30, "SectionSpread"),
            (input.SustainedPressure, .15, "SustainedContext"));

        SkillPressureMetric finger = Metric(
            input,
            quantity,
            "Finger Control combines sustained irregularity, alternation and regime changes; no raw interval variance is used.",
            (input.IrregularStrength, .50, "Irregular"),
            (input.AlternatingStrength, .30, "Alternating"),
            (input.RhythmTransitionStrength, .15, "RhythmTransitions"),
            (input.TemporalBreadth, .05, "TemporalBreadth"));

        SkillPressureMetric burst = Metric(
            input,
            quantity,
            "Burst Control requires repeated short rapid runs; Speed is context-free and is not an input.",
            (input.BurstCoverage, .25, "Coverage"),
            (input.BurstRepetition, .30, "Repetition"),
            (input.BurstIntensity, .20, "Intensity"),
            (input.BurstLength, .15, "Length"),
            (input.BurstVariation, .10, "Variation"));

        SkillPressureMetric aimControl = Metric(
            input,
            quantity,
            "Aim Control emphasizes non-linear directional control; raw Aim contributes only 15% context.",
            (input.BaseAim / 100, .15, "AimContext"),
            (input.AimAngleControl, .20, "AngleControl"),
            (input.JumpControlPatternStrength, .35, "JumpControlPatterns"),
            (input.SpatialAwkwardStrength, .20, "SpatialAwkward"),
            (input.SliderDirectionStrength, .10, "SliderDirection"));

        SkillPressureMetric precision = input.PrecisionGeometryAvailable
            ? Metric(
                input,
                quantity,
                "Precision requires geometry relative to a reliable playable margin.",
                (input.PrecisionGeometryStrength, .70, "RelativeGeometry"),
                (input.PrecisionDemandCoverage, .30, "Coverage"))
            : SkillPressureMetric.Unavailable(
                "Current runtime exposes CS and an Aim precision modifier, but no reliable object-spacing-to-playable-margin geometry. CS alone is insufficient.");

        SkillPressureMetric reading = Metric(
            input,
            quantity,
            "Reading Pressure retains visual-load signals; ReadScore is only 35% context and patterns add 10%.",
            (input.BaseReading / 100, .35, "ReadContext"),
            (input.ReadingDensity, .20, "Density"),
            (input.ReadingClutter, .15, "Clutter"),
            (input.ReadingAmbiguity, .15, "Ambiguity"),
            (input.ReadingNovelty, .05, "Novelty"),
            (input.ReadingPatternStrength, .10, "PatternTransitions"));

        bool rhythmAvailable = input.ObjectCount >= 5;
        SkillPressureMetric rhythm = rhythmAvailable
            ? Metric(
                input,
                quantity,
                "Rhythm Complexity requires regime transitions plus sustained irregular/alternating evidence; it is not interval variance.",
                (input.RhythmTransitionStrength, .45, "RegimeTransitions"),
                (input.IrregularStrength, .35, "SustainedIrregularity"),
                (input.AlternatingStrength, .10, "AlternatingControl"),
                (input.TemporalBreadth, .10, "MultiRegimeBreadth"))
            : SkillPressureMetric.Unavailable("At least five chronological objects are required for multi-window temporal evidence.");

        SkillPressureMetric[] metrics = [stamina, endurance, finger, burst, aimControl, precision, reading, rhythm];
        SkillPressureMetric[] available = metrics.Where(metric => metric.Available).ToArray();
        double confidence = available.Length == 0 ? 0 : available.Average(metric => metric.Confidence);
        var diagnostics = new List<string>();
        if (!precision.Available) diagnostics.Add("PrecisionUnavailable");
        if (!rhythm.Available) diagnostics.Add("RhythmComplexityUnavailable");
        if ((stamina.Score ?? 0) >= 60 && (endurance.Score ?? 0) < 30) diagnostics.Add("LocalStaminaWithoutGlobalEndurance");
        if ((input.BaseAim >= 60) && (aimControl.Score ?? 0) < 30) diagnostics.Add("HighAimLowAimControl");
        if ((input.BaseReading >= 60) && input.ReadingPatternStrength < .15) diagnostics.Add("HighReadingLowPatternSupport");

        return new(stamina, endurance, finger, burst, aimControl, precision, reading, rhythm,
            Signal(confidence), available.Length, diagnostics);
    }

    private static SkillPressureInput BuildInput(Beatmap beatmap)
    {
        IReadOnlyList<HitObject> objects = beatmap.HitObjects;
        GameplayProfile profile = beatmap.GameplayProfile;
        var stamina = StreamPatternAnalyzer.AnalyzeStaminaStream(objects);
        var longStream = StreamPatternAnalyzer.AnalyzeLongStream(objects);
        var deathstream = StreamPatternAnalyzer.AnalyzeDeathstream(objects);
        var irregular = StreamPatternAnalyzer.AnalyzeIrregularStream(objects);
        var alternating = StreamPatternAnalyzer.AnalyzeAlternatingStream(objects);
        var burst = StreamPatternAnalyzer.AnalyzeBurst(objects);
        var rhythm = TechPatternAnalyzer.AnalyzeRhythmTransition(objects);
        var spatial = TechPatternAnalyzer.AnalyzeSpatialAwkwardTransition(objects);
        var sliderDirection = TechPatternAnalyzer.AnalyzeSliderDirectionChange(beatmap);
        var circleSlider = TechPatternAnalyzer.AnalyzeCircleSliderTransition(objects);
        var overlap = TechPatternAnalyzer.AnalyzeOverlap(objects);
        JumpPatternMatch[] jumps = JumpPatternAnalyzer.Analyze(objects, profile).ToArray();

        double durationSeconds = objects.Count < 2 ? 0 : Math.Max(0, (objects[^1].Time - objects[0].Time) / 1000.0);
        double staminaRepetition = SaturateCount(Math.Max(0, stamina.StaminaSequenceCount - 1), 2);
        double recovery = stamina.StaminaSequenceCount == 0 ? 0 : 1 - Math.Clamp(stamina.AverageRecoveryGap / 1500, 0, 1);
        double rapidVolume = SaturateCount(stamina.StaminaObjectCount, 48);
        double longRapid = Math.Max(longStream.Coverage, deathstream.Coverage);
        double sustained = Math.Max(stamina.Coverage, longRapid);
        double globalPressure = Math.Max(profile.StreamRatio, Math.Max(profile.JumpRatio, profile.TechPresence));
        double sectionSpread = CalculatePressureSpread(beatmap, profile);

        double irregularStrength = Strength(irregular.Coverage, irregular.Confidence, irregular.AverageIrregularityScore);
        double alternatingStrength = Strength(alternating.Coverage, alternating.Confidence, alternating.AverageAlternatingScore);
        double rhythmStrength = Strength(rhythm.Coverage, rhythm.Confidence, rhythm.AverageTransitionScore);
        double temporalBreadth = Signal(new[] { irregularStrength, alternatingStrength, rhythmStrength }.Count(value => value > 0) / 3.0);

        double burstRepetition = SaturateCount(Math.Max(0, burst.BurstSequenceCount - 1), 6);
        double burstIntensity = burst.BurstSequenceCount == 0 ? 0 : Math.Clamp((130 - burst.AverageInterval) / 100, 0, 1);
        double burstLength = burst.BurstSequenceCount == 0 ? 0 : Math.Clamp((burst.AverageBurstLength - 3) / 5, 0, 1);
        double burstVariation = burst.BurstSequenceCount < 2 ? 0 : Math.Clamp((burst.LongestBurst - burst.AverageBurstLength) / 5, 0, 1);

        JumpPatternType[] controlTypes =
        [
            JumpPatternType.Sharp, JumpPatternType.Reversal, JumpPatternType.Arc,
            JumpPatternType.WideAngle, JumpPatternType.AlternatingSpacing, JumpPatternType.Triangle
        ];
        JumpPatternMatch[] control = jumps.Where(match => controlTypes.Contains(match.Type)).ToArray();
        double controlCoverage = ObjectCoverage(control, objects.Count);
        double controlConfidence = control.Length == 0 ? 0 : control.Average(match => Signal(match.Confidence));
        double jumpControl = Strength(controlCoverage, controlConfidence, null);
        double spatialStrength = Strength(spatial.Coverage, spatial.Confidence, spatial.AverageSpatialAwkwardScore);
        double directionStrength = Strength(sliderDirection.Coverage, sliderDirection.Confidence, sliderDirection.AverageDirectionChangeScore);

        double readingPatterns = new[]
        {
            rhythmStrength,
            irregularStrength,
            alternatingStrength * .75,
            Strength(circleSlider.Coverage, circleSlider.Confidence, circleSlider.AverageTransitionScore) * .5,
            spatialStrength * .5,
            Strength(overlap.Coverage, overlap.Confidence, overlap.AverageOverlapStrength) * .25
        }.Max();

        return new SkillPressureInput
        {
            ObjectCount = objects.Count,
            DurationSeconds = durationSeconds,
            BaseAim = profile.AimScore,
            BaseSpeed = profile.SpeedScore,
            BaseReading = profile.ReadScore,
            StaminaCoverage = stamina.Coverage,
            StaminaRepetition = staminaRepetition,
            StaminaActiveRatio = stamina.AverageActiveRatio,
            StaminaRecoveryPressure = recovery,
            RapidObjectVolume = rapidVolume,
            LongRapidSupport = longRapid,
            GlobalPressureCoverage = globalPressure,
            PressureSectionSpread = sectionSpread,
            SustainedPressure = sustained,
            IrregularStrength = irregularStrength,
            AlternatingStrength = alternatingStrength,
            RhythmTransitionStrength = rhythmStrength,
            TemporalBreadth = temporalBreadth,
            BurstCoverage = burst.Coverage,
            BurstRepetition = burstRepetition,
            BurstIntensity = burstIntensity,
            BurstLength = burstLength,
            BurstVariation = burstVariation,
            AimAngleControl = profile.AimAngleSignal,
            JumpControlPatternStrength = jumpControl,
            SpatialAwkwardStrength = spatialStrength,
            SliderDirectionStrength = directionStrength,
            ReadingDensity = profile.ReadDensitySignal,
            ReadingClutter = profile.ReadClutterSignal,
            ReadingAmbiguity = profile.ReadAmbiguity,
            ReadingNovelty = profile.ReadNovelty,
            ReadingPatternStrength = readingPatterns,
            PrecisionGeometryAvailable = false
        };
    }

    private static SkillPressureMetric Metric(
        SkillPressureInput input,
        double dataQuantity,
        string limitation,
        params (double Value, double Weight, string Name)[] signals)
    {
        (double Value, double Weight, string Name)[] valid = signals
            .Where(signal => double.IsFinite(signal.Value) && signal.Weight > 0)
            .Select(signal => (Signal(signal.Value), signal.Weight, signal.Name))
            .ToArray();
        if (valid.Length == 0) return SkillPressureMetric.Unavailable(limitation);

        double weight = valid.Sum(signal => signal.Weight);
        double normalized = weight <= 0 ? 0 : valid.Sum(signal => signal.Value * signal.Weight) / weight;
        double mean = valid.Average(signal => signal.Value);
        double dispersion = valid.Average(signal => Math.Abs(signal.Value - mean));
        double coherence = 1 - Math.Clamp(dispersion * 2, 0, 1);
        double availability = valid.Length / (double)signals.Length;
        double confidence = Signal(.40 * availability + .35 * dataQuantity + .25 * coherence);
        return new(
            Score(normalized * 100),
            confidence,
            true,
            confidence >= .50,
            valid.ToDictionary(signal => signal.Name, signal => signal.Value),
            limitation);
    }

    private static double CalculatePressureSpread(Beatmap beatmap, GameplayProfile profile)
    {
        if (beatmap.HitObjects.Count == 0) return 0;
        var indices = new HashSet<int>();
        foreach (PatternSequence sequence in profile.StreamSequences.Concat(profile.JumpSequences))
            for (int index = sequence.StartObjectIndex; index <= sequence.EndObjectIndex; index++) indices.Add(index);
        foreach (GameplaySection section in profile.TechSections)
            for (int index = section.StartObjectIndex; index <= section.EndObjectIndex; index++) indices.Add(index);
        if (indices.Count == 0) return 0;
        const int bucketCount = 8;
        int firstTime = beatmap.HitObjects[0].Time;
        int lastTime = beatmap.HitObjects[^1].Time;
        double duration = Math.Max(1, lastTime - firstTime);
        int activeBuckets = indices.Select(index => Math.Clamp(
            (int)(((beatmap.HitObjects[index].Time - firstTime) / duration) * bucketCount), 0, bucketCount - 1)).Distinct().Count();
        return activeBuckets / (double)bucketCount;
    }

    private static double ObjectCoverage(IEnumerable<JumpPatternMatch> matches, int total) => total == 0
        ? 0
        : matches.SelectMany(match => Enumerable.Range(match.StartObjectIndex, match.ObjectCount)).Distinct().Count() / (double)total;

    private static double Strength(double coverage, double confidence, double? intensity)
    {
        double numerator = .5 * Signal(coverage) + .3 * Signal(confidence);
        double denominator = .8;
        if (intensity.HasValue) { numerator += .2 * Signal(intensity.Value); denominator = 1; }
        return Signal(numerator / denominator);
    }

    private static double SaturateCount(int count, double scale) => count <= 0 ? 0 : 1 - Math.Exp(-count / scale);
    private static double DataQuantity(int objects) => objects <= 0 ? 0 : Math.Clamp(Math.Log10(objects + 1) / 3, 0, 1);
    private static double Finite(double value) => double.IsFinite(value) ? value : 0;
    private static double Signal(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
    private static double Score(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;
}
