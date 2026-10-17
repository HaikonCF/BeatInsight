using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using BeatInsight.CommunityOverlays.Poc.Managed;
using Xunit;

namespace BeatInsight.CommunityOverlays.Poc.Tests;

/// <summary>FEAT-003.2 : click-through des overlays gérés, transitions Passive ↔ Editable.</summary>
public class ManagedWindowInteractionTests
{
    private const int WsExToolWindow = 0x00000080;
    private const int ClickThrough = ManagedWindowInteraction.WsExTransparent | ManagedWindowInteraction.WsExNoActivate;

    [Fact]
    public void Passive_mode_is_click_through_and_keeps_other_styles()
    {
        int styles = ManagedWindowInteraction.ExtendedStylesFor(WsExToolWindow, editing: false);

        Assert.Equal(ClickThrough, styles & ClickThrough);
        Assert.NotEqual(0, styles & ManagedWindowInteraction.WsExLayered);
        Assert.NotEqual(0, styles & WsExToolWindow);
    }

    [Fact]
    public void Edit_mode_is_interactive_and_keeps_layering()
    {
        int passive = ManagedWindowInteraction.ExtendedStylesFor(WsExToolWindow, editing: false);
        int editing = ManagedWindowInteraction.ExtendedStylesFor(passive, editing: true);

        Assert.Equal(0, editing & ClickThrough);
        Assert.NotEqual(0, editing & ManagedWindowInteraction.WsExLayered); // transparence WPF/WebView2 conservée
        Assert.NotEqual(0, editing & WsExToolWindow);
        Assert.Equal(passive, ManagedWindowInteraction.ExtendedStylesFor(editing, editing: false));
    }

    [Fact]
    public void Real_window_follows_passive_edit_passive_transitions()
    {
        var results = new List<int>();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                bool editing = false;
                var window = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    Left = -10000, Top = -10000, Width = 50, Height = 50,
                };
                ManagedWindowInteraction.Attach(window, () => editing);
                window.Show();
                IntPtr handle = new WindowInteropHelper(window).Handle;
                results.Add(GetWindowLong(handle, -20)); // passif dès la création du handle

                editing = true;
                window.ResizeMode = ResizeMode.CanResizeWithGrip;
                ManagedWindowInteraction.Apply(window, editing);
                results.Add(GetWindowLong(handle, -20));

                editing = false;
                window.ResizeMode = ResizeMode.NoResize; // WPF réécrit les styles : le hook garde le click-through
                ManagedWindowInteraction.Apply(window, editing);
                results.Add(GetWindowLong(handle, -20));

                window.Topmost = true;
                results.Add(GetWindowLong(handle, -20));

                window.Close();
                ManagedWindowInteraction.Apply(window, editing: true); // après fermeture : sans effet, sans exception
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
        Assert.Equal(4, results.Count);
        Assert.Equal(ClickThrough, results[0] & ClickThrough);
        Assert.Equal(0, results[1] & ClickThrough);
        Assert.Equal(ClickThrough, results[2] & ClickThrough);
        Assert.Equal(ClickThrough, results[3] & ClickThrough);
        Assert.All(results, styles => Assert.NotEqual(0, styles & ManagedWindowInteraction.WsExLayered));
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr windowHandle, int index);
}
