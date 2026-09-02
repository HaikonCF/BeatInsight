namespace BeatInsight.Analysis;

/// <summary>
/// Version de l'analyse gameplay produite par GameplayAnalyzer.
///
/// Cette classe ne réalise aucun calcul et n'influence en rien
/// le pipeline d'analyse. Elle sert uniquement de marqueur de
/// validité pour les résultats persistés.
///
/// Un résultat persisté dont la version diffère de
/// <see cref="Current"/> doit être considéré comme périmé
/// et recalculé.
/// </summary>
internal static class AnalyzerVersion
{
    /// <summary>
    /// Version courante de l'analyse gameplay.
    ///
    /// ATTENTION : cette valeur doit être incrémentée manuellement
    /// dès qu'un changement modifie les valeurs produites par
    /// GameplayAnalyzer (seuils, formules, détection de patterns,
    /// calibration des scores).
    ///
    /// Ne pas l'incrémenter après une recalibration ferait servir
    /// silencieusement des résultats obsolètes depuis le cache.
    /// </summary>
    internal const int Current = 1;
}
