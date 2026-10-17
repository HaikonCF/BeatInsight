using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using BeatInsight.Integration.Game;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Difficulty;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace BeatInsight.Services;

/// <summary>
/// Calculates osu!standard PP with the official ppy.osu.Game engine.
/// Stable scores always include OsuModClassic with slider-head accuracy disabled.
/// </summary>
public sealed class OsuLivePerformanceCalculator
{
    private readonly Dictionary<string, ParsedMap> parsedMaps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<CacheKey, PreparedMap> preparedMaps = [];
    private LivePerformanceInput? lastInput;
    private LivePerformanceCalculationResult? lastResult;
    private int beatmapParseCount;
    private int difficultyCalculationCount;
    private int cacheHitCount;
    private int nonFiniteCurrentNormalizationCount;
    private TimeSpan? lastPreparationDuration;
    private TimeSpan? lastCurrentCalculationDuration;
    private TimeSpan? lastFcCalculationDuration;
    private TimeSpan? lastStaticCalculationDuration;

    public LivePerformanceSynchronizationDiagnostics? LastSynchronizationDiagnostics { get; private set; }

    public LivePerformanceCacheDiagnostics Diagnostics => new(
        beatmapParseCount,
        difficultyCalculationCount,
        cacheHitCount,
        nonFiniteCurrentNormalizationCount,
        lastPreparationDuration,
        lastCurrentCalculationDuration,
        lastFcCalculationDuration,
        lastStaticCalculationDuration);

    public LivePerformanceCalculationResult Calculate(LivePerformanceInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (lastInput == input && lastResult is not null)
        {
            cacheHitCount++;
            return lastResult;
        }

        LivePerformanceCalculationResult result;
        try
        {
            result = calculate(input);
        }
        catch (Exception)
        {
            result = new(null, LivePerformanceFailureReason.CalculationFailed);
        }

        lastInput = input;
        lastResult = result;
        return result;
    }

    private LivePerformanceCalculationResult calculate(LivePerformanceInput input)
    {
        if (string.IsNullOrWhiteSpace(input.BeatmapPath) || !File.Exists(input.BeatmapPath))
            return new(null, LivePerformanceFailureReason.BeatmapUnavailable);
        if (!validStats(input))
            return new(null, LivePerformanceFailureReason.InvalidStats);
        if (!TryCreateMods(input.Mods, input.Profile, out Mod[] mods, out string modKey))
            return new(null, LivePerformanceFailureReason.UnsupportedMods);

        PreparedMap prepared = GetOrPrepare(input.BeatmapPath, mods, modKey, input.Profile);
        int passedObjectsFromTime = CountPassedObjects(
            prepared.ObjectEndTimes,
            input.CurrentTime.TotalMilliseconds);
        int judgedObjects = input.Count300 + input.Count100 + input.Count50 + input.CountMiss;
        LastSynchronizationDiagnostics = CreateSynchronizationDiagnostics(
            prepared,
            input.CurrentTime.TotalMilliseconds,
            passedObjectsFromTime,
            judgedObjects);
        if (judgedObjects == 0)
            return new(null, LivePerformanceFailureReason.NoPassedObjects);
        if (judgedObjects > prepared.TimedAttributes.Count ||
            input.CurrentCombo > prepared.FullAttributes.MaxCombo ||
            input.MaxComboReached > prepared.FullAttributes.MaxCombo)
        {
            return new(null, LivePerformanceFailureReason.HitCountMismatch);
        }

        // CalculateTimed() yields one entry per top-level HitObject. Stable's
        // 300/100/50/miss counters are the fresh set of top-level judgements,
        // while CurrentTime may straddle the same update boundary. Therefore
        // the judgement total is the deterministic progressive-attribute index.
        int passedObjects = judgedObjects;
        var currentAttributes = (OsuDifficultyAttributes)prepared.TimedAttributes[passedObjects - 1].Attributes;
        if (input.Profile == LivePerformanceRulesetProfile.Lazer &&
            (!TryCalculateLazerAccuracy(currentAttributes, passedObjects,
                    input.Count300, input.Count100, input.Count50, input.CountMiss,
                    input.CountLargeTickMiss, input.CountSliderTailHit,
                    out double expectedLazerAccuracy) ||
             Math.Abs(input.Accuracy - expectedLazerAccuracy) > 0.005))
        {
            return new(null, LivePerformanceFailureReason.HitCountMismatch);
        }

        var performance = new OsuPerformanceCalculator();
        long currentStarted = Stopwatch.GetTimestamp();
        double rawCurrentPp = CalculatePp(
            performance,
            prepared.Parsed.Beatmap.BeatmapInfo,
            mods,
            prepared.TimedAttributes[passedObjects - 1].Attributes,
            input.CurrentCombo,
            input.Count300,
            input.Count100,
            input.Count50,
            input.CountMiss,
            input.Profile == LivePerformanceRulesetProfile.StableClassic ? input.LegacyScore : null,
            input.Profile,
            input.Profile == LivePerformanceRulesetProfile.Lazer ? input.Accuracy : null,
            input.CountLargeTickMiss,
            input.CountSliderTailHit);
        double currentPp = NormalizeCurrentPp(rawCurrentPp);
        if (!double.IsFinite(rawCurrentPp))
            nonFiniteCurrentNormalizationCount++;
        lastCurrentCalculationDuration = Stopwatch.GetElapsedTime(currentStarted);

        int remainingObjects = prepared.ObjectEndTimes.Length - passedObjects;
        int remainingCombo = prepared.FullAttributes.MaxCombo -
            prepared.TimedAttributes[passedObjects - 1].Attributes.MaxCombo;
        int projectedCombo = Math.Min(
            prepared.FullAttributes.MaxCombo,
            Math.Max(input.MaxComboReached, input.CurrentCombo + Math.Max(0, remainingCombo)));
        int projectedSliderTailHit = input.CountSliderTailHit;
        if (input.Profile == LivePerformanceRulesetProfile.Lazer)
        {
            projectedSliderTailHit += Math.Max(0,
                prepared.FullAttributes.SliderCount - currentAttributes.SliderCount);
        }
        long fcStarted = Stopwatch.GetTimestamp();
        double fcPp = CalculatePp(
            performance,
            prepared.Parsed.Beatmap.BeatmapInfo,
            mods,
            prepared.FullAttributes,
            projectedCombo,
            input.Count300 + remainingObjects,
            input.Count100,
            input.Count50,
            input.CountMiss,
            null,
            input.Profile,
            null,
            input.CountLargeTickMiss,
            projectedSliderTailHit);
        lastFcCalculationDuration = Stopwatch.GetElapsedTime(fcStarted);

        return new(new LivePerformanceSnapshot(
            currentPp,
            fcPp,
            prepared.Accuracy95FcPp,
            prepared.SsPp,
            passedObjects,
            DateTimeOffset.UtcNow), LivePerformanceFailureReason.None);
    }

    /// <summary>
    /// V3.14-COM-PP-03 : PP SS et 95 % d'une beatmap pour des mods donnés, hors partie (sélection des maps).
    /// Aucune formule propre : réutilise exactement la préparation du gameplay (<see cref="GetOrPrepare"/> :
    /// CalculateTimed, Classic sans précision des têtes de sliders, mêmes conversions de mods). Les valeurs
    /// sont donc celles de <see cref="LivePerformanceSnapshot.SsPp"/> et <see cref="LivePerformanceSnapshot.Accuracy95FcPp"/>
    /// pour la même beatmap et les mêmes mods. Le chemin gameplay (<see cref="Calculate"/>) n'est pas modifié.
    /// Lève <see cref="OperationCanceledException"/> si l'annulation est demandée. Pas thread-safe, comme Calculate.
    /// </summary>
    public LiveStaticPerformanceResult CalculateStatic(
        string beatmapPath,
        ImmutableArray<LiveGameplayMod> liveMods,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(beatmapPath) || !File.Exists(beatmapPath))
            return new(null, LivePerformanceFailureReason.BeatmapUnavailable);
        if (!TryCreateMods(liveMods, LivePerformanceRulesetProfile.StableClassic, out Mod[] mods, out string modKey))
            return new(null, LivePerformanceFailureReason.UnsupportedMods);
        cancellationToken.ThrowIfCancellationRequested();

        PreparedMap prepared;
        try
        {
            prepared = GetOrPrepare(beatmapPath, mods, modKey,
                LivePerformanceRulesetProfile.StableClassic, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new(null, LivePerformanceFailureReason.CalculationFailed);
        }

        // osu!standard uniquement : une beatmap d'un autre mode n'a pas de SS osu!standard.
        if (prepared.Parsed.Beatmap.BeatmapInfo.Ruleset.OnlineID != 0)
            return new(null, LivePerformanceFailureReason.CalculationFailed);
        if (!double.IsFinite(prepared.SsPp) || !double.IsFinite(prepared.Accuracy95FcPp))
            return new(null, LivePerformanceFailureReason.CalculationFailed);
        return new(new LiveStaticPerformance(prepared.SsPp, prepared.Accuracy95FcPp, modKey), LivePerformanceFailureReason.None);
    }

    /// <summary>
    /// V3.14-COM-PP-03 : identifie la convention de calcul de <see cref="CalculateStatic"/> (moteur ppy et
    /// conventions stables) ; une autre version invalide tout résultat mis en cache.
    /// </summary>
    public static string StaticPerformanceVersion { get; } =
        "ppy.osu.Game.Rulesets.Osu/" + typeof(OsuPerformanceCalculator).Assembly.GetName().Version + "/classic-nsha/timed";

    private PreparedMap GetOrPrepare(
        string path,
        Mod[] mods,
        string modKey,
        LivePerformanceRulesetProfile profile,
        CancellationToken cancellationToken = default)
    {
        string fullPath = Path.GetFullPath(path);
        var file = new System.IO.FileInfo(fullPath);
        var key = new CacheKey(fullPath, file.Length, file.LastWriteTimeUtc.Ticks, modKey, profile);
        if (preparedMaps.TryGetValue(key, out PreparedMap? cached))
        {
            cacheHitCount++;
            return cached;
        }

        long started = Stopwatch.GetTimestamp();
        if (!parsedMaps.TryGetValue(fullPath, out ParsedMap? parsed) ||
            parsed.Length != file.Length || parsed.LastWriteTicks != file.LastWriteTimeUtc.Ticks)
        {
            LegacyDifficultyCalculatorBeatmapDecoder.Register();
            using var stream = File.OpenRead(fullPath);
            using var reader = new LineBufferedReader(stream);
            Beatmap beatmap = Decoder.GetDecoder<Beatmap>(reader).Decode(reader);
            parsed = new ParsedMap(beatmap, new InMemoryWorkingBeatmap(beatmap), file.Length, file.LastWriteTimeUtc.Ticks);
            parsedMaps[fullPath] = parsed;
            beatmapParseCount++;
        }

        var ruleset = new OsuRuleset();
        DifficultyCalculator calculator = ruleset.CreateDifficultyCalculator(parsed.WorkingBeatmap);
        List<TimedDifficultyAttributes> timed = calculator.CalculateTimed(mods, cancellationToken);
        difficultyCalculationCount++;
        if (timed.Count == 0)
            throw new InvalidOperationException("The beatmap has no playable osu!standard objects.");

        var full = (OsuDifficultyAttributes)timed[^1].Attributes;
        double[] endTimes = timed.Select(item => item.Time).ToArray();
        HitObjectBoundary[] boundaries = parsed.Beatmap.HitObjects
            .Select(item => new HitObjectBoundary(
                item.GetType().Name,
                item.StartTime,
                item.GetEndTime()))
            .ToArray();
        var performance = new OsuPerformanceCalculator();
        long staticStarted = Stopwatch.GetTimestamp();
        double ss = CalculatePp(performance, parsed.Beatmap.BeatmapInfo, mods, full,
            full.MaxCombo, timed.Count, 0, 0, 0, null, profile, null, 0,
            profile == LivePerformanceRulesetProfile.Lazer ? full.SliderCount : 0);
        (int great, int ok, int meh) = BuildAccuracy95Distribution(timed.Count);
        double accuracy95 = CalculatePp(performance, parsed.Beatmap.BeatmapInfo, mods, full,
            full.MaxCombo, great, ok, meh, 0, null, profile, null, 0,
            profile == LivePerformanceRulesetProfile.Lazer ? full.SliderCount : 0);
        lastStaticCalculationDuration = Stopwatch.GetElapsedTime(staticStarted);

        var prepared = new PreparedMap(parsed, timed, full, endTimes, boundaries, ss, accuracy95);
        preparedMaps[key] = prepared;
        lastPreparationDuration = Stopwatch.GetElapsedTime(started);
        return prepared;
    }

    internal static int CountPassedObjects(IReadOnlyList<double> objectEndTimes, double currentTime)
    {
        int low = 0;
        int high = objectEndTimes.Count;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (objectEndTimes[middle] <= currentTime)
                low = middle + 1;
            else
                high = middle;
        }
        return low;
    }

    private static LivePerformanceSynchronizationDiagnostics CreateSynchronizationDiagnostics(
        PreparedMap prepared,
        double currentTimeMs,
        int passedObjectsFromTime,
        int judgedObjects)
    {
        int delta = passedObjectsFromTime - judgedObjects;
        int boundaryIndex = delta >= 0 ? judgedObjects : passedObjectsFromTime;
        HitObjectBoundary? boundary = boundaryIndex >= 0 && boundaryIndex < prepared.ObjectBoundaries.Length
            ? prepared.ObjectBoundaries[boundaryIndex]
            : null;
        return new(
            currentTimeMs,
            passedObjectsFromTime,
            judgedObjects,
            delta,
            passedObjectsFromTime > 0 ? prepared.ObjectEndTimes[passedObjectsFromTime - 1] : null,
            passedObjectsFromTime < prepared.ObjectEndTimes.Length ? prepared.ObjectEndTimes[passedObjectsFromTime] : null,
            boundary?.Type,
            boundary?.StartTime,
            boundary?.EndTime,
            boundary is null ? null : currentTimeMs - boundary.StartTime,
            boundary is null ? null : boundary.EndTime - currentTimeMs);
    }

    internal static (int Great, int Ok, int Meh) BuildAccuracy95Distribution(int objectCount)
    {
        if (objectCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(objectCount));

        int bestOk = 0;
        int bestMeh = 0;
        double bestError = double.MaxValue;
        for (int meh = 0; meh <= objectCount; meh++)
        {
            double idealOk = (0.05 * 300 * objectCount - 250 * meh) / 200;
            int ok = Math.Clamp((int)Math.Round(idealOk, MidpointRounding.AwayFromZero), 0, objectCount - meh);
            double accuracy = (300.0 * (objectCount - ok - meh) + 100.0 * ok + 50.0 * meh) /
                (300.0 * objectCount);
            double error = Math.Abs(accuracy - 0.95);
            if (error < bestError - 1e-12 ||
                (Math.Abs(error - bestError) <= 1e-12 && meh < bestMeh))
            {
                bestError = error;
                bestOk = ok;
                bestMeh = meh;
            }
        }
        return (objectCount - bestOk - bestMeh, bestOk, bestMeh);
    }

    internal static double NormalizeCurrentPp(double value) =>
        double.IsFinite(value) ? value : 0;

    private static double CalculatePp(
        OsuPerformanceCalculator calculator,
        BeatmapInfo beatmapInfo,
        Mod[] mods,
        DifficultyAttributes attributes,
        int maxCombo,
        int great,
        int ok,
        int meh,
        int miss,
        int? legacyScore,
        LivePerformanceRulesetProfile profile = LivePerformanceRulesetProfile.StableClassic,
        double? explicitAccuracy = null,
        int largeTickMiss = 0,
        int sliderTailHit = 0)
    {
        int total = great + ok + meh + miss;
        double accuracy;
        if (explicitAccuracy.HasValue)
        {
            accuracy = explicitAccuracy.Value;
        }
        else if (profile == LivePerformanceRulesetProfile.Lazer &&
                 TryCalculateLazerAccuracy((OsuDifficultyAttributes)attributes, total,
                     great, ok, meh, miss, largeTickMiss, sliderTailHit, out double lazerAccuracy))
        {
            accuracy = lazerAccuracy;
        }
        else
        {
            accuracy = total == 0 ? 0 :
                (300.0 * great + 100.0 * ok + 50.0 * meh) / (300.0 * total);
        }
        var statistics = new Dictionary<HitResult, int>
        {
            [HitResult.Great] = great,
            [HitResult.Ok] = ok,
            [HitResult.Meh] = meh,
            [HitResult.Miss] = miss
        };
        if (profile == LivePerformanceRulesetProfile.Lazer)
        {
            statistics[HitResult.LargeTickMiss] = largeTickMiss;
            statistics[HitResult.SliderTailHit] = sliderTailHit;
        }
        var score = new ScoreInfo
        {
            BeatmapInfo = beatmapInfo,
            Ruleset = new OsuRuleset().RulesetInfo,
            Mods = mods,
            IsLegacyScore = profile == LivePerformanceRulesetProfile.StableClassic,
            LegacyTotalScore = profile == LivePerformanceRulesetProfile.StableClassic ? legacyScore : null,
            TotalScoreVersion = 1,
            MaxCombo = maxCombo,
            Accuracy = accuracy,
            Statistics = statistics
        };
        return ((OsuPerformanceAttributes)calculator.Calculate(score, attributes)).Total;
    }

    private static bool TryCreateMods(
        ImmutableArray<LiveGameplayMod> liveMods,
        LivePerformanceRulesetProfile profile,
        out Mod[] mods,
        out string key)
    {
        var acronyms = liveMods.IsDefaultOrEmpty
            ? []
            : liveMods.Select(item => item.Acronym.Trim().ToUpperInvariant())
                .Where(item => item.Length > 0 && item != "NM")
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
        var result = new List<Mod>();
        foreach (string acronym in acronyms)
        {
            Mod? mod = acronym switch
            {
                "HD" => new OsuModHidden(),
                "HR" => new OsuModHardRock(),
                "DT" => new OsuModDoubleTime(),
                "NC" => new OsuModNightcore(),
                "EZ" => new OsuModEasy(),
                "HT" => new OsuModHalfTime(),
                "FL" => new OsuModFlashlight(),
                "NF" => new OsuModNoFail(),
                "SD" => new OsuModSuddenDeath(),
                "PF" => new OsuModPerfect(),
                "SO" => new OsuModSpunOut(),
                _ => null
            };
            if (mod is null)
            {
                mods = [];
                key = string.Empty;
                return false;
            }
            result.Add(mod);
        }

        if (profile == LivePerformanceRulesetProfile.StableClassic)
        {
            var classic = new OsuModClassic();
            classic.NoSliderHeadAccuracy.Value = true;
            result.Add(classic);
        }
        mods = result.ToArray();
        key = acronyms.Length == 0 ? "NM" : string.Join('+', acronyms);
        return true;
    }

    private static bool validStats(LivePerformanceInput input)
    {
        if (input.CurrentTime < TimeSpan.Zero ||
            input.CurrentCombo < 0 || input.MaxComboReached < 0 ||
            input.Count300 < 0 || input.Count100 < 0 || input.Count50 < 0 || input.CountMiss < 0 ||
            input.CountLargeTickMiss < 0 || input.CountSliderTailHit < 0 ||
            !double.IsFinite(input.Accuracy) || input.Accuracy is < 0 or > 1 ||
            input.LegacyScore is < 0)
        {
            return false;
        }

        long judgedLong = (long)input.Count300 + input.Count100 + input.Count50 + input.CountMiss;
        if (judgedLong > int.MaxValue)
            return false;
        int judged = (int)judgedLong;
        if (judged == 0)
            return true;
        if (input.Profile == LivePerformanceRulesetProfile.Lazer)
            return true;
        double derivedAccuracy =
            (300.0 * input.Count300 + 100.0 * input.Count100 + 50.0 * input.Count50) /
            (300.0 * judged);
        return Math.Abs(input.Accuracy - derivedAccuracy) <= 0.005;
    }

    private static bool TryCalculateLazerAccuracy(
        OsuDifficultyAttributes attributes,
        int objectCount,
        int great,
        int ok,
        int meh,
        int miss,
        int largeTickMiss,
        int sliderTailHit,
        out double accuracy)
    {
        accuracy = 0;
        int largeTickCount = attributes.MaxCombo - objectCount - attributes.SliderCount;
        if (objectCount < 0 || largeTickCount < 0 || largeTickMiss < 0 ||
            largeTickMiss > largeTickCount || sliderTailHit < 0 ||
            sliderTailHit > attributes.SliderCount || great + ok + meh + miss != objectCount)
        {
            return false;
        }

        double achieved = 6.0 * great + 2.0 * ok + meh +
            3.0 * sliderTailHit + 0.6 * (largeTickCount - largeTickMiss);
        double maximum = 6.0 * objectCount + 3.0 * attributes.SliderCount +
            0.6 * largeTickCount;
        accuracy = maximum == 0 ? 1 : achieved / maximum;
        return double.IsFinite(accuracy) && accuracy is >= 0 and <= 1;
    }

    private sealed record CacheKey(
        string Path,
        long Length,
        long LastWriteTicks,
        string Mods,
        LivePerformanceRulesetProfile Profile);
    private sealed record ParsedMap(Beatmap Beatmap, InMemoryWorkingBeatmap WorkingBeatmap, long Length, long LastWriteTicks);
    private sealed record PreparedMap(
        ParsedMap Parsed,
        List<TimedDifficultyAttributes> TimedAttributes,
        OsuDifficultyAttributes FullAttributes,
        double[] ObjectEndTimes,
        HitObjectBoundary[] ObjectBoundaries,
        double SsPp,
        double Accuracy95FcPp);

    private sealed record HitObjectBoundary(string Type, double StartTime, double EndTime);

    private sealed class InMemoryWorkingBeatmap : WorkingBeatmap
    {
        private readonly IBeatmap beatmap;
        public InMemoryWorkingBeatmap(IBeatmap beatmap) : base(beatmap.BeatmapInfo, null!) => this.beatmap = beatmap;
        protected override IBeatmap GetBeatmap() => beatmap;
        public override osu.Framework.Graphics.Textures.Texture GetBackground() => null!;
        protected override osu.Framework.Audio.Track.Track GetBeatmapTrack() => null!;
        protected override osu.Game.Skinning.ISkin GetSkin() => null!;
        public override Stream GetStream(string storagePath) => Stream.Null;
    }
}

/// <summary>V3.14-COM-PP-03 : PP sans partie en cours (SS et 95 %), mêmes conventions que le gameplay.</summary>
public sealed record LiveStaticPerformance(double SsPp, double Accuracy95FcPp, string ModsKey);

public sealed record LiveStaticPerformanceResult(LiveStaticPerformance? Value, LivePerformanceFailureReason FailureReason);
