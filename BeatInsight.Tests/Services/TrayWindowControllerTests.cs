using BeatInsight.Services.Settings;
using BeatInsight.Services.Tray;
using System.Windows;

namespace BeatInsight.Tests.Services;

public sealed class TrayWindowControllerTests
{
    [Fact]
    public void Minimize_HidesExistingWindow()
    {
        using var fixture = new Fixture();

        fixture.Controller.RequestMinimize();

        Assert.False(fixture.Window.IsVisible);
        Assert.Equal(1, fixture.Window.HideCount);
    }

    [Fact]
    public void Minimize_RemovesWindowFromTaskbar()
    {
        using var fixture = new Fixture();

        fixture.Controller.RequestMinimize();

        Assert.False(fixture.Window.ShowInTaskbar);
    }

    [Fact]
    public void Minimize_KeepsApplicationAndTrayRunning()
    {
        using var fixture = new Fixture();

        fixture.Controller.RequestMinimize();

        Assert.Equal(0, fixture.ShutdownCount);
        Assert.True(fixture.Tray.Visible);
        Assert.False(fixture.Tray.IsDisposed);
    }

    [Fact]
    public void Restore_ShowsActivatesAndReturnsWindowToTaskbar()
    {
        using var fixture = new Fixture();
        fixture.Controller.RequestMinimize();

        fixture.Controller.Restore();

        Assert.True(fixture.Window.IsVisible);
        Assert.True(fixture.Window.ShowInTaskbar);
        Assert.Equal(1, fixture.Window.ShowCount);
        Assert.Equal(1, fixture.Window.ActivateCount);
    }

    [Fact]
    public void StartHidden_DoesNotShowOrHideWindowDuringStartup()
    {
        using var fixture = new Fixture(initiallyVisible: false);

        fixture.Controller.StartHidden();

        Assert.True(fixture.Controller.IsHiddenToTray);
        Assert.False(fixture.Window.IsVisible);
        Assert.False(fixture.Window.ShowInTaskbar);
        Assert.Equal(0, fixture.Window.ShowCount);
        Assert.Equal(0, fixture.Window.HideCount);
        Assert.Equal(0, fixture.Tray.NotificationCount);
    }

    [Fact]
    public void OpenAfterHiddenStartup_RestoresSameWindowNormally()
    {
        using var fixture = new Fixture(initiallyVisible: false);
        ITrayWindowHost originalWindow = fixture.Window;
        fixture.Controller.StartHidden();

        fixture.Tray.RaiseOpenRequested();

        Assert.Same(originalWindow, fixture.Window);
        Assert.True(fixture.Window.IsVisible);
        Assert.True(fixture.Window.ShowInTaskbar);
        Assert.False(fixture.Controller.IsHiddenToTray);
        Assert.Equal(1, fixture.Window.ActivateCount);
    }

    [Fact]
    public void Restore_PreservesPreviousMaximizedState()
    {
        using var fixture = new Fixture(WindowState.Maximized);

        fixture.Controller.RequestMinimize();
        fixture.Controller.Restore();

        Assert.Equal(WindowState.Maximized, fixture.Window.WindowState);
    }

    [Fact]
    public void TrayOpen_ReusesExistingWindow()
    {
        using var fixture = new Fixture();
        ITrayWindowHost originalWindow = fixture.Window;
        fixture.Controller.RequestMinimize();

        fixture.Tray.RaiseOpenRequested();

        Assert.Same(originalWindow, fixture.Window);
        Assert.Equal(1, fixture.Window.ShowCount);
    }

    [Fact]
    public void Closing_WithCloseToTray_HidesWithoutShutdown()
    {
        using var fixture = new Fixture(closeToTray: true);

        bool cancelClosing = fixture.Controller.TryHandleClosing();

        Assert.True(cancelClosing);
        Assert.False(fixture.Window.IsVisible);
        Assert.Equal(0, fixture.ShutdownCount);
    }

    [Fact]
    public void Closing_WithExitBehavior_AllowsNormalShutdown()
    {
        using var fixture = new Fixture(closeToTray: false);

        bool cancelClosing = fixture.Controller.TryHandleClosing();

        Assert.False(cancelClosing);
        Assert.True(fixture.Window.IsVisible);
    }

    [Fact]
    public void TrayQuit_BypassesCloseToTrayAndRequestsShutdown()
    {
        using var fixture = new Fixture(closeToTray: true);

        fixture.Tray.RaiseQuitRequested();

        Assert.True(fixture.Controller.IsExplicitShutdown);
        Assert.Equal(1, fixture.Window.CloseCount);
        Assert.Equal(1, fixture.ShutdownCount);
        Assert.False(fixture.Controller.TryHandleClosing());
    }

    [Fact]
    public void Dispose_RemovesTrayIconOnlyAtTrueLifetimeEnd()
    {
        var fixture = new Fixture();
        fixture.Controller.RequestMinimize();
        Assert.False(fixture.Tray.IsDisposed);

        fixture.Dispose();

        Assert.False(fixture.Tray.Visible);
        Assert.True(fixture.Tray.IsDisposed);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var fixture = new Fixture();

        fixture.Controller.Dispose();
        fixture.Controller.Dispose();

        Assert.Equal(1, fixture.Tray.DisposeCount);
    }

    [Fact]
    public void HiddenWindow_DoesNotStopUnrelatedOverlayOrServiceState()
    {
        using var fixture = new Fixture();
        object overlayState = new();
        int successfulTicks = 42;

        fixture.Controller.RequestMinimize();

        Assert.Same(overlayState, overlayState);
        Assert.Equal(42, successfulTicks);
        Assert.Equal(0, fixture.ShutdownCount);
    }

    [Fact]
    public void MinimizeRestore_DoesNotMutateThemeOrLayoutState()
    {
        using var fixture = new Fixture();
        object theme = new();
        object layout = new();

        fixture.Controller.RequestMinimize();
        fixture.Controller.Restore();

        Assert.Same(theme, theme);
        Assert.Same(layout, layout);
    }

    [Fact]
    public void SettingsStore_RoundTripsCloseBehavior()
    {
        using var directory = new TemporaryDirectory();
        string path = System.IO.Path.Combine(directory.Path, "window.json");
        var store = new WindowLifecycleSettingsStore(path);

        Assert.True(store.Save(new WindowLifecycleSettings(1, false)));

        Assert.False(store.Load().CloseToTray);
    }

    [Fact]
    public void SettingsStore_InvalidOrMissingContentFallsBackToSafeDefault()
    {
        using var directory = new TemporaryDirectory();
        string path = System.IO.Path.Combine(directory.Path, "window.json");
        var store = new WindowLifecycleSettingsStore(path);
        Assert.True(store.Load().CloseToTray);

        System.IO.File.WriteAllText(path, "{ definitely invalid }");

        Assert.True(store.Load().CloseToTray);
    }

    [Fact]
    public void EditModeMinimize_CancelsOnceWithoutPersistingAnything()
    {
        using var fixture = new Fixture(editModeActive: true);

        fixture.Controller.RequestMinimize();
        fixture.Controller.HandleWindowStateChanged();

        Assert.Equal(1, fixture.CancelEditCount);
        Assert.Equal(0, fixture.SaveCount);
    }

    [Fact]
    public void FirstMinimizeNotification_IsShownOnlyOnce()
    {
        using var fixture = new Fixture();

        fixture.Controller.RequestMinimize();
        fixture.Controller.Restore();
        fixture.Controller.RequestMinimize();

        Assert.Equal(1, fixture.Tray.NotificationCount);
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture(
            WindowState initialState = WindowState.Normal,
            bool closeToTray = true,
            bool editModeActive = false,
            bool initiallyVisible = true)
        {
            Window = new FakeWindow(initialState, initiallyVisible);
            Tray = new FakeTrayIcon();
            Controller = new TrayWindowController(
                Window,
                Tray,
                closeToTray,
                () => editModeActive,
                () => CancelEditCount++,
                () => ShutdownCount++);
        }

        public FakeWindow Window { get; }
        public FakeTrayIcon Tray { get; }
        public TrayWindowController Controller { get; }
        public int CancelEditCount { get; private set; }
        public int ShutdownCount { get; private set; }
        public int SaveCount { get; private set; }

        public void Dispose() => Controller.Dispose();
    }

    private sealed class FakeWindow(
        WindowState initialState,
        bool initiallyVisible) : ITrayWindowHost
    {
        public WindowState WindowState { get; set; } = initialState;
        public bool IsVisible { get; private set; } = initiallyVisible;
        public bool ShowInTaskbar { get; set; } = true;
        public int ShowCount { get; private set; }
        public int HideCount { get; private set; }
        public int ActivateCount { get; private set; }
        public int CloseCount { get; private set; }

        public void Show()
        {
            IsVisible = true;
            ShowCount++;
        }

        public void Hide()
        {
            IsVisible = false;
            HideCount++;
        }

        public bool Activate()
        {
            ActivateCount++;
            return true;
        }

        public void Close() => CloseCount++;
    }

    private sealed class FakeTrayIcon : ITrayIconLifetime
    {
        public event EventHandler? OpenRequested;
        public event EventHandler? QuitRequested;
        public bool Visible { get; set; }
        public bool IsDisposed { get; private set; }
        public int NotificationCount { get; private set; }
        public int DisposeCount { get; private set; }

        public void RaiseOpenRequested() => OpenRequested?.Invoke(this, EventArgs.Empty);
        public void RaiseQuitRequested() => QuitRequested?.Invoke(this, EventArgs.Empty);
        public void ShowFirstMinimizeNotification() => NotificationCount++;
        public void Dispose()
        {
            IsDisposed = true;
            DisposeCount++;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "BeatInsightTrayTests",
                Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Path))
                System.IO.Directory.Delete(Path, recursive: true);
        }
    }
}
