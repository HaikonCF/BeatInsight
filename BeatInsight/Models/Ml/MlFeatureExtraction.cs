namespace BeatInsight.Models.Ml;

/// <summary>
/// Contexte de provenance fourni explicitement lors de la capture d'un
/// échantillon ML. Il ne lit pas le disque et ne génère aucune date : cela
/// laisse l'extraction entièrement pure et déterministe.
/// </summary>
internal sealed record MlDatasetCaptureContext(
    string SourceFilePath,
    long FileSize,
    DateTime FileLastWriteUtc,
    DateTime CapturedAtUtc,
    int? BeatmapId = null,
    string? Md5 = null);

/// <summary>
/// Résultat pur de l'extraction de features. Les JSON destinés au repository
/// sont construits à partir de ces deux objets, sans accès SQLite.
/// </summary>
internal sealed record MlFeatureExtraction(
    MlRawFeatures RawFeatures,
    MlSectionFeatures SectionFeatures);

/// <summary>
/// Features globales disponibles avant toute classification structurelle.
///
/// GameplayIdentity, Traits, Concepts, raisons de classification, scores
/// d'Identity et Community Evidence en sont volontairement absents afin
/// d'éviter toute fuite de label dans le futur dataset.
/// </summary>
internal sealed class MlRawFeatures
{
    // Contexte de la map
    public int Bpm { get; init; }
    public double DurationMilliseconds { get; init; }
    public double Ar { get; init; }
    public double Od { get; init; }
    public double Cs { get; init; }
    public double Hp { get; init; }
    public int CircleCount { get; init; }
    public int SliderCount { get; init; }
    public int SpinnerCount { get; init; }
    public int AnalysedCircleCount { get; init; }

    // Stream
    public double StreamRatio { get; init; }
    public double StreamCoverage { get; init; }
    public int StreamObjectCount { get; init; }
    public int StreamSequenceCount { get; init; }
    public double StreamMeanSequenceLength { get; init; }
    public int StreamMaxSequenceLength { get; init; }
    public int StreamSectionCount { get; init; }

    // Jump
    public double JumpRatio { get; init; }
    public double JumpCoverage { get; init; }
    public int JumpObjectCount { get; init; }
    public int JumpSequenceCount { get; init; }
    public double JumpMeanSequenceLength { get; init; }
    public int JumpMaxSequenceLength { get; init; }
    public int JumpSectionCount { get; init; }

    // Burst transversal
    public double BurstRatio { get; init; }
    public int BurstObjectCount { get; init; }
    public int BurstSequenceCount { get; init; }
    public double BurstMeanSequenceLength { get; init; }
    public int BurstMaxSequenceLength { get; init; }

    // Tech : présence, intensité brute et composants avant Identity.
    public double TechPresence { get; init; }
    public double TechIntensity { get; init; }
    public double TechScore { get; init; }
    public double TechTransitionSignal { get; init; }
    public double TechStructureSignal { get; init; }
    public double TechSpatialSignal { get; init; }
    public double TechTemporalSignal { get; init; }
    public int ComplexSliderCount { get; init; }
    public int SliderSpatialOverlapCount { get; init; }
    public int SharpTechTransitionCount { get; init; }
    public int TechSectionCount { get; init; }

    // Aim : composants et sorties de pression conservés comme observables.
    public double AimDistanceSignal { get; init; }
    public double AimSpeedSignal { get; init; }
    public double AimAngleSignal { get; init; }
    public double AimTemporalSignal { get; init; }
    public double AimTemporalModifier { get; init; }
    public double AimRawIntensity { get; init; }
    public double AimPrecisionModifier { get; init; }
    public double AimAdjustedIntensity { get; init; }
    public double AimCoverage { get; init; }
    public double AimScore { get; init; }

    // Speed : composants et sorties de pression conservés comme observables.
    public int SpeedObjectCount { get; init; }
    public double SpeedRatio { get; init; }
    public double SpeedFastObjectRatio { get; init; }
    public double SpeedDensitySignal { get; init; }
    public double SpeedArSignal { get; init; }
    public double SpeedIntensity { get; init; }
    public double SpeedCoverage { get; init; }
    public double SpeedScore { get; init; }
    public int SpeedNearThresholdTransitionCount { get; init; }
    public double SpeedNearThresholdTransitionRatio { get; init; }
    public int SpeedSectionCount { get; init; }

    // Reading : composants et sorties de pression conservés comme observables.
    public int ReadObjectCount { get; init; }
    public double ReadRatio { get; init; }
    public double ReadCoverage { get; init; }
    public double ReadDensitySignal { get; init; }
    public double ReadClutterSignal { get; init; }
    public double ReadPersistenceSignal { get; init; }
    public double ReadCsSignal { get; init; }
    public double ReadPredictability { get; init; }
    public double ReadNovelty { get; init; }
    public double ReadTemporalRegularity { get; init; }
    public double ReadSpacingRegularity { get; init; }
    public double ReadTrajectoryRepetition { get; init; }
    public double ReadAmbiguity { get; init; }
    public double ReadScore { get; init; }
    public int ReadSectionCount { get; init; }
}

/// <summary>
/// Résumés déterministes des zones déjà détectées par l'analyseur. Les
/// HitObjects individuels ne sont pas sérialisés afin de conserver un dataset
/// compact tout en préservant la structure temporelle utile.
/// </summary>
internal sealed class MlSectionFeatures
{
    public MlSectionSummary Stream { get; init; } = MlSectionSummary.Empty;
    public MlSectionSummary Jump { get; init; } = MlSectionSummary.Empty;
    public MlSectionSummary Tech { get; init; } = MlSectionSummary.Empty;
    public MlSectionSummary Read { get; init; } = MlSectionSummary.Empty;
    public MlSectionSummary Speed { get; init; } = MlSectionSummary.Empty;
}

/// <summary>
/// Agrégat d'une famille de sections : cardinalité, durée, taille et densité
/// des objets. Les densités de sections à durée nulle sont définies à zéro
/// pour que le document JSON ne contienne jamais NaN ou Infinity.
/// </summary>
internal sealed record MlSectionSummary(
    int Count,
    double MeanDurationMilliseconds,
    double MaxDurationMilliseconds,
    double MeanObjectCount,
    int MaxObjectCount,
    double MeanObjectsPerSecond,
    double MaxObjectsPerSecond)
{
    internal static MlSectionSummary Empty { get; } = new(
        Count: 0,
        MeanDurationMilliseconds: 0,
        MaxDurationMilliseconds: 0,
        MeanObjectCount: 0,
        MaxObjectCount: 0,
        MeanObjectsPerSecond: 0,
        MaxObjectsPerSecond: 0);
}

/// <summary>
/// Agrégat de séquences structurelles. Burst n'expose pas de sections dans
/// GameplayProfile ; ses séquences restent donc sa granularité disponible.
/// </summary>
internal sealed record MlSequenceSummary(
    int Count,
    double MeanObjectCount,
    int MaxObjectCount)
{
    internal static MlSequenceSummary Empty { get; } = new(
        Count: 0,
        MeanObjectCount: 0,
        MaxObjectCount: 0);
}
