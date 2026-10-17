using BeatInsight.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BeatInsight.Views;

public partial class SettingsWindow : Window
{
    internal event RoutedEventHandler? ChangeSongsFolderRequested;
    internal event RoutedEventHandler? ScanLibraryRequested;
    internal event RoutedEventHandler? CancelLibraryScanRequested;
    internal event EventHandler<OsuApiCredentialsSaveRequestedEventArgs>?
        SaveOsuApiCredentialsRequested;
    internal event RoutedEventHandler? TestOsuApiConnectionRequested;
    internal event RoutedEventHandler? RemoveOsuApiCredentialsRequested;
    internal event RoutedEventHandler? CreateOsuOAuthApplicationRequested;
    internal event RoutedEventHandler? CommunityThemesRequested;
    internal event RoutedEventHandler? CheckForUpdatesRequested;
    internal event RoutedEventHandler? DownloadUpdateRequested;
    internal event RoutedEventHandler? RestartAndUpdateRequested;

    public SettingsWindow(SettingsWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }

    internal SettingsWindowViewModel ViewModel =>
        (SettingsWindowViewModel)DataContext;

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void SettingsWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        Close();
        e.Handled = true;
    }

    private void Category_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string category }
            && Enum.TryParse(category, out SettingsCategory parsed))
        {
            ViewModel.SelectedCategory = parsed;
        }
    }

    private void ChangeSongsFolderButton_Click(object sender, RoutedEventArgs e) =>
        ChangeSongsFolderRequested?.Invoke(this, e);

    private void ScanLibraryButton_Click(object sender, RoutedEventArgs e) =>
        ScanLibraryRequested?.Invoke(this, e);

    private void CancelLibraryScanButton_Click(object sender, RoutedEventArgs e) =>
        CancelLibraryScanRequested?.Invoke(this, e);

    private void BeginOverlayEditButton_Click(object sender, RoutedEventArgs e) =>
        ViewModel.OverlaySettings.RequestBeginEdit();

    private void SaveOverlayEditButton_Click(object sender, RoutedEventArgs e) =>
        ViewModel.OverlaySettings.RequestSaveEdit();

    private void CancelOverlayEditButton_Click(object sender, RoutedEventArgs e) =>
        ViewModel.OverlaySettings.RequestCancelEdit();

    private void ResetOverlayEditButton_Click(object sender, RoutedEventArgs e) =>
        ViewModel.OverlaySettings.RequestResetEdit();

    private void AddCommunityOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select a tosu! overlay folder (the folder that contains index.html)"
        };
        if (dialog.ShowDialog(this) == true)
            ViewModel.CommunityOverlays.Import(dialog.FolderName);
    }

    private static CommunityOverlayItemViewModel? CommunityOverlayOf(object sender) =>
        (sender as FrameworkElement)?.DataContext as CommunityOverlayItemViewModel;

    private void StartCommunityOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (CommunityOverlayOf(sender) is { } item) ViewModel.CommunityOverlays.Start(item);
    }

    private void StopCommunityOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (CommunityOverlayOf(sender) is { } item) ViewModel.CommunityOverlays.Stop(item);
    }

    private void PreviewCommunityOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (CommunityOverlayOf(sender) is { } item) ViewModel.CommunityOverlays.Preview(item);
    }

    private void EditCommunityOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (CommunityOverlayOf(sender) is { } item) ViewModel.CommunityOverlays.ToggleEdit(item);
    }

    private void ResetCommunityOverlayScaleButton_Click(object sender, RoutedEventArgs e)
    {
        if (CommunityOverlayOf(sender) is { } item) ViewModel.CommunityOverlays.ResetScale(item);
    }

    private void ApplyCommunityOverlayBoundsButton_Click(object sender, RoutedEventArgs e)
    {
        if (CommunityOverlayOf(sender) is { } item) ViewModel.CommunityOverlays.ApplyBounds(item);
    }

    private void RemoveCommunityOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (CommunityOverlayOf(sender) is not { } item)
            return;

        MessageBoxResult answer = MessageBox.Show(
            this,
            $"Remove \"{item.Name}\" from BeatInsight?\n\nThe overlay folder and its files are kept:\n{item.FolderPath}",
            "Remove community overlay",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (answer == MessageBoxResult.OK)
            ViewModel.CommunityOverlays.Remove(item);
    }

    private void CommunityThemesButton_Click(object sender, RoutedEventArgs e) =>
        CommunityThemesRequested?.Invoke(this, e);

    private void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e) =>
        CheckForUpdatesRequested?.Invoke(this, e);

    private void DownloadUpdateButton_Click(object sender, RoutedEventArgs e) =>
        DownloadUpdateRequested?.Invoke(this, e);

    private void RestartAndUpdateButton_Click(object sender, RoutedEventArgs e) =>
        RestartAndUpdateRequested?.Invoke(this, e);

    private void SaveOsuApiCredentialsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveOsuApiCredentialsRequested?.Invoke(
                this,
                new OsuApiCredentialsSaveRequestedEventArgs(
                    OsuClientIdTextBox.Text,
                    OsuClientSecretPasswordBox.Password));
        }
        finally
        {
            OsuClientSecretPasswordBox.Clear();
        }
    }

    private void TestOsuApiConnectionButton_Click(object sender, RoutedEventArgs e) =>
        TestOsuApiConnectionRequested?.Invoke(this, e);

    private void RemoveOsuApiCredentialsButton_Click(object sender, RoutedEventArgs e) =>
        RemoveOsuApiCredentialsRequested?.Invoke(this, e);

    private void CreateOsuOAuthApplicationButton_Click(object sender, RoutedEventArgs e) =>
        CreateOsuOAuthApplicationRequested?.Invoke(this, e);

    protected override void OnClosed(EventArgs e)
    {
        OsuClientSecretPasswordBox.Clear();
        base.OnClosed(e);
    }
}
