using System.Diagnostics;

namespace BeatInsight.Diagnostics.NativeLazer;

internal enum NativeLazerGameRoute
{
    Unknown,
    SongSelect,
    Gameplay,
    Results
}

internal sealed record NativeLazerGameRouteMethodTables(
    NativeLazerGenerationIdentity Generation,
    ulong? SoloSongSelect,
    ulong? SoloPlayer,
    ulong? SoloResultsScreen)
{
    internal bool HasAny =>
        SoloSongSelect.HasValue || SoloPlayer.HasValue || SoloResultsScreen.HasValue;

    internal bool HasMissing =>
        !SoloSongSelect.HasValue || !SoloPlayer.HasValue || !SoloResultsScreen.HasValue;

    internal bool IsCoherent =>
        HasAny &&
        IsValid(SoloSongSelect) &&
        IsValid(SoloPlayer) &&
        IsValid(SoloResultsScreen) &&
        KnownValues().Distinct().Count() == KnownValues().Count();

    internal bool Contains(ulong methodTable) =>
        SoloSongSelect == methodTable ||
        SoloPlayer == methodTable ||
        SoloResultsScreen == methodTable;

    internal NativeLazerGameRouteMethodTables Merge(
        NativeLazerGameRouteMethodTables update)
    {
        if (update.Generation != Generation)
            return this;

        return new(
            Generation,
            SoloSongSelect ?? update.SoloSongSelect,
            SoloPlayer ?? update.SoloPlayer,
            SoloResultsScreen ?? update.SoloResultsScreen);
    }

    internal bool ConflictsWith(NativeLazerGameRouteMethodTables update) =>
        update.Generation != Generation ||
        Conflicts(SoloSongSelect, update.SoloSongSelect) ||
        Conflicts(SoloPlayer, update.SoloPlayer) ||
        Conflicts(SoloResultsScreen, update.SoloResultsScreen);

    private IReadOnlyList<ulong> KnownValues() =>
        new ulong?[] { SoloSongSelect, SoloPlayer, SoloResultsScreen }
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();

    private static bool IsValid(ulong? value) => value is null or >= 0x10000;

    private static bool Conflicts(ulong? current, ulong? update) =>
        current.HasValue && update.HasValue && current != update;
}

internal sealed record NativeLazerGameRouteResolution(
    NativeLazerGenerationIdentity Generation,
    NativeLazerGameRouteMethodTables? MethodTables,
    double DurationMs,
    string? FailureReason)
{
    internal int InvocationCount { get; init; } = 1;

    internal bool IsAvailable => MethodTables is { IsCoherent: true } tables &&
        tables.Generation == Generation;
}

internal sealed class NativeLazerGameRouteMethodTableState
{
    private const int MaximumResolutionAttemptsPerTop = 2;
    private ulong? lastObservedTopMethodTable;
    private int resolutionAttemptsForTop;

    internal NativeLazerGameRouteResolution? Current { get; private set; }

    internal void Initialize(NativeLazerGameRouteResolution resolution)
    {
        Current = resolution;
        lastObservedTopMethodTable = null;
        resolutionAttemptsForTop = 0;
    }

    internal bool TryEnrich(
        NativeLazerGenerationIdentity generation,
        ulong topMethodTable,
        Func<NativeLazerGameRouteResolution> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        NativeLazerGameRouteResolution? current = Current;
        bool topChanged = lastObservedTopMethodTable != topMethodTable;
        if (topChanged)
        {
            lastObservedTopMethodTable = topMethodTable;
            resolutionAttemptsForTop = 0;
        }
        if (current is null ||
            current.Generation != generation ||
            current.MethodTables is { IsCoherent: false } ||
            current.MethodTables is { HasMissing: false } ||
            current.MethodTables?.Contains(topMethodTable) == true ||
            resolutionAttemptsForTop >= MaximumResolutionAttemptsPerTop)
        {
            return false;
        }

        resolutionAttemptsForTop++;
        NativeLazerGameRouteResolution update = resolve();
        Current = Merge(current, update);
        return true;
    }

    internal void ObserveKnownTop(ulong? topMethodTable)
    {
        if (topMethodTable.HasValue)
        {
            lastObservedTopMethodTable = topMethodTable.Value;
            resolutionAttemptsForTop = 0;
        }
    }

    internal void Clear()
    {
        Current = null;
        lastObservedTopMethodTable = null;
        resolutionAttemptsForTop = 0;
    }

    private static NativeLazerGameRouteResolution Merge(
        NativeLazerGameRouteResolution current,
        NativeLazerGameRouteResolution update)
    {
        int invocationCount = current.InvocationCount + Math.Max(1, update.InvocationCount);
        double durationMs = current.DurationMs + update.DurationMs;
        if (update.Generation != current.Generation)
        {
            return current with
            {
                DurationMs = durationMs,
                FailureReason = "MethodTable enrichment returned a different process generation.",
                InvocationCount = invocationCount
            };
        }

        NativeLazerGameRouteMethodTables? previous = current.MethodTables;
        NativeLazerGameRouteMethodTables? discovered = update.MethodTables;
        if (discovered is null)
        {
            return current with
            {
                DurationMs = durationMs,
                FailureReason = update.FailureReason ??
                    "MethodTable enrichment produced no usable route entries.",
                InvocationCount = invocationCount
            };
        }
        if (previous is null)
        {
            return new(
                current.Generation,
                discovered,
                durationMs,
                update.FailureReason)
            {
                InvocationCount = invocationCount
            };
        }
        if (previous.ConflictsWith(discovered))
        {
            return current with
            {
                DurationMs = durationMs,
                FailureReason = "MethodTable enrichment conflicted with the current generation table.",
                InvocationCount = invocationCount
            };
        }

        NativeLazerGameRouteMethodTables merged = previous.Merge(discovered);
        bool changed = merged != previous;
        return new(
            current.Generation,
            merged,
            durationMs,
            changed ? null : update.FailureReason ??
                "MethodTable enrichment produced no new route entries.")
        {
            InvocationCount = invocationCount
        };
    }
}

internal interface INativeLazerGameRouteMethodTableResolver
{
    NativeLazerGameRouteResolution Resolve(
        NativeLazerProcessCandidate process,
        NativeLazerGenerationIdentity generation);
}

internal sealed record NativeLazerGameRouteResult
{
    internal required NativeLazerGenerationIdentity Generation { get; init; }
    internal NativeLazerGameRoute Route { get; init; }
    internal DateTimeOffset ObservedAtUtc { get; init; }
    internal string? FailureReason { get; init; }
    internal ulong? TopMethodTable { get; init; }
    internal required NativeLazerScreenStackMetrics Metrics { get; init; }
}

internal sealed class NativeLazerGameRouteReader
{
    private readonly NativeLazerScreenStackReader screenStackReader = new();

    internal NativeLazerGameRouteResult Read(
        NativeLazerGenerationIdentity generation,
        NativeLazerGenerationIdentity validatedGeneration,
        ulong validatedRootAddress,
        NativeLazerGameplayClockLayout layout,
        NativeLazerGameRouteMethodTables? methodTables,
        INativeLazerGameplayClockMemory memory,
        Func<NativeLazerGenerationIdentity, bool> generationStillCurrent)
    {
        long started = Stopwatch.GetTimestamp();
        long initialBytes = memory.BytesRead;
        int initialOperations = memory.ReadOperationCount;
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;

        NativeLazerGameRouteResult Unknown(string reason) => new()
        {
            Generation = generation,
            Route = NativeLazerGameRoute.Unknown,
            ObservedAtUtc = observedAt,
            FailureReason = reason,
            Metrics = new(
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                memory.BytesRead - initialBytes,
                memory.ReadOperationCount - initialOperations,
                0)
        };

        if (generation != validatedGeneration || !generationStillCurrent(generation))
            return Unknown("Process generation is not current.");
        if (methodTables is { } suppliedTables && suppliedTables.Generation != generation)
            return Unknown("MethodTable mapping belongs to another process generation.");
        NativeLazerScreenStackSample stack = screenStackReader.Read(
            generation,
            validatedGeneration,
            validatedRootAddress,
            layout,
            memory,
            generationStillCurrent);
        if (stack.Outcome != NativeLazerScreenStackOutcome.Unique)
            return Unknown(stack.FailureReason ?? "ScreenStack read failed.");

        NativeLazerScreenStackEntry? top = stack.Entries.LastOrDefault(entry => entry.IsTop);
        if (top is null)
            return Unknown("ScreenStack top object is unavailable.");

        if (methodTables is not { IsCoherent: true } tables ||
            tables.Generation != generation)
        {
            return UnknownWithTop(
                "No coherent MethodTable mapping exists for this process generation.",
                top.MethodTable,
                stack);
        }

        NativeLazerGameRoute route = top.MethodTable switch
        {
            var value when value == tables.SoloSongSelect => NativeLazerGameRoute.SongSelect,
            var value when value == tables.SoloPlayer => NativeLazerGameRoute.Gameplay,
            var value when value == tables.SoloResultsScreen => NativeLazerGameRoute.Results,
            _ => NativeLazerGameRoute.Unknown
        };
        return new()
        {
            Generation = generation,
            Route = route,
            ObservedAtUtc = stack.ObservedAtUtc,
            TopMethodTable = top.MethodTable,
            FailureReason = route == NativeLazerGameRoute.Unknown
                ? "ScreenStack top MethodTable is unknown for this generation."
                : null,
            Metrics = stack.Metrics
        };

        NativeLazerGameRouteResult UnknownWithTop(
            string reason,
            ulong topMethodTable,
            NativeLazerScreenStackSample sample) => new()
        {
            Generation = generation,
            Route = NativeLazerGameRoute.Unknown,
            ObservedAtUtc = sample.ObservedAtUtc,
            TopMethodTable = topMethodTable,
            FailureReason = reason,
            Metrics = sample.Metrics
        };
    }
}

public sealed record NativeLazerGameRouteResponse
{
    public string? ProcessIdentity { get; init; }
    public int? ProcessId { get; init; }
    public string Route { get; init; } = "Unknown";
    public double DurationMs { get; init; }
    public long BytesRead { get; init; }
    public int ReadOperationCount { get; init; }
    public double? MethodTableResolutionMs { get; init; }
    public int MethodTableResolutionCount { get; init; }
    public string? FailureReason { get; init; }

    internal static NativeLazerGameRouteResponse From(
        NativeLazerGameRouteResult result,
        NativeLazerGameRouteResolution? resolution) => new()
    {
        ProcessIdentity = result.Generation.Display,
        ProcessId = result.Generation.ProcessId > 0 ? result.Generation.ProcessId : null,
        Route = result.Route.ToString(),
        DurationMs = result.Metrics.DurationMs,
        BytesRead = result.Metrics.BytesRead,
        ReadOperationCount = result.Metrics.ReadOperations,
        MethodTableResolutionMs = resolution?.DurationMs,
        MethodTableResolutionCount = resolution?.InvocationCount ?? 0,
        FailureReason = result.Route == NativeLazerGameRoute.Unknown
            ? resolution?.FailureReason ?? result.FailureReason
            : null
    };

    internal static NativeLazerGameRouteResponse Unavailable(
        NativeLazerGenerationIdentity generation,
        string reason,
        NativeLazerGameRouteResolution? resolution = null) => new()
    {
        ProcessIdentity = generation.ProcessId > 0 ? generation.Display : null,
        ProcessId = generation.ProcessId > 0 ? generation.ProcessId : null,
        MethodTableResolutionMs = resolution?.DurationMs,
        MethodTableResolutionCount = resolution?.InvocationCount ?? 0,
        FailureReason = reason
    };
}
