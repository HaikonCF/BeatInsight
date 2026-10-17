using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace BeatInsight.Diagnostics.NativeLazer;

internal enum NativeLazerAttachStatus
{
    None,
    Unique,
    Ambiguous,
    AccessDenied,
    ProcessLost,
    UnsupportedPlatform
}

internal sealed record NativeLazerProcessCandidate(
    int ProcessId,
    DateTime StartTimeUtc,
    string ExecutablePath,
    string? ProductVersion);

internal sealed record NativeLazerDiscovery(
    NativeLazerAttachStatus Status,
    IReadOnlyList<NativeLazerProcessCandidate> Candidates)
{
    internal NativeLazerProcessCandidate? UniqueCandidate =>
        Status == NativeLazerAttachStatus.Unique ? Candidates.Single() : null;
}

internal static class NativeLazerProcessDiscovery
{
    internal static NativeLazerDiscovery Discover()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new(NativeLazerAttachStatus.UnsupportedPlatform, []);
        }

        var candidates = new List<NativeLazerProcessCandidate>();

        foreach (Process process in Process.GetProcessesByName("osu!"))
        {
            using (process)
            {
                try
                {
                    string? path = process.MainModule?.FileName;
                    string? version = process.MainModule?.FileVersionInfo.ProductVersion;

                    if (path is null || !IsLazer(path, version))
                    {
                        continue;
                    }

                    candidates.Add(new(
                        process.Id,
                        process.StartTime.ToUniversalTime(),
                        Path.GetFullPath(path),
                        version));
                }
                catch (InvalidOperationException)
                {
                    // Process disappeared during the read-only discovery pass.
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // An inaccessible process is not a usable candidate.
                }
            }
        }

        return new(candidates.Count switch
        {
            0 => NativeLazerAttachStatus.None,
            1 => NativeLazerAttachStatus.Unique,
            _ => NativeLazerAttachStatus.Ambiguous
        }, candidates);
    }

    private static bool IsLazer(string executablePath, string? productVersion)
    {
        string normalized = executablePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        string lazerSegment = $"{Path.DirectorySeparatorChar}osulazer{Path.DirectorySeparatorChar}";

        return normalized.Contains(lazerSegment, StringComparison.OrdinalIgnoreCase) ||
               productVersion?.Contains("-lazer", StringComparison.OrdinalIgnoreCase) == true;
    }
}

internal sealed class NativeLazerProcessMemory : IDisposable
{
    private const int maximumSingleReadBytes = 16 * 1024 * 1024;
    private readonly SafeProcessHandle handle;
    private bool disposed;

    private NativeLazerProcessMemory(NativeLazerProcessCandidate process, SafeProcessHandle handle)
    {
        Process = process;
        this.handle = handle;
    }

    internal NativeLazerProcessCandidate Process { get; }

    internal static (NativeLazerProcessMemory? Memory, NativeLazerAttachStatus Status) TryAttach(
        NativeLazerProcessCandidate process)
    {
        SafeProcessHandle handle = NativeLazerInterop.OpenProcess(
            NativeLazerInterop.ProcessAccess.QueryInformation |
            NativeLazerInterop.ProcessAccess.VmRead,
            inheritHandle: false,
            checked((uint)process.ProcessId));

        if (handle.IsInvalid)
        {
            handle.Dispose();
            return (null, NativeLazerAttachStatus.AccessDenied);
        }

        try
        {
            using Process current = System.Diagnostics.Process.GetProcessById(process.ProcessId);
            if (current.StartTime.ToUniversalTime() != process.StartTimeUtc)
            {
                handle.Dispose();
                return (null, NativeLazerAttachStatus.ProcessLost);
            }
        }
        catch (ArgumentException)
        {
            handle.Dispose();
            return (null, NativeLazerAttachStatus.ProcessLost);
        }
        catch (InvalidOperationException)
        {
            handle.Dispose();
            return (null, NativeLazerAttachStatus.ProcessLost);
        }

        return (new NativeLazerProcessMemory(process, handle), NativeLazerAttachStatus.Unique);
    }

    internal IReadOnlyList<NativeLazerMemoryRegion> EnumerateReadableRegions()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        var regions = new List<NativeLazerMemoryRegion>();
        nuint address = 0;
        nuint informationSize = checked((nuint)Marshal.SizeOf<NativeLazerInterop.MemoryBasicInformation>());

        while (true)
        {
            nuint result = NativeLazerInterop.VirtualQueryEx(
                handle,
                address,
                out NativeLazerInterop.MemoryBasicInformation information,
                informationSize);

            if (result == 0)
            {
                break;
            }

            if (IsReadable(information) && information.RegionSize > 0)
            {
                regions.Add(new(
                    checked((ulong)information.BaseAddress),
                    checked((ulong)information.RegionSize),
                    checked((ulong)information.AllocationBase),
                    information.Protect,
                    information.Type));
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

    internal NativeLazerMemoryRead Read(ulong address, int count)
    {
        if (disposed || address == 0 || count <= 0 || count > maximumSingleReadBytes)
        {
            return NativeLazerMemoryRead.Failed;
        }

        var buffer = new byte[count];
        bool complete = NativeLazerInterop.ReadProcessMemory(
            handle,
            checked((nuint)address),
            buffer,
            checked((nuint)count),
            out nuint bytesRead);

        int actual = bytesRead > checked((nuint)count) ? count : checked((int)bytesRead);
        if (actual == 0)
        {
            return NativeLazerMemoryRead.Failed;
        }

        if (actual != buffer.Length)
        {
            Array.Resize(ref buffer, actual);
        }

        return new(buffer, complete && actual == count);
    }

    internal bool IsCurrentGeneration(NativeLazerGenerationIdentity expected)
    {
        if (disposed || handle.IsInvalid ||
            Process.ProcessId != expected.ProcessId ||
            Process.StartTimeUtc != expected.ProcessStartIdentity)
        {
            return false;
        }

        return NativeLazerInterop.GetExitCodeProcess(handle, out uint exitCode) &&
               exitCode == NativeLazerInterop.StillActive;
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

    private static bool IsReadable(NativeLazerInterop.MemoryBasicInformation information)
    {
        if (information.State != NativeLazerInterop.MemoryCommit ||
            (information.Protect & NativeLazerInterop.PageGuard) != 0)
        {
            return false;
        }

        uint baseProtection = information.Protect & 0xff;
        return baseProtection is 0x02 or 0x04 or 0x08 or 0x20 or 0x40 or 0x80;
    }
}
