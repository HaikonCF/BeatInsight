using BeatInsight.Analysis;
using BeatInsight.Models;
using BeatInsight.Models.Persistence;
using BeatInsight.Parser;
using BeatInsight.Services.Persistence;
using System.Globalization;
using System.IO;
using System.Text;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// Phase 2.8 test-only forensic tool. It never calls or mutates the
/// production decision engine: every threshold used below (.45/.60/.80 and
/// the geometry sub-constants such as 160/110/150/125/45/35) is a read-only
/// mirror of <see cref="TechV2ExperimentalDecisionEngine"/> and
/// <see cref="TechDetectorV2Experimental"/>, kept here only to explain
/// existing decisions and to explore hypothetical, entirely offline guards.
/// No map name is referenced from any runtime file; reference maps are
/// resolved here, in test code, either from the bundled fixture corpus or
/// from BeatInsight's existing local analysis cache
/// (<see cref="MlDatasetSampleRepository.DefaultDatabasePath"/>).
/// </summary>
internal static class TechV2StrongTechForensicAudit
{
    // Mirrors of the production decision thresholds (never modified, never
    // written back). Kept local so this audit does not need production
    // internals to be made public.
    private const double ModerateFamilyThreshold = 0.45;
    private const double StrongFamilyThreshold = 0.60;
    private const double ExceptionalFamilyThreshold = 0.80;
    private const double GeometryIrregularityWeight = 0.65;
    private const double AngleCoherenceWeight = 0.35;

    internal static readonly IReadOnlyList<TechV2StrongTechReferenceDefinition> ReferenceMaps =
    [
        new("Inferno", "NON-TECH", "Inferno (TV Size)", "Rein_Liya", IsFixture: false),
        new("Howling", "NON-TECH", "Howling (TV Size)", "A r M i N", IsFixture: false),
        new("WorldEndsNow", "TECH (strong)", "The World Ends Now", "0pp", IsFixture: false),
        new("GloriousDays", "TECH (very weak)", "GLORIOUS DAYS", "Garden", IsFixture: false),
        new(
            "Exit",
            "TECH",
            "Exit This Earth's Atmosphere",
            "Primordial Nucleosynthesis",
            IsFixture: true),
    ];

    internal static TechV2StrongTechMapForensic Resolve(
        TechV2StrongTechReferenceDefinition definition,
        IReadOnlyList<MlDatasetSample>? localCacheSamples)
    {
        ArgumentNullException.ThrowIfNull(definition);

        string? path = definition.IsFixture
            ? ResolveFixturePath(definition)
            : ResolveLocalLibraryPath(definition, localCacheSamples ?? []);

        if (path is null)
        {
            return TechV2StrongTechMapForensic.NotFound(definition);
        }

        return AnalyzeMap(definition, path);
    }

    internal static IReadOnlyList<MlDatasetSample> LoadLocalCacheSamples(
        string? databasePath = null)
    {
        string resolvedPath = databasePath ?? MlDatasetSampleRepository.DefaultDatabasePath;

        if (!File.Exists(resolvedPath))
        {
            return [];
        }

        return new MlDatasetSampleRepository(resolvedPath).List();
    }

    private static string? ResolveFixturePath(TechV2StrongTechReferenceDefinition definition)
    {
        string fixturesDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Maps");

        if (!Directory.Exists(fixturesDirectory))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(fixturesDirectory, "*.osu", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault(path => Path.GetFileName(path).Contains(
                definition.TitleContains,
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Reuses BeatInsight's existing local analysis cache (populated by the
    /// production <c>MlDatasetBuilder</c> scan of the user's configured osu!
    /// Songs folder) exactly like Phase 2.7. A cheap filename pre-filter
    /// keeps this resolution fast without touching the network or scanning
    /// any path outside that cache.
    /// </summary>
    private static string? ResolveLocalLibraryPath(
        TechV2StrongTechReferenceDefinition definition,
        IReadOnlyList<MlDatasetSample> localCacheSamples)
    {
        IReadOnlyList<string> candidatePaths = localCacheSamples
            .Select(sample => sample.SourceFilePath)
            .Where(path => File.Exists(path))
            .Where(path => Path.GetFileName(path).Contains(
                    definition.TitleContains,
                    StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(path).Contains(
                    definition.DifficultyContains,
                    StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        foreach (string path in candidatePaths)
        {
            Beatmap map;
            try
            {
                map = BeatmapParser.Load(path);
            }
            catch (Exception exception) when (
                exception is IOException or FormatException or InvalidDataException)
            {
                continue;
            }

            if (map.Title.Contains(definition.TitleContains, StringComparison.OrdinalIgnoreCase)
                && map.Version.Contains(
                    definition.DifficultyContains,
                    StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
        }

        return null;
    }

    private static TechV2StrongTechMapForensic AnalyzeMap(
        TechV2StrongTechReferenceDefinition definition,
        string path)
    {
        Beatmap map = BeatmapParser.Load(path);

        IReadOnlyList<TechV2ExperimentalWindowResult> windows =
            TechDetectorV2Experimental.Analyze(
                map.HitObjects,
                GameplayAnalyzer.IsSpinner,
                GameplayAnalyzer.IsSlider,
                GameplayAnalyzer.IsComplexSlider,
                GameplayAnalyzer.GetObjectStartPosition,
                GameplayAnalyzer.GetObjectEndPosition,
                GameplayAnalyzer.GetTechTurnAngle);
        IReadOnlyList<TechV2WindowDecision> baseDecisions =
            TechV2ExperimentalDecisionEngine.Decide(windows);
        // Default guard (applyGeometryGuard: true): reproduces production
        // Tech V2 exactly. This audit never simulates an alternate geometry
        // threshold for SustainedTech.
        IReadOnlyList<TechV2WindowDecision> decisions =
            TechV2ExperimentalDecisionEngine
                .PromoteSustainedTechWithDiagnostics(baseDecisions)
                .Decisions;

        double playableStart = map.HitObjects.First().Time;
        double playableEnd = map.HitObjects.Last().Time;
        TechV2PresenceResult presence = TechV2ExperimentalDecisionEngine.BuildPresence(
            decisions, playableStart, playableEnd);

        IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> byStartIndex =
            windows.ToDictionary(window => window.StartIndex);

        IReadOnlyList<TechV2StrongTechWindowDetail> strongTechWindows = decisions
            .Where(decision => decision.IsStrongTech)
            .OrderBy(decision => decision.StartIndex)
            .Select(decision => DescribeWindow(
                decision,
                byStartIndex[decision.StartIndex],
                map.HitObjects))
            .ToArray();

        return new TechV2StrongTechMapForensic(
            definition,
            path,
            Found: true,
            windows.Count,
            decisions.Count(decision => decision.IsTechSupport),
            decisions.Count(decision => decision.IsSustainedTech),
            presence.PresencePercent,
            presence.Regions,
            playableEnd - playableStart,
            strongTechWindows);
    }

    internal static TechV2StrongTechWindowDetail DescribeWindow(
        TechV2WindowDecision decision,
        TechV2ExperimentalWindowResult source,
        IReadOnlyList<HitObject> objects)
    {
        double[] families =
        [
            decision.GeometryEvidence,
            decision.MovementEvidence,
            decision.SliderEvidence,
            decision.MotifEvidence,
        ];
        string[] familyNames = ["Geometry", "Movement", "Slider", "Motif"];

        (string[] rankedNames, double[] rankedValues) = RankFamilies(familyNames, families);

        TechV2StrongTechRuleTriggered rule = ClassifyRule(families);

        TechV2StrongTechSupportCounts support = ComputeSupportCounts(
            objects,
            decision.StartIndex,
            decision.EndIndex,
            source);

        return new TechV2StrongTechWindowDetail(
            decision.StartTime,
            decision.EndTime,
            rule,
            decision.GeometryEvidence,
            decision.MovementEvidence,
            decision.SliderEvidence,
            decision.MotifEvidence,
            families.Count(value => value >= StrongFamilyThreshold),
            rankedNames[0],
            rankedValues[0],
            rankedNames[1],
            rankedValues[1],
            source.GeometryIrregularity,
            source.RhythmIrregularity,
            source.SliderComplexity,
            source.Awkwardness,
            source.SpacingVariation,
            source.AngleCoherenceWithSupport,
            source.ComplexSliderTransitionTopology.SliderToSliderRatio,
            source.ComplexSliderTransitionTopology.SliderToCircleRatio,
            source.ComplexSliderTransitionTopology.CircleToSliderRatio,
            source.ComplexSliderTransitionTopology.CircleToCircleRatio,
            source.ComplexSliderRunLength,
            source.CoupledTechnicalMotifContinuity,
            support);
    }

    internal static (string[] Names, double[] Values) RankFamilies(
        string[] names,
        double[] values)
    {
        int[] order = Enumerable.Range(0, names.Length)
            .OrderByDescending(index => values[index])
            .ThenBy(index => index)
            .ToArray();

        return (
            order.Select(index => names[index]).ToArray(),
            order.Select(index => values[index]).ToArray());
    }

    internal static TechV2StrongTechRuleTriggered ClassifyRule(double[] families)
    {
        int strongFamilyCount = families.Count(value => value >= StrongFamilyThreshold);
        int moderateFamilyCount = families.Count(value => value >= ModerateFamilyThreshold);
        bool hasExceptionalFamily = families.Any(value => value >= ExceptionalFamilyThreshold);

        bool twoStrong = strongFamilyCount >= 2;
        bool exceptionalPlusModerate = hasExceptionalFamily && moderateFamilyCount >= 2;

        if (twoStrong && exceptionalPlusModerate)
        {
            return TechV2StrongTechRuleTriggered.BOTH;
        }

        return twoStrong
            ? TechV2StrongTechRuleTriggered.TWO_STRONG_FAMILIES
            : TechV2StrongTechRuleTriggered.EXCEPTIONAL_PLUS_MODERATE;
    }

    /// <summary>
    /// Mirrors the private counting performed inside
    /// TechDetectorV2Experimental.ComputeGeometryAwkwardSpacing, purely to
    /// expose how many real transitions/pivots actually carried each family
    /// forward. It reads the same beatmap objects and the same constants,
    /// but writes nothing back and is not called by production code.
    /// </summary>
    internal static TechV2StrongTechSupportCounts ComputeSupportCounts(
        IReadOnlyList<HitObject> objects,
        int start,
        int end,
        TechV2ExperimentalWindowResult source)
    {
        int validTransitions = 0;
        int awkwardCount = 0;
        int spacingVariationCount = 0;

        for (int i = start + 1; i <= end - 1; i++)
        {
            HitObject previous = objects[i - 1];
            HitObject current = objects[i];
            HitObject next = objects[i + 1];

            if (GameplayAnalyzer.IsSpinner(previous)
                || GameplayAnalyzer.IsSpinner(current)
                || GameplayAnalyzer.IsSpinner(next))
            {
                continue;
            }

            double firstInterval = current.Time - previous.Time;
            double secondInterval = next.Time - current.Time;

            if (firstInterval <= 0 || secondInterval <= 0)
            {
                continue;
            }

            double firstDistance = Distance(
                GameplayAnalyzer.GetObjectEndPosition(previous),
                GameplayAnalyzer.GetObjectStartPosition(current));
            double secondDistance = Distance(
                GameplayAnalyzer.GetObjectEndPosition(current),
                GameplayAnalyzer.GetObjectStartPosition(next));

            if (firstDistance > 160 || secondDistance > 160)
            {
                continue;
            }

            validTransitions++;

            double turnAngle = GameplayAnalyzer.GetTechTurnAngle(previous, current, next);
            double averageDistance = (firstDistance + secondDistance) / 2.0;

            bool isAwkward = turnAngle is >= 45 and <= 150 && averageDistance <= 125;
            bool hasSpacingVariation = Math.Abs(firstDistance - secondDistance) >= 35;

            if (isAwkward)
            {
                awkwardCount++;
            }

            if (hasSpacingVariation)
            {
                spacingVariationCount++;
            }
        }

        int sliderCount = 0;
        int complexSliderCount = 0;

        for (int i = start; i <= end; i++)
        {
            if (!GameplayAnalyzer.IsSlider(objects[i]))
            {
                continue;
            }

            sliderCount++;

            if (GameplayAnalyzer.IsComplexSlider(objects[i]))
            {
                complexSliderCount++;
            }
        }

        return new TechV2StrongTechSupportCounts(
            validTransitions,
            source.AngleCoherenceStructuralPivotCount,
            sliderCount,
            complexSliderCount,
            awkwardCount,
            spacingVariationCount);
    }

    internal static double Distance((double X, double Y) first, (double X, double Y) second)
    {
        double x = second.X - first.X;
        double y = second.Y - first.Y;
        return Math.Sqrt(x * x + y * y);
    }

    // ================================================================
    // SECTION 9 - OFFLINE GUARD SIMULATIONS (never applied to the engine)
    // ================================================================

    /// <summary>
    /// Phase 2.8's standout candidate: top-2 evidence pair = Movement+Slider
    /// AND StructuralPivotCount &lt; 3. Exposed by name so Phase 2.9's larger
    /// calibration pass can simulate exactly this guard, offline only,
    /// without duplicating or reinterpreting its predicate.
    /// </summary>
    internal static readonly TechV2StrongTechGuardDefinition MovementSliderLowGeometryGuard = new(
        "G3: low geometry support suppresses Movement+Slider",
        "If the top two families are exactly Movement and Slider (no "
        + "Geometry contribution) and StructuralPivotCount < 3, the "
        + "window is discarded.",
        window => !(SetEquals(window.BestFamily, window.SecondBestFamily, "Movement", "Slider")
            && window.Support.StructuralPivotCount < 3));

    internal static readonly IReadOnlyList<TechV2StrongTechGuardDefinition> CandidateGuards =
    [
        new(
            "G1: minimum evidence support count",
            "A StrongTech window is discarded if ValidTransitionCount < 6 "
            + "(fewer than half the window's 10 possible transitions actually "
            + "carried evidence), regardless of family scores.",
            window => window.Support.ValidTransitionCount >= 6),
        new(
            "G2: exceptional rule requires stronger secondary support",
            "For the EXCEPTIONAL_PLUS_MODERATE branch only, the second-best "
            + "family must reach 0.55 instead of the production 0.45 "
            + "moderate threshold. TWO_STRONG_FAMILIES is untouched.",
            window => StillTechUnderStrongerSecondary(window)),
        MovementSliderLowGeometryGuard,
        new(
            "G4: slider family requires minimum complex-slider support",
            "The Slider family only counts toward strong/moderate thresholds "
            + "if ComplexSliderCount >= 2; otherwise it is treated as 0 "
            + "before re-evaluating the two production rules.",
            window => StillTechWithSliderGatedByComplexSliderSupport(window)),
        new(
            "G5: angle coherence ignored below minimum pivot count",
            "If StructuralPivotCount < 3, AngleCoherenceWithSupport's "
            + "contribution to the Geometry family is dropped before "
            + "re-evaluating the two production rules.",
            window => StillTechWithAngleCoherenceGatedByPivotCount(window)),
    ];

    private static bool SetEquals(string a, string b, string x, string y) =>
        (string.Equals(a, x, StringComparison.Ordinal) && string.Equals(b, y, StringComparison.Ordinal))
        || (string.Equals(a, y, StringComparison.Ordinal) && string.Equals(b, x, StringComparison.Ordinal));

    private static bool StillTechUnderStrongerSecondary(TechV2StrongTechWindowDetail window)
    {
        double[] families = [window.Geometry, window.Movement, window.Slider, window.Motif];
        int strongFamilyCount = families.Count(value => value >= StrongFamilyThreshold);
        bool hasExceptionalFamily = families.Any(value => value >= ExceptionalFamilyThreshold);
        int moderateFamilyCountStrict = families.Count(value => value >= 0.55);

        bool twoStrong = strongFamilyCount >= 2;
        bool exceptionalPlusModerateStrict = hasExceptionalFamily && moderateFamilyCountStrict >= 2;

        return twoStrong || exceptionalPlusModerateStrict;
    }

    private static bool StillTechWithSliderGatedByComplexSliderSupport(
        TechV2StrongTechWindowDetail window)
    {
        double slider = window.Support.ComplexSliderCount >= 2 ? window.Slider : 0;
        double[] families = [window.Geometry, window.Movement, slider, window.Motif];
        return DetermineIsTech(families);
    }

    private static bool StillTechWithAngleCoherenceGatedByPivotCount(
        TechV2StrongTechWindowDetail window)
    {
        double geometry = window.Support.StructuralPivotCount < 3
            ? Math.Clamp(window.GeometryIrregularity * GeometryIrregularityWeight, 0, 1)
            : window.Geometry;
        double[] families = [geometry, window.Movement, window.Slider, window.Motif];
        return DetermineIsTech(families);
    }

    private static bool DetermineIsTech(double[] families)
    {
        int strongFamilyCount = families.Count(value => value >= StrongFamilyThreshold);
        int moderateFamilyCount = families.Count(value => value >= ModerateFamilyThreshold);
        bool hasExceptionalFamily = families.Any(value => value >= ExceptionalFamilyThreshold);

        return strongFamilyCount >= 2 || (hasExceptionalFamily && moderateFamilyCount >= 2);
    }

    internal static TechV2StrongTechGuardOutcome SimulateGuard(
        TechV2StrongTechGuardDefinition guard,
        IReadOnlyList<TechV2StrongTechMapForensic> maps)
    {
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(maps);

        Dictionary<string, bool> keptByMap = [];

        foreach (TechV2StrongTechMapForensic map in maps)
        {
            if (!map.Found)
            {
                continue;
            }

            bool anySurvives = map.StrongTechWindows.Count > 0
                && map.StrongTechWindows.Any(guard.KeepsWindow);
            keptByMap[map.Definition.Label] = anySurvives;
        }

        return new TechV2StrongTechGuardOutcome(guard.Name, guard.Description, keptByMap);
    }

    // ================================================================
    // REPORT FORMATTING
    // ================================================================

    internal static string FormatReport(
        IReadOnlyList<TechV2StrongTechMapForensic> maps,
        IReadOnlyList<TechV2StrongTechGuardOutcome> guardOutcomes)
    {
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(guardOutcomes);

        StringBuilder text = new();
        text.AppendLine("===== TECH V2 PHASE 2.8 STRONGTECH FORENSIC (TEST-ONLY) =====");

        text.AppendLine("MAPS RESOLVED:");
        foreach (TechV2StrongTechMapForensic map in maps)
        {
            text.AppendLine(map.Found
                ? $"  {map.Definition.Label} ({map.Definition.HumanTruth}): {map.Path}"
                : $"  {map.Definition.Label} ({map.Definition.HumanTruth}): NOT FOUND locally");
        }

        foreach (TechV2StrongTechMapForensic map in maps)
        {
            AppendMapSection(text, map);
        }

        AppendFamilyComparisonTable(text, maps);
        AppendGuardSimulations(text, guardOutcomes);

        return text.ToString();
    }

    private static void AppendMapSection(StringBuilder text, TechV2StrongTechMapForensic map)
    {
        text.AppendLine();
        text.AppendLine($"----- {map.Definition.Label} ({map.Definition.HumanTruth}) -----");

        if (!map.Found)
        {
            text.AppendLine("  Not found in the local fixture corpus or analysis cache.");
            return;
        }

        text.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "  TotalWindows={0} | TechSupport={1} | SustainedTech={2} | Presence={3:F2}% "
            + "| PlayableDuration={4:F0}ms | Regions={5}",
            map.TotalWindows,
            map.TechSupportCount,
            map.SustainedTechCount,
            map.PresencePercent,
            map.PlayableDuration,
            map.Regions.Count));

        foreach (TechV2Region region in map.Regions)
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  Region: {0:F0}ms -> {1:F0}ms | Duration={2:F0}ms | Windows={3} | StrongTechWindows={4} "
                + "| DurationShareOfPlayable={5:F2}%",
                region.StartTime,
                region.EndTime,
                region.Duration,
                region.WindowCount,
                region.StrongTechWindowCount,
                map.PlayableDuration <= 0 ? 0 : region.Duration / map.PlayableDuration * 100));
        }

        if (map.StrongTechWindows.Count == 0)
        {
            text.AppendLine("  StrongTech windows: none.");

            if (map.SustainedTechCount > 0 || map.PresencePercent > 0)
            {
                text.AppendLine(
                    "  V2 presence for this map is not produced by StrongTech: it comes from "
                    + $"SustainedTech ({map.SustainedTechCount} windows) and/or bridged TechSupport "
                    + "windows inside the regions listed above (see BuildPresence's hysteresis "
                    + "bridging, unrelated to any single-window duration).");
            }

            return;
        }

        text.AppendLine("  StrongTech windows:");
        foreach (TechV2StrongTechWindowDetail window in map.StrongTechWindows)
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "    {0:F0}ms -> {1:F0}ms | Rule={2} | G/M/S/Mo={3:F3}/{4:F3}/{5:F3}/{6:F3} "
                + "| StrongFamilyCount={7} | Best={8}:{9:F3} Second={10}:{11:F3}",
                window.StartTime,
                window.EndTime,
                window.RuleTriggered,
                window.Geometry,
                window.Movement,
                window.Slider,
                window.Motif,
                window.StrongFamilyCount,
                window.BestFamily,
                window.BestFamilyValue,
                window.SecondBestFamily,
                window.SecondBestFamilyValue));
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "      raw: GeometryIrregularity={0:F3} RhythmIrregularity={1:F3} "
                + "SliderComplexity={2:F3} Awkwardness={3:F3} SpacingVariation={4:F3} "
                + "AngleCoherenceWithSupport={5:F3}",
                window.GeometryIrregularity,
                window.RhythmIrregularity,
                window.SliderComplexity,
                window.Awkwardness,
                window.SpacingVariation,
                window.AngleCoherenceWithSupport));
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "      topology: S>S={0:F3} S>C={1:F3} C>S={2:F3} C>C={3:F3} "
                + "ComplexSliderRunLength={4} CoupledTechnicalMotifContinuity={5:F3}",
                window.SliderToSliderRatio,
                window.SliderToCircleRatio,
                window.CircleToSliderRatio,
                window.CircleToCircleRatio,
                window.ComplexSliderRunLength,
                window.CoupledTechnicalMotifContinuity));
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "      support: ValidTransitions={0} StructuralPivots={1} SliderCount={2} "
                + "ComplexSliderCount={3} AwkwardTransitions={4} SpacingVariationTransitions={5}",
                window.Support.ValidTransitionCount,
                window.Support.StructuralPivotCount,
                window.Support.SliderCount,
                window.Support.ComplexSliderCount,
                window.Support.AwkwardTransitionCount,
                window.Support.SpacingVariationTransitionCount));
        }
    }

    private static void AppendFamilyComparisonTable(
        StringBuilder text,
        IReadOnlyList<TechV2StrongTechMapForensic> maps)
    {
        text.AppendLine();
        text.AppendLine("----- FAMILY / FEATURE COMPARISON (avg / max over StrongTech windows) -----");
        text.AppendLine(
            "Metric | " + string.Join(" | ", maps.Select(map => map.Definition.Label)));

        (string Name, Func<TechV2StrongTechWindowDetail, double> Select)[] metrics =
        [
            ("Geometry", window => window.Geometry),
            ("Movement", window => window.Movement),
            ("Slider", window => window.Slider),
            ("Motif", window => window.Motif),
            ("GeometryIrregularity", window => window.GeometryIrregularity),
            ("RhythmIrregularity", window => window.RhythmIrregularity),
            ("SliderComplexity", window => window.SliderComplexity),
            ("Awkwardness", window => window.Awkwardness),
            ("SpacingVariation", window => window.SpacingVariation),
            ("AngleCoherenceWithSupport", window => window.AngleCoherenceWithSupport),
            ("SliderToSliderRatio", window => window.SliderToSliderRatio),
            ("ComplexSliderRunLength", window => window.ComplexSliderRunLength),
            ("CoupledTechnicalMotifContinuity", window => window.CoupledTechnicalMotifContinuity),
        ];

        foreach ((string name, Func<TechV2StrongTechWindowDetail, double> select) in metrics)
        {
            string row = string.Join(
                " | ",
                maps.Select(map =>
                {
                    if (!map.Found || map.StrongTechWindows.Count == 0)
                    {
                        return "n/a";
                    }

                    double average = map.StrongTechWindows.Average(select);
                    double max = map.StrongTechWindows.Max(select);
                    return string.Format(
                        CultureInfo.InvariantCulture, "{0:F3}/{1:F3}", average, max);
                }));
            text.AppendLine($"{name} | {row}");
        }
    }

    private static void AppendGuardSimulations(
        StringBuilder text,
        IReadOnlyList<TechV2StrongTechGuardOutcome> guardOutcomes)
    {
        text.AppendLine();
        text.AppendLine("----- OFFLINE GUARD SIMULATIONS (never applied to the engine) -----");

        foreach (TechV2StrongTechGuardOutcome outcome in guardOutcomes)
        {
            text.AppendLine($"{outcome.Name}: {outcome.Description}");
            text.AppendLine(
                "  "
                + string.Join(
                    " | ",
                    outcome.KeptByMap.Select(pair =>
                        $"{pair.Key} kept={pair.Value}")));
        }
    }
}

internal enum TechV2StrongTechRuleTriggered
{
    TWO_STRONG_FAMILIES,
    EXCEPTIONAL_PLUS_MODERATE,
    BOTH,
}

internal sealed record TechV2StrongTechReferenceDefinition(
    string Label,
    string HumanTruth,
    string TitleContains,
    string DifficultyContains,
    bool IsFixture);

internal sealed record TechV2StrongTechSupportCounts(
    int ValidTransitionCount,
    int StructuralPivotCount,
    int SliderCount,
    int ComplexSliderCount,
    int AwkwardTransitionCount,
    int SpacingVariationTransitionCount);

internal sealed record TechV2StrongTechWindowDetail(
    double StartTime,
    double EndTime,
    TechV2StrongTechRuleTriggered RuleTriggered,
    double Geometry,
    double Movement,
    double Slider,
    double Motif,
    int StrongFamilyCount,
    string BestFamily,
    double BestFamilyValue,
    string SecondBestFamily,
    double SecondBestFamilyValue,
    double GeometryIrregularity,
    double RhythmIrregularity,
    double SliderComplexity,
    double Awkwardness,
    double SpacingVariation,
    double AngleCoherenceWithSupport,
    double SliderToSliderRatio,
    double SliderToCircleRatio,
    double CircleToSliderRatio,
    double CircleToCircleRatio,
    int ComplexSliderRunLength,
    double CoupledTechnicalMotifContinuity,
    TechV2StrongTechSupportCounts Support);

internal sealed record TechV2StrongTechMapForensic(
    TechV2StrongTechReferenceDefinition Definition,
    string? Path,
    bool Found,
    int TotalWindows,
    int TechSupportCount,
    int SustainedTechCount,
    double PresencePercent,
    IReadOnlyList<TechV2Region> Regions,
    double PlayableDuration,
    IReadOnlyList<TechV2StrongTechWindowDetail> StrongTechWindows)
{
    internal static TechV2StrongTechMapForensic NotFound(
        TechV2StrongTechReferenceDefinition definition) => new(
            definition, null, false, 0, 0, 0, 0, [], 0, []);
}

internal sealed record TechV2StrongTechGuardDefinition(
    string Name,
    string Description,
    Func<TechV2StrongTechWindowDetail, bool> KeepsWindow);

internal sealed record TechV2StrongTechGuardOutcome(
    string Name,
    string Description,
    IReadOnlyDictionary<string, bool> KeptByMap);
