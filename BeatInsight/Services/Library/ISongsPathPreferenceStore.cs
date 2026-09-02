namespace BeatInsight.Services.Library;

/// <summary>
/// Stockage de la préférence de chemin Songs choisi manuellement.
///
/// Abstrait uniquement pour permettre aux tests d'utiliser un
/// emplacement temporaire : en dehors des tests, seule
/// <see cref="FileSongsPathPreferenceStore"/> est utilisée.
/// </summary>
internal interface ISongsPathPreferenceStore
{
    /// <summary>
    /// Charge le chemin sauvegardé, ou null si aucun n'existe.
    ///
    /// Ne valide pas le chemin : c'est la responsabilité de l'appelant
    /// (<see cref="SongsPathResolver"/>).
    /// </summary>
    string? LoadManualPath();

    /// <summary>Sauvegarde le chemin choisi manuellement.</summary>
    void SaveManualPath(string path);

    /// <summary>Supprime la préférence sauvegardée, si elle existe.</summary>
    void ClearManualPath();
}
