namespace BeatInsight.Models.Persistence;

/// <summary>
/// Version du schéma de persistance des analyses.
///
/// Cette version est volontairement distincte de
/// <see cref="BeatInsight.Analysis.AnalyzerVersion"/> :
///
/// - AnalyzerVersion décrit la validité MÉTIER du résultat
///   (les valeurs calculées ont-elles changé ?).
/// - PersistenceSchemaVersion décrit la FORME du résultat stocké
///   (la structure des enregistrements a-t-elle changé ?).
///
/// Les deux évoluent indépendamment : ajouter un champ persisté
/// sans toucher aux formules incrémente uniquement le schéma,
/// et recalibrer une formule sans changer la structure incrémente
/// uniquement l'analyse.
/// </summary>
internal static class PersistenceSchemaVersion
{
    /// <summary>
    /// Version courante du schéma de persistance.
    ///
    /// À incrémenter dès que la structure de
    /// <see cref="BeatmapAnalysisRecord"/> ou de
    /// <see cref="GameplayProfileRecord"/> change de manière
    /// incompatible avec les enregistrements déjà stockés.
    /// </summary>
    internal const int Current = 1;
}
