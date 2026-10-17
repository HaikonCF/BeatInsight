using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BeatInsight.Overlay;

public enum OverlayWindowInteractionMode
{
    Passive,
    Editable
}

public sealed class OverlayPlacementChangedEventArgs(
    string widgetId,
    double x,
    double y,
    double width,
    double height,
    bool resized) : EventArgs
{
    public string WidgetId { get; } = widgetId;
    public double X { get; } = x;
    public double Y { get; } = y;
    public double Width { get; } = width;
    public double Height { get; } = height;
    public bool Resized { get; } = resized;
}

public interface IEditableOverlayView
{
    event EventHandler<OverlayPlacementChangedEventArgs>? PlacementChanged;
    void SetInteractionMode(OverlayWindowInteractionMode mode);
}

public static class OverlayWindowInteraction
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    public static void Apply(Window window, OverlayWindowInteractionMode mode)
    {
        ArgumentNullException.ThrowIfNull(window);
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        int styles = GetWindowLong(handle, GwlExStyle);
        styles |= WsExToolWindow | WsExNoActivate;
        styles = mode == OverlayWindowInteractionMode.Passive
            ? styles | WsExTransparent
            : styles & ~WsExTransparent;
        _ = SetWindowLong(handle, GwlExStyle, styles);
    }

    internal static bool IsTransparentStyleSet(int styles) =>
        (styles & WsExTransparent) != 0;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr windowHandle, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr windowHandle, int index, int newStyle);
}
