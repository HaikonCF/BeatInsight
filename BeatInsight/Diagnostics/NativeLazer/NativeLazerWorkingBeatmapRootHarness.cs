using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace BeatInsight.Diagnostics.NativeLazer;

/// <summary>
/// V3.6B diagnostic-only upward reference research. It consumes a completed
/// V3.6A A/B/A session and never repeats marker or level-1 reference discovery.
/// </summary>
public static class NativeLazerWorkingBeatmapRootHarness
{
    private const string enableVariable = "BEATINSIGHT_NATIVE_LAZER_ROOT_RESEARCH";
    private const string phaseVariable = "BEATINSIGHT_NATIVE_LAZER_ROOT_PHASE";
    private const string baselineVariable = "BEATINSIGHT_NATIVE_LAZER_ROOT_BASE_SESSION_PATH";
    private const string sessionVariable = "BEATINSIGHT_NATIVE_LAZER_ROOT_SESSION_PATH";
    private const int pageReadWrite = 0x04;
    private const int chunkBytes = 4 * 1024 * 1024;
    private const int maximumObjectOffset = 512;
    private const int maximumTargetsPerHop = 100_000;
    private const int maximumParentCandidatesPerHop = 250_000;
    private const int maximumChainsPerDepth = 20_000;
    private const int maximumTransitionCandidates = 10_000;

    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static int RunFromEnvironment()
    {
        try
        {
            if (!string.Equals(Environment.GetEnvironmentVariable(enableVariable), "1", StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"Disabled: set {enableVariable}=1 explicitly.");
                return 2;
            }

            NativeLazerRootPhase phase = ParsePhase();
            string baselinePath = RequiredPath(baselineVariable, mustExist: true);
            string sessionPath = RequiredPath(sessionVariable, mustExist: phase != NativeLazerRootPhase.CaptureA);
            int maximumDepth = ParseMaximumDepth();
            NativeLazerResearchSession baseline = LoadBaseline(baselinePath);
            string baselineSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(baselinePath)));
            NativeLazerRootSession session = LoadOrCreateSession(
                sessionPath, phase, baselinePath, baselineSha256, baseline, maximumDepth);

            NativeLazerRootCapture capture = Capture(phase, baseline, maximumDepth);
            ApplyCapture(session, capture);
            SaveJson(sessionPath, session);
            if (phase == NativeLazerRootPhase.CaptureA2)
            {
                SaveSurvivorReport(sessionPath, session);
            }

            WriteSummary(session, capture, sessionPath);
            return 0;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"Root research failed: {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    private static NativeLazerRootCapture Capture(
        NativeLazerRootPhase phase,
        NativeLazerResearchSession baseline,
        int maximumDepth)
    {
        long attachStarted = Stopwatch.GetTimestamp();
        NativeLazerDiscovery discovery = NativeLazerProcessDiscovery.Discover();
        NativeLazerProcessCandidate process = discovery.UniqueCandidate ?? throw new InvalidOperationException(
            $"Lazer process discovery is {discovery.Status}; candidates={discovery.Candidates.Count}.");

        NativeLazerCapture baselineProcess = baseline.Captures[0];
        if (process.ProcessId != baselineProcess.ProcessId || process.StartTimeUtc != baselineProcess.ProcessStartUtc)
        {
            throw new InvalidDataException(
                "The completed level-1 session belongs to another process generation.");
        }

        (NativeLazerProcessMemory? memory, NativeLazerAttachStatus status) =
            NativeLazerProcessMemory.TryAttach(process);
        using NativeLazerProcessMemory attached = memory ?? throw new InvalidOperationException(
            $"Read-only attach failed: {status}.");
        double attachMs = Stopwatch.GetElapsedTime(attachStarted).TotalMilliseconds;

        long regionsStarted = Stopwatch.GetTimestamp();
        NativeLazerMemoryRegion[] regions = attached.EnumerateReadableRegions()
            .Where(IsPrivateReadWrite)
            .OrderBy(region => region.BaseAddress)
            .ToArray();
        double regionsMs = Stopwatch.GetElapsedTime(regionsStarted).TotalMilliseconds;

        string expectedMap = phase == NativeLazerRootPhase.CaptureB ? "B" : "A";
        List<NativeLazerRootChain> current = BuildSeeds(baseline, expectedMap);
        var allChains = new List<NativeLazerRootChain>();
        var depthMetrics = new List<NativeLazerRootDepthMetrics>();
        bool truncated = false;

        for (int depth = 2; depth <= maximumDepth; depth++)
        {
            long hopStarted = Stopwatch.GetTimestamp();
            NativeLazerExpansion expansion = ExpandOneHop(attached, regions, current, depth);
            double elapsedMs = Stopwatch.GetElapsedTime(hopStarted).TotalMilliseconds;
            depthMetrics.Add(new(
                depth,
                current.Count,
                expansion.TargetCount,
                expansion.Chains.Count,
                expansion.BytesScanned,
                elapsedMs,
                expansion.Truncated));
            allChains.AddRange(expansion.Chains);
            current = expansion.Chains;
            truncated |= expansion.Truncated;

            if (current.Count == 0 || truncated)
            {
                break;
            }
        }

        return new(
            phase,
            DateTime.UtcNow,
            process.ProcessId,
            process.StartTimeUtc,
            process.ProductVersion,
            regions.Length,
            BuildSeeds(baseline, expectedMap).Count,
            allChains,
            depthMetrics,
            truncated,
            attachMs,
            regionsMs);
    }

    private static List<NativeLazerRootChain> BuildSeeds(
        NativeLazerResearchSession baseline,
        string mapLabel)
    {
        return baseline.Captures
            .SelectMany(capture => capture.References)
            .Where(reference => reference.MapLabel == mapLabel &&
                                BaseProtection(reference.Protection) == pageReadWrite &&
                                reference.SlotAddress % sizeof(ulong) == 0 &&
                                reference.TargetAddress % sizeof(ulong) == 0)
            .GroupBy(reference => new
            {
                reference.SlotAddress,
                reference.TargetAddress,
                reference.MarkerName,
                reference.MarkerOffset
            })
            .Select(group => group.First())
            .Select(reference => new NativeLazerRootChain(
                mapLabel,
                Depth: 1,
                reference.SlotAddress,
                reference.TargetAddress,
                reference.TargetAddress,
                reference.SlotAddress,
                Shape(reference.MarkerName),
                reference.MarkerOffset,
                [],
                [reference.SlotAddress],
                [reference.TargetAddress],
                reference.RegionBase,
                reference.RegionSize,
                reference.AllocationBase,
                reference.Protection))
            .OrderBy(chain => chain.RootSlot)
            .ThenBy(chain => chain.RootTarget)
            .ToList();
    }

    private static NativeLazerExpansion ExpandOneHop(
        NativeLazerProcessMemory memory,
        IReadOnlyList<NativeLazerMemoryRegion> regions,
        IReadOnlyList<NativeLazerRootChain> chains,
        int depth)
    {
        var targets = new Dictionary<ulong, List<NativeLazerParentCandidate>>();
        bool truncated = false;
        int parentCandidateCount = 0;

        foreach (NativeLazerRootChain chain in chains)
        {
            NativeLazerMemoryRegion? containing = FindRegion(regions, chain.RootSlot);
            if (containing is null)
            {
                continue;
            }

            NativeLazerMemoryRegion region = containing.Value;
            for (int offset = sizeof(ulong); offset <= maximumObjectOffset; offset += sizeof(ulong))
            {
                if (chain.RootSlot < checked((ulong)offset))
                {
                    break;
                }

                ulong parentBase = chain.RootSlot - checked((ulong)offset);
                if (parentBase < region.BaseAddress || parentBase >= region.BaseAddress + region.Size ||
                    parentBase % sizeof(ulong) != 0)
                {
                    continue;
                }

                if (!targets.TryGetValue(parentBase, out List<NativeLazerParentCandidate>? candidates))
                {
                    if (targets.Count >= maximumTargetsPerHop)
                    {
                        truncated = true;
                        break;
                    }

                    candidates = [];
                    targets.Add(parentBase, candidates);
                }

                candidates.Add(new(chain, offset, parentBase));
                parentCandidateCount++;
                if (parentCandidateCount >= maximumParentCandidatesPerHop)
                {
                    truncated = true;
                    break;
                }
            }

            if (truncated)
            {
                break;
            }
        }

        var expanded = new List<NativeLazerRootChain>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        ulong bytesScanned = 0;

        foreach (NativeLazerMemoryRegion region in regions)
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

                bytesScanned += checked((ulong)read.Data.Length);
                int last = read.Data.Length - sizeof(ulong);
                for (int index = 0; index <= last; index += sizeof(ulong))
                {
                    ulong target = BitConverter.ToUInt64(read.Data, index);
                    if (!targets.TryGetValue(target, out List<NativeLazerParentCandidate>? candidates))
                    {
                        continue;
                    }

                    ulong slot = region.BaseAddress + consumed + checked((ulong)index);
                    foreach (NativeLazerParentCandidate candidate in candidates)
                    {
                        if (candidate.Chain.SlotPath.Contains(slot))
                        {
                            continue;
                        }

                        int[] offsets = [candidate.ObjectOffset, .. candidate.Chain.ObjectOffsets];
                        ulong[] slots = [slot, .. candidate.Chain.SlotPath];
                        ulong[] pathTargets = [candidate.ParentBase, .. candidate.Chain.TargetPath];
                        string key = ChainIdentity(
                            slot,
                            candidate.ParentBase,
                            candidate.Chain.LeafTarget,
                            candidate.Chain.MarkerShape,
                            candidate.Chain.MarkerOffset,
                            offsets);
                        if (!seen.Add(key))
                        {
                            continue;
                        }

                        expanded.Add(candidate.Chain with
                        {
                            Depth = depth,
                            RootSlot = slot,
                            RootTarget = candidate.ParentBase,
                            ObjectOffsets = offsets,
                            SlotPath = slots,
                            TargetPath = pathTargets,
                            RegionBase = region.BaseAddress,
                            RegionSize = region.Size,
                            AllocationBase = region.AllocationBase,
                            Protection = region.Protection
                        });

                        if (expanded.Count >= maximumChainsPerDepth)
                        {
                            truncated = true;
                            break;
                        }
                    }

                    if (truncated)
                    {
                        break;
                    }
                }

                if (truncated || read.Data.Length < requested)
                {
                    break;
                }

                consumed += checked((ulong)read.Data.Length);
            }

            if (truncated)
            {
                break;
            }
        }

        return new(expanded, targets.Count, bytesScanned, truncated);
    }

    private static void ApplyCapture(NativeLazerRootSession session, NativeLazerRootCapture capture)
    {
        NativeLazerRootPhase expected = session.Captures.Count switch
        {
            0 => NativeLazerRootPhase.CaptureA,
            1 => NativeLazerRootPhase.CaptureB,
            2 => NativeLazerRootPhase.CaptureA2,
            _ => NativeLazerRootPhase.None
        };
        if (capture.Phase != expected)
        {
            throw new InvalidDataException($"Expected {expected}; received {capture.Phase}.");
        }

        if (session.Captures.Count > 0)
        {
            NativeLazerRootCapture first = session.Captures[0];
            if (capture.ProcessId != first.ProcessId || capture.ProcessStartUtc != first.ProcessStartUtc)
            {
                throw new InvalidDataException("Process identity changed inside the root A/B/A generation.");
            }
        }

        session.Captures.Add(capture with { Chains = [] });
        session.Truncated |= capture.Truncated;

        if (capture.Phase == NativeLazerRootPhase.CaptureA)
        {
            session.AChains = capture.Chains;
            return;
        }

        if (capture.Phase == NativeLazerRootPhase.CaptureB)
        {
            var bIndex = capture.Chains.ToLookup(TransitionKey);
            foreach (NativeLazerRootChain a in session.AChains)
            {
                foreach (NativeLazerRootChain b in bIndex[TransitionKey(a)])
                {
                    if (a.RootTarget == b.RootTarget)
                    {
                        continue;
                    }

                    session.ABTransitions.Add(new(a, b));
                    if (session.ABTransitions.Count >= maximumTransitionCandidates)
                    {
                        session.Truncated = true;
                        break;
                    }
                }

                if (session.Truncated)
                {
                    break;
                }
            }

            session.AChains = [];
            return;
        }

        var a2Index = capture.Chains.ToLookup(TransitionKey);
        foreach (NativeLazerABTransition transition in session.ABTransitions)
        {
            foreach (NativeLazerRootChain a2 in a2Index[TransitionKey(transition.A)])
            {
                session.Survivors.Add(new(
                    transition.A.Depth,
                    transition.A.RootSlot,
                    transition.A.MarkerShape,
                    transition.A.MarkerOffset,
                    transition.A.ObjectOffsets,
                    transition.A.RootTarget,
                    transition.B.RootTarget,
                    a2.RootTarget,
                    transition.A.RootTarget == a2.RootTarget,
                    transition.A.LeafTarget,
                    transition.B.LeafTarget,
                    a2.LeafTarget,
                    transition.A.RegionBase,
                    transition.A.RegionSize,
                    transition.A.AllocationBase,
                    transition.A.Protection));

                if (session.Survivors.Count >= maximumTransitionCandidates)
                {
                    session.Truncated = true;
                    break;
                }
            }

            if (session.Truncated)
            {
                break;
            }
        }

        session.Survivors = session.Survivors
            .GroupBy(value => SurvivorIdentity(value))
            .Select(group => group.First())
            .OrderBy(value => value.Depth)
            .ThenBy(value => value.RootSlot)
            .ToList();
        session.ABTransitions = [];
        session.Outcome = session.Truncated
            ? NativeLazerRootOutcome.Ambiguous
            : session.Survivors.Count switch
            {
                0 => NativeLazerRootOutcome.None,
                1 => NativeLazerRootOutcome.Unique,
                _ => NativeLazerRootOutcome.Ambiguous
            };
    }

    private static NativeLazerRootSession LoadOrCreateSession(
        string path,
        NativeLazerRootPhase phase,
        string baselinePath,
        string baselineSha256,
        NativeLazerResearchSession baseline,
        int maximumDepth)
    {
        if (phase == NativeLazerRootPhase.CaptureA)
        {
            if (File.Exists(path) && !string.Equals(
                    Environment.GetEnvironmentVariable("BEATINSIGHT_NATIVE_LAZER_ROOT_RESET_SESSION"),
                    "1",
                    StringComparison.Ordinal))
            {
                throw new IOException("Root session already exists; use a new path or explicit reset.");
            }

            return new()
            {
                FormatVersion = 1,
                Generation = Environment.GetEnvironmentVariable("BEATINSIGHT_NATIVE_LAZER_ROOT_GENERATION") ?? "A",
                BaselinePath = baselinePath,
                BaselineSha256 = baselineSha256,
                MaximumDepth = maximumDepth,
                ProcessId = baseline.Captures[0].ProcessId,
                ProcessStartUtc = baseline.Captures[0].ProcessStartUtc
            };
        }

        NativeLazerRootSession session = JsonSerializer.Deserialize<NativeLazerRootSession>(
            File.ReadAllText(path), jsonOptions) ?? throw new InvalidDataException("Invalid root session JSON.");
        if (session.FormatVersion != 1 ||
            !string.Equals(session.BaselineSha256, baselineSha256, StringComparison.Ordinal) ||
            session.MaximumDepth != maximumDepth)
        {
            throw new InvalidDataException("Root session does not match the baseline or depth configuration.");
        }

        return session;
    }

    private static NativeLazerResearchSession LoadBaseline(string path)
    {
        NativeLazerResearchSession baseline = JsonSerializer.Deserialize<NativeLazerResearchSession>(
            File.ReadAllText(path), jsonOptions) ?? throw new InvalidDataException("Invalid baseline JSON.");
        if (baseline.FormatVersion != 1 || baseline.Captures.Count != 3 ||
            baseline.Captures[0].Phase != NativeLazerResearchPhase.CaptureA ||
            baseline.Captures[1].Phase != NativeLazerResearchPhase.CaptureB ||
            baseline.Captures[2].Phase != NativeLazerResearchPhase.CaptureA2 ||
            baseline.Captures.Any(capture => capture.MarkerHitsTruncated || capture.ReferenceHitsTruncated))
        {
            throw new InvalidDataException("Baseline must be a complete, untruncated V3.6A A/B/A session.");
        }

        if (baseline.Captures.Any(capture =>
                capture.ProcessId != baseline.Captures[0].ProcessId ||
                capture.ProcessStartUtc != baseline.Captures[0].ProcessStartUtc))
        {
            throw new InvalidDataException("Baseline A/B/A captures do not share one process identity.");
        }

        return baseline;
    }

    private static void SaveJson(string path, object value)
    {
        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException("Output directory must already exist.");
        }

        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, jsonOptions));
        File.Move(temporary, path, overwrite: true);
    }

    private static void SaveSurvivorReport(string sessionPath, NativeLazerRootSession session)
    {
        string path = sessionPath + ".survivors.json";
        SaveJson(path, new NativeLazerRootSurvivorReport(
            session.Generation,
            session.ProcessId,
            session.ProcessStartUtc,
            session.MaximumDepth,
            session.Outcome,
            session.Truncated,
            session.Survivors));
    }

    private static void WriteSummary(
        NativeLazerRootSession session,
        NativeLazerRootCapture capture,
        string path)
    {
        Console.WriteLine($"Phase={capture.Phase}");
        Console.WriteLine($"Generation={session.Generation}");
        Console.WriteLine($"ProcessIdentity={capture.ProcessId}:{capture.ProcessStartUtc:O}");
        Console.WriteLine($"PrivateReadWriteRegions={capture.RegionCount}");
        Console.WriteLine($"Level1Seeds={capture.SeedCount}");
        foreach (NativeLazerRootDepthMetrics metric in capture.Depths)
        {
            Console.WriteLine(
                $"Depth={metric.Depth}; Input={metric.InputChains}; Targets={metric.TargetCount}; " +
                $"Chains={metric.OutputChains}; Bytes={metric.BytesScanned}; Ms={metric.ElapsedMs:F3}; " +
                $"Truncated={metric.Truncated}");
        }

        Console.WriteLine($"Outcome={session.Outcome}; Survivors={session.Survivors.Count}; Truncated={session.Truncated}");
        Console.WriteLine($"Session={path}");
        if (capture.Phase == NativeLazerRootPhase.CaptureA2)
        {
            Console.WriteLine($"SurvivorJson={path}.survivors.json");
        }
    }

    private static NativeLazerRootPhase ParsePhase()
    {
        string text = (Environment.GetEnvironmentVariable(phaseVariable) ?? string.Empty)
            .Replace("_", string.Empty, StringComparison.Ordinal);
        if (!Enum.TryParse(text, ignoreCase: true, out NativeLazerRootPhase phase) ||
            phase == NativeLazerRootPhase.None)
        {
            throw new InvalidDataException($"{phaseVariable} must be CAPTURE_A, CAPTURE_B, or CAPTURE_A2.");
        }

        return phase;
    }

    private static int ParseMaximumDepth()
    {
        string? value = Environment.GetEnvironmentVariable("BEATINSIGHT_NATIVE_LAZER_ROOT_MAX_DEPTH");
        if (string.IsNullOrWhiteSpace(value))
        {
            return 4;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int depth) || depth is < 2 or > 4)
        {
            throw new InvalidDataException("Root maximum depth must be between 2 and 4.");
        }

        return depth;
    }

    private static string RequiredPath(string variable, bool mustExist)
    {
        string? value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"Missing required {variable}.");
        }

        string path = Path.GetFullPath(value);
        if (mustExist && !File.Exists(path))
        {
            throw new FileNotFoundException($"{variable} does not exist.", path);
        }

        return path;
    }

    private static NativeLazerMemoryRegion? FindRegion(
        IReadOnlyList<NativeLazerMemoryRegion> regions,
        ulong address)
    {
        int low = 0;
        int high = regions.Count - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            NativeLazerMemoryRegion region = regions[middle];
            if (address < region.BaseAddress)
            {
                high = middle - 1;
            }
            else if (address - region.BaseAddress >= region.Size)
            {
                low = middle + 1;
            }
            else
            {
                return region;
            }
        }

        return null;
    }

    private static bool IsPrivateReadWrite(NativeLazerMemoryRegion region) =>
        region.IsPrivate && BaseProtection(region.Protection) == pageReadWrite;

    private static uint BaseProtection(uint protection) => protection & 0xff;

    private static string Shape(string markerName) => markerName.EndsWith(".DotNet", StringComparison.Ordinal)
        ? "Guid.DotNet"
        : markerName.EndsWith(".Rfc4122", StringComparison.Ordinal)
            ? "Guid.Rfc4122"
            : markerName.EndsWith(".Ascii", StringComparison.Ordinal)
                ? "Hash.Ascii"
                : "Hash.Utf16";

    private static string TransitionKey(NativeLazerRootChain chain) =>
        $"{chain.Depth}:{chain.RootSlot:X}:{chain.MarkerShape}:{chain.MarkerOffset}:" +
        string.Join(',', chain.ObjectOffsets);

    private static string ChainIdentity(
        ulong slot,
        ulong target,
        ulong leaf,
        string markerShape,
        int markerOffset,
        IReadOnlyList<int> offsets) =>
        $"{slot:X}:{target:X}:{leaf:X}:{markerShape}:{markerOffset}:" + string.Join(',', offsets);

    private static string SurvivorIdentity(NativeLazerRootSurvivor value) =>
        $"{value.Depth}:{value.RootSlot:X}:{value.MarkerShape}:{value.MarkerOffset}:" +
        string.Join(',', value.ObjectOffsets) +
        $":{value.TargetA:X}:{value.TargetB:X}:{value.TargetA2:X}";
}

public enum NativeLazerRootPhase { None, CaptureA, CaptureB, CaptureA2 }

public enum NativeLazerRootOutcome { Unknown, None, Unique, Ambiguous }

internal sealed record NativeLazerParentCandidate(
    NativeLazerRootChain Chain,
    int ObjectOffset,
    ulong ParentBase);

internal sealed record NativeLazerExpansion(
    List<NativeLazerRootChain> Chains,
    int TargetCount,
    ulong BytesScanned,
    bool Truncated);

public sealed record NativeLazerRootChain(
    string MapLabel,
    int Depth,
    ulong RootSlot,
    ulong RootTarget,
    ulong LeafTarget,
    ulong LeafSlot,
    string MarkerShape,
    int MarkerOffset,
    int[] ObjectOffsets,
    ulong[] SlotPath,
    ulong[] TargetPath,
    ulong RegionBase,
    ulong RegionSize,
    ulong AllocationBase,
    uint Protection);

public sealed record NativeLazerRootDepthMetrics(
    int Depth,
    int InputChains,
    int TargetCount,
    int OutputChains,
    ulong BytesScanned,
    double ElapsedMs,
    bool Truncated);

public sealed record NativeLazerRootCapture(
    NativeLazerRootPhase Phase,
    DateTime CapturedUtc,
    int ProcessId,
    DateTime ProcessStartUtc,
    string? ProductVersion,
    int RegionCount,
    int SeedCount,
    List<NativeLazerRootChain> Chains,
    List<NativeLazerRootDepthMetrics> Depths,
    bool Truncated,
    double AttachMs,
    double RegionEnumerationMs);

public sealed record NativeLazerABTransition(
    NativeLazerRootChain A,
    NativeLazerRootChain B);

public sealed record NativeLazerRootSurvivor(
    int Depth,
    ulong RootSlot,
    string MarkerShape,
    int MarkerOffset,
    int[] ObjectOffsets,
    ulong TargetA,
    ulong TargetB,
    ulong TargetA2,
    bool ReturnedToSameTarget,
    ulong LeafA,
    ulong LeafB,
    ulong LeafA2,
    ulong RegionBase,
    ulong RegionSize,
    ulong AllocationBase,
    uint Protection);

public sealed class NativeLazerRootSession
{
    public int FormatVersion { get; set; }
    public string Generation { get; set; } = "A";
    public string BaselinePath { get; set; } = string.Empty;
    public string BaselineSha256 { get; set; } = string.Empty;
    public int MaximumDepth { get; set; } = 4;
    public int ProcessId { get; set; }
    public DateTime ProcessStartUtc { get; set; }
    public List<NativeLazerRootCapture> Captures { get; set; } = [];
    public List<NativeLazerRootChain> AChains { get; set; } = [];
    public List<NativeLazerABTransition> ABTransitions { get; set; } = [];
    public List<NativeLazerRootSurvivor> Survivors { get; set; } = [];
    public NativeLazerRootOutcome Outcome { get; set; } = NativeLazerRootOutcome.Unknown;
    public bool Truncated { get; set; }
}

public sealed record NativeLazerRootSurvivorReport(
    string Generation,
    int ProcessId,
    DateTime ProcessStartUtc,
    int MaximumDepth,
    NativeLazerRootOutcome Outcome,
    bool Truncated,
    IReadOnlyList<NativeLazerRootSurvivor> Survivors);
