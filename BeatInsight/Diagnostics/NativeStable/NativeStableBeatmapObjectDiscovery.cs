using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BeatInsight.Diagnostics.NativeStable;

/// <summary>
/// EXPERIMENTAL — V3 PROOF OF CONCEPT — NOT PRODUCTION INTEGRATION.
/// Tests caller-supplied beatmap-object field offsets using read-only evidence.
/// </summary>
internal static class NativeStableBeatmapObjectDiscovery
{
    internal const nuint ChecksumOffset = 0x6c;
    internal const nuint FolderOffset = 0x78;
    internal const nuint FilenameOffset = 0x90;
    internal const nuint BeatmapIdOffset = 0xc8;
    internal const nuint BeatmapSetIdOffset = 0xcc;
    private const int ChunkBytes = 1024 * 1024;

    internal static NativeStableBeatmapObjectCapture Capture(
        string knownOsuPath,
        CancellationToken cancellationToken = default)
    {
        string captureId = Guid.NewGuid().ToString("N");
        DateTimeOffset observedAtUtc = DateTimeOffset.UtcNow;
        var total = Stopwatch.StartNew();
        var phase = Stopwatch.StartNew();
        NativeStableProcessDiscoveryResult processDiscovery =
            NativeStableProcessDiscovery.Discover();
        TimeSpan processDiscoveryTime = phase.Elapsed;
        if (processDiscovery.Status != NativeStableAttachmentStatus.Discovered ||
            processDiscovery.UniqueCandidate is null)
        {
            return Unavailable(captureId, observedAtUtc, processDiscovery.Status,
                processDiscoveryTime, total.Elapsed,
                $"Process discovery failed: {processDiscovery.Status}.");
        }

        NativeStableProcessCandidate process = processDiscovery.UniqueCandidate;
        NativeStableAttachResult attachment = NativeStableProcessMemory.TryAttach(process);
        if (attachment.Memory is null)
        {
            return Unavailable(captureId, observedAtUtc, attachment.Status,
                processDiscoveryTime, total.Elapsed,
                $"Read-only attachment failed: {attachment.Status}.");
        }

        using NativeStableProcessMemory memory = attachment.Memory;
        NativeStableSongsPathResult songs = new NativeStableSongsPathResolver()
            .Resolve(process.ExecutablePath!);
        if (songs.Path is null ||
            !NativeStableKnownBeatmapObjectMetadata.TryCreate(
                knownOsuPath, songs.Path, out NativeStableKnownBeatmapObjectMetadata? metadata))
        {
            return Unavailable(captureId, observedAtUtc, attachment.Status,
                processDiscoveryTime, total.Elapsed,
                "The supplied .osu file or Songs path could not be validated.");
        }

        phase.Restart();
        IReadOnlyList<NativeStableMemoryRegion> regions = memory.EnumerateReadableRegions();
        TimeSpan regionTime = phase.Elapsed;
        NativeStablePreparedKnownMap prepared = new(
            "MAP", metadata!.Checksum, metadata.Filename, metadata.Folder, metadata.KnownMapPath);
        IReadOnlyList<NativeStableProbeSignal> definitions =
            NativeStablePoc.BuildSignalDefinitions([prepared]);
        NativeStablePatternSearchRequest[] requests = definitions
            .Select((signal, index) => new NativeStablePatternSearchRequest(
                index.ToString(CultureInfo.InvariantCulture),
                BytePattern.FromBytes(signal.KnownBytes)))
            .ToArray();

        phase.Restart();
        IReadOnlyDictionary<string, IReadOnlyList<nuint>> matches =
            NativeStableProcessPatternSearch.FindAllMany(
                memory, regions, requests, cancellationToken);
        NativeStableProbeSignal[] signals = definitions
            .Select((signal, index) => signal with
            {
                Addresses = matches[index.ToString(CultureInfo.InvariantCulture)]
            })
            .ToArray();
        TimeSpan signalTime = phase.Elapsed;

        NativeStableBeatmapObjectDiscoveryResult result = Discover(
            memory, regions, metadata, signals, cancellationToken);
        DateTimeOffset? processStartedAtUtc = TryGetProcessStart(process.ProcessId);
        NativeStableBeatmapObjectTimings timings = result.Timings with
        {
            ProcessDiscovery = processDiscoveryTime,
            MemoryRegionEnumeration = regionTime,
            KnownSignalSearch = signalTime,
            Total = total.Elapsed
        };
        return new(captureId, observedAtUtc, process.ProcessId, processStartedAtUtc,
            metadata, signals, result with { Timings = timings }, attachment.Status, null);
    }

    internal static NativeStableBeatmapObjectDiscoveryResult Discover(
        INativeStableMemoryReader memory,
        IReadOnlyList<NativeStableMemoryRegion> regions,
        NativeStableKnownBeatmapObjectMetadata metadata,
        IReadOnlyList<NativeStableProbeSignal> signals,
        CancellationToken cancellationToken = default)
    {
        if (!memory.Is32Bit)
        {
            return NativeStableBeatmapObjectDiscoveryResult.UnsupportedArchitecture;
        }

        var reverseTimer = Stopwatch.StartNew();
        IReadOnlyList<NativeStableManagedStringTarget> targets = BuildManagedStringTargets(
            memory, metadata, signals);
        IReadOnlyList<NativeStableBeatmapObjectReference> references = FindReferences(
            memory, regions, targets, cancellationToken);
        TimeSpan reverseTime = reverseTimer.Elapsed;

        var validationTimer = Stopwatch.StartNew();
        Dictionary<nuint, HashSet<NativeStableKnownSignalKind>> implied = [];
        foreach (NativeStableBeatmapObjectReference reference in references)
        {
            nuint fieldOffset = Offset(reference.Kind);
            if (reference.ReferenceAddress < fieldOffset)
            {
                continue;
            }

            nuint candidateAddress = reference.ReferenceAddress - fieldOffset;
            if (!implied.TryGetValue(candidateAddress,
                    out HashSet<NativeStableKnownSignalKind>? kinds))
            {
                kinds = [];
                implied[candidateAddress] = kinds;
            }
            kinds.Add(reference.Kind);
        }

        NativeStableBeatmapObjectCandidate[] candidates = implied.Keys
            .OrderBy(address => address)
            .Select(address => ValidateCandidate(memory, regions, metadata, address))
            .Where(candidate => candidate.MatchedCoreFieldCount > 0)
            .ToArray();
        TimeSpan validationTime = validationTimer.Elapsed;
        int strongCount = candidates.Count(candidate => candidate.MatchedCoreFieldCount == 3);
        NativeStableBeatmapObjectDiscoveryStatus status = strongCount switch
        {
            0 => NativeStableBeatmapObjectDiscoveryStatus.None,
            1 => NativeStableBeatmapObjectDiscoveryStatus.Unique,
            _ => NativeStableBeatmapObjectDiscoveryStatus.Ambiguous
        };
        int availableIds = metadata.AvailableIdCount;
        var statistics = new NativeStableBeatmapObjectStatistics(
            references.Count(reference => reference.Kind == NativeStableKnownSignalKind.Checksum),
            references.Count(reference => reference.Kind == NativeStableKnownSignalKind.Folder),
            references.Count(reference => reference.Kind == NativeStableKnownSignalKind.Filename),
            ImpliedBaseCount(references, NativeStableKnownSignalKind.Checksum),
            ImpliedBaseCount(references, NativeStableKnownSignalKind.Folder),
            ImpliedBaseCount(references, NativeStableKnownSignalKind.Filename),
            candidates.Count(candidate => candidate.MatchedCoreFieldCount >= 2),
            strongCount,
            availableIds == 0 ? 0 : candidates.Count(candidate =>
                candidate.MatchedCoreFieldCount == 3 &&
                candidate.MatchedOptionalIdCount == availableIds));

        nuint? checksumRaw = FirstRawUtf16(signals, NativeStableKnownSignalKind.Checksum);
        nuint? filenameRaw = FirstRawUtf16(signals, NativeStableKnownSignalKind.Filename);
        long? rawDelta = checksumRaw is not null && filenameRaw is not null &&
            NativeStableCandidateCorrelationAnalyzer.TrySignedDelta(
                checksumRaw.Value, filenameRaw.Value, out long delta)
            ? delta
            : null;
        return new(status, candidates, references, targets, statistics,
            checksumRaw, filenameRaw, rawDelta,
            new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
                reverseTime, validationTime, reverseTime + validationTime));
    }

    private static IReadOnlyList<NativeStableManagedStringTarget> BuildManagedStringTargets(
        INativeStableMemoryReader memory,
        NativeStableKnownBeatmapObjectMetadata metadata,
        IReadOnlyList<NativeStableProbeSignal> signals)
    {
        var targets = new List<NativeStableManagedStringTarget>();
        int characterOffset = NativeStableManagedStringReader.CharacterDataOffset(memory.Is32Bit);
        foreach (NativeStableProbeSignal signal in signals.Where(signal =>
                     signal.Encoding == NativeStableTextEncoding.Utf16))
        {
            string expected = signal.Kind switch
            {
                NativeStableKnownSignalKind.Checksum => metadata.Checksum,
                NativeStableKnownSignalKind.Folder => metadata.Folder,
                NativeStableKnownSignalKind.Filename => metadata.Filename,
                _ => string.Empty
            };
            foreach (nuint rawAddress in signal.Addresses)
            {
                if (rawAddress < checked((nuint)characterOffset))
                {
                    continue;
                }

                nuint objectAddress = rawAddress - checked((nuint)characterOffset);
                if (NativeStableManagedStringReader.TryRead(
                        memory, objectAddress, out string? value) &&
                    value.Equals(expected, StringComparison.Ordinal))
                {
                    targets.Add(new(signal.Kind, objectAddress, rawAddress, expected));
                }
            }
        }

        return targets.Distinct().OrderBy(target => target.ObjectAddress).ToArray();
    }

    private static IReadOnlyList<NativeStableBeatmapObjectReference> FindReferences(
        INativeStableMemoryReader memory,
        IReadOnlyList<NativeStableMemoryRegion> regions,
        IReadOnlyList<NativeStableManagedStringTarget> targets,
        CancellationToken cancellationToken)
    {
        if (targets.Count == 0)
        {
            return [];
        }

        var byAddress = targets.GroupBy(target => target.ObjectAddress)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var references = new List<NativeStableBeatmapObjectReference>();
        int pointerSize = memory.Is32Bit ? sizeof(uint) : sizeof(ulong);
        foreach (NativeStableMemoryRegion region in regions)
        {
            nuint offset = 0;
            while (offset < region.Size)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = region.Size - offset > ChunkBytes
                    ? ChunkBytes
                    : checked((int)(region.Size - offset));
                nuint bufferAddress = region.BaseAddress + offset;
                NativeStableMemoryReadResult read = memory.ReadBytes(bufferAddress, count);
                if (read.BytesRead > 0)
                {
                    int first = AlignmentOffset(bufferAddress, pointerSize);
                    for (int index = first;
                         index <= read.Data.Length - pointerSize;
                         index += pointerSize)
                    {
                        nuint value = memory.Is32Bit
                            ? BitConverter.ToUInt32(read.Data, index)
                            : checked((nuint)BitConverter.ToUInt64(read.Data, index));
                        if (!byAddress.TryGetValue(value,
                                out NativeStableManagedStringTarget[]? found))
                        {
                            continue;
                        }

                        foreach (NativeStableManagedStringTarget target in found)
                        {
                            references.Add(new(
                                target.Kind,
                                bufferAddress + checked((nuint)index),
                                value,
                                target.RawCharacterAddress));
                        }
                    }
                }
                offset += checked((nuint)count);
            }
        }

        return references.Distinct().OrderBy(reference => reference.ReferenceAddress).ToArray();
    }

    internal static NativeStableBeatmapObjectCandidate ValidateCandidate(
        INativeStableMemoryReader memory,
        IReadOnlyList<NativeStableMemoryRegion> regions,
        NativeStableKnownBeatmapObjectMetadata metadata,
        nuint address)
    {
        NativeStableMemoryRegion? region = regions
            .Where(item => item.Contains(address))
            .Select(item => (NativeStableMemoryRegion?)item)
            .FirstOrDefault();
        NativeStableBeatmapObjectFieldEvidence checksum = ValidateField(
            memory, address, ChecksumOffset, metadata.Checksum);
        NativeStableBeatmapObjectFieldEvidence folder = ValidateField(
            memory, address, FolderOffset, metadata.Folder);
        NativeStableBeatmapObjectFieldEvidence filename = ValidateField(
            memory, address, FilenameOffset, metadata.Filename);
        int? observedBeatmapId = TryReadInt32(memory, address + BeatmapIdOffset);
        int? observedBeatmapSetId = TryReadInt32(memory, address + BeatmapSetIdOffset);
        bool? beatmapIdMatches = metadata.BeatmapId is null
            ? null : observedBeatmapId == metadata.BeatmapId;
        bool? beatmapSetIdMatches = metadata.BeatmapSetId is null
            ? null : observedBeatmapSetId == metadata.BeatmapSetId;
        int coreMatches = new[] { checksum.Matches, folder.Matches, filename.Matches }
            .Count(matches => matches);
        int optionalMatches = new[] { beatmapIdMatches, beatmapSetIdMatches }
            .Count(matches => matches == true);
        int availableIds = metadata.AvailableIdCount;
        NativeStableBeatmapObjectCandidateLevel level = coreMatches switch
        {
            3 when availableIds > 0 && optionalMatches == availableIds =>
                NativeStableBeatmapObjectCandidateLevel.StrongCandidateWithIds,
            3 => NativeStableBeatmapObjectCandidateLevel.StrongCandidate,
            2 => NativeStableBeatmapObjectCandidateLevel.PartialCandidate,
            _ => NativeStableBeatmapObjectCandidateLevel.WeakCandidate
        };
        return new(address, checksum, folder, filename,
            observedBeatmapId, metadata.BeatmapId, beatmapIdMatches,
            observedBeatmapSetId, metadata.BeatmapSetId, beatmapSetIdMatches,
            coreMatches, optionalMatches, availableIds, level, region);
    }

    private static NativeStableBeatmapObjectFieldEvidence ValidateField(
        INativeStableMemoryReader memory,
        nuint candidateAddress,
        nuint offset,
        string expected)
    {
        nuint fieldAddress = candidateAddress + offset;
        bool referenceReadable = TryReadPointerValue(memory, fieldAddress, out nuint reference);
        bool matches = referenceReadable && reference != 0 &&
            NativeStableManagedStringReader.TryRead(memory, reference, out string? value) &&
            value.Equals(expected, StringComparison.Ordinal);
        return new(fieldAddress, referenceReadable ? reference : null, matches);
    }

    private static bool TryReadPointerValue(
        INativeStableMemoryReader memory,
        nuint address,
        out nuint value)
    {
        int size = memory.Is32Bit ? sizeof(uint) : sizeof(ulong);
        NativeStableMemoryReadResult read = memory.ReadBytes(address, size);
        if (!read.Complete || read.Data.Length != size)
        {
            value = 0;
            return false;
        }
        value = memory.Is32Bit
            ? BitConverter.ToUInt32(read.Data, 0)
            : checked((nuint)BitConverter.ToUInt64(read.Data, 0));
        return true;
    }

    private static int? TryReadInt32(INativeStableMemoryReader memory, nuint address)
    {
        NativeStableMemoryReadResult read = memory.ReadBytes(address, sizeof(int));
        return read.Complete && read.Data.Length == sizeof(int)
            ? BitConverter.ToInt32(read.Data, 0)
            : null;
    }

    private static int ImpliedBaseCount(
        IReadOnlyList<NativeStableBeatmapObjectReference> references,
        NativeStableKnownSignalKind kind) =>
        references.Where(reference => reference.Kind == kind &&
                reference.ReferenceAddress >= Offset(kind))
            .Select(reference => reference.ReferenceAddress - Offset(kind))
            .Distinct().Count();

    private static nuint Offset(NativeStableKnownSignalKind kind) => kind switch
    {
        NativeStableKnownSignalKind.Checksum => ChecksumOffset,
        NativeStableKnownSignalKind.Folder => FolderOffset,
        NativeStableKnownSignalKind.Filename => FilenameOffset,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static int AlignmentOffset(nuint address, int pointerSize)
    {
        nuint remainder = address % checked((nuint)pointerSize);
        return remainder == 0 ? 0 : checked(pointerSize - (int)remainder);
    }

    private static nuint? FirstRawUtf16(
        IReadOnlyList<NativeStableProbeSignal> signals,
        NativeStableKnownSignalKind kind)
    {
        NativeStableProbeSignal? signal = signals.FirstOrDefault(item =>
            item.Kind == kind && item.Encoding == NativeStableTextEncoding.Utf16 &&
            item.Addresses.Count > 0);
        return signal is null ? null : signal.Addresses[0];
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

    private static NativeStableBeatmapObjectCapture Unavailable(
        string captureId,
        DateTimeOffset observedAtUtc,
        NativeStableAttachmentStatus status,
        TimeSpan processDiscovery,
        TimeSpan total,
        string failure) =>
        new(captureId, observedAtUtc, null, null, null, [],
            NativeStableBeatmapObjectDiscoveryResult.Empty with
            {
                Timings = NativeStableBeatmapObjectTimings.Empty with
                {
                    ProcessDiscovery = processDiscovery,
                    Total = total
                }
            }, status, failure);
}

/// <summary>
/// Minimal diagnostic assumption: a managed string reference points to an object whose
/// first pointer-sized word is readable, followed by an Int32 character count and UTF-16
/// characters. No object graph traversal is performed.
/// </summary>
internal static class NativeStableManagedStringReader
{
    internal const int MaximumCharacters = 512;

    internal static int CharacterDataOffset(bool is32Bit) =>
        (is32Bit ? sizeof(uint) : sizeof(ulong)) + sizeof(int);

    internal static bool TryRead(
        INativeStableMemoryReader memory,
        nuint objectAddress,
        out string value)
    {
        value = string.Empty;
        int pointerSize = memory.Is32Bit ? sizeof(uint) : sizeof(ulong);
        NativeStableMemoryReadResult header = memory.ReadBytes(
            objectAddress, pointerSize + sizeof(int));
        if (!header.Complete || header.Data.Length != pointerSize + sizeof(int))
        {
            return false;
        }

        ulong methodTable = memory.Is32Bit
            ? BitConverter.ToUInt32(header.Data, 0)
            : BitConverter.ToUInt64(header.Data, 0);
        int length = BitConverter.ToInt32(header.Data, pointerSize);
        if (methodTable == 0 || length < 0 || length > MaximumCharacters)
        {
            return false;
        }

        int bytes = checked(length * sizeof(char));
        if (bytes == 0)
        {
            value = string.Empty;
            return true;
        }

        NativeStableMemoryReadResult characters = memory.ReadBytes(
            objectAddress + checked((nuint)CharacterDataOffset(memory.Is32Bit)), bytes);
        if (!characters.Complete || characters.Data.Length != bytes)
        {
            return false;
        }

        value = Encoding.Unicode.GetString(characters.Data);
        return !value.Any(char.IsControl);
    }
}

internal sealed record NativeStableKnownBeatmapObjectMetadata(
    string KnownMapPath,
    string Checksum,
    string Folder,
    string Filename,
    int? BeatmapId,
    int? BeatmapSetId)
{
    internal int AvailableIdCount => (BeatmapId is null ? 0 : 1) +
        (BeatmapSetId is null ? 0 : 1);

    internal static bool TryCreate(
        string path,
        string songsPath,
        out NativeStableKnownBeatmapObjectMetadata? metadata)
    {
        metadata = null;
        try
        {
            string fullPath = Path.GetFullPath(path);
            string root = Path.GetFullPath(songsPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string relative = Path.GetRelativePath(root, fullPath);
            if (Path.IsPathRooted(relative) || relative.Equals("..", StringComparison.Ordinal) ||
                relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal) ||
                !fullPath.EndsWith(".osu", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(fullPath))
            {
                return false;
            }

            using FileStream stream = File.OpenRead(fullPath);
            string checksum = Convert.ToHexString(MD5.HashData(stream)).ToLowerInvariant();
            (int? beatmapId, int? beatmapSetId) = ReadIds(fullPath);
            metadata = new(fullPath, checksum,
                Path.GetDirectoryName(relative) ?? string.Empty,
                Path.GetFileName(relative), beatmapId, beatmapSetId);
            return !string.IsNullOrWhiteSpace(metadata.Folder);
        }
        catch (Exception exception) when (exception is ArgumentException or
            NotSupportedException or IOException or UnauthorizedAccessException or
            System.Security.SecurityException)
        {
            return false;
        }
    }

    private static (int? BeatmapId, int? BeatmapSetId) ReadIds(string path)
    {
        int? beatmapId = null;
        int? beatmapSetId = null;
        foreach (string line in File.ReadLines(path))
        {
            if (line.StartsWith("BeatmapID:", StringComparison.Ordinal))
            {
                beatmapId = ParsePositive(line["BeatmapID:".Length..]);
            }
            else if (line.StartsWith("BeatmapSetID:", StringComparison.Ordinal))
            {
                beatmapSetId = ParsePositive(line["BeatmapSetID:".Length..]);
            }
            if (line.Equals("[HitObjects]", StringComparison.Ordinal))
            {
                break;
            }
        }
        return (beatmapId, beatmapSetId);
    }

    private static int? ParsePositive(string value) =>
        int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
            out int parsed) && parsed > 0 ? parsed : null;
}

internal enum NativeStableBeatmapObjectDiscoveryStatus { None, Unique, Ambiguous }
internal enum NativeStableBeatmapObjectCandidateLevel
{
    WeakCandidate,
    PartialCandidate,
    StrongCandidate,
    StrongCandidateWithIds
}

internal sealed record NativeStableManagedStringTarget(
    NativeStableKnownSignalKind Kind,
    nuint ObjectAddress,
    nuint RawCharacterAddress,
    string ExpectedValue);

internal sealed record NativeStableBeatmapObjectReference(
    NativeStableKnownSignalKind Kind,
    nuint ReferenceAddress,
    nuint ManagedObjectAddress,
    nuint RawCharacterAddress);

internal sealed record NativeStableBeatmapObjectFieldEvidence(
    nuint FieldAddress,
    nuint? Reference,
    bool Matches);

internal sealed record NativeStableBeatmapObjectCandidate(
    nuint CandidateAddress,
    NativeStableBeatmapObjectFieldEvidence Checksum,
    NativeStableBeatmapObjectFieldEvidence Folder,
    NativeStableBeatmapObjectFieldEvidence Filename,
    int? ObservedBeatmapId,
    int? ExpectedBeatmapId,
    bool? BeatmapIdMatches,
    int? ObservedBeatmapSetId,
    int? ExpectedBeatmapSetId,
    bool? BeatmapSetIdMatches,
    int MatchedCoreFieldCount,
    int MatchedOptionalIdCount,
    int AvailableOptionalIdCount,
    NativeStableBeatmapObjectCandidateLevel Level,
    NativeStableMemoryRegion? Region);

internal sealed record NativeStableBeatmapObjectStatistics(
    int ChecksumReferenceCount,
    int FolderReferenceCount,
    int FilenameReferenceCount,
    int ChecksumImpliedBaseCount,
    int FolderImpliedBaseCount,
    int FilenameImpliedBaseCount,
    int SurvivingTwoFieldCount,
    int SurvivingThreeFieldCount,
    int SurvivingAvailableIdCount);

internal sealed record NativeStableBeatmapObjectTimings(
    TimeSpan ProcessDiscovery,
    TimeSpan MemoryRegionEnumeration,
    TimeSpan KnownSignalSearch,
    TimeSpan ReverseReferenceSearch,
    TimeSpan CandidateValidation,
    TimeSpan Total)
{
    internal static NativeStableBeatmapObjectTimings Empty { get; } =
        new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
            TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
}

internal sealed record NativeStableBeatmapObjectDiscoveryResult(
    NativeStableBeatmapObjectDiscoveryStatus Status,
    IReadOnlyList<NativeStableBeatmapObjectCandidate> Candidates,
    IReadOnlyList<NativeStableBeatmapObjectReference> References,
    IReadOnlyList<NativeStableManagedStringTarget> ManagedStringTargets,
    NativeStableBeatmapObjectStatistics Statistics,
    nuint? ChecksumRawUtf16Address,
    nuint? FilenameRawUtf16Address,
    long? RawChecksumToFilenameDelta,
    NativeStableBeatmapObjectTimings Timings)
{
    internal bool ArchitectureSupported { get; init; } = true;

    internal static NativeStableBeatmapObjectDiscoveryResult Empty { get; } = new(
        NativeStableBeatmapObjectDiscoveryStatus.None, [], [], [],
        new(0, 0, 0, 0, 0, 0, 0, 0, 0), null, null, null,
        NativeStableBeatmapObjectTimings.Empty);

    internal static NativeStableBeatmapObjectDiscoveryResult UnsupportedArchitecture { get; } =
        Empty with { ArchitectureSupported = false };

    internal IReadOnlyList<NativeStableBeatmapObjectCandidate> StrongCandidates =>
        Candidates.Where(candidate => candidate.MatchedCoreFieldCount == 3).ToArray();

    internal bool? RawDeltaMatchesHypothesizedFieldSpacing =>
        RawChecksumToFilenameDelta is null
            ? null
            : RawChecksumToFilenameDelta == checked((long)(
                NativeStableBeatmapObjectDiscovery.FilenameOffset -
                NativeStableBeatmapObjectDiscovery.ChecksumOffset));
}

internal sealed record NativeStableBeatmapObjectCapture(
    string CaptureId,
    DateTimeOffset ObservedAtUtc,
    int? ProcessId,
    DateTimeOffset? ProcessStartedAtUtc,
    NativeStableKnownBeatmapObjectMetadata? Metadata,
    IReadOnlyList<NativeStableProbeSignal> Signals,
    NativeStableBeatmapObjectDiscoveryResult Discovery,
    NativeStableAttachmentStatus AttachmentStatus,
    string? FailureReason);
