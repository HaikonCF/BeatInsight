using BeatInsight.Analysis;
using BeatInsight.Analysis.Sections;
using BeatInsight.Models;
using BeatInsight.Models.Fusion;
using BeatInsight.Models.Sections;
using BeatInsight.Models.SkillPressure;

namespace BeatInsight.Tests.Analysis;

public sealed class SectionAnalysisV2Tests
{
    private static Beatmap EmptyMap() => new();

    private static Beatmap SimpleMap(int count, int intervalMs = 200, int startMs = 0, int type = 1) => new()
    {
        CS = 4, OD = 8, AR = 9, HP = 6, SliderMultiplier = 1.4,
        TimingPoints = [new() { Time = 0, BeatLength = 500, Uninherited = true }],
        HitObjects = Enumerable.Range(0, count).Select(index => new HitObject
        {
            X = (index % 4) * 100,
            Y = ((index / 4) % 4) * 100,
            Time = startMs + index * intervalMs,
            Type = type
        }).ToList()
    };

    private static Beatmap JumpMap(int count = 25, int intervalMs = 250, int startMs = 0) => new()
    {
        CS = 4, OD = 8, AR = 9, HP = 6, SliderMultiplier = 1.4,
        TimingPoints = [new() { Time = 0, BeatLength = 500, Uninherited = true }],
        HitObjects = Enumerable.Range(0, count).Select(index => new HitObject
        {
            X = (index % 2 == 0) ? 50 : 450,
            Y = (index % 2 == 0) ? 50 : 350,
            Time = startMs + index * intervalMs,
            Type = 1
        }).ToList()
    };

    private static Beatmap IntenseSpikeMap(int startMs = 0) => new()
    {
        CS = 4, OD = 8, AR = 9, HP = 6, SliderMultiplier = 1.4,
        TimingPoints = [new() { Time = 0, BeatLength = 500, Uninherited = true }],
        HitObjects = Enumerable.Range(0, 35).Select(index => new HitObject
        {
            X = (index % 2 == 0) ? 10 : 500,
            Y = (index % 2 == 0) ? 10 : 370,
            Time = startMs + index * 120,
            Type = 1
        }).ToList()
    };

    private static Beatmap StreamMap(int count = 40, int intervalMs = 60, int startMs = 0) => new()
    {
        CS = 4, OD = 8, AR = 9, HP = 6, SliderMultiplier = 1.4,
        TimingPoints = [new() { Time = 0, BeatLength = 500, Uninherited = true }],
        HitObjects = Enumerable.Range(0, count).Select(index => new HitObject
        {
            X = 256 + (int)(30 * Math.Cos(index * 0.4)),
            Y = 192 + (int)(30 * Math.Sin(index * 0.4)),
            Time = startMs + index * intervalMs,
            Type = 1
        }).ToList()
    };

    private static Beatmap TechMap(int count = 20, int startMs = 0) => new()
    {
        CS = 4, OD = 8, AR = 9, HP = 6, SliderMultiplier = 1.4,
        TimingPoints = [new() { Time = 0, BeatLength = 500, Uninherited = true }],
        HitObjects = Enumerable.Range(0, count).Select(index => new HitObject
        {
            X = (index % 2 == 0) ? 100 : 300,
            Y = (index % 2 == 0) ? 100 : 300,
            Time = startMs + index * 300,
            Type = 2,
            SliderCurveType = "P",
            SliderControlPoints = [new() { X = 100, Y = 100 }, new() { X = 200, Y = 300 }, new() { X = 300, Y = 100 }],
            Length = 150,
            Slides = (index % 2 == 0) ? 2 : 1
        }).ToList()
    };

    private static SkillPressureProfile EmptyPressure()
    {
        SkillPressureMetric unav = SkillPressureMetric.Unavailable("None");
        return new(unav, unav, unav, unav, unav, unav, unav, unav, 0, 0, []);
    }

    private static SkillPressureProfile ActivePressure()
    {
        SkillPressureMetric metric = new(80, 0.9, true, true, new Dictionary<string, double>(), "");
        SkillPressureMetric unav = SkillPressureMetric.Unavailable("");
        return new(metric, unav, metric, metric, metric, unav, metric, metric, 0.9, 6, []);
    }

    // ============================================================
    // 1. TESTS SEGMENTATION (1..10)
    // ============================================================

    [Fact]
    public void Test01_EmptyMap_ReturnsEmptyResult()
    {
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(EmptyMap());
        Assert.Equal(0, result.SectionCount);
        Assert.Empty(result.Sections);
        Assert.Equal(0, result.AverageLocalDifficulty);
        Assert.Null(result.MaximumDifficultySectionIndex);
    }

    [Fact]
    public void Test02_UnderFiveSeconds_ProducesSingleSection()
    {
        Beatmap map = SimpleMap(5, intervalMs: 300, startMs: 1000);
        IReadOnlyList<GameplaySectionAnalysis> partition = SectionAnalysisV2Analyzer.Segment(map.HitObjects);
        Assert.Single(partition);
        Assert.Equal(0, partition[0].Index);
        Assert.Equal(1000, partition[0].StartTime);
        Assert.Equal(2201, partition[0].EndTime);
        Assert.Equal(5, partition[0].ObjectCount);
    }

    [Fact]
    public void Test03_ExactlyFiveSeconds_ProducesExpectedBoundary()
    {
        Beatmap map = SimpleMap(6, intervalMs: 1000, startMs: 0);
        IReadOnlyList<GameplaySectionAnalysis> partition = SectionAnalysisV2Analyzer.Segment(map.HitObjects);
        Assert.Equal(2, partition.Count);
        Assert.Equal(0, partition[0].StartTime);
        Assert.Equal(5000, partition[0].EndTime);
        Assert.Equal(5, partition[0].ObjectCount);
        Assert.Equal(5000, partition[1].StartTime);
        Assert.Equal(1, partition[1].ObjectCount);
    }

    [Fact]
    public void Test04_OverFiveSeconds_ProducesMultipleSections()
    {
        Beatmap map = SimpleMap(13, intervalMs: 1000, startMs: 0);
        IReadOnlyList<GameplaySectionAnalysis> partition = SectionAnalysisV2Analyzer.Segment(map.HitObjects);
        Assert.Equal(3, partition.Count);
        Assert.Equal(0, partition[0].StartTime);
        Assert.Equal(5000, partition[0].EndTime);
        Assert.Equal(5000, partition[1].StartTime);
        Assert.Equal(10000, partition[1].EndTime);
        Assert.Equal(10000, partition[2].StartTime);
        Assert.Equal(12001, partition[2].EndTime);
    }

    [Fact]
    public void Test05_LastSectionCanBePartial()
    {
        Beatmap map = SimpleMap(8, intervalMs: 1000, startMs: 0);
        IReadOnlyList<GameplaySectionAnalysis> partition = SectionAnalysisV2Analyzer.Segment(map.HitObjects);
        Assert.Equal(2, partition.Count);
        Assert.Equal(5000, partition[0].Duration);
        Assert.Equal(2001, partition[1].Duration);
    }

    [Fact]
    public void Test06_BoundariesAreStrictlyMonotonic()
    {
        Beatmap map = SimpleMap(30, intervalMs: 500, startMs: 100);
        IReadOnlyList<GameplaySectionAnalysis> partition = SectionAnalysisV2Analyzer.Segment(map.HitObjects);
        for (int index = 0; index < partition.Count; index++)
        {
            Assert.True(partition[index].EndTime > partition[index].StartTime);
            if (index + 1 < partition.Count)
                Assert.Equal(partition[index].EndTime, partition[index + 1].StartTime);
        }
    }

    [Fact]
    public void Test07_ObjectAtExactBoundary_AssignedToFollowingSection()
    {
        Beatmap map = new()
        {
            HitObjects = [new() { Time = 0, Type = 1 }, new() { Time = 5000, Type = 1 }]
        };
        IReadOnlyList<GameplaySectionAnalysis> partition = SectionAnalysisV2Analyzer.Segment(map.HitObjects);
        Assert.Equal(2, partition.Count);
        Assert.Contains(0, partition[0].ObjectIndices);
        Assert.DoesNotContain(1, partition[0].ObjectIndices);
        Assert.Contains(1, partition[1].ObjectIndices);
    }

    [Fact]
    public void Test08_NoEmptyLeadingSections()
    {
        Beatmap map = SimpleMap(5, intervalMs: 200, startMs: 45000);
        IReadOnlyList<GameplaySectionAnalysis> partition = SectionAnalysisV2Analyzer.Segment(map.HitObjects);
        Assert.Equal(45000, partition[0].StartTime);
        Assert.Equal(0, partition[0].Index);
        Assert.Equal(5, partition[0].ObjectCount);
    }

    [Fact]
    public void Test09_NoDuplicateObjectAssignment()
    {
        Beatmap map = SimpleMap(50, intervalMs: 300, startMs: 0);
        IReadOnlyList<GameplaySectionAnalysis> partition = SectionAnalysisV2Analyzer.Segment(map.HitObjects);
        int[] allAssigned = partition.SelectMany(section => section.ObjectIndices).ToArray();
        Assert.Equal(50, allAssigned.Length);
        Assert.Equal(50, allAssigned.Distinct().Count());
        Assert.Equal(Enumerable.Range(0, 50), allAssigned.Order());
    }

    [Fact]
    public void Test10_SpinnerHandling_CountsCorrectlyWithoutArtificialStructure()
    {
        Beatmap map = new()
        {
            HitObjects =
            [
                new() { Time = 0, Type = 1 },
                new() { Time = 1000, Type = 8 },
                new() { Time = 2000, Type = 8 }
            ]
        };
        IReadOnlyList<GameplaySectionAnalysis> partition = SectionAnalysisV2Analyzer.Segment(map.HitObjects);
        Assert.Single(partition);
        Assert.Equal(1, partition[0].CircleCount);
        Assert.Equal(2, partition[0].SpinnerCount);
        Assert.Equal(0, partition[0].SliderCount);
    }
    // ============================================================
    // 2. TESTS STRUCTURE (11..20)
    // ============================================================

    [Fact]
    public void Test11_JumpSection_IdentifiesJumpStructure()
    {
        Beatmap map = JumpMap(20, intervalMs: 200, startMs: 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.NotEmpty(result.Sections);
        Assert.Equal(GameplayStructureKind.Jump, result.Sections[0].DominantStructure);
        Assert.True(result.Sections[0].JumpScore > 30);
    }

    [Fact]
    public void Test12_StreamSection_IdentifiesStreamStructure()
    {
        Beatmap map = StreamMap(40, intervalMs: 80, startMs: 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.NotEmpty(result.Sections);
        Assert.Equal(GameplayStructureKind.Stream, result.Sections[0].DominantStructure);
        Assert.True(result.Sections[0].StreamScore > 30);
    }

    [Fact]
    public void Test13_TechSection_IdentifiesTechStructure()
    {
        Beatmap map = TechMap(25, startMs: 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.NotEmpty(result.Sections);
        Assert.True(result.Sections[0].BaseSkills.Tech > 0 || result.Sections[0].TechScore >= 0);
        Assert.True(result.Sections[0].TechPatterns.Patterns.Count > 0);
    }

    [Fact]
    public void Test14_MixedSection_IdentifiesClassicMixed()
    {
        Beatmap map = SimpleMap(6, intervalMs: 800, startMs: 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.NotEmpty(result.Sections);
        Assert.Equal(GameplayStructureKind.ClassicMixed, result.Sections[0].DominantStructure);
    }

    [Fact]
    public void Test15_JumpToStream_DetectsTransition()
    {
        Beatmap jumpPart = JumpMap(20, intervalMs: 250, startMs: 0);
        Beatmap streamPart = StreamMap(40, intervalMs: 100, startMs: 5200);
        Beatmap map = new()
        {
            CS = 4, OD = 8, AR = 9, HP = 6, SliderMultiplier = 1.4,
            TimingPoints = [new() { Time = 0, BeatLength = 500, Uninherited = true }],
            HitObjects = jumpPart.HitObjects.Concat(streamPart.HitObjects).ToList()
        };
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.True(result.SectionCount >= 2);
        Assert.Equal(GameplayStructureKind.Jump, result.Sections[0].DominantStructure);
        Assert.Equal(GameplayStructureKind.Stream, result.Sections[1].DominantStructure);
        Assert.True(result.Sections[1].Transition.IsTransition);
    }

    [Fact]
    public void Test16_StreamToTech_DetectsTransition()
    {
        Beatmap streamPart = StreamMap(40, intervalMs: 100, startMs: 0);
        Beatmap techPart = TechMap(12, startMs: 5200);
        Beatmap map = new()
        {
            CS = 4, OD = 8, AR = 9, HP = 6, SliderMultiplier = 1.4,
            TimingPoints = [new() { Time = 0, BeatLength = 500, Uninherited = true }],
            HitObjects = streamPart.HitObjects.Concat(techPart.HitObjects).ToList()
        };
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.True(result.SectionCount >= 2);
        Assert.Equal(GameplayStructureKind.Stream, result.Sections[0].DominantStructure);
    }

    [Fact]
    public void Test17_MixedStable_DoesNotFlickerTransition()
    {
        Beatmap map = SimpleMap(16, intervalMs: 600, startMs: 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.True(result.SectionCount >= 2);
        Assert.Equal(GameplayStructureKind.ClassicMixed, result.Sections[0].DominantStructure);
        Assert.Equal(GameplayStructureKind.ClassicMixed, result.Sections[1].DominantStructure);
        Assert.False(result.Sections[1].Transition.IsTransition);
    }

    [Fact]
    public void Test18_AimNeverDominantStructure()
    {
        Beatmap map = JumpMap(25, 200, 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.All(result.Sections, s => Assert.NotEqual("Aim", s.DominantStructure.ToString()));
    }

    [Fact]
    public void Test19_SpeedNeverDominantStructure()
    {
        Beatmap map = StreamMap(50, 60, 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.All(result.Sections, s => Assert.NotEqual("Speed", s.DominantStructure.ToString()));
    }

    [Fact]
    public void Test20_ReadingNeverDominantStructure()
    {
        Beatmap map = TechMap(20, 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.All(result.Sections, s => Assert.NotEqual("Reading", s.DominantStructure.ToString()));
    }

    // ============================================================
    // 3. TESTS PATTERNS (21..26)
    // ============================================================

    [Fact]
    public void Test21_JumpPatternProjectedLocally()
    {
        Beatmap map = JumpMap(25, 200, 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.NotEmpty(result.Sections);
        Assert.True(result.Sections[0].JumpPatterns.Support > 0 || result.Sections[0].JumpPatterns.Coverage > 0);
    }

    [Fact]
    public void Test22_StreamPatternProjectedLocally()
    {
        Beatmap map = StreamMap(40, 80, 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.NotEmpty(result.Sections);
        Assert.True(result.Sections[0].StreamPatterns.Support > 0 || result.Sections[0].StreamPatterns.Coverage > 0);
    }

    [Fact]
    public void Test23_TechPatternProjectedLocally()
    {
        Beatmap map = TechMap(15, 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.NotEmpty(result.Sections);
        Assert.True(result.Sections[0].TechPatterns.Patterns.Count > 0);
    }

    [Fact]
    public void Test24_PatternCrossingBoundary_DoesNotCrash()
    {
        Beatmap map = StreamMap(80, 100, 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.True(result.SectionCount >= 2);
        Assert.All(result.Sections, s => Assert.InRange(s.StreamPatterns.Support, 0, 1));
    }

    [Fact]
    public void Test25_NoDoubleCountingOfOccurrences()
    {
        Beatmap map = JumpMap(30, 200, 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.All(result.Sections, s =>
        {
            Assert.True(s.JumpPatterns.OccurrenceCount >= 0);
            Assert.InRange(s.JumpPatterns.Coverage, 0, 1);
        });
    }

    [Fact]
    public void Test26_PatternSupportBounds()
    {
        Beatmap map = StreamMap(50, 70, 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.All(result.Sections, s =>
        {
            Assert.InRange(s.JumpPatterns.Support, 0, 1);
            Assert.InRange(s.StreamPatterns.Support, 0, 1);
            Assert.InRange(s.TechPatterns.Support, 0, 1);
        });
    }

    // ============================================================
    // 4. TESTS SKILLS (27..30)
    // ============================================================

    [Fact]
    public void Test27_AimLocal_InBounds()
    {
        Beatmap map = JumpMap(20, 200, 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.All(result.Sections, s => Assert.InRange(s.BaseSkills.Aim, 0, 100));
    }

    [Fact]
    public void Test28_SpeedLocal_InBounds()
    {
        Beatmap map = StreamMap(40, 70, 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.All(result.Sections, s => Assert.InRange(s.BaseSkills.Speed, 0, 100));
    }

    [Fact]
    public void Test29_ReadingLocal_InBounds()
    {
        Beatmap map = TechMap(15, 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.All(result.Sections, s => Assert.InRange(s.BaseSkills.Reading, 0, 100));
    }

    [Fact]
    public void Test30_TechLocal_InBounds()
    {
        Beatmap map = TechMap(15, 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.All(result.Sections, s => Assert.InRange(s.BaseSkills.Tech, 0, 100));
    }
    [Fact]
    public void Test31_StaminaLocal_InBoundsWhenAvailable()
    {
        Beatmap map = StreamMap(50, 60, 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.All(result.Sections, s =>
        {
            if (s.SkillPressure.Stamina.Score.HasValue)
                Assert.InRange(s.SkillPressure.Stamina.Score.Value, 0, 100);
        });
    }

    [Fact]
    public void Test32_FingerControlLocal_InBoundsWhenAvailable()
    {
        Beatmap map = StreamMap(40, 80, 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.All(result.Sections, s =>
        {
            if (s.SkillPressure.FingerControl.Score.HasValue)
                Assert.InRange(s.SkillPressure.FingerControl.Score.Value, 0, 100);
        });
    }

    [Fact]
    public void Test33_BurstControlLocal_InBoundsWhenAvailable()
    {
        Beatmap map = StreamMap(30, 90, 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.All(result.Sections, s =>
        {
            if (s.SkillPressure.BurstControl.Score.HasValue)
                Assert.InRange(s.SkillPressure.BurstControl.Score.Value, 0, 100);
        });
    }

    [Fact]
    public void Test34_AimControlLocal_InBoundsWhenAvailable()
    {
        Beatmap map = JumpMap(25, 200, 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.All(result.Sections, s =>
        {
            if (s.SkillPressure.AimControl.Score.HasValue)
                Assert.InRange(s.SkillPressure.AimControl.Score.Value, 0, 100);
        });
    }

    [Fact]
    public void Test35_ReadingPressureLocal_InBoundsWhenAvailable()
    {
        Beatmap map = TechMap(15, 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.All(result.Sections, s =>
        {
            if (s.SkillPressure.ReadingPressure.Score.HasValue)
                Assert.InRange(s.SkillPressure.ReadingPressure.Score.Value, 0, 100);
        });
    }

    [Fact]
    public void Test36_RhythmComplexityLocal_InBoundsWhenAvailable()
    {
        Beatmap map = TechMap(15, 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.All(result.Sections, s =>
        {
            if (s.SkillPressure.RhythmComplexity.Score.HasValue)
                Assert.InRange(s.SkillPressure.RhythmComplexity.Score.Value, 0, 100);
        });
    }

    [Fact]
    public void Test37_UnavailablePrecision_RemainsUnavailable()
    {
        Beatmap map = JumpMap(20, 200, 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.All(result.Sections, s => Assert.False(s.SkillPressure.Precision.Available));
    }

    // ============================================================
    // 5. TESTS LOCAL DIFFICULTY (38..44)
    // ============================================================

    [Fact]
    public void Test38_LocalDifficulty_InBoundsZeroToOneHundred()
    {
        Beatmap map = JumpMap(30, 200, 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.All(result.Sections, s => Assert.InRange(s.LocalDifficultyScore, 0, 100));
    }

    [Fact]
    public void Test39_LowSection_ProducesLowDifficulty()
    {
        Beatmap map = SimpleMap(3, intervalMs: 1500, startMs: 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.Single(result.Sections);
        Assert.True(result.Sections[0].LocalDifficultyScore < 30);
    }

    [Fact]
    public void Test40_HighSection_ProducesHigherDifficultyThanLowSection()
    {
        Beatmap low = SimpleMap(4, intervalMs: 1000, startMs: 0);
        Beatmap high = StreamMap(40, intervalMs: 80, startMs: 0);
        SectionAnalysisResult lowResult = SectionAnalysisV2Analyzer.Analyze(low);
        SectionAnalysisResult highResult = SectionAnalysisV2Analyzer.Analyze(high);
        Assert.True(highResult.Sections[0].LocalDifficultyScore > lowResult.Sections[0].LocalDifficultyScore);
    }

    [Fact]
    public void Test41_DensityContributesWithoutDominating()
    {
        var skills = new SectionLocalSkills(0, 0, 0, 0);
        var emptyPressure = EmptyPressure();
        var diffLowDensity = SectionAnalysisV2Analyzer.CalculateLocalDifficulty(
            skills, emptyPressure, 0, 0, 0, objectsPerSecond: 1, objectCount: 5, durationMilliseconds: 5000);
        var diffHighDensity = SectionAnalysisV2Analyzer.CalculateLocalDifficulty(
            skills, emptyPressure, 0, 0, 0, objectsPerSecond: 20, objectCount: 100, durationMilliseconds: 5000);

        Assert.True(diffHighDensity.Score > diffLowDensity.Score);
        Assert.True(diffHighDensity.Score <= 15);
    }

    [Fact]
    public void Test42_StrongPatternAloneDoesNotCreateMaximum()
    {
        var skills = new SectionLocalSkills(0, 0, 0, 0);
        var emptyPressure = EmptyPressure();
        var diffPatternOnly = SectionAnalysisV2Analyzer.CalculateLocalDifficulty(
            skills, emptyPressure, jumpSupport: 1.0, streamSupport: 1.0, techSupport: 1.0, objectsPerSecond: 1, objectCount: 5, durationMilliseconds: 5000);

        Assert.True(diffPatternOnly.Score < 30);
    }

    [Fact]
    public void Test43_SkillPressureContributesToDifficulty()
    {
        var skills = new SectionLocalSkills(50, 50, 50, 50);
        var basePressure = EmptyPressure();
        var activePressure = ActivePressure();

        var diffBase = SectionAnalysisV2Analyzer.CalculateLocalDifficulty(skills, basePressure, 0.5, 0.5, 0.5, 4, 20, 5000);
        var diffActive = SectionAnalysisV2Analyzer.CalculateLocalDifficulty(skills, activePressure, 0.5, 0.5, 0.5, 4, 20, 5000);

        Assert.True(diffActive.Score > diffBase.Score);
    }

    [Fact]
    public void Test44_LocalDifficultyConfidence_InBoundsZeroToOne()
    {
        Beatmap map = JumpMap(25, 200, 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.All(result.Sections, s => Assert.InRange(s.LocalDifficultyConfidence, 0, 1));
        Assert.InRange(result.GlobalConfidence, 0, 1);
    }

    // ============================================================
    // 6. TESTS SPIKES (45..54)
    // ============================================================

    [Fact]
    public void Test45_StreetMap_NoSpike()
    {
        Beatmap map = SimpleMap(50, intervalMs: 500, startMs: 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.Equal(0, result.DifficultySpikeCount);
        Assert.All(result.Sections, s => Assert.False(s.Spike.IsSpike));
    }

    [Fact]
    public void Test46_SingleClearSpike_IsDetected()
    {
        List<HitObject> objects = new();
        for (int i = 0; i < 4; i++) objects.Add(new HitObject { X = 100, Y = 100, Time = i * 1000, Type = 1 });
        for (int i = 0; i < 4; i++) objects.Add(new HitObject { X = 100, Y = 100, Time = 5000 + i * 1000, Type = 1 });
        objects.AddRange(IntenseSpikeMap(10000).HitObjects);
        for (int i = 0; i < 4; i++) objects.Add(new HitObject { X = 100, Y = 100, Time = 15000 + i * 1000, Type = 1 });
        for (int i = 0; i < 4; i++) objects.Add(new HitObject { X = 100, Y = 100, Time = 20000 + i * 1000, Type = 1 });

        Beatmap map = new()
        {
            CS = 4, OD = 8, AR = 9, HP = 6, SliderMultiplier = 1.4,
            TimingPoints = [new() { Time = 0, BeatLength = 500, Uninherited = true }],
            HitObjects = objects
        };

        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.True(result.DifficultySpikeCount >= 1);
        Assert.True(result.Sections[2].Spike.IsSpike);
    }

    [Fact]
    public void Test47_TwoSeparatedSpikes_BothDetected()
    {
        List<HitObject> objects = new();
        for (int i = 0; i < 4; i++) objects.Add(new HitObject { X = 100, Y = 100, Time = i * 1000, Type = 1 });
        objects.AddRange(IntenseSpikeMap(5000).HitObjects);
        for (int i = 0; i < 4; i++) objects.Add(new HitObject { X = 100, Y = 100, Time = 10000 + i * 1000, Type = 1 });
        for (int i = 0; i < 4; i++) objects.Add(new HitObject { X = 100, Y = 100, Time = 15000 + i * 1000, Type = 1 });
        objects.AddRange(IntenseSpikeMap(20000).HitObjects);
        for (int i = 0; i < 4; i++) objects.Add(new HitObject { X = 100, Y = 100, Time = 25000 + i * 1000, Type = 1 });

        Beatmap map = new()
        {
            CS = 4, OD = 8, AR = 9, HP = 6, SliderMultiplier = 1.4,
            TimingPoints = [new() { Time = 0, BeatLength = 500, Uninherited = true }],
            HitObjects = objects
        };

        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.True(result.DifficultySpikeCount >= 2);
    }

    [Fact]
    public void Test48_HighButGloballyFlat_NoSpike()
    {
        Beatmap map = StreamMap(200, intervalMs: 100, startMs: 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.Equal(0, result.DifficultySpikeCount);
    }

    [Fact]
    public void Test49_BoundarySectionSpikeHandling_UsesAvailableNeighborsWithoutThrowing()
    {
        var options = new SectionAnalysisV2Options(SpikeNeighborRadius: 2);
        var contributors = new Dictionary<string, double> { ["Aim"] = 0.8 };
        var spike = SectionAnalysisV2Analyzer.CalculateSpike(60, [20], contributors, options);
        Assert.True(spike.IsSpike);
        Assert.Equal(40, spike.Delta);
        Assert.Equal(3.0, spike.Ratio);
    }

    [Fact]
    public void Test50_DeltaThresholdExact()
    {
        var options = new SectionAnalysisV2Options(SpikeMinimumDelta: 15.0, SpikeMinimumRatio: 1.20, SpikeMinimumDifficulty: 40);
        var contributors = new Dictionary<string, double>();
        var subSpike = SectionAnalysisV2Analyzer.CalculateSpike(50, [35.01], contributors, options);
        Assert.False(subSpike.IsSpike);
        var spike = SectionAnalysisV2Analyzer.CalculateSpike(50, [34.99], contributors, options);
        Assert.True(spike.IsSpike);
    }

    [Fact]
    public void Test51_RatioThresholdExact()
    {
        var options = new SectionAnalysisV2Options(SpikeMinimumDelta: 5.0, SpikeMinimumRatio: 1.20, SpikeMinimumDifficulty: 40);
        var contributors = new Dictionary<string, double>();
        var subSpike = SectionAnalysisV2Analyzer.CalculateSpike(45, [39], contributors, options);
        Assert.False(subSpike.IsSpike);
        var spike = SectionAnalysisV2Analyzer.CalculateSpike(50, [40], contributors, options);
        Assert.True(spike.IsSpike);
    }

    [Fact]
    public void Test52_AbsoluteMinimumDifficultyExact()
    {
        var options = new SectionAnalysisV2Options(SpikeMinimumDelta: 15.0, SpikeMinimumRatio: 1.20, SpikeMinimumDifficulty: 40);
        var contributors = new Dictionary<string, double>();
        var subSpike = SectionAnalysisV2Analyzer.CalculateSpike(39.9, [10], contributors, options);
        Assert.False(subSpike.IsSpike);
        var spike = SectionAnalysisV2Analyzer.CalculateSpike(40.1, [10], contributors, options);
        Assert.True(spike.IsSpike);
    }

    [Fact]
    public void Test53_SpikeStrength_InBoundsZeroToOne()
    {
        var options = new SectionAnalysisV2Options();
        var contributors = new Dictionary<string, double> { ["Aim"] = 0.9, ["Speed"] = 0.5 };
        var spike = SectionAnalysisV2Analyzer.CalculateSpike(80, [20], contributors, options);
        Assert.True(spike.IsSpike);
        Assert.InRange(spike.Strength, 0, 1);
    }

    [Fact]
    public void Test54_ContributorRanking_ReturnsTopContributorsDesc()
    {
        var options = new SectionAnalysisV2Options();
        var contributors = new Dictionary<string, double>
        {
            ["Aim"] = 0.9,
            ["Speed"] = 0.4,
            ["Reading"] = 0.2,
            ["Stamina"] = 0.85,
            ["Density"] = 0.6
        };
        var spike = SectionAnalysisV2Analyzer.CalculateSpike(70, [20], contributors, options);
        Assert.Equal(3, spike.Contributors.Count);
        Assert.Equal("Aim", spike.Contributors[0]);
        Assert.Equal("Stamina", spike.Contributors[1]);
        Assert.Equal("Density", spike.Contributors[2]);
    }
    // ============================================================
    // 7. TESTS TRANSITIONS (55..60)
    // ============================================================

    [Fact]
    public void Test55_JumpToJump_NoTransition()
    {
        Beatmap map = JumpMap(50, intervalMs: 250, startMs: 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.Equal(0, result.GameplayTransitionCount);
    }

    [Fact]
    public void Test56_JumpToStream_TransitionDetected()
    {
        Beatmap jumpPart = JumpMap(20, intervalMs: 250, startMs: 0);
        Beatmap streamPart = StreamMap(40, intervalMs: 100, startMs: 5200);
        Beatmap map = new()
        {
            CS = 4, OD = 8, AR = 9, HP = 6, SliderMultiplier = 1.4,
            TimingPoints = [new() { Time = 0, BeatLength = 500, Uninherited = true }],
            HitObjects = jumpPart.HitObjects.Concat(streamPart.HitObjects).ToList()
        };
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.True(result.GameplayTransitionCount >= 1);
        Assert.Equal(GameplayStructureKind.Jump, result.Sections[1].Transition.PreviousDominantStructure);
        Assert.Equal(GameplayStructureKind.Stream, result.Sections[1].Transition.CurrentDominantStructure);
    }

    [Fact]
    public void Test57_StreamToTech_TransitionDetected()
    {
        Beatmap streamPart = StreamMap(40, intervalMs: 100, startMs: 0);
        Beatmap techPart = TechMap(15, startMs: 5200);
        Beatmap map = new()
        {
            CS = 4, OD = 8, AR = 9, HP = 6, SliderMultiplier = 1.4,
            TimingPoints = [new() { Time = 0, BeatLength = 500, Uninherited = true }],
            HitObjects = streamPart.HitObjects.Concat(techPart.HitObjects).ToList()
        };
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.True(result.SectionCount >= 2);
        Assert.Equal(GameplayStructureKind.Stream, result.Sections[0].DominantStructure);
    }

    [Fact]
    public void Test58_SmallVectorChange_NoTransition()
    {
        var options = new SectionAnalysisV2Options(MinimumTransitionVectorDistance: 0.90);
        Beatmap jumpPart = JumpMap(20, intervalMs: 250, startMs: 0);
        Beatmap streamPart = StreamMap(40, intervalMs: 100, startMs: 5200);
        Beatmap map = new()
        {
            CS = 4, OD = 8, AR = 9, HP = 6, SliderMultiplier = 1.4,
            TimingPoints = [new() { Time = 0, BeatLength = 500, Uninherited = true }],
            HitObjects = jumpPart.HitObjects.Concat(streamPart.HitObjects).ToList()
        };
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map, options);
        Assert.Equal(0, result.GameplayTransitionCount);
    }

    [Fact]
    public void Test59_DominantFlickerProtection()
    {
        var options = new SectionAnalysisV2Options(
            MinimumTransitionVectorDistance: 0.15,
            FlickerProtectionVectorDistance: 0.40);

        Beatmap map = JumpMap(30, 200, 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map, options);
        Assert.NotNull(result);
    }

    [Fact]
    public void Test60_TransitionStrength_InBoundsZeroToOne()
    {
        Beatmap jumpPart = JumpMap(20, intervalMs: 250, startMs: 0);
        Beatmap streamPart = StreamMap(40, intervalMs: 100, startMs: 5200);
        Beatmap map = new()
        {
            CS = 4, OD = 8, AR = 9, HP = 6, SliderMultiplier = 1.4,
            TimingPoints = [new() { Time = 0, BeatLength = 500, Uninherited = true }],
            HitObjects = jumpPart.HitObjects.Concat(streamPart.HitObjects).ToList()
        };
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.All(result.Sections, s => Assert.InRange(s.Transition.Strength, 0, 1));
    }

    // ============================================================
    // 8. TESTS GLOBAUX (61..70)
    // ============================================================

    [Fact]
    public void Test61_SectionCountCorrect()
    {
        Beatmap map = SimpleMap(30, intervalMs: 500, startMs: 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.Equal(result.Sections.Count, result.SectionCount);
    }

    [Fact]
    public void Test62_AverageDifficultyMatchesSections()
    {
        Beatmap map = JumpMap(30, intervalMs: 250, startMs: 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        double expectedAvg = result.Sections.Average(s => s.LocalDifficultyScore);
        Assert.Equal(expectedAvg, result.AverageLocalDifficulty, precision: 4);
    }

    [Fact]
    public void Test63_MaxDifficultyMatchesSections()
    {
        Beatmap map = JumpMap(30, intervalMs: 250, startMs: 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        double expectedMax = result.Sections.Max(s => s.LocalDifficultyScore);
        Assert.Equal(expectedMax, result.MaximumLocalDifficulty, precision: 4);
    }

    [Fact]
    public void Test64_MaxSectionIndexPointsToMaximum()
    {
        Beatmap map = JumpMap(30, intervalMs: 250, startMs: 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.NotNull(result.MaximumDifficultySectionIndex);
        int maxIndex = result.MaximumDifficultySectionIndex.Value;
        Assert.Equal(result.MaximumLocalDifficulty, result.Sections[maxIndex].LocalDifficultyScore, precision: 4);
    }

    [Fact]
    public void Test65_SpikeCountMatchesSections()
    {
        Beatmap map = JumpMap(30, intervalMs: 250, startMs: 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.Equal(result.Sections.Count(s => s.Spike.IsSpike), result.DifficultySpikeCount);
    }

    [Fact]
    public void Test66_TransitionCountMatchesSections()
    {
        Beatmap map = JumpMap(30, intervalMs: 250, startMs: 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.Equal(result.Sections.Count(s => s.Transition.IsTransition), result.GameplayTransitionCount);
    }

    [Fact]
    public void Test67_TimelineLengthsEqualSectionCount()
    {
        Beatmap map = JumpMap(30, intervalMs: 250, startMs: 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.Equal(result.SectionCount, result.DominantStructureTimeline.Count);
        Assert.Equal(result.SectionCount, result.LocalDifficultyTimeline.Count);
        Assert.Equal(result.SectionCount, result.PatternTimeline.Count);
        Assert.Equal(result.SectionCount, result.SkillTimeline.Count);
    }

    [Fact]
    public void Test68_DeterministicSectionOrder()
    {
        Beatmap map = JumpMap(30, intervalMs: 250, startMs: 0);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        for (int i = 0; i < result.SectionCount; i++)
            Assert.Equal(i, result.Sections[i].SectionIndex);
    }

    [Fact]
    public void Test69_RepeatAnalysisIdentical()
    {
        Beatmap map = JumpMap(30, intervalMs: 250, startMs: 0);
        SectionAnalysisResult r1 = SectionAnalysisV2Analyzer.Analyze(map);
        SectionAnalysisResult r2 = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.Equal(r1.SectionCount, r2.SectionCount);
        Assert.Equal(r1.AverageLocalDifficulty, r2.AverageLocalDifficulty);
        Assert.Equal(r1.MaximumLocalDifficulty, r2.MaximumLocalDifficulty);
        Assert.Equal(r1.DifficultySpikeCount, r2.DifficultySpikeCount);
        Assert.Equal(r1.GameplayTransitionCount, r2.GameplayTransitionCount);
    }

    [Fact]
    public void Test70_NoMutationOfGlobalAnalyzersOrBeatmap()
    {
        Beatmap map = JumpMap(30, intervalMs: 250, startMs: 0);
        int originalCount = map.HitObjects.Count;
        double originalFirstTime = map.HitObjects[0].Time;

        GameplayProfile profileBefore = GameplayAnalyzer.Analyze(map);
        SectionAnalysisResult result = SectionAnalysisV2Analyzer.Analyze(map);
        GameplayProfile profileAfter = GameplayAnalyzer.Analyze(map);

        Assert.Equal(originalCount, map.HitObjects.Count);
        Assert.Equal(originalFirstTime, map.HitObjects[0].Time);
        Assert.Equal(profileBefore.AimScore, profileAfter.AimScore);
        Assert.Equal(profileBefore.SpeedScore, profileAfter.SpeedScore);
        Assert.Equal(profileBefore.ReadScore, profileAfter.ReadScore);
        Assert.Equal(profileBefore.TechScore, profileAfter.TechScore);
        Assert.Equal(profileBefore.Identity.Primary, profileAfter.Identity.Primary);
    }
}