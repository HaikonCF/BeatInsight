using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace BeatInsight.Diagnostics.NativeStable;

/// <summary>
/// EXPERIMENTAL — V3 PROOF OF CONCEPT — NOT PRODUCTION INTEGRATION.
/// Explicit, default-off diagnostic for caller-supplied known maps only.
/// </summary>
internal static class NativeStablePoc
{
    internal const bool RunsAutomatically = false;

    internal static NativeStableProbeResult CaptureKnownMap(
        string expectedOsuPath,
        CancellationToken cancellationToken = default) =>
        CaptureKnownMaps(
            [NativeStableKnownMapInput.FromPath("MAP", expectedOsuPath)],
            expectedSelectedMapLabel: "MAP",
            cancellationToken);

    internal static NativeStableProbeResult CaptureKnownMaps(
        IReadOnlyList<NativeStableKnownMapInput> knownMaps,
        string? expectedSelectedMapLabel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(knownMaps);
        if (knownMaps.Count == 0)
        {
            throw new ArgumentException("At least one known map is required.", nameof(knownMaps));
        }

        ValidateLabels(knownMaps, expectedSelectedMapLabel);

        string captureId = Guid.NewGuid().ToString("N");
        DateTimeOffset observedAtUtc = DateTimeOffset.UtcNow;
        var total = Stopwatch.StartNew();
        var phase = Stopwatch.StartNew();

        NativeStableProcessDiscoveryResult discovery = NativeStableProcessDiscovery.Discover();
        TimeSpan discoveryElapsed = phase.Elapsed;
        if (discovery.Status != NativeStableAttachmentStatus.Discovered ||
            discovery.UniqueCandidate is null)
        {
            DebugLogger.Log(
                $"[NATIVE-STABLE-POC] unsupported / ambiguous ({discovery.Status})");
            return Unavailable(captureId, observedAtUtc, expectedSelectedMapLabel,
                discovery.Status, discoveryElapsed, TimeSpan.Zero, total.Elapsed);
        }

        NativeStableProcessCandidate process = discovery.UniqueCandidate;
        DebugLogger.Log(
            $"[NATIVE-STABLE-POC] candidate process found pid={process.ProcessId}");

        phase.Restart();
        NativeStableAttachResult attachment = NativeStableProcessMemory.TryAttach(process);
        TimeSpan attachmentElapsed = phase.Elapsed;
        if (attachment.Memory is null)
        {
            DebugLogger.Log(
                $"[NATIVE-STABLE-POC] unsupported / ambiguous ({attachment.Status})");
            return Unavailable(captureId, observedAtUtc, expectedSelectedMapLabel,
                attachment.Status, discoveryElapsed, attachmentElapsed, total.Elapsed);
        }

        using NativeStableProcessMemory memory = attachment.Memory;
        DebugLogger.Log("[NATIVE-STABLE-POC] attached read-only");

        NativeStableSongsPathResult songs = new NativeStableSongsPathResolver()
            .Resolve(process.ExecutablePath!);
        if (songs.Path is null ||
            !TryPrepareKnownMaps(knownMaps, songs.Path, out NativeStablePreparedKnownMap[] preparedMaps))
        {
            DebugLogger.Log("[NATIVE-STABLE-POC] known map input or songs path unavailable");
            return Unavailable(captureId, observedAtUtc, expectedSelectedMapLabel,
                NativeStableAttachmentStatus.AttachedReadOnly,
                discoveryElapsed, attachmentElapsed, total.Elapsed);
        }

        DebugLogger.Log(
            $"[NATIVE-STABLE-POC] songs path resolved source={songs.Source}");
        DebugLogger.Log(
            $"[NATIVE-STABLE-POC] capture id={captureId} pid={process.ProcessId} observed={observedAtUtc:O} maps={preparedMaps.Length} expected={expectedSelectedMapLabel ?? "unspecified"}");

        phase.Restart();
        IReadOnlyList<NativeStableMemoryRegion> regions = memory.EnumerateReadableRegions();
        TimeSpan regionEnumerationElapsed = phase.Elapsed;

        IReadOnlyList<NativeStableProbeSignal> definitions = BuildSignalDefinitions(preparedMaps);
        NativeStablePatternSearchRequest[] requests = definitions
            .Select((signal, index) => new NativeStablePatternSearchRequest(
                index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                BytePattern.FromBytes(signal.KnownBytes)))
            .ToArray();

        DebugLogger.Log("[NATIVE-STABLE-POC] scanning all known map signals in one pass");
        phase.Restart();
        IReadOnlyDictionary<string, IReadOnlyList<nuint>> matches =
            NativeStableProcessPatternSearch.FindAllMany(
                memory, regions, requests, cancellationToken);
        NativeStableProbeSignal[] signals = definitions
            .Select((signal, index) => signal with
            {
                Addresses = matches[index.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)]
            })
            .ToArray();
        TimeSpan scanElapsed = phase.Elapsed;

        phase.Restart();
        IReadOnlyList<NativeStableCorrelationCandidate> correlated =
            NativeStableCandidateCorrelationAnalyzer.Analyze(signals, regions);
        TimeSpan correlationElapsed = phase.Elapsed;

        phase.Restart();
        NativeStableCorrelationCandidate[] inspected = correlated
            .Select(candidate => InspectCandidate(memory, candidate, signals, regions))
            .ToArray();
        TimeSpan inspectionElapsed = phase.Elapsed;

        phase.Restart();
        NativeStableCandidateTargetSet targetSet =
            NativeStableReferenceDiscovery.BuildTargetSet(inspected, regions);
        TimeSpan neighborhoodAnalysisElapsed = phase.Elapsed;

        phase.Restart();
        NativeStableReferenceDiscoveryResult referenceDiscovery =
            NativeStableReferenceDiscovery.Search(
                memory,
                regions,
                targetSet,
                signals,
                cancellationToken);
        referenceDiscovery = NativeStableReferenceDiscovery.SearchOneIntermediateLevel(
            memory,
            regions,
            referenceDiscovery,
            signals,
            cancellationToken);
        TimeSpan reverseReferenceElapsed = phase.Elapsed;

        IReadOnlyList<NativeStableKnownMapCapture> mapCaptures =
            BuildKnownMapCaptures(preparedMaps, signals, inspected);
        IReadOnlyList<NativeStableCrossMapComparison> crossMapComparisons =
            BuildCrossMapComparisons(mapCaptures);

        foreach (NativeStableKnownMapCapture map in mapCaptures)
        {
            DebugLogger.Log(
                $"[NATIVE-STABLE-POC] map={map.MapLabel} status={map.Status} candidates={map.CorrelationCandidates.Count} plus-0x50={map.Plus0x50CandidateCount}");
        }

        DebugLogger.Log(
            "[NATIVE-STABLE-POC] capture complete; lifetime and current-map ownership remain unproven");

        return new(
            captureId,
            observedAtUtc,
            process.ProcessId,
            process.ExecutablePath,
            songs.Path,
            expectedSelectedMapLabel,
            mapCaptures,
            signals,
            inspected,
            crossMapComparisons,
            referenceDiscovery,
            attachment.Status,
            new(discoveryElapsed, attachmentElapsed, regionEnumerationElapsed,
                scanElapsed, correlationElapsed, inspectionElapsed,
                reverseReferenceElapsed, neighborhoodAnalysisElapsed, total.Elapsed));
    }

    internal static IReadOnlyList<NativeStableProbeSignal> BuildSignalDefinitions(
        IReadOnlyList<NativeStablePreparedKnownMap> knownMaps)
    {
        var signals = new List<NativeStableProbeSignal>();
        foreach (NativeStablePreparedKnownMap map in knownMaps)
        {
            AddSignal(signals, map, "checksum-ascii", NativeStableKnownSignalKind.Checksum,
                NativeStableTextEncoding.Ascii, Encoding.ASCII.GetBytes(map.Checksum));
            AddSignal(signals, map, "checksum-utf16", NativeStableKnownSignalKind.Checksum,
                NativeStableTextEncoding.Utf16, Encoding.Unicode.GetBytes(map.Checksum));
            AddSignal(signals, map, "filename-utf8", NativeStableKnownSignalKind.Filename,
                NativeStableTextEncoding.Utf8, Encoding.UTF8.GetBytes(map.Filename));
            AddSignal(signals, map, "filename-utf16", NativeStableKnownSignalKind.Filename,
                NativeStableTextEncoding.Utf16, Encoding.Unicode.GetBytes(map.Filename));

            if (!string.IsNullOrWhiteSpace(map.Folder))
            {
                AddSignal(signals, map, "folder-utf8", NativeStableKnownSignalKind.Folder,
                    NativeStableTextEncoding.Utf8, Encoding.UTF8.GetBytes(map.Folder));
                AddSignal(signals, map, "folder-utf16", NativeStableKnownSignalKind.Folder,
                    NativeStableTextEncoding.Utf16, Encoding.Unicode.GetBytes(map.Folder));
            }
        }

        return signals;
    }

    internal static IReadOnlyList<NativeStableCrossMapComparison> BuildCrossMapComparisons(
        IReadOnlyList<NativeStableKnownMapCapture> maps)
    {
        var comparisons = new List<NativeStableCrossMapComparison>();
        for (int firstMapIndex = 0; firstMapIndex < maps.Count; firstMapIndex++)
        {
            for (int secondMapIndex = firstMapIndex + 1;
                 secondMapIndex < maps.Count;
                 secondMapIndex++)
            {
                NativeStableKnownMapCapture firstMap = maps[firstMapIndex];
                NativeStableKnownMapCapture secondMap = maps[secondMapIndex];
                for (int firstCandidateIndex = 0;
                     firstCandidateIndex < firstMap.CorrelationCandidates.Count;
                     firstCandidateIndex++)
                {
                    for (int secondCandidateIndex = 0;
                         secondCandidateIndex < secondMap.CorrelationCandidates.Count;
                         secondCandidateIndex++)
                    {
                        NativeStableCorrelationCandidate first =
                            firstMap.CorrelationCandidates[firstCandidateIndex];
                        NativeStableCorrelationCandidate second =
                            secondMap.CorrelationCandidates[secondCandidateIndex];
                        if (!NativeStableCandidateCorrelationAnalyzer.TrySignedDelta(
                                first.ChecksumAddress, second.ChecksumAddress,
                                out long checksumDistance) ||
                            !NativeStableCandidateCorrelationAnalyzer.TrySignedDelta(
                                first.FilenameAddress, second.FilenameAddress,
                                out long filenameDistance))
                        {
                            continue;
                        }

                        bool sameRegion = first.SameRegion &&
                            second.SameRegion &&
                            first.ChecksumRegion is not null &&
                            second.ChecksumRegion is not null &&
                            first.ChecksumRegion.Value.BaseAddress ==
                            second.ChecksumRegion.Value.BaseAddress;
                        comparisons.Add(new(
                            firstMap.MapLabel,
                            firstCandidateIndex,
                            secondMap.MapLabel,
                            secondCandidateIndex,
                            sameRegion,
                            checksumDistance,
                            filenameDistance));
                    }
                }
            }
        }

        return comparisons;
    }

    internal static IReadOnlyList<NativeStableKnownMapCapture> BuildKnownMapCaptures(
        IReadOnlyList<NativeStablePreparedKnownMap> maps,
        IReadOnlyList<NativeStableProbeSignal> signals,
        IReadOnlyList<NativeStableCorrelationCandidate> candidates) =>
        maps
            .Select(map => new NativeStableKnownMapCapture(
                map.Label,
                map.Checksum,
                map.Filename,
                map.Folder,
                map.FullPath,
                signals.Where(signal => signal.MapLabel.Equals(
                    map.Label, StringComparison.OrdinalIgnoreCase)).ToArray(),
                candidates.Where(candidate => candidate.MapLabel.Equals(
                    map.Label, StringComparison.OrdinalIgnoreCase)).ToArray()))
            .ToArray();

    private static void AddSignal(
        ICollection<NativeStableProbeSignal> signals,
        NativeStablePreparedKnownMap map,
        string name,
        NativeStableKnownSignalKind kind,
        NativeStableTextEncoding encoding,
        byte[] bytes) =>
        signals.Add(new(map.Label, name, kind, encoding, bytes, []));

    private static NativeStableCorrelationCandidate InspectCandidate(
        INativeStableMemoryReader memory,
        NativeStableCorrelationCandidate candidate,
        IReadOnlyList<NativeStableProbeSignal> signals,
        IReadOnlyList<NativeStableMemoryRegion> regions)
    {
        if (!candidate.SameRegion)
        {
            return candidate;
        }

        NativeStableCandidateInspectionResult inspection =
            NativeStableCandidateInspector.Inspect(memory, candidate, signals, regions);
        return candidate with
        {
            PointerRelationships = inspection.Relationships,
            FilteredStringPayloadPointerLikeValueCount =
                inspection.FilteredStringPayloadPointerLikeValueCount
        };
    }

    private static bool TryPrepareKnownMaps(
        IReadOnlyList<NativeStableKnownMapInput> inputs,
        string songsPath,
        out NativeStablePreparedKnownMap[] prepared)
    {
        var results = new List<NativeStablePreparedKnownMap>(inputs.Count);
        foreach (NativeStableKnownMapInput input in inputs)
        {
            if (!TryPrepareKnownMap(input, songsPath, out NativeStablePreparedKnownMap? map))
            {
                prepared = [];
                return false;
            }

            results.Add(map!);
        }

        prepared = results.ToArray();
        return true;
    }

    private static bool TryPrepareKnownMap(
        NativeStableKnownMapInput input,
        string songsPath,
        out NativeStablePreparedKnownMap? prepared)
    {
        prepared = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(input.FullPath))
            {
                string fullPath = Path.GetFullPath(input.FullPath);
                string songsRoot = Path.GetFullPath(songsPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string relative = Path.GetRelativePath(songsRoot, fullPath);
                if (Path.IsPathRooted(relative) ||
                    relative.Equals("..", StringComparison.Ordinal) ||
                    relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                    relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal) ||
                    !fullPath.EndsWith(".osu", StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(fullPath))
                {
                    return false;
                }

                string filename = Path.GetFileName(relative);
                string? folder = Path.GetDirectoryName(relative);
                using FileStream stream = File.OpenRead(fullPath);
                string checksum = Convert.ToHexString(MD5.HashData(stream)).ToLowerInvariant();

                if ((!string.IsNullOrWhiteSpace(input.Checksum) &&
                        !checksum.Equals(input.Checksum, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(input.Filename) &&
                        !filename.Equals(input.Filename, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(input.Folder) &&
                        !string.Equals(folder, input.Folder, StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }

                prepared = new(input.Label, checksum, filename, folder, fullPath);
                return true;
            }

            if (string.IsNullOrWhiteSpace(input.Checksum) ||
                !NativeStableCurrentMapValidator.IsMd5(input.Checksum) ||
                string.IsNullOrWhiteSpace(input.Filename) ||
                !input.Filename.EndsWith(".osu", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            prepared = new(input.Label, input.Checksum.ToLowerInvariant(), input.Filename,
                input.Folder, FullPath: null);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (SecurityException)
        {
            return false;
        }
    }

    private static void ValidateLabels(
        IReadOnlyList<NativeStableKnownMapInput> maps,
        string? expectedSelectedMapLabel)
    {
        if (maps.Any(map => string.IsNullOrWhiteSpace(map.Label)) ||
            maps.Select(map => map.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != maps.Count)
        {
            throw new ArgumentException("Known map labels must be non-empty and unique.", nameof(maps));
        }

        if (!string.IsNullOrWhiteSpace(expectedSelectedMapLabel) &&
            !maps.Any(map => map.Label.Equals(
                expectedSelectedMapLabel, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "The expected selected map label must identify one supplied map.",
                nameof(expectedSelectedMapLabel));
        }
    }

    private static NativeStableProbeResult Unavailable(
        string captureId,
        DateTimeOffset observedAtUtc,
        string? expectedSelectedMapLabel,
        NativeStableAttachmentStatus status,
        TimeSpan discovery,
        TimeSpan attachment,
        TimeSpan total) =>
        new(captureId, observedAtUtc, null, null, null, expectedSelectedMapLabel,
            [], [], [], [], NativeStableReferenceDiscoveryResult.Empty, status,
            new(discovery, attachment, TimeSpan.Zero, TimeSpan.Zero,
                TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, total));
}

internal sealed record NativeStableKnownMapInput(
    string Label,
    string? Checksum,
    string? Filename,
    string? Folder,
    string? FullPath)
{
    internal static NativeStableKnownMapInput FromPath(string label, string fullPath) =>
        new(label, null, null, null, fullPath);
}

internal sealed record NativeStablePreparedKnownMap(
    string Label,
    string Checksum,
    string Filename,
    string? Folder,
    string? FullPath);

internal sealed record NativeStableProbeSignal(
    string MapLabel,
    string Name,
    NativeStableKnownSignalKind Kind,
    NativeStableTextEncoding Encoding,
    byte[] KnownBytes,
    IReadOnlyList<nuint> Addresses);

internal enum NativeStableMapObservationStatus
{
    NotObserved,
    Observed,
    MultipleCandidates
}

internal sealed record NativeStableKnownMapCapture(
    string MapLabel,
    string KnownChecksum,
    string KnownFilename,
    string? KnownFolder,
    string? KnownMapPath,
    IReadOnlyList<NativeStableProbeSignal> Signals,
    IReadOnlyList<NativeStableCorrelationCandidate> CorrelationCandidates)
{
    internal bool Observed => CorrelationCandidates.Count > 0;

    internal NativeStableMapObservationStatus Status => CorrelationCandidates.Count switch
    {
        0 => NativeStableMapObservationStatus.NotObserved,
        1 => NativeStableMapObservationStatus.Observed,
        _ => NativeStableMapObservationStatus.MultipleCandidates
    };

    internal int Plus0x50CandidateCount =>
        CorrelationCandidates.Count(candidate => candidate.IsPlus50);
}

internal sealed record NativeStableCrossMapComparison(
    string FirstMapLabel,
    int FirstCandidateIndex,
    string SecondMapLabel,
    int SecondCandidateIndex,
    bool SameRegion,
    long ChecksumAddressDistance,
    long FilenameAddressDistance);

internal sealed record NativeStableProbeResult(
    string CaptureId,
    DateTimeOffset ObservedAtUtc,
    int? ProcessId,
    string? ExecutablePath,
    string? SongsPath,
    string? ExpectedSelectedMapLabel,
    IReadOnlyList<NativeStableKnownMapCapture> KnownMaps,
    IReadOnlyList<NativeStableProbeSignal> Signals,
    IReadOnlyList<NativeStableCorrelationCandidate> CorrelationCandidates,
    IReadOnlyList<NativeStableCrossMapComparison> CrossMapComparisons,
    NativeStableReferenceDiscoveryResult ReferenceDiscovery,
    NativeStableAttachmentStatus AttachmentStatus,
    NativeStableProbeTimings Timings)
{
    internal string? KnownMapPath => KnownMaps.FirstOrDefault()?.KnownMapPath;

    internal string? KnownChecksum => KnownMaps.FirstOrDefault()?.KnownChecksum;

    internal bool MultipleSignalsObserved => Signals.Count(signal => signal.Addresses.Count > 0) >= 2;
}

internal sealed record NativeStableProbeTimings(
    TimeSpan ProcessDiscovery,
    TimeSpan Attachment,
    TimeSpan RegionEnumeration,
    TimeSpan KnownStringScan,
    TimeSpan Correlation,
    TimeSpan TargetedInspection,
    TimeSpan ReverseReferenceSearch,
    TimeSpan NeighborhoodAnalysis,
    TimeSpan Total);
