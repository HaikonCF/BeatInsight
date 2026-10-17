using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace BeatInsight.Diagnostics.NativeLazer;

internal sealed class NativeLazerGameplayClockProcessMemory(NativeLazerProcessMemory memory)
    : INativeLazerGameplayClockMemory, INativeLazerFastMemory
{
    public long BytesRead { get; private set; }
    public int ReadOperationCount { get; private set; }

    public bool IsReadable(ulong address, int count) => ReadBytes(address, count) is not null;
    public byte? ReadByte(ulong address) => ReadBytes(address, 1) is { } bytes ? bytes[0] : null;
    public int? ReadInt32(ulong address) => ReadBytes(address, 4) is { } bytes
        ? BinaryPrimitives.ReadInt32LittleEndian(bytes)
        : null;
    public double? ReadDouble(ulong address) => ReadBytes(address, 8) is { } bytes
        ? BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(bytes))
        : null;
    public ulong? ReadPointer(ulong address) => ReadBytes(address, 8) is { } bytes
        ? BinaryPrimitives.ReadUInt64LittleEndian(bytes)
        : null;
    public Guid? ReadGuid(ulong address) => ReadBytes(address, 16) is { } bytes ? new Guid(bytes) : null;

    public NativeLazerRemoteText? ReadString(ulong address, int maximumCharacters)
    {
        ulong? methodTable = ReadPointer(address);
        int? length = ReadInt32(address + 8);
        if (methodTable is not >= 0x10000 || length is null || length < 0 || length > maximumCharacters)
            return null;
        byte[]? characters = length == 0 ? [] : ReadBytes(address + 12, checked(length.Value * 2));
        return characters is null
            ? null
            : new(System.Text.Encoding.Unicode.GetString(characters), methodTable.Value);
    }

    private byte[]? ReadBytes(ulong address, int count)
    {
        ReadOperationCount++;
        NativeLazerMemoryRead read = memory.Read(address, count);
        BytesRead += read.Data.Length;
        return read.Complete ? read.Data : null;
    }
}

internal sealed record NativeLazerGameplayClockLiveInitialization(
    NativeLazerAttachStatus Status,
    string? FailureReason,
    NativeLazerGenerationIdentity Generation,
    NativeLazerGameplayClockStaticAudit? StaticAudit,
    double DurationMs,
    long BytesRead,
    int ReadOperations);

internal sealed class NativeLazerGameplayClockLiveSession : IDisposable
{
    private readonly NativeLazerProcessCandidate process;
    private readonly NativeLazerGenerationIdentity generation;
    private readonly NativeLazerProcessMemory processMemory;
    private readonly NativeLazerRootCacheEntry rootEntry;
    private readonly NativeLazerGameplayClockStaticAudit staticAudit;
    private readonly NativeLazerDiagnosticRootCache rootCache = new();
    private readonly NativeLazerFastRefresher fastRefresher = new();
    private readonly NativeLazerGameplayClockDiscovery discovery = new();
    private readonly NativeLazerScreenStackReader screenStackReader = new();
    private string? beatmapHash;
    private double beatmapDurationMs;
    private bool disposed;

    private NativeLazerGameplayClockLiveSession(
        NativeLazerProcessCandidate process,
        NativeLazerProcessMemory processMemory,
        NativeLazerRootCacheEntry rootEntry,
        NativeLazerGameplayClockStaticAudit staticAudit,
        NativeLazerGameplayClockLiveInitialization initialization)
    {
        this.process = process;
        generation = rootEntry.GenerationIdentity;
        this.processMemory = processMemory;
        this.rootEntry = rootEntry;
        this.staticAudit = staticAudit;
        Initialization = initialization;
        rootCache.TryStore(rootEntry, generation, processExists: true);
    }

    internal NativeLazerGameplayClockLiveInitialization Initialization { get; }

    internal NativeLazerProcessCandidate ProcessCandidate => process;

    internal static NativeLazerGameplayClockLiveSession? TryCreate(
        out NativeLazerGameplayClockLiveInitialization initialization)
    {
        NativeLazerDiscovery processDiscovery = NativeLazerProcessDiscovery.Discover();
        NativeLazerProcessCandidate? process = processDiscovery.UniqueCandidate;
        if (process is null)
        {
            initialization = new(processDiscovery.Status,
                $"Process discovery was {processDiscovery.Status}.", default, null, 0, 0, 0);
            return null;
        }

        var generation = new NativeLazerGenerationIdentity(process.ProcessId, process.StartTimeUtc);
        (NativeLazerProcessMemory? attached, NativeLazerAttachStatus attachStatus) =
            NativeLazerProcessMemory.TryAttach(process);
        if (attached is null)
        {
            initialization = new(attachStatus, $"Read-only attach failed: {attachStatus}.",
                generation, null, 0, 0, 0);
            return null;
        }

        try
        {
            string? installDirectory = Path.GetDirectoryName(process.ExecutablePath);
            if (string.IsNullOrWhiteSpace(installDirectory))
                throw new InvalidOperationException("osu!lazer install directory is unavailable.");
            NativeLazerGameplayClockStaticAudit audit =
                NativeLazerGameplayClockAssemblyInspector.Inspect(installDirectory);
            (NativeLazerFastLayout fastLayout, _) =
                NativeLazerFastLayoutDiscovery.Derive(installDirectory, process.ProductVersion);
            NativeLazerPersistentHintContext context = NativeLazerPersistentHintIdentity.Create(
                process.ProductVersion ?? string.Empty,
                process.ExecutablePath,
                installDirectory,
                fastLayout);
            var hintStore = new NativeLazerPersistentRootHintStore();
            NativeLazerPersistentRootHint? hint = hintStore.Load(out NativeLazerPersistentHintMissReason loadReason);
            var memory = new NativeLazerGameplayClockProcessMemory(attached);
            if (!NativeLazerPersistentHintResolver.TryResolve(
                    hint,
                    context,
                    generation,
                    fastLayout,
                    memory,
                    out NativeLazerRootCacheEntry? entry,
                    out _,
                    out NativeLazerPersistentHintMissReason resolveReason,
                    NativeLazerBlobPathResolver.Resolve))
            {
                string reason = resolveReason == NativeLazerPersistentHintMissReason.None
                    ? loadReason.ToString()
                    : resolveReason.ToString();
                initialization = new(attachStatus,
                    $"Existing persistent root hint was not accepted: {reason}. No heavy discovery was attempted.",
                    generation, audit, 0, memory.BytesRead, memory.ReadOperationCount);
                attached.Dispose();
                return null;
            }

            initialization = new(attachStatus, null, generation, audit, 0,
                memory.BytesRead, memory.ReadOperationCount);
            return new(process, attached, entry!, audit, initialization);
        }
        catch (Exception exception) when (exception is IOException or BadImageFormatException or
                                           FileLoadException or TypeLoadException or MissingMemberException or
                                           InvalidOperationException)
        {
            attached.Dispose();
            initialization = new(attachStatus,
                $"Gameplay clock initialization failed with {exception.GetType().Name}: {exception.Message}",
                generation, null, 0, 0, 0);
            return null;
        }
    }

    internal NativeLazerGameplayClockResult Sample()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!IsCurrentGeneration(generation))
            return Unavailable("Process generation changed before sampling.");

        var validationMemory = new NativeLazerGameplayClockProcessMemory(processMemory);
        NativeLazerFastRefreshResult beatmap = fastRefresher.Refresh(
            generation,
            rootEntry,
            rootCache,
            validationMemory,
            IsCurrentGeneration,
            hash => NativeLazerBlobPathResolver.Resolve(hash).Exists);
        if (beatmap.Outcome != NativeLazerFastOutcome.Available ||
            string.IsNullOrWhiteSpace(beatmap.BeatmapHash))
        {
            beatmapHash = null;
            beatmapDurationMs = 0;
            return Unavailable($"Beatmap precondition was {beatmap.Outcome}: {beatmap.FailureReason ?? "unavailable"}.");
        }

        if (!string.Equals(beatmapHash, beatmap.BeatmapHash, StringComparison.Ordinal))
        {
            (string? path, bool exists) = NativeLazerBlobPathResolver.Resolve(beatmap.BeatmapHash);
            double? duration = exists && path is not null
                ? NativeLazerGameplayClockBeatmapDuration.TryReadMilliseconds(path)
                : null;
            if (!duration.HasValue)
            {
                beatmapHash = null;
                beatmapDurationMs = 0;
                return Unavailable("Beatmap duration could not be determined conservatively.");
            }
            beatmapHash = beatmap.BeatmapHash;
            beatmapDurationMs = duration.Value;
        }

        var clockMemory = new NativeLazerGameplayClockProcessMemory(processMemory);
        return discovery.Discover(
            generation,
            rootEntry.GenerationIdentity,
            rootEntry.RootAddress,
            staticAudit.Layout,
            clockMemory,
            beatmapDurationMs,
            IsCurrentGeneration);
    }

    internal NativeLazerScreenStackSample SampleScreenStack()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!IsCurrentGeneration(generation))
        {
            return new()
            {
                Generation = generation,
                Outcome = NativeLazerScreenStackOutcome.Unknown,
                ObservedAtUtc = DateTimeOffset.UtcNow,
                FailureReason = "Process generation changed before ScreenStack sampling.",
                Metrics = new(0, 0, 0, 0)
            };
        }

        var memory = new NativeLazerGameplayClockProcessMemory(processMemory);
        return screenStackReader.Read(
            generation,
            rootEntry.GenerationIdentity,
            rootEntry.RootAddress,
            staticAudit.Layout,
            memory,
            IsCurrentGeneration);
    }

    private NativeLazerGameplayClockResult Unavailable(string reason) => new()
    {
        Generation = generation,
        Outcome = NativeLazerGameplayClockOutcome.Unknown,
        FailureReason = reason,
        Metrics = new(0, 0, 0, 0, 0)
    };

    private bool IsCurrentGeneration(NativeLazerGenerationIdentity expected)
    {
        try
        {
            using Process current = Process.GetProcessById(expected.ProcessId);
            return !current.HasExited && current.StartTime.ToUniversalTime() == expected.ProcessStartIdentity;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                                           System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        processMemory.Dispose();
    }
}

internal static class NativeLazerGameplayClockBeatmapDuration
{
    internal static double? TryReadMilliseconds(string path)
    {
        try
        {
            double sliderMultiplier = 1.4;
            string section = string.Empty;
            var timingPoints = new List<(double Time, double BeatLength, double SliderVelocity)>();
            double maximum = 0;
            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                    continue;
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    section = line;
                    continue;
                }
                if (section == "[Difficulty]" && line.StartsWith("SliderMultiplier:", StringComparison.Ordinal))
                {
                    double.TryParse(line["SliderMultiplier:".Length..], NumberStyles.Float,
                        CultureInfo.InvariantCulture, out sliderMultiplier);
                }
                else if (section == "[TimingPoints]")
                {
                    string[] parts = line.Split(',');
                    if (parts.Length >= 2 &&
                        double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double time) &&
                        double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double beatLength))
                    {
                        bool uninherited = parts.Length < 7 || parts[6] != "0";
                        double previousBeatLength = timingPoints.LastOrDefault(point => point.BeatLength > 0).BeatLength;
                        if (uninherited && beatLength > 0)
                            timingPoints.Add((time, beatLength, 1));
                        else if (!uninherited && beatLength < 0 && previousBeatLength > 0)
                            timingPoints.Add((time, previousBeatLength, Math.Clamp(-100 / beatLength, 0.1, 10)));
                    }
                }
                else if (section == "[HitObjects]")
                {
                    string[] parts = line.Split(',');
                    if (parts.Length < 5 ||
                        !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double start) ||
                        !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int type))
                        continue;
                    double end = start;
                    if ((type & 8) != 0 && parts.Length > 5)
                        double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out end);
                    else if ((type & 128) != 0 && parts.Length > 5)
                    {
                        string holdEnd = parts[5].Split(':')[0];
                        double.TryParse(holdEnd, NumberStyles.Float, CultureInfo.InvariantCulture, out end);
                    }
                    else if ((type & 2) != 0 && parts.Length > 7 && sliderMultiplier > 0 &&
                             int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int repeats) &&
                             double.TryParse(parts[7], NumberStyles.Float, CultureInfo.InvariantCulture, out double length))
                    {
                        (double _, double beatLength, double velocity) = timingPoints
                            .Where(point => point.Time <= start)
                            .DefaultIfEmpty((0, 500, 1))
                            .Last();
                        end = start + length * Math.Max(1, repeats) * beatLength /
                            (100 * sliderMultiplier * Math.Max(0.1, velocity));
                    }
                    maximum = Math.Max(maximum, Math.Max(start, end));
                }
            }
            return maximum > 0 && double.IsFinite(maximum) ? maximum : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           ArgumentException or OverflowException)
        {
            return null;
        }
    }
}
