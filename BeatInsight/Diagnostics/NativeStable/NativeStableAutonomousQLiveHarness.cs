using System.Diagnostics;
using System.IO;

namespace BeatInsight.Diagnostics.NativeStable;

internal static class NativeStableAutonomousQLiveHarness
{
    internal static (
        NativeStableAutonomousQLocatorResult Result,
        NativeStableAutonomousHarnessTruth[] Truth) CaptureStartup(
            NativeStableAutonomousQProfile profile,
            string expectedMapPath)
    {
        var total = Stopwatch.StartNew();
        var attachTimer = Stopwatch.StartNew();
        NativeStableProcessCandidate? process = NativeStableProcessDiscovery.Discover().UniqueCandidate;
        if (process is null)
        {
            throw new InvalidOperationException("A unique osu!stable process is required.");
        }
        DateTimeOffset? start = TryGetProcessStart(process.ProcessId);
        if (start is null)
        {
            throw new InvalidOperationException("Process start identity is unavailable.");
        }
        NativeStableAttachResult attachment = NativeStableProcessMemory.TryAttach(process);
        if (attachment.Memory is null)
        {
            throw new InvalidOperationException($"Read-only attachment failed: {attachment.Status}.");
        }
        long attachTicks = attachTimer.Elapsed.Ticks;
        using NativeStableProcessMemory memory = attachment.Memory;
        IReadOnlyList<NativeStableMemoryRegion> regions = memory.EnumerateReadableRegions();
        IReadOnlyList<NativeStableModuleRange> modules =
            NativeStableModuleCatalog.Capture(process.ProcessId);
        var locator = new NativeStableAutonomousQLocator();
        NativeStableAutonomousQLocatorResult result = locator.Locate(
            memory, regions, modules, process.ProcessId, start.Value, profile);
        result = result with
        {
            Timings = result.Timings with
            {
                ProcessAttachTicks = attachTicks,
                TotalTicks = total.Elapsed.Ticks
            }
        };
        NativeStableKnownBeatmapObjectMetadata expected =
            CreateExpectedMetadata(process, expectedMapPath);
        NativeStableAutonomousHarnessTruth[] truth = result.Candidates
            .Select((candidate, index) => ToTruth(
                NativeStableQRestartLiveHarness.Validate(
                    memory, regions, candidate.QAddress, expected, index)))
            .ToArray();
        return (result, truth);
    }

    internal static (
        int ProcessId,
        DateTimeOffset? ProcessStartIdentity,
        NativeStableAutonomousHarnessTruth[] Truth,
        long ValidationTicks) CaptureTransition(
            NativeStableAutonomousGenerationValidation generation,
            string expectedMapPath,
            bool fromB)
    {
        NativeStableProcessCandidate? process = NativeStableProcessDiscovery.Discover().UniqueCandidate;
        if (process is null)
        {
            throw new InvalidOperationException("A unique osu!stable process is required.");
        }
        DateTimeOffset? start = TryGetProcessStart(process.ProcessId);
        NativeStableProcessGenerationGuard.EnsureSame(
            generation.ProcessId, generation.ProcessStartIdentity,
            process.ProcessId, start);
        NativeStableAttachResult attachment = NativeStableProcessMemory.TryAttach(process);
        if (attachment.Memory is null)
        {
            throw new InvalidOperationException($"Read-only attachment failed: {attachment.Status}.");
        }
        using NativeStableProcessMemory memory = attachment.Memory;
        IReadOnlyList<NativeStableMemoryRegion> regions = memory.EnumerateReadableRegions();
        NativeStableKnownBeatmapObjectMetadata expected =
            CreateExpectedMetadata(process, expectedMapPath);
        ulong[] entering = fromB
            ? generation.TruthB.Where(item => item.ExpectedMapMatches)
                .Select(item => item.QAddress).ToArray()
            : generation.TruthA.Where(item => item.ExpectedMapMatches)
                .Select(item => item.QAddress).ToArray();
        var timer = Stopwatch.StartNew();
        NativeStableAutonomousHarnessTruth[] truth = entering
            .Select((q, index) => ToTruth(
                NativeStableQRestartLiveHarness.Validate(
                    memory, regions, q, expected, index)))
            .ToArray();
        return (process.ProcessId, start, truth, timer.Elapsed.Ticks);
    }

    private static NativeStableKnownBeatmapObjectMetadata CreateExpectedMetadata(
        NativeStableProcessCandidate process,
        string mapPath)
    {
        NativeStableSongsPathResult songs = new NativeStableSongsPathResolver()
            .Resolve(process.ExecutablePath!);
        if (songs.Path is null ||
            !NativeStableKnownBeatmapObjectMetadata.TryCreate(
                mapPath, songs.Path, out NativeStableKnownBeatmapObjectMetadata? metadata))
        {
            throw new InvalidDataException("Harness expected map could not be validated.");
        }
        return metadata!;
    }

    private static NativeStableAutonomousHarnessTruth ToTruth(
        NativeStableRestartCandidateTransition value) => new(
            value.QAddress, value.XAddress, value.PointerReadable,
            value.FinalObjectReadable, value.MatchedCoreFields,
            value.MatchedOptionalIds, value.AvailableOptionalIds,
            value.ExpectedMapMatches);

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
