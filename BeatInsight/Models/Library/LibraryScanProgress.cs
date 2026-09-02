namespace BeatInsight.Models.Library;

/// <summary>
/// Instantané de la progression d'un scan de bibliothèque, rapporté
/// après le traitement de chaque fichier.
///
/// Ce type ne réalise aucun calcul métier : il transporte des
/// compteurs déjà maintenus par BeatmapLibraryScanner.
/// </summary>
internal sealed class LibraryScanProgress
{
    /// <summary>
    /// Nombre total de fichiers .osu trouvés, connu avant le début du
    /// traitement.
    /// </summary>
    internal required int TotalFiles { get; init; }

    /// <summary>
    /// Nombre de fichiers dont le traitement est terminé (analysés,
    /// ignorés parce qu'à jour, ou en échec).
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
    /// Nombre de fichiers dont l'analyse a échoué. Un échec n'arrête
    /// pas le scan.
    /// </summary>
    internal required int FailedFiles { get; init; }

    /// <summary>
    /// Fichier en cours de traitement au moment de ce rapport, ou
    /// null une fois le scan terminé.
    /// </summary>
    internal string? CurrentFile { get; init; }

    /// <summary>
    /// Progression en pourcentage, de 0 à 100.
    ///
    /// Vaut 0 lorsque <see cref="TotalFiles"/> est nul, afin d'éviter
    /// une division par zéro sur un dossier vide.
    /// </summary>
    internal required double Percent { get; init; }
}
