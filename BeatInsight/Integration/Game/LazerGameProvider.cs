using BeatInsight.Diagnostics.NativeLazer;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace BeatInsight.Integration.Game;

public sealed class LazerGameProvider : IGameStateProvider, IGameplayPositionProvider, IGameRouteProvider,
    ILiveGameplayStatsProvider, ILiveGameplayStatsDiagnosticsProvider, IDisposable
{
    public const string ProviderName = "NativeLazer";
    private readonly object gate = new();
    private readonly INativeLazerGameRuntime runtime;
    private readonly Func<Func<NativeLazerServerResponse>, Task<NativeLazerServerResponse>> startBackground;
    private readonly ILazerBackgroundResolver backgroundResolver;
    private NativeLazerProcessGeneration? installedGeneration;
    private NativeLazerProcessGeneration? pendingGeneration;
    private Task<NativeLazerServerResponse>? pendingDiscovery;
    private string? backgroundCacheKey;
    private string? backgroundCachePath;
    private string? lastProviderTraceState;
    private string? lastStatsTraceState;
    private NativeLazerProcessProbeStatus lastProbeStatus;
    private int discoveryStartCount;
    private int fastRefreshCount;
    private GameStateSnapshot current = Unavailable(
        DateTimeOffset.MinValue,
        GameStateAvailability.ProviderUnavailable,
        "Provider has not refreshed.");
    private int lifecycleState;

    public LazerGameProvider()
        : this(new NativeLazerGameRuntime(), work => Task.Run(work), new LazerBackgroundResolver())
    {
    }

    internal LazerGameProvider(
        INativeLazerGameRuntime runtime,
        Func<Func<NativeLazerServerResponse>, Task<NativeLazerServerResponse>>? startBackground = null,
        ILazerBackgroundResolver? backgroundResolver = null)
    {
        this.runtime = runtime;
        this.startBackground = startBackground ?? (work => Task.Run(work));
        this.backgroundResolver = backgroundResolver ?? NullLazerBackgroundResolver.Instance;
    }

    public string Name => ProviderName;

    public GameStateSnapshot Current => Volatile.Read(ref current);

    public GameStateSnapshot Refresh()
    {
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        if (Volatile.Read(ref lifecycleState) != 0)
            return Current;
        NativeLazerProcessProbe probe;
        try
        {
            probe = runtime.Probe();
        }
        catch (Exception exception)
        {
            return SetCurrent(Unavailable(observedAt, GameStateAvailability.Error,
                $"NativeLazer process probe failed with {exception.GetType().Name}."));
        }

        lock (gate)
        {
            lastProbeStatus = probe.Status;
            if (Volatile.Read(ref lifecycleState) != 0)
                return Current;
            if (probe.Status != NativeLazerProcessProbeStatus.Unique || probe.Generation is null)
            {
                installedGeneration = null;
                return SetCurrent(Unavailable(
                    observedAt,
                    probe.Status == NativeLazerProcessProbeStatus.Ambiguous
                        ? GameStateAvailability.Ambiguous
                        : GameStateAvailability.NoProcess,
                    probe.Status == NativeLazerProcessProbeStatus.Ambiguous
                        ? "Multiple osu!lazer process generations are present."
                        : "No osu!lazer process is available."));
            }

            NativeLazerProcessGeneration generation = probe.Generation;
            if (pendingDiscovery is not null)
            {
                if (!pendingDiscovery.IsCompleted)
                    return SetCurrent(DiscoveryInProgress(observedAt, generation));

                Task<NativeLazerServerResponse> completed = pendingDiscovery;
                NativeLazerProcessGeneration? completedGeneration = pendingGeneration;
                pendingDiscovery = null;
                pendingGeneration = null;

                if (completedGeneration != generation)
                {
                    BeginDiscovery(generation);
                    return SetCurrent(DiscoveryInProgress(observedAt, generation));
                }

                NativeLazerServerResponse response;
                try
                {
                    response = completed.GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    installedGeneration = null;
                    return SetCurrent(Unavailable(observedAt, GameStateAvailability.ProviderUnavailable,
                        $"NativeLazer discovery failed with {exception.GetType().Name}.", generation));
                }

                return SetCurrent(ConsumeResponse(response, generation, observedAt, installGeneration: true));
            }

            if (installedGeneration == generation)
            {
                NativeLazerServerResponse response;
                try
                {
                    fastRefreshCount++;
                    response = runtime.Refresh();
                }
                catch (Exception exception)
                {
                    installedGeneration = null;
                    BeginDiscovery(generation);
                    return SetCurrent(Unavailable(observedAt, GameStateAvailability.ProviderUnavailable,
                        $"NativeLazer fast refresh failed with {exception.GetType().Name}; discovery restarted.", generation));
                }

                GameStateSnapshot snapshot = ConsumeResponse(response, generation, observedAt, installGeneration: false);
                if (snapshot.Availability is GameStateAvailability.ProviderUnavailable or GameStateAvailability.InvalidData)
                {
                    installedGeneration = null;
                    BeginDiscovery(generation);
                }
                return SetCurrent(snapshot);
            }

            BeginDiscovery(generation);
            return SetCurrent(DiscoveryInProgress(observedAt, generation));
        }
    }

    public GameplayPositionSnapshot ReadGameplayPosition()
    {
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        lock (gate)
        {
            if (Volatile.Read(ref lifecycleState) != 0)
                return GameplayPositionSnapshot.Unavailable(observedAt);
            NativeLazerProcessGeneration? generation = installedGeneration;
            if (generation is null)
                return GameplayPositionSnapshot.Unavailable(observedAt);

            NativeLazerGameplayPositionResponse response;
            try
            {
                response = runtime.ReadGameplayPosition();
            }
            catch (Exception)
            {
                return GameplayPositionSnapshot.Unavailable(observedAt, generation.Display);
            }

            if (response.ProcessId != generation.ProcessId ||
                !string.Equals(response.ProcessIdentity, generation.Display, StringComparison.Ordinal))
            {
                installedGeneration = null;
                return GameplayPositionSnapshot.Unavailable(observedAt);
            }

            double? milliseconds = response.CurrentTimeMilliseconds;
            if (!string.Equals(response.Outcome, "Unique", StringComparison.Ordinal) ||
                !milliseconds.HasValue ||
                !double.IsFinite(milliseconds.Value) ||
                milliseconds.Value < 0 ||
                milliseconds.Value > TimeSpan.MaxValue.TotalMilliseconds)
            {
                return GameplayPositionSnapshot.Unavailable(observedAt, generation.Display);
            }

            return new(
                TimeSpan.FromMilliseconds(milliseconds.Value),
                observedAt,
                generation.Display);
        }
    }

    public GameRouteSnapshot ReadGameRoute()
    {
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        lock (gate)
        {
            if (Volatile.Read(ref lifecycleState) != 0)
                return GameRouteSnapshot.Unknown(observedAt);
            NativeLazerProcessGeneration? generation = installedGeneration;
            if (generation is null)
                return GameRouteSnapshot.Unknown(observedAt);

            NativeLazerGameRouteResponse response;
            try
            {
                response = runtime.ReadGameRoute();
            }
            catch (Exception)
            {
                return GameRouteSnapshot.Unknown(observedAt, generation.Display);
            }

            if (response.ProcessId != generation.ProcessId ||
                !string.Equals(response.ProcessIdentity, generation.Display, StringComparison.Ordinal))
            {
                installedGeneration = null;
                return GameRouteSnapshot.Unknown(observedAt);
            }

            GameRoute route = response.Route switch
            {
                "SongSelect" => GameRoute.SongSelect,
                "Gameplay" => GameRoute.Gameplay,
                "Results" => GameRoute.Results,
                _ => GameRoute.Unknown
            };
            return new(route, observedAt, generation.Display);
        }
    }

    public LiveGameplayStatsSnapshot? ReadLiveGameplayStats() =>
        ReadLiveGameplayStatsWithDiagnostics().Snapshot;

    public LiveGameplayStatsReadResult ReadLiveGameplayStatsWithDiagnostics()
    {
        lock (gate)
        {
            if (Volatile.Read(ref lifecycleState) != 0)
                return FailedStats(LiveGameplayStatsFailureReason.ProviderUnavailable);
            NativeLazerProcessGeneration? generation = installedGeneration;
            GameStateSnapshot game = Current;
            if (generation is null || !game.IsAvailable)
                return FailedStats(LiveGameplayStatsFailureReason.GameUnavailable);

            NativeLazerGameRouteResponse route;
            NativeLazerGameplayStatsResponse response;
            try
            {
                route = runtime.ReadGameRoute();
                if (route.ProcessId != generation.ProcessId ||
                    !string.Equals(route.ProcessIdentity, generation.Display, StringComparison.Ordinal))
                {
                    installedGeneration = null;
                    return FailedStats(LiveGameplayStatsFailureReason.ProcessIdentityMismatch,
                        "NativeLazer route belongs to another process generation.");
                }
                if (!string.Equals(route.Route, "Gameplay", StringComparison.Ordinal))
                    return FailedStats(LiveGameplayStatsFailureReason.RouteNotGameplay);
                response = runtime.ReadGameplayStats();
                TraceStats(response.Outcome, response.FailureReason, generation);
            }
            catch (Exception exception)
            {
                TraceStats("Exception", exception.GetType().Name, generation);
                return FailedStats(LiveGameplayStatsFailureReason.ProviderException);
            }

            if (response.ProcessId != generation.ProcessId ||
                !string.Equals(response.ProcessIdentity, generation.Display, StringComparison.Ordinal))
            {
                installedGeneration = null;
                return FailedStats(LiveGameplayStatsFailureReason.ProcessIdentityMismatch,
                    "NativeLazer stats belong to another process generation.");
            }
            if (!string.Equals(response.Outcome, "Valid", StringComparison.Ordinal) ||
                response.CurrentTimeMilliseconds is not { } time ||
                response.CurrentCombo is not { } combo || response.HighestCombo is not { } highest ||
                response.CountGreat is not { } great || response.CountOk is not { } ok ||
                response.CountMeh is not { } meh || response.CountMiss is not { } miss ||
                response.CountLargeTickMiss is not { } largeTickMiss ||
                response.CountSliderTailHit is not { } sliderTailHit ||
                response.Accuracy is not { } accuracy || response.Score is not { } score)
            {
                return FailedStats(LiveGameplayStatsFailureReason.ProviderReturnedNull,
                    response.FailureReason ?? $"NativeLazer stats outcome was {response.Outcome}.");
            }

            if (!double.IsFinite(time) || time < 0 || combo < 0 || highest < combo ||
                great < 0 || ok < 0 || meh < 0 || miss < 0 ||
                largeTickMiss < 0 || sliderTailHit < 0 ||
                !double.IsFinite(accuracy) || accuracy is < 0 or > 1 || score < 0 ||
                response.Mods.Any(string.IsNullOrWhiteSpace))
            {
                return FailedStats(LiveGameplayStatsFailureReason.SnapshotValuesInvalid);
            }

            var snapshot = new LiveGameplayStatsSnapshot(
                generation.Display,
                game.CurrentBeatmapId,
                game.CurrentBeatmapSetId,
                TimeSpan.FromMilliseconds(time),
                combo,
                highest,
                great,
                ok,
                meh,
                miss,
                accuracy,
                response.Mods.Select(acronym => new LiveGameplayMod(acronym))
                    .ToImmutableArray(),
                response.ObservedAtUtc,
                score,
                largeTickMiss,
                sliderTailHit);
            return new(snapshot, LiveGameplayStatsDiagnostics.Unavailable(
                LiveGameplayStatsFailureReason.None) with
            {
                DiscoveryState = "NativeLazerValidatedChain",
                ReaderResolved = true
            });
        }
    }

    private static LiveGameplayStatsReadResult FailedStats(
        LiveGameplayStatsFailureReason reason,
        string? detail = null) =>
        new(null, LiveGameplayStatsDiagnostics.Unavailable(reason) with
        {
            LastDiscoveryFailure = detail ?? "None"
        });

    private GameStateSnapshot ConsumeResponse(
        NativeLazerServerResponse response,
        NativeLazerProcessGeneration generation,
        DateTimeOffset observedAt,
        bool installGeneration)
    {
        if (response.ProcessId != generation.ProcessId ||
            !string.Equals(response.ProcessIdentity, generation.Display, StringComparison.Ordinal))
        {
            installedGeneration = null;
            return Unavailable(observedAt, GameStateAvailability.ProviderUnavailable,
                "NativeLazer rejected an obsolete process-generation result.", generation);
        }

        if (string.Equals(response.Outcome, "NoBeatmap", StringComparison.Ordinal) && response.RootValid)
        {
            if (installGeneration)
                installedGeneration = generation;
            return Unavailable(observedAt, GameStateAvailability.NoBeatmap,
                "osu!lazer has no current WorkingBeatmap.", generation);
        }

        if (!string.Equals(response.Outcome, "Available", StringComparison.Ordinal) ||
            !response.RootValid ||
            response.BeatmapOnlineId is null or < 0 ||
            response.BeatmapSetOnlineId is null or < 0 ||
            string.IsNullOrWhiteSpace(response.BeatmapHash) ||
            response.BeatmapHash.Length != 64 ||
            !response.BeatmapHash.All(Uri.IsHexDigit) ||
            string.IsNullOrWhiteSpace(response.BeatmapBlobPath) ||
            !response.BlobExists)
        {
            installedGeneration = null;
            string reason = string.IsNullOrWhiteSpace(response.FailureReason)
                ? "NativeLazer returned incomplete or invalid beatmap data."
                : response.FailureReason;
            return Unavailable(observedAt,
                response.RootValid ? GameStateAvailability.InvalidData : GameStateAvailability.ProviderUnavailable,
                reason, generation);
        }

        string path;
        try
        {
            path = Path.GetFullPath(response.BeatmapBlobPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            installedGeneration = null;
            return Unavailable(observedAt, GameStateAvailability.InvalidData,
                "NativeLazer returned an invalid blob path.", generation);
        }

        if (!File.Exists(path))
        {
            installedGeneration = null;
            return Unavailable(observedAt, GameStateAvailability.InvalidData,
                "NativeLazer beatmap blob no longer exists.", generation);
        }

        if (installGeneration)
            installedGeneration = generation;
        string? backgroundPath = ResolveBackground(
            generation,
            response.BeatmapHash,
            response.BeatmapSetOnlineId.Value,
            path);
        return new(
            ProviderName,
            "osu!lazer",
            generation.ProcessId,
            generation.Display,
            path,
            response.BeatmapOnlineId.Value.ToString(CultureInfo.InvariantCulture),
            response.BeatmapSetOnlineId.Value.ToString(CultureInfo.InvariantCulture),
            observedAt,
            GameStateAvailability.Available,
            null)
        {
            CurrentBackgroundPath = backgroundPath
        };
    }

    private string? ResolveBackground(
        NativeLazerProcessGeneration generation,
        string beatmapHash,
        int beatmapSetOnlineId,
        string beatmapBlobPath)
    {
        string key = $"{generation.Display}|{beatmapHash}|{beatmapSetOnlineId}";
        if (string.Equals(backgroundCacheKey, key, StringComparison.Ordinal))
        {
            if (backgroundCachePath is null || File.Exists(backgroundCachePath))
                return backgroundCachePath;
        }

        backgroundCacheKey = key;
        backgroundCachePath = null;
        try
        {
            string? resolved = backgroundResolver.Resolve(beatmapBlobPath, beatmapSetOnlineId);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                string fullPath = Path.GetFullPath(resolved);
                if (File.Exists(fullPath))
                    backgroundCachePath = fullPath;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            backgroundCachePath = null;
        }
        return backgroundCachePath;
    }

    private void BeginDiscovery(NativeLazerProcessGeneration generation)
    {
        if (Volatile.Read(ref lifecycleState) != 0 || pendingDiscovery is not null)
            return;
        pendingGeneration = generation;
        discoveryStartCount++;
        pendingDiscovery = startBackground(runtime.Refresh);
    }

    private GameStateSnapshot SetCurrent(GameStateSnapshot snapshot)
    {
        if (Volatile.Read(ref lifecycleState) != 0)
            return Current;
        if (!snapshot.IsAvailable)
        {
            backgroundCacheKey = null;
            backgroundCachePath = null;
        }
        TraceProviderState(snapshot);
        Volatile.Write(ref current, snapshot);
        return snapshot;
    }

    private void TraceProviderState(GameStateSnapshot snapshot)
    {
        string state = string.Join('|',
            lastProbeStatus,
            snapshot.Availability,
            snapshot.ProcessIdentity ?? "<null>",
            snapshot.DiagnosticReason ?? "<null>",
            installedGeneration?.Display ?? "<null>",
            pendingGeneration?.Display ?? "<null>",
            pendingDiscovery is null ? "None" : pendingDiscovery.IsCompleted ? "Completed" : "Running");
        if (string.Equals(lastProviderTraceState, state, StringComparison.Ordinal))
            return;
        lastProviderTraceState = state;
        Diagnostics.DebugLogger.Log(
            $"LAZER PROVIDER STATE | Probe={lastProbeStatus} | " +
            $"ProcessIdentity={snapshot.ProcessIdentity ?? "<null>"} | " +
            $"Availability={snapshot.Availability} | Reason={snapshot.DiagnosticReason ?? "<null>"} | " +
            $"InstalledGeneration={installedGeneration?.Display ?? "<null>"} | " +
            $"PendingGeneration={pendingGeneration?.Display ?? "<null>"} | " +
            $"PendingDiscovery={(pendingDiscovery is null ? "None" : pendingDiscovery.IsCompleted ? "Completed" : "Running")} | " +
            $"DiscoveryStarts={discoveryStartCount} | FastRefreshes={fastRefreshCount}");
    }

    private void TraceStats(
        string outcome,
        string? reason,
        NativeLazerProcessGeneration generation)
    {
        string state = $"{generation.Display}|{outcome}|{reason ?? "<null>"}";
        if (string.Equals(lastStatsTraceState, state, StringComparison.Ordinal))
            return;
        lastStatsTraceState = state;
        Diagnostics.DebugLogger.Log(
            $"LAZER STATS STATE | ProcessIdentity={generation.Display} | " +
            $"Outcome={outcome} | FailureReason={reason ?? "<null>"}");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref lifecycleState, 1) != 0)
            return;

        Task<NativeLazerServerResponse>? pending = pendingDiscovery;
        installedGeneration = null;
        pendingGeneration = null;
        pendingDiscovery = null;
        backgroundCacheKey = null;
        backgroundCachePath = null;
        runtime.Shutdown();
        if (pending is not null)
        {
            _ = pending.ContinueWith(
                static completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted |
                    TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        Volatile.Write(ref current, Unavailable(
            DateTimeOffset.UtcNow,
            GameStateAvailability.ProviderUnavailable,
            "NativeLazer provider is shut down."));
    }

    private static GameStateSnapshot DiscoveryInProgress(
        DateTimeOffset observedAt,
        NativeLazerProcessGeneration generation) =>
        Unavailable(observedAt, GameStateAvailability.ProviderUnavailable,
            "NativeLazer discovery in progress.", generation);

    private static GameStateSnapshot Unavailable(
        DateTimeOffset observedAt,
        GameStateAvailability availability,
        string reason,
        NativeLazerProcessGeneration? generation = null) => new(
            ProviderName,
            "osu!lazer",
            generation?.ProcessId,
            generation?.Display,
            null,
            null,
            null,
            observedAt,
            availability,
            reason);
}

internal enum NativeLazerProcessProbeStatus
{
    None,
    Unique,
    Ambiguous
}

internal sealed record NativeLazerProcessGeneration(int ProcessId, DateTime ProcessStartIdentity)
{
    internal string Display => $"{ProcessId}@{ProcessStartIdentity.ToUniversalTime():O}";
}

internal sealed record NativeLazerProcessProbe(
    NativeLazerProcessProbeStatus Status,
    NativeLazerProcessGeneration? Generation = null);

internal interface INativeLazerGameRuntime
{
    NativeLazerProcessProbe Probe();

    NativeLazerServerResponse Refresh();

    NativeLazerGameplayPositionResponse ReadGameplayPosition() => new()
    {
        FailureReason = "Gameplay position is not available from this runtime."
    };

    NativeLazerGameRouteResponse ReadGameRoute() => new()
    {
        FailureReason = "Game route is not available from this runtime."
    };

    NativeLazerGameplayStatsResponse ReadGameplayStats() => new()
    {
        FailureReason = "Gameplay stats are not available from this runtime."
    };

    void Shutdown()
    {
    }
}

internal sealed class NativeLazerGameRuntime : INativeLazerGameRuntime
{
    private readonly NativeLazerHelperClient helper = new();

    public NativeLazerProcessProbe Probe()
    {
        NativeLazerDiscovery discovery = NativeLazerProcessDiscovery.Discover();
        NativeLazerProcessCandidate? candidate = discovery.UniqueCandidate;
        if (candidate is not null)
        {
            return new(NativeLazerProcessProbeStatus.Unique,
                new(candidate.ProcessId, candidate.StartTimeUtc));
        }

        return new(discovery.Status == NativeLazerAttachStatus.Ambiguous
            ? NativeLazerProcessProbeStatus.Ambiguous
            : NativeLazerProcessProbeStatus.None);
    }

    public NativeLazerServerResponse Refresh() => helper.Refresh();

    public NativeLazerGameplayPositionResponse ReadGameplayPosition() =>
        helper.ReadGameplayPosition();

    public NativeLazerGameRouteResponse ReadGameRoute() =>
        helper.ReadGameRoute();

    public NativeLazerGameplayStatsResponse ReadGameplayStats() =>
        helper.ReadGameplayStats();

    public void Shutdown() => helper.Dispose();
}

internal sealed record NativeLazerHelperShutdownResult(
    bool HadProcess,
    bool StdinClosed,
    bool Exited,
    bool TimedOut,
    TimeSpan Duration);

internal interface INativeLazerHelperProcess : IDisposable
{
    bool HasExited { get; }
    void WriteCommand(string command);
    string? ReadResponse();
    void CloseStandardInput();
    bool WaitForExit(TimeSpan timeout);
}

internal sealed class NativeLazerHelperProcess(Process process) : INativeLazerHelperProcess
{
    public bool HasExited => process.HasExited;

    public void WriteCommand(string command)
    {
        process.StandardInput.WriteLine(command);
        process.StandardInput.Flush();
    }

    public string? ReadResponse() => process.StandardOutput.ReadLine();

    public void CloseStandardInput() => process.StandardInput.Close();

    public bool WaitForExit(TimeSpan timeout) => process.WaitForExit(
        (int)Math.Clamp(timeout.TotalMilliseconds, 0, int.MaxValue));

    public void Dispose() => process.Dispose();
}

internal sealed class NativeLazerHelperClient : IDisposable
{
    internal static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(3);
    private readonly object gate = new();
    private readonly Func<INativeLazerHelperProcess> startProcess;
    private readonly TimeSpan shutdownTimeout;
    private INativeLazerHelperProcess? process;
    private int disposed;

    internal NativeLazerHelperClient(
        Func<INativeLazerHelperProcess>? startProcess = null,
        TimeSpan? shutdownTimeout = null)
    {
        this.startProcess = startProcess ?? StartProcess;
        this.shutdownTimeout = shutdownTimeout ?? DefaultShutdownTimeout;
    }

    internal NativeLazerServerResponse Refresh()
        => Send<NativeLazerServerResponse>("REFRESH");

    internal NativeLazerGameplayPositionResponse ReadGameplayPosition()
        => Send<NativeLazerGameplayPositionResponse>("POSITION");

    internal NativeLazerGameRouteResponse ReadGameRoute()
        => Send<NativeLazerGameRouteResponse>("ROUTE");

    internal NativeLazerGameplayStatsResponse ReadGameplayStats()
        => Send<NativeLazerGameplayStatsResponse>("STATS");

    private T Send<T>(string command) where T : class
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            INativeLazerHelperProcess active = EnsureProcess();
            try
            {
                active.WriteCommand(command);
                string? json = active.ReadResponse();
                T? response = json is null
                    ? null
                    : JsonSerializer.Deserialize<T>(json);
                return response ?? throw new InvalidDataException("NativeLazer helper returned no response.");
            }
            catch
            {
                Diagnostics.DebugLogger.Warning(
                    $"NativeLazer helper command {command} failed; helper connection will be recycled.");
                ReleaseProcess(active, TimeSpan.FromMilliseconds(250));
                throw;
            }
        }
    }

    private INativeLazerHelperProcess EnsureProcess()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        INativeLazerHelperProcess? active = Volatile.Read(ref process);
        if (active is not null && !active.HasExited)
            return active;

        if (active is not null)
            ReleaseProcess(active, TimeSpan.Zero);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        INativeLazerHelperProcess started = startProcess();
        if (Volatile.Read(ref disposed) != 0)
        {
            CloseProcess(started, shutdownTimeout);
            throw new ObjectDisposedException(nameof(NativeLazerHelperClient));
        }

        Interlocked.Exchange(ref process, started)?.Dispose();
        if (Volatile.Read(ref disposed) != 0)
        {
            ReleaseProcess(started, shutdownTimeout);
            throw new ObjectDisposedException(nameof(NativeLazerHelperClient));
        }
        return started;
    }

    private static INativeLazerHelperProcess StartProcess()
    {
        ProcessStartInfo startInfo = CreateStartInfo(AppContext.BaseDirectory);
        startInfo.Environment["BEATINSIGHT_NATIVE_LAZER_FAST_REFRESH"] = "1";
        startInfo.Environment["BEATINSIGHT_NATIVE_LAZER_FAST_MODE"] = "SERVER";

        Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException("NativeLazer helper could not be started.");
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        return new NativeLazerHelperProcess(process);
    }

    internal static ProcessStartInfo CreateStartInfo(string applicationBaseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationBaseDirectory);
        string helperDirectory = Path.Combine(
            Path.GetFullPath(applicationBaseDirectory), "NativeLazer");
        string executablePath = Path.Combine(helperDirectory,
            "NativeLazerFastRefreshRunner.exe");
        string dllPath = Path.Combine(helperDirectory,
            "NativeLazerFastRefreshRunner.dll");

        string fileName;
        string arguments;
        if (File.Exists(executablePath))
        {
            fileName = executablePath;
            arguments = string.Empty;
        }
        else if (File.Exists(dllPath))
        {
            fileName = "dotnet";
            arguments = $"\"{dllPath}\"";
        }
        else
        {
            throw new FileNotFoundException(
                "The NativeLazer helper is unavailable.", executablePath);
        }

        return new ProcessStartInfo(fileName)
        {
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = helperDirectory
        };
    }

    internal NativeLazerHelperShutdownResult Shutdown()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return new(false, false, true, false, TimeSpan.Zero);

        INativeLazerHelperProcess? previous = Interlocked.Exchange(ref process, null);
        NativeLazerHelperShutdownResult result = CloseProcess(previous, shutdownTimeout);
        if (result.TimedOut)
            Diagnostics.DebugLogger.Warning("SHUTDOWN | NativeLazer helper timeout; continuing without kill");
        else if (result.HadProcess)
            Diagnostics.DebugLogger.Info("SHUTDOWN | NativeLazer helper stdin closed; helper exited");
        return result;
    }

    public void Dispose() => Shutdown();

    private void ReleaseProcess(INativeLazerHelperProcess active, TimeSpan timeout)
    {
        if (!ReferenceEquals(Interlocked.CompareExchange(ref process, null, active), active))
            return;
        CloseProcess(active, timeout);
    }

    private static NativeLazerHelperShutdownResult CloseProcess(
        INativeLazerHelperProcess? previous,
        TimeSpan timeout)
    {
        long started = Stopwatch.GetTimestamp();
        if (previous is null)
            return new(false, false, true, false, TimeSpan.Zero);

        bool stdinClosed = false;
        bool exited = false;
        try
        {
            if (!previous.HasExited)
            {
                previous.CloseStandardInput();
                stdinClosed = true;
                exited = previous.WaitForExit(timeout);
            }
            else
            {
                exited = true;
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
        }
        finally
        {
            previous.Dispose();
        }

        return new(
            true,
            stdinClosed,
            exited,
            !exited,
            Stopwatch.GetElapsedTime(started));
    }
}
