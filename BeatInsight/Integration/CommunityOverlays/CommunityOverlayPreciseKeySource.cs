using System.Diagnostics;
using System.Globalization;
using BeatInsight.Diagnostics.NativeStable;
using BeatInsight.Integration.Game;
using BeatInsight.Integration.KeyOverlay;

namespace BeatInsight.Integration.CommunityOverlays;

/// <summary>
/// Source des touches réelles pour le canal precise (V3.14-COM-KEY-04, expérimental, opt-in).
/// Réutilise NativeStableKeyOverlayReader (COM-KEY-03) sans modifier aucun reader existant :
/// <list type="bullet">
/// <item>osu!stable uniquement (ClientName de GameStateService), osu!standard uniquement (contrôle du lecteur) ;</item>
/// <item>conteneur du ruleset repris du diagnostic du reader de production (aucun nouveau scan de cette signature) ;</item>
/// <item>handle propre en PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, identité vérifiée contre GameStateService ;</item>
/// <item>status / playTime : NativeStableLiveStateReader existant (ses signatures, une découverte par processus) ;</item>
/// <item>identité de beatmap : GameStateService.Current (chemin du .osu, à défaut l'ID), si même processus.</item>
/// </list>
/// Toute indisponibilité produit un échantillon Unknown ; aucune valeur n'est jamais inventée.
/// <see cref="Read"/> n'est appelé que par le thread de la boucle (pas de concurrence). L'attachement
/// (lecture du diagnostic de production, découverte des signatures status/playTime, énumération des
/// régions) est coûteux : il s'exécute hors de la boucle, pour ne jamais retarder les ticks à 100 Hz.
/// </summary>
internal sealed class CommunityOverlayPreciseKeySource : IDisposable
{
    private const string StableClientName = "osu!stable";
    private static readonly TimeSpan AttachRetry = TimeSpan.FromSeconds(1);

    private readonly GameStateService gameState;
    private readonly Action<string>? log;
    private readonly object gate = new();
    private Session? session;
    private Task<(Session? Session, string Reason, string? Identity)>? attaching;
    private long nextAttachTimestamp;
    private bool disposed;

    public CommunityOverlayPreciseKeySource(GameStateService gameState, Action<string>? log = null)
    {
        this.gameState = gameState;
        this.log = log;
    }

    public int Attachments { get; private set; }

    // COM-KEY-05 : chronologie d'attachement (jalons, relatifs au démarrage de la source).
    private readonly Stopwatch since = Stopwatch.StartNew();
    private string? milestoneIdentity;
    private bool awaitingFirstValid;
    private string? lastGameStateNote;

    private void Milestone(string text) => log?.Invoke($"COMMUNITY OVERLAY PRECISE | t+{since.Elapsed.TotalSeconds:0.000} s | {text}");

    public string? LastAttachFailure { get; private set; }

    public KeyOverlaySample Read()
    {
        lock (gate)
        {
            long now = Stopwatch.GetTimestamp();
            if (disposed)
                return Unknown(KeyOverlayReadFailureReason.Disposed, now);

            GameStateSnapshot game = gameState.Current;
            bool stable = game.IsAvailable &&
                string.Equals(game.ClientName, StableClientName, StringComparison.Ordinal) &&
                game.ProcessIdentity is not null;
            string gameNote = $"{game.Availability}/{game.ClientName ?? "-"}";
            if (!string.Equals(gameNote, lastGameStateNote, StringComparison.Ordinal))
            {
                lastGameStateNote = gameNote;
                Milestone($"GameStateService : {gameNote} ({game.DiagnosticReason ?? "ok"})");
            }
            if (stable && !string.Equals(milestoneIdentity, game.ProcessIdentity, StringComparison.Ordinal))
            {
                milestoneIdentity = game.ProcessIdentity;
                Milestone($"processus disponible : {game.ProcessIdentity}");
            }
            if (!stable)
            {
                Close("client osu!stable indisponible");
                return Unknown(KeyOverlayReadFailureReason.SourceUnavailable, now, game.ProcessIdentity);
            }
            if (session is not null && !string.Equals(session.Identity, game.ProcessIdentity, StringComparison.Ordinal))
                Close("identité de processus changée");

            if (session is null)
            {
                if (attaching is { IsCompleted: true } done)
                {
                    attaching = null;
                    (Session? opened, string reason, string? identity) = done.Result;
                    if (opened is not null && string.Equals(identity, game.ProcessIdentity, StringComparison.Ordinal))
                    {
                        session = opened;
                        Attachments++;
                        LastAttachFailure = null;
                        awaitingFirstValid = true;
                        Milestone("lecteur precise attaché");
                        log?.Invoke($"COMMUNITY OVERLAY PRECISE | source attachée (conteneur 0x{session.Container:X})");
                    }
                    else
                    {
                        opened?.Dispose();
                        if (!string.Equals(LastAttachFailure, reason, StringComparison.Ordinal))
                            Milestone($"source non attachée : {reason}");
                        LastAttachFailure = reason;
                    }
                }
                if (session is null)
                {
                    if (attaching is null && now >= nextAttachTimestamp)
                    {
                        nextAttachTimestamp = now + (long)(AttachRetry.TotalSeconds * Stopwatch.Frequency);
                        string expected = game.ProcessIdentity!;
                        attaching = Task.Run(() =>
                        {
                            Session? opened = Session.TryOpen(gameState, expected, out string reason);
                            return (opened, reason, (string?)expected);
                        });
                    }
                    return Unknown(KeyOverlayReadFailureReason.SourceUnavailable, now, game.ProcessIdentity);
                }
            }

            KeyOverlaySample sample = session.Reader.Read();
            if (awaitingFirstValid && sample.IsValid)
            {
                awaitingFirstValid = false;
                Milestone("premier échantillon KeyCounter valide");
            }
            if (sample.Reason is KeyOverlayReadFailureReason.ProcessExited or KeyOverlayReadFailureReason.ProcessIdentityMismatch)
                Close(sample.Reason.ToString());
            return sample;
        }
    }

    public void Dispose()
    {
        Task<(Session? Session, string Reason, string? Identity)>? pending;
        lock (gate)
        {
            disposed = true;
            Close(null);
            pending = attaching;
            attaching = null;
        }
        // Un attachement en cours se termine sans être utilisé ; sa session éventuelle est fermée.
        if (pending is not null)
        {
            try
            {
                if (pending.Wait(TimeSpan.FromSeconds(5))) pending.Result.Session?.Dispose();
            }
            catch (AggregateException) { }
        }
    }

    private void Close(string? reason)
    {
        if (session is null) return;
        session.Dispose();
        session = null;
        if (reason is not null) log?.Invoke($"COMMUNITY OVERLAY PRECISE | source fermée : {reason}");
    }

    private static KeyOverlaySample Unknown(KeyOverlayReadFailureReason reason, long now, string? identity = null) =>
        new(KeyOverlayReadStatus.Unknown, reason, identity, null, null, null, null, now, 0, 0);

    /// <summary>Attachement à un processus osu!stable précis ; fermé dès que l'identité change.</summary>
    private sealed class Session : IDisposable
    {
        private readonly NativeStableGameProcessContext context;
        private readonly NativeStableProcessMemory memory;
        private readonly NativeStableLiveStateReader live;
        private readonly GameStateService gameState;

        private Session(GameStateService gameState, NativeStableGameProcessContext context, NativeStableProcessMemory memory,
            string identity, nuint container, INativeStableLivePatternDiscovery liveDiscovery)
        {
            live = new NativeStableLiveStateReader(liveDiscovery);
            this.gameState = gameState;
            this.context = context;
            this.memory = memory;
            Identity = identity;
            Container = container;
            Reader = new NativeStableKeyOverlayReader(identity, container, memory, new Regions(memory, context.Regions),
                new Game(this));
        }

        public string Identity { get; }

        public nuint Container { get; }

        public NativeStableKeyOverlayReader Reader { get; }

        public static Session? TryOpen(GameStateService gameState, string expectedIdentity, out string reason)
        {
            // COM-KEY-05 : chemin principal = découverte osu!stable partagée par le provider de production
            // (signatures status/playTime ET conteneur), sans aucun scan ici.
            if (gameState.HasSharedStableDiscoveryProvider)
            {
                NativeStableSharedDiscovery? shared = gameState.TryGetSharedStableDiscovery();
                if (shared is null)
                {
                    reason = "découverte partagée pas encore disponible (conteneur résolu en gameplay par le provider)";
                    return null;
                }
                if (!string.Equals(shared.ProcessIdentity, expectedIdentity, StringComparison.Ordinal))
                {
                    reason = "découverte partagée d'un autre processus";
                    return null;
                }
                return Open(gameState, expectedIdentity, shared.ContainerAddress,
                    new SharedLiveDiscovery(shared), shared.ProcessId, shared.ProcessStartIdentity, out reason);
            }

            // Repli COM-KEY-04 (aucun provider ne partage sa découverte) : conteneur lu dans le diagnostic
            // de production, signatures status/playTime redécouvertes localement.
            _ = gameState.ReadLiveGameplayStats(out LiveGameplayStatsDiagnostics diagnostics);
            if (!TryParseAddress(diagnostics.ContainerAddress, out nuint container))
            {
                reason = $"conteneur non résolu par le reader de production ({diagnostics.FailureReason}, " +
                         $"découverte {diagnostics.DiscoveryState}, tentatives {diagnostics.DiscoveryAttempts}, " +
                         $"échec {diagnostics.LastDiscoveryFailure})";
                return null;
            }
            return Open(gameState, expectedIdentity, container, new NativeStableLivePatternDiscovery(), null, null, out reason);
        }

        private static Session? Open(
            GameStateService gameState, string expectedIdentity, nuint container, INativeStableLivePatternDiscovery liveDiscovery,
            int? expectedProcessId, DateTimeOffset? expectedStart, out string reason)
        {
            if (!string.Equals(gameState.Current.ProcessIdentity, expectedIdentity, StringComparison.Ordinal))
            {
                reason = "identité changée pendant l'attachement";
                return null;
            }

            NativeStableProcessCandidate? candidate = NativeStableProcessDiscovery.Discover().UniqueCandidate;
            if (candidate?.ExecutablePath is null)
            {
                reason = "processus osu!stable introuvable ou ambigu";
                return null;
            }
            if (expectedProcessId is int pid && candidate.ProcessId != pid)
            {
                reason = "le processus trouvé n'est pas celui de la découverte partagée";
                return null;
            }
            DateTimeOffset start;
            try
            {
                using Process process = Process.GetProcessById(candidate.ProcessId);
                start = process.StartTime.ToUniversalTime();
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                reason = "processus disparu";
                return null;
            }
            // Même format que NativeStableGameProvider.FormatProcessIdentity.
            string identity = string.Create(CultureInfo.InvariantCulture, $"{candidate.ProcessId}:{start.UtcDateTime:O}");
            if (!string.Equals(identity, expectedIdentity, StringComparison.Ordinal) ||
                (expectedStart is DateTimeOffset expected && expected != start))
            {
                reason = "le processus trouvé ne correspond pas à l'identité attendue";
                return null;
            }

            NativeStableAttachResult attach = NativeStableProcessMemory.TryAttach(candidate);
            if (attach.Memory is null)
            {
                reason = $"attachement refusé ({attach.Status})";
                return null;
            }
            var context = new NativeStableGameProcessContext(
                candidate.ProcessId, start, candidate.ExecutablePath, attach.Memory,
                attach.Memory.EnumerateReadableRegions(), []);
            var session = new Session(gameState, context, attach.Memory, identity, container, liveDiscovery);
            if (!session.live.EnsureDiscovered(context))
            {
                session.Dispose();
                reason = "signatures status/playTime indisponibles";
                return null;
            }
            reason = "ok";
            return session;
        }

        /// <summary>
        /// Adresses status/playTime reprises de la découverte partagée : aucun scan. Revalidées par une
        /// lecture complète, et refusées si le contexte n'est pas celui du même processus.
        /// </summary>
        private sealed class SharedLiveDiscovery(NativeStableSharedDiscovery shared) : INativeStableLivePatternDiscovery
        {
            public NativeStableLivePatternAddresses? Discover(NativeStableGameProcessContext context)
            {
                if (context.ProcessId != shared.ProcessId || context.ProcessStartIdentity != shared.ProcessStartIdentity)
                    return null;
                if (!NativeStableLivePatternDiscovery.TryReadInt32(context.Memory, shared.StatusValueAddress, out _) ||
                    !NativeStableLivePatternDiscovery.TryReadInt32(context.Memory, shared.PlayTimeValueAddress, out _))
                    return null;
                return new NativeStableLivePatternAddresses(shared.StatusValueAddress, shared.PlayTimeValueAddress, 0);
            }
        }

        /// <summary>Le lecteur d'abord (attend une lecture en cours), puis le handle.</summary>
        public void Dispose()
        {
            Reader.Dispose();
            context.Dispose();
        }

        internal static bool TryParseAddress(string? text, out nuint address)
        {
            address = 0;
            if (text is null || !text.StartsWith("0x", StringComparison.Ordinal)) return false;
            if (!ulong.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong value))
                return false;
            if (value == 0 || value > uint.MaxValue) return false;
            address = (nuint)value;
            return true;
        }

        private sealed class Regions(NativeStableProcessMemory memory, IReadOnlyList<NativeStableMemoryRegion> initial)
            : IKeyOverlayRegionSource
        {
            private static readonly long MinRefreshTicks = Stopwatch.Frequency; // 1 s
            private long lastRefresh = Stopwatch.GetTimestamp();

            public RegionIndex Current { get; private set; } = new(initial);

            public bool TryRefresh()
            {
                long now = Stopwatch.GetTimestamp();
                if (now - lastRefresh < MinRefreshTicks || !memory.IsAlive) return false;
                lastRefresh = now;
                Current = new RegionIndex(memory.EnumerateReadableRegions());
                return true;
            }
        }

        private sealed class Game(Session owner) : IKeyOverlayGameSource
        {
            // Le handle maintient l'objet processus : un PID réutilisé ne peut pas être confondu.
            public string? CurrentProcessIdentity() => owner.memory.IsAlive ? owner.Identity : null;

            public int? ReadStatus() => owner.live.ReadStatus(owner.context);

            public int? ReadPlayTime() => owner.live.ReadPlayTime(owner.context);

            public string? ReadBeatmapIdentity()
            {
                GameStateSnapshot game = owner.gameState.Current;
                if (!string.Equals(game.ProcessIdentity, owner.Identity, StringComparison.Ordinal))
                    return null;
                return game.CurrentBeatmapPath ?? (game.CurrentBeatmapId is { } id ? $"id:{id}" : null);
            }
        }
    }
}
