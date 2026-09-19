using System.Collections.ObjectModel;
using System.Diagnostics;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Analysis.SkillPressure;
using BeatInsight.Analysis.StreamPatterns;
using BeatInsight.Analysis.TechPatterns;
using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.Fusion;
using BeatInsight.Models.Sections;
using BeatInsight.Models.SkillPressure;
using BeatInsight.Models.TechPatterns;

namespace BeatInsight.Analysis.Sections;

/// <summary>
/// Read-only fixed-window Section Analysis V2. Frozen global analyzers are
/// consumed without changing Identity, Rating, Fusion, Pattern or Skill Pressure semantics.
/// </summary>
public static class SectionAnalysisV2Analyzer
{
    public static SectionAnalysisResult Analyze(
        Beatmap beatmap,
        SectionAnalysisV2Options? options = null)
    {
        ArgumentNullException.ThrowIfNull(beatmap);
        options ??= new SectionAnalysisV2Options();
        Validate(options);
        var stopwatch = Stopwatch.StartNew();

        IReadOnlyList<HitObject> objects = beatmap.HitObjects;
        if (objects.Count == 0)
        {
            stopwatch.Stop();
            SectionAnalysisResult empty = SectionAnalysisResult.Empty(stopwatch.Elapsed);
            GameplayDebug.SectionAnalysisV2(empty);
            return empty;
        }

        IReadOnlyList<GameplaySectionAnalysis> partition = Segment(objects, options);
        GameplayProfile globalProfile = GameplayAnalyzer.Analyze(beatmap);

        IReadOnlyList<GameplaySectionAnalysis> jumpProjected = SectionJumpPatternAggregator.Aggregate(
            partition,
            JumpPatternAnalyzer.Analyze(objects, globalProfile));

        SectionStreamPatternAnalysisResults streamResults = AnalyzeStreamPatterns(objects);
        IReadOnlyList<GameplaySectionAnalysis> projected = SectionStreamPatternAggregator.Aggregate(
            jumpProjected,
            streamResults);
        IReadOnlyList<SectionPatternFamilyV2> techFamilies = ProjectTechPatterns(beatmap, projected);

        var candidates = new List<SectionCandidate>(projected.Count);
        for (int index = 0; index < projected.Count; index++)
        {
            GameplaySectionAnalysis cell = projected[index];
            Beatmap localMap = CreateLocalBeatmap(beatmap, cell.ObjectIndices);
            GameplayProfile localProfile = GameplayAnalyzer.Analyze(localMap);
            SectionPatternFamilyV2 jumpFamily = BuildJumpFamily(cell.JumpPatterns);
            SectionPatternFamilyV2 streamFamily = BuildStreamFamily(cell.StreamPatterns);
            SectionPatternFamilyV2 techFamily = techFamilies[index];

            StructureDecision structure = DecideStructure(
                localProfile.Identity.JumpScore,
                localProfile.Identity.StreamScore,
                localProfile.Identity.TechScore);
            var baseSkills = new SectionLocalSkills(
                Score(localProfile.AimScore),
                Score(localProfile.SpeedScore),
                Score(localProfile.ReadScore),
                Score(localProfile.TechScore));
            SkillPressureProfile pressure = BuildLocalSkillPressure(
                cell,
                localProfile,
                jumpFamily,
                streamFamily,
                techFamily);

            double durationSeconds = Math.Max(0.001, cell.Duration / 1000.0);
            double density = cell.ObjectCount / durationSeconds;
            DifficultyCalculation difficulty = CalculateLocalDifficulty(
                baseSkills,
                pressure,
                jumpFamily.Support,
                streamFamily.Support,
                techFamily.Support,
                density,
                cell.ObjectCount,
                cell.Duration,
                options.SectionDurationMilliseconds);

            var section = new GameplaySectionV2Analysis(
                cell.Index,
                cell.StartTime,
                cell.EndTime,
                cell.ObjectIndices,
                cell.CircleCount,
                cell.SliderCount,
                cell.SpinnerCount,
                cell.OtherObjectCount,
                Signal(density),
                Score(localProfile.Identity.JumpScore),
                Score(localProfile.Identity.StreamScore),
                Score(localProfile.Identity.TechScore),
                Score(localProfile.BurstRatio * 100),
                structure.Dominant,
                structure.Secondary,
                baseSkills,
                pressure,
                jumpFamily,
                streamFamily,
                techFamily,
                difficulty.Score,
                difficulty.Confidence,
                SectionDifficultySpike.None,
                SectionGameplayTransition.First(structure.Dominant),
                string.Empty);

            candidates.Add(new SectionCandidate(section, difficulty.Contributors));
        }

        ApplySpikes(candidates, options);
        ApplyTransitions(candidates, options);
        for (int index = 0; index < candidates.Count; index++)
        {
            GameplaySectionV2Analysis section = candidates[index].Section;
            candidates[index] = candidates[index] with { Section = section with { Summary = BuildSummary(section) } };
        }

        GameplaySectionV2Analysis[] sections = candidates.Select(candidate => candidate.Section).ToArray();
        stopwatch.Stop();
        SectionAnalysisResult result = BuildResult(sections, stopwatch.Elapsed);
        GameplayDebug.SectionAnalysisV2(result);
        return result;
    }

    public static IReadOnlyList<GameplaySectionAnalysis> Segment(
        IReadOnlyList<HitObject> objects,
        SectionAnalysisV2Options? options = null)
    {
        ArgumentNullException.ThrowIfNull(objects);
        options ??= new SectionAnalysisV2Options();
        Validate(options);
        if (objects.Count == 0) return Array.Empty<GameplaySectionAnalysis>();
        for (int index = 1; index < objects.Count; index++)
            if (objects[index].Time < objects[index - 1].Time)
                throw new ArgumentException("Section Analysis V2 requires chronological objects.", nameof(objects));

        double first = objects[0].Time;
        double size = options.SectionDurationMilliseconds;
        int sectionCount = (int)Math.Floor((objects[^1].Time - first) / size) + 1;
        var assignments = Enumerable.Range(0, sectionCount).Select(_ => new List<int>()).ToArray();
        for (int objectIndex = 0; objectIndex < objects.Count; objectIndex++)
        {
            int sectionIndex = (int)Math.Floor((objects[objectIndex].Time - first) / size);
            assignments[Math.Clamp(sectionIndex, 0, sectionCount - 1)].Add(objectIndex);
        }

        double exclusiveGameplayEnd = Math.Max(first + 1, objects[^1].Time + 1.0);
        var result = new GameplaySectionAnalysis[sectionCount];
        for (int index = 0; index < sectionCount; index++)
        {
            double start = first + index * size;
            double end = index == sectionCount - 1
                ? Math.Min(start + size, exclusiveGameplayEnd)
                : start + size;
            if (end <= start) end = start + 1;
            int circles = 0, sliders = 0, spinners = 0, other = 0;
            foreach (int objectIndex in assignments[index])
            {
                int type = objects[objectIndex].Type;
                if ((type & 1) == 1) circles++;
                else if ((type & 2) == 2) sliders++;
                else if ((type & 8) == 8) spinners++;
                else other++;
            }
            result[index] = new GameplaySectionAnalysis(
                index, start, end, circles, sliders, spinners, other,
                new ReadOnlyCollection<int>(assignments[index].ToArray()));
        }
        return Array.AsReadOnly(result);
    }

    internal static DifficultyCalculation CalculateLocalDifficulty(
        SectionLocalSkills baseSkills,
        SkillPressureProfile pressure,
        double jumpSupport,
        double streamSupport,
        double techSupport,
        double objectsPerSecond,
        int objectCount,
        double durationMilliseconds,
        double targetDurationMilliseconds = 5_000)
    {
        double[] baseSignals =
        [
            Score(baseSkills.Aim) / 100,
            Score(baseSkills.Speed) / 100,
            Score(baseSkills.Reading) / 100,
            Score(baseSkills.Tech) / 100
        ];
        SkillPressureMetric[] pressureMetrics =
        [
            pressure.Stamina, pressure.FingerControl, pressure.BurstControl,
            pressure.AimControl, pressure.ReadingPressure, pressure.RhythmComplexity
        ];
        double[] pressureSignals = pressureMetrics
            .Where(metric => metric.Available && metric.Reliable && metric.Score.HasValue)
            .Select(metric => metric.Score!.Value / 100)
            .ToArray();
        double baseSignal = baseSignals.Average();
        double pressureSignal = pressureSignals.Length == 0 ? 0 : pressureSignals.Average();
        double patternSignal = new[] { Signal(jumpSupport), Signal(streamSupport), Signal(techSupport) }.Average();
        double densitySignal = 1 - Math.Exp(-Math.Max(0, objectsPerSecond) / 4.0);
        double score = Score(100 * (.45 * baseSignal + .30 * pressureSignal + .15 * patternSignal + .10 * densitySignal));

        double objectSignal = 1 - Math.Exp(-Math.Max(0, objectCount) / 12.0);
        double availability = (4 + pressureSignals.Length) / 10.0;
        double durationSignal = targetDurationMilliseconds <= 0
            ? 0
            : Signal(durationMilliseconds / targetDurationMilliseconds);
        double[] agreementInputs = baseSignals.Concat(pressureSignals).ToArray();
        double agreementMean = agreementInputs.Length == 0 ? 0 : agreementInputs.Average();
        double agreement = agreementInputs.Length == 0
            ? 0
            : 1 - Math.Clamp(agreementInputs.Average(value => Math.Abs(value - agreementMean)) * 2, 0, 1);
        double confidence = Signal(new[] { objectSignal, availability, durationSignal, agreement }.Average());

        var contributors = new Dictionary<string, double>
        {
            ["Aim"] = baseSignals[0], ["Speed"] = baseSignals[1],
            ["Reading"] = baseSignals[2], ["Tech"] = baseSignals[3],
            ["Stamina"] = Value(pressure.Stamina), ["BurstControl"] = Value(pressure.BurstControl),
            ["AimControl"] = Value(pressure.AimControl), ["RhythmComplexity"] = Value(pressure.RhythmComplexity),
            ["Density"] = densitySignal, ["JumpPatternSupport"] = Signal(jumpSupport),
            ["StreamPatternSupport"] = Signal(streamSupport), ["TechPatternSupport"] = Signal(techSupport)
        };
        return new DifficultyCalculation(score, confidence, contributors);
    }

    internal static SectionDifficultySpike CalculateSpike(
        double difficulty,
        IReadOnlyList<double> neighbors,
        IReadOnlyDictionary<string, double> contributors,
        SectionAnalysisV2Options options)
    {
        if (neighbors.Count == 0) return SectionDifficultySpike.None;
        double baseline = neighbors.Average();
        double delta = difficulty - baseline;
        double ratio = baseline <= 0 ? (difficulty > 0 ? double.PositiveInfinity : 0) : difficulty / baseline;
        bool qualified = delta >= options.SpikeMinimumDelta
            && ratio >= options.SpikeMinimumRatio
            && difficulty >= options.SpikeMinimumDifficulty;
        double finiteRatio = double.IsFinite(ratio) ? ratio : 2;
        double strength = qualified
            ? Signal((Signal(delta / 30) + Signal((finiteRatio - 1) / .50) + Signal(difficulty / 100)) / 3)
            : 0;
        string[] top = qualified
            ? contributors.OrderByDescending(item => item.Value).ThenBy(item => item.Key).Take(3).Select(item => item.Key).ToArray()
            : Array.Empty<string>();
        return new SectionDifficultySpike(qualified, baseline, delta, ratio, strength, top);
    }

    internal static double StructureVectorDistance(GameplaySectionV2Analysis left, GameplaySectionV2Analysis right) =>
        Signal(Math.Sqrt(
            Math.Pow((left.JumpScore - right.JumpScore) / 100, 2)
            + Math.Pow((left.StreamScore - right.StreamScore) / 100, 2)
            + Math.Pow((left.TechScore - right.TechScore) / 100, 2)) / Math.Sqrt(3));

    private static void ApplySpikes(List<SectionCandidate> sections, SectionAnalysisV2Options options)
    {
        for (int index = 0; index < sections.Count; index++)
        {
            var neighbors = new List<double>();
            int start = Math.Max(0, index - options.SpikeNeighborRadius);
            int end = Math.Min(sections.Count - 1, index + options.SpikeNeighborRadius);
            for (int other = start; other <= end; other++)
                if (other != index) neighbors.Add(sections[other].Section.LocalDifficultyScore);
            SectionDifficultySpike spike = CalculateSpike(
                sections[index].Section.LocalDifficultyScore,
                neighbors,
                sections[index].Contributors,
                options);
            sections[index] = sections[index] with { Section = sections[index].Section with { Spike = spike } };
        }
    }

    private static void ApplyTransitions(List<SectionCandidate> sections, SectionAnalysisV2Options options)
    {
        for (int index = 1; index < sections.Count; index++)
        {
            GameplaySectionV2Analysis previous = sections[index - 1].Section;
            GameplaySectionV2Analysis current = sections[index].Section;
            double structureDelta = StructureVectorDistance(previous, current);
            double skillDelta = SkillDistance(previous.SkillPressure, current.SkillPressure);
            double patternDelta = new[]
            {
                Math.Abs(previous.JumpPatterns.Support - current.JumpPatterns.Support),
                Math.Abs(previous.StreamPatterns.Support - current.StreamPatterns.Support),
                Math.Abs(previous.TechPatterns.Support - current.TechPatterns.Support)
            }.Average();
            bool dominantChanged = previous.DominantStructure != current.DominantStructure;
            bool flicker = index + 1 < sections.Count
                && sections[index + 1].Section.DominantStructure == previous.DominantStructure
                && structureDelta < options.FlickerProtectionVectorDistance
                && StructureVectorDistance(current, sections[index + 1].Section) < options.FlickerProtectionVectorDistance;
            bool qualified = dominantChanged
                && structureDelta >= options.MinimumTransitionVectorDistance
                && !flicker;
            double strength = qualified
                ? Signal((1 + structureDelta + skillDelta + patternDelta) / 4)
                : 0;
            var transition = new SectionGameplayTransition(
                qualified,
                previous.DominantStructure,
                current.DominantStructure,
                structureDelta,
                skillDelta,
                patternDelta,
                strength);
            sections[index] = sections[index] with { Section = current with { Transition = transition } };
        }
    }

    private static SectionAnalysisResult BuildResult(GameplaySectionV2Analysis[] sections, TimeSpan duration)
    {
        if (sections.Length == 0) return SectionAnalysisResult.Empty(duration);
        GameplaySectionV2Analysis maximum = sections.OrderByDescending(section => section.LocalDifficultyScore).ThenBy(section => section.SectionIndex).First();
        return new SectionAnalysisResult(
            Array.AsReadOnly(sections),
            sections.Average(section => section.LocalDifficultyScore),
            maximum.LocalDifficultyScore,
            maximum.SectionIndex,
            sections.Count(section => section.Spike.IsSpike),
            sections.Count(section => section.Transition.IsTransition),
            sections.Select(section => section.DominantStructure).ToArray(),
            sections.Select(section => section.LocalDifficultyScore).ToArray(),
            sections.Select(section => new SectionPatternTimelinePoint(section.JumpPatterns.Support, section.StreamPatterns.Support, section.TechPatterns.Support)).ToArray(),
            sections.Select(section => new SectionSkillTimelinePoint(
                section.BaseSkills.Aim, section.BaseSkills.Speed, section.BaseSkills.Reading, section.BaseSkills.Tech,
                section.SkillPressure.Stamina.Score, section.SkillPressure.FingerControl.Score,
                section.SkillPressure.BurstControl.Score, section.SkillPressure.AimControl.Score,
                section.SkillPressure.ReadingPressure.Score, section.SkillPressure.RhythmComplexity.Score)).ToArray(),
            sections.Average(section => section.LocalDifficultyConfidence),
            duration);
    }

    private static SkillPressureProfile BuildLocalSkillPressure(
        GameplaySectionAnalysis section,
        GameplayProfile localProfile,
        SectionPatternFamilyV2 jump,
        SectionPatternFamilyV2 stream,
        SectionPatternFamilyV2 tech)
    {
        SectionPatternMetricV2 stamina = Get(stream, "Stamina");
        SectionPatternMetricV2 irregular = Get(stream, "Irregular");
        SectionPatternMetricV2 alternating = Get(stream, "Alternating");
        SectionPatternMetricV2 burst = Get(stream, "Burst");
        SectionPatternMetricV2 longStream = Get(stream, "Long");
        SectionPatternMetricV2 deathstream = Get(stream, "Deathstream");
        SectionPatternMetricV2 rhythm = Get(tech, "RhythmTransition");
        SectionPatternMetricV2 spatial = Get(tech, "SpatialAwkward");
        SectionPatternMetricV2 sliderDirection = Get(tech, "SliderDirectionChange");
        double irregularStrength = PatternStrength(irregular);
        double alternatingStrength = PatternStrength(alternating);
        double rhythmStrength = PatternStrength(rhythm);
        double temporalBreadth = new[] { irregularStrength, alternatingStrength, rhythmStrength }.Count(value => value > 0) / 3.0;
        double controlPatterns = new[] { "Sharp", "Reversal", "Arc", "WideAngle", "AlternatingSpacing", "Triangle" }
            .Select(name => PatternStrength(Get(jump, name))).DefaultIfEmpty().Max();

        SkillPressureProfile pressure = SkillPressureAnalyzer.Analyze(new SkillPressureInput
        {
            ObjectCount = section.ObjectCount,
            DurationSeconds = section.Duration / 1000,
            BaseAim = localProfile.AimScore,
            BaseSpeed = localProfile.SpeedScore,
            BaseReading = localProfile.ReadScore,
            StaminaCoverage = stamina.Coverage,
            StaminaRepetition = Saturate(Math.Max(0, stamina.OccurrenceCount - 1), 2),
            StaminaActiveRatio = stamina.Coverage,
            StaminaRecoveryPressure = stamina.OccurrenceCount > 1 ? 1 : 0,
            RapidObjectVolume = Saturate(stamina.ObjectCount, 48),
            LongRapidSupport = Math.Max(longStream.Coverage, deathstream.Coverage),
            GlobalPressureCoverage = Math.Max(localProfile.StreamRatio, Math.Max(localProfile.JumpRatio, localProfile.TechPresence)),
            PressureSectionSpread = 0,
            SustainedPressure = Math.Max(stamina.Coverage, Math.Max(longStream.Coverage, deathstream.Coverage)),
            IrregularStrength = irregularStrength,
            AlternatingStrength = alternatingStrength,
            RhythmTransitionStrength = rhythmStrength,
            TemporalBreadth = temporalBreadth,
            BurstCoverage = burst.Coverage,
            BurstRepetition = Saturate(Math.Max(0, burst.OccurrenceCount - 1), 6),
            BurstIntensity = burst.Intensity ?? 0,
            BurstLength = Signal((burst.ObjectCount - 3) / 5.0),
            BurstVariation = burst.OccurrenceCount > 1 ? .5 : 0,
            AimAngleControl = localProfile.AimAngleSignal,
            JumpControlPatternStrength = controlPatterns,
            SpatialAwkwardStrength = PatternStrength(spatial),
            SliderDirectionStrength = PatternStrength(sliderDirection),
            ReadingDensity = localProfile.ReadDensitySignal,
            ReadingClutter = localProfile.ReadClutterSignal,
            ReadingAmbiguity = localProfile.ReadAmbiguity,
            ReadingNovelty = localProfile.ReadNovelty,
            ReadingPatternStrength = Math.Max(tech.Support, Math.Max(irregularStrength, rhythmStrength)),
            PrecisionGeometryAvailable = false
        });

        return pressure with
        {
            Endurance = SkillPressureMetric.Unavailable(
                "Endurance is a global-duration skill. A five-second section exposes sustained contributors but no artificial local Endurance score.")
        };
    }

    private static SectionStreamPatternAnalysisResults AnalyzeStreamPatterns(IReadOnlyList<HitObject> objects) => new(
        StreamPatternAnalyzer.AnalyzeBurst(objects),
        StreamPatternAnalyzer.AnalyzeShortStream(objects),
        StreamPatternAnalyzer.AnalyzeLongStream(objects),
        StreamPatternAnalyzer.AnalyzeDeathstream(objects),
        StreamPatternAnalyzer.AnalyzeStaminaStream(objects),
        StreamPatternAnalyzer.AnalyzeIrregularStream(objects),
        StreamPatternAnalyzer.AnalyzeSpeedStream(objects),
        StreamPatternAnalyzer.AnalyzeFlowStream(objects),
        StreamPatternAnalyzer.AnalyzeAlternatingStream(objects));

    private static SectionPatternFamilyV2 BuildJumpFamily(SectionJumpPatternProfile profile) => BuildFamily(
    [
        ("Linear", profile.Linear), ("Edge", profile.Edge), ("Sharp", profile.Sharp),
        ("Triangle", profile.Triangle), ("OneTwo", profile.OneTwo), ("Spaced", profile.Spaced),
        ("WideAngle", profile.WideAngle), ("Reversal", profile.Reversal), ("Arc", profile.Arc),
        ("SpacingTrend", profile.SpacingTrend), ("AlternatingSpacing", profile.AlternatingSpacing)
    ]);

    private static SectionPatternFamilyV2 BuildStreamFamily(SectionStreamPatternProfile profile) => BuildFamily(
    [
        ("Burst", profile.Burst), ("Short", profile.Short), ("Long", profile.Long),
        ("Deathstream", profile.Deathstream), ("Stamina", profile.Stamina),
        ("Irregular", profile.Irregular), ("Speed", profile.Speed), ("Flow", profile.Flow),
        ("Alternating", profile.Alternating)
    ]);

    private static SectionPatternFamilyV2 BuildFamily(IEnumerable<(string Name, SectionPatternMetric Metric)> source)
    {
        Dictionary<string, SectionPatternMetricV2> metrics = source.ToDictionary(
            item => item.Name,
            item => new SectionPatternMetricV2(
                item.Metric.SequenceCount,
                item.Metric.ObjectCount,
                Signal(item.Metric.Coverage),
                Signal(item.Metric.AverageConfidence),
                null));
        return BuildFamily(metrics);
    }

    private static SectionPatternFamilyV2 BuildFamily(Dictionary<string, SectionPatternMetricV2> metrics)
    {
        SectionPatternMetricV2[] active = metrics.Values.Where(metric => metric.Coverage > 0 || metric.OccurrenceCount > 0).ToArray();
        double support = active.Length == 0 ? 0 : Signal(
            .70 * active.Max(PatternStrength)
            + .25 * active.Average(PatternStrength)
            + .05 * Signal(active.Length / (double)Math.Max(1, metrics.Count)));
        double coverage = active.Length == 0 ? 0 : active.Max(metric => metric.Coverage);
        return new SectionPatternFamilyV2(active.Sum(metric => metric.OccurrenceCount), coverage, support, metrics);
    }

    private static IReadOnlyList<SectionPatternFamilyV2> ProjectTechPatterns(
        Beatmap beatmap,
        IReadOnlyList<GameplaySectionAnalysis> sections)
    {
        IReadOnlyList<HitObject> objects = beatmap.HitObjects;
        var occurrences = new List<TechOccurrence>();
        var chain = TechPatternAnalyzer.AnalyzeSliderChain(objects);
        occurrences.AddRange(chain.Sequences.Select(run => Occurrence("SliderChain", run.StartIndex, run.StartIndex, run.EndIndex, chain.Confidence, run.SliderRatio)));
        var complex = TechPatternAnalyzer.AnalyzeComplexSlider(beatmap);
        occurrences.AddRange(complex.ComplexSliders.Select(item => new TechOccurrence("ComplexSlider", item.ObjectIndex, [item.ObjectIndex], complex.Confidence, item.SliderComplexityScore)));
        var direction = TechPatternAnalyzer.AnalyzeSliderDirectionChange(beatmap);
        occurrences.AddRange(direction.QualifiedOccurrences.Select(item => new TechOccurrence(
            "SliderDirectionChange", item.SliderIndex,
            new[] { item.PreviousObjectIndex, item.SliderIndex, item.NextObjectIndex }.Where(value => value.HasValue).Select(value => value!.Value).Distinct().Order().ToArray(),
            direction.Confidence, item.DirectionChangeScore)));
        var circleSlider = TechPatternAnalyzer.AnalyzeCircleSliderTransition(objects);
        occurrences.AddRange(circleSlider.Sequences.Select(run => Occurrence("CircleSliderTransition", run.StartIndex, run.StartIndex, run.EndIndex, circleSlider.Confidence, run.TransitionScore)));
        var rhythm = TechPatternAnalyzer.AnalyzeRhythmTransition(objects);
        occurrences.AddRange(rhythm.Transitions.Select(item => Occurrence("RhythmTransition", item.PivotIndex, item.StartIndex, item.EndIndex, rhythm.Confidence, item.RhythmTransitionScore)));
        var spatial = TechPatternAnalyzer.AnalyzeSpatialAwkwardTransition(objects);
        occurrences.AddRange(spatial.Transitions.Select(item => Occurrence("SpatialAwkward", item.PivotIndex, item.StartIndex, item.EndIndex, spatial.Confidence, item.SpatialAwkwardScore)));
        var overlap = TechPatternAnalyzer.AnalyzeOverlap(objects);
        occurrences.AddRange(overlap.Sequences.Select(run => Occurrence("Overlap", run.StartIndex, run.StartIndex, run.EndIndex, overlap.Confidence, run.OverlapSequenceScore)));

        string[] names = ["SliderChain", "ComplexSlider", "SliderDirectionChange", "CircleSliderTransition", "RhythmTransition", "SpatialAwkward", "Overlap"];
        Dictionary<int, int> objectToSection = sections.SelectMany((section, sectionIndex) => section.ObjectIndices.Select(objectIndex => (objectIndex, sectionIndex))).ToDictionary(item => item.objectIndex, item => item.sectionIndex);
        var states = Enumerable.Range(0, sections.Count).Select(_ => names.ToDictionary(name => name, _ => new TechState())).ToArray();
        foreach (TechOccurrence occurrence in occurrences)
        {
            int startSection = objectToSection[occurrence.RepresentativeIndex];
            TechState start = states[startSection][occurrence.Name];
            start.Count++;
            start.Confidence += occurrence.Confidence;
            start.Intensity += occurrence.Intensity;
            foreach (int objectIndex in occurrence.ObjectIndices.Distinct())
                states[objectToSection[objectIndex]][occurrence.Name].ObjectIndices.Add(objectIndex);
        }

        var result = new SectionPatternFamilyV2[sections.Count];
        for (int sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            int objectCount = sections[sectionIndex].ObjectCount;
            var metrics = new Dictionary<string, SectionPatternMetricV2>();
            foreach (string name in names)
            {
                TechState state = states[sectionIndex][name];
                metrics[name] = new SectionPatternMetricV2(
                    state.Count,
                    state.ObjectIndices.Count,
                    objectCount == 0 ? 0 : Signal(state.ObjectIndices.Count / (double)objectCount),
                    state.Count == 0 ? 0 : Signal(state.Confidence / state.Count),
                    state.Count == 0 ? null : Signal(state.Intensity / state.Count));
            }
            result[sectionIndex] = BuildFamily(metrics);
        }
        return Array.AsReadOnly(result);
    }

    private static TechOccurrence Occurrence(string name, int representative, int start, int end, double confidence, double intensity) =>
        new(name, representative, Enumerable.Range(start, Math.Max(0, end - start + 1)).ToArray(), confidence, intensity);

    private static Beatmap CreateLocalBeatmap(Beatmap source, IReadOnlyList<int> indices) => new()
    {
        Title = source.Title, Artist = source.Artist, Creator = source.Creator, Version = source.Version,
        AR = source.AR, OD = source.OD, CS = source.CS, HP = source.HP, BPM = source.BPM,
        SliderMultiplier = source.SliderMultiplier, SliderTickRate = source.SliderTickRate,
        TimingPoints = source.TimingPoints,
        HitObjects = indices.Select(index => source.HitObjects[index]).ToList()
    };

    private static StructureDecision DecideStructure(double jump, double stream, double tech)
    {
        var ranked = new[]
        {
            (Kind: GameplayStructureKind.Jump, Score: Score(jump)),
            (Kind: GameplayStructureKind.Stream, Score: Score(stream)),
            (Kind: GameplayStructureKind.Tech, Score: Score(tech))
        }.OrderByDescending(item => item.Score).ToArray();
        bool mixed = ranked[0].Score < 20 || ranked[0].Score - ranked[1].Score <= 5;
        return new StructureDecision(
            mixed ? GameplayStructureKind.ClassicMixed : ranked[0].Kind,
            mixed ? (ranked[0].Score >= 10 ? ranked[0].Kind : null) : (ranked[1].Score >= 15 ? ranked[1].Kind : null));
    }

    private static string BuildSummary(GameplaySectionV2Analysis section)
    {
        string pressure = new[]
        {
            ("Stamina", section.SkillPressure.Stamina.Score), ("Finger Control", section.SkillPressure.FingerControl.Score),
            ("Burst Control", section.SkillPressure.BurstControl.Score), ("Aim Control", section.SkillPressure.AimControl.Score),
            ("Reading Pressure", section.SkillPressure.ReadingPressure.Score), ("Rhythm Complexity", section.SkillPressure.RhythmComplexity.Score)
        }.Where(item => item.Item2.HasValue).OrderByDescending(item => item.Item2).Select(item => item.Item1).FirstOrDefault() ?? "Low pressure";
        string patterns = section.JumpPatterns.Patterns.Concat(section.StreamPatterns.Patterns).Concat(section.TechPatterns.Patterns)
            .Where(item => item.Value.Coverage > 0).OrderByDescending(item => PatternStrength(item.Value)).Take(2).Select(item => item.Key).DefaultIfEmpty("No dominant pattern").Aggregate((left, right) => $"{left} + {right}");
        return $"{section.DominantStructure} / {pressure} / {patterns}";
    }

    private static double SkillDistance(SkillPressureProfile left, SkillPressureProfile right)
    {
        (SkillPressureMetric Left, SkillPressureMetric Right)[] pairs =
        [
            (left.Stamina, right.Stamina), (left.FingerControl, right.FingerControl),
            (left.BurstControl, right.BurstControl), (left.AimControl, right.AimControl),
            (left.ReadingPressure, right.ReadingPressure), (left.RhythmComplexity, right.RhythmComplexity)
        ];
        double[] deltas = pairs.Where(pair => pair.Left.Score.HasValue && pair.Right.Score.HasValue)
            .Select(pair => Math.Abs(pair.Left.Score!.Value - pair.Right.Score!.Value) / 100).ToArray();
        return deltas.Length == 0 ? 0 : Signal(deltas.Average());
    }

    private static SectionPatternMetricV2 Get(SectionPatternFamilyV2 family, string name) =>
        family.Patterns.TryGetValue(name, out SectionPatternMetricV2? metric)
            ? metric
            : new SectionPatternMetricV2(0, 0, 0, 0, null);
    private static double PatternStrength(SectionPatternMetricV2 metric)
    {
        double numerator = .50 * Signal(metric.Coverage) + .30 * Signal(metric.Confidence);
        double denominator = .80;
        if (metric.Intensity.HasValue) { numerator += .20 * Signal(metric.Intensity.Value); denominator = 1; }
        return denominator == 0 ? 0 : Signal(numerator / denominator);
    }
    private static double Value(SkillPressureMetric metric) => metric.Score.HasValue ? metric.Score.Value / 100 : 0;
    private static double Saturate(int count, double scale) => count <= 0 ? 0 : 1 - Math.Exp(-count / scale);
    private static double Signal(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
    private static double Score(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;
    private static void Validate(SectionAnalysisV2Options options)
    {
        if (!double.IsFinite(options.SectionDurationMilliseconds) || options.SectionDurationMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Section duration must be finite and positive.");
        if (options.SpikeNeighborRadius < 1) throw new ArgumentOutOfRangeException(nameof(options));
    }

    internal sealed record DifficultyCalculation(double Score, double Confidence, IReadOnlyDictionary<string, double> Contributors);
    private sealed record StructureDecision(GameplayStructureKind Dominant, GameplayStructureKind? Secondary);
    private sealed record SectionCandidate(GameplaySectionV2Analysis Section, IReadOnlyDictionary<string, double> Contributors);
    private sealed record TechOccurrence(string Name, int RepresentativeIndex, IReadOnlyList<int> ObjectIndices, double Confidence, double Intensity);
    private sealed class TechState { public int Count; public double Confidence; public double Intensity; public HashSet<int> ObjectIndices { get; } = []; }
}
