using BeatInsight.Models;

namespace BeatInsight.Services.Persistence;

/// <summary>
/// Résultat typé d'une résolution de cache.
///
/// Existe pour que les appelants qui ont besoin de savoir si le
/// résultat provient du cache (par exemple le scanner de
/// bibliothèque, pour ses compteurs de progression) n'aient pas à le
/// déduire de l'état de l'objet <see cref="Beatmap"/> — notamment pas
/// de la présence ou de l'absence de HitObjects, qui est un détail
/// d'implémentation du snapshot et non un signal métier.
/// </summary>
internal sealed class BeatmapAnalysisOutcome
{
    /// <summary>
    /// Beatmap analysée, ou snapshot de présentation restauré depuis
    /// le cache.
    /// </summary>
    internal required Beatmap Beatmap { get; init; }

    /// <summary>
    /// true si le résultat provient d'un enregistrement de cache
    /// valide, false s'il a été produit par une exécution du
    /// pipeline local (miss, péremption, ou ligne illisible).
    /// </summary>
    internal required bool WasCacheHit { get; init; }
}
