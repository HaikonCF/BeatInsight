using BeatInsight.Analysis;
using BeatInsight.Models;
using BeatInsight.Models.Ml;
using BeatInsight.Models.Persistence;
using System.Text.Json;

namespace BeatInsight.Services.Ml;

/// <summary>
/// Extrait des features ML à partir d'une <see cref="Beatmap"/> déjà analysée.
///
/// Cette classe est pure : elle ne relit aucun fichier, ne modifie pas la
/// beatmap, n'écrit pas en SQLite et ne contacte aucun service externe.
/// </summary>
internal static class MlFeatureExtractor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>
    /// Construit les features globales et les résumés de sections à partir
    /// des seules données d'analyse déjà présentes dans la beatmap.
    /// </summary>
    internal static MlFeatureExtraction Extract(Beatmap beatmap)
    {
        ArgumentNullException.ThrowIfNull(beatmap);

        GameplayProfile profile = beatmap.GameplayProfile
            ?? throw new InvalidOperationException(
                "ML feature extraction requires an analysed GameplayProfile.");

        MlSequenceSummary streamSequences =
            SummarizeSequences(profile.StreamSequences);
        MlSequenceSummary jumpSequences =
            SummarizeSequences(profile.JumpSequences);
        MlSequenceSummary burstSequences =
            SummarizeSequences(profile.BurstSequences);

        MlSectionSummary streamSections =
            SummarizeSections(profile.StreamSections);
        MlSectionSummary jumpSections =
            SummarizeSections(profile.JumpSections);
        MlSectionSummary techSections =
            SummarizeSections(profile.TechSections);
        MlSectionSummary readSections =
            SummarizeSections(profile.ReadSections);
        MlSectionSummary speedSections =
            SummarizeSections(profile.SpeedSections);

        return new MlFeatureExtraction(
            new MlRawFeatures
            {
                // Contexte map
                Bpm = beatmap.BPM,
                DurationMilliseconds = Finite(beatmap.Length.TotalMilliseconds),
                Ar = Finite(beatmap.AR),
                Od = Finite(beatmap.OD),
                Cs = Finite(beatmap.CS),
                Hp = Finite(beatmap.HP),
                CircleCount = beatmap.CircleCount,
                SliderCount = beatmap.SliderCount,
                SpinnerCount = beatmap.SpinnerCount,
                AnalysedCircleCount = profile.AnalysedCircleCount,

                // Stream
                StreamRatio = Finite(profile.StreamRatio),
                StreamCoverage = CalculateSectionCoverage(
                    profile.StreamSections,
                    profile.AnalysedCircleCount),
                StreamObjectCount = profile.StreamObjectCount,
                StreamSequenceCount = streamSequences.Count,
                StreamMeanSequenceLength = streamSequences.MeanObjectCount,
                StreamMaxSequenceLength = streamSequences.MaxObjectCount,
                StreamSectionCount = streamSections.Count,

                // Jump
                JumpRatio = Finite(profile.JumpRatio),
                JumpCoverage = CalculateSectionCoverage(
                    profile.JumpSections,
                    profile.AnalysedCircleCount),
                JumpObjectCount = profile.JumpObjectCount,
                JumpSequenceCount = jumpSequences.Count,
                JumpMeanSequenceLength = jumpSequences.MeanObjectCount,
                JumpMaxSequenceLength = jumpSequences.MaxObjectCount,
                JumpSectionCount = jumpSections.Count,

                // Burst
                BurstRatio = Finite(profile.BurstRatio),
                BurstObjectCount = profile.BurstObjectCount,
                BurstSequenceCount = burstSequences.Count,
                BurstMeanSequenceLength = burstSequences.MeanObjectCount,
                BurstMaxSequenceLength = burstSequences.MaxObjectCount,

                // Tech
                TechPresence = Finite(profile.TechPresence),
                TechIntensity = Finite(profile.TechIntensity),
                TechScore = Finite(profile.TechScore),
                TechTransitionSignal = Finite(profile.TechTransitionSignal),
                TechStructureSignal = Finite(profile.TechStructureSignal),
                TechSpatialSignal = Finite(profile.TechSpatialSignal),
                TechTemporalSignal = Finite(profile.TechTemporalSignal),
                ComplexSliderCount = profile.ComplexSliderCount,
                SliderSpatialOverlapCount = profile.SliderSpatialOverlapCount,
                SharpTechTransitionCount = profile.SharpTechTransitionCount,
                TechSectionCount = techSections.Count,

                // Aim
                AimDistanceSignal = Finite(profile.AimDistanceSignal),
                AimSpeedSignal = Finite(profile.AimSpeedSignal),
                AimAngleSignal = Finite(profile.AimAngleSignal),
                AimTemporalSignal = Finite(profile.AimTemporalSignal),
                AimTemporalModifier = Finite(profile.AimTemporalModifier),
                AimRawIntensity = Finite(profile.AimRawIntensity),
                AimPrecisionModifier = Finite(profile.AimPrecisionModifier),
                AimAdjustedIntensity = Finite(profile.AimAdjustedIntensity),
                AimCoverage = Finite(profile.AimCoverage),
                AimScore = Finite(profile.AimScore),

                // Speed
                SpeedObjectCount = profile.SpeedObjectCount,
                SpeedRatio = Finite(profile.SpeedRatio),
                SpeedFastObjectRatio = Finite(profile.SpeedFastObjectRatio),
                SpeedDensitySignal = Finite(profile.SpeedDensitySignal),
                SpeedArSignal = Finite(profile.SpeedARSignal),
                SpeedIntensity = Finite(profile.SpeedIntensityValue),
                SpeedCoverage = Finite(profile.SpeedCoverage),
                SpeedScore = Finite(profile.SpeedScore),
                SpeedNearThresholdTransitionCount =
                    profile.SpeedNearThresholdTransitionCount,
                SpeedNearThresholdTransitionRatio =
                    Finite(profile.SpeedNearThresholdTransitionRatio),
                SpeedSectionCount = speedSections.Count,

                // Reading
                ReadObjectCount = profile.ReadObjectCount,
                ReadRatio = Finite(profile.ReadRatio),
                ReadCoverage = Finite(profile.ReadCoverage),
                ReadDensitySignal = Finite(profile.ReadDensitySignal),
                ReadClutterSignal = Finite(profile.ReadClutterSignal),
                ReadPersistenceSignal = Finite(profile.ReadPersistenceSignal),
                ReadCsSignal = Finite(profile.ReadCSSignal),
                ReadPredictability = Finite(profile.ReadPredictability),
                ReadNovelty = Finite(profile.ReadNovelty),
                ReadTemporalRegularity =
                    Finite(profile.ReadTemporalRegularity),
                ReadSpacingRegularity =
                    Finite(profile.ReadSpacingRegularity),
                ReadTrajectoryRepetition =
                    Finite(profile.ReadTrajectoryRepetition),
                ReadAmbiguity = Finite(profile.ReadAmbiguity),
                ReadScore = Finite(profile.ReadScore),
                ReadSectionCount = readSections.Count,
            },
            new MlSectionFeatures
            {
                Stream = streamSections,
                Jump = jumpSections,
                Tech = techSections,
                Read = readSections,
                Speed = speedSections,
            });
    }

    /// <summary>
    /// Construit un DTO persistant, sans effectuer l'écriture SQLite. Les
    /// informations de provenance et de capture sont fournies par l'appelant
    /// afin de ne pas introduire de lecture de fichier ou de DateTime.UtcNow.
    /// </summary>
    internal static MlDatasetSample CreateSample(
        Beatmap beatmap,
        MlDatasetCaptureContext captureContext)
    {
        ArgumentNullException.ThrowIfNull(captureContext);

        return CreateSample(Extract(beatmap), captureContext);
    }

    /// <summary>
    /// Convertit une extraction déjà calculée en DTO persistant. Cette
    /// surcharge permet de tester et de composer la capture sans relancer
    /// l'extraction.
    /// </summary>
    internal static MlDatasetSample CreateSample(
        MlFeatureExtraction extraction,
        MlDatasetCaptureContext captureContext)
    {
        ArgumentNullException.ThrowIfNull(extraction);
        ArgumentNullException.ThrowIfNull(captureContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            captureContext.SourceFilePath);

        return new MlDatasetSample
        {
            SourceFilePath = captureContext.SourceFilePath,
            BeatmapId = captureContext.BeatmapId,
            Md5 = captureContext.Md5,
            FileSize = captureContext.FileSize,
            FileLastWriteUtc = ToUtc(captureContext.FileLastWriteUtc),
            FeatureSchemaVersion = MlFeatureSchemaVersion.Current,
            AnalyzerVersion = AnalyzerVersion.Current,
            CapturedAtUtc = ToUtc(captureContext.CapturedAtUtc),
            RawFeaturesJson = JsonSerializer.Serialize(
                extraction.RawFeatures,
                JsonOptions),
            SectionFeaturesJson = JsonSerializer.Serialize(
                extraction.SectionFeatures,
                JsonOptions),
            // Les labels humains et Community Evidence sont ajoutés par des
            // étapes explicites ultérieures ; l'extracteur ne les infère pas.
            HumanLabel = null,
            HumanValidated = false,
            CommunityEvidenceJson = null,
            CommunityCapturedAtUtc = null,
        };
    }

    private static MlSequenceSummary SummarizeSequences(
        IReadOnlyList<PatternSequence>? sequences)
    {
        if (sequences is not { Count: > 0 })
        {
            return MlSequenceSummary.Empty;
        }

        int[] lengths = sequences
            .Select(sequence => Math.Max(0, sequence.ObjectCount))
            .ToArray();

        return new MlSequenceSummary(
            Count: lengths.Length,
            MeanObjectCount: Finite(lengths.Average()),
            MaxObjectCount: lengths.Max());
    }

    private static MlSectionSummary SummarizeSections(
        IReadOnlyList<GameplaySection>? sections)
    {
        if (sections is not { Count: > 0 })
        {
            return MlSectionSummary.Empty;
        }

        double[] durations = sections
            .Select(section => Math.Max(0.0, Finite(section.Duration)))
            .ToArray();
        int[] objectCounts = sections
            .Select(section => Math.Max(0, section.ObjectCount))
            .ToArray();
        double[] densities = sections
            .Select(section => CalculateSectionDensity(section))
            .ToArray();

        return new MlSectionSummary(
            Count: sections.Count,
            MeanDurationMilliseconds: Finite(durations.Average()),
            MaxDurationMilliseconds: Finite(durations.Max()),
            MeanObjectCount: Finite(objectCounts.Average()),
            MaxObjectCount: objectCounts.Max(),
            MeanObjectsPerSecond: Finite(densities.Average()),
            MaxObjectsPerSecond: Finite(densities.Max()));
    }

    private static double CalculateSectionCoverage(
        IReadOnlyList<GameplaySection>? sections,
        int analysedCircleCount)
    {
        if (analysedCircleCount <= 0 || sections is not { Count: > 0 })
        {
            return 0;
        }

        int coveredObjects = sections.Sum(
            section => Math.Max(0, section.ObjectCount));

        return Finite(Math.Clamp(
            (double)coveredObjects / analysedCircleCount,
            0.0,
            1.0));
    }

    private static double CalculateSectionDensity(GameplaySection section)
    {
        double durationMilliseconds = Math.Max(0.0, Finite(section.Duration));

        if (durationMilliseconds <= 0.0)
        {
            return 0.0;
        }

        return Finite(
            Math.Max(0, section.ObjectCount)
            / (durationMilliseconds / 1000.0));
    }

    private static double Finite(double value) =>
        double.IsFinite(value) ? value : 0.0;

    private static DateTime ToUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : value.ToUniversalTime();
}
