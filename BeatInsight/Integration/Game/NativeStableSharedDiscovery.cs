namespace BeatInsight.Integration.Game;

/// <summary>
/// V3.14-COM-KEY-05 : instantané en lecture seule de la découverte osu!stable déjà effectuée par
/// <see cref="NativeStableGameProvider"/> (signatures status/playTime et conteneur du ruleset).
/// Permet à un consommateur (canal precise) de réutiliser cette découverte au lieu de refaire ses
/// propres scans. Ne publie que des valeurs validées, toutes rattachées à une même génération de
/// processus. Un conteneur disponible n'implique pas qu'un ruleset ou un KeyCounter soit valide :
/// le consommateur doit relire et valider la chaîne mobile à chaque échantillon.
/// </summary>
internal sealed record NativeStableSharedDiscovery(
    string ProcessIdentity,
    int ProcessId,
    DateTimeOffset ProcessStartIdentity,
    nuint StatusValueAddress,
    nuint PlayTimeValueAddress,
    nuint ContainerAddress,
    NativeStableGameplayStatsDiscoveryState ContainerDiscoveryState,
    DateTimeOffset ContainerValidatedAtUtc,
    int ContainerDiscoveryGeneration);

internal interface INativeStableSharedDiscoveryProvider
{
    /// <summary>Null tant que la découverte n'est pas complète et cohérente pour un même processus.</summary>
    NativeStableSharedDiscovery? TryGetSharedDiscovery();

    /// <summary>
    /// V3.14-COM-PP-03 : adresse du statut seule (sans conteneur), dès que les signatures status/playTime
    /// sont découvertes pour un processus, y compris avant toute partie. Aucun scan. Null si inconnue.
    /// </summary>
    NativeStableSharedStatusDiscovery? TryGetSharedStatusDiscovery() => null;
}

/// <summary>
/// V3.14-COM-PP-03 : adresse du statut osu!stable déjà découverte par le provider de production, rattachée
/// à son processus. Le consommateur relit le statut lui-même et n'en déduit rien d'autre.
/// </summary>
internal sealed record NativeStableSharedStatusDiscovery(
    string ProcessIdentity,
    int ProcessId,
    DateTimeOffset ProcessStartIdentity,
    nuint StatusValueAddress);
