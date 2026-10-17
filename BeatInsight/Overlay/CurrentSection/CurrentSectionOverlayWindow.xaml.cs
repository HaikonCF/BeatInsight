using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BeatInsight.Overlay.CurrentSection;

public partial class CurrentSectionOverlayWindow : Window, ICurrentSectionOverlayView
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private readonly CurrentSectionOverlayViewModel viewModel = new();

    public CurrentSectionOverlayWindow()
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public void Apply(
        CurrentSectionOverlayState state,
        OverlayWidgetPlacement placement,
        OverlayTheme theme)
    {
        viewModel.Apply(state, theme);
        Left = placement.X;
        Top = placement.Y;
        Width = placement.Width;
        Height = placement.Height;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr handle = new WindowInteropHelper(this).Handle;
        int styles = GetWindowLong(handle, GwlExStyle);
        _ = SetWindowLong(
            handle,
            GwlExStyle,
            styles | WsExTransparent | WsExToolWindow | WsExNoActivate);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr windowHandle, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr windowHandle, int index, int newStyle);
}
