using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace BeatInsight.Diagnostics.NativeStable;

/// <summary>
/// EXPERIMENTAL — V3 PROOF OF CONCEPT — NOT PRODUCTION INTEGRATION.
/// </summary>
internal static class NativeStableProcessDiscovery
{
    internal static NativeStableProcessDiscoveryResult Discover()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new([], NativeStableAttachmentStatus.UnsupportedPlatform);
        }

        var candidates = new List<NativeStableProcessCandidate>();

        foreach (Process process in Process.GetProcessesByName("osu!"))
        {
            using (process)
            {
                try
                {
                    using SafeProcessHandle handle = NativeStableInterop.OpenProcess(
                        NativeStableInterop.ProcessAccess.QueryLimitedInformation,
                        inheritHandle: false,
                        checked((uint)process.Id));

                    if (handle.IsInvalid)
                    {
                        candidates.Add(new(
                            process.Id,
                            null,
                            IsCandidateStable: false,
                            NativeStableAttachmentStatus.AccessDenied));
                        continue;
                    }

                    string? executablePath = TryGetExecutablePath(handle);
                    bool is32Bit = TryIs32Bit(handle, out bool value) && value;
                    bool expectedName = string.Equals(
                        Path.GetFileName(executablePath),
                        "osu!.exe",
                        StringComparison.OrdinalIgnoreCase);

                    candidates.Add(new(
                        process.Id,
                        executablePath,
                        IsCandidateStable: is32Bit && expectedName,
                        NativeStableAttachmentStatus.Discovered));
                }
                catch (InvalidOperationException)
                {
                    // Process disappeared between enumeration and inspection.
                }
                catch (ArgumentException)
                {
                    // Process disappeared between enumeration and inspection.
                }
            }
        }

        int plausibleCount = candidates.Count(candidate => candidate.IsCandidateStable);
        NativeStableAttachmentStatus status = plausibleCount switch
        {
            0 when candidates.Any(candidate =>
                candidate.AttachmentStatus == NativeStableAttachmentStatus.AccessDenied) =>
                NativeStableAttachmentStatus.AccessDenied,
            0 => NativeStableAttachmentStatus.NotFound,
            1 => NativeStableAttachmentStatus.Discovered,
            _ => NativeStableAttachmentStatus.Ambiguous
        };

        return new(candidates, status);
    }

    private static string? TryGetExecutablePath(SafeProcessHandle handle)
    {
        var buffer = new char[32768];
        uint length = checked((uint)buffer.Length);

        return NativeStableInterop.QueryFullProcessImageName(
            handle,
            flags: 0,
            buffer,
            ref length)
            ? new string(buffer, 0, checked((int)length))
            : null;
    }

    private static bool TryIs32Bit(
        SafeProcessHandle handle,
        out bool is32Bit)
    {
        if (!Environment.Is64BitOperatingSystem)
        {
            is32Bit = true;
            return true;
        }

        if (!NativeStableInterop.IsWow64Process(handle, out bool isWow64))
        {
            is32Bit = false;
            return false;
        }

        is32Bit = isWow64;
        return true;
    }
}

internal interface INativeStableMemoryReader
{
    bool Is32Bit { get; }

    NativeStableMemoryReadResult ReadBytes(nuint address, int count);

    bool TryReadPointer(nuint address, out nuint value);
}

internal sealed class NativeStableProcessMemory : INativeStableMemoryReader, IDisposable
{
    private const int MaximumSingleReadBytes = 16 * 1024 * 1024;
    private readonly SafeProcessHandle handle;
    private bool disposed;

    private NativeStableProcessMemory(
        int processId,
        SafeProcessHandle handle,
        bool is32Bit)
    {
        ProcessId = processId;
        this.handle = handle;
        Is32Bit = is32Bit;
    }

    internal int ProcessId { get; }

    public bool Is32Bit { get; }

    internal static NativeStableAttachResult TryAttach(
        NativeStableProcessCandidate candidate)
    {
        if (!candidate.IsCandidateStable)
        {
            return new(null, NativeStableAttachmentStatus.UnsupportedProcess);
        }

        SafeProcessHandle handle = NativeStableInterop.OpenProcess(
            NativeStableInterop.ProcessAccess.QueryInformation |
            NativeStableInterop.ProcessAccess.VmRead,
            inheritHandle: false,
            checked((uint)candidate.ProcessId));

        if (handle.IsInvalid)
        {
            handle.Dispose();
            return new(null, NativeStableAttachmentStatus.AccessDenied);
        }

        if (!NativeStableInterop.GetExitCodeProcess(handle, out uint exitCode) ||
            exitCode != NativeStableInterop.StillActive)
        {
            handle.Dispose();
            return new(null, NativeStableAttachmentStatus.ProcessLost);
        }

        return new(
            new NativeStableProcessMemory(candidate.ProcessId, handle, is32Bit: true),
            NativeStableAttachmentStatus.AttachedReadOnly);
    }

    internal bool IsAlive =>
        !disposed &&
        !handle.IsInvalid &&
        NativeStableInterop.GetExitCodeProcess(handle, out uint exitCode) &&
        exitCode == NativeStableInterop.StillActive;

    internal IReadOnlyList<NativeStableMemoryRegion> EnumerateReadableRegions()
    {
        var regions = new List<NativeStableMemoryRegion>();
        nuint address = 0;
        nuint maximumAddress = Is32Bit ? uint.MaxValue : nuint.MaxValue;
        nuint informationSize = checked((nuint)Marshal.SizeOf<NativeStableInterop.MemoryBasicInformation>());

        while (!disposed && address < maximumAddress)
        {
            nuint result = NativeStableInterop.VirtualQueryEx(
                handle,
                address,
                out NativeStableInterop.MemoryBasicInformation information,
                informationSize);

            if (result == 0)
            {
                break;
            }

            if (IsReadable(information) && information.RegionSize > 0)
            {
                regions.Add(new(
                    information.BaseAddress,
                    information.RegionSize,
                    information.State,
                    information.Protect,
                    information.Type,
                    information.AllocationBase));
            }

            nuint next = information.BaseAddress + information.RegionSize;
            if (next <= address)
            {
                break;
            }

            address = next;
        }

        return regions;
    }

    public NativeStableMemoryReadResult ReadBytes(nuint address, int count)
    {
        if (disposed || address == 0 || count <= 0 || count > MaximumSingleReadBytes)
        {
            return NativeStableMemoryReadResult.Failed;
        }

        var buffer = new byte[count];
        bool success = NativeStableInterop.ReadProcessMemory(
            handle,
            address,
            buffer,
            checked((nuint)count),
            out nuint bytesRead);

        int actualCount = bytesRead > checked((nuint)count)
            ? count
            : checked((int)bytesRead);

        if (actualCount == 0)
        {
            return NativeStableMemoryReadResult.Failed;
        }

        if (actualCount != buffer.Length)
        {
            Array.Resize(ref buffer, actualCount);
        }

        return new(buffer, success && actualCount == count, actualCount);
    }

    internal bool TryReadUInt32(nuint address, out uint value)
    {
        NativeStableMemoryReadResult read = ReadBytes(address, sizeof(uint));
        if (!read.Complete)
        {
            value = 0;
            return false;
        }

        value = BitConverter.ToUInt32(read.Data, 0);
        return true;
    }

    public bool TryReadPointer(nuint address, out nuint value)
    {
        if (Is32Bit && TryReadUInt32(address, out uint pointer32))
        {
            value = pointer32;
            return pointer32 != 0;
        }

        NativeStableMemoryReadResult read = ReadBytes(address, IntPtr.Size);
        if (!read.Complete)
        {
            value = 0;
            return false;
        }

        value = checked((nuint)BitConverter.ToUInt64(read.Data, 0));
        return value != 0;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        handle.Dispose();
    }

    private static bool IsReadable(NativeStableInterop.MemoryBasicInformation information)
    {
        if (information.State != NativeStableInterop.MemoryCommit ||
            (information.Protect & NativeStableInterop.PageGuard) != 0)
        {
            return false;
        }

        uint baseProtection = information.Protect & 0xff;
        return baseProtection is 0x02 or 0x04 or 0x08 or 0x20 or 0x40 or 0x80;
    }
}

internal enum NativeStableAttachmentStatus
{
    NotFound,
    Discovered,
    Ambiguous,
    AttachedReadOnly,
    AccessDenied,
    ProcessLost,
    UnsupportedProcess,
    UnsupportedPlatform
}

internal sealed record NativeStableProcessCandidate(
    int ProcessId,
    string? ExecutablePath,
    bool IsCandidateStable,
    NativeStableAttachmentStatus AttachmentStatus);

internal sealed record NativeStableProcessDiscoveryResult(
    IReadOnlyList<NativeStableProcessCandidate> Candidates,
    NativeStableAttachmentStatus Status)
{
    internal NativeStableProcessCandidate? UniqueCandidate =>
        Status == NativeStableAttachmentStatus.Discovered
            ? Candidates.Single(candidate => candidate.IsCandidateStable)
            : null;
}

internal sealed record NativeStableAttachResult(
    NativeStableProcessMemory? Memory,
    NativeStableAttachmentStatus Status);

internal readonly record struct NativeStableMemoryRegion(
    nuint BaseAddress,
    nuint Size,
    uint State,
    uint Protect,
    uint Type,
    nuint AllocationBase = 0)
{
    internal bool Contains(nuint address)
    {
        if (address < BaseAddress)
        {
            return false;
        }

        return address - BaseAddress < Size;
    }
}

internal sealed record NativeStableMemoryReadResult(
    byte[] Data,
    bool Complete,
    int BytesRead)
{
    internal static NativeStableMemoryReadResult Failed { get; } =
        new([], Complete: false, BytesRead: 0);
}
