using System.Diagnostics;
using BeatInsight.Diagnostics.NativeStable;

namespace BeatInsight.Integration.KeyOverlay;

/// <summary>État du jeu hors KeyCounter (statut, temps, beatmap, vie du processus).</summary>
internal interface IKeyOverlayGameSource
{
    /// <summary>Identité du processus attaché si encore vivant ; null s'il est terminé.</summary>
    string? CurrentProcessIdentity();

    int? ReadStatus();

    int? ReadPlayTime();

    /// <summary>Identité de beatmap si connue ; null = inconnue (non utilisée comme signal).</summary>
    string? ReadBeatmapIdentity();
}

/// <summary>Instantané des régions lisibles, rafraîchissable à débit limité.</summary>
internal interface IKeyOverlayRegionSource
{
    RegionIndex Current { get; }

    /// <summary>Tente un rafraîchissement ; false si limité par le débit ou impossible.</summary>
    bool TryRefresh();
}

/// <summary>
/// Lecteur KeyCounter osu!stable isolé, déterministe et fail-closed.
/// Chaque <see cref="Read"/> relit toute la chaîne depuis le conteneur résolu par BeatInsight ;
/// aucune adresse d'objet n'est conservée entre deux échantillons. Toute anomalie produit
/// un échantillon Unknown avec une raison explicite et sans touches.
/// Thread-safe : les lectures sont sérialisées ; après <see cref="Dispose"/>, plus aucun accès natif.
/// </summary>
internal sealed class NativeStableKeyOverlayReader : IDisposable
{
    private readonly object gate = new();
    private readonly string expectedProcessIdentity;
    private readonly nuint container;
    private readonly INativeStableMemoryReader memory;
    private readonly IKeyOverlayRegionSource regions;
    private readonly IKeyOverlayGameSource game;
    private readonly Func<long> timestamp;
    private readonly KeyOverlayPlayTracker tracker = new();
    private readonly CountingReader counting;
    private bool disposed;

    public NativeStableKeyOverlayReader(
        string expectedProcessIdentity,
        nuint container,
        INativeStableMemoryReader memory,
        IKeyOverlayRegionSource regions,
        IKeyOverlayGameSource game,
        Func<long>? timestamp = null)
    {
        this.expectedProcessIdentity = expectedProcessIdentity;
        this.container = container;
        this.memory = memory;
        this.regions = regions;
        this.game = game;
        this.timestamp = timestamp ?? Stopwatch.GetTimestamp;
        counting = new CountingReader(memory);
    }

    public long PlaySequence { get { lock (gate) return tracker.PlaySequence; } }

    public KeyOverlaySample Read()
    {
        lock (gate)
        {
            long observedAt = timestamp();
            if (disposed)
                return Sample(KeyOverlayReadFailureReason.Disposed, null, null, null, null, null, observedAt, 0, 0);
            if (!memory.Is32Bit)
                return Sample(KeyOverlayReadFailureReason.UnsupportedProcessArchitecture, null, null, null, null, null, observedAt, 0, 0);

            counting.Reads = 0;
            string? identity = game.CurrentProcessIdentity();
            if (identity is null)
                return Sample(KeyOverlayReadFailureReason.ProcessExited, null, null, null, null, null, observedAt, 0, 0);
            if (!string.Equals(identity, expectedProcessIdentity, StringComparison.Ordinal))
                return Sample(KeyOverlayReadFailureReason.ProcessIdentityMismatch, identity, null, null, null, null, observedAt, 0, 0);

            int? status = game.ReadStatus();
            int? playTime = status == KeyOverlayPlayTracker.Gameplay ? game.ReadPlayTime() : null;
            string? beatmap = status == KeyOverlayPlayTracker.Gameplay ? game.ReadBeatmapIdentity() : null;

            KeyCounterChainRead chain = default;
            if (status == KeyOverlayPlayTracker.Gameplay && playTime is not null)
            {
                chain = KeyCounterChain.Read(counting, regions.Current, container);
                if (IsRegionFailure(chain.Failure) && regions.TryRefresh())
                    chain = KeyCounterChain.Read(counting, regions.Current, container);
            }

            var observation = new KeyOverlayObservation(
                identity, beatmap, status, playTime,
                chain.Failure, chain.Ok ? chain.Keys : null);
            var (resultStatus, reason, play, trackedBeatmap) = tracker.Observe(observation);

            KeyOverlayKeys? keys = resultStatus == KeyOverlayReadStatus.Valid ? chain.Keys : null;
            return new KeyOverlaySample(
                resultStatus, reason, identity, trackedBeatmap, play, playTime, keys,
                observedAt, checked((int)counting.Reads), chain.Addresses.Ruleset);
        }
    }

    public void Dispose()
    {
        // Attend la fin d'une lecture en cours : après le retour, plus aucun accès natif.
        lock (gate)
        {
            disposed = true;
        }
    }

    private static bool IsRegionFailure(KeyOverlayReadFailureReason reason) => reason is
        KeyOverlayReadFailureReason.RulesetUnreadable or
        KeyOverlayReadFailureReason.ModeChainUnreadable or
        KeyOverlayReadFailureReason.KeyOverlayUnreadable or
        KeyOverlayReadFailureReason.KeyOverlayTypeInvalid or
        KeyOverlayReadFailureReason.ListUnreadable or
        KeyOverlayReadFailureReason.ListTypeInvalid or
        KeyOverlayReadFailureReason.ItemsUnreadable or
        KeyOverlayReadFailureReason.ItemsTypeInvalid or
        KeyOverlayReadFailureReason.ElementUnreadable or
        KeyOverlayReadFailureReason.ElementTypeInvalid;

    private KeyOverlaySample Sample(
        KeyOverlayReadFailureReason reason, string? identity, string? beatmap, string? play,
        int? playTime, KeyOverlayKeys? keys, long observedAt, int reads, nuint ruleset) =>
        new(KeyOverlayReadStatus.Unknown, reason, identity, beatmap, play, playTime, keys, observedAt, reads, ruleset);

    /// <summary>Compte les ReadProcessMemory de la chaîne (diagnostic).</summary>
    private sealed class CountingReader(INativeStableMemoryReader inner) : INativeStableMemoryReader
    {
        public long Reads;

        public bool Is32Bit => inner.Is32Bit;

        public NativeStableMemoryReadResult ReadBytes(nuint address, int count)
        {
            Reads++;
            return inner.ReadBytes(address, count);
        }

        public bool TryReadPointer(nuint address, out nuint value)
        {
            Reads++;
            return inner.TryReadPointer(address, out value);
        }
    }
}
