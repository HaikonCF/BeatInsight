using BeatInsight.Diagnostics.NativeStable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeatInsight.Integration.Game;

public sealed class NativeStableGameProvider :
    IGameStateProvider,
    IGameRouteProvider,
    IGameplayPositionProvider,
    ILiveGameplayStatsProvider,
    ILiveGameplayStatsDiagnosticsProvider,
    INativeStableSharedDiscoveryProvider,
    IDisposable
{
    public const string ProviderName = "NativeStable";
    private const string ProfileFileName = "native-stable-autonomous-q-profile.json";
    private readonly INativeStableGameRuntime runtime;
    private readonly INativeStableGameLocator locator;
    private readonly INativeStableLiveStateReader liveStateReader;
    private readonly INativeStableBeatmapIdentityReader beatmapIdentityReader;
    private readonly INativeStableGameplayStatsReader gameplayStatsReader;
    private readonly NativeStableAutonomousQProfile? profile;
    private string? cachedIdentityProcess;
    private string? cachedIdentityPath;
    private NativeStableBeatmapIdentity? cachedBeatmapIdentity;
    private int disposed;
    private GameStateSnapshot current = Unavailable(
        DateTimeOffset.MinValue,
        GameStateAvailability.ProviderUnavailable,
        "Provider has not refreshed.");

    public NativeStableGameProvider()
        : this(DefaultProfilePath)
    {
    }

    public NativeStableGameProvider(string profilePath)
        : this(new NativeStableGameRuntime(), new NativeStableGameLocator(),
            new NativeStableLiveStateReader(), new NativeStableBeatmapIdentityReader(),
            NativeStableGameProfileLoader.TryLoad(profilePath))
    {
    }

    internal NativeStableGameProvider(
        INativeStableGameRuntime runtime,
        INativeStableGameLocator locator,
        NativeStableAutonomousQProfile? profile)
        : this(runtime, locator, new NativeStableLiveStateReader(),
            new NativeStableBeatmapIdentityReader(), profile)
    {
    }

    internal NativeStableGameProvider(
        INativeStableGameRuntime runtime,
        INativeStableGameLocator locator,
        INativeStableLiveStateReader liveStateReader,
        NativeStableAutonomousQProfile? profile)
        : this(runtime, locator, liveStateReader,
            new NativeStableBeatmapIdentityReader(), profile)
    {
    }

    internal NativeStableGameProvider(
        INativeStableGameRuntime runtime,
        INativeStableGameLocator locator,
        INativeStableLiveStateReader liveStateReader,
        INativeStableBeatmapIdentityReader beatmapIdentityReader,
        NativeStableAutonomousQProfile? profile)
        : this(runtime, locator, liveStateReader, beatmapIdentityReader,
            new NativeStableGameplayStatsReader(), profile)
    {
    }

    internal NativeStableGameProvider(
        INativeStableGameRuntime runtime,
        INativeStableGameLocator locator,
        INativeStableLiveStateReader liveStateReader,
        INativeStableBeatmapIdentityReader beatmapIdentityReader,
        INativeStableGameplayStatsReader gameplayStatsReader,
        NativeStableAutonomousQProfile? profile)
    {
        this.runtime = runtime;
        this.locator = locator;
        this.liveStateReader = liveStateReader;
        this.beatmapIdentityReader = beatmapIdentityReader;
        this.gameplayStatsReader = gameplayStatsReader;
        this.profile = profile;
    }

    public static string DefaultProfilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BeatInsight", "NativeStable", ProfileFileName);

    public string Name => ProviderName;

    public GameStateSnapshot Current => Volatile.Read(ref current);

    public bool IsAvailable => Current.IsAvailable;

    public string? ProcessIdentity => Current.ProcessIdentity;

    public string? CurrentBeatmapPath => Current.CurrentBeatmapPath;

    public string? CurrentBeatmapId => Current.CurrentBeatmapId;

    public DateTime? LastUpdate => Current.LastUpdateUtc == DateTimeOffset.MinValue
        ? null
        : Current.LastUpdateUtc.UtcDateTime;

    public GameStateSnapshot Refresh()
    {
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        if (Volatile.Read(ref disposed) != 0)
        {
            return SetCurrent(Unavailable(
                observedAt,
                GameStateAvailability.ProviderUnavailable,
                "Native provider is shutting down."));
        }
        NativeStableGameProcessContext? context = null;
        GameStateSnapshot result;
        try
        {
            result = RefreshCore(observedAt, ref context);
        }
        catch (Exception exception)
        {
            result = Unavailable(
                observedAt,
                GameStateAvailability.Error,
                $"Native provider failed with {exception.GetType().Name}.",
                context);
        }
        finally
        {
            try
            {
                context?.Dispose();
            }
            catch (Exception exception)
            {
                result = Unavailable(
                    observedAt,
                    GameStateAvailability.Error,
                    $"Native process cleanup failed with {exception.GetType().Name}.");
            }
        }

        return SetCurrent(result);
    }

    private GameStateSnapshot RefreshCore(
        DateTimeOffset observedAt,
        ref NativeStableGameProcessContext? context)
    {
        if (profile is null)
        {
            return Unavailable(
                observedAt,
                GameStateAvailability.ProviderUnavailable,
                "Native stable profile is unavailable.");
        }

        context = runtime.TryOpen();
        if (context is null)
        {
            liveStateReader.Invalidate();
            gameplayStatsReader.Invalidate();
            return Unavailable(
                observedAt,
                GameStateAvailability.NoProcess,
                "No osu!stable process is available.");
        }

        string processIdentity = FormatProcessIdentity(context);
        gameplayStatsReader.ObserveProcessIdentity(processIdentity);
        if (!liveStateReader.EnsureDiscovered(context))
        {
            return Unavailable(
                observedAt,
                GameStateAvailability.ProviderUnavailable,
                "Native stable live-state signatures are unavailable.",
                context);
        }

        NativeStableGameLocatorOutcome outcome = locator.Locate(context, profile);
        if (outcome.Discovery != NativeStableAutonomousQDiscovery.Unique)
        {
            GameStateAvailability availability = outcome.Discovery switch
            {
                NativeStableAutonomousQDiscovery.Ambiguous => GameStateAvailability.Ambiguous,
                NativeStableAutonomousQDiscovery.None => GameStateAvailability.NoBeatmap,
                _ => GameStateAvailability.ProviderUnavailable
            };
            return Unavailable(
                observedAt,
                availability,
                $"Native locator result: {outcome.Discovery}.",
                context);
        }

        if (!outcome.ObjectValid || string.IsNullOrWhiteSpace(outcome.Folder) ||
            string.IsNullOrWhiteSpace(outcome.Filename))
        {
            return Unavailable(
                observedAt,
                GameStateAvailability.InvalidData,
                "Native locator returned incomplete beatmap data.",
                context);
        }

        string? beatmapPath = runtime.TryResolveBeatmapPath(
            context, outcome.Folder, outcome.Filename);
        if (string.IsNullOrWhiteSpace(beatmapPath))
        {
            return Unavailable(
                observedAt,
                GameStateAvailability.NoBeatmap,
                "The current native beatmap path could not be resolved.",
                context);
        }

        NativeStableBeatmapIdentity fileIdentity = ReadBeatmapIdentity(
            processIdentity,
            beatmapPath);
        string? beatmapSetId = outcome.BeatmapId is > 0 &&
            fileIdentity.BeatmapId == outcome.BeatmapId &&
            fileIdentity.BeatmapSetId is > 0
                ? fileIdentity.BeatmapSetId.Value.ToString(CultureInfo.InvariantCulture)
                : null;

        // Refresh runs on the existing background path. Warm the one-time structural
        // root discovery there so the UI-side live tick remains a cached fast read.
        if (liveStateReader.ReadStatus(context) == 2)
            _ = gameplayStatsReader.Read(context, processIdentity);

        return new(
            ProviderName,
            "osu!stable",
            context.ProcessId,
            processIdentity,
            beatmapPath,
            outcome.BeatmapId is > 0
                ? outcome.BeatmapId.Value.ToString(CultureInfo.InvariantCulture)
                : null,
            beatmapSetId,
            observedAt,
            GameStateAvailability.Available,
            null)
        {
            SongsDirectory = TryGetSongsDirectory(beatmapPath),
            CurrentBackgroundPath = BeatmapBackgroundResolver.Resolve(beatmapPath)
        };
    }

    private NativeStableBeatmapIdentity ReadBeatmapIdentity(
        string processIdentity,
        string beatmapPath)
    {
        if (cachedBeatmapIdentity is not null &&
            string.Equals(cachedIdentityProcess, processIdentity, StringComparison.Ordinal) &&
            string.Equals(cachedIdentityPath, beatmapPath, StringComparison.OrdinalIgnoreCase))
        {
            return cachedBeatmapIdentity;
        }

        NativeStableBeatmapIdentity identity = beatmapIdentityReader.Read(beatmapPath);
        cachedIdentityProcess = processIdentity;
        cachedIdentityPath = beatmapPath;
        cachedBeatmapIdentity = identity;
        return identity;
    }

    private GameStateSnapshot SetCurrent(GameStateSnapshot snapshot)
    {
        Volatile.Write(ref current, snapshot);
        return snapshot;
    }

    public GameRouteSnapshot ReadGameRoute()
    {
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        GameStateSnapshot game = Current;
        if (Volatile.Read(ref disposed) != 0)
            return GameRouteSnapshot.Unknown(observedAt, game.ProcessIdentity);
        using NativeStableGameProcessContext? context = OpenCurrentGeneration(game);
        if (context is null)
        {
            return GameRouteSnapshot.Unknown(observedAt, game.ProcessIdentity);
        }

        int? status = liveStateReader.ReadStatus(context);
        GameRoute route = status switch
        {
            5 => GameRoute.SongSelect,
            2 => GameRoute.Gameplay,
            7 => GameRoute.Results,
            _ => GameRoute.Unknown
        };
        return new(route, observedAt, game.ProcessIdentity);
    }

    public GameplayPositionSnapshot ReadGameplayPosition()
    {
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        GameStateSnapshot game = Current;
        if (Volatile.Read(ref disposed) != 0)
            return GameplayPositionSnapshot.Unavailable(observedAt, game.ProcessIdentity);
        using NativeStableGameProcessContext? context = OpenCurrentGeneration(game);
        if (context is null)
        {
            return GameplayPositionSnapshot.Unavailable(observedAt, game.ProcessIdentity);
        }

        int? playTime = liveStateReader.ReadPlayTime(context);
        return playTime is >= 0
            ? new(TimeSpan.FromMilliseconds(playTime.Value), observedAt, game.ProcessIdentity)
            : GameplayPositionSnapshot.Unavailable(observedAt, game.ProcessIdentity);
    }

    public LiveGameplayStatsSnapshot? ReadLiveGameplayStats()
        => ReadLiveGameplayStatsWithDiagnostics().Snapshot;

    public LiveGameplayStatsReadResult ReadLiveGameplayStatsWithDiagnostics()
    {
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        GameStateSnapshot game = Current;
        if (Volatile.Read(ref disposed) != 0)
            return Failed(LiveGameplayStatsFailureReason.ProviderUnavailable);
        using NativeStableGameProcessContext? context = OpenCurrentGeneration(game);
        if (context is null)
        {
            return Failed(game.ProcessIdentity is null
                ? LiveGameplayStatsFailureReason.ProcessIdentityMissing
                : LiveGameplayStatsFailureReason.ProcessOpenFailed);
        }
        if (game.ProcessIdentity is null)
            return Failed(LiveGameplayStatsFailureReason.ProcessIdentityMissing);
        if (liveStateReader.ReadStatus(context) != 2)
            return Failed(LiveGameplayStatsFailureReason.RouteNotGameplay);

        int? playTime = liveStateReader.ReadPlayTime(context);
        NativeStableGameplayStats? stats = gameplayStatsReader.ReadCached(
            context,
            game.ProcessIdentity,
            out NativeStableGameplayStatsFailureReason readerFailure);
        if (playTime is not >= 0)
            return Failed(LiveGameplayStatsFailureReason.CurrentTimeUnavailable);
        if (stats is null)
            return Failed(ToPublicFailure(readerFailure));

        var snapshot = new LiveGameplayStatsSnapshot(
            game.ProcessIdentity,
            game.CurrentBeatmapId,
            game.CurrentBeatmapSetId,
            TimeSpan.FromMilliseconds(playTime.Value),
            stats.CurrentCombo,
            stats.MaxComboReached,
            stats.Count300,
            stats.Count100,
            stats.Count50,
            stats.CountMiss,
            stats.Accuracy,
            stats.Mods,
            observedAt,
            stats.Score);

        return new(snapshot, Diagnostics(LiveGameplayStatsFailureReason.None));

        LiveGameplayStatsReadResult Failed(LiveGameplayStatsFailureReason reason) =>
            new(null, Diagnostics(reason));
    }

    private LiveGameplayStatsDiagnostics Diagnostics(
        LiveGameplayStatsFailureReason failureReason)
    {
        NativeStableGameplayStatsReaderDiagnostics diagnostics = gameplayStatsReader.Diagnostics;
        NativeStableGameplayStatsDiscoveryReport report = diagnostics.LastDiscoveryReport;
        return new(
            failureReason,
            diagnostics.DiscoveryState.ToString(),
            diagnostics.ReaderResolved,
            diagnostics.DiscoveryAttempts,
            diagnostics.SuccessfulDiscoveryCount,
            diagnostics.LastDiscoveryFailure.ToString(),
            report.Outcome.ToString(),
            report.RawCandidateCount,
            report.StructuralCandidateCount,
            report.ValidatedCandidateCount,
            report.PlausibleCandidateCount,
            report.ActiveCandidateCount,
            report.Candidates.Select(candidate => new
                LiveGameplayStatsDiscoveryCandidateDiagnostics(
                    Hex(candidate.RootAddress),
                    "WritablePrivateRegionScan",
                    Hex(candidate.SourceRegionBase),
                    $"0x{candidate.SourceOffset:X}",
                    Hex(candidate.GameplayBase),
                    Hex(candidate.ScoreBase),
                    Hex(candidate.AccuracyBase),
                    Hex(candidate.ModsBase),
                    candidate.PointerChainStructurallyValid,
                    candidate.ProbeReadsComplete,
                    candidate.RejectionReason.ToString(),
                    candidate.SemanticReadSucceeded,
                    candidate.SemanticFailureReason.ToString(),
                    FormatSemanticSample(candidate.SemanticSample),
                    candidate.RawSample,
                    candidate.PlausibleCandidate,
                    candidate.TemporalObservationCount,
                    candidate.CoherentProgressionCount,
                    candidate.TemporalEvidence,
                    candidate.TemporallyActive,
                    candidate.ActiveGameplayEvidence))
                .ToArray(),
            diagnostics.NextDiscoveryRetry,
            diagnostics.ProcessAttachToDiscoveryReady,
            diagnostics.LastDiscoveryDuration,
            Hex(diagnostics.ContainerAddress),
            Hex(diagnostics.ActiveRulesetAddress),
            Hex(diagnostics.GameplayBaseAddress),
            Hex(diagnostics.ScoreBaseAddress));

        static string Hex(nuint value) => value == 0 ? "<null>" : $"0x{value:X}";
        static string FormatSemanticSample(NativeStableGameplayStats? stats) => stats is null
            ? "<null>"
            : $"Combo={stats.CurrentCombo}/{stats.MaxComboReached};300={stats.Count300};" +
              $"100={stats.Count100};50={stats.Count50};Miss={stats.CountMiss};" +
              $"Accuracy={stats.Accuracy:F6};Score={stats.Score};" +
              $"Mods={string.Join(',', stats.Mods.Select(mod => mod.Acronym))}";
    }

    private static LiveGameplayStatsFailureReason ToPublicFailure(
        NativeStableGameplayStatsFailureReason reason) => reason switch
    {
        NativeStableGameplayStatsFailureReason.None => LiveGameplayStatsFailureReason.ProviderReturnedNull,
        NativeStableGameplayStatsFailureReason.ProcessIdentityMismatch => LiveGameplayStatsFailureReason.ProcessIdentityMismatch,
        NativeStableGameplayStatsFailureReason.DiscoveryNotReady => LiveGameplayStatsFailureReason.DiscoveryNotReady,
        NativeStableGameplayStatsFailureReason.PointerChainUnavailable => LiveGameplayStatsFailureReason.PointerChainUnavailable,
        NativeStableGameplayStatsFailureReason.ScoreReadIncomplete => LiveGameplayStatsFailureReason.ScoreReadIncomplete,
        NativeStableGameplayStatsFailureReason.AccuracyReadIncomplete => LiveGameplayStatsFailureReason.AccuracyReadIncomplete,
        NativeStableGameplayStatsFailureReason.ModsReadIncomplete => LiveGameplayStatsFailureReason.ModsReadIncomplete,
        NativeStableGameplayStatsFailureReason.AccuracyNotFinite => LiveGameplayStatsFailureReason.AccuracyNotFinite,
        NativeStableGameplayStatsFailureReason.AccuracyOutOfRange => LiveGameplayStatsFailureReason.AccuracyOutOfRange,
        NativeStableGameplayStatsFailureReason.HitCountOutOfRange => LiveGameplayStatsFailureReason.HitCountOutOfRange,
        NativeStableGameplayStatsFailureReason.ComboOutOfRange => LiveGameplayStatsFailureReason.ComboOutOfRange,
        NativeStableGameplayStatsFailureReason.ScoreNegative => LiveGameplayStatsFailureReason.ScoreNegative,
        NativeStableGameplayStatsFailureReason.ModsNegative => LiveGameplayStatsFailureReason.ModsNegative,
        NativeStableGameplayStatsFailureReason.ModsUnsupportedForStandard => LiveGameplayStatsFailureReason.ModsUnsupportedForStandard,
        NativeStableGameplayStatsFailureReason.AccuracyMismatch => LiveGameplayStatsFailureReason.AccuracyMismatch,
        _ => LiveGameplayStatsFailureReason.ProviderReturnedNull
    };

    private NativeStableGameProcessContext? OpenCurrentGeneration(GameStateSnapshot game)
    {
        if (Volatile.Read(ref disposed) != 0)
            return null;
        if (!game.IsAvailable || string.IsNullOrWhiteSpace(game.ProcessIdentity))
        {
            return null;
        }

        NativeStableGameProcessContext? context = runtime.TryOpen();
        if (context is not null && string.Equals(
            FormatProcessIdentity(context),
            game.ProcessIdentity,
            StringComparison.Ordinal))
        {
            return context;
        }

        context?.Dispose();
        liveStateReader.Invalidate();
        gameplayStatsReader.Invalidate();
        return null;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        cachedIdentityProcess = null;
        cachedIdentityPath = null;
        cachedBeatmapIdentity = null;
        liveStateReader.Invalidate();
        gameplayStatsReader.Invalidate();
        Volatile.Write(ref current, Unavailable(
            DateTimeOffset.UtcNow,
            GameStateAvailability.ProviderUnavailable,
            "Native provider is shut down."));
    }

    private static GameStateSnapshot Unavailable(
        DateTimeOffset observedAt,
        GameStateAvailability availability,
        string reason,
        NativeStableGameProcessContext? context = null) =>
        new(
            ProviderName,
            "osu!stable",
            context?.ProcessId,
            context is null ? null : FormatProcessIdentity(context),
            null,
            null,
            null,
            observedAt,
            availability,
            reason);

    // V3.14-COM-KEY-05 : exposition en lecture seule de la découverte existante (aucun scan ici).
    private readonly object sharedDiscoveryGate = new();
    private string? sharedDiscoveryKey;
    private DateTimeOffset sharedDiscoveryValidatedAt;

    NativeStableSharedDiscovery? INativeStableSharedDiscoveryProvider.TryGetSharedDiscovery()
    {
        if (Volatile.Read(ref disposed) != 0)
            return null;
        if (!liveStateReader.TryGetDiscovered(out NativeStableLivePatternAddresses? addresses, out int processId, out DateTimeOffset processStart) ||
            addresses is null)
        {
            return null;
        }

        string identity = string.Create(CultureInfo.InvariantCulture, $"{processId}:{processStart.UtcDateTime:O}");
        NativeStableGameplayStatsReaderDiagnostics stats = gameplayStatsReader.Diagnostics;
        // Conteneur publié seulement s'il est résolu (donc validé par le reader) pour CE processus.
        if (stats.ContainerAddress == 0 ||
            stats.DiscoveryState != NativeStableGameplayStatsDiscoveryState.Resolved ||
            !string.Equals(stats.ProcessIdentity, identity, StringComparison.Ordinal))
        {
            return null;
        }

        DateTimeOffset validatedAt;
        string key = string.Create(CultureInfo.InvariantCulture,
            $"{identity}#{stats.SuccessfulDiscoveryCount}#{stats.ContainerAddress}");
        lock (sharedDiscoveryGate)
        {
            if (!string.Equals(key, sharedDiscoveryKey, StringComparison.Ordinal))
            {
                sharedDiscoveryKey = key;
                sharedDiscoveryValidatedAt = DateTimeOffset.UtcNow;
            }
            validatedAt = sharedDiscoveryValidatedAt;
        }

        return new NativeStableSharedDiscovery(
            identity,
            processId,
            processStart,
            addresses.StatusValueAddress,
            addresses.PlayTimeValueAddress,
            stats.ContainerAddress,
            stats.DiscoveryState,
            validatedAt,
            stats.SuccessfulDiscoveryCount);
    }

    NativeStableSharedStatusDiscovery? INativeStableSharedDiscoveryProvider.TryGetSharedStatusDiscovery()
    {
        if (Volatile.Read(ref disposed) != 0)
            return null;
        if (!liveStateReader.TryGetDiscovered(out NativeStableLivePatternAddresses? addresses, out int processId, out DateTimeOffset processStart) ||
            addresses is null)
        {
            return null;
        }

        string identity = string.Create(CultureInfo.InvariantCulture, $"{processId}:{processStart.UtcDateTime:O}");
        return new NativeStableSharedStatusDiscovery(identity, processId, processStart, addresses.StatusValueAddress);
    }

    private static string FormatProcessIdentity(NativeStableGameProcessContext context) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{context.ProcessId}:{context.ProcessStartIdentity.UtcDateTime:O}");

    private static string? TryGetSongsDirectory(string beatmapPath)
    {
        try
        {
            return Directory.GetParent(beatmapPath)?.Parent?.FullName;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}

internal sealed record NativeStableGameLocatorOutcome(
    NativeStableAutonomousQDiscovery Discovery,
    bool ObjectValid,
    string? Folder,
    string? Filename,
    int? BeatmapId);

internal interface INativeStableGameLocator
{
    NativeStableGameLocatorOutcome Locate(
        NativeStableGameProcessContext context,
        NativeStableAutonomousQProfile profile);
}

internal sealed class NativeStableGameLocator : INativeStableGameLocator
{
    // Keeping one locator instance is the process-generation cache. Its cached Q is
    // keyed by (ProcessId, ProcessStartIdentity) and revalidated by Locate every time.
    private readonly NativeStableAutonomousQLocator locator = new();

    public NativeStableGameLocatorOutcome Locate(
        NativeStableGameProcessContext context,
        NativeStableAutonomousQProfile profile)
    {
        NativeStableAutonomousQLocatorResult result = locator.Locate(
            context.Memory,
            context.Regions,
            context.Modules,
            context.ProcessId,
            context.ProcessStartIdentity,
            profile);
        if (result.Discovery != NativeStableAutonomousQDiscovery.Unique ||
            result.SelectedQAddress is not ulong selected)
        {
            return new(result.Discovery, false, null, null, null);
        }

        NativeStableAutonomousQCandidate? candidate = result.Candidates
            .SingleOrDefault(item => item.QAddress == selected);
        NativeStableAutonomousBeatmapObjectEvidence? evidence = candidate?.ObjectEvidence;
        return evidence is not null && evidence.IsValid
            ? new(result.Discovery, true, evidence.Folder, evidence.Filename,
                evidence.BeatmapId)
            : new(NativeStableAutonomousQDiscovery.Unavailable, false, null, null, null);
    }
}

internal interface INativeStableGameRuntime
{
    NativeStableGameProcessContext? TryOpen();

    string? TryResolveBeatmapPath(
        NativeStableGameProcessContext context,
        string folder,
        string filename);
}

internal sealed class NativeStableGameRuntime : INativeStableGameRuntime
{
    public NativeStableGameProcessContext? TryOpen()
    {
        NativeStableProcessMemory? memory = null;
        try
        {
            NativeStableProcessCandidate? process =
                NativeStableProcessDiscovery.Discover().UniqueCandidate;
            if (process?.ExecutablePath is null)
            {
                return null;
            }

            using Process liveProcess = Process.GetProcessById(process.ProcessId);
            DateTimeOffset processStart = liveProcess.StartTime.ToUniversalTime();
            NativeStableAttachResult attachment = NativeStableProcessMemory.TryAttach(process);
            memory = attachment.Memory;
            if (memory is null)
            {
                return null;
            }

            var context = new NativeStableGameProcessContext(
                process.ProcessId,
                processStart,
                process.ExecutablePath,
                memory,
                memory.EnumerateReadableRegions(),
                NativeStableModuleCatalog.Capture(process.ProcessId));
            memory = null;
            return context;
        }
        catch (Exception)
        {
            memory?.Dispose();
            return null;
        }
    }

    public string? TryResolveBeatmapPath(
        NativeStableGameProcessContext context,
        string folder,
        string filename)
    {
        try
        {
            NativeStableSongsPathResult songs = new NativeStableSongsPathResolver()
                .Resolve(context.ExecutablePath);
            if (songs.Path is null)
            {
                return null;
            }

            string root = Path.GetFullPath(songs.Path);
            string candidate = Path.GetFullPath(Path.Combine(root, folder, filename));
            string rootedPrefix = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;
            return candidate.StartsWith(rootedPrefix, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(candidate)
                ? candidate
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

internal sealed class NativeStableGameProcessContext : IDisposable
{
    internal NativeStableGameProcessContext(
        int processId,
        DateTimeOffset processStartIdentity,
        string executablePath,
        INativeStableMemoryReader memory,
        IReadOnlyList<NativeStableMemoryRegion> regions,
        IReadOnlyList<NativeStableModuleRange> modules,
        Action? dispose = null)
    {
        ProcessId = processId;
        ProcessStartIdentity = processStartIdentity;
        ExecutablePath = executablePath;
        Memory = memory;
        Regions = regions;
        Modules = modules;
        this.dispose = dispose;
    }

    private readonly Action? dispose;

    internal int ProcessId { get; }

    internal DateTimeOffset ProcessStartIdentity { get; }

    internal string ExecutablePath { get; }

    internal INativeStableMemoryReader Memory { get; }

    internal IReadOnlyList<NativeStableMemoryRegion> Regions { get; }

    internal IReadOnlyList<NativeStableModuleRange> Modules { get; }

    public void Dispose()
    {
        if (Memory is IDisposable disposable)
        {
            disposable.Dispose();
        }
        dispose?.Invoke();
    }
}

internal static class NativeStableGameProfileLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    internal static NativeStableAutonomousQProfile? TryLoad(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }
            NativeStableAutonomousQProfile? profile = JsonSerializer.Deserialize<
                NativeStableAutonomousQProfile>(File.ReadAllText(path), Options);
            profile?.Validate();
            return profile;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
