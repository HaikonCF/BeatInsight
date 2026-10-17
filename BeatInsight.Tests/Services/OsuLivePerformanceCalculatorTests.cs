using System.Collections.Immutable;
using System.IO;
using BeatInsight.Integration.Game;
using BeatInsight.Services;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Rulesets.Objects;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Services;

public sealed class OsuLivePerformanceCalculatorTests
{
    private const string MapOne = "Tower Of Heaven [Extra].osu";
    private const string MapTwo = "Snow Goose [Extra].osu";
    private static readonly Dictionary<string, int> MaxComboCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ITestOutputHelper output;

    public OsuLivePerformanceCalculatorTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void NmSsProducesFinitePositivePerformance()
    {
        var calculator = new OsuLivePerformanceCalculator();
        LivePerformanceCalculationResult result = calculator.Calculate(FullSs(MapOne));

        Assert.Equal(LivePerformanceFailureReason.None, result.FailureReason);
        Assert.NotNull(result.Snapshot);
        Assert.True(result.Snapshot.SsPp > 0);
        Assert.Equal(result.Snapshot.SsPp, result.Snapshot.CurrentPp, 8);
    }

    [Fact]
    public void NmAccuracy95FcIsBelowSsAndCloseToRequestedAccuracy()
    {
        int count = HitObjectCount(PathFor(MapOne));
        (int great, int ok, int meh) = OsuLivePerformanceCalculator.BuildAccuracy95Distribution(count);
        double accuracy = (300.0 * great + 100.0 * ok + 50.0 * meh) / (300.0 * count);
        var calculator = new OsuLivePerformanceCalculator();
        LivePerformanceSnapshot snapshot = Assert.IsType<LivePerformanceSnapshot>(
            calculator.Calculate(FullSs(MapOne)).Snapshot);

        Assert.InRange(Math.Abs(accuracy - .95), 0, 1.0 / (3 * count));
        Assert.InRange(snapshot.Accuracy95FcPp, 0, snapshot.SsPp);
    }

    [Fact]
    public void MissesReduceCurrentPerformance()
    {
        var calculator = new OsuLivePerformanceCalculator();
        LivePerformanceInput ss = FullSs(MapOne);
        double perfect = calculator.Calculate(ss).Snapshot!.CurrentPp;
        double missed = calculator.Calculate(ss with
        {
            Count300 = ss.Count300 - 1,
            CountMiss = 1,
            Accuracy = (ss.Count300 - 1.0) / ss.Count300
        }).Snapshot!.CurrentPp;

        Assert.True(missed < perfect);
    }

    [Fact]
    public void OkAndMehReduceCurrentPerformance()
    {
        var calculator = new OsuLivePerformanceCalculator();
        LivePerformanceInput ss = FullSs(MapOne);
        double perfect = calculator.Calculate(ss).Snapshot!.CurrentPp;
        LivePerformanceSnapshot imperfect = calculator.Calculate(ss with
        {
            Count300 = ss.Count300 - 2,
            Count100 = 1,
            Count50 = 1,
            Accuracy = (300.0 * (ss.Count300 - 2) + 150) / (300.0 * ss.Count300)
        }).Snapshot!;

        Assert.True(imperfect.CurrentPp < perfect);
    }

    [Fact]
    public void ComboBreakReducesCurrentPerformance()
    {
        var calculator = new OsuLivePerformanceCalculator();
        LivePerformanceInput ss = FullSs(MapOne);
        LivePerformanceInput imperfect = ss with
        {
            Count300 = ss.Count300 - 1,
            Count100 = 1,
            Accuracy = (300.0 * (ss.Count300 - 1) + 100) / (300.0 * ss.Count300)
        };
        double fullCombo = calculator.Calculate(imperfect).Snapshot!.CurrentPp;
        double broken = calculator.Calculate(imperfect with { CurrentCombo = Math.Max(1, imperfect.CurrentCombo / 3) })
            .Snapshot!.CurrentPp;

        Assert.True(broken < fullCombo);
    }

    [Theory]
    [InlineData("HD")]
    [InlineData("HR")]
    [InlineData("DT")]
    [InlineData("HD", "HR")]
    [InlineData("HD", "DT")]
    public void SupportedModsProduceOfficialPerformance(params string[] acronyms)
    {
        var calculator = new OsuLivePerformanceCalculator();
        LivePerformanceInput input = FullSs(MapOne) with
        {
            Mods = acronyms.Select(item => new LiveGameplayMod(item)).ToImmutableArray()
        };

        LivePerformanceCalculationResult result = calculator.Calculate(input);

        Assert.Equal(LivePerformanceFailureReason.None, result.FailureReason);
        Assert.True(result.Snapshot!.SsPp > 0);
    }

    [Fact]
    public void LazerProfile_UsesLazerScoringAndProducesFinitePerformance()
    {
        var calculator = new OsuLivePerformanceCalculator();
        LivePerformanceInput input = FullSs(MapOne) with
        {
            Profile = LivePerformanceRulesetProfile.Lazer,
            LegacyScore = null,
            CountSliderTailHit = SliderCount(PathFor(MapOne))
        };

        LivePerformanceCalculationResult result = calculator.Calculate(input);

        Assert.Equal(LivePerformanceFailureReason.None, result.FailureReason);
        Assert.NotNull(result.Snapshot);
        Assert.True(double.IsFinite(result.Snapshot.CurrentPp));
        Assert.True(result.Snapshot.SsPp > 0);
        Assert.Equal(result.Snapshot.SsPp, result.Snapshot.CurrentPp, 8);
    }

    [Fact]
    public void LazerProfile_AccuracyMismatchAgainstMapStructure_FailsClosed()
    {
        var calculator = new OsuLivePerformanceCalculator();
        LivePerformanceInput input = FullSs(MapOne) with
        {
            Profile = LivePerformanceRulesetProfile.Lazer,
            LegacyScore = null,
            CountSliderTailHit = SliderCount(PathFor(MapOne)),
            Accuracy = 0.9
        };

        LivePerformanceCalculationResult result = calculator.Calculate(input);

        Assert.Null(result.Snapshot);
        Assert.Equal(LivePerformanceFailureReason.HitCountMismatch, result.FailureReason);
    }

    [Fact]
    public void StableAndLazerProfiles_HaveDistinctPreparedCacheEntries()
    {
        var calculator = new OsuLivePerformanceCalculator();
        LivePerformanceInput stable = FullSs(MapOne);
        LivePerformanceSnapshot stableResult = calculator.Calculate(stable).Snapshot!;
        LivePerformanceCacheDiagnostics afterStable = calculator.Diagnostics;

        LivePerformanceSnapshot lazerResult = calculator.Calculate(stable with
        {
            Profile = LivePerformanceRulesetProfile.Lazer,
            LegacyScore = null,
            CountSliderTailHit = SliderCount(PathFor(MapOne))
        }).Snapshot!;
        LivePerformanceCacheDiagnostics afterLazer = calculator.Diagnostics;

        Assert.Equal(afterStable.BeatmapParseCount, afterLazer.BeatmapParseCount);
        Assert.Equal(afterStable.DifficultyCalculationCount + 1, afterLazer.DifficultyCalculationCount);
        Assert.NotEqual(stableResult.SsPp, lazerResult.SsPp);
    }

    [Fact]
    public void HdEarlyMixedJudgementsProduceFiniteCurrentPerformance()
    {
        var calculator = new OsuLivePerformanceCalculator();
        LivePerformanceInput input = PartialPerfect(MapTwo, 2, TimeSpan.FromSeconds(3)) with
        {
            Mods = ImmutableArray.Create(new LiveGameplayMod("HD")),
            CurrentCombo = 1,
            MaxComboReached = 2,
            Count300 = 1,
            Count100 = 1,
            Accuracy = 2d / 3d,
            LegacyScore = 1_000
        };

        LivePerformanceCalculationResult result = calculator.Calculate(input);

        Assert.Equal(LivePerformanceFailureReason.None, result.FailureReason);
        Assert.NotNull(result.Snapshot);
        Assert.True(double.IsFinite(result.Snapshot.CurrentPp));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteCurrentPpIsConservativelyNormalized(double value)
    {
        Assert.Equal(0, OsuLivePerformanceCalculator.NormalizeCurrentPp(value));
    }

    [Fact]
    public void PassedObjectsIncreaseWithAbsoluteCurrentTime()
    {
        double[] completionTimes = [100, 250, 250, 900];

        Assert.Equal(0, OsuLivePerformanceCalculator.CountPassedObjects(completionTimes, 99));
        Assert.Equal(1, OsuLivePerformanceCalculator.CountPassedObjects(completionTimes, 100));
        Assert.Equal(3, OsuLivePerformanceCalculator.CountPassedObjects(completionTimes, 250));
        Assert.Equal(4, OsuLivePerformanceCalculator.CountPassedObjects(completionTimes, 1_000));
    }

    [Fact]
    public void TimelineAheadOfJudgementUsesFreshJudgedObjectCount()
    {
        var calculator = new OsuLivePerformanceCalculator();
        LivePerformanceInput input = PartialPerfect(MapOne, 10, TimeSpan.FromDays(1));

        LivePerformanceCalculationResult result = calculator.Calculate(input);

        Assert.Equal(LivePerformanceFailureReason.None, result.FailureReason);
        Assert.Equal(10, result.Snapshot!.PassedObjects);
        Assert.True(calculator.LastSynchronizationDiagnostics!.Delta > 0);
    }

    [Fact]
    public void JudgementAheadOfTimelineUsesFreshJudgedObjectCount()
    {
        var calculator = new OsuLivePerformanceCalculator();
        LivePerformanceInput input = PartialPerfect(MapOne, 10, TimeSpan.Zero);

        LivePerformanceCalculationResult result = calculator.Calculate(input);

        Assert.Equal(LivePerformanceFailureReason.None, result.FailureReason);
        Assert.Equal(10, result.Snapshot!.PassedObjects);
        Assert.True(calculator.LastSynchronizationDiagnostics!.Delta < 0);
    }

    [Theory]
    [InlineData("Slider")]
    [InlineData("Spinner")]
    public void SliderAndSpinnerBoundariesUseTopLevelJudgements(string objectType)
    {
        (int index, double startTime, double endTime) = FirstObjectWithType(MapOne, objectType);
        var calculator = new OsuLivePerformanceCalculator();
        double boundaryTime = objectType == "Slider"
            ? startTime + ((endTime - startTime) / 2)
            : endTime - 1;
        LivePerformanceInput input = PartialPerfect(
            MapOne,
            index + 1,
            TimeSpan.FromMilliseconds(boundaryTime));

        LivePerformanceCalculationResult result = calculator.Calculate(input);

        Assert.Equal(LivePerformanceFailureReason.None, result.FailureReason);
        Assert.Equal(index + 1, result.Snapshot!.PassedObjects);
    }

    [Fact]
    public void RetryCanReturnToFirstFreshJudgementWithoutStalePerformance()
    {
        var calculator = new OsuLivePerformanceCalculator();
        Assert.NotNull(calculator.Calculate(PartialPerfect(MapOne, 20, TimeSpan.FromSeconds(30))).Snapshot);

        LivePerformanceCalculationResult retry = calculator.Calculate(
            PartialPerfect(MapOne, 1, TimeSpan.Zero));

        Assert.Equal(LivePerformanceFailureReason.None, retry.FailureReason);
        Assert.Equal(1, retry.Snapshot!.PassedObjects);
    }

    [Fact]
    public void ImpossibleJudgementTotalStillFailsClosed()
    {
        LivePerformanceInput full = FullSs(MapOne);
        var calculator = new OsuLivePerformanceCalculator();

        LivePerformanceCalculationResult result = calculator.Calculate(full with
        {
            Count300 = full.Count300 + 1,
            CurrentCombo = 1,
            MaxComboReached = 1
        });

        Assert.Null(result.Snapshot);
        Assert.Equal(LivePerformanceFailureReason.HitCountMismatch, result.FailureReason);
    }

    [Fact]
    public void CurrentPerformanceAdvancesWithoutNormalSynchronizationNulls()
    {
        var calculator = new OsuLivePerformanceCalculator();
        for (int judged = 1; judged <= 20; judged++)
        {
            TimeSpan observedTime = judged % 2 == 0 ? TimeSpan.Zero : TimeSpan.FromDays(1);
            LivePerformanceCalculationResult result = calculator.Calculate(
                PartialPerfect(MapOne, judged, observedTime));
            Assert.Equal(LivePerformanceFailureReason.None, result.FailureReason);
            Assert.Equal(judged, result.Snapshot!.PassedObjects);
        }
    }

    [Fact]
    public void StaticNinetyFiveAndSsRemainIndependentOfTimelineSkew()
    {
        var calculator = new OsuLivePerformanceCalculator();
        LivePerformanceInput full = FullSs(MapOne);
        LivePerformanceSnapshot behind = Assert.IsType<LivePerformanceSnapshot>(
            calculator.Calculate(full with { CurrentTime = TimeSpan.Zero }).Snapshot);
        LivePerformanceSnapshot ahead = Assert.IsType<LivePerformanceSnapshot>(
            calculator.Calculate(full with { CurrentTime = TimeSpan.FromDays(1) }).Snapshot);

        Assert.Equal(behind.Accuracy95FcPp, ahead.Accuracy95FcPp, 12);
        Assert.Equal(behind.SsPp, ahead.SsPp, 12);
    }

    [Fact]
    public void MapChangeParsesAndPreparesNewMap()
    {
        var calculator = new OsuLivePerformanceCalculator();
        Assert.NotNull(calculator.Calculate(FullSs(MapOne)).Snapshot);
        LivePerformanceCacheDiagnostics before = calculator.Diagnostics;

        Assert.NotNull(calculator.Calculate(FullSs(MapTwo)).Snapshot);
        LivePerformanceCacheDiagnostics after = calculator.Diagnostics;

        Assert.Equal(before.BeatmapParseCount + 1, after.BeatmapParseCount);
        Assert.Equal(before.DifficultyCalculationCount + 1, after.DifficultyCalculationCount);
    }

    [Fact]
    public void ModChangeReusesParseAndPreparesNewAttributes()
    {
        var calculator = new OsuLivePerformanceCalculator();
        LivePerformanceInput nm = FullSs(MapOne);
        Assert.NotNull(calculator.Calculate(nm).Snapshot);
        LivePerformanceCacheDiagnostics before = calculator.Diagnostics;

        Assert.NotNull(calculator.Calculate(nm with
        {
            Mods = ImmutableArray.Create(new LiveGameplayMod("HD"))
        }).Snapshot);
        LivePerformanceCacheDiagnostics after = calculator.Diagnostics;

        Assert.Equal(before.BeatmapParseCount, after.BeatmapParseCount);
        Assert.Equal(before.DifficultyCalculationCount + 1, after.DifficultyCalculationCount);
    }

    [Fact]
    public void SameMapAndModsReusePreparedCache()
    {
        var calculator = new OsuLivePerformanceCalculator();
        LivePerformanceInput first = FullSs(MapOne);
        Assert.NotNull(calculator.Calculate(first).Snapshot);
        LivePerformanceCacheDiagnostics before = calculator.Diagnostics;

        Assert.NotNull(calculator.Calculate(first with { CurrentCombo = first.CurrentCombo - 1 }).Snapshot);
        LivePerformanceCacheDiagnostics after = calculator.Diagnostics;

        Assert.Equal(before.BeatmapParseCount, after.BeatmapParseCount);
        Assert.Equal(before.DifficultyCalculationCount, after.DifficultyCalculationCount);
        Assert.True(after.CacheHitCount > before.CacheHitCount);
    }

    [Fact]
    public void UnsupportedModFailsClosedWithoutStaleSnapshot()
    {
        var calculator = new OsuLivePerformanceCalculator();
        LivePerformanceInput valid = FullSs(MapOne);
        Assert.NotNull(calculator.Calculate(valid).Snapshot);

        LivePerformanceCalculationResult invalid = calculator.Calculate(valid with
        {
            Mods = ImmutableArray.Create(new LiveGameplayMod("RX"))
        });

        Assert.Null(invalid.Snapshot);
        Assert.Equal(LivePerformanceFailureReason.UnsupportedMods, invalid.FailureReason);
    }

    [Fact]
    public void WarmDynamicCalculationsStayWithinFiveHundredMillisecondTickBudget()
    {
        var calculator = new OsuLivePerformanceCalculator();
        LivePerformanceInput input = FullSs(MapOne);
        Assert.NotNull(calculator.Calculate(input).Snapshot);
        LivePerformanceCacheDiagnostics cold = calculator.Diagnostics;

        Assert.NotNull(calculator.Calculate(input with { CurrentCombo = input.CurrentCombo - 1 }).Snapshot);
        LivePerformanceCacheDiagnostics warm = calculator.Diagnostics;

        output.WriteLine($"PreparationMs={cold.LastPreparationDuration?.TotalMilliseconds:F3}");
        output.WriteLine($"Static95AndSsMs={cold.LastStaticCalculationDuration?.TotalMilliseconds:F3}");
        output.WriteLine($"CurrentPpMs={warm.LastCurrentCalculationDuration?.TotalMilliseconds:F3}");
        output.WriteLine($"FcPpMs={warm.LastFcCalculationDuration?.TotalMilliseconds:F3}");
        Assert.True(warm.LastCurrentCalculationDuration < TimeSpan.FromMilliseconds(500));
        Assert.True(warm.LastFcCalculationDuration < TimeSpan.FromMilliseconds(500));
    }

    private static LivePerformanceInput FullSs(string filename)
    {
        string path = PathFor(filename);
        int count = HitObjectCount(path);
        if (!MaxComboCache.TryGetValue(path, out int maxCombo))
        {
            maxCombo = OsuStarRatingCalculator.CalculateSummary(path).MaxCombo;
            MaxComboCache[path] = maxCombo;
        }
        return new(
            path,
            ImmutableArray<LiveGameplayMod>.Empty,
            maxCombo,
            maxCombo,
            count,
            0,
            0,
            0,
            1,
            TimeSpan.FromDays(1));
    }

    private static LivePerformanceInput PartialPerfect(
        string filename,
        int judgedObjects,
        TimeSpan currentTime)
    {
        LivePerformanceInput full = FullSs(filename);
        return full with
        {
            CurrentCombo = Math.Min(judgedObjects, full.CurrentCombo),
            MaxComboReached = Math.Min(judgedObjects, full.MaxComboReached),
            Count300 = judgedObjects,
            Count100 = 0,
            Count50 = 0,
            CountMiss = 0,
            Accuracy = 1,
            CurrentTime = currentTime,
            LegacyScore = null
        };
    }

    private static (int Index, double StartTime, double EndTime) FirstObjectWithType(
        string filename,
        string objectType)
    {
        LegacyDifficultyCalculatorBeatmapDecoder.Register();
        using var stream = File.OpenRead(PathFor(filename));
        using var reader = new LineBufferedReader(stream);
        Beatmap beatmap = Decoder.GetDecoder<Beatmap>(reader).Decode(reader);
        for (int index = 0; index < beatmap.HitObjects.Count; index++)
        {
            HitObject hitObject = beatmap.HitObjects[index];
            if (hitObject.GetType().Name.Contains(objectType, StringComparison.Ordinal))
                return (index, hitObject.StartTime, hitObject.GetEndTime());
        }

        throw new InvalidOperationException($"No {objectType} in {filename}.");
    }

    private static string PathFor(string filename) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps", filename);

    private static int HitObjectCount(string path)
    {
        bool inObjects = false;
        int count = 0;
        foreach (string line in File.ReadLines(path))
        {
            if (line == "[HitObjects]")
            {
                inObjects = true;
                continue;
            }
            if (inObjects && !string.IsNullOrWhiteSpace(line) && !line.StartsWith("//", StringComparison.Ordinal))
                count++;
        }
        return count;
    }

    private static int SliderCount(string path)
    {
        LegacyDifficultyCalculatorBeatmapDecoder.Register();
        using var stream = File.OpenRead(path);
        using var reader = new LineBufferedReader(stream);
        Beatmap beatmap = Decoder.GetDecoder<Beatmap>(reader).Decode(reader);
        return beatmap.HitObjects.Count(hitObject =>
            hitObject.GetType().Name.Contains("Slider", StringComparison.Ordinal));
    }
}
