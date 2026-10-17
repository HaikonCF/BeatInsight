using System.Diagnostics;

namespace BeatInsight.Diagnostics.NativeStable;

/// <summary>
/// Explicitly invoked manual-live session. Calibration is performed once on A1;
/// subsequent phases evaluate only the saved root-slot candidates.
/// </summary>
internal sealed class NativeStableRootReverseDiscoverySession : IDisposable
{
    private readonly NativeStableProcessMemory memory;
    private readonly NativeStableKnownBeatmapObjectMetadata mapA;
    private readonly NativeStableKnownBeatmapObjectMetadata mapB;
    private readonly List<NativeStableRootTransitionCapture> captures = [];

    private NativeStableRootReverseDiscoverySession(
        NativeStableProcessMemory memory,
        int processId,
        DateTimeOffset? processStartIdentity,
        NativeStableKnownBeatmapObjectMetadata mapA,
        NativeStableKnownBeatmapObjectMetadata mapB,
        NativeStableRootCalibration calibration,
        TimeSpan groundTruthObjectDiscovery,
        TimeSpan totalCalibration)
    {
        this.memory = memory;
        ProcessId = processId;
        ProcessStartIdentity = processStartIdentity;
        this.mapA = mapA;
        this.mapB = mapB;
        Calibration = calibration;
        GroundTruthObjectDiscovery = groundTruthObjectDiscovery;
        TotalCalibration = totalCalibration;
    }

    internal int ProcessId { get; }
    internal DateTimeOffset? ProcessStartIdentity { get; }
    internal NativeStableKnownBeatmapObjectMetadata MapA => mapA;
    internal NativeStableKnownBeatmapObjectMetadata MapB => mapB;
    internal NativeStableRootCalibration Calibration { get; }
    internal TimeSpan GroundTruthObjectDiscovery { get; }
    internal TimeSpan TotalCalibration { get; }
    internal IReadOnlyList<NativeStableRootTransitionCapture> Captures => captures;

    internal static NativeStableRootReverseDiscoverySession? TryCalibrate(
        string mapAPath,
        string mapBPath,
        out string? failure,
        CancellationToken cancellationToken = default)
    {
        failure = null;
        var total = Stopwatch.StartNew();
        var groundTruthTimer = Stopwatch.StartNew();
        NativeStableBeatmapObjectCapture groundTruth =
            NativeStableBeatmapObjectDiscovery.Capture(mapAPath, cancellationToken);
        TimeSpan groundTruthTime = groundTruthTimer.Elapsed;
        if (groundTruth.ProcessId is null || groundTruth.Metadata is null ||
            groundTruth.Discovery.Status != NativeStableBeatmapObjectDiscoveryStatus.Unique ||
            groundTruth.Discovery.StrongCandidates.Count != 1)
        {
            failure = "A1 ground-truth object discovery was not unique and fully validated.";
            return null;
        }

        NativeStableProcessDiscoveryResult processDiscovery =
            NativeStableProcessDiscovery.Discover();
        NativeStableProcessCandidate? process = processDiscovery.UniqueCandidate;
        if (process is null || process.ProcessId != groundTruth.ProcessId.Value)
        {
            failure = "The osu! process changed after A1 ground-truth discovery.";
            return null;
        }

        DateTimeOffset? currentStart = TryGetProcessStart(process.ProcessId);
        try
        {
            NativeStableProcessGenerationGuard.EnsureSame(
                groundTruth.ProcessId.Value,
                groundTruth.ProcessStartedAtUtc,
                process.ProcessId,
                currentStart);
        }
        catch (NativeStableProcessGenerationChangedException exception)
        {
            failure = exception.Message;
            return null;
        }

        NativeStableAttachResult attachment = NativeStableProcessMemory.TryAttach(process);
        if (attachment.Memory is null)
        {
            failure = $"Read-only attachment failed: {attachment.Status}.";
            return null;
        }

        NativeStableProcessMemory memory = attachment.Memory;
        try
        {
            NativeStableSongsPathResult songs = new NativeStableSongsPathResolver()
                .Resolve(process.ExecutablePath!);
            if (songs.Path is null ||
                !NativeStableKnownBeatmapObjectMetadata.TryCreate(
                    mapBPath, songs.Path, out NativeStableKnownBeatmapObjectMetadata? mapB))
            {
                failure = "Map B or the osu! Songs path could not be validated.";
                memory.Dispose();
                return null;
            }

            IReadOnlyList<NativeStableMemoryRegion> regions = memory.EnumerateReadableRegions();
            nuint x = groundTruth.Discovery.StrongCandidates.Single().CandidateAddress;
            NativeStableRootCalibration calibration = NativeStableRootReverseDiscovery.Calibrate(
                memory, regions, x, cancellationToken: cancellationToken);
            return new(
                memory,
                process.ProcessId,
                currentStart,
                groundTruth.Metadata,
                mapB!,
                calibration,
                groundTruthTime,
                total.Elapsed);
        }
        catch
        {
            memory.Dispose();
            throw;
        }
    }

    internal NativeStableRootTransitionCapture Capture(string phase, string expectedMap)
    {
        if (!memory.IsAlive)
        {
            throw new NativeStableProcessGenerationChangedException(
                ProcessId, ProcessStartIdentity, ProcessId, null);
        }

        DateTimeOffset? currentStart = TryGetProcessStart(ProcessId);
        NativeStableProcessGenerationGuard.EnsureSame(
            ProcessId, ProcessStartIdentity, ProcessId, currentStart);
        NativeStableKnownBeatmapObjectMetadata metadata = expectedMap switch
        {
            "A" => mapA,
            "B" => mapB,
            _ => throw new ArgumentOutOfRangeException(nameof(expectedMap))
        };
        NativeStableRootTransitionCapture capture =
            NativeStableRootTransitionValidator.Capture(
                memory,
                memory.EnumerateReadableRegions(),
                Calibration.Chains,
                metadata,
                phase);
        captures.Add(capture);
        return capture;
    }

    internal NativeStableRootTransitionSummary Summarize() =>
        NativeStableRootTransitionValidator.Summarize(Calibration.Chains, captures);

    public void Dispose() => memory.Dispose();

    private static DateTimeOffset? TryGetProcessStart(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return process.StartTime.ToUniversalTime();
        }
        catch (Exception exception) when (exception is ArgumentException or
            InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
