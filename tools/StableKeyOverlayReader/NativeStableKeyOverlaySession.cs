using System.Diagnostics;
using System.Globalization;
using BeatInsight.Diagnostics.NativeStable;
using BeatInsight.Integration.Game;

namespace BeatInsight.Integration.KeyOverlay;

/// <summary>
/// Adaptateur réel (prototype isolé) : attache osu!stable en lecture seule et assemble le lecteur
/// à partir des composants BeatInsight existants, non modifiés :
/// NativeStableProcessDiscovery / NativeStableProcessMemory (PROCESS_QUERY_INFORMATION | PROCESS_VM_READ),
/// NativeStableLiveStateReader (status, playTime), NativeStableGameplayStatsReader (conteneur du ruleset).
/// Aucun nouveau scan AOB : la signature du conteneur est celle du reader de production.
/// Identité de beatmap : non disponible dans ce périmètre isolé (null) — le changement de map est
/// détecté par le passage hors gameplay et le recul du temps.
/// </summary>
internal sealed class NativeStableKeyOverlaySession : IDisposable
{
    private readonly NativeStableGameProcessContext context;
    private readonly NativeStableProcessMemory memory;
    private int disposed;

    private NativeStableKeyOverlaySession(
        NativeStableGameProcessContext context, NativeStableProcessMemory memory, string identity, nuint container)
    {
        this.context = context;
        this.memory = memory;
        ProcessIdentity = identity;
        Container = container;
        Regions = new LiveRegionSource(memory, context.Regions);
        Game = new LiveGameSource(this, new NativeStableLiveStateReader());
        Reader = new NativeStableKeyOverlayReader(identity, container, memory, Regions, Game);
    }

    public string ProcessIdentity { get; }

    public nuint Container { get; }

    public NativeStableKeyOverlayReader Reader { get; }

    internal LiveRegionSource Regions { get; }

    internal LiveGameSource Game { get; }

    public static NativeStableKeyOverlaySession? TryOpen(out string reason)
    {
        NativeStableProcessDiscoveryResult discovery = NativeStableProcessDiscovery.Discover();
        NativeStableProcessCandidate? candidate = discovery.UniqueCandidate;
        if (candidate?.ExecutablePath is null)
        {
            reason = $"osu!stable introuvable ou ambigu ({discovery.Status})";
            return null;
        }
        DateTimeOffset start;
        try
        {
            using Process p = Process.GetProcessById(candidate.ProcessId);
            start = p.StartTime.ToUniversalTime();
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            reason = "Processus disparu pendant l'attachement";
            return null;
        }
        NativeStableAttachResult attach = NativeStableProcessMemory.TryAttach(candidate);
        if (attach.Memory is null)
        {
            reason = $"Attachement refusé ({attach.Status})";
            return null;
        }
        var context = new NativeStableGameProcessContext(
            candidate.ProcessId, start, candidate.ExecutablePath, attach.Memory,
            attach.Memory.EnumerateReadableRegions(), []);
        // Même format que NativeStableGameProvider.FormatProcessIdentity.
        string identity = string.Create(CultureInfo.InvariantCulture,
            $"{context.ProcessId}:{context.ProcessStartIdentity.UtcDateTime:O}");

        var stats = new NativeStableGameplayStatsReader();
        stats.ObserveProcessIdentity(identity);
        _ = stats.Read(context, identity);
        nuint container = stats.Diagnostics.ContainerAddress;
        if (container == 0)
        {
            context.Dispose();
            reason = $"Conteneur ruleset non résolu ({stats.Diagnostics.LastDiscoveryFailure})";
            return null;
        }
        var session = new NativeStableKeyOverlaySession(context, attach.Memory, identity, container);
        if (!session.Game.EnsureDiscovered())
        {
            session.Dispose();
            reason = "Signatures status/playTime introuvables";
            return null;
        }
        reason = "ok";
        return session;
    }

    /// <summary>Le lecteur est fermé d'abord (attend la lecture en cours), puis le handle.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Reader.Dispose();
        context.Dispose();
    }

    internal sealed class LiveRegionSource(NativeStableProcessMemory memory, IReadOnlyList<NativeStableMemoryRegion> initial)
        : IKeyOverlayRegionSource
    {
        private static readonly long MinRefreshTicks = Stopwatch.Frequency; // 1 s
        private long lastRefresh = Stopwatch.GetTimestamp();

        public RegionIndex Current { get; private set; } = new(initial);

        public int Refreshes { get; private set; }

        public bool TryRefresh()
        {
            long now = Stopwatch.GetTimestamp();
            if (now - lastRefresh < MinRefreshTicks || !memory.IsAlive) return false;
            lastRefresh = now;
            Current = new RegionIndex(memory.EnumerateReadableRegions());
            Refreshes++;
            return true;
        }
    }

    internal sealed class LiveGameSource(NativeStableKeyOverlaySession owner, NativeStableLiveStateReader live)
        : IKeyOverlayGameSource
    {
        public bool EnsureDiscovered() => live.EnsureDiscovered(owner.context);

        // Le handle maintient l'objet processus : un PID réutilisé ne peut pas être confondu.
        public string? CurrentProcessIdentity() => owner.memory.IsAlive ? owner.ProcessIdentity : null;

        public int? ReadStatus() => live.ReadStatus(owner.context);

        public int? ReadPlayTime() => live.ReadPlayTime(owner.context);

        public string? ReadBeatmapIdentity() => null;
    }
}
