using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BeatInsight.Views
{
    public partial class DashboardView : UserControl
    {
        public event RoutedEventHandler? ChangeSongsFolderRequested;
        public event RoutedEventHandler? ScanLibraryRequested;
        public event RoutedEventHandler? CancelLibraryScanRequested;

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

        private Window? HostWindow =>
            Window.GetWindow(this);

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
