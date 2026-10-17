using System.Diagnostics;
using System.Globalization;
using BeatInsight.Diagnostics.NativeStable;
using BeatInsight.Integration.Game;
using BeatInsight.Integration.KeyOverlay;

namespace BeatInsight.Integration.SongSelect;

/// <summary>Accès au processus osu!stable courant, abstrait pour les tests.</summary>
internal interface ISongSelectModsHost
{
    /// <summary>Identité du processus osu!stable courant (GameStateService), null sinon.</summary>
    string? CurrentStableIdentity();

    /// <summary>Adresse du statut déjà découverte pour CE processus (découverte partagée), null sinon.</summary>
    nuint? StatusAddress(string processIdentity);

    /// <summary>Ouvre un accès en lecture seule au processus de cette identité. Appelé hors du thread appelant.</summary>
    ISongSelectModsProcess? TryOpen(string processIdentity, out string reason);
}

/// <summary>Handle en lecture seule sur un processus précis.</summary>
internal interface ISongSelectModsProcess : IDisposable
{
    bool IsAlive { get; }

    INativeStableMemoryReader Memory { get; }

    IReadOnlyList<NativeStableMemoryRegion> EnumerateReadableRegions();
}

/// <summary>Résultat d'une découverte de la variable globale des mods de menu.</summary>
internal sealed record SongSelectModsDiscovery(
    SongSelectModsFailure Failure,
    nuint MatchAddress,
    nuint GlobalAddress,
    int MatchCount,
    TimeSpan Duration);

/// <summary>
/// V3.14-COM-PP-03 : mods sélectionnés dans la sélection des maps d'osu!stable, en lecture mémoire seule
/// (aucun hook, aucune injection, aucune écriture). Chaîne validée en COM-PP-02 (tosu a4da331,
/// memory/stable.ts:161-164 et :797) :
/// <code>
/// match   = 'C8 FF ?? ?? ?? ?? ?? 81 0D ?? ?? ?? ?? ?? 08 00 00' (exactement une occurrence, régions exécutables)
/// operand = match + 0x9    → « OR dword ptr [global], 0x800 »
/// global  = uint32 [operand]
/// mods    = int32 [global] (en clair)
/// </code>
/// <list type="bullet">
/// <item>Découverte asynchrone (tâche de fond), au plus une active par identité de processus, reprise bornée
/// (1 s, 2 s, puis 5 s) tant que le code JIT est absent. Jamais de recherche dans <see cref="Observe"/>.</item>
/// <item>Adresse globale conservée seulement pour la même identité ; le masque est relu à chaque observation.</item>
/// <item>Statut lu à l'adresse partagée par le provider de production (COM-KEY-05) : aucune nouvelle recherche
/// status/playTime. <see cref="SongSelectModsValidity.Valid"/> uniquement en sélection confirmée (statut 5).</item>
/// </list>
/// </summary>
internal sealed class NativeStableSongSelectModsReader : IDisposable
{
    public const string SignatureText = "C8 FF ?? ?? ?? ?? ?? 81 0D ?? ?? ?? ?? ?? 08 00 00";
    public const int OperandOffset = 0x9;

    private static readonly BytePattern Signature = BytePattern.Parse(SignatureText);

    private readonly ISongSelectModsHost host;
    private readonly Func<DateTimeOffset> clock;
    private readonly Action<string>? log;
    private readonly Func<Func<Attempt>, Task<Attempt>> runInBackground;
    private readonly object gate = new();
    private string? identity;
    private Session? session;
    private Task<Attempt>? pending;
    private int failures;
    private DateTimeOffset nextAttempt;
    private SongSelectModsFailure lastFailure = SongSelectModsFailure.Discovering;
    private bool disposed;

    public NativeStableSongSelectModsReader(
        ISongSelectModsHost host,
        Func<DateTimeOffset>? clock = null,
        Action<string>? log = null,
        Func<Func<Attempt>, Task<Attempt>>? runInBackground = null)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        this.log = log;
        this.runInBackground = runInBackground ?? (work => Task.Run(work));
    }

    /// <summary>Découvertes lancées (toutes identités confondues).</summary>
    public int Discoveries { get; private set; }

    public int DiscoveryFailures { get; private set; }

    /// <summary>Lectures du masque (une par observation utile).</summary>
    public long MaskReads { get; private set; }

    public SongSelectModsDiscovery? LastDiscovery { get; private set; }

    /// <summary>
    /// Une observation : au plus deux lectures de 4 octets (statut et masque). Ne bloque jamais sur une découverte :
    /// tant qu'elle n'a pas abouti, l'observation est <see cref="SongSelectModsValidity.Unknown"/>.
    /// </summary>
    public SongSelectModsSnapshot Observe()
    {
        lock (gate)
        {
            DateTimeOffset now = clock();
            if (disposed)
                return SongSelectModsSnapshot.Unknown(null, null, now, SongSelectModsFailure.Disposed);

            string? current = host.CurrentStableIdentity();
            if (current is null)
            {
                Reset(null);
                return SongSelectModsSnapshot.Unknown(null, null, now, SongSelectModsFailure.SourceUnavailable);
            }
            if (!string.Equals(current, identity, StringComparison.Ordinal))
                Reset(current);

            if (session is null && !TryCompleteDiscovery(current, now))
                return SongSelectModsSnapshot.Unknown(current, null, now, lastFailure);

            if (!session!.Process.IsAlive)
            {
                Reset(current);
                return SongSelectModsSnapshot.Unknown(current, null, now, SongSelectModsFailure.ProcessExited);
            }

            int? status = ReadStatus(current);
            return Read(session.Process.Memory, session.GlobalAddress, current, status, now);
        }
    }

    private bool TryCompleteDiscovery(string current, DateTimeOffset now)
    {
        if (pending is { IsCompleted: true } done)
        {
            pending = null;
            Attempt attempt = done.IsCompletedSuccessfully ? done.Result : Attempt.Failed(current, SongSelectModsFailure.SourceUnavailable, null, "exception");
            LastDiscovery = attempt.Discovery ?? LastDiscovery;
            if (!string.Equals(attempt.Identity, current, StringComparison.Ordinal))
            {
                // Découverte d'un processus précédent (redémarrage pendant la recherche) : jamais utilisée,
                // sans pénalité de reprise pour le nouveau processus.
                attempt.Process?.Dispose();
            }
            else if (attempt.Process is not null && attempt.Discovery is { Failure: SongSelectModsFailure.None } found)
            {
                session = new Session(attempt.Process, found.GlobalAddress);
                failures = 0;
                lastFailure = SongSelectModsFailure.None;
                log?.Invoke(string.Create(CultureInfo.InvariantCulture,
                    $"SONGSELECT MODS | variable des mods trouvée en {found.Duration.TotalMilliseconds:0} ms (global 0x{found.GlobalAddress:X})"));
                return true;
            }
            else
            {
                attempt.Process?.Dispose();
                DiscoveryFailures++;
                failures++;
                lastFailure = attempt.Discovery?.Failure ?? attempt.Failure;
                nextAttempt = now + (failures switch { 1 => TimeSpan.FromSeconds(1), 2 => TimeSpan.FromSeconds(2), _ => TimeSpan.FromSeconds(5) });
                if (failures is 1 or 3)
                    log?.Invoke($"SONGSELECT MODS | découverte non aboutie ({lastFailure} {attempt.Note}), reprise dans {(nextAttempt - now).TotalSeconds:0} s");
            }
        }

        if (pending is null && now >= nextAttempt)
        {
            Discoveries++;
            lastFailure = SongSelectModsFailure.Discovering;
            string expected = current;
            pending = runInBackground(() => Discover(host, expected));
            // Une découverte synchrone (tests) se termine immédiatement : on la consomme dans la même observation.
            if (pending.IsCompleted)
                return TryCompleteDiscovery(current, now);
        }
        return false;
    }

    private int? ReadStatus(string current)
    {
        if (host.StatusAddress(current) is not { } address)
            return null;
        NativeStableMemoryReadResult read = session!.Process.Memory.ReadBytes(address, 4);
        return read.Complete ? BitConverter.ToInt32(read.Data, 0) : null;
    }

    /// <summary>Lit le masque et qualifie l'observation. Jamais de NM supposé : seulement une lecture valide en sélection.</summary>
    internal SongSelectModsSnapshot Read(INativeStableMemoryReader memory, nuint global, string processIdentity, int? status, DateTimeOffset now)
    {
        MaskReads++;
        NativeStableMemoryReadResult read = memory.ReadBytes(global, 4);
        if (!read.Complete)
            return SongSelectModsSnapshot.Unknown(processIdentity, status, now, SongSelectModsFailure.ReadIncomplete);
        int mask = BitConverter.ToInt32(read.Data, 0);
        if (mask < 0)
            return SongSelectModsSnapshot.Unknown(processIdentity, status, now, SongSelectModsFailure.NegativeMask) with { RawModsMask = mask };
        if (status is null)
            return SongSelectModsSnapshot.Unknown(processIdentity, status, now, SongSelectModsFailure.RouteUnavailable) with { RawModsMask = mask };

        IReadOnlyList<string> mods = StableModsMask.Normalize(mask);
        SongSelectModsValidity validity =
            status != StableModsMask.SongSelectStatus ? SongSelectModsValidity.NotSongSelect :
            (mask & ~StableModsMask.KnownMask) != 0 ? SongSelectModsValidity.UnknownBits :
            SongSelectModsValidity.Valid;
        return new(processIdentity, status, mask, mods,
            validity == SongSelectModsValidity.Valid ? StableModsMask.Key(mods) : null,
            validity, now, SongSelectModsFailure.None);
    }

    /// <summary>
    /// Une découverte contrôlée : ouverture du processus attendu, recherche de la signature dans les seules régions
    /// exécutables (exactement une occurrence), lecture de l'opérande, validation de l'adresse globale.
    /// </summary>
    internal static Attempt Discover(ISongSelectModsHost host, string expectedIdentity)
    {
        ISongSelectModsProcess? process = null;
        try
        {
            process = host.TryOpen(expectedIdentity, out string reason);
            if (process is null)
                return Attempt.Failed(expectedIdentity, SongSelectModsFailure.SourceUnavailable, null, reason);
            IReadOnlyList<NativeStableMemoryRegion> readable = process.EnumerateReadableRegions();
            SongSelectModsDiscovery discovery = Discover(process.Memory, readable.Where(IsExecutable).ToArray(), new RegionIndex(readable));
            if (discovery.Failure != SongSelectModsFailure.None)
            {
                process.Dispose();
                return Attempt.Failed(expectedIdentity, discovery.Failure, discovery, "");
            }
            return new Attempt(expectedIdentity, process, discovery, SongSelectModsFailure.None, "");
        }
        catch (Exception ex)
        {
            process?.Dispose();
            return Attempt.Failed(expectedIdentity, SongSelectModsFailure.SourceUnavailable, null, ex.GetType().Name);
        }
    }

    internal static SongSelectModsDiscovery Discover(
        INativeStableMemoryReader memory, IReadOnlyList<NativeStableMemoryRegion> searchRegions, RegionIndex readable)
    {
        var watch = Stopwatch.StartNew();
        IReadOnlyList<nuint> matches = NativeStableProcessPatternSearch.FindAll(memory, searchRegions, Signature, maximumMatches: 2);
        if (matches.Count == 0)
            return new(SongSelectModsFailure.SignatureAbsent, 0, 0, 0, watch.Elapsed);
        if (matches.Count > 1)
            return new(SongSelectModsFailure.SignatureAmbiguous, 0, 0, matches.Count, watch.Elapsed);

        nuint match = matches[0];
        NativeStableMemoryReadResult operand = memory.ReadBytes(match + OperandOffset, 4);
        if (!operand.Complete)
            return new(SongSelectModsFailure.OperandUnreadable, match, 0, 1, watch.Elapsed);
        nuint global = BitConverter.ToUInt32(operand.Data, 0);
        if (!readable.IsReadableAligned(global, 4))
            return new(SongSelectModsFailure.GlobalAddressInvalid, match, global, 1, watch.Elapsed);
        return new(SongSelectModsFailure.None, match, global, 1, watch.Elapsed);
    }

    /// <summary>PAGE_EXECUTE, _READ, _READWRITE, _WRITECOPY : le code JIT ne vit que là.</summary>
    internal static bool IsExecutable(NativeStableMemoryRegion region) => (region.Protect & 0xff) is 0x10 or 0x20 or 0x40 or 0x80;

    private void Reset(string? newIdentity)
    {
        identity = newIdentity;
        session?.Process.Dispose();
        session = null;
        failures = 0;
        nextAttempt = default;
        lastFailure = SongSelectModsFailure.Discovering;
        // Une découverte en cours pour l'ancienne identité sera rejetée (identité différente) puis fermée.
    }

    public void Dispose()
    {
        Task<Attempt>? running;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            Reset(null);
            running = pending;
            pending = null;
        }
        if (running is null) return;
        try
        {
            if (running.Wait(TimeSpan.FromSeconds(5))) running.Result.Process?.Dispose();
        }
        catch (AggregateException) { }
    }

    internal sealed record Attempt(
        string Identity,
        ISongSelectModsProcess? Process,
        SongSelectModsDiscovery? Discovery,
        SongSelectModsFailure Failure,
        string Note)
    {
        public static Attempt Failed(string identity, SongSelectModsFailure failure, SongSelectModsDiscovery? discovery, string note) =>
            new(identity, null, discovery, failure, note);
    }

    private sealed record Session(ISongSelectModsProcess Process, nuint GlobalAddress);
}

/// <summary>Hôte de production : GameStateService pour l'identité et le statut partagé, handle propre en lecture seule.</summary>
internal sealed class GameStateSongSelectModsHost(GameStateService gameState) : ISongSelectModsHost
{
    private const string StableClientName = "osu!stable";

    public string? CurrentStableIdentity()
    {
        GameStateSnapshot game = gameState.Current;
        return string.Equals(game.ClientName, StableClientName, StringComparison.Ordinal) ? game.ProcessIdentity : null;
    }

    public nuint? StatusAddress(string processIdentity) =>
        gameState.TryGetSharedStableStatusDiscovery() is { } shared &&
        string.Equals(shared.ProcessIdentity, processIdentity, StringComparison.Ordinal)
            ? shared.StatusValueAddress
            : null;

    public ISongSelectModsProcess? TryOpen(string processIdentity, out string reason)
    {
        NativeStableProcessCandidate? candidate = NativeStableProcessDiscovery.Discover().UniqueCandidate;
        if (candidate is null)
        {
            reason = "processus osu!stable introuvable ou ambigu";
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
        if (!string.Equals(identity, processIdentity, StringComparison.Ordinal))
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
        reason = "ok";
        return new Handle(attach.Memory);
    }

    /// <summary>Le handle maintient l'objet processus : un PID réutilisé ne peut pas être confondu.</summary>
    private sealed class Handle(NativeStableProcessMemory memory) : ISongSelectModsProcess
    {
        public bool IsAlive => memory.IsAlive;

        public INativeStableMemoryReader Memory => memory;

        public IReadOnlyList<NativeStableMemoryRegion> EnumerateReadableRegions() => memory.EnumerateReadableRegions();

        public void Dispose() => memory.Dispose();
    }
}
