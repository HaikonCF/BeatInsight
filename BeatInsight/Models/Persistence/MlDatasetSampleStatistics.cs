namespace BeatInsight.Models.Persistence;

/// <summary>
/// Compteurs de présentation du dataset ML. Cette projection ne contient ni
/// feature, ni label individuel : elle sert uniquement à afficher l'état du
/// corpus dans l'interface sans exposer de SQL au code-behind.
/// </summary>
internal sealed record MlDatasetSampleStatistics(
    int SampleCount,
    int HumanValidatedCount,
    int UnlabeledCount);
