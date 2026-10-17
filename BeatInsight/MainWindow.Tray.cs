using BeatInsight.Diagnostics;
using BeatInsight.Services.Settings;
using BeatInsight.Services.Tray;
using System.ComponentModel;
using System.Windows;

namespace BeatInsight;

public partial class MainWindow
{
    private readonly WindowLifecycleSettingsStore windowLifecycleSettingsStore = new();
    private readonly IStartupRegistrationService startupRegistrationService =
        new WindowsStartupRegistrationService();
    private TrayWindowController? trayWindowController;
    private bool startMinimized;

    private void InitializeTrayLifecycle()
    {
        WindowLifecycleSettings settings = windowLifecycleSettingsStore.Load();
        startMinimized = settings.StartMinimized;
        mainViewModel.WindowLifecycle.Apply(settings);
        mainViewModel.WindowLifecycle.SettingsChanged += ApplyWindowLifecycleSettings;
        mainViewModel.WindowLifecycle.StartWithWindowsChangeRequested +=
            ApplyStartupRegistration;
        trayWindowController = new TrayWindowController(
            new WpfTrayWindowHost(this),
            new BeatInsightTrayIcon(),
            settings.CloseToTray,
            () => overlayEngine.IsOverlayEditMode,
            CancelOverlayEditForWindowHide,
            RequestApplicationShutdown);

        StartupRegistrationState startupState =
            startupRegistrationService.RepairIfNeeded();
        mainViewModel.WindowLifecycle.ApplyStartupRegistration(startupState.IsEnabled);
        DebugLogger.Info(
            $"STARTUP | registration enabled={startupState.IsEnabled} | startMinimized={startMinimized}");
        if (!startupState.Success)
        {
            DebugLogger.Warning(
                $"STARTUP | registration repair failed | reason={startupState.FailureReason}");
        }
    }

    internal bool StartHiddenToTrayIfConfigured()
    {
        if (!startMinimized || trayWindowController is null)
            return false;

        trayWindowController.StartHidden();
        DebugLogger.Info("STARTUP | window presentation=tray-hidden");
        return true;
    }

    internal void RequestMinimizeToTray()
    {
        CloseSettingsWindow();
        trayWindowController?.RequestMinimize();
    }

    private void RequestApplicationShutdown()
    {
        BeginShutdown();
        Application.Current.Shutdown();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
            CloseSettingsWindow();
        trayWindowController?.HandleWindowStateChanged();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!e.Cancel && trayWindowController?.TryHandleClosing() == true)
        {
            CloseSettingsWindow();
            e.Cancel = true;
        }
        else if (!e.Cancel)
        {
            BeginShutdown();
        }

        base.OnClosing(e);
    }

    private void ApplyWindowLifecycleSettings(WindowLifecycleSettings settings)
    {
        startMinimized = settings.StartMinimized;
        if (trayWindowController is not null)
            trayWindowController.CloseToTray = settings.CloseToTray;
        _ = windowLifecycleSettingsStore.Save(settings);
    }

    private void ApplyStartupRegistration(bool enabled)
    {
        StartupRegistrationState state = enabled
            ? startupRegistrationService.Enable()
            : startupRegistrationService.Disable();
        mainViewModel.WindowLifecycle.ApplyStartupRegistration(state.IsEnabled);
        DebugLogger.Info(
            $"STARTUP | registration requested={enabled} | actual={state.IsEnabled} | success={state.Success}");
        if (!state.Success)
        {
            DebugLogger.Warning(
                $"STARTUP | registration update failed | reason={state.FailureReason}");
        }
    }

    private void DisposeTrayLifecycle()
    {
        mainViewModel.WindowLifecycle.SettingsChanged -= ApplyWindowLifecycleSettings;
        mainViewModel.WindowLifecycle.StartWithWindowsChangeRequested -=
            ApplyStartupRegistration;
        trayWindowController?.Dispose();
        trayWindowController = null;
    }
}
