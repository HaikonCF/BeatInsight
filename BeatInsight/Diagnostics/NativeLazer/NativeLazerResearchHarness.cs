using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace BeatInsight.Diagnostics.NativeLazer;

/// <summary>
/// V3.6A diagnostic harness. It is not a game-state provider and is never called by
/// application composition. The only process access is query + VM read.
/// </summary>
public static class NativeLazerResearchHarness
{
    private const string enableVariable = "BEATINSIGHT_NATIVE_LAZER_RESEARCH";
    private const string phaseVariable = "BEATINSIGHT_NATIVE_LAZER_PHASE";
    private const string sessionVariable = "BEATINSIGHT_NATIVE_LAZER_SESSION_PATH";
    private const int chunkBytes = 4 * 1024 * 1024;
    private const int maximumMarkerHits = 10_000;
    private const int maximumReferenceHits = 50_000;
    private const int maximumStructuralOffset = 512;

    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Environment-driven entry point intended for explicit operator invocation.
    /// Returns a process exit code and writes no file unless the opt-in flag and a
    /// session path are both supplied.
    /// </summary>
    public static int RunFromEnvironment()
    {
        try
        {
            if (!string.Equals(
                    Environment.GetEnvironmentVariable(enableVariable),
                    "1",
                    StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"Disabled: set {enableVariable}=1 explicitly.");
                return 2;
            }

            string phaseText = (Environment.GetEnvironmentVariable(phaseVariable) ?? string.Empty)
                .Replace("_", string.Empty, StringComparison.Ordinal);
            if (!Enum.TryParse(
                    phaseText,
                    ignoreCase: true,
                    out NativeLazerResearchPhase phase) ||
                phase == NativeLazerResearchPhase.None)
            {
                Console.Error.WriteLine(
                    $"Invalid {phaseVariable}. Expected CAPTURE_A, CAPTURE_B, or CAPTURE_A2.");
                return 2;
            }

            string? sessionPath = Environment.GetEnvironmentVariable(sessionVariable);
            if (string.IsNullOrWhiteSpace(sessionPath))
            {
                Console.Error.WriteLine($"Missing required {sessionVariable}.");
                return 2;
            }

            NativeLazerMapIdentity mapA = ReadIdentity("A");
            NativeLazerMapIdentity mapB = ReadIdentity("B");
            ValidateDistinctMaps(mapA, mapB);

            NativeLazerResearchSession session = LoadOrCreateSession(
                Path.GetFullPath(sessionPath), phase, mapA, mapB);
            NativeLazerCapture capture = Capture(phase, mapA, mapB);
            ApplyCapture(session, capture);
            SaveSession(Path.GetFullPath(sessionPath), session);
            WriteSummary(session, capture, Path.GetFullPath(sessionPath));
            return 0;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"Research capture failed: {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    private static NativeLazerCapture Capture(
        NativeLazerResearchPhase phase,
        NativeLazerMapIdentity mapA,
        NativeLazerMapIdentity mapB)
    {
        long attachStarted = Stopwatch.GetTimestamp();
        NativeLazerDiscovery discovery = NativeLazerProcessDiscovery.Discover();
        NativeLazerProcessCandidate process = discovery.UniqueCandidate ?? throw new InvalidOperationException(
            $"Lazer process discovery is {discovery.Status}; candidates={discovery.Candidates.Count}.");

        (NativeLazerProcessMemory? memory, NativeLazerAttachStatus attachStatus) =
            NativeLazerProcessMemory.TryAttach(process);
        using NativeLazerProcessMemory attached = memory ?? throw new InvalidOperationException(
            $"Read-only attach failed: {attachStatus}.");
        double attachMs = Stopwatch.GetElapsedTime(attachStarted).TotalMilliseconds;

        long regionsStarted = Stopwatch.GetTimestamp();
        IReadOnlyList<NativeLazerMemoryRegion> regions = attached.EnumerateReadableRegions();
        double regionsMs = Stopwatch.GetElapsedTime(regionsStarted).TotalMilliseconds;

        NativeLazerMarker[] markers = BuildMarkers(mapA, mapB);
        long discoveryStarted = Stopwatch.GetTimestamp();
        (List<NativeLazerMarkerHit> markerHits, bool markerTruncated, ulong bytesScanned) =
            ScanMarkers(attached, regions, markers);
        CorroborateNumericIdentifiers(attached, markerHits, mapA, mapB);
        double initialDiscoveryMs = Stopwatch.GetElapsedTime(discoveryStarted).TotalMilliseconds;

        long referencesStarted = Stopwatch.GetTimestamp();
        (List<NativeLazerReferenceHit> references, bool referenceTruncated) =
            ScanReferences(attached, regions, markerHits);
        double referenceValidationMs = Stopwatch.GetElapsedTime(referencesStarted).TotalMilliseconds;

        return new(
            phase,
            DateTime.UtcNow,
            process.ProcessId,
            process.StartTimeUtc,
            process.ExecutablePath,
            process.ProductVersion,
            regions.Count,
            bytesScanned,
            markerHits,
            references,
            markerTruncated,
            referenceTruncated,
            new(attachMs, regionsMs, initialDiscoveryMs, referenceValidationMs));
    }

    private static NativeLazerMarker[] BuildMarkers(
        NativeLazerMapIdentity mapA,
        NativeLazerMapIdentity mapB) =>
        BuildMarkersForMap(mapA).Concat(BuildMarkersForMap(mapB)).ToArray();

    private static IEnumerable<NativeLazerMarker> BuildMarkersForMap(NativeLazerMapIdentity map)
    {
        yield return new(map.Label, "Guid.DotNet", map.BeatmapGuid.ToByteArray(), Strong: true);
        yield return new(map.Label, "Guid.Rfc4122", Convert.FromHexString(
            map.BeatmapGuid.ToString("N", CultureInfo.InvariantCulture)), Strong: true);

        foreach ((string name, string? value) in new[]
                 {
                     ("BeatmapHash", map.BeatmapHash),
                     ("FileHash", map.FileHash)
                 })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            string normalized = value.Trim().ToLowerInvariant();
            yield return new(map.Label, $"{name}.Ascii", Encoding.ASCII.GetBytes(normalized), Strong: true);
            yield return new(map.Label, $"{name}.Utf16", Encoding.Unicode.GetBytes(normalized), Strong: true);
        }
    }

    private static (List<NativeLazerMarkerHit> Hits, bool Truncated, ulong BytesScanned) ScanMarkers(
        NativeLazerProcessMemory memory,
        IReadOnlyList<NativeLazerMemoryRegion> regions,
        IReadOnlyList<NativeLazerMarker> markers)
    {
        var hits = new List<NativeLazerMarkerHit>();
        var seen = new HashSet<(string Map, string Marker, ulong Address)>();
        int overlap = Math.Max(0, markers.Max(marker => marker.Bytes.Length) - 1);
        ulong bytesScanned = 0;

        foreach (NativeLazerMemoryRegion region in regions)
        {
            ulong consumed = 0;
            while (consumed < region.Size)
            {
                int requested = checked((int)Math.Min((ulong)chunkBytes, region.Size - consumed));
                ulong readAddress = region.BaseAddress + consumed;
                NativeLazerMemoryRead read = memory.Read(readAddress, requested);
                if (read.Data.Length == 0)
                {
                    break;
                }

                bytesScanned += checked((ulong)read.Data.Length);
                foreach (NativeLazerMarker marker in markers)
                {
                    foreach (int index in FindAll(read.Data, marker.Bytes))
                    {
                        ulong absolute = readAddress + checked((ulong)index);
                        if (seen.Add((marker.MapLabel, marker.Name, absolute)))
                        {
                            hits.Add(new(
                                marker.MapLabel,
                                marker.Name,
                                absolute,
                                region.BaseAddress,
                                region.Size,
                                region.AllocationBase,
                                region.Protection,
                                region.TypeName,
                                region.IsPrivate,
                                OnlineIdNearby: null,
                                BeatmapSetOnlineIdNearby: null));
                        }

                        if (hits.Count >= maximumMarkerHits)
                        {
                            return (hits, true, bytesScanned);
                        }
                    }
                }

                if (read.Data.Length < requested || consumed + checked((ulong)read.Data.Length) >= region.Size)
                {
                    break;
                }

                consumed += checked((ulong)Math.Max(1, read.Data.Length - overlap));
            }
        }

        return (hits, false, bytesScanned);
    }

    private static void CorroborateNumericIdentifiers(
        NativeLazerProcessMemory memory,
        List<NativeLazerMarkerHit> hits,
        NativeLazerMapIdentity mapA,
        NativeLazerMapIdentity mapB)
    {
        for (int index = 0; index < hits.Count; index++)
        {
            NativeLazerMarkerHit hit = hits[index];
            if (!hit.IsPrivate || !hit.MarkerName.StartsWith("Guid.", StringComparison.Ordinal))
            {
                continue;
            }

            NativeLazerMapIdentity identity = hit.MapLabel == "A" ? mapA : mapB;
            ulong availableBefore = hit.Address - hit.RegionBase;
            ulong before = Math.Min(availableBefore, checked((ulong)maximumStructuralOffset));
            ulong regionEnd = hit.RegionBase + hit.RegionSize;
            ulong availableAfter = regionEnd > hit.Address ? regionEnd - hit.Address : 0;
            ulong after = Math.Min(availableAfter, checked((ulong)maximumStructuralOffset));
            int count = checked((int)(before + after));
            NativeLazerMemoryRead neighborhood = memory.Read(hit.Address - before, count);

            hits[index] = hit with
            {
                OnlineIdNearby = ContainsInt32(neighborhood.Data, identity.OnlineId),
                BeatmapSetOnlineIdNearby = ContainsInt32(neighborhood.Data, identity.BeatmapSetOnlineId)
            };
        }
    }

    private static bool? ContainsInt32(byte[] data, int? value)
    {
        if (value is null)
        {
            return null;
        }

        return data.AsSpan().IndexOf(BitConverter.GetBytes(value.Value)) >= 0;
    }

    private static (List<NativeLazerReferenceHit> Hits, bool Truncated) ScanReferences(
        NativeLazerProcessMemory memory,
        IReadOnlyList<NativeLazerMemoryRegion> regions,
        IReadOnlyList<NativeLazerMarkerHit> markerHits)
    {
        var targets = new Dictionary<ulong, List<NativeLazerTarget>>();
        foreach (NativeLazerMarkerHit hit in markerHits.Where(hit => hit.IsPrivate))
        {
            for (int delta = 0; delta <= maximumStructuralOffset; delta += sizeof(ulong))
            {
                if (hit.Address < checked((ulong)delta))
                {
                    break;
                }

                ulong target = hit.Address - checked((ulong)delta);
                if (target < hit.RegionBase || target >= hit.RegionBase + hit.RegionSize)
                {
                    continue;
                }

                if (!targets.TryGetValue(target, out List<NativeLazerTarget>? values))
                {
                    values = [];
                    targets.Add(target, values);
                }

                values.Add(new(hit.MapLabel, hit.MarkerName, hit.Address, delta));
            }
        }

        var references = new List<NativeLazerReferenceHit>();
        var seen = new HashSet<(ulong Slot, string Map, string Marker, int Delta)>();

        foreach (NativeLazerMemoryRegion region in regions.Where(region => region.IsPrivate))
        {
            ulong consumed = 0;
            while (consumed < region.Size)
            {
                int requested = checked((int)Math.Min((ulong)chunkBytes, region.Size - consumed));
                NativeLazerMemoryRead read = memory.Read(region.BaseAddress + consumed, requested);
                if (read.Data.Length < sizeof(ulong))
                {
                    break;
                }

                int alignedLength = read.Data.Length - sizeof(ulong) + 1;
                for (int index = 0; index < alignedLength; index += sizeof(ulong))
                {
                    ulong value = BitConverter.ToUInt64(read.Data, index);
                    if (!targets.TryGetValue(value, out List<NativeLazerTarget>? matching))
                    {
                        continue;
                    }

                    ulong slot = region.BaseAddress + consumed + checked((ulong)index);
                    foreach (NativeLazerTarget target in matching)
                    {
                        if (seen.Add((slot, target.MapLabel, target.MarkerName, target.MarkerOffset)))
                        {
                            references.Add(new(
                                slot,
                                value,
                                target.MapLabel,
                                target.MarkerName,
                                target.MarkerAddress,
                                target.MarkerOffset,
                                region.BaseAddress,
                                region.Size,
                                region.AllocationBase,
                                region.Protection));
                        }

                        if (references.Count >= maximumReferenceHits)
                        {
                            return (references, true);
                        }
                    }
                }

                if (read.Data.Length < requested)
                {
                    break;
                }

                consumed += checked((ulong)read.Data.Length);
            }
        }

        return (references, false);
    }

    private static IEnumerable<int> FindAll(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || needle.Length > haystack.Length)
        {
            yield break;
        }

        int start = 0;
        while (start <= haystack.Length - needle.Length)
        {
            int relative = haystack.AsSpan(start).IndexOf(needle);
            if (relative < 0)
            {
                yield break;
            }

            int index = start + relative;
            yield return index;
            start = index + 1;
        }
    }

    private static void ApplyCapture(NativeLazerResearchSession session, NativeLazerCapture capture)
    {
        NativeLazerResearchPhase expected = session.Captures.Count switch
        {
            0 => NativeLazerResearchPhase.CaptureA,
            1 => NativeLazerResearchPhase.CaptureB,
            2 => NativeLazerResearchPhase.CaptureA2,
            _ => NativeLazerResearchPhase.None
        };

        if (capture.Phase != expected)
        {
            throw new InvalidDataException($"Expected {expected}; received {capture.Phase}.");
        }

        if (session.Captures.Count > 0)
        {
            NativeLazerCapture first = session.Captures[0];
            if (capture.ProcessId != first.ProcessId || capture.ProcessStartUtc != first.ProcessStartUtc)
            {
                throw new InvalidDataException(
                    "Process identity changed inside one A/B/A generation. Start a new generation/session.");
            }
        }

        session.Captures.Add(capture);
        if (session.Captures.Count == 3)
        {
            session.Survivors = EvaluateTransitions(session.Captures);
            session.Outcome = capture.MarkerHitsTruncated || capture.ReferenceHitsTruncated ||
                              session.Captures.Any(item => item.MarkerHitsTruncated || item.ReferenceHitsTruncated)
                ? NativeLazerResearchOutcome.Ambiguous
                : session.Survivors.Count switch
                {
                    0 => NativeLazerResearchOutcome.None,
                    1 => NativeLazerResearchOutcome.Unique,
                    _ => NativeLazerResearchOutcome.Ambiguous
                };
        }
    }

    private static List<NativeLazerTransitionCandidate> EvaluateTransitions(
        IReadOnlyList<NativeLazerCapture> captures)
    {
        NativeLazerCapture a = captures[0];
        NativeLazerCapture b = captures[1];
        NativeLazerCapture a2 = captures[2];

        var bIndex = b.References.Where(reference => reference.MapLabel == "B")
            .ToLookup(reference => (reference.SlotAddress, Shape(reference.MarkerName), reference.MarkerOffset));
        var a2Index = a2.References.Where(reference => reference.MapLabel == "A")
            .ToLookup(reference => (reference.SlotAddress, Shape(reference.MarkerName), reference.MarkerOffset));

        return a.References
            .Where(reference => reference.MapLabel == "A")
            .SelectMany(first =>
            {
                var key = (first.SlotAddress, Shape(first.MarkerName), first.MarkerOffset);
                return bIndex[key].SelectMany(second =>
                    a2Index[key].Select(third => new NativeLazerTransitionCandidate(
                        first.SlotAddress,
                        Shape(first.MarkerName),
                        first.MarkerOffset,
                        first.TargetAddress,
                        second.TargetAddress,
                        third.TargetAddress,
                        first.RegionBase,
                        first.RegionSize,
                        first.AllocationBase,
                        first.Protection)));
            })
            .GroupBy(candidate => new
            {
                candidate.SlotAddress,
                candidate.MarkerShape,
                candidate.MarkerOffset,
                candidate.TargetA,
                candidate.TargetB,
                candidate.TargetA2
            })
            .Select(group => group.First())
            .OrderBy(candidate => candidate.SlotAddress)
            .ToList();
    }

    private static string Shape(string markerName) => markerName.EndsWith(".DotNet", StringComparison.Ordinal)
        ? "Guid.DotNet"
        : markerName.EndsWith(".Rfc4122", StringComparison.Ordinal)
            ? "Guid.Rfc4122"
            : markerName.EndsWith(".Ascii", StringComparison.Ordinal)
                ? "Hash.Ascii"
                : "Hash.Utf16";

    private static NativeLazerResearchSession LoadOrCreateSession(
        string path,
        NativeLazerResearchPhase phase,
        NativeLazerMapIdentity mapA,
        NativeLazerMapIdentity mapB)
    {
        if (phase == NativeLazerResearchPhase.CaptureA)
        {
            if (File.Exists(path) && !string.Equals(
                    Environment.GetEnvironmentVariable("BEATINSIGHT_NATIVE_LAZER_RESET_SESSION"),
                    "1",
                    StringComparison.Ordinal))
            {
                throw new IOException(
                    "Session already exists. Use a new path or explicitly set BEATINSIGHT_NATIVE_LAZER_RESET_SESSION=1.");
            }

            return new()
            {
                FormatVersion = 1,
                Generation = Environment.GetEnvironmentVariable("BEATINSIGHT_NATIVE_LAZER_GENERATION") ?? "UNSPECIFIED",
                MapA = mapA,
                MapB = mapB
            };
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Research session does not exist.", path);
        }

        NativeLazerResearchSession session = JsonSerializer.Deserialize<NativeLazerResearchSession>(
            File.ReadAllText(path), jsonOptions) ?? throw new InvalidDataException("Invalid research session JSON.");

        if (session.FormatVersion != 1 || session.MapA != mapA || session.MapB != mapB)
        {
            throw new InvalidDataException("Session format or map identities do not match the current environment.");
        }

        return session;
    }

    private static void SaveSession(string path, NativeLazerResearchSession session)
    {
        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException("The session output directory must already exist.");
        }

        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(session, jsonOptions));
        File.Move(temporary, path, overwrite: true);
    }

    private static NativeLazerMapIdentity ReadIdentity(string label)
    {
        string prefix = $"BEATINSIGHT_NATIVE_LAZER_MAP_{label}_";
        if (!Guid.TryParse(Environment.GetEnvironmentVariable(prefix + "GUID"), out Guid guid) || guid == Guid.Empty)
        {
            throw new InvalidDataException($"{prefix}GUID must contain the exact non-empty BeatmapInfo ID.");
        }

        string? beatmapHash = NormalizeHash(Environment.GetEnvironmentVariable(prefix + "HASH"));
        string? fileHash = NormalizeHash(Environment.GetEnvironmentVariable(prefix + "FILE_HASH"));
        int? onlineId = ParseOptionalPositiveInt(prefix + "ONLINE_ID");
        int? setOnlineId = ParseOptionalPositiveInt(prefix + "SET_ONLINE_ID");

        return new(label, guid, onlineId, setOnlineId, beatmapHash, fileHash);
    }

    private static string? NormalizeHash(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length is not (32 or 64) || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException("Hashes must be exact 32- or 64-character hexadecimal values.");
        }

        return normalized;
    }

    private static int? ParseOptionalPositiveInt(string variable)
    {
        string? value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) || parsed <= 0)
        {
            throw new InvalidDataException($"{variable} must be a positive integer when supplied.");
        }

        return parsed;
    }

    private static void ValidateDistinctMaps(NativeLazerMapIdentity mapA, NativeLazerMapIdentity mapB)
    {
        if (mapA.BeatmapGuid == mapB.BeatmapGuid ||
            mapA.OnlineId is not null && mapA.OnlineId == mapB.OnlineId ||
            mapA.BeatmapHash is not null && mapA.BeatmapHash == mapB.BeatmapHash ||
            mapA.FileHash is not null && mapA.FileHash == mapB.FileHash)
        {
            throw new InvalidDataException("Map A and map B must be distinct exact beatmap identities.");
        }
    }

    private static void WriteSummary(
        NativeLazerResearchSession session,
        NativeLazerCapture capture,
        string path)
    {
        Console.WriteLine($"Phase={capture.Phase}");
        Console.WriteLine($"Generation={session.Generation}");
        Console.WriteLine($"ProcessIdentity={capture.ProcessId}:{capture.ProcessStartUtc:O}");
        Console.WriteLine($"Version={capture.ProductVersion ?? "UNKNOWN"}");
        Console.WriteLine($"Regions={capture.RegionCount}; BytesScanned={capture.BytesScanned}");
        Console.WriteLine($"MarkerHits={capture.MarkerHits.Count}; ReferenceHits={capture.References.Count}");
        Console.WriteLine($"Truncated={capture.MarkerHitsTruncated || capture.ReferenceHitsTruncated}");
        Console.WriteLine(
            $"AttachMs={capture.Performance.AttachMs:F3}; RegionsMs={capture.Performance.RegionEnumerationMs:F3}; " +
            $"DiscoveryMs={capture.Performance.InitialDiscoveryMs:F3}; ValidationMs={capture.Performance.ReferenceValidationMs:F3}");
        Console.WriteLine($"Outcome={session.Outcome}; Survivors={session.Survivors.Count}");
        Console.WriteLine($"Session={path}");
    }
}

public enum NativeLazerResearchPhase { None, CaptureA, CaptureB, CaptureA2 }

public enum NativeLazerResearchOutcome { Unknown, None, Unique, Ambiguous }

internal sealed record NativeLazerMarker(string MapLabel, string Name, byte[] Bytes, bool Strong);

internal sealed record NativeLazerTarget(
    string MapLabel,
    string MarkerName,
    ulong MarkerAddress,
    int MarkerOffset);

public sealed record NativeLazerMapIdentity(
    string Label,
    Guid BeatmapGuid,
    int? OnlineId,
    int? BeatmapSetOnlineId,
    string? BeatmapHash,
    string? FileHash);

public sealed record NativeLazerMarkerHit(
    string MapLabel,
    string MarkerName,
    ulong Address,
    ulong RegionBase,
    ulong RegionSize,
    ulong AllocationBase,
    uint Protection,
    string RegionType,
    bool IsPrivate,
    bool? OnlineIdNearby,
    bool? BeatmapSetOnlineIdNearby);

public sealed record NativeLazerReferenceHit(
    ulong SlotAddress,
    ulong TargetAddress,
    string MapLabel,
    string MarkerName,
    ulong MarkerAddress,
    int MarkerOffset,
    ulong RegionBase,
    ulong RegionSize,
    ulong AllocationBase,
    uint Protection);

public sealed record NativeLazerTransitionCandidate(
    ulong SlotAddress,
    string MarkerShape,
    int MarkerOffset,
    ulong TargetA,
    ulong TargetB,
    ulong TargetA2,
    ulong RegionBase,
    ulong RegionSize,
    ulong AllocationBase,
    uint Protection);

public sealed record NativeLazerPerformance(
    double AttachMs,
    double RegionEnumerationMs,
    double InitialDiscoveryMs,
    double ReferenceValidationMs);

public sealed record NativeLazerCapture(
    NativeLazerResearchPhase Phase,
    DateTime CapturedUtc,
    int ProcessId,
    DateTime ProcessStartUtc,
    string ExecutablePath,
    string? ProductVersion,
    int RegionCount,
    ulong BytesScanned,
    List<NativeLazerMarkerHit> MarkerHits,
    List<NativeLazerReferenceHit> References,
    bool MarkerHitsTruncated,
    bool ReferenceHitsTruncated,
    NativeLazerPerformance Performance);

public sealed class NativeLazerResearchSession
{
    public int FormatVersion { get; set; }
    public string Generation { get; set; } = "UNSPECIFIED";
    public NativeLazerMapIdentity MapA { get; set; } = null!;
    public NativeLazerMapIdentity MapB { get; set; } = null!;
    public List<NativeLazerCapture> Captures { get; set; } = [];
    public NativeLazerResearchOutcome Outcome { get; set; } = NativeLazerResearchOutcome.Unknown;
    public List<NativeLazerTransitionCandidate> Survivors { get; set; } = [];
}
