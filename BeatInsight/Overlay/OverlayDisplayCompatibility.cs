namespace BeatInsight.Overlay;

public enum OverlayDisplayMode
{
    ClientAbsent,
    Windowed,
    Borderless,
    FullscreenExclusive,
    Unknown
}

public sealed record OverlayDisplayCompatibility(
    OverlayDisplayMode DisplayMode,
    bool OverlaySupported,
    bool OverlayVisible,
    string StatusText)
{
    public static OverlayDisplayCompatibility ClientAbsent { get; } =
        new(OverlayDisplayMode.ClientAbsent, false, false, string.Empty);

    public static OverlayDisplayCompatibility Unknown { get; } =
        new(
            OverlayDisplayMode.Unknown,
            false,
            false,
            "Overlay visibility could not be verified.");

    public static OverlayDisplayCompatibility Supported(OverlayDisplayMode displayMode)
    {
        if (displayMode is not (OverlayDisplayMode.Windowed or OverlayDisplayMode.Borderless))
        {
            throw new ArgumentOutOfRangeException(nameof(displayMode));
        }

        return new(displayMode, true, true, string.Empty);
    }

    public static OverlayDisplayCompatibility FullscreenExclusive { get; } =
        new(
            OverlayDisplayMode.FullscreenExclusive,
            false,
            false,
            "BeatInsight overlays require Borderless Fullscreen.");
}

internal readonly record struct OverlayWindowRectangle(
    int Left,
    int Top,
    int Right,
    int Bottom)
{
    public long Area =>
        Math.Max(0L, (long)Right - Left) * Math.Max(0L, (long)Bottom - Top);
}

internal sealed record OverlayWindowSnapshot(
    long Style,
    long ExtendedStyle,
    bool IsMinimized,
    OverlayWindowRectangle Bounds,
    OverlayWindowRectangle MonitorBounds);

internal static class OverlayDisplayModeClassifier
{
    internal const long WindowCaption = 0x00C00000L;
    internal const long ThickFrame = 0x00040000L;
    internal const long ExtendedWindowEdge = 0x00000100L;
    internal const long ExtendedTopMost = 0x00000008L;
    private const int FullscreenTolerancePixels = 2;

    public static OverlayDisplayCompatibility Classify(
        bool clientPresent,
        OverlayWindowSnapshot? window)
    {
        if (!clientPresent)
        {
            return OverlayDisplayCompatibility.ClientAbsent;
        }

        if (window is null)
        {
            return OverlayDisplayCompatibility.Unknown;
        }

        bool hasWindowedFrame =
            (window.Style & (WindowCaption | ThickFrame)) != 0 ||
            (window.ExtendedStyle & ExtendedWindowEdge) != 0;
        bool isTopMost = (window.ExtendedStyle & ExtendedTopMost) != 0;

        // osu! keeps the exclusive style while minimized during an Alt+Tab.
        // Preserve that incompatibility instead of misclassifying the iconic
        // -32000 rectangle as an ordinary Windowed client.
        if (window.IsMinimized)
        {
            return hasWindowedFrame
                ? OverlayDisplayCompatibility.Supported(OverlayDisplayMode.Windowed)
                : OverlayDisplayCompatibility.FullscreenExclusive;
        }

        if (!CoversMonitor(window.Bounds, window.MonitorBounds))
        {
            return OverlayDisplayCompatibility.Supported(OverlayDisplayMode.Windowed);
        }

        return hasWindowedFrame || !isTopMost
            ? OverlayDisplayCompatibility.Supported(OverlayDisplayMode.Borderless)
            : OverlayDisplayCompatibility.FullscreenExclusive;
    }

    private static bool CoversMonitor(
        OverlayWindowRectangle window,
        OverlayWindowRectangle monitor) =>
        Math.Abs(window.Left - monitor.Left) <= FullscreenTolerancePixels &&
        Math.Abs(window.Top - monitor.Top) <= FullscreenTolerancePixels &&
        Math.Abs(window.Right - monitor.Right) <= FullscreenTolerancePixels &&
        Math.Abs(window.Bottom - monitor.Bottom) <= FullscreenTolerancePixels;
}
