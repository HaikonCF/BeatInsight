using BeatInsight.Diagnostics.NativeStable;

namespace BeatInsight.Integration.KeyOverlay;

/// <summary>Accès au processus courant pour le suivi des mods (abstrait pour les tests).</summary>
internal interface ISongSelectModsTarget
{
    /// <summary>Identité du processus attaché, null s'il est terminé ou absent.</summary>
    string? CurrentIdentity();

    INativeStableMemoryReader Memory { get; }

    /// <summary>Régions où chercher la signature (régions exécutables, ou toutes).</summary>
    IReadOnlyList<NativeStableMemoryRegion> SearchRegions();

    RegionIndex ReadableRegions();

    int? ReadStatus();
}

/// <summary>
/// Prototype COM-PP-02 : une découverte contrôlée par identité de processus (jamais par tick), reprise
/// bornée après échec (1 s, 2 s, puis 5 s), lecture d'un seul int32 par observation.
/// Changement d'identité (redémarrage d'osu!) → découverte refaite, rien n'est réutilisé.
/// </summary>
internal sealed class SongSelectModsTracker
{
    public static readonly TimeSpan MaxObservationAge = TimeSpan.FromSeconds(1);

    private readonly Func<DateTimeOffset> clock;
    private string? identity;
    private SongSelectModsDiscovery? discovery;
    private int failures;
    private DateTimeOffset nextAttempt;

    public SongSelectModsTracker(Func<DateTimeOffset>? clock = null) => this.clock = clock ?? (() => DateTimeOffset.UtcNow);

    public int Discoveries { get; private set; }

    public SongSelectModsDiscovery? LastDiscovery => discovery;

    public SongSelectModsObservation Observe(ISongSelectModsTarget target)
    {
        DateTimeOffset now = clock();
        string? current = target.CurrentIdentity();
        if (current is null)
        {
            Reset(null);
            return Unknown(null, null, now, SongSelectModsFailure.ProcessIdentityChanged);
        }
        if (!string.Equals(current, identity, StringComparison.Ordinal))
            Reset(current);

        if (discovery is not { Failure: SongSelectModsFailure.None })
        {
            if (now < nextAttempt)
                return Unknown(current, target.ReadStatus(), now, discovery?.Failure ?? SongSelectModsFailure.SignatureAbsent, discovery?.Duration);
            discovery = SongSelectModsProbe.Discover(target.Memory, target.SearchRegions(), target.ReadableRegions());
            Discoveries++;
            if (discovery.Failure != SongSelectModsFailure.None)
            {
                failures++;
                nextAttempt = now + (failures switch { 1 => TimeSpan.FromSeconds(1), 2 => TimeSpan.FromSeconds(2), _ => TimeSpan.FromSeconds(5) });
            }
        }
        return SongSelectModsProbe.Read(target.Memory, discovery, current, target.ReadStatus(), now);
    }

    /// <summary>Une observation n'est « actuelle » que si elle est Valid, récente et du processus courant.</summary>
    public static bool IsCurrent(SongSelectModsObservation o, string? currentIdentity, DateTimeOffset now) =>
        o.Validity == SongSelectModsValidity.Valid &&
        string.Equals(o.ProcessIdentity, currentIdentity, StringComparison.Ordinal) &&
        now - o.ObservedAtUtc <= MaxObservationAge;

    private void Reset(string? newIdentity)
    {
        identity = newIdentity;
        discovery = null;
        failures = 0;
        nextAttempt = default;
    }

    private static SongSelectModsObservation Unknown(string? id, int? status, DateTimeOffset now, SongSelectModsFailure failure, TimeSpan? duration = null) =>
        new(id, status, null, [], SongSelectModsValidity.Unknown, now, duration, failure);
}
