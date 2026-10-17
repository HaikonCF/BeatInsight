using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace BeatInsight.Diagnostics.NativeLazer;

/// <summary>
/// EXPERIMENTAL / READ-ONLY. Minimal Win32 surface for the V3.6A research harness.
/// Deliberately contains no process-write, allocation, injection, or remote-thread API.
/// </summary>
internal static class NativeLazerInterop
{
    internal const uint StillActive = 259;
    internal const uint MemoryCommit = 0x1000;
    internal const uint MemoryPrivate = 0x20000;
    internal const uint MemoryMapped = 0x40000;
    internal const uint MemoryImage = 0x1000000;
    internal const uint PageGuard = 0x100;

    [Flags]
    internal enum ProcessAccess : uint
    {
        VmRead = 0x0010,
        QueryInformation = 0x0400
    }

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
    internal static extern SafeProcessHandle OpenProcess(
        ProcessAccess desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReadProcessMemory(
        SafeProcessHandle process,
        nuint baseAddress,
        [Out] byte[] buffer,
        nuint size,
        out nuint bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetExitCodeProcess(
        SafeProcessHandle process,
        out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nuint VirtualQueryEx(
        SafeProcessHandle process,
        nuint address,
        out MemoryBasicInformation information,
        nuint length);
}
