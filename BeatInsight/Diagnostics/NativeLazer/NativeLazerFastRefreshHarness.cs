using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace BeatInsight.Diagnostics.NativeLazer;

/// <summary>
/// Entry point for the long-lived NativeLazer helper. SERVER mode is used by
/// the production provider; SINGLE and SESSION remain explicit diagnostic modes.
/// </summary>
public static class NativeLazerFastRefreshHarness
{
    private const string optInVariable = "BEATINSIGHT_NATIVE_LAZER_FAST_REFRESH";
    private const string modeVariable = "BEATINSIGHT_NATIVE_LAZER_FAST_MODE";
    private const int samplesPerMap = 10;

    internal static int RunFromEnvironment(
        INativeLazerGameRouteMethodTableResolver? routeResolver = null,
        INativeLazerGameplayStatsMethodTableResolver? gameplayStatsResolver = null)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        if (!string.Equals(Environment.GetEnvironmentVariable(optInVariable), "1", StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"FAIL_CLOSED: set {optInVariable}=1 to opt in to diagnostic reads.");
            return 3;
        }

        string mode = (Environment.GetEnvironmentVariable(modeVariable) ?? "SINGLE").Trim().ToUpperInvariant();
        return mode switch
        {
            "SINGLE" => RunSingle(new NativeLazerFastLiveSession(routeResolver, gameplayStatsResolver)),
            "SESSION" => RunInteractiveSession(new NativeLazerFastLiveSession(routeResolver, gameplayStatsResolver)),
            "SERVER" => RunServer(routeResolver, gameplayStatsResolver),
            _ => InvalidMode(mode)
        };
    }

    private static int RunServer(
        INativeLazerGameRouteMethodTableResolver? routeResolver,
        INativeLazerGameplayStatsMethodTableResolver? gameplayStatsResolver)
    {
        var session = new NativeLazerFastLiveSession(routeResolver, gameplayStatsResolver, emitAudit: false);
        string? command;
        while ((command = Console.ReadLine()) is not null)
        {
            string response = command switch
            {
                "REFRESH" => JsonSerializer.Serialize(NativeLazerServerResponse.From(session.Refresh())),
                "POSITION" => JsonSerializer.Serialize(session.ReadGameplayPosition()),
                "ROUTE" => JsonSerializer.Serialize(session.ReadGameRoute()),
                "STATS" => JsonSerializer.Serialize(session.ReadGameplayStats()),
                _ => JsonSerializer.Serialize(
                    NativeLazerServerResponse.ProtocolFailure("Unsupported helper command."))
            };
            Console.WriteLine(response);
            Console.Out.Flush();
        }
        return 0;
    }

    private static int RunSingle(NativeLazerFastLiveSession session)
    {
        NativeLazerFastRefreshResult result = session.Refresh();
        WriteRecord(result);
        WriteSummary(
            result.ProcessIdentity,
            result.CachedRoot,
            result.HeavyDiscoveryMs,
            result.HeavyDiscoveryBytesRead,
            new Dictionary<string, IReadOnlyList<NativeLazerFastRefreshResult>>(StringComparer.Ordinal)
            {
                ["SINGLE"] = [result]
            },
            [],
            restart: null);
        return result.Outcome == NativeLazerFastOutcome.Available ? 0 : 2;
    }

    private static int RunInteractiveSession(NativeLazerFastLiveSession session)
    {
        Console.WriteLine("RESEARCH-ONLY NativeLazer fast-refresh session.");
        Console.WriteLine("Keep one osu!lazer process generation alive through A/B/A2.");
        Prompt("Select map A, then press Enter to perform the one heavy acquisition.");
        NativeLazerFastRefreshResult acquisition = session.Refresh();
        WriteRecord(acquisition);
        if (acquisition.Outcome != NativeLazerFastOutcome.Available)
        {
            Console.Error.WriteLine("FAIL_CLOSED: heavy acquisition did not produce Available; A/B/A2 was not started.");
            return 2;
        }

        NativeLazerGenerationIdentity originalGeneration = acquisition.ProcessIdentity;
        ulong? originalRoot = acquisition.CachedRoot;
        IReadOnlyList<NativeLazerFastRefreshResult> a = CaptureGroup(session, "A", prompt: null);
        Prompt("Select map B, wait until it is visibly loaded, then press Enter.");
        IReadOnlyList<NativeLazerFastRefreshResult> b = CaptureGroup(session, "B", prompt: null);
        Prompt("Select map A again (A2), wait until loaded, then press Enter.");
        IReadOnlyList<NativeLazerFastRefreshResult> a2 = CaptureGroup(session, "A2", prompt: null);

        int? aId = FirstAvailableId(a);
        int? bId = FirstAvailableId(b);
        var transitions = new List<NativeLazerTransitionMetrics>();
        if (aId.HasValue && bId.HasValue && aId != bId)
        {
            Prompt("Transition A2 -> B: press Enter, then immediately switch to B while sampling continues.");
            transitions.Add(CaptureTransition(session, aId.Value, bId.Value));
            Prompt("Transition B -> A: press Enter, then immediately switch to A while sampling continues.");
            transitions.Add(CaptureTransition(session, bId.Value, aId.Value));
        }
        else
        {
            Console.WriteLine("TRANSITIONS_SKIPPED: coherent distinct A/B online IDs were unavailable.");
        }

        Prompt("Close osu!lazer completely, verify it has exited, then press Enter.");
        NativeLazerFastRefreshResult exited = session.Refresh();
        WriteRecord(exited);
        Prompt("Restart osu!lazer, select a beatmap, then press Enter. A new heavy discovery will run.");
        NativeLazerFastRefreshResult restarted = session.Refresh();
        WriteRecord(restarted);
        NativeLazerRestartMetrics restart = new(
            originalGeneration,
            restarted.ProcessIdentity,
            originalRoot,
            exited.Outcome == NativeLazerFastOutcome.Unknown && restarted.HeavyDiscoveryTriggered,
            restarted.HeavyDiscoveryTriggered,
            restarted.CachedRoot);

        var groups = new Dictionary<string, IReadOnlyList<NativeLazerFastRefreshResult>>(StringComparer.Ordinal)
        {
            ["A x10"] = a,
            ["B x10"] = b,
            ["A2 x10"] = a2
        };
        WriteSummary(
            originalGeneration,
            originalRoot,
            acquisition.HeavyDiscoveryMs,
            acquisition.HeavyDiscoveryBytesRead,
            groups,
            transitions,
            restart);

        bool sameGeneration = groups.Values.SelectMany(group => group)
            .All(result => result.ProcessIdentity == originalGeneration && result.CachedRoot == originalRoot);
        bool noStale = !groups.Values.SelectMany(group => group).Any(result => result.StaleAvailableDetected) &&
                       transitions.All(transition => !transition.StaleAvailableDetected);
        return sameGeneration && noStale && restart.OldCacheRejected ? 0 : 2;
    }

    private static IReadOnlyList<NativeLazerFastRefreshResult> CaptureGroup(
        NativeLazerFastLiveSession session,
        string label,
        string? prompt)
    {
        if (prompt is not null)
            Prompt(prompt);
        Console.WriteLine($"BEGIN_GROUP={label}");
        var results = new List<NativeLazerFastRefreshResult>(samplesPerMap);
        for (int index = 0; index < samplesPerMap; index++)
        {
            NativeLazerFastRefreshResult result = session.Refresh();
            results.Add(result);
            Console.WriteLine($"Sample={index + 1}/{samplesPerMap}");
            WriteRecord(result);
        }
        return results;
    }

    private static NativeLazerTransitionMetrics CaptureTransition(
        NativeLazerFastLiveSession session,
        int previousBeatmapId,
        int targetBeatmapId)
    {
        var timer = Stopwatch.StartNew();
        var samples = new List<(double ElapsedMs, NativeLazerFastRefreshResult Result)>();
        int coherentTargetSamples = 0;
        while (timer.Elapsed < TimeSpan.FromSeconds(5) && samples.Count < 500)
        {
            NativeLazerFastRefreshResult result = session.Refresh();
            samples.Add((timer.Elapsed.TotalMilliseconds, result));
            WriteRecord(result);
            if (result.Outcome == NativeLazerFastOutcome.Available && result.BeatmapOnlineId == targetBeatmapId)
            {
                coherentTargetSamples++;
                if (coherentTargetSamples >= 3)
                    break;
            }
            else
            {
                coherentTargetSamples = 0;
            }
        }

        NativeLazerTransitionMetrics metrics = NativeLazerTransitionAnalyzer.Analyze(
            previousBeatmapId, targetBeatmapId, samples);
        WriteTransition(metrics);
        return metrics;
    }

    private static void WriteRecord(NativeLazerFastRefreshResult result)
    {
        Console.WriteLine($"ProcessIdentity={result.ProcessIdentity.Display}");
        Console.WriteLine($"CachedRoot={FormatRoot(result.CachedRoot)}");
        Console.WriteLine($"CacheHit={result.CacheHit}");
        Console.WriteLine($"CacheMissReason={result.CacheMissReason}");
        Console.WriteLine($"HeavyDiscoveryTriggered={result.HeavyDiscoveryTriggered}");
        Console.WriteLine($"HeavyDiscoveryMs={result.HeavyDiscoveryMs:F3}");
        Console.WriteLine($"HeavyDiscoveryBytesRead={result.HeavyDiscoveryBytesRead}");
        Console.WriteLine($"HeavyDiscoveryReadOperationCount={result.HeavyDiscoveryReadOperationCount}");
        Console.WriteLine($"RootAcquisitionMs={result.RootAcquisitionMs:F3}");
        Console.WriteLine($"RootAcquisitionBytesRead={result.RootAcquisitionBytesRead}");
        Console.WriteLine($"RootAcquisitionReadOperationCount={result.RootAcquisitionReadOperationCount}");
        Console.WriteLine($"PersistentHintAttempted={result.PersistentHintAttempted}");
        Console.WriteLine($"PersistentHintAccepted={result.PersistentHintAccepted}");
        Console.WriteLine($"PersistentHintMissReason={result.PersistentHintMissReason ?? "none"}");
        Console.WriteLine($"RootValid={result.RootValid}");
        Console.WriteLine($"BeatmapOnlineId={FormatNullable(result.BeatmapOnlineId)}");
        Console.WriteLine($"BeatmapSetOnlineId={FormatNullable(result.BeatmapSetOnlineId)}");
        Console.WriteLine($"BeatmapHash={result.BeatmapHash ?? "unavailable"}");
        Console.WriteLine($"BlobExists={FormatNullable(result.BlobExists)}");
        Console.WriteLine($"BytesRead={result.Metrics.BytesRead}");
        Console.WriteLine($"ReadOperationCount={result.Metrics.ReadOperationCount}");
        Console.WriteLine($"RootRevalidationMs={result.Metrics.RootRevalidationMs:F3}");
        Console.WriteLine($"BeatmapDereferenceMs={result.Metrics.BeatmapDereferenceMs:F3}");
        Console.WriteLine($"MetadataReadMs={result.Metrics.MetadataReadMs:F3}");
        Console.WriteLine($"BlobValidationMs={result.Metrics.BlobValidationMs:F3}");
        Console.WriteLine($"TotalRefreshMs={result.Metrics.TotalRefreshMs:F3}");
        Console.WriteLine($"Outcome={result.Outcome}");
        Console.WriteLine($"StaleAvailableDetected={result.StaleAvailableDetected}");
        if (!string.IsNullOrWhiteSpace(result.FailureReason))
            Console.WriteLine($"FailureReason={result.FailureReason}");
        if (result.Metrics.TotalRefreshMs > 100)
            Console.WriteLine($"DominantCost={DominantCost(result.Metrics)}");
        foreach (NativeLazerDiagnosticTrace rejected in result.RejectedCandidateTraces)
            WriteTrace(rejected);
        if (result.CleanRoomTrace is not null)
            WriteTrace(result.CleanRoomTrace);
        if (result.FastPathTrace is not null)
            WriteTrace(result.FastPathTrace);
        foreach (NativeLazerDiagnosticStageComparison comparison in result.TraceComparison)
        {
            Console.WriteLine(
                $"TraceComparison.Stage={comparison.Number}:{comparison.Name}; Match={comparison.Match}; " +
                $"Difference={comparison.Difference ?? "none"}");
        }
        Console.WriteLine();
    }

    private static void WriteTrace(NativeLazerDiagnosticTrace trace)
    {
        Console.WriteLine($"Trace.Path={trace.Path}; ValidationResult={trace.ValidationResult}; " +
                          $"FailureReason={trace.FailureReason ?? "none"}");
        foreach (NativeLazerDiagnosticStage stage in trace.Stages)
        {
            Console.WriteLine(
                $"Trace.Stage={stage.Number}:{stage.Name}; SourceAddress={FormatRoot(stage.SourceAddress)}; " +
                $"OffsetUsed={FormatOffset(stage.OffsetUsed)}; TargetAddressOrValue={stage.TargetAddressOrValue}; " +
                $"Readable={stage.Readable}; Null={stage.IsNull}; ValidationResult={stage.ValidationResult}; " +
                $"FailureReason={stage.FailureReason ?? "none"}");
        }
    }

    private static void WriteSummary(
        NativeLazerGenerationIdentity processIdentity,
        ulong? cachedRoot,
        double discoveryMs,
        long discoveryBytes,
        IReadOnlyDictionary<string, IReadOnlyList<NativeLazerFastRefreshResult>> groups,
        IReadOnlyList<NativeLazerTransitionMetrics> transitions,
        NativeLazerRestartMetrics? restart)
    {
        Console.WriteLine("=== NATIVELAZER FAST REFRESH SUMMARY ===");
        Console.WriteLine($"ProcessIdentity={processIdentity.Display}");
        Console.WriteLine($"CachedRoot={FormatRoot(cachedRoot)}");
        Console.WriteLine($"RootDiscoveryMs={discoveryMs:F3}");
        Console.WriteLine($"RootDiscoveryBytesRead={discoveryBytes}");
        foreach ((string label, IReadOnlyList<NativeLazerFastRefreshResult> results) in groups)
        {
            NativeLazerFastRefreshStatistics statistics = NativeLazerFastRefreshStatistics.From(results);
            Console.WriteLine($"{label}:");
            Console.WriteLine($"AvailableCount={statistics.AvailableCount}");
            Console.WriteLine($"UnknownCount={statistics.UnknownCount}");
            Console.WriteLine($"NoBeatmapCount={statistics.NoBeatmapCount}");
            Console.WriteLine($"MinRefreshMs={statistics.MinRefreshMs:F3}");
            Console.WriteLine($"MedianRefreshMs={statistics.MedianRefreshMs:F3}");
            Console.WriteLine($"P95RefreshMs={statistics.P95RefreshMs:F3}");
            Console.WriteLine($"MaxRefreshMs={statistics.MaxRefreshMs:F3}");
            Console.WriteLine($"AverageBytesRead={statistics.AverageBytesRead:F1}");
        }

        Console.WriteLine("Transitions:");
        foreach (NativeLazerTransitionMetrics transition in transitions)
            WriteTransition(transition);

        Console.WriteLine("Restart:");
        if (restart is not null)
        {
            Console.WriteLine($"OldProcessIdentity={restart.OldProcessIdentity.Display}");
            Console.WriteLine($"NewProcessIdentity={restart.NewProcessIdentity.Display}");
            Console.WriteLine($"OldCachedRoot={FormatRoot(restart.OldCachedRoot)}");
            Console.WriteLine($"OldCacheRejected={restart.OldCacheRejected}");
            Console.WriteLine($"HeavyDiscoveryTriggered={restart.HeavyDiscoveryTriggered}");
            Console.WriteLine($"NewCachedRoot={FormatRoot(restart.NewCachedRoot)}");
        }
        else
        {
            Console.WriteLine("unavailable");
        }

        IReadOnlyList<NativeLazerFastRefreshResult> all = groups.Values.SelectMany(group => group).ToArray();
        bool stale = all.Any(result => result.StaleAvailableDetected) ||
                     transitions.Any(transition => transition.StaleAvailableDetected);
        bool generationSafe = restart?.OldCacheRejected ?? all.All(result => result.ProcessIdentity == processIdentity);
        bool failClosed = all.Where(result => !result.RootValid)
            .All(result => result.Outcome == NativeLazerFastOutcome.Unknown && result.BeatmapOnlineId is null);
        Console.WriteLine("Global:");
        Console.WriteLine($"StaleAvailableDetected={stale}");
        Console.WriteLine($"RootCacheGenerationSafe={generationSafe}");
        Console.WriteLine($"RootRevalidationFailClosed={failClosed}");
        int heavyDiscoveryCount = all.Count(result => result.HeavyDiscoveryTriggered);
        if (heavyDiscoveryCount == 0 && discoveryMs > 0)
            heavyDiscoveryCount++;
        if (restart?.HeavyDiscoveryTriggered == true)
            heavyDiscoveryCount++;
        Console.WriteLine($"HeavyDiscoveryCount={heavyDiscoveryCount}");
        Console.WriteLine($"FastRefreshCount={all.Count}");
    }

    private static void WriteTransition(NativeLazerTransitionMetrics metrics)
    {
        Console.WriteLine($"PreviousBeatmapId={metrics.PreviousBeatmapId}");
        Console.WriteLine($"TargetBeatmapId={metrics.TargetBeatmapId}");
        Console.WriteLine($"FirstOldMapGoneMs={FormatNullable(metrics.FirstOldMapGoneMs)}");
        Console.WriteLine($"FirstNewMapAvailableMs={FormatNullable(metrics.FirstNewMapAvailableMs)}");
        Console.WriteLine($"TransientUnknownCount={metrics.TransientUnknownCount}");
        Console.WriteLine($"TransientNoBeatmapCount={metrics.TransientNoBeatmapCount}");
        Console.WriteLine($"StaleAvailableDetected={metrics.StaleAvailableDetected}");
    }

    private static string DominantCost(NativeLazerFastRefreshMetrics metrics)
    {
        (string Name, double Value)[] phases =
        [
            ("RootRevalidation", metrics.RootRevalidationMs),
            ("BeatmapDereference", metrics.BeatmapDereferenceMs),
            ("MetadataRead", metrics.MetadataReadMs),
            ("BlobValidation", metrics.BlobValidationMs)
        ];
        (string name, double value) = phases.MaxBy(phase => phase.Value);
        double accounted = phases.Sum(phase => phase.Value);
        double other = Math.Max(0, metrics.TotalRefreshMs - accounted);
        return other > value ? "other" : name;
    }

    private static int? FirstAvailableId(IEnumerable<NativeLazerFastRefreshResult> results) => results
        .FirstOrDefault(result => result.Outcome == NativeLazerFastOutcome.Available)?.BeatmapOnlineId;

    private static void Prompt(string message)
    {
        Console.WriteLine(message);
        Console.ReadLine();
    }

    private static string FormatRoot(ulong? value) => value.HasValue ? $"0x{value.Value:X}" : "unavailable";
    private static string FormatOffset(ulong? value) => value.HasValue ? $"0x{value.Value:X}" : "n/a";
    private static string FormatNullable<T>(T? value) where T : struct => value?.ToString() ?? "unavailable";
    private static int InvalidMode(string mode)
    {
        Console.Error.WriteLine($"FAIL_CLOSED: unsupported {modeVariable}={mode}; use SINGLE, SESSION, or SERVER.");
        return 3;
    }
}

public sealed record NativeLazerServerResponse
{
    public string? ProcessIdentity { get; init; }
    public int? ProcessId { get; init; }
    public string Outcome { get; init; } = "Unknown";
    public bool RootValid { get; init; }
    public int? BeatmapOnlineId { get; init; }
    public int? BeatmapSetOnlineId { get; init; }
    public string? BeatmapHash { get; init; }
    public string? BeatmapBlobPath { get; init; }
    public bool BlobExists { get; init; }
    public string? FailureReason { get; init; }
    public string? CacheMissReason { get; init; }

    internal static NativeLazerServerResponse From(NativeLazerFastRefreshResult result)
    {
        (string? path, bool exists) = NativeLazerBlobPathResolver.Resolve(result.BeatmapHash);
        return new()
        {
            ProcessIdentity = result.ProcessIdentity.Display,
            ProcessId = result.ProcessIdentity.ProcessId > 0 ? result.ProcessIdentity.ProcessId : null,
            Outcome = result.Outcome.ToString(),
            RootValid = result.RootValid,
            BeatmapOnlineId = result.BeatmapOnlineId,
            BeatmapSetOnlineId = result.BeatmapSetOnlineId,
            BeatmapHash = result.BeatmapHash,
            BeatmapBlobPath = path,
            BlobExists = result.BlobExists == true && exists,
            FailureReason = result.FailureReason,
            CacheMissReason = result.CacheMissReason.ToString()
        };
    }

    internal static NativeLazerServerResponse ProtocolFailure(string reason) => new()
    {
        FailureReason = reason,
        CacheMissReason = "Protocol"
    };
}

internal sealed record NativeLazerRestartMetrics(
    NativeLazerGenerationIdentity OldProcessIdentity,
    NativeLazerGenerationIdentity NewProcessIdentity,
    ulong? OldCachedRoot,
    bool OldCacheRejected,
    bool HeavyDiscoveryTriggered,
    ulong? NewCachedRoot);
