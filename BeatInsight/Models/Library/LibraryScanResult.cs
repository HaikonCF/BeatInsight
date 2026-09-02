namespace BeatInsight.Models.Library;

/// <summary>
/// Résultat final d'un scan de bibliothèque, terminé normalement ou
/// annulé.
///
/// En cas d'annulation, les compteurs reflètent le travail réellement
/// accompli avant l'arrêt : les lignes déjà persistées pendant le
/// scan restent en base, ce résultat ne les invalide pas.
/// </summary>
internal sealed class LibraryScanResult
{
    /// <summary>Nombre total de fichiers .osu trouvés.</summary>
    internal required int TotalFiles { get; init; }

    /// <summary>
    /// Nombre de fichiers dont le traitement a été mené à terme
    /// (analysés, ignorés, ou en échec) avant la fin ou l'annulation
    /// du scan.
    /// </summary>
    internal required int ProcessedFiles { get; init; }

    /// <summary>
    /// Nombre de fichiers ayant nécessité une analyse (miss ou entrée
    /// périmée).
    /// </summary>
    internal required int AnalyzedFiles { get; init; }

    /// <summary>
    /// Nombre de fichiers ignorés parce que déjà à jour dans le
    /// cache.
    /// </summary>
    internal required int SkippedUpToDateFiles { get; init; }

    /// <summary>
    /// Nombre de fichiers ignorés parce que leur mode osu! n'est pas
    /// pris en charge par BeatInsight (tout mode différent de 0).
    /// </summary>
    internal required int SkippedUnsupportedFiles { get; init; }

    /// <summary>
    /// Nombre de fichiers dont l'analyse a échoué.
    /// </summary>
    internal required int FailedFiles { get; init; }

    /// <summary>
    /// true si le scan s'est arrêté sur demande d'annulation avant
    /// d'avoir traité tous les fichiers.
    /// </summary>
    internal required bool WasCancelled { get; init; }

    /// <summary>Durée totale du scan.</summary>
    internal required TimeSpan Elapsed { get; init; }
}
