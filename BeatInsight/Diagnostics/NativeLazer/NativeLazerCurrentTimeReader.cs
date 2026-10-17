namespace BeatInsight.Diagnostics.NativeLazer;

/// <summary>
/// Lightweight read-only path from one already validated NativeLazer root to the
/// active gameplay clock. It never discovers a root or resolves beatmap data.
/// </summary>
internal sealed class NativeLazerCurrentTimeReader
{
    private readonly NativeLazerGameplayClockDiscovery discovery = new();

    internal NativeLazerGameplayClockResult TryReadCurrentTime(
        NativeLazerGenerationIdentity generation,
        NativeLazerGenerationIdentity validatedGeneration,
        ulong validatedRootAddress,
        NativeLazerGameplayClockLayout validatedLayout,
        INativeLazerGameplayClockMemory memory,
        Func<NativeLazerGenerationIdentity, bool> generationStillCurrent,
        double? knownBeatmapDurationMilliseconds = null) =>
        discovery.Discover(
            generation,
            validatedGeneration,
            validatedRootAddress,
            validatedLayout,
            memory,
            knownBeatmapDurationMilliseconds,
            generationStillCurrent);
}

public sealed record NativeLazerGameplayPositionResponse
{
    public string? ProcessIdentity { get; init; }
    public int? ProcessId { get; init; }
    public string Outcome { get; init; } = NativeLazerGameplayClockOutcome.Unknown.ToString();
    public double? CurrentTimeMilliseconds { get; init; }
    public bool? IsPaused { get; init; }
    public string? AuthoritativeSource { get; init; }
    public string? FailureReason { get; init; }
    public double DurationMs { get; init; }
    public long BytesRead { get; init; }
    public int ReadOperations { get; init; }
    public int PointerDereferences { get; init; }

    internal static NativeLazerGameplayPositionResponse From(NativeLazerGameplayClockResult result) => new()
    {
        ProcessIdentity = result.Generation.Display,
        ProcessId = result.Generation.ProcessId > 0 ? result.Generation.ProcessId : null,
        Outcome = result.Outcome.ToString(),
        CurrentTimeMilliseconds = result.Outcome == NativeLazerGameplayClockOutcome.Unique
            ? result.CurrentTimeMilliseconds
            : null,
        IsPaused = result.IsPaused,
        AuthoritativeSource = result.AuthoritativeSource,
        FailureReason = result.FailureReason,
        DurationMs = result.Metrics.DurationMs,
        BytesRead = result.Metrics.BytesRead,
        ReadOperations = result.Metrics.ReadOperations,
        PointerDereferences = result.Metrics.PointerDereferences
    };

    internal static NativeLazerGameplayPositionResponse Unavailable(
        NativeLazerGenerationIdentity generation,
        string reason) => new()
    {
        ProcessIdentity = generation.Display,
        ProcessId = generation.ProcessId > 0 ? generation.ProcessId : null,
        FailureReason = reason
    };
}
