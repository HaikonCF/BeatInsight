using BeatInsight.ViewModels;
using BeatInsight.Views;
using System.Windows;

namespace BeatInsight;

public partial class MainWindow
{
    private SettingsWindow? settingsWindow;

    internal bool IsSettingsWindowOpen => settingsWindow is not null;

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        if (settingsWindow is not null)
        {
            if (!settingsWindow.IsVisible)
                settingsWindow.Show();

            if (settingsWindow.WindowState == WindowState.Minimized)
                settingsWindow.WindowState = WindowState.Normal;

            settingsWindow.Activate();
            return;
        }

        var window = new SettingsWindow(new SettingsWindowViewModel(mainViewModel))
        {
            Owner = this
        };

        window.ChangeSongsFolderRequested += ChangeSongsFolder_Click;
        window.ScanLibraryRequested += ScanLibrary_Click;
        window.CancelLibraryScanRequested += CancelLibraryScan_Click;
        window.SaveOsuApiCredentialsRequested += SaveOsuApiCredentials_Click;
        window.TestOsuApiConnectionRequested += TestOsuApiConnection_Click;
        window.RemoveOsuApiCredentialsRequested += RemoveOsuApiCredentials_Click;
        window.CreateOsuOAuthApplicationRequested += CreateOsuOAuthApplication_Click;
        window.CommunityThemesRequested += CommunityThemes_Click;
        window.CheckForUpdatesRequested += CheckForUpdates_Click;
        window.DownloadUpdateRequested += DownloadUpdate_Click;
        window.RestartAndUpdateRequested += RestartAndUpdate_Click;
        window.Closed += SettingsWindow_Closed;
        settingsWindow = window;
        window.Show();
    }

    private void SettingsWindow_Closed(object? sender, EventArgs e)
    {
        if (sender is not SettingsWindow window)
            return;

        window.ChangeSongsFolderRequested -= ChangeSongsFolder_Click;
        window.ScanLibraryRequested -= ScanLibrary_Click;
        window.CancelLibraryScanRequested -= CancelLibraryScan_Click;
        window.SaveOsuApiCredentialsRequested -= SaveOsuApiCredentials_Click;
        window.TestOsuApiConnectionRequested -= TestOsuApiConnection_Click;
        window.RemoveOsuApiCredentialsRequested -= RemoveOsuApiCredentials_Click;
        window.CreateOsuOAuthApplicationRequested -= CreateOsuOAuthApplication_Click;
        window.CommunityThemesRequested -= CommunityThemes_Click;
        window.CheckForUpdatesRequested -= CheckForUpdates_Click;
        window.DownloadUpdateRequested -= DownloadUpdate_Click;
        window.RestartAndUpdateRequested -= RestartAndUpdate_Click;
        window.Closed -= SettingsWindow_Closed;

        if (ReferenceEquals(settingsWindow, window))
            settingsWindow = null;
    }

    private void CloseSettingsWindow()
    {
        SettingsWindow? window = settingsWindow;
        if (window is null)
            return;

        window.Close();
    }
}
