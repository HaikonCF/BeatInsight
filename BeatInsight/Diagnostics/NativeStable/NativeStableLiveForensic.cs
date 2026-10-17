using System.Diagnostics;
using System.Globalization;

namespace BeatInsight.Diagnostics.NativeStable;

/// <summary>
/// Diagnostic-only live reader for validating externally supplied osu!stable
/// status, play-time and current-beatmap signatures. No result is published to
/// the production game-state pipeline.
/// </summary>
public static class NativeStableLiveForensicHarness
{
    public static int RunFromConsole()
    {
        using var session = new NativeStableLiveForensicSession();
        using var stats = new NativeStableGameplayStatsForensicSession();
        session.Discover();
        Console.WriteLine("COMMANDS: REDISCOVER | SAMPLE <label> [count] [intervalMs] | PERF [iterations] | STATS_CONTAINER_DISCOVER [candidate...] | STATS_CONTAINER_SAMPLE <label> [count] [intervalMs] | STATS_CONTAINER_PERF [iterations] | STATS_DISCOVER | STATS_SELECT <index> | STATS_ROOTS | STATS_FOLLOW <address> | STATS_OWNER <address> | STATS_INSPECT <address> | STATS_SAMPLE <label> [count] [intervalMs] | STATS_PERF [iterations] | EXIT");

        string? line;
        while ((line = Console.ReadLine()) is not null)
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
                continue;

            switch (parts[0].ToUpperInvariant())
            {
                case "REDISCOVER":
                case "DISCOVER":
                    session.Discover();
                    break;
                case "SAMPLE":
                    string label = parts.Length > 1 ? parts[1] : "sample";
                    int count = ParsePositive(parts, 2, 1, 200);
                    int interval = ParsePositive(parts, 3, 0, 10_000);
                    session.Sample(label, count, interval);
                    break;
                case "PERF":
                    session.MeasurePerformance(ParsePositive(parts, 1, 500, 100_000));
                    break;
                case "STATS_DISCOVER":
                    stats.Discover();
                    break;
                case "STATS_CONTAINER_DISCOVER":
                    stats.DiscoverContainer(ParseAddresses(parts, 1));
                    break;
                case "STATS_CONTAINER_SAMPLE":
                    stats.SampleContainer(
                        parts.Length > 1 ? parts[1] : "sample",
                        ParsePositive(parts, 2, 1, 200),
                        ParsePositive(parts, 3, 0, 10_000));
                    break;
                case "STATS_CONTAINER_PERF":
                    stats.MeasureContainerPerformance(ParsePositive(parts, 1, 500, 100_000));
                    break;
                case "STATS_SELECT":
                    stats.Select(ParsePositive(parts, 1, 0, 10_000));
                    break;
                case "STATS_ROOTS":
                    stats.FindRulesetRoots();
                    break;
                case "STATS_FOLLOW":
                    stats.FollowRuleset(ParseAddress(parts, 1));
                    break;
                case "STATS_OWNER":
                    stats.InspectRulesetOwnership(ParseAddress(parts, 1));
                    break;
                case "STATS_INSPECT":
                    stats.InspectAddress(ParseAddress(parts, 1));
                    break;
                case "STATS_SAMPLE":
                    stats.Sample(
                        parts.Length > 1 ? parts[1] : "sample",
                        ParsePositive(parts, 2, 1, 200),
                        ParsePositive(parts, 3, 0, 10_000));
                    break;
                case "STATS_PERF":
                    stats.MeasurePerformance(ParsePositive(parts, 1, 500, 100_000));
                    break;
                case "EXIT":
                case "QUIT":
                    return 0;
                default:
                    Console.WriteLine("ERROR unknown-command");
                    break;
            }
        }

        return 0;
    }

    private static int ParsePositive(string[] parts, int index, int fallback, int maximum) =>
        index < parts.Length && int.TryParse(parts[index], NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int value) && value >= 0
            ? Math.Min(value, maximum)
            : fallback;

    private static nuint ParseAddress(string[] parts, int index)
    {
        if (index >= parts.Length)
            return 0;
        string value = parts[index].StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? parts[index][2..]
            : parts[index];
        return nuint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture,
            out nuint address) ? address : 0;
    }

    private static IReadOnlyList<nuint> ParseAddresses(string[] parts, int startIndex) =>
        Enumerable.Range(startIndex, Math.Max(0, parts.Length - startIndex))
            .Select(index => ParseAddress(parts, index))
            .Where(address => address != 0)
            .Distinct()
            .ToArray();
}

internal sealed class NativeStableLiveForensicSession : IDisposable
{
    private const string StatusPattern = "48 83 F8 04 73 1E";
    private const string PlayTimePattern = "5E 5F 5D C3 A1 ?? ?? ?? ?? 89 ?? 04";
    private const string BeatmapBasePattern = "F8 01 74 04 83 65";
    private const nuint BeatmapIdOffset = 0xc8;
    private const nuint BeatmapSetIdOffset = 0xcc;
    private const nuint Md5Offset = 0x6c;
    private const nuint FolderOffset = 0x78;
    private const nuint FilenameOffset = 0x90;
    private const nuint DifficultyOffset = 0xac;

    private NativeStableProcessMemory? memory;
    private int? processId;
    private DateTimeOffset? processStartIdentity;
    private nuint? statusMatch;
    private nuint? playTimeMatch;
    private nuint? beatmapBaseMatch;
    private IReadOnlyDictionary<string, IReadOnlyList<nuint>> matches =
        new Dictionary<string, IReadOnlyList<nuint>>();
    private TimeSpan discoveryTime;

    internal void Discover()
    {
        Reset();
        Stopwatch timer = Stopwatch.StartNew();
        NativeStableProcessDiscoveryResult discovery = NativeStableProcessDiscovery.Discover();
        NativeStableProcessCandidate? candidate = discovery.UniqueCandidate;
        if (candidate is null)
        {
            timer.Stop();
            discoveryTime = timer.Elapsed;
            Console.WriteLine($"DISCOVERY unavailable status={discovery.Status} elapsedMs={Ms(discoveryTime)}");
            return;
        }

        NativeStableAttachResult attachment = NativeStableProcessMemory.TryAttach(candidate);
        if (attachment.Memory is null)
        {
            timer.Stop();
            discoveryTime = timer.Elapsed;
            Console.WriteLine($"DISCOVERY unavailable status={attachment.Status} pid={candidate.ProcessId} elapsedMs={Ms(discoveryTime)}");
            return;
        }

        memory = attachment.Memory;
        processId = candidate.ProcessId;
        try
        {
            using Process process = Process.GetProcessById(candidate.ProcessId);
            processStartIdentity = process.StartTime.ToUniversalTime();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            timer.Stop();
            discoveryTime = timer.Elapsed;
            Console.WriteLine($"DISCOVERY unavailable status=ProcessLost pid={candidate.ProcessId} elapsedMs={Ms(discoveryTime)}");
            Reset();
            return;
        }

        IReadOnlyList<NativeStableMemoryRegion> regions = memory.EnumerateReadableRegions();
        matches = NativeStableProcessPatternSearch.FindAllMany(memory, regions,
        [
            new("status", BytePattern.Parse(StatusPattern)),
            new("playTime", BytePattern.Parse(PlayTimePattern)),
            new("beatmapBase", BytePattern.Parse(BeatmapBasePattern))
        ]);
        timer.Stop();
        discoveryTime = timer.Elapsed;

        statusMatch = Unique("status");
        playTimeMatch = Unique("playTime");
        beatmapBaseMatch = Unique("beatmapBase");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"DISCOVERY pid={processId} identity={Identity()} elapsedMs={Ms(discoveryTime)} regions={regions.Count} " +
            $"statusMatches={matches["status"].Count} statusMatch={Hex(statusMatch)} " +
            $"playTimeMatches={matches["playTime"].Count} playTimeMatch={Hex(playTimeMatch)} " +
            $"beatmapMatches={matches["beatmapBase"].Count} beatmapMatch={Hex(beatmapBaseMatch)}"));
        Sample("discovery", 1, 0);
    }

    internal void Sample(string label, int count, int intervalMs)
    {
        for (int index = 0; index < count; index++)
        {
            if (memory is null || !memory.IsAlive)
            {
                Console.WriteLine($"SAMPLE label={label} index={index} available=false reason=ProcessLost identity={Identity()}");
                continue;
            }

            NativeStableLiveStatus status = ReadStatus();
            NativeStableLivePlayTime playTime = ReadPlayTime();
            NativeStableLiveBeatmap beatmap = ReadBeatmap();
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"SAMPLE label={label} index={index} utc={DateTimeOffset.UtcNow:O} pid={processId} identity={Identity()} " +
                $"status={Value(status.Value)} statusDerived={Hex(status.DerivedAddress)} statusPtr={Hex(status.Pointer)} statusFinal={Hex(status.FinalAddress)} " +
                $"playTime={Value(playTime.Value)} playDerived={Hex(playTime.DerivedAddress)} playPtr={Hex(playTime.IntermediatePointer)} " +
                $"beatmapObject={Hex(beatmap.ObjectAddress)} beatmapId={Value(beatmap.BeatmapId)} beatmapSetId={Value(beatmap.BeatmapSetId)} " +
                $"md5={Text(beatmap.Md5)} folder={Quote(beatmap.Folder)} filename={Quote(beatmap.Filename)} difficulty={Quote(beatmap.Difficulty)}"));

            if (index + 1 < count && intervalMs > 0)
                Thread.Sleep(intervalMs);
        }
    }

    internal void MeasurePerformance(int iterations)
    {
        if (memory is null || !memory.IsAlive || statusMatch is null || playTimeMatch is null || beatmapBaseMatch is null)
        {
            Console.WriteLine("PERF unavailable");
            return;
        }

        Console.WriteLine($"PERF discoveryMs={Ms(discoveryTime)} iterations={iterations} " +
            $"status={Measure(iterations, ReadStatus)} playTime={Measure(iterations, ReadPlayTime)} " +
            $"beatmap={Measure(iterations, ReadBeatmap)}");
    }

    private NativeStableLiveStatus ReadStatus()
    {
        if (memory is null || statusMatch is not nuint match || match < 4)
            return default;
        nuint derived = match - 4;
        nuint pointer = 0;
        if (!memory.TryReadPointer(derived, out pointer) ||
            !TryReadInt32(pointer, out int value))
            return new(derived, pointer, pointer, null);
        return new(derived, pointer, pointer, value);
    }

    private NativeStableLivePlayTime ReadPlayTime()
    {
        if (memory is null || playTimeMatch is not nuint match)
            return default;
        nuint derived = match + 5;
        if (!memory.TryReadPointer(derived, out nuint pointer) ||
            !TryReadInt32(pointer, out int value))
            return new(derived, pointer, null);
        return new(derived, pointer, value);
    }

    private NativeStableLiveBeatmap ReadBeatmap()
    {
        if (memory is null || beatmapBaseMatch is not nuint match || match < 0xc)
            return default;
        nuint derived = match - 0xc;
        nuint pointer = 0;
        nuint objectAddress = 0;
        if (!memory.TryReadPointer(derived, out pointer) ||
            !memory.TryReadPointer(pointer, out objectAddress))
            return new(derived, pointer, objectAddress, null, null, null, null, null, null);

        int? beatmapId = TryReadInt32(objectAddress + BeatmapIdOffset, out int id) ? id : null;
        int? beatmapSetId = TryReadInt32(objectAddress + BeatmapSetIdOffset, out int setId) ? setId : null;
        return new(derived, pointer, objectAddress, beatmapId, beatmapSetId,
            ReadStringField(objectAddress + Md5Offset),
            ReadStringField(objectAddress + FolderOffset),
            ReadStringField(objectAddress + FilenameOffset),
            ReadStringField(objectAddress + DifficultyOffset));
    }

    private string? ReadStringField(nuint address) =>
        memory is not null && memory.TryReadPointer(address, out nuint pointer) &&
        NativeStableManagedStringReader.TryRead(memory, pointer, out string value)
            ? value
            : null;

    private bool TryReadInt32(nuint address, out int value)
    {
        NativeStableMemoryReadResult? read = memory?.ReadBytes(address, sizeof(int));
        if (read is null || !read.Complete || read.Data.Length != sizeof(int))
        {
            value = 0;
            return false;
        }
        value = BitConverter.ToInt32(read.Data, 0);
        return true;
    }

    private nuint? Unique(string key) => matches.TryGetValue(key, out IReadOnlyList<nuint>? found) && found.Count == 1
        ? found[0]
        : null;

    private string Identity() => processId is int pid && processStartIdentity is DateTimeOffset start
        ? string.Create(CultureInfo.InvariantCulture, $"{pid}:{start.UtcDateTime:O}")
        : "N/A";

    private static string Measure<T>(int iterations, Func<T> read)
    {
        var samples = new double[iterations];
        for (int index = 0; index < iterations; index++)
        {
            long start = Stopwatch.GetTimestamp();
            _ = read();
            samples[index] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        Array.Sort(samples);
        double average = samples.Average();
        double median = samples[samples.Length / 2];
        double p95 = samples[Math.Min(samples.Length - 1, (int)Math.Ceiling(samples.Length * .95) - 1)];
        return string.Create(CultureInfo.InvariantCulture, $"avgMs={average:F6},medianMs={median:F6},p95Ms={p95:F6}");
    }

    private static string Ms(TimeSpan value) => value.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture);
    private static string Hex(nuint? value) => value is null or 0 ? "N/A" : $"0x{value.Value:X}";
    private static string Value(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "N/A";
    private static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? "N/A" : value;
    private static string Quote(string? value) => string.IsNullOrWhiteSpace(value) ? "N/A" : $"\"{value.Replace("\"", "'")}\"";

    private void Reset()
    {
        memory?.Dispose();
        memory = null;
        processId = null;
        processStartIdentity = null;
        statusMatch = null;
        playTimeMatch = null;
        beatmapBaseMatch = null;
        matches = new Dictionary<string, IReadOnlyList<nuint>>();
        discoveryTime = TimeSpan.Zero;
    }

    public void Dispose() => Reset();
}

internal readonly record struct NativeStableLiveStatus(
    nuint? DerivedAddress,
    nuint? Pointer,
    nuint? FinalAddress,
    int? Value);

internal readonly record struct NativeStableLivePlayTime(
    nuint? DerivedAddress,
    nuint? IntermediatePointer,
    int? Value);

internal readonly record struct NativeStableLiveBeatmap(
    nuint? DerivedAddress,
    nuint? IntermediatePointer,
    nuint? ObjectAddress,
    int? BeatmapId,
    int? BeatmapSetId,
    string? Md5,
    string? Folder,
    string? Filename,
    string? Difficulty);
