using System.Diagnostics;
using System.IO;

namespace BeatInsight.Diagnostics.NativeStable;

internal static class NativeStableQRestartLiveHarness
{
    internal static NativeStableRestartGeneration CalibrateGenerationB(
        NativeStableQRestartExperimentSession session,
        string mapAPath,
        string mapBPath,
        CancellationToken cancellationToken = default)
    {
        NativeStableQRestartExperiment.EnsurePhase(
            session, NativeStableQRestartPhase.RestartCalibrateA);
        var total = Stopwatch.StartNew();
        var groundTruthTimer = Stopwatch.StartNew();
        NativeStableBeatmapObjectCapture groundTruth =
            NativeStableBeatmapObjectDiscovery.Capture(mapAPath, cancellationToken);
        long groundTruthTicks = groundTruthTimer.Elapsed.Ticks;
        if (groundTruth.ProcessId is null || groundTruth.ProcessStartedAtUtc is null ||
            groundTruth.Metadata is null ||
            groundTruth.Discovery.Status != NativeStableBeatmapObjectDiscoveryStatus.Unique ||
            groundTruth.Discovery.StrongCandidates.Count != 1)
        {
            throw new InvalidDataException(
                "GenerationB map-A ground-truth discovery was not uniquely validated.");
        }
        NativeStableQRestartExperiment.EnsureNewGeneration(
            session.GenerationA.ProcessStartIdentity, groundTruth.ProcessStartedAtUtc.Value);

        NativeStableProcessCandidate? process = NativeStableProcessDiscovery.Discover().UniqueCandidate;
        if (process is null || process.ProcessId != groundTruth.ProcessId.Value)
        {
            throw new InvalidOperationException(
                "osu! process changed after GenerationB ground-truth discovery.");
        }
        DateTimeOffset? currentStart = TryGetProcessStart(process.ProcessId);
        NativeStableProcessGenerationGuard.EnsureSame(
            groundTruth.ProcessId.Value, groundTruth.ProcessStartedAtUtc,
            process.ProcessId, currentStart);
        NativeStableAttachResult attachment = NativeStableProcessMemory.TryAttach(process);
        if (attachment.Memory is null)
        {
            throw new InvalidOperationException($"Read-only attachment failed: {attachment.Status}.");
        }
        using NativeStableProcessMemory memory = attachment.Memory;
        NativeStableSongsPathResult songs = new NativeStableSongsPathResolver()
            .Resolve(process.ExecutablePath!);
        if (songs.Path is null ||
            !NativeStableKnownBeatmapObjectMetadata.TryCreate(
                mapBPath, songs.Path, out NativeStableKnownBeatmapObjectMetadata? mapB))
        {
            throw new InvalidDataException("GenerationB map B could not be validated.");
        }
        IReadOnlyList<NativeStableMemoryRegion> regions = memory.EnumerateReadableRegions();
        IReadOnlyList<NativeStableModuleRange> modules =
            NativeStableModuleCatalog.Capture(process.ProcessId);
        nuint x = groundTruth.Discovery.StrongCandidates.Single().CandidateAddress;
        var searchTimer = Stopwatch.StartNew();
        IReadOnlyList<nuint> qAddresses =
            NativeStableQRestartExperiment.DiscoverGenerationBQCandidates(memory, regions, x);
        long searchTicks = searchTimer.Elapsed.Ticks;
        var characterizationTimings = new NativeStableRestartTimings();
        NativeStableRestartCandidate[] candidates = qAddresses.Select(q =>
        {
            NativeStableQCharacterizationCapture capture = Characterize(
                memory, regions, modules, q, groundTruth.Metadata,
                "RestartCalibrateA", characterizationTimings);
            return new NativeStableRestartCandidate(checked((ulong)q), capture);
        }).ToArray();
        characterizationTimings.GroundTruthObjectDiscoveryTicks = groundTruthTicks;
        characterizationTimings.QCandidateSearchTicks = searchTicks;
        characterizationTimings.TotalTicks = total.Elapsed.Ticks;
        return new()
        {
            ProcessId = process.ProcessId,
            ProcessStartIdentity = currentStart!.Value,
            MapA = NativeStablePersistedMap.FromRuntime(groundTruth.Metadata),
            MapB = NativeStablePersistedMap.FromRuntime(mapB!),
            XMapA = checked((ulong)x),
            XMapB = null,
            AllObservedQAddresses = qAddresses.Select(value => checked((ulong)value)).ToArray(),
            InitialCandidates = candidates,
            SurvivorIndexes = Enumerable.Range(0, candidates.Length).ToArray(),
            TransitionPhases = [],
            Timings = characterizationTimings,
            PresentationLimit = NativeStableQRestartExperiment.DefaultPresentationLimit
        };
    }

    internal static NativeStableRestartTransitionPhase CaptureTransition(
        NativeStableQRestartExperimentSession session,
        NativeStableQRestartPhase phase,
        out int processId,
        out DateTimeOffset? processStartIdentity)
    {
        NativeStableQRestartExperiment.EnsurePhase(session, phase);
        NativeStableRestartGeneration generation = session.GenerationB!;
        NativeStableProcessCandidate? process = NativeStableProcessDiscovery.Discover().UniqueCandidate;
        if (process is null)
        {
            throw new InvalidOperationException("A unique osu!stable process is required.");
        }
        processId = process.ProcessId;
        processStartIdentity = TryGetProcessStart(processId);
        NativeStableProcessGenerationGuard.EnsureSame(
            generation.ProcessId, generation.ProcessStartIdentity,
            processId, processStartIdentity);
        NativeStableAttachResult attachment = NativeStableProcessMemory.TryAttach(process);
        if (attachment.Memory is null)
        {
            throw new InvalidOperationException($"Read-only attachment failed: {attachment.Status}.");
        }
        using NativeStableProcessMemory memory = attachment.Memory;
        IReadOnlyList<NativeStableMemoryRegion> regions = memory.EnumerateReadableRegions();
        IReadOnlyList<NativeStableModuleRange> modules =
            NativeStableModuleCatalog.Capture(processId);
        NativeStableKnownBeatmapObjectMetadata expected = phase switch
        {
            NativeStableQRestartPhase.RestartCaptureB => generation.MapB.ToRuntime(),
            NativeStableQRestartPhase.RestartCaptureA2 => generation.MapA.ToRuntime(),
            _ => throw new InvalidDataException($"Phase {phase} is not a transition phase.")
        };
        var validationTimer = Stopwatch.StartNew();
        NativeStableRestartCandidateTransition[] transitions = generation.SurvivorIndexes
            .Select(index => Validate(
                memory, regions, generation.InitialCandidates[index].InitialQAddress,
                expected, index))
            .ToArray();
        long validationTicks = validationTimer.Elapsed.Ticks;
        var timings = new NativeStableRestartTimings();
        NativeStableQCharacterizationCapture[] characterizations = transitions
            .Where(item => item.ExpectedMapMatches)
            .Select(item => Characterize(
                memory, regions, modules, checked((nuint)item.QAddress), expected,
                phase.ToString(), timings))
            .ToArray();
        return new(
            phase.ToString(), transitions, characterizations,
            validationTicks, timings);
    }

    internal static NativeStableRestartCandidateTransition Validate(
        INativeStableMemoryReader memory,
        IReadOnlyList<NativeStableMemoryRegion> regions,
        ulong qAddress,
        NativeStableKnownBeatmapObjectMetadata expected,
        int candidateIndex)
    {
        NativeStableMemoryReadResult read = memory.ReadBytes(
            checked((nuint)qAddress), sizeof(uint));
        bool pointerReadable = read.Complete && read.Data.Length == sizeof(uint);
        nuint x = pointerReadable ? BitConverter.ToUInt32(read.Data, 0) : 0;
        bool finalObjectReadable = x != 0 && memory.ReadBytes(x, 1).BytesRead == 1;
        NativeStableBeatmapObjectCandidate? validation = finalObjectReadable
            ? NativeStableBeatmapObjectDiscovery.ValidateCandidate(memory, regions, expected, x)
            : null;
        bool idsMatch = validation is not null &&
            (validation.AvailableOptionalIdCount == 0 ||
             validation.MatchedOptionalIdCount == validation.AvailableOptionalIdCount);
        bool matches = validation is not null &&
            validation.MatchedCoreFieldCount == 3 && idsMatch;
        return new(
            candidateIndex,
            qAddress,
            x == 0 ? null : checked((ulong)x),
            pointerReadable,
            finalObjectReadable,
            validation?.MatchedCoreFieldCount ?? 0,
            validation?.MatchedOptionalIdCount ?? 0,
            validation?.AvailableOptionalIdCount ?? expected.AvailableIdCount,
            matches);
    }

    private static NativeStableQCharacterizationCapture Characterize(
        INativeStableMemoryReader memory,
        IReadOnlyList<NativeStableMemoryRegion> regions,
        IReadOnlyList<NativeStableModuleRange> modules,
        nuint q,
        NativeStableKnownBeatmapObjectMetadata expected,
        string phase,
        NativeStableRestartTimings timings)
    {
        var total = Stopwatch.StartNew();
        NativeStableRestartCandidateTransition transition = Validate(
            memory, regions, checked((ulong)q), expected, 0);
        if (!transition.ExpectedMapMatches || transition.XAddress is null)
        {
            throw new NativeStableExpectedMapMismatchException(phase);
        }
        var timer = Stopwatch.StartNew();
        NativeStableObservedAddress qMetadata =
            NativeStableRootCharacterizer.Describe(q, regions, modules);
        timings.QMetadataTicks += timer.Elapsed.Ticks;
        timer.Restart();
        NativeStableStructuralFingerprint qNeighborhood =
            NativeStableRootCharacterizer.Fingerprint(memory, regions, modules, q);
        timings.QNeighborhoodTicks += timer.Elapsed.Ticks;
        timer.Restart();
        IReadOnlyList<nuint> rAddresses = NativeStableRootReverseDiscovery
            .FindAlignedReferences(memory, regions, [q]);
        timings.RReferenceSearchTicks += timer.Elapsed.Ticks;
        timer.Restart();
        NativeStableQReference[] references = rAddresses.Select(address =>
            new NativeStableQReference(
                checked((ulong)address),
                NativeStableRootCharacterizer.Describe(address, regions, modules),
                NativeStableRootCharacterizer.Fingerprint(
                    memory, regions, modules, address))).ToArray();
        NativeStableReferenceCluster[] clusters =
            NativeStableRootCharacterizer.GroupReferences(references);
        timings.RMetadataTicks += timer.Elapsed.Ticks;
        timer.Restart();
        NativeStableThirdLevelReference[] third =
            NativeStableRootCharacterizer.DiscoverThirdLevel(
                memory, regions, modules, references);
        timings.ThirdLevelReferenceSearchTicks += timer.Elapsed.Ticks;
        timings.TotalTicks += total.Elapsed.Ticks;
        return new(
            phase,
            checked((ulong)q),
            transition.XAddress.Value,
            qMetadata,
            qNeighborhood,
            references,
            clusters,
            third,
            0,
            [],
            transition.MatchedCoreFields,
            transition.MatchedOptionalIds,
            transition.AvailableOptionalIds,
            true,
            total.Elapsed.Ticks);
    }

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
