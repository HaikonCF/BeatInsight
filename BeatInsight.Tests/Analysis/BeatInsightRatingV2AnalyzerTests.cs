using BeatInsight.Analysis.Rating;
using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.Fusion;
using BeatInsight.Models.Rating;
using BeatInsight.Models.Sections;
using BeatInsight.Models.SkillPressure;

namespace BeatInsight.Tests.Analysis;

public sealed class BeatInsightRatingV2AnalyzerTests
{
    [Fact]
    public void EmptyMapProducesZero() => Assert.Equal(0, Analyze(Input()).Score);

    [Fact]
    public void TrivialMapProducesLowRating() => Assert.InRange(Analyze(Input(10, skills: 5, sections: Sections(5))).Score, 0, 15);

    [Fact]
    public void MediumDifficultyProducesIntermediateRating() => Assert.InRange(Analyze(Input(100, skills: 50, pressure: Pressure(50), sections: Sections(50, 50), patterns: Patterns(.5))).Score, 35, 65);

    [Fact]
    public void HighDifficultyOutscoresMediumDifficulty()
    {
        double medium = Analyze(Input(100, skills: 45, pressure: Pressure(45), sections: Sections(45, 45), patterns: Patterns(.45))).Score;
        double high = Analyze(Input(300, 300, 85, Pressure(85), Sections(85, 85, 85), Patterns(.85))).Score;
        Assert.True(high > medium);
    }

    [Fact]
    public void ScoreIsBounded() => Assert.InRange(Analyze(Input(1000, 1000, 1000, Pressure(1000), Sections(1000, 1000), Patterns(10))).Score, 0, 100);

    [Fact]
    public void ConfidenceIsBounded() => Assert.InRange(Analyze(Input(100, skills: 50, pressure: Pressure(50), sections: Sections(50), patterns: Patterns(.5), identityConfidence: 5)).Confidence, 0, 1);

    [Fact]
    public void InvalidNumbersNeverProduceNaNOrInfinity()
    {
        BeatInsightRatingV2 result = Analyze(Input(100, skills: double.NaN, pressure: Pressure(double.PositiveInfinity), sections: Sections(double.NaN), patterns: [new("Bad", FusionPatternFamily.Tech, double.PositiveInfinity, double.NaN)]));
        Assert.All(Numbers(result), value => Assert.True(double.IsFinite(value)));
    }

    [Fact]
    public void IsolatedPeakIsLessSustainedThanPlateau()
    {
        BeatInsightRatingV2 isolated = Analyze(Input(100, skills: 50, sections: Sections(20, 20, 80, 20, 20)));
        BeatInsightRatingV2 plateau = Analyze(Input(100, skills: 50, sections: Sections(60, 60, 80, 60, 60)));
        Assert.True(plateau.SustainedDifficulty > isolated.SustainedDifficulty);
        Assert.True(plateau.Score > isolated.Score);
    }

    [Fact]
    public void SustainedDifficultyRaisesRating() => Assert.True(
        Analyze(Input(100, skills: 50, sections: Sections(60, 60, 60))).Score
        > Analyze(Input(100, skills: 50, sections: Sections(20, 60, 20))).Score);

    [Fact]
    public void SamePeakDifferentSustainedProducesDifferentRating()
    {
        BeatInsightRatingV2 low = Analyze(Input(100, skills: 50, sections: Sections(20, 80, 20)));
        BeatInsightRatingV2 high = Analyze(Input(100, skills: 50, sections: Sections(65, 80, 65)));
        Assert.Equal(low.MaxSectionDifficulty, high.MaxSectionDifficulty);
        Assert.True(high.Score > low.Score);
    }

    [Fact]
    public void SeparatedHardClustersCountAsRepeatedPassages()
    {
        BeatInsightRatingV2 result = Analyze(Input(100, skills: 50, sections: Sections(60, 60, 20, 20, 60, 60, 20, 20, 60, 60)));
        Assert.Equal(3, result.HardPassageCount);
        Assert.True(result.RepeatedHardPassageSignal > 0);
    }

    [Fact]
    public void AdjacentHighSectionsFormOnePassage() => Assert.Equal(1, Analyze(Input(100, skills: 50, sections: Sections(60, 60, 60))).HardPassageCount);

    [Fact]
    public void OneWindowGapDoesNotSplitHardPassage() => Assert.Equal(1, Analyze(Input(100, skills: 50, sections: Sections(60, 60, 20, 60, 60))).HardPassageCount);

    [Fact]
    public void IsolatedHardWindowDoesNotBecomeSustainedPassage() => Assert.Equal(0, Analyze(Input(100, skills: 50, sections: Sections(20, 80, 20))).HardPassageCount);

    [Fact]
    public void EmptyIntroAndOutroSectionsAreExcluded()
    {
        BeatInsightRatingV2 baseline = Analyze(Input(100, skills: 50, sections: Sections(50, 50)));
        BeatInsightRatingV2 withEmpty = Analyze(Input(100, skills: 50, sections: SectionsWithPlayable([100, 50, 50, 100], [false, true, true, false])));
        Assert.Equal(baseline.MeanSectionDifficulty, withEmpty.MeanSectionDifficulty, precision: 10);
    }

    [Fact]
    public void SmallMapRemainsFinite() => Assert.All(Numbers(Analyze(Input(3, 1, 70, Pressure(70), Sections(70), Patterns(.7)))), value => Assert.True(double.IsFinite(value)));

    [Fact]
    public void LongMapCanRemainBounded() => Assert.InRange(Analyze(Input(1000, 900, 80, Pressure(80), Sections(80, 80, 80), Patterns(.8))).Score, 0, 100);

    [Fact]
    public void LongEasyMapGetsNoLargeEnduranceBonus() => Assert.InRange(Analyze(Input(1000, 900, 10, Pressure(10), Sections(10, 10))).EnduranceContribution, 0, 20);

    [Fact]
    public void ShortDifficultMapIsNotStronglyPenalized()
    {
        BeatInsightRatingV2 shortMap = Analyze(Input(200, 60, 85, Pressure(85), Sections(85, 85), Patterns(.85)));
        Assert.True(shortMap.Score >= 65);
    }

    [Fact]
    public void LongSustainedHardMapGetsEnduranceContribution() => Assert.True(Analyze(Input(1000, 600, 80, Pressure(80), Sections(80, 80, 80), Patterns(.8))).EnduranceContribution > 30);

    [Fact]
    public void StaminaWithoutDurationHasLimitedEndurance() => Assert.Equal(0, Analyze(Input(100, 60, 60, Pressure(90), Sections(60, 60))).EnduranceContribution);

    [Fact]
    public void DurationWithoutPressureHasLimitedEndurance() => Assert.True(Analyze(Input(1000, 600, 10, Pressure(0), Sections(10, 10))).EnduranceContribution < 20);

    [Fact]
    public void NoPatternProducesZeroPatternContribution() => Assert.Equal(0, Analyze(Input(100, skills: 50, sections: Sections(50))).PatternContribution);

    [Fact]
    public void OneStrongPatternProducesBoundedContribution()
    {
        double value = Analyze(Input(100, skills: 50, sections: Sections(50), patterns: Patterns(.9))).PatternContribution;
        Assert.InRange(value, 0, 100);
        Assert.True(value > 0);
    }

    [Fact]
    public void ManyWeakPatternsDoNotInflateLinearly()
    {
        RatingV2PatternEvidence[] one = [new("A", FusionPatternFamily.Jump, 30, .8)];
        RatingV2PatternEvidence[] many = Enumerable.Range(0, 20).Select(index => new RatingV2PatternEvidence($"P{index}", FusionPatternFamily.Jump, 30, .8)).ToArray();
        Assert.Equal(Analyze(Input(100, skills: 50, sections: Sections(50), patterns: one)).PatternContribution,
            Analyze(Input(100, skills: 50, sections: Sections(50), patterns: many)).PatternContribution);
    }

    [Fact]
    public void MultipleStrongFamiliesIncreasePatternContribution() => Assert.True(
        Analyze(Input(100, skills: 50, sections: Sections(50), patterns: Patterns(.8, 3))).PatternContribution
        > Analyze(Input(100, skills: 50, sections: Sections(50), patterns: Patterns(.8))).PatternContribution);

    [Fact]
    public void PatternContributionIsBounded() => Assert.InRange(Analyze(Input(100, skills: 50, sections: Sections(50), patterns: Patterns(10, 3))).PatternContribution, 0, 100);

    [Fact]
    public void BalancedProfileHasNoSpecializationBonus() => Assert.Equal(0, Analyze(Input(100, aim: 70, speed: 70, reading: 70, tech: 70, sections: Sections(50))).SpecializationContribution);

    [Fact]
    public void WeakSpecializedProfileHasNoStrongBonus() => Assert.True(Analyze(Input(100, aim: 45, speed: 5, reading: 5, tech: 5, sections: Sections(30))).SpecializationContribution < 1);

    [Fact]
    public void StrongSpecializedProfileGetsSmallBonus() => Assert.InRange(Analyze(Input(100, aim: 95, speed: 10, reading: 10, tech: 10, sections: Sections(50))).SpecializationContribution, 1, 5);

    [Fact]
    public void SpecializationCapIsRespected() => Assert.InRange(Analyze(Input(100, aim: 1000, speed: 0, reading: 0, tech: 0, sections: Sections(50))).SpecializationContribution, 0, BeatInsightRatingV2Analyzer.SpecializationCap);

    [Fact]
    public void StarRatingCannotEnterSyntheticInput() => Assert.DoesNotContain(typeof(BeatInsightRatingV2Input).GetProperties(), property => property.Name.Contains("Star", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void IdentityLabelCannotEnterSyntheticInput() => Assert.DoesNotContain(typeof(BeatInsightRatingV2Input).GetProperties(), property => property.Name.Contains("Primary", StringComparison.OrdinalIgnoreCase) || property.Name.Contains("Secondary", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void CommunityTagsCannotEnterSyntheticInput() => Assert.DoesNotContain(typeof(BeatInsightRatingV2Input).GetProperties(), property => property.Name.Contains("Community", StringComparison.OrdinalIgnoreCase) || property.Name.Contains("Tag", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void DebugSwitchDoesNotChangeScore()
    {
        BeatInsightRatingV2Input input = Input(100, skills: 60, pressure: Pressure(60), sections: Sections(60), patterns: Patterns(.6));
        GameplayDebug.RatingV2Enabled = false;
        double off = Analyze(input).Score;
        GameplayDebug.RatingV2Enabled = true;
        double on = Analyze(input).Score;
        GameplayDebug.RatingV2Enabled = false;
        Assert.Equal(off, on);
    }

    [Fact]
    public void DoubleAnalysisIsDeterministic()
    {
        BeatInsightRatingV2Input input = Input(100, 300, 60, Pressure(60), Sections(40, 60, 40), Patterns(.6, 2));
        Assert.Equal(Snapshot(Analyze(input)), Snapshot(Analyze(input)));
    }

    [Fact]
    public void ContributorOrderIsStable()
    {
        BeatInsightRatingV2Input input = Input(100, 300, 60, Pressure(60), Sections(40, 60, 40), Patterns(.6, 2));
        Assert.Equal(Analyze(input).TopContributors.Select(item => item.Name), Analyze(input).TopContributors.Select(item => item.Name));
    }

    [Fact]
    public void SyntheticAnalysisDoesNotMutateSections()
    {
        SectionAnalysisResult sections = Sections(20, 80, 20);
        string before = string.Join('|', sections.LocalDifficultyTimeline);
        _ = Analyze(Input(100, skills: 50, sections: sections));
        Assert.Equal(before, string.Join('|', sections.LocalDifficultyTimeline));
    }

    [Fact]
    public void RealAdapterIgnoresStarRatingAndPreservesHistoricalState()
    {
        Beatmap first = FixtureLoader.Load("Tower Of Heaven [Extra].osu");
        Beatmap second = FixtureLoader.Load("Tower Of Heaven [Extra].osu");
        double historicalRating = first.BeatInsightRating;
        GameplayProfile profile = first.GameplayProfile;
        string profileSnapshot = ProfileSnapshot(profile);
        second.OsuStarRating = first.OsuStarRating + 10;

        BeatInsightRatingV2 firstResult = BeatInsightRatingV2Analyzer.Analyze(first);
        BeatInsightRatingV2 secondResult = BeatInsightRatingV2Analyzer.Analyze(second);

        Assert.Equal(firstResult.Score, secondResult.Score, precision: 10);
        Assert.Equal(historicalRating, first.BeatInsightRating);
        Assert.Same(profile, first.GameplayProfile);
        Assert.Equal(profileSnapshot, ProfileSnapshot(first.GameplayProfile));
    }

    [Fact]
    public void ConfidenceDoesNotModifyScore()
    {
        BeatInsightRatingV2Input high = Input(100, skills: 60, pressure: Pressure(60, .9), sections: Sections(60), patterns: Patterns(.6), identityConfidence: .9);
        BeatInsightRatingV2Input low = high with { SkillPressure = Pressure(60, .1), IdentityConfidence = .1, Patterns = high.Patterns.Select(pattern => pattern with { Confidence = .1 }).ToArray() };
        Assert.Equal(Analyze(high).Score, Analyze(low).Score, precision: 10);
        Assert.NotEqual(Analyze(high).Confidence, Analyze(low).Confidence);
    }

    private static BeatInsightRatingV2 Analyze(BeatInsightRatingV2Input input) => BeatInsightRatingV2Analyzer.Analyze(input);

    private static BeatInsightRatingV2Input Input(
        int objects = 0,
        double duration = 0,
        double skills = 0,
        SkillPressureProfile? pressure = null,
        SectionAnalysisResult? sections = null,
        IReadOnlyList<RatingV2PatternEvidence>? patterns = null,
        double identityConfidence = .8,
        double? aim = null,
        double? speed = null,
        double? reading = null,
        double? tech = null) => new(
            objects, duration, aim ?? skills, speed ?? skills, reading ?? skills, tech ?? skills,
            pressure ?? Pressure(), sections ?? Sections(), patterns ?? Array.Empty<RatingV2PatternEvidence>(), identityConfidence);

    private static SkillPressureProfile Pressure(double score = 0, double confidence = .8)
    {
        SkillPressureMetric metric = new(Math.Clamp(score, 0, 100), confidence, true, true, new Dictionary<string, double>(), string.Empty);
        SkillPressureMetric unavailable = SkillPressureMetric.Unavailable("Unavailable");
        return new(metric, metric, metric, metric, metric, unavailable, metric, metric, confidence, 7, []);
    }

    private static RatingV2PatternEvidence[] Patterns(double strength, int families = 1) => Enumerable.Range(0, families)
        .Select(index => new RatingV2PatternEvidence($"P{index}", (FusionPatternFamily)index, strength * 100, .8)).ToArray();

    private static SectionAnalysisResult Sections(params double[] difficulty) => SectionsWithPlayable(difficulty, Enumerable.Repeat(true, difficulty.Length).ToArray());

    private static SectionAnalysisResult SectionsWithPlayable(IReadOnlyList<double> difficulty, IReadOnlyList<bool> playable)
    {
        GameplaySectionV2Analysis[] sections = difficulty.Select((value, index) => Section(index, value, playable[index])).ToArray();
        if (sections.Length == 0) return SectionAnalysisResult.Empty(TimeSpan.Zero);
        int maxIndex = Array.IndexOf(difficulty.ToArray(), difficulty.Max());
        return new(sections, difficulty.Average(), difficulty.Max(), maxIndex, 0, 0,
            Enumerable.Repeat(GameplayStructureKind.ClassicMixed, sections.Length).ToArray(), difficulty.ToArray(),
            Enumerable.Repeat(new SectionPatternTimelinePoint(0, 0, 0), sections.Length).ToArray(),
            Enumerable.Repeat(new SectionSkillTimelinePoint(0, 0, 0, 0, null, null, null, null, null, null), sections.Length).ToArray(),
            .8, TimeSpan.Zero);
    }

    private static GameplaySectionV2Analysis Section(int index, double difficulty, bool playable) => new(
        index, index * 5000, (index + 1) * 5000, playable ? [index] : Array.Empty<int>(), playable ? 1 : 0, 0, 0, 0, 1,
        0, 0, 0, 0, GameplayStructureKind.ClassicMixed, null, new SectionLocalSkills(0, 0, 0, 0), Pressure(),
        SectionPatternFamilyV2.Empty, SectionPatternFamilyV2.Empty, SectionPatternFamilyV2.Empty,
        difficulty, .8, SectionDifficultySpike.None, SectionGameplayTransition.First(GameplayStructureKind.ClassicMixed), string.Empty);

    private static IEnumerable<double> Numbers(BeatInsightRatingV2 result) =>
    [
        result.Score, result.Confidence, result.BaseDifficulty, result.PeakDifficulty,
        result.SustainedDifficulty, result.SkillPressureContribution, result.PatternContribution,
        result.EnduranceContribution, result.SpecializationContribution, result.MeanSectionDifficulty,
        result.MaxSectionDifficulty, result.HighDifficultySectionRatio, result.RepeatedHardPassageSignal
    ];

    private static string ProfileSnapshot(GameplayProfile profile) => string.Join('|', profile.AimScore, profile.SpeedScore, profile.ReadScore, profile.TechScore, profile.Identity.Primary, profile.Identity.Secondary);
    private static string Snapshot(BeatInsightRatingV2 rating) => string.Join('|', Numbers(rating).Select(value => value.ToString("R")).Concat(rating.TopContributors.Select(item => item.ToString())).Concat(rating.Flags.Select(flag => flag.ToString())));
}
