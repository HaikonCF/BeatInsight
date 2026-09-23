using BeatInsight.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static BeatInsight.Controls.SectionTimelineGraphControl;

namespace BeatInsight.Views
{
    internal sealed class OsuApiCredentialsSaveRequestedEventArgs(
        string clientId,
        string clientSecret) : EventArgs
    {
        internal string ClientId { get; } = clientId;

        internal string ClientSecret { get; } = clientSecret;
    }

    public partial class DashboardView : UserControl
    {
        public event RoutedEventHandler? ChangeSongsFolderRequested;
        public event RoutedEventHandler? ScanLibraryRequested;
        public event RoutedEventHandler? CancelLibraryScanRequested;
        internal event EventHandler<OsuApiCredentialsSaveRequestedEventArgs>?
            SaveOsuApiCredentialsRequested;
        internal event RoutedEventHandler? TestOsuApiConnectionRequested;
        internal event RoutedEventHandler? RemoveOsuApiCredentialsRequested;
        internal event RoutedEventHandler? CreateOsuOAuthApplicationRequested;

        public DashboardView()
        {
            InitializeComponent();
        }

        internal bool IsOverviewDisplayed(
            string expectedTitle,
            string expectedDifficulty) =>
            string.Equals(
                OverviewTitleText.Text,
                expectedTitle,
                StringComparison.Ordinal)
            && string.Equals(
                OverviewDifficultyText.Text,
                expectedDifficulty,
                StringComparison.Ordinal);

        internal bool IsLoadingOverlayVisible =>
            OverviewLoadingOverlay.Visibility == Visibility.Visible;

        private Window? HostWindow =>
            Window.GetWindow(this);

        private void SectionTimelineGraph_SectionClicked(
            object? sender,
            SectionClickedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            int index = e.Index;

            if (index < 0 || index >= vm.Sections.Sections.Count)
                return;

            vm.Sections.SelectSection(vm.Sections.Sections[index]);
        }

        private void OverviewTabButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
                vm.SelectedTab = NavigationTab.Overview;
        }

        private void SkillsTabButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
                vm.SelectedTab = NavigationTab.Skills;
        }

        private void PreviousSectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
                vm.Sections.SelectPrevious();
        }

        private void NextSectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
                vm.Sections.SelectNext();
        }

        private void SectionsTabButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
                vm.SelectedTab = NavigationTab.Sections;
        }

        private void PatternsTabButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
                vm.SelectedTab = NavigationTab.Patterns;
        }

        private void IdentityTabButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
                vm.SelectedTab = NavigationTab.Identity;
        }

        private void DifficultyTabButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
                vm.SelectedTab = NavigationTab.Difficulty;
        }

        private void SettingsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            SetSettingsPanelOpen(
                SettingsPanel.Visibility != Visibility.Visible);
        }

        private void CloseSettingsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            SetSettingsPanelOpen(false);
        }

        private void SettingsDismissLayer_MouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            SetSettingsPanelOpen(false);
        }

        private void SettingsPanel_MouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private void SetSettingsPanelOpen(bool isOpen)
        {
            Visibility visibility = isOpen
                ? Visibility.Visible
                : Visibility.Collapsed;

            SettingsDismissLayer.Visibility = visibility;
            SettingsPanel.Visibility = visibility;

            if (!isOpen)
            {
                OsuClientSecretPasswordBox.Clear();
            }
        }

        private void SaveOsuApiCredentialsButton_Click(
            object sender,
            RoutedEventArgs e)
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

        private void TestOsuApiConnectionButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            TestOsuApiConnectionRequested?.Invoke(this, e);
        }

        private void RemoveOsuApiCredentialsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            RemoveOsuApiCredentialsRequested?.Invoke(this, e);
        }

        private void CreateOsuOAuthApplicationButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            CreateOsuOAuthApplicationRequested?.Invoke(this, e);
        }

        private void ChangeSongsFolderButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ChangeSongsFolderRequested?.Invoke(this, e);
        }

        private void ScanLibraryButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ScanLibraryRequested?.Invoke(this, e);
        }

        private void CancelLibraryScanButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            CancelLibraryScanRequested?.Invoke(this, e);
        }

        // ============================================================
        // WINDOW CONTROLS
        // ============================================================

        private void MinimizeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (HostWindow is not { } window)
                return;

            window.WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (HostWindow is not { } window)
                return;

            window.WindowState =
                window.WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
        }

        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            HostWindow?.Close();
        }


        // ============================================================
        // WINDOW DRAG
        // ============================================================

        private void Header_MouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (HostWindow is not { } window)
                return;

            // Double click = maximize / restore
            if (e.ClickCount == 2)
            {
                window.WindowState =
                    window.WindowState == WindowState.Maximized
                        ? WindowState.Normal
                        : WindowState.Maximized;

                return;
            }

            // Ne tente le DragMove que pour un clic simple.
            if (e.LeftButton == MouseButtonState.Pressed)
                window.DragMove();
        }
    }
}
