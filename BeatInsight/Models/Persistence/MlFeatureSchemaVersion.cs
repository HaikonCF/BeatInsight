namespace BeatInsight.Models.Persistence;

/// <summary>
/// Version de la forme des features capturées dans le dataset ML.
///
/// Cette version est indépendante du schéma du cache runtime et de
/// l'AnalyzerVersion : elle évolue uniquement quand la structure ou
/// la sémantique des features exportées (RawFeaturesJson /
/// SectionFeaturesJson) change.
///
/// Elle ne versionne ni le schéma SQL de la table MlDatasetSample ni
/// la représentation des annotations humaines : la migration
/// HumanLabel -> PrimaryHumanLabel/SecondaryHumanLabel (V2.3.5b-1)
/// est gérée par MlDatasetSampleRepository.EnsureSchema() et ne
/// modifie ni la forme ni la sémantique des features, donc n'a pas
/// à incrémenter cette version.
/// </summary>
internal static class MlFeatureSchemaVersion
{
    internal const int Current = 1;
}
