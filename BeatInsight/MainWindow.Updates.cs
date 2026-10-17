using BeatInsight.Diagnostics;
using BeatInsight.Services.Lifecycle;
using BeatInsight.Services.Settings;
using BeatInsight.Services.Updates;
using System.Windows;

namespace BeatInsight;

public partial class MainWindow
{
    private readonly UpdateSettingsStore updateSettingsStore = new();
    private readonly CancellationTokenSource updateLifetimeCancellation = new();
    private UpdateService? updateService;

    private void InitializeUpdateLifecycle()
    {
        UpdateSettings settings = updateSettingsStore.Load();
        mainViewModel.Updates.ApplySettings(settings);
        mainViewModel.Updates.SettingsChanged += ApplyUpdateSettings;
        try
        {
            UpdateFeedConfiguration configuration = UpdateFeedConfiguration.CreateDefault();
            updateService = new UpdateService(new VelopackUpdateClient(configuration));
            updateService.StatusChanged += UpdateService_StatusChanged;
            mainViewModel.Updates.ApplyStatus(updateService.Status);
            DebugLogger.Info(
                $"UPDATE | source={configuration.SourceDescription} | " +
                $"channel={configuration.Channel} | installed={updateService.IsInstalled}");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            DebugLogger.Warning($"UPDATE INIT failed | {exception.GetType().Name}");
        }

        Loaded += MainWindow_UpdateLoaded;
    }

    private async void MainWindow_UpdateLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_UpdateLoaded;
        if (!mainViewModel.Updates.CheckAutomatically || updateService?.IsInstalled != true)
            return;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), updateLifetimeCancellation.Token);
            await updateService.CheckAsync(updateLifetimeCancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (updateService is null)
        {
            mainViewModel.Updates.ApplyStatus(new UpdateStatus(
                UpdateState.Failed,
                ReleaseInfo.Version,
                Message: "Unable to check for updates"));
            return;
        }

        await updateService.CheckAsync(updateLifetimeCancellation.Token);
    }

    private async void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (updateService is not null)
            await updateService.DownloadAsync(updateLifetimeCancellation.Token);
    }

    private void RestartAndUpdate_Click(object sender, RoutedEventArgs e)
    {
        UpdateService? service = updateService;
        if (service is null
            || !service.TryBeginApply(out UpdateCandidate? update)
            || update is null)
        {
            return;
        }

        DebugLogger.Info($"UPDATE APPLY requested | target={update.Version}");
        ShutdownResult shutdown = BeginShutdown();
        DebugLogger.Info(
            $"UPDATE APPLY handoff | shutdownStarted={shutdown.Started} | " +
            $"shutdownState={shutdown.State} | failedSteps={shutdown.FailedSteps.Count}");

        try
        {
            service.WaitExitThenApplyAndRestart(update);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            DebugLogger.Warning($"UPDATE APPLY handoff failed | {exception.GetType().Name}");
        }
        finally
        {
            Application.Current.Shutdown();
        }
    }

    private void ApplyUpdateSettings(UpdateSettings settings)
    {
        if (!updateSettingsStore.Save(settings))
            DebugLogger.Warning("UPDATE SETTINGS save failed");
    }

    private void UpdateService_StatusChanged(UpdateStatus status)
    {
        if (Dispatcher.CheckAccess())
        {
            mainViewModel.Updates.ApplyStatus(status);
            return;
        }

        _ = Dispatcher.BeginInvoke(() => mainViewModel.Updates.ApplyStatus(status));
    }

    private void DisposeUpdateLifecycle()
    {
        Loaded -= MainWindow_UpdateLoaded;
        mainViewModel.Updates.SettingsChanged -= ApplyUpdateSettings;
        updateLifetimeCancellation.Cancel();
        if (updateService is not null)
        {
            updateService.StatusChanged -= UpdateService_StatusChanged;
            updateService.Dispose();
            updateService = null;
        }
        updateLifetimeCancellation.Dispose();
    }
}
