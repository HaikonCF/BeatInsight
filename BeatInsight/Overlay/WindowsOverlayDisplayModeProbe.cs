using BeatInsight.Integration.Game;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BeatInsight.Overlay;

internal interface IOverlayDisplayModeProbe
{
    OverlayDisplayCompatibility Probe(GameStateSnapshot snapshot);
}

internal interface IOverlayForegroundProbe
{
    bool IsOsuForeground(GameStateSnapshot snapshot);
}

internal sealed class WindowsOverlayDisplayModeProbe :
    IOverlayDisplayModeProbe,
    IOverlayForegroundProbe
{
    private const uint MonitorDefaultToNearest = 2;
    private const uint GetOwner = 4;
    private const int WindowStyle = -16;
    private const int WindowExtendedStyle = -20;

    public OverlayDisplayCompatibility Probe(GameStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.ProcessId is not int processId)
        {
            return OverlayDisplayCompatibility.ClientAbsent;
        }

        try
        {
            OverlayWindowSnapshot? window = FindMainWindow(processId, out _);
            return OverlayDisplayModeClassifier.Classify(clientPresent: true, window);
        }
        catch
        {
            return OverlayDisplayCompatibility.Unknown;
        }
    }

    public bool IsOsuForeground(GameStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.ProcessId is not int processId)
        {
            return false;
        }

        try
        {
            bool processExists = IsProcessRunning(processId);
            OverlayWindowSnapshot? window = FindMainWindow(
                processId,
                out IntPtr osuWindowHandle);
            IntPtr foregroundWindow = GetForegroundWindow();
            int? foregroundProcessId = null;
            if (foregroundWindow != IntPtr.Zero)
            {
                _ = GetWindowThreadProcessId(
                    foregroundWindow,
                    out uint nativeForegroundProcessId);
                foregroundProcessId = unchecked((int)nativeForegroundProcessId);
            }

            return OverlayForegroundEvaluator.IsOsuForeground(
                processId,
                processExists,
                window is not null && osuWindowHandle != IntPtr.Zero,
                foregroundWindow,
                foregroundProcessId);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsProcessRunning(int processId)
    {
        using Process process = Process.GetProcessById(processId);
        return !process.HasExited;
    }

    private static OverlayWindowSnapshot? FindMainWindow(
        int processId,
        out IntPtr mainWindowHandle)
    {
        OverlayWindowSnapshot? largest = null;
        IntPtr largestHandle = IntPtr.Zero;
        EnumWindows((windowHandle, parameter) =>
        {
            _ = GetWindowThreadProcessId(windowHandle, out uint candidateProcessId);
            if (candidateProcessId != (uint)processId ||
                !IsWindowVisible(windowHandle) ||
                GetWindow(windowHandle, GetOwner) != IntPtr.Zero ||
                !GetWindowRect(windowHandle, out NativeRectangle bounds))
            {
                return true;
            }

            IntPtr monitorHandle = MonitorFromWindow(
                windowHandle,
                MonitorDefaultToNearest);
            var monitorInfo = new MonitorInfo
            {
                Size = (uint)Marshal.SizeOf<MonitorInfo>()
            };
            if (monitorHandle == IntPtr.Zero ||
                !GetMonitorInfo(monitorHandle, ref monitorInfo))
            {
                return true;
            }

            var candidate = new OverlayWindowSnapshot(
                unchecked((uint)GetWindowLong(windowHandle, WindowStyle)),
                unchecked((uint)GetWindowLong(windowHandle, WindowExtendedStyle)),
                IsIconic(windowHandle),
                ToRectangle(bounds),
                ToRectangle(monitorInfo.Monitor));
            if (largest is null || candidate.Bounds.Area > largest.Bounds.Area)
            {
                largest = candidate;
                largestHandle = windowHandle;
            }

            return true;
        }, IntPtr.Zero);

        mainWindowHandle = largestHandle;
        return largest;
    }

    private static OverlayWindowRectangle ToRectangle(NativeRectangle rectangle) =>
        new(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);

    private delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRectangle Monitor;
        public NativeRectangle WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(
        EnumWindowsCallback callback,
        IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr windowHandle,
        out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr windowHandle, uint command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(
        IntPtr windowHandle,
        out NativeRectangle rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr windowHandle, int index);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(
        IntPtr windowHandle,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(
        IntPtr monitorHandle,
        ref MonitorInfo monitorInfo);
}

internal static class OverlayForegroundEvaluator
{
    public static bool IsOsuForeground(
        int? osuProcessId,
        bool osuProcessExists,
        bool osuWindowFound,
        IntPtr foregroundWindow,
        int? foregroundProcessId) =>
        osuProcessId is int expectedProcessId &&
        osuProcessExists &&
        osuWindowFound &&
        foregroundWindow != IntPtr.Zero &&
        foregroundProcessId == expectedProcessId;
}
