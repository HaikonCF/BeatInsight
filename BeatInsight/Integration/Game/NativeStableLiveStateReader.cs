using BeatInsight.Diagnostics.NativeStable;
using System.Diagnostics;

namespace BeatInsight.Integration.Game;

internal sealed record NativeStableLivePatternAddresses(
    nuint StatusValueAddress,
    nuint PlayTimeValueAddress,
    nuint BeatmapBaseMatchAddress);

internal interface INativeStableLivePatternDiscovery
{
    NativeStableLivePatternAddresses? Discover(NativeStableGameProcessContext context);
}

internal sealed class NativeStableLivePatternDiscovery : INativeStableLivePatternDiscovery
{
    internal const string StatusPatternText = "48 83 F8 04 73 1E";
    internal const string PlayTimePatternText = "5E 5F 5D C3 A1 ?? ?? ?? ?? 89 ?? 04";
    internal const string BeatmapBasePatternText = "F8 01 74 04 83 65";

    private static readonly IReadOnlyList<NativeStablePatternSearchRequest> Patterns =
    [
        new("status", BytePattern.Parse(StatusPatternText)),
        new("playTime", BytePattern.Parse(PlayTimePatternText)),
        new("beatmapBase", BytePattern.Parse(BeatmapBasePatternText))
    ];

    public NativeStableLivePatternAddresses? Discover(
        NativeStableGameProcessContext context)
    {
        if (context.Memory is not NativeStableProcessMemory processMemory)
        {
            return null;
        }

        IReadOnlyDictionary<string, IReadOnlyList<nuint>> matches =
            NativeStableProcessPatternSearch.FindAllMany(
                processMemory,
                context.Regions,
                Patterns);

        if (!TryGetUnique(matches, "status", out nuint statusMatch) ||
            !TryGetUnique(matches, "playTime", out nuint playTimeMatch) ||
            !TryGetUnique(matches, "beatmapBase", out nuint beatmapBaseMatch) ||
            statusMatch < 4 ||
            !context.Memory.TryReadPointer(statusMatch - 4, out nuint statusAddress) ||
            statusAddress == 0 ||
            !context.Memory.TryReadPointer(playTimeMatch + 5, out nuint playTimeAddress) ||
            playTimeAddress == 0 ||
            !TryReadInt32(context.Memory, statusAddress, out _) ||
            !TryReadInt32(context.Memory, playTimeAddress, out _))
        {
            return null;
        }

        return new(statusAddress, playTimeAddress, beatmapBaseMatch);
    }

    private static bool TryGetUnique(
        IReadOnlyDictionary<string, IReadOnlyList<nuint>> matches,
        string name,
        out nuint address)
    {
        if (matches.TryGetValue(name, out IReadOnlyList<nuint>? found) &&
            found.Count == 1)
        {
            address = found[0];
            return true;
        }

        address = 0;
        return false;
    }

    internal static bool TryReadInt32(
        INativeStableMemoryReader memory,
        nuint address,
        out int value)
    {
        NativeStableMemoryReadResult read = memory.ReadBytes(address, sizeof(int));
        if (!read.Complete || read.Data.Length != sizeof(int))
        {
            value = 0;
            return false;
        }

        value = BitConverter.ToInt32(read.Data, 0);
        return true;
    }
}

internal interface INativeStableLiveStateReader
{
    bool EnsureDiscovered(NativeStableGameProcessContext context);

    int? ReadStatus(NativeStableGameProcessContext context);

    int? ReadPlayTime(NativeStableGameProcessContext context);

    void Invalidate();

    /// <summary>
    /// V3.14-COM-KEY-05 : lecture seule des adresses déjà découvertes et de la génération de processus
    /// à laquelle elles appartiennent. Aucun scan, aucun effet de bord. Faux si rien n'est découvert.
    /// </summary>
    bool TryGetDiscovered(
        out NativeStableLivePatternAddresses? addresses,
        out int processId,
        out DateTimeOffset processStartIdentity)
    {
        addresses = null;
        processId = 0;
        processStartIdentity = default;
        return false;
    }
}

internal sealed class NativeStableLiveStateReader : INativeStableLiveStateReader
{
    private readonly object gate = new();
    private readonly INativeStableLivePatternDiscovery discovery;
    private readonly Func<long> getTimestamp;
    private readonly long timestampFrequency;
    private NativeStableLivePatternAddresses? addresses;
    private int? processId;
    private DateTimeOffset? processStartIdentity;
    private bool discoveryAttempted;
    private int failedDiscoveryAttempts;
    private long nextDiscoveryRetryTimestamp;

    internal NativeStableLiveStateReader()
        : this(new NativeStableLivePatternDiscovery(), Stopwatch.GetTimestamp, Stopwatch.Frequency)
    {
    }

    internal NativeStableLiveStateReader(
        INativeStableLivePatternDiscovery discovery,
        Func<long>? getTimestamp = null,
        long? timestampFrequency = null)
    {
        this.discovery = discovery;
        this.getTimestamp = getTimestamp ?? Stopwatch.GetTimestamp;
        this.timestampFrequency = timestampFrequency ?? Stopwatch.Frequency;
        if (this.timestampFrequency <= 0)
            throw new ArgumentOutOfRangeException(nameof(timestampFrequency));
    }

    public bool EnsureDiscovered(NativeStableGameProcessContext context)
    {
        lock (gate)
        {
            long now = getTimestamp();
            if (IsCurrentGeneration(context) && addresses is not null)
            {
                return true;
            }
            if (IsCurrentGeneration(context) && discoveryAttempted &&
                now < nextDiscoveryRetryTimestamp)
            {
                return false;
            }

            if (!IsCurrentGeneration(context))
            {
                Clear();
                processId = context.ProcessId;
                processStartIdentity = context.ProcessStartIdentity;
            }

            discoveryAttempted = true;
            NativeStableLivePatternAddresses? discovered = discovery.Discover(context);
            if (discovered is null)
            {
                failedDiscoveryAttempts++;
                nextDiscoveryRetryTimestamp = AddDelay(
                    getTimestamp(),
                    RetryDelayAfterFailure(failedDiscoveryAttempts));
                return false;
            }

            addresses = discovered;
            failedDiscoveryAttempts = 0;
            nextDiscoveryRetryTimestamp = 0;
            return true;
        }
    }

    public bool TryGetDiscovered(
        out NativeStableLivePatternAddresses? discovered,
        out int discoveredProcessId,
        out DateTimeOffset discoveredProcessStart)
    {
        lock (gate)
        {
            discovered = addresses;
            discoveredProcessId = processId ?? 0;
            discoveredProcessStart = processStartIdentity ?? default;
            return addresses is not null && processId is not null && processStartIdentity is not null;
        }
    }

    public int? ReadStatus(NativeStableGameProcessContext context) =>
        Read(context, static value => value.StatusValueAddress);

    public int? ReadPlayTime(NativeStableGameProcessContext context) =>
        Read(context, static value => value.PlayTimeValueAddress);

    public void Invalidate()
    {
        lock (gate)
        {
            Clear();
        }
    }

    private int? Read(
        NativeStableGameProcessContext context,
        Func<NativeStableLivePatternAddresses, nuint> selectAddress)
    {
        lock (gate)
        {
            if (!IsCurrentGeneration(context) || addresses is null)
            {
                return null;
            }

            return NativeStableLivePatternDiscovery.TryReadInt32(
                context.Memory,
                selectAddress(addresses),
                out int value)
                ? value
                : null;
        }
    }

    private bool IsCurrentGeneration(NativeStableGameProcessContext context) =>
        processId == context.ProcessId &&
        processStartIdentity == context.ProcessStartIdentity;

    private static TimeSpan RetryDelayAfterFailure(int attempts) => attempts switch
    {
        1 => TimeSpan.FromSeconds(2),
        2 => TimeSpan.FromSeconds(5),
        3 => TimeSpan.FromSeconds(15),
        _ => TimeSpan.FromSeconds(30)
    };

    private long AddDelay(long timestamp, TimeSpan delay) => checked(
        timestamp + (long)Math.Ceiling(delay.TotalSeconds * timestampFrequency));

    private void Clear()
    {
        addresses = null;
        processId = null;
        processStartIdentity = null;
        discoveryAttempted = false;
        failedDiscoveryAttempts = 0;
        nextDiscoveryRetryTimestamp = 0;
    }
}
