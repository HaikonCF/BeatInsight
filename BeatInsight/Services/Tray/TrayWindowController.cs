using System.Windows;

namespace BeatInsight.Services.Tray;

internal interface ITrayWindowHost
{
    WindowState WindowState { get; set; }
    bool IsVisible { get; }
    bool ShowInTaskbar { get; set; }
    void Show();
    void Hide();
    bool Activate();
    void Close();
}

internal interface ITrayIconLifetime : IDisposable
{
    event EventHandler? OpenRequested;
    event EventHandler? QuitRequested;
    bool Visible { get; set; }
    void ShowFirstMinimizeNotification();
}

internal sealed class TrayWindowController : IDisposable
{
    private readonly ITrayWindowHost window;
    private readonly ITrayIconLifetime trayIcon;
    private readonly Func<bool> isEditModeActive;
    private readonly Action cancelEditMode;
    private readonly Action shutdownApplication;
    private WindowState restoreWindowState;
    private bool firstMinimizeNotificationShown;
    private bool disposed;

    public TrayWindowController(
        ITrayWindowHost window,
        ITrayIconLifetime trayIcon,
        bool closeToTray,
        Func<bool> isEditModeActive,
        Action cancelEditMode,
        Action shutdownApplication)
    {
        this.window = window ?? throw new ArgumentNullException(nameof(window));
        this.trayIcon = trayIcon ?? throw new ArgumentNullException(nameof(trayIcon));
        this.isEditModeActive = isEditModeActive ?? throw new ArgumentNullException(nameof(isEditModeActive));
        this.cancelEditMode = cancelEditMode ?? throw new ArgumentNullException(nameof(cancelEditMode));
        this.shutdownApplication = shutdownApplication ?? throw new ArgumentNullException(nameof(shutdownApplication));
        CloseToTray = closeToTray;
        restoreWindowState = window.WindowState == WindowState.Maximized
            ? WindowState.Maximized
            : WindowState.Normal;
        trayIcon.OpenRequested += TrayIcon_OpenRequested;
        trayIcon.QuitRequested += TrayIcon_QuitRequested;
        trayIcon.Visible = true;
    }

    public bool CloseToTray { get; set; }
    public bool IsExplicitShutdown { get; private set; }
    public bool IsHiddenToTray { get; private set; }
    public WindowState RestoreWindowState => restoreWindowState;

    public void RequestMinimize()
    {
        RememberRestorableState();
        window.WindowState = WindowState.Minimized;
        HandleWindowStateChanged();
    }

    public void StartHidden()
    {
        if (disposed || IsHiddenToTray)
            return;

        RememberRestorableState();
        window.ShowInTaskbar = false;
        IsHiddenToTray = true;
    }

    public void HandleWindowStateChanged()
    {
        if (window.WindowState == WindowState.Minimized)
        {
            HideToTray();
            return;
        }

        if (!IsHiddenToTray)
            RememberRestorableState();
    }

    public bool TryHandleClosing()
    {
        if (IsExplicitShutdown || !CloseToTray)
            return false;

        HideToTray();
        return true;
    }

    public void Restore()
    {
        if (disposed)
            return;

        window.Show();
        window.WindowState = restoreWindowState;
        window.ShowInTaskbar = true;
        IsHiddenToTray = false;
        window.Activate();
    }

    public void RequestQuit()
    {
        if (disposed || IsExplicitShutdown)
            return;

        IsExplicitShutdown = true;
        window.Close();
        shutdownApplication();
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        trayIcon.OpenRequested -= TrayIcon_OpenRequested;
        trayIcon.QuitRequested -= TrayIcon_QuitRequested;
        trayIcon.Visible = false;
        trayIcon.Dispose();
    }

    private void HideToTray()
    {
        if (disposed || IsHiddenToTray)
            return;

        RememberRestorableState();
        if (isEditModeActive())
            cancelEditMode();

        window.ShowInTaskbar = false;
        window.Hide();
        IsHiddenToTray = true;

        if (!firstMinimizeNotificationShown)
        {
            firstMinimizeNotificationShown = true;
            trayIcon.ShowFirstMinimizeNotification();
        }
    }

    private void RememberRestorableState()
    {
        if (window.WindowState is WindowState.Normal or WindowState.Maximized)
            restoreWindowState = window.WindowState;
    }

    private void TrayIcon_OpenRequested(object? sender, EventArgs e) => Restore();

    private void TrayIcon_QuitRequested(object? sender, EventArgs e) => RequestQuit();
}
