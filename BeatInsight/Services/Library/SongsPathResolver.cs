using System.IO;

namespace BeatInsight.Services.Library;

/// <summary>
/// Résout le dossier Songs à utiliser, avec la priorité suivante :
///
/// 1. Chemin manuel sauvegardé, s'il est encore valide.
/// 2. Chemin Songs fourni par tosu, s'il est valide.
/// 3. Aucun chemin disponible : à l'appelant de demander à
///    l'utilisateur (hors périmètre de cette classe).
///
/// AUCUNE DÉPENDANCE WPF
///
/// Ce type ne référence ni MainWindow, ni tosu directement : il reçoit
/// le chemin tosu en paramètre plutôt que d'aller le chercher, ce qui
/// le rend testable et réutilisable en dehors de l'UI.
///
/// UN CHEMIN MANUEL VALIDE N'EST JAMAIS ÉCRASÉ AUTOMATIQUEMENT
///
/// <see cref="Resolve"/> ne modifie jamais la préférence sauvegardée.
/// Tant qu'un chemin manuel reste valide, il prime systématiquement
/// sur celui fourni par tosu, y compris si tosu en indique un
/// différent. Seul un appel explicite à <see cref="SaveManualPath"/>
/// ou <see cref="ClearManualPath"/> change la préférence.
/// </summary>
internal sealed class SongsPathResolver
{
    private readonly ISongsPathPreferenceStore preferenceStore;

    /// <summary>
    /// Crée un resolver utilisant le stockage de préférence par
    /// défaut, sous %LOCALAPPDATA%\BeatInsight.
    /// </summary>
    internal SongsPathResolver()
        : this(new FileSongsPathPreferenceStore())
    {
    }

    /// <summary>
    /// Crée un resolver avec un stockage explicite.
    ///
    /// Permet aux tests d'utiliser un emplacement temporaire dédié
    /// sans jamais toucher à la préférence réelle de l'utilisateur.
    /// </summary>
    internal SongsPathResolver(ISongsPathPreferenceStore preferenceStore)
    {
        ArgumentNullException.ThrowIfNull(preferenceStore);

        this.preferenceStore = preferenceStore;
    }


    // ============================================================
    // RÉSOLUTION
    // ============================================================

    /// <summary>
    /// Retourne le dossier Songs à utiliser, ou null si aucune source
    /// n'est valide.
    /// </summary>
    /// <param name="tosuSongsPath">
    /// Chemin Songs actuellement rapporté par tosu, ou null/absent
    /// s'il n'est pas disponible.
    /// </param>
    internal string? Resolve(string? tosuSongsPath)
    {
        string? saved = preferenceStore.LoadManualPath();

        if (IsValidSongsFolder(saved))
        {
            return saved;
        }

        if (IsValidSongsFolder(tosuSongsPath))
        {
            return tosuSongsPath;
        }

        return null;
    }


    // ============================================================
    // PRÉFÉRENCE MANUELLE
    // ============================================================

    /// <summary>
    /// Sauvegarde un chemin choisi manuellement par l'utilisateur.
    ///
    /// Ce chemin primera sur celui fourni par tosu tant qu'il reste
    /// valide.
    /// </summary>
    internal void SaveManualPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        preferenceStore.SaveManualPath(path);
    }

    /// <summary>
    /// Supprime la préférence manuelle. Un futur appel à
    /// <see cref="Resolve"/> retombera sur le chemin tosu, s'il est
    /// valide.
    /// </summary>
    internal void ClearManualPath()
    {
        preferenceStore.ClearManualPath();
    }


    // ============================================================
    // VALIDATION
    //
    // Volontairement minimale : seule l'existence du dossier est
    // vérifiée. Exiger un contenu particulier (présence de maps,
    // structure interne) serait une heuristique fragile et hors
    // périmètre de cette phase, qui ne fait aucun scan.
    // ============================================================

    private static bool IsValidSongsFolder(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
               && Directory.Exists(path);
    }
}
