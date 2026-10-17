using System.Diagnostics;
using System.Globalization;

namespace BeatInsight.Diagnostics.NativeStable;

internal sealed record NativeStableGameplayStatsSample(
    nuint GameplayBase,
    nuint ScoreBase,
    double Accuracy,
    int CurrentCombo,
    int MaxComboReached,
    int Count300,
    int Count100,
    int Count50,
    int CountGeki,
    int CountKatu,
    int CountMiss,
    int Score,
    int Mods);

internal static class NativeStableGameplayStatsScanner
{
    private const int ChunkBytes = 1024 * 1024;
    private const int GameplayProbeBytes = 0x4c;
    private const int ScoreProbeBytes = 0x98;
    private const int AccuracyProbeBytes = 0x14;
    private const uint MemoryPrivate = 0x20000;

    internal static IReadOnlyList<NativeStableGameplayStatsSample> Discover(
        INativeStableMemoryReader memory,
        IReadOnlyList<NativeStableMemoryRegion> regions,
        CancellationToken cancellationToken = default)
    {
        if (!memory.Is32Bit)
            return [];

        NativeStableMemoryRegion[] writable = regions
            .Where(IsWritablePrivate)
            .OrderBy(region => region.BaseAddress)
            .ToArray();
        HashSet<nuint> scoreBases = DiscoverScoreBases(memory, writable, cancellationToken);
        if (scoreBases.Count == 0)
            return [];

        var found = new Dictionary<nuint, NativeStableGameplayStatsSample>();
        foreach (NativeStableMemoryRegion region in writable)
        {
            nuint offset = 0;
            while (offset < region.Size)
            {
                cancellationToken.ThrowIfCancellationRequested();
                nuint remaining = region.Size - offset;
                int requested = remaining > checked((nuint)(ChunkBytes + GameplayProbeBytes))
                    ? ChunkBytes + GameplayProbeBytes
                    : checked((int)remaining);
                NativeStableMemoryReadResult read = memory.ReadBytes(
                    region.BaseAddress + offset,
                    requested);
                if (read.BytesRead >= GameplayProbeBytes)
                {
                    int scanBytes = Math.Min(ChunkBytes, read.BytesRead - GameplayProbeBytes + 1);
                    int alignment = checked((int)((4 - ((region.BaseAddress + offset) & 3)) & 3));
                    for (int index = alignment; index < scanBytes; index += sizeof(uint))
                    {
                        uint scorePointer = BitConverter.ToUInt32(read.Data, index + 0x38);
                        uint accuracyPointer = BitConverter.ToUInt32(read.Data, index + 0x48);
                        if (!scoreBases.Contains(scorePointer) ||
                            accuracyPointer < 0x10000 || (accuracyPointer & 3) != 0 ||
                            !Contains(writable, accuracyPointer, AccuracyProbeBytes))
                        {
                            continue;
                        }

                        nuint gameplayBase = region.BaseAddress + offset + checked((nuint)index);
                        NativeStableGameplayStatsSample? sample = TryRead(
                            memory,
                            gameplayBase,
                            scorePointer,
                            accuracyPointer);
                        // Discovery-only noise rejection. Direct reads do not impose
                        // this floor; production will follow the validated Ruleset root.
                        if (sample is { Accuracy: >= 0.05 })
                            found.TryAdd(gameplayBase, sample);
                    }
                }

                if (remaining <= ChunkBytes)
                    break;
                offset += ChunkBytes;
            }
        }

        return found.Values.OrderBy(item => item.GameplayBase).ToArray();
    }

    private static HashSet<nuint> DiscoverScoreBases(
        INativeStableMemoryReader memory,
        IReadOnlyList<NativeStableMemoryRegion> regions,
        CancellationToken cancellationToken)
    {
        var found = new HashSet<nuint>();
        foreach (NativeStableMemoryRegion region in regions)
        {
            nuint offset = 0;
            while (offset < region.Size)
            {
                cancellationToken.ThrowIfCancellationRequested();
                nuint remaining = region.Size - offset;
                int requested = remaining > checked((nuint)(ChunkBytes + ScoreProbeBytes))
                    ? ChunkBytes + ScoreProbeBytes
                    : checked((int)remaining);
                NativeStableMemoryReadResult read = memory.ReadBytes(
                    region.BaseAddress + offset,
                    requested);
                if (read.BytesRead >= ScoreProbeBytes)
                {
                    int scanBytes = Math.Min(ChunkBytes, read.BytesRead - ScoreProbeBytes + 1);
                    int alignment = checked((int)((4 - ((region.BaseAddress + offset) & 3)) & 3));
                    for (int index = alignment; index < scanBytes; index += sizeof(uint))
                    {
                        uint modsPointer = BitConverter.ToUInt32(read.Data, index + 0x1c);
                        if (LooksLikeScore(read.Data, index) &&
                            modsPointer >= 0x10000 && (modsPointer & 3) == 0 &&
                            Contains(regions, modsPointer, 0x10))
                        {
                            found.Add(region.BaseAddress + offset + checked((nuint)index));
                        }
                    }
                }

                if (remaining <= ChunkBytes)
                    break;
                offset += ChunkBytes;
            }
        }
        return found;
    }

    private static bool LooksLikeScore(byte[] data, int index)
    {
        int maxCombo = BitConverter.ToUInt16(data, index + 0x68);
        int score = BitConverter.ToInt32(data, index + 0x78);
        int count100 = BitConverter.ToUInt16(data, index + 0x88);
        int count300 = BitConverter.ToUInt16(data, index + 0x8a);
        int count50 = BitConverter.ToUInt16(data, index + 0x8c);
        int countGeki = BitConverter.ToUInt16(data, index + 0x8e);
        int countKatu = BitConverter.ToUInt16(data, index + 0x90);
        int countMiss = BitConverter.ToUInt16(data, index + 0x92);
        int combo = BitConverter.ToUInt16(data, index + 0x94);
        long totalHits = (long)count300 + count100 + count50 + countMiss;
        return score >= 0 && totalHits is > 0 and <= 20_000 && count300 > 0 &&
            count300 <= 20_000 && count100 <= 20_000 && count50 <= 20_000 &&
            countGeki <= 20_000 && countKatu <= 20_000 && countMiss <= 20_000 &&
            countGeki <= totalHits && countKatu <= totalHits &&
            combo <= 20_000 && maxCombo <= 20_000 && combo <= maxCombo;
    }

    internal static NativeStableGameplayStatsSample? Read(
        INativeStableMemoryReader memory,
        nuint gameplayBase)
    {
        if (!memory.TryReadPointer(gameplayBase + 0x38, out nuint scoreBase) ||
            !memory.TryReadPointer(gameplayBase + 0x48, out nuint accuracyBase))
        {
            return null;
        }

        return TryRead(memory, gameplayBase, scoreBase, accuracyBase);
    }

    private static NativeStableGameplayStatsSample? TryRead(
        INativeStableMemoryReader memory,
        nuint gameplayBase,
        nuint scoreBase,
        nuint accuracyBase)
    {
        NativeStableMemoryReadResult score = memory.ReadBytes(scoreBase, ScoreProbeBytes);
        NativeStableMemoryReadResult accuracy = memory.ReadBytes(accuracyBase, AccuracyProbeBytes);
        if (!score.Complete || !accuracy.Complete ||
            !memory.TryReadPointer(scoreBase + 0x1c, out nuint modsBase))
        {
            return null;
        }

        NativeStableMemoryReadResult mods = memory.ReadBytes(modsBase + 0x8, sizeof(int) * 2);
        if (!mods.Complete)
            return null;

        double rawAccuracy = BitConverter.ToDouble(accuracy.Data, 0x0c);
        double accuracyValue = rawAccuracy > 1.000001 && rawAccuracy <= 100.000001
            ? rawAccuracy / 100d
            : rawAccuracy;
        int currentCombo = BitConverter.ToUInt16(score.Data, 0x94);
        int maxCombo = BitConverter.ToUInt16(score.Data, 0x68);
        int count100 = BitConverter.ToUInt16(score.Data, 0x88);
        int count300 = BitConverter.ToUInt16(score.Data, 0x8a);
        int count50 = BitConverter.ToUInt16(score.Data, 0x8c);
        int countGeki = BitConverter.ToUInt16(score.Data, 0x8e);
        int countKatu = BitConverter.ToUInt16(score.Data, 0x90);
        int countMiss = BitConverter.ToUInt16(score.Data, 0x92);
        int scoreValue = BitConverter.ToInt32(score.Data, 0x78);
        int modsValue = BitConverter.ToInt32(mods.Data, 0) ^
            BitConverter.ToInt32(mods.Data, sizeof(int));
        long totalHits = (long)count300 + count100 + count50 + countMiss;
        double derivedAccuracy = totalHits == 0
            ? double.NaN
            : ((300d * count300) + (100d * count100) + (50d * count50)) /
                (300d * totalHits);

        if (!double.IsFinite(accuracyValue) || accuracyValue is <= 0 or > 1.000001 ||
            totalHits is <= 0 or > 100_000 ||
            count300 <= 0 || countGeki > totalHits || countKatu > totalHits ||
            currentCombo > 100_000 || maxCombo > 100_000 ||
            currentCombo > maxCombo || scoreValue < 0 || modsValue < 0 ||
            Math.Abs(accuracyValue - derivedAccuracy) > 0.02)
        {
            return null;
        }

        return new(
            gameplayBase,
            scoreBase,
            accuracyValue,
            currentCombo,
            maxCombo,
            count300,
            count100,
            count50,
            countGeki,
            countKatu,
            countMiss,
            scoreValue,
            modsValue);
    }

    private static bool IsWritablePrivate(NativeStableMemoryRegion region)
    {
        uint protection = region.Protect & 0xff;
        return region.Type == MemoryPrivate && protection is 0x04 or 0x08 or 0x40 or 0x80;
    }

    private static bool Contains(
        IReadOnlyList<NativeStableMemoryRegion> regions,
        nuint address,
        int requiredBytes)
    {
        foreach (NativeStableMemoryRegion region in regions)
        {
            if (address < region.BaseAddress)
                return false;
            if (region.Contains(address) &&
                region.Size - (address - region.BaseAddress) >= checked((nuint)requiredBytes))
            {
                return true;
            }
        }
        return false;
    }
}

internal sealed class NativeStableGameplayStatsForensicSession : IDisposable
{
    private const string ActiveRulesetContainerPattern = "7D 15 A1 ?? ?? ?? ?? 85 C0";

    private NativeStableProcessMemory? memory;
    private int? processId;
    private DateTimeOffset? processStartIdentity;
    private IReadOnlyList<NativeStableMemoryRegion> regions = [];
    private IReadOnlyList<NativeStableModuleRange> modules = [];
    private IReadOnlyList<NativeStableGameplayStatsSample> candidates = [];
    private int? selectedIndex;
    private nuint? containerMatch;
    private nuint? globalContainerAddress;
    private IReadOnlySet<nuint> comparisonRulesets = new HashSet<nuint>();

    internal void DiscoverContainer(IReadOnlyList<nuint> rulesetsToCompare)
    {
        Reset();
        var timer = Stopwatch.StartNew();
        NativeStableProcessDiscoveryResult discovery = NativeStableProcessDiscovery.Discover();
        NativeStableProcessCandidate? candidate = discovery.UniqueCandidate;
        if (candidate is null)
        {
            Console.WriteLine($"STATS_CONTAINER_DISCOVERY available=false reason=ProcessUnavailable status={discovery.Status}");
            return;
        }

        NativeStableAttachResult attachment = NativeStableProcessMemory.TryAttach(candidate);
        if (attachment.Memory is null)
        {
            Console.WriteLine($"STATS_CONTAINER_DISCOVERY available=false reason=AttachFailed status={attachment.Status} pid={candidate.ProcessId}");
            return;
        }

        memory = attachment.Memory;
        processId = candidate.ProcessId;
        try
        {
            using Process process = Process.GetProcessById(candidate.ProcessId);
            processStartIdentity = process.StartTime.ToUniversalTime();
        }
        catch
        {
            Console.WriteLine($"STATS_CONTAINER_DISCOVERY available=false reason=ProcessLost pid={candidate.ProcessId}");
            Reset();
            return;
        }

        regions = memory.EnumerateReadableRegions();
        modules = NativeStableModuleCatalog.Capture(candidate.ProcessId);
        comparisonRulesets = rulesetsToCompare.ToHashSet();
        NativeStableMemoryRegion[] executableRegions = regions
            .Where(region => IsExecutable(region.Protect))
            .ToArray();
        IReadOnlyList<nuint> matches = NativeStableProcessPatternSearch.FindAll(
            memory,
            executableRegions,
            BytePattern.Parse(ActiveRulesetContainerPattern),
            maximumMatches: 3);
        timer.Stop();

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"STATS_CONTAINER_DISCOVERY identity={Identity()} signatureMatches={matches.Count} elapsedMs={timer.Elapsed.TotalMilliseconds:F3}"));
        foreach (nuint match in matches)
        {
            NativeStableObservedAddress observed = NativeStableRootCharacterizer.Describe(
                match, regions, modules);
            Console.WriteLine(
                $"STATS_CONTAINER_MATCH address=0x{match:X} region=0x{observed.RegionBase:X} " +
                $"protect=0x{observed.Protection:X} type=0x{observed.Type:X} " +
                $"module={observed.Module?.Name ?? "<none>"}");
        }

        if (matches.Count != 1)
        {
            Console.WriteLine($"STATS_CONTAINER_DISCOVERY available=false reason={(matches.Count == 0 ? "SignatureAbsent" : "MultipleSignatureMatches")}");
            return;
        }

        nuint matchAddress = matches[0];
        if (matchAddress < 0xb ||
            !memory.TryReadPointer(matchAddress - 0xb, out nuint container) ||
            container == 0)
        {
            Console.WriteLine($"STATS_CONTAINER_DISCOVERY available=false reason=ContainerUnreadable match=0x{matchAddress:X}");
            return;
        }

        containerMatch = matchAddress;
        globalContainerAddress = container;
        PrintContainerSample("discovery", 0);
    }

    internal void SampleContainer(string label, int count, int intervalMs)
    {
        if (memory is null || !memory.IsAlive || globalContainerAddress is null)
        {
            Console.WriteLine($"STATS_CONTAINER_SAMPLE label={label} available=false reason=ContainerNotResolved");
            return;
        }

        for (int sampleIndex = 0; sampleIndex < count; sampleIndex++)
        {
            PrintContainerSample(label, sampleIndex);
            if (sampleIndex + 1 < count && intervalMs > 0)
                Thread.Sleep(intervalMs);
        }
    }

    private void PrintContainerSample(string label, int sampleIndex)
    {
        if (memory is null || globalContainerAddress is not nuint container)
        {
            Console.WriteLine($"STATS_CONTAINER_SAMPLE label={label} sample={sampleIndex} available=false reason=ContainerNotResolved");
            return;
        }

        long started = Stopwatch.GetTimestamp();
        if (!memory.TryReadPointer(container + 0x4, out nuint ruleset))
        {
            PrintContainerFailure(label, sampleIndex, container, "ActiveSlotUnreadable", started);
            return;
        }
        if (ruleset == 0)
        {
            PrintContainerFailure(label, sampleIndex, container, "ActiveRulesetNull", started);
            return;
        }
        if (!IsReadableAlignedPointer(ruleset, 0x68))
        {
            PrintContainerFailure(label, sampleIndex, container, "ActiveRulesetInvalid", started, ruleset);
            return;
        }
        if (!memory.TryReadPointer(ruleset + 0x64, out nuint gameplay))
        {
            PrintContainerFailure(label, sampleIndex, container, "RulesetUnreadable", started, ruleset);
            return;
        }
        if (gameplay == 0)
        {
            PrintContainerFailure(label, sampleIndex, container, "GameplayChildNull", started, ruleset);
            return;
        }
        if (!IsReadableAlignedPointer(gameplay, 0x3c))
        {
            PrintContainerFailure(label, sampleIndex, container, "GameplayChildInvalid", started, ruleset, gameplay);
            return;
        }
        if (!memory.TryReadPointer(gameplay + 0x38, out nuint score))
        {
            PrintContainerFailure(label, sampleIndex, container, "GameplayUnreadable", started, ruleset, gameplay);
            return;
        }
        if (score == 0)
        {
            PrintContainerFailure(label, sampleIndex, container, "ScoreChildNull", started, ruleset, gameplay);
            return;
        }
        if (!IsReadableAlignedPointer(score, sizeof(uint)))
        {
            PrintContainerFailure(label, sampleIndex, container, "ScoreChildInvalid", started, ruleset, gameplay);
            return;
        }

        string matched = comparisonRulesets.Count == 0
            ? "not-requested"
            : comparisonRulesets.Contains(ruleset) ? $"0x{ruleset:X}" : "none";
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"STATS_CONTAINER_SAMPLE label={label} sample={sampleIndex} available=true identity={Identity()} " +
            $"match=0x{containerMatch:X} container=0x{container:X} ruleset=0x{ruleset:X} " +
            $"gameplay=0x{gameplay:X} score=0x{score:X} matchedCandidate={matched} " +
            $"elapsedMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F6}"));
    }

    private void PrintContainerFailure(
        string label,
        int sampleIndex,
        nuint container,
        string reason,
        long started,
        nuint ruleset = 0,
        nuint gameplay = 0) =>
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"STATS_CONTAINER_SAMPLE label={label} sample={sampleIndex} available=false reason={reason} " +
            $"identity={Identity()} match=0x{containerMatch:X} container=0x{container:X} " +
            $"ruleset=0x{ruleset:X} gameplay=0x{gameplay:X} score=0x0 " +
            $"elapsedMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F6}"));

    internal void MeasureContainerPerformance(int iterations)
    {
        if (memory is null || globalContainerAddress is not nuint container)
        {
            Console.WriteLine("STATS_CONTAINER_PERF available=false reason=ContainerNotResolved");
            return;
        }

        var slot = new double[iterations];
        var gameplayChild = new double[iterations];
        var scoreChild = new double[iterations];
        int valid = 0;
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            nuint ruleset = 0;
            nuint gameplay = 0;
            nuint score = 0;
            long started = Stopwatch.GetTimestamp();
            bool slotRead = memory.TryReadPointer(container + 0x4, out ruleset) &&
                IsReadableAlignedPointer(ruleset, 0x68);
            slot[iteration] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            started = Stopwatch.GetTimestamp();
            bool gameplayRead = slotRead && memory.TryReadPointer(ruleset + 0x64, out gameplay) &&
                IsReadableAlignedPointer(gameplay, 0x3c);
            gameplayChild[iteration] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            started = Stopwatch.GetTimestamp();
            bool scoreRead = gameplayRead && memory.TryReadPointer(gameplay + 0x38, out score) &&
                IsReadableAlignedPointer(score, sizeof(uint));
            scoreChild[iteration] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (scoreRead)
                valid++;
        }

        Console.WriteLine(
            $"STATS_CONTAINER_PERF iterations={iterations} valid={valid} " +
            $"slot={PerformanceText(slot)} gameplay={PerformanceText(gameplayChild)} score={PerformanceText(scoreChild)}");
    }

    private static string PerformanceText(double[] samples)
    {
        Array.Sort(samples);
        return string.Create(CultureInfo.InvariantCulture,
            $"avg:{samples.Average():F6},median:{samples[samples.Length / 2]:F6},p95:{samples[Math.Min(samples.Length - 1, (int)Math.Ceiling(samples.Length * .95) - 1)]:F6}");
    }

    private bool IsReadableAlignedPointer(nuint address, int requiredBytes)
    {
        if (address < 0x10000 || (address & 3) != 0)
            return false;

        foreach (NativeStableMemoryRegion region in regions)
        {
            if (region.Contains(address) &&
                region.Size - (address - region.BaseAddress) >= checked((nuint)requiredBytes))
            {
                return true;
            }
        }

        return false;
    }

    internal void Discover()
    {
        Reset();
        var timer = Stopwatch.StartNew();
        NativeStableProcessDiscoveryResult discovery = NativeStableProcessDiscovery.Discover();
        NativeStableProcessCandidate? candidate = discovery.UniqueCandidate;
        if (candidate is null)
        {
            Console.WriteLine($"STATS_DISCOVERY unavailable status={discovery.Status}");
            return;
        }

        NativeStableAttachResult attachment = NativeStableProcessMemory.TryAttach(candidate);
        if (attachment.Memory is null)
        {
            Console.WriteLine($"STATS_DISCOVERY unavailable status={attachment.Status} pid={candidate.ProcessId}");
            return;
        }

        memory = attachment.Memory;
        processId = candidate.ProcessId;
        try
        {
            using Process process = Process.GetProcessById(candidate.ProcessId);
            processStartIdentity = process.StartTime.ToUniversalTime();
        }
        catch
        {
            Console.WriteLine($"STATS_DISCOVERY unavailable status=ProcessLost pid={candidate.ProcessId}");
            Reset();
            return;
        }

        regions = memory.EnumerateReadableRegions();
        modules = NativeStableModuleCatalog.Capture(candidate.ProcessId);
        candidates = NativeStableGameplayStatsScanner.Discover(memory, regions);
        timer.Stop();
        if (candidates.Count == 1)
            selectedIndex = 0;

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"STATS_DISCOVERY pid={processId} identity={Identity()} candidates={candidates.Count} selected={SelectedText()} elapsedMs={timer.Elapsed.TotalMilliseconds:F3}"));
        for (int index = 0; index < candidates.Count; index++)
            Print("discovery", index, candidates[index]);
    }

    internal void Select(int index)
    {
        if (index < 0 || index >= candidates.Count)
        {
            Console.WriteLine($"STATS_SELECT invalid index={index} candidates={candidates.Count}");
            return;
        }

        selectedIndex = index;
        Console.WriteLine($"STATS_SELECT index={index} gameplayBase=0x{candidates[index].GameplayBase:X}");
    }

    internal void FindRulesetRoots()
    {
        if (memory is null || selectedIndex is not int index || index >= candidates.Count)
        {
            Console.WriteLine("STATS_ROOTS unavailable");
            return;
        }

        nuint gameplayBase = candidates[index].GameplayBase;
        nuint[] references = NativeStableRootReverseDiscovery
            .FindAlignedReferences(memory, regions, [gameplayBase])
            .ToArray();
        var roots = new List<nuint>();
        foreach (nuint reference in references)
        {
            if (reference < 0x64)
                continue;
            nuint ruleset = reference - 0x64;
            if (memory.TryReadPointer(ruleset + 0x64, out nuint value) && value == gameplayBase)
                roots.Add(ruleset);
        }

        Console.WriteLine($"STATS_ROOTS gameplayBase=0x{gameplayBase:X} references={references.Length} roots={roots.Count}");
        for (int rootIndex = 0; rootIndex < roots.Count; rootIndex++)
            Console.WriteLine($"STATS_ROOT index={rootIndex} ruleset=0x{roots[rootIndex]:X} field=0x{roots[rootIndex] + 0x64:X}");
    }

    internal void FollowRuleset(nuint ruleset)
    {
        if (memory is null || ruleset == 0 ||
            !memory.TryReadPointer(ruleset + 0x64, out nuint gameplayBase))
        {
            Console.WriteLine($"STATS_FOLLOW ruleset=0x{ruleset:X} available=false");
            return;
        }

        NativeStableGameplayStatsSample? sample =
            NativeStableGameplayStatsScanner.Read(memory, gameplayBase);
        Console.WriteLine($"STATS_FOLLOW ruleset=0x{ruleset:X} gameplayBase=0x{gameplayBase:X} valid={sample is not null}");
        if (sample is not null)
            Print("follow", -1, sample);
    }

    internal void InspectRulesetOwnership(nuint ruleset)
    {
        if (memory is null || ruleset == 0)
        {
            Console.WriteLine($"STATS_OWNER ruleset=0x{ruleset:X} available=false");
            return;
        }

        nuint[] rulesetReferences = NativeStableRootReverseDiscovery
            .FindAlignedReferences(memory, regions, [ruleset])
            .ToArray();
        Console.WriteLine($"STATS_OWNER ruleset=0x{ruleset:X} slotReferences={rulesetReferences.Length}");

        nuint[] owners = rulesetReferences
            .Where(slotAddress => slotAddress >= sizeof(uint))
            .Select(slotAddress => slotAddress - sizeof(uint))
            .Distinct()
            .ToArray();
        IReadOnlyDictionary<nuint, IReadOnlyList<nuint>> executableReferences =
            FindExecutableOwnerReferences(memory, regions, owners);

        foreach (nuint slotAddress in rulesetReferences)
        {
            if (slotAddress < sizeof(uint))
                continue;

            nuint owner = slotAddress - sizeof(uint);
            NativeStableObservedAddress ownerObserved = NativeStableRootCharacterizer.Describe(
                owner, regions, modules);
            NativeStableObservedAddress[] codeReferences = executableReferences[owner]
                .Select(reference => NativeStableRootCharacterizer.Describe(reference, regions, modules))
                .ToArray();

            Console.WriteLine(
                $"STATS_OWNER_SLOT ruleset=0x{ruleset:X} owner=0x{owner:X} slot=0x{slotAddress:X} " +
                $"ownerRegion=0x{ownerObserved.RegionBase:X} ownerAllocation=0x{ownerObserved.AllocationBase:X} " +
                $"ownerProtect=0x{ownerObserved.Protection:X} ownerType=0x{ownerObserved.Type:X} " +
                $"ownerModule={ownerObserved.Module?.Name ?? "<none>"} " +
                $"executableReferences={codeReferences.Length}");
            foreach (NativeStableObservedAddress reference in codeReferences.Take(20))
            {
                Console.WriteLine(
                    $"STATS_OWNER_CODE_REF owner=0x{owner:X} address=0x{reference.AbsoluteAddress:X} " +
                    $"region=0x{reference.RegionBase:X} allocation=0x{reference.AllocationBase:X} " +
                    $"protect=0x{reference.Protection:X} type=0x{reference.Type:X} " +
                    $"module={reference.Module?.Name ?? "<none>"}");
            }
        }
    }

    private static IReadOnlyDictionary<nuint, IReadOnlyList<nuint>>
        FindExecutableOwnerReferences(
            INativeStableMemoryReader memory,
            IReadOnlyList<NativeStableMemoryRegion> regions,
            IReadOnlyList<nuint> owners)
    {
        var targets = owners
            .Where(owner => owner <= uint.MaxValue)
            .ToDictionary(owner => checked((uint)owner), _ => new List<nuint>());
        if (targets.Count == 0)
            return owners.ToDictionary(owner => owner, _ => (IReadOnlyList<nuint>)[]);

        const int chunkBytes = 1024 * 1024;
        foreach (NativeStableMemoryRegion region in regions.Where(region =>
                     IsExecutable(region.Protect)))
        {
            nuint offset = 0;
            while (offset < region.Size)
            {
                nuint remaining = region.Size - offset;
                int requested = remaining > checked((nuint)(chunkBytes + 3))
                    ? chunkBytes + 3
                    : checked((int)remaining);
                NativeStableMemoryReadResult read = memory.ReadBytes(
                    region.BaseAddress + offset, requested);
                int scanBytes = Math.Min(chunkBytes, Math.Max(0, read.BytesRead - 3));
                for (int index = 0; index < scanBytes; index++)
                {
                    uint value = BitConverter.ToUInt32(read.Data, index);
                    if (targets.TryGetValue(value, out List<nuint>? found))
                        found.Add(region.BaseAddress + offset + checked((nuint)index));
                }

                if (remaining <= chunkBytes)
                    break;
                offset += chunkBytes;
            }
        }

        return owners.ToDictionary(
            owner => owner,
            owner => (IReadOnlyList<nuint>)(owner <= uint.MaxValue
                ? targets[checked((uint)owner)]
                : []));
    }

    private static bool IsExecutable(uint protection) =>
        (protection & 0xff) is 0x10 or 0x20 or 0x40 or 0x80;

    internal void InspectAddress(nuint address)
    {
        if (memory is null || address == 0)
        {
            Console.WriteLine($"STATS_INSPECT address=0x{address:X} available=false");
            return;
        }

        NativeStableMemoryReadResult value = memory.ReadBytes(address, 0x80);
        if (!value.Complete)
        {
            Console.WriteLine($"STATS_INSPECT address=0x{address:X} available=false");
            return;
        }

        uint methodTable = BitConverter.ToUInt32(value.Data, 0);
        NativeStableMemoryReadResult method = memory.ReadBytes(methodTable, 0x20);
        string cells = string.Join(",", Enumerable.Range(0, 8).Select(index =>
            $"+0x{index * 4:X}=0x{BitConverter.ToUInt32(value.Data, index * 4):X}"));
        string methodCells = method.Complete
            ? string.Join(",", Enumerable.Range(0, 8).Select(index =>
                $"+0x{index * 4:X}=0x{BitConverter.ToUInt32(method.Data, index * 4):X}"))
            : "unreadable";
        NativeStableObservedAddress observed = NativeStableRootCharacterizer.Describe(
            address, regions, modules);
        nuint[] references = NativeStableRootReverseDiscovery
            .FindAlignedReferences(memory, regions, [address])
            .ToArray();
        Console.WriteLine($"STATS_INSPECT address=0x{address:X} region=0x{observed.RegionBase:X} allocation=0x{observed.AllocationBase:X} allocationOffset=0x{observed.OffsetFromAllocationBase:X} protect=0x{observed.Protection:X} type=0x{observed.Type:X} module={observed.Module?.Name ?? "<none>"} methodTable=0x{methodTable:X} cells={cells} method={methodCells} references={references.Length}");
        foreach (nuint reference in references.Take(20))
        {
            NativeStableObservedAddress referenceObserved =
                NativeStableRootCharacterizer.Describe(reference, regions, modules);
            Console.WriteLine($"STATS_INSPECT_REF address=0x{reference:X} region=0x{referenceObserved.RegionBase:X} allocation=0x{referenceObserved.AllocationBase:X} allocationOffset=0x{referenceObserved.OffsetFromAllocationBase:X} protect=0x{referenceObserved.Protection:X} type=0x{referenceObserved.Type:X} module={referenceObserved.Module?.Name ?? "<none>"}");
        }
    }

    internal void Sample(string label, int count, int intervalMs)
    {
        if (memory is null || !memory.IsAlive || candidates.Count == 0)
        {
            Console.WriteLine($"STATS_SAMPLE label={label} available=false");
            return;
        }

        IReadOnlyList<int> indices = selectedIndex is int selected ? [selected] :
            Enumerable.Range(0, candidates.Count).Take(20).ToArray();
        for (int sampleIndex = 0; sampleIndex < count; sampleIndex++)
        {
            foreach (int candidateIndex in indices)
            {
                NativeStableGameplayStatsSample? sample =
                    NativeStableGameplayStatsScanner.Read(
                        memory,
                        candidates[candidateIndex].GameplayBase);
                if (sample is null)
                    Console.WriteLine($"STATS_SAMPLE label={label} sample={sampleIndex} candidate={candidateIndex} valid=false identity={Identity()}");
                else
                    Print(label, candidateIndex, sample, sampleIndex);
            }

            if (sampleIndex + 1 < count && intervalMs > 0)
                Thread.Sleep(intervalMs);
        }
    }

    internal void MeasurePerformance(int iterations)
    {
        if (memory is null || selectedIndex is not int index || index >= candidates.Count)
        {
            Console.WriteLine("STATS_PERF unavailable");
            return;
        }

        nuint gameplayBase = candidates[index].GameplayBase;
        var samples = new double[iterations];
        int valid = 0;
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            long start = Stopwatch.GetTimestamp();
            if (NativeStableGameplayStatsScanner.Read(memory, gameplayBase) is not null)
                valid++;
            samples[iteration] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        Array.Sort(samples);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"STATS_PERF iterations={iterations} valid={valid} avgMs={samples.Average():F6} medianMs={samples[samples.Length / 2]:F6} p95Ms={samples[Math.Min(samples.Length - 1, (int)Math.Ceiling(samples.Length * .95) - 1)]:F6}"));
    }

    private void Print(
        string label,
        int candidateIndex,
        NativeStableGameplayStatsSample sample,
        int sampleIndex = 0) =>
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"STATS_SAMPLE label={label} sample={sampleIndex} candidate={candidateIndex} identity={Identity()} " +
            $"gameplayBase=0x{sample.GameplayBase:X} scoreBase=0x{sample.ScoreBase:X} " +
            $"combo={sample.CurrentCombo} maxCombo={sample.MaxComboReached} n300={sample.Count300} n100={sample.Count100} n50={sample.Count50} miss={sample.CountMiss} " +
            $"accuracy={sample.Accuracy:F8} mods=0x{sample.Mods:X} score={sample.Score}"));

    private string Identity() => processId is int pid && processStartIdentity is DateTimeOffset start
        ? string.Create(CultureInfo.InvariantCulture, $"{pid}:{start.UtcDateTime:O}")
        : "N/A";

    private string SelectedText() => selectedIndex?.ToString(CultureInfo.InvariantCulture) ?? "none";

    private void Reset()
    {
        memory?.Dispose();
        memory = null;
        processId = null;
        processStartIdentity = null;
        regions = [];
        modules = [];
        candidates = [];
        selectedIndex = null;
        containerMatch = null;
        globalContainerAddress = null;
        comparisonRulesets = new HashSet<nuint>();
    }

    public void Dispose() => Reset();
}
