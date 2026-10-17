using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace BeatInsight.Diagnostics.NativeMcOsu;

/// <summary>
/// EXPERIMENTAL — V3.8A PROOF OF CONCEPT — READ-ONLY / DIAGNOSTIC ONLY.
/// No provider, hook, injection, write, patch, remote thread, suspension, or
/// debugger attachment is present in this implementation.
/// </summary>
internal static class NativeMcOsuPoc
{
    public static Task<int> RunAsync(string[] args)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        Process[] processes = Process.GetProcesses()
            .Where(p => p.ProcessName.StartsWith("McEngine", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        try
        {
            List<Process> candidates = processes.Where(IsMcOsu).ToList();
            if (candidates.Count != 1)
            {
                Console.WriteLine($"Classification={(candidates.Count == 0 ? "None" : "Ambiguous")}");
                Console.WriteLine($"ProcessCandidates={candidates.Count}");
                return Task.FromResult(candidates.Count == 0 ? 2 : 3);
            }

            using Process process = candidates[0];
            DateTime startUtc = process.StartTime.ToUniversalTime();
            Console.WriteLine($"ProcessId={process.Id}");
            Console.WriteLine($"ProcessStartUtc={startUtc:O}");
            Console.WriteLine($"Image={process.MainModule?.FileName}");

            using var memory = ProcessMemory.Attach(process);
            DiscoveryResult result = Discovery.Discover(memory);
            Console.WriteLine($"Classification={result.Classification}");
            Console.WriteLine($"OsuObject={Format(result.OsuObject)}");
            Console.WriteLine($"SongBrowser={Format(result.SongBrowserObject)}");
            Console.WriteLine($"RuntimeBeatmap={Format(result.RuntimeBeatmapObject)}");
            Console.WriteLine($"Difficulty={Format(result.DifficultyObject)}");
            Console.WriteLine($"DifficultyPointerOffset={FormatOffset(result.DifficultyPointerOffset)}");
            Console.WriteLine($"PathDescriptorOffset={FormatOffset(result.PathDescriptorOffset)}");
            Console.WriteLine($"Path={result.Path ?? "<none>"}");
            Console.WriteLine($"DifficultyName={result.DifficultyName ?? "<none>"}");
            Console.WriteLine($"BeatmapId={result.BeatmapId?.ToString() ?? "<none>"}");
            Console.WriteLine($"InitialDiscoveryMs={elapsed.Elapsed.TotalMilliseconds:F1}");
            Console.WriteLine($"ReadOperations={memory.ReadOperations}");
            Console.WriteLine($"BytesRead={memory.BytesRead}");
            Console.WriteLine($"Truncated={result.Truncated}");
            foreach (string diagnostic in result.Diagnostics)
            {
                Console.WriteLine($"Diagnostic={diagnostic}");
            }

            if (result.Classification == Classification.Unique && args.Contains("--refresh"))
            {
                long readsBefore = memory.ReadOperations;
                long bytesBefore = memory.BytesRead;
                Stopwatch refreshElapsed = Stopwatch.StartNew();
                RefreshResult refresh = Discovery.Refresh(memory, result, process.Id, startUtc);
                Console.WriteLine($"RefreshClassification={refresh.Classification}");
                Console.WriteLine($"RefreshPath={refresh.Path ?? "<none>"}");
                Console.WriteLine($"RefreshMs={refreshElapsed.Elapsed.TotalMilliseconds:F1}");
                Console.WriteLine($"RefreshReadOperations={memory.ReadOperations - readsBefore}");
                Console.WriteLine($"RefreshBytesRead={memory.BytesRead - bytesBefore}");
            }

            return Task.FromResult(result.Classification == Classification.Unique ? 0 : 4);
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static bool IsMcOsu(Process process)
    {
        try
        {
            string? path = process.MainModule?.FileName;
            return path is not null &&
                   Path.GetFileName(path).StartsWith("McEngine", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(new DirectoryInfo(Path.GetDirectoryName(path)!).Name, "McOsu", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string Format(nuint? value) => value is null ? "<none>" : $"0x{value.Value:x}";
    private static string FormatOffset(int? value) => value is null ? "<none>" : $"0x{value.Value:x}";
}

internal enum Classification { None, Unique, Ambiguous, Unknown }

internal sealed record DiscoveryResult(
    Classification Classification,
    nuint? OsuObject,
    nuint? SongBrowserObject,
    nuint? RuntimeBeatmapObject,
    nuint? DifficultyObject,
    int? DifficultyPointerOffset,
    int? PathDescriptorOffset,
    string? Path,
    string? DifficultyName,
    int? BeatmapId,
    bool Truncated,
    IReadOnlyList<string> Diagnostics,
    int ProcessId,
    DateTime ProcessStartUtc);

internal sealed record RefreshResult(Classification Classification, string? Path);

internal static class Discovery
{
    private const int ObjectWindow = 0x10000;
    private const int RuntimeWindow = 0x500;
    private const int DifficultyWindow = 0x400;

    internal static DiscoveryResult Discover(ProcessMemory memory)
    {
        var diagnostics = new List<string>();
        ModuleImage? module = memory.ReadMainModule();
        if (module is null)
        {
            return Empty(Classification.Unknown, memory, true, "main module unreadable");
        }

        IReadOnlyList<nuint> osuVtables = Rtti.FindVptrs(module, "Osu", memory.PointerSize);
        IReadOnlyList<nuint> browserVtables = Rtti.FindVptrs(module, "OsuSongBrowser2", memory.PointerSize);
        IReadOnlyList<nuint> beatmapVtables = Rtti.FindVptrs(module, "OsuBeatmapStandard", memory.PointerSize);
        diagnostics.Add($"RTTI Osu={osuVtables.Count}, SongBrowser={browserVtables.Count}, BeatmapStandard={beatmapVtables.Count}");

        if (osuVtables.Count == 0 || browserVtables.Count == 0 || beatmapVtables.Count == 0)
        {
            return Empty(Classification.Unknown, memory, false, "required RTTI not found", diagnostics);
        }

        IReadOnlyList<MemoryRegion> regions = memory.EnumerateReadableRegions();
        ObjectScanResult objects = FindObjects(memory, regions, osuVtables, browserVtables, beatmapVtables);
        diagnostics.Add($"Objects Osu={objects.Osu.Count}, SongBrowser={objects.Browser.Count}, BeatmapStandard={objects.Beatmap.Count}");
        diagnostics.Add($"OsuObjects={string.Join(",", objects.Osu.Select(FormatAddress))}");
        diagnostics.Add($"SongBrowserObjects={string.Join(",", objects.Browser.Select(FormatAddress))}");
        diagnostics.Add($"BeatmapObjects={string.Join(",", objects.Beatmap.Select(FormatAddress))}");

        var chains = new List<Chain>();
        foreach (nuint osu in objects.Osu)
        {
            byte[]? osuBytes = memory.ReadExact(osu, ObjectWindow);
            if (osuBytes is null) continue;

            foreach (nuint browser in objects.Browser.Where(b => ContainsPointer(osuBytes, b, memory.PointerSize)))
            {
                byte[]? browserBytes = memory.ReadExact(browser, ObjectWindow);
                if (browserBytes is null || !ContainsPointer(browserBytes, osu, memory.PointerSize)) continue;
                diagnostics.Add($"Validated Osu<->SongBrowser 0x{osu:x}<->0x{browser:x}");
                foreach (nuint runtimeCandidate in objects.Beatmap)
                {
                    string browserOffsets = string.Join(",", EnumeratePointers(browserBytes, memory.PointerSize).Where(p => p.Pointer == runtimeCandidate).Select(p => $"0x{p.Offset:x}"));
                    byte[]? candidateBytes = memory.ReadExact(runtimeCandidate, RuntimeWindow);
                    string osuOffsets = candidateBytes is null ? "unreadable" : string.Join(",", EnumeratePointers(candidateBytes, memory.PointerSize).Where(p => p.Pointer == osu).Select(p => $"0x{p.Offset:x}"));
                    diagnostics.Add($"BeatmapCandidate 0x{runtimeCandidate:x} browserOffsets=[{browserOffsets}] osuOffsets=[{osuOffsets}]");
                }

                foreach (nuint runtime in objects.Beatmap.Where(b => ContainsPointer(browserBytes, b, memory.PointerSize)))
                {
                    byte[]? runtimeBytes = memory.ReadExact(runtime, RuntimeWindow);
                    if (runtimeBytes is null || !ContainsPointer(runtimeBytes, osu, memory.PointerSize)) continue;

                    foreach ((int offset, nuint pointer) in EnumeratePointers(runtimeBytes, memory.PointerSize))
                    {
                        if (pointer == 0 || pointer == osu || pointer == browser || pointer == runtime || !memory.IsReadable(pointer)) continue;
                        DifficultyCandidate? difficulty = TryReadDifficulty(memory, pointer);
                        if (difficulty is not null)
                        {
                            chains.Add(new(osu, browser, runtime, pointer, offset, difficulty));
                        }
                    }
                }
            }
        }

        Chain[] uniqueChains = chains
            .DistinctBy(c => (c.Osu, c.Browser, c.Runtime, c.Difficulty, c.DifficultyPointerOffset, c.Candidate.Path))
            .ToArray();
        diagnostics.Add($"ValidatedChains={uniqueChains.Length}");
        foreach (Chain chain in uniqueChains.Take(8))
        {
            diagnostics.Add($"Chain osu=0x{chain.Osu:x} browser=0x{chain.Browser:x} runtime=0x{chain.Runtime:x} diff=0x{chain.Difficulty:x} path={chain.Candidate.Path}");
        }

        Classification classification = uniqueChains.Length switch
        {
            0 when objects.Truncated => Classification.Unknown,
            0 => Classification.None,
            1 => Classification.Unique,
            _ => Classification.Ambiguous
        };

        Chain? selected = uniqueChains.Length == 1 ? uniqueChains[0] : null;
        return new(
            classification,
            selected?.Osu,
            selected?.Browser,
            selected?.Runtime,
            selected?.Difficulty,
            selected?.DifficultyPointerOffset,
            selected?.Candidate.PathDescriptorOffset,
            selected?.Candidate.Path,
            selected?.Candidate.DifficultyName,
            selected?.Candidate.BeatmapId,
            objects.Truncated,
            diagnostics,
            memory.ProcessId,
            memory.ProcessStartUtc);
    }

    internal static RefreshResult Refresh(ProcessMemory memory, DiscoveryResult discovery, int processId, DateTime startUtc)
    {
        if (discovery.RuntimeBeatmapObject is null || discovery.DifficultyPointerOffset is null ||
            processId != discovery.ProcessId || startUtc != discovery.ProcessStartUtc)
        {
            return new(Classification.Unknown, null);
        }

        if (!memory.TryReadPointer(discovery.RuntimeBeatmapObject.Value + (nuint)discovery.DifficultyPointerOffset.Value, out nuint difficulty))
        {
            return new(Classification.None, null);
        }

        DifficultyCandidate? candidate = TryReadDifficulty(memory, difficulty);
        return candidate is null
            ? new(Classification.None, null)
            : new(Classification.Unique, candidate.Path);
    }

    private static ObjectScanResult FindObjects(
        ProcessMemory memory,
        IReadOnlyList<MemoryRegion> regions,
        IReadOnlyList<nuint> osuVtables,
        IReadOnlyList<nuint> browserVtables,
        IReadOnlyList<nuint> beatmapVtables)
    {
        const long maxBytes = 3L * 1024 * 1024 * 1024;
        const int chunkSize = 1024 * 1024;
        var targets = new Dictionary<nuint, int>();
        foreach (nuint value in osuVtables) targets[value] = 1;
        foreach (nuint value in browserVtables) targets[value] = 2;
        foreach (nuint value in beatmapVtables) targets[value] = 3;
        var osu = new HashSet<nuint>();
        var browser = new HashSet<nuint>();
        var beatmap = new HashSet<nuint>();
        long considered = 0;
        bool truncated = false;

        foreach (MemoryRegion region in regions.Where(r => r.Type != Native.MemoryImage))
        {
            for (nuint offset = 0; offset < region.Size; offset += chunkSize)
            {
                int count = (int)Math.Min((nuint)chunkSize, region.Size - offset);
                considered += count;
                if (considered > maxBytes)
                {
                    truncated = true;
                    return new(osu, browser, beatmap, truncated);
                }

                byte[]? bytes = memory.Read(region.BaseAddress + offset, count);
                if (bytes is null)
                {
                    truncated = true;
                    continue;
                }

                for (int i = 0; i <= bytes.Length - memory.PointerSize; i += memory.PointerSize)
                {
                    nuint value = ReadPointer(bytes, i, memory.PointerSize);
                    if (!targets.TryGetValue(value, out int kind)) continue;
                    nuint address = region.BaseAddress + offset + (nuint)i;
                    if (kind == 1) osu.Add(address);
                    else if (kind == 2) browser.Add(address);
                    else beatmap.Add(address);
                }
            }
        }

        return new(osu, browser, beatmap, truncated);
    }

    private static DifficultyCandidate? TryReadDifficulty(ProcessMemory memory, nuint address)
    {
        byte[]? bytes = memory.ReadExact(address, DifficultyWindow);
        if (bytes is null) return null;
        var paths = new List<(int Offset, string Value)>();
        var strings = new List<(int Offset, string Value)>();

        int descriptorSize = memory.PointerSize == 4 ? 20 : 32;
        for (int offset = 0; offset <= bytes.Length - descriptorSize; offset += memory.PointerSize)
        {
            if (!TryReadUString(memory, bytes, offset, out string? value) || string.IsNullOrWhiteSpace(value)) continue;
            strings.Add((offset, value));
            if (value.EndsWith(".osu", StringComparison.OrdinalIgnoreCase) &&
                Path.IsPathFullyQualified(value) &&
                File.Exists(value))
            {
                paths.Add((offset, Path.GetFullPath(value)));
            }
        }

        var distinct = paths.DistinctBy(p => p.Value, StringComparer.OrdinalIgnoreCase).ToArray();
        if (distinct.Length != 1) return null;

        string path = distinct[0].Value;
        string? difficultyName = ReadDifficultyName(path);
        int? beatmapId = ReadBeatmapId(path);
        return new(path, distinct[0].Offset, difficultyName, beatmapId);
    }

    private static bool TryReadUString(ProcessMemory memory, byte[] owner, int offset, out string? value)
    {
        value = null;
        int length = BitConverter.ToInt32(owner, offset);
        int utf8Length = BitConverter.ToInt32(owner, offset + 4);
        byte asciiFlag = owner[offset + 8];
        if (length <= 0 || length > 32767 || utf8Length <= 0 || utf8Length > 131072 || asciiFlag > 1) return false;
        int pointerOffset = memory.PointerSize == 4 ? 12 : 16;
        nuint unicode = ReadPointer(owner, offset + pointerOffset, memory.PointerSize);
        nuint utf8 = ReadPointer(owner, offset + pointerOffset + memory.PointerSize, memory.PointerSize);
        if (unicode == 0 || utf8 == 0) return false;
        byte[]? unicodeBytes = memory.ReadExact(unicode, checked((length + 1) * 2));
        if (unicodeBytes is null || unicodeBytes[^1] != 0 || unicodeBytes[^2] != 0) return false;
        string decoded = Encoding.Unicode.GetString(unicodeBytes, 0, length * 2);
        if (decoded.Any(char.IsControl)) return false;
        value = decoded;
        return true;
    }

    private static string? ReadDifficultyName(string path)
    {
        foreach (string line in File.ReadLines(path))
        {
            if (line.StartsWith("Version:", StringComparison.OrdinalIgnoreCase)) return line[8..].Trim();
        }
        return null;
    }

    private static int? ReadBeatmapId(string path)
    {
        foreach (string line in File.ReadLines(path))
        {
            if (line.StartsWith("BeatmapID:", StringComparison.OrdinalIgnoreCase) && int.TryParse(line[10..].Trim(), out int id)) return id;
        }
        return null;
    }

    private static bool ContainsPointer(byte[] bytes, nuint pointer, int pointerSize) => EnumeratePointers(bytes, pointerSize).Any(p => p.Pointer == pointer);

    private static string FormatAddress(nuint address) => $"0x{address:x}";

    private static IEnumerable<(int Offset, nuint Pointer)> EnumeratePointers(byte[] bytes, int pointerSize)
    {
        for (int i = 0; i <= bytes.Length - pointerSize; i += pointerSize)
        {
            yield return (i, ReadPointer(bytes, i, pointerSize));
        }
    }

    private static nuint ReadPointer(byte[] bytes, int offset, int pointerSize) =>
        pointerSize == 4 ? BitConverter.ToUInt32(bytes, offset) : (nuint)BitConverter.ToUInt64(bytes, offset);

    private static DiscoveryResult Empty(Classification classification, ProcessMemory memory, bool truncated, string message, List<string>? diagnostics = null)
    {
        diagnostics ??= [];
        diagnostics.Add(message);
        return new(classification, null, null, null, null, null, null, null, null, null, truncated, diagnostics, memory.ProcessId, memory.ProcessStartUtc);
    }

    private sealed record DifficultyCandidate(string Path, int PathDescriptorOffset, string? DifficultyName, int? BeatmapId);
    private sealed record Chain(nuint Osu, nuint Browser, nuint Runtime, nuint Difficulty, int DifficultyPointerOffset, DifficultyCandidate Candidate);
    private sealed record ObjectScanResult(IReadOnlySet<nuint> Osu, IReadOnlySet<nuint> Browser, IReadOnlySet<nuint> Beatmap, bool Truncated);
}

internal static class Rtti
{
    internal static IReadOnlyList<nuint> FindVptrs(ModuleImage module, string className, int pointerSize)
    {
        byte[] name = Encoding.ASCII.GetBytes($"{className.Length}{className}\0");
        var vptrs = new HashSet<nuint>();
        foreach (int nameOffset in Find(module.Bytes, name))
        {
            nuint nameAddress = module.BaseAddress + (nuint)nameOffset;
            byte[] namePointer = PointerBytes(nameAddress, pointerSize);
            foreach (int nameRef in FindAligned(module.Bytes, namePointer, pointerSize))
            {
                if (nameRef < pointerSize) continue;
                nuint typeInfo = module.BaseAddress + (nuint)(nameRef - pointerSize);
                byte[] typeInfoPointer = PointerBytes(typeInfo, pointerSize);
                foreach (int typeRef in FindAligned(module.Bytes, typeInfoPointer, pointerSize))
                {
                    if (typeRef < pointerSize || ReadPointer(module.Bytes, typeRef - pointerSize, pointerSize) != 0) continue;
                    int firstFunction = typeRef + pointerSize;
                    if (firstFunction + pointerSize > module.Bytes.Length) continue;
                    nuint function = ReadPointer(module.Bytes, firstFunction, pointerSize);
                    if (function < module.BaseAddress || function >= module.BaseAddress + (nuint)module.Bytes.Length) continue;
                    vptrs.Add(module.BaseAddress + (nuint)firstFunction);
                }
            }
        }
        return vptrs.Order().ToArray();
    }

    private static byte[] PointerBytes(nuint value, int pointerSize) =>
        pointerSize == 4 ? BitConverter.GetBytes((uint)value) : BitConverter.GetBytes((ulong)value);

    private static nuint ReadPointer(byte[] bytes, int offset, int pointerSize) =>
        pointerSize == 4 ? BitConverter.ToUInt32(bytes, offset) : (nuint)BitConverter.ToUInt64(bytes, offset);

    private static IEnumerable<int> Find(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) yield return i;
        }
    }

    private static IEnumerable<int> FindAligned(byte[] haystack, byte[] needle, int alignment)
    {
        for (int i = 0; i <= haystack.Length - needle.Length; i += alignment)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) yield return i;
        }
    }
}

internal sealed class ProcessMemory : IDisposable
{
    private readonly SafeProcessHandle handle;
    private readonly List<MemoryRegion> regions;

    private ProcessMemory(Process process, SafeProcessHandle handle)
    {
        Process = process;
        this.handle = handle;
        ProcessId = process.Id;
        ProcessStartUtc = process.StartTime.ToUniversalTime();
        if (!Environment.Is64BitOperatingSystem)
        {
            PointerSize = 4;
        }
        else if (!Native.IsWow64Process(handle, out bool wow64))
        {
            throw new InvalidOperationException($"IsWow64Process failed: {Marshal.GetLastWin32Error()}");
        }
        else
        {
            PointerSize = wow64 ? 4 : 8;
        }
        regions = EnumerateReadableRegions().ToList();
    }

    internal Process Process { get; }
    internal int ProcessId { get; }
    internal DateTime ProcessStartUtc { get; }
    internal int PointerSize { get; }
    internal long ReadOperations { get; private set; }
    internal long BytesRead { get; private set; }

    internal static ProcessMemory Attach(Process process)
    {
        SafeProcessHandle handle = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, (uint)process.Id);
        if (handle.IsInvalid) throw new InvalidOperationException($"OpenProcess failed: {Marshal.GetLastWin32Error()}");
        return new(process, handle);
    }

    internal ModuleImage? ReadMainModule()
    {
        ProcessModule? module = Process.MainModule;
        if (module is null) return null;
        byte[]? bytes = ReadExact((nuint)module.BaseAddress, module.ModuleMemorySize);
        return bytes is null ? null : new((nuint)module.BaseAddress, bytes);
    }

    internal IReadOnlyList<MemoryRegion> EnumerateReadableRegions()
    {
        var result = new List<MemoryRegion>();
        nuint address = 0;
        nuint size = (nuint)Marshal.SizeOf<Native.MemoryBasicInformation>();
        while (true)
        {
            nuint queried = Native.VirtualQueryEx(handle, address, out Native.MemoryBasicInformation info, size);
            if (queried == 0) break;
            if (info.State == Native.MemoryCommit && (info.Protect & Native.PageGuard) == 0 && (info.Protect & 0xff) is 0x02 or 0x04 or 0x08 or 0x20 or 0x40 or 0x80)
            {
                result.Add(new(info.BaseAddress, info.RegionSize, info.Type));
            }
            nuint next = info.BaseAddress + info.RegionSize;
            if (next <= address) break;
            address = next;
        }
        return result;
    }

    internal bool IsReadable(nuint address) => regions.Any(r => r.Contains(address));

    internal byte[]? ReadExact(nuint address, int count)
    {
        byte[]? bytes = Read(address, count);
        return bytes?.Length == count ? bytes : null;
    }

    internal byte[]? Read(nuint address, int count)
    {
        if (address == 0 || count <= 0 || count > 16 * 1024 * 1024) return null;
        var bytes = new byte[count];
        ReadOperations++;
        bool ok = Native.ReadProcessMemory(handle, address, bytes, (nuint)count, out nuint read);
        BytesRead += (long)read;
        if (read == 0) return null;
        if ((int)read != bytes.Length) Array.Resize(ref bytes, (int)read);
        return ok || bytes.Length > 0 ? bytes : null;
    }

    internal bool TryReadPointer(nuint address, out nuint pointer)
    {
        byte[]? bytes = ReadExact(address, PointerSize);
        pointer = bytes is null ? 0 : PointerSize == 4 ? BitConverter.ToUInt32(bytes, 0) : (nuint)BitConverter.ToUInt64(bytes, 0);
        return pointer != 0;
    }

    public void Dispose() => handle.Dispose();
}

internal sealed record ModuleImage(nuint BaseAddress, byte[] Bytes);
internal readonly record struct MemoryRegion(nuint BaseAddress, nuint Size, uint Type)
{
    internal bool Contains(nuint address) => address >= BaseAddress && address - BaseAddress < Size;
}

internal static class Native
{
    internal const uint ProcessVmRead = 0x0010;
    internal const uint ProcessQueryInformation = 0x0400;
    internal const uint MemoryCommit = 0x1000;
    internal const uint MemoryImage = 0x1000000;
    internal const uint PageGuard = 0x100;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryBasicInformation
    {
        internal nuint BaseAddress;
        internal nuint AllocationBase;
        internal uint AllocationProtect;
        internal ushort PartitionId;
        internal nuint RegionSize;
        internal uint State;
        internal uint Protect;
        internal uint Type;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReadProcessMemory(SafeProcessHandle process, nuint address, [Out] byte[] buffer, nuint size, out nuint bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nuint VirtualQueryEx(SafeProcessHandle process, nuint address, out MemoryBasicInformation information, nuint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWow64Process(SafeProcessHandle process, [MarshalAs(UnmanagedType.Bool)] out bool wow64);
}
