using System.IO;
using System.Windows;
using System.Windows.Media;
using BeatInsight.CommunityOverlays.Poc.Managed;
using BeatInsight.Integration.CommunityOverlays.Managed;
using Xunit;

namespace BeatInsight.CommunityOverlays.Poc.Tests;

/// <summary>BUG-003 : un overlay géré suit le premier plan d'osu!, sauf pendant l'édition ; le mode CLI ne change pas.</summary>
public class ManagedForegroundVisibilityTests
{
    private static CompanionMessage Foreground(bool osu) =>
        CommunityOverlayCompanionProtocol.Parse(CommunityOverlayCompanionProtocol.Foreground(osu))!;

    private static CompanionMessage Edit(bool enabled) =>
        CommunityOverlayCompanionProtocol.Parse(CommunityOverlayCompanionProtocol.Edit(enabled))!;

    [Fact]
    public void Osu_foreground_shows_other_application_hides_and_return_shows_again()
    {
        var changes = RunWithHost(host =>
        {
            host.Apply(Foreground(true));
            host.Apply(Foreground(false)); // Alt+Tab vers Opera
            host.Apply(Foreground(false)); // inchangé : aucun événement
            host.Apply(Foreground(true));  // retour sur osu!
        }, out bool finalDisplay);

        Assert.Equal([false, true], changes);
        Assert.True(finalDisplay);
    }

    [Fact]
    public void Edit_mode_keeps_the_overlay_visible_then_returns_to_the_foreground_rule()
    {
        var changes = RunWithHost(host =>
        {
            host.Apply(Foreground(false)); // osu! fermé ou en arrière-plan
            host.Apply(Edit(true));        // Move / resize depuis les Settings
            host.Apply(Foreground(false));
            host.Apply(Edit(false));       // sortie de l'édition : masqué de nouveau
        }, out bool finalDisplay);

        Assert.Equal([false, true, false], changes);
        Assert.False(finalDisplay);
    }

    [Fact]
    public void Overlay_is_displayed_until_BeatInsight_sends_a_foreground_state()
    {
        // Prévisualisation : BeatInsight n'envoie jamais d'état de premier plan.
        var changes = RunWithHost(host => host.Apply(Edit(false)), out bool finalDisplay);

        Assert.Empty(changes);
        Assert.True(finalDisplay);
    }

    [Fact]
    public void Window_opacity_follows_the_host_without_moving_or_recreating_the_window()
    {
        var opacities = new List<double>();
        Rect? before = null, after = null;
        IntPtr handleBefore = IntPtr.Zero, handleAfter = IntPtr.Zero;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            ManagedCompanionHost? host = null;
            try
            {
                host = ManagedCompanionHost.TryAcquire(UniqueProfile(), TextWriter.Null)!;
                var window = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    Left = -10000, Top = -10000, Width = 50, Height = 50,
                };
                ManagedForegroundVisibility.Attach(window, host);
                window.Show();
                handleBefore = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                before = new Rect(window.Left, window.Top, window.Width, window.Height);
                opacities.Add(window.Opacity);

                host.Apply(Foreground(false));
                opacities.Add(window.Opacity);
                host.Apply(Foreground(true));
                opacities.Add(window.Opacity);

                after = new Rect(window.Left, window.Top, window.Width, window.Height);
                handleAfter = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                Assert.True(window.IsVisible);
                window.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                host?.Dispose();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
        Assert.Equal([1.0, 0.0, 1.0], opacities);
        Assert.Equal(before, after);
        Assert.NotEqual(IntPtr.Zero, handleBefore);
        Assert.Equal(handleBefore, handleAfter);
    }

    [Fact]
    public void Standalone_cli_has_no_managed_host_and_ignores_foreground()
    {
        var options = PocOptions.Parse(["--overlay", @"C:\overlays\KeyOverlay", "--source", "live-pipe"]);

        Assert.False(options.IsManaged);
        Assert.Null(options.ManagedByPid);
    }

    private static List<bool> RunWithHost(Action<ManagedCompanionHost> scenario, out bool finalDisplay)
    {
        var changes = new List<bool>();
        bool display = false;
        Exception? failure = null;
        // Le mutex appartient au thread qui le prend : acquisition et libération sur le même thread.
        var thread = new Thread(() =>
        {
            ManagedCompanionHost? host = null;
            try
            {
                host = ManagedCompanionHost.TryAcquire(UniqueProfile(), TextWriter.Null)!;
                host.DisplayChanged += changes.Add;
                scenario(host);
                display = host.ShouldDisplay;
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                host?.Dispose();
            }
        });
        thread.Start();
        thread.Join();
        Assert.Null(failure);
        finalDisplay = display;
        return changes;
    }

    private static string UniqueProfile() =>
        CommunityOverlayWindow.ProfileNameFor(Path.Combine(Path.GetTempPath(), $"foreground-{Guid.NewGuid():N}"));
}
