namespace BeatInsight.Models.Persistence;

/// <summary>
/// Version de la forme des features capturées dans le dataset ML.
///
/// Cette version est indépendante du schéma du cache runtime et de
/// l'AnalyzerVersion : elle évolue uniquement quand la structure ou
/// la sémantique des features exportées change.
/// </summary>
internal static class MlFeatureSchemaVersion
{
    internal const int Current = 1;
}
