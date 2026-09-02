using BeatInsight.Models;
using BeatInsight.Models.Ml;
using BeatInsight.Models.Persistence;
using BeatInsight.Services.Ml;
using System.Text.Json;

namespace BeatInsight.Tests.Ml;

/// <summary>
/// Contrats de l'extraction ML pure. Ces tests ne consultent ni SQLite, ni
/// API communautaire, ni GameplayIdentity comme source de feature.
/// </summary>
public sealed class MlFeatureExtractorTests
{
    private static readonly MlDatasetCaptureContext CaptureContext = new(
        SourceFilePath: @"C:\Fixtures\structural-map.osu",
        FileSize: 123_456,
        FileLastWriteUtc: new DateTime(
            2026,
            9,
            3,
            10,
            20,
            30,
            DateTimeKind.Utc),
        CapturedAtUtc: new DateTime(
            2026,
            9,
            3,
            11,
            20,
            30,
            DateTimeKind.Utc),
        BeatmapId: 42,
        Md5: "test-md5");

    [Fact]
    public void Extract_IsDeterministicForSameAnalysedBeatmap()
    {
        Beatmap beatmap = CreateStructuredBeatmap();

        MlDatasetSample first = MlFeatureExtractor.CreateSample(
            beatmap,
            CaptureContext);
        MlDatasetSample second = MlFeatureExtractor.CreateSample(
            beatmap,
            CaptureContext);

        Assert.Equal(first.RawFeaturesJson, second.RawFeaturesJson);
        Assert.Equal(first.SectionFeaturesJson, second.SectionFeaturesJson);
        Assert.Equal(first.FeatureSchemaVersion, second.FeatureSchemaVersion);
        Assert.Equal(first.AnalyzerVersion, second.AnalyzerVersion);
        Assert.Equal(first.CapturedAtUtc, second.CapturedAtUtc);
    }

    [Fact]
    public void Extract_PreservesStructuralSignalsWithoutReadingIdentity()
    {
        Beatmap beatmap = CreateStructuredBeatmap();

        MlFeatureExtraction extraction = MlFeatureExtractor.Extract(beatmap);
        MlRawFeatures raw = extraction.RawFeatures;

        Assert.Equal(0.50, raw.StreamRatio, precision: 10);
        Assert.Equal(0.80, raw.StreamCoverage, precision: 10);
        Assert.Equal(5, raw.StreamObjectCount);
        Assert.Equal(2, raw.StreamSequenceCount);
        Assert.Equal(4.0, raw.StreamMeanSequenceLength, precision: 10);
        Assert.Equal(4, raw.StreamMaxSequenceLength);
        Assert.Equal(1, raw.JumpSequenceCount);
        Assert.Equal(0.30, raw.TechPresence, precision: 10);
        Assert.Equal(41.0, raw.TechIntensity, precision: 10);
        Assert.Equal(0.44, raw.TechTemporalSignal, precision: 10);

        MlDatasetSample sample = MlFeatureExtractor.CreateSample(
            beatmap,
            CaptureContext);

        Assert.DoesNotContain("LEAKAGE_PRIMARY", sample.RawFeaturesJson);
        Assert.DoesNotContain("LEAKAGE_SECONDARY", sample.RawFeaturesJson);
        Assert.DoesNotContain("Identity", sample.RawFeaturesJson);
        Assert.Null(sample.HumanLabel);
        Assert.False(sample.HumanValidated);
        Assert.Null(sample.CommunityEvidenceJson);
    }

    [Fact]
    public void Extract_SummarizesSectionsWithDurationsObjectsAndDensities()
    {
        MlFeatureExtraction extraction = MlFeatureExtractor.Extract(
            CreateStructuredBeatmap());

        MlSectionSummary stream = extraction.SectionFeatures.Stream;

        Assert.Equal(2, stream.Count);
        Assert.Equal(750.0, stream.MeanDurationMilliseconds, precision: 10);
        Assert.Equal(1_000.0, stream.MaxDurationMilliseconds, precision: 10);
        Assert.Equal(4.0, stream.MeanObjectCount, precision: 10);
        Assert.Equal(5, stream.MaxObjectCount);
        Assert.Equal(5.5, stream.MeanObjectsPerSecond, precision: 10);
        Assert.Equal(6.0, stream.MaxObjectsPerSecond, precision: 10);

        Assert.Equal(1, extraction.SectionFeatures.Tech.Count);
        Assert.Equal(500.0,
            extraction.SectionFeatures.Tech.MeanDurationMilliseconds,
            precision: 10);
        Assert.Equal(0, extraction.SectionFeatures.Read.Count);
    }

    [Fact]
    public void Extract_NormalizesNonFiniteValuesBeforeJsonSerialization()
    {
        Beatmap beatmap = new()
        {
            AR = double.NaN,
            OD = double.PositiveInfinity,
            GameplayProfile = new GameplayProfile
            {
                AnalysedCircleCount = 3,
                StreamRatio = double.NaN,
                TechIntensity = double.NegativeInfinity,
                AimScore = double.PositiveInfinity,
                ReadAmbiguity = double.NaN,
                StreamSections =
                [
                    new GameplaySection(
                        "Stream",
                        0,
                        2,
                        100,
                        100,
                        3),
                ],
            },
        };

        MlDatasetSample sample = MlFeatureExtractor.CreateSample(
            beatmap,
            CaptureContext);

        Assert.DoesNotContain("NaN", sample.RawFeaturesJson);
        Assert.DoesNotContain("Infinity", sample.RawFeaturesJson);
        string sectionFeaturesJson = Assert.IsType<string>(
            sample.SectionFeaturesJson);

        Assert.DoesNotContain("NaN", sectionFeaturesJson);
        Assert.DoesNotContain("Infinity", sectionFeaturesJson);

        using JsonDocument rawJson = JsonDocument.Parse(sample.RawFeaturesJson);
        using JsonDocument sectionJson = JsonDocument.Parse(
            sectionFeaturesJson);

        Assert.Equal(0.0, rawJson.RootElement.GetProperty("ar").GetDouble());
        Assert.Equal(0.0,
            rawJson.RootElement.GetProperty("techIntensity").GetDouble());
        Assert.Equal(0.0,
            sectionJson.RootElement
                .GetProperty("stream")
                .GetProperty("meanObjectsPerSecond")
                .GetDouble());
    }

    [Fact]
    public void CreateSample_JsonRoundTripsRawAndSectionFeatures()
    {
        MlDatasetSample sample = MlFeatureExtractor.CreateSample(
            CreateStructuredBeatmap(),
            CaptureContext);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        MlRawFeatures? raw = JsonSerializer.Deserialize<MlRawFeatures>(
            sample.RawFeaturesJson,
            options);
        string sectionFeaturesJson = Assert.IsType<string>(
            sample.SectionFeaturesJson);

        MlSectionFeatures? sections = JsonSerializer.Deserialize<
            MlSectionFeatures>(
            sectionFeaturesJson,
            options);

        Assert.NotNull(raw);
        Assert.NotNull(sections);
        Assert.Equal(180, raw.Bpm);
        Assert.Equal(0.80, raw.StreamCoverage, precision: 10);
        Assert.Equal(2, sections.Stream.Count);
        Assert.Equal(1, sections.Tech.Count);
    }

    private static Beatmap CreateStructuredBeatmap()
    {
        return new Beatmap
        {
            BPM = 180,
            Length = TimeSpan.FromSeconds(120),
            AR = 9.5,
            OD = 8.2,
            CS = 4.0,
            HP = 6.0,
            CircleCount = 10,
            SliderCount = 3,
            SpinnerCount = 1,
            GameplayProfile = new GameplayProfile
            {
                AnalysedCircleCount = 10,

                StreamRatio = 0.50,
                StreamObjectCount = 5,
                StreamSequenceCount = 2,
                StreamSequences =
                [
                    new PatternSequence(0, 3),
                    new PatternSequence(5, 8),
                ],
                StreamSections =
                [
                    new GameplaySection(
                        "Stream",
                        0,
                        2,
                        100,
                        600,
                        3),
                    new GameplaySection(
                        "Stream",
                        5,
                        9,
                        1_000,
                        2_000,
                        5),
                ],

                JumpRatio = 0.20,
                JumpObjectCount = 2,
                JumpSequenceCount = 1,
                JumpSequences = [new PatternSequence(2, 5)],
                JumpSections =
                [
                    new GameplaySection(
                        "Jump",
                        2,
                        5,
                        700,
                        1_500,
                        4),
                ],

                BurstRatio = 0.10,
                BurstObjectCount = 4,
                BurstSequenceCount = 1,
                BurstSequences = [new PatternSequence(1, 4)],

                TechPresence = 0.30,
                TechIntensity = 41.0,
                TechScore = 22.4,
                TechTransitionSignal = 0.12,
                TechStructureSignal = 0.38,
                TechSpatialSignal = 0.20,
                TechTemporalSignal = 0.44,
                ComplexSliderCount = 2,
                SliderSpatialOverlapCount = 1,
                SharpTechTransitionCount = 3,
                TechSections =
                [
                    new GameplaySection(
                        "Tech",
                        4,
                        7,
                        2_000,
                        2_500,
                        4),
                ],

                AimDistanceSignal = 0.50,
                AimSpeedSignal = 0.60,
                AimAngleSignal = 0.40,
                AimTemporalSignal = 0.30,
                AimTemporalModifier = 0.72,
                AimRawIntensity = 0.43,
                AimPrecisionModifier = 1.0,
                AimAdjustedIntensity = 0.43,
                AimCoverage = 0.50,
                AimScore = 21.5,

                SpeedObjectCount = 6,
                SpeedRatio = 0.60,
                SpeedFastObjectRatio = 0.55,
                SpeedDensitySignal = 0.70,
                SpeedARSignal = 0.0,
                SpeedIntensityValue = 0.65,
                SpeedCoverage = 0.45,
                SpeedScore = 29.25,
                SpeedNearThresholdTransitionCount = 2,
                SpeedNearThresholdTransitionRatio = 0.20,
                SpeedSections =
                [
                    new GameplaySection(
                        "Speed",
                        1,
                        6,
                        500,
                        1_100,
                        6),
                ],

                ReadObjectCount = 4,
                ReadRatio = 0.40,
                ReadCoverage = 0.30,
                ReadDensitySignal = 0.65,
                ReadClutterSignal = 0.55,
                ReadPersistenceSignal = 0.0,
                ReadCSSignal = 0.0,
                ReadPredictability = 0.70,
                ReadNovelty = 0.30,
                ReadTemporalRegularity = 0.60,
                ReadSpacingRegularity = 0.50,
                ReadTrajectoryRepetition = 0.40,
                ReadAmbiguity = 0.20,
                ReadScore = 33.0,
                ReadSectionCount = 0,
                ReadSections = [],

                Identity = new GameplayIdentity
                {
                    Primary = "LEAKAGE_PRIMARY",
                    Secondary = "LEAKAGE_SECONDARY",
                    Confidence = 99,
                },
            },
        };
    }
}
