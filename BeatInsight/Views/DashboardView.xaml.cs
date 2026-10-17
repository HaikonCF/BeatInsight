using BeatInsight.ViewModels;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using static BeatInsight.Controls.SectionTimelineGraphControl;

namespace BeatInsight.Views
{
    public partial class DashboardView : UserControl
    {
        private bool _isMaximizedDragPending;
        private Point _maximizedDragStart;
        private double _maximizedDragHorizontalRatio;

        internal event RoutedEventHandler? SettingsRequested;

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
            SettingsRequested?.Invoke(this, e);
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

            if (window is MainWindow mainWindow)
                mainWindow.RequestMinimizeToTray();
            else
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

            if (IsInteractiveTitleBarElement(e.OriginalSource as DependencyObject))
                return;

            // Double click = maximize / restore
            if (e.ClickCount == 2)
            {
                CancelPendingMaximizedDrag();
                window.WindowState =
                    window.WindowState == WindowState.Maximized
                        ? WindowState.Normal
                        : WindowState.Maximized;

                e.Handled = true;
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            if (window.WindowState == WindowState.Maximized)
            {
                _maximizedDragStart = e.GetPosition(window);
                _maximizedDragHorizontalRatio = window.ActualWidth > 0
                    ? Math.Clamp(_maximizedDragStart.X / window.ActualWidth, 0, 1)
                    : 0.5;
                _isMaximizedDragPending = DashboardHeader.CaptureMouse();
                e.Handled = _isMaximizedDragPending;
                return;
            }

            window.DragMove();
        }

        private void Header_MouseMove(
            object sender,
            MouseEventArgs e)
        {
            if (!_isMaximizedDragPending || HostWindow is not { } window)
                return;

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                CancelPendingMaximizedDrag();
                return;
            }

            Point currentPosition = e.GetPosition(window);
            bool dragStarted =
                Math.Abs(currentPosition.X - _maximizedDragStart.X)
                    >= SystemParameters.MinimumHorizontalDragDistance
                || Math.Abs(currentPosition.Y - _maximizedDragStart.Y)
                    >= SystemParameters.MinimumVerticalDragDistance;

            if (!dragStarted)
                return;

            Point cursorOnScreen = window.PointToScreen(currentPosition);
            double titleBarCursorOffset = _maximizedDragStart.Y;

            CancelPendingMaximizedDrag();
            RestoreWindowForDrag(
                window,
                cursorOnScreen,
                _maximizedDragHorizontalRatio,
                titleBarCursorOffset);

            e.Handled = true;
            window.DragMove();
        }

        private void Header_MouseLeftButtonUp(
            object sender,
            MouseButtonEventArgs e)
        {
            CancelPendingMaximizedDrag();
        }

        private void CancelPendingMaximizedDrag()
        {
            _isMaximizedDragPending = false;

            if (Mouse.Captured == DashboardHeader)
                DashboardHeader.ReleaseMouseCapture();
        }

        private static void RestoreWindowForDrag(
            Window window,
            Point cursorOnScreen,
            double horizontalRatio,
            double titleBarCursorOffset)
        {
            window.WindowState = WindowState.Normal;
            window.UpdateLayout();

            double restoredWidth = window.ActualWidth;
            double restoredHeight = window.ActualHeight;
            Point cursorInRestoredWindow = window.PointFromScreen(cursorOnScreen);
            double cursorScreenX = window.Left + cursorInRestoredWindow.X;
            double cursorScreenY = window.Top + cursorInRestoredWindow.Y;

            double desiredLeft = cursorScreenX - (restoredWidth * horizontalRatio);
            double desiredTop = cursorScreenY
                - Math.Clamp(titleBarCursorOffset, 0, restoredHeight);

            if (TryGetMonitorWorkArea(window, cursorOnScreen, out Rect workArea))
            {
                desiredLeft = ClampWindowOrigin(
                    desiredLeft,
                    workArea.Left,
                    workArea.Width,
                    restoredWidth);
                desiredTop = ClampWindowOrigin(
                    desiredTop,
                    workArea.Top,
                    workArea.Height,
                    restoredHeight);
            }

            window.Left = desiredLeft;
            window.Top = desiredTop;
        }

        private static double ClampWindowOrigin(
            double desiredOrigin,
            double workAreaOrigin,
            double workAreaLength,
            double windowLength)
        {
            if (windowLength >= workAreaLength)
                return workAreaOrigin;

            return Math.Clamp(
                desiredOrigin,
                workAreaOrigin,
                workAreaOrigin + workAreaLength - windowLength);
        }

        private static bool TryGetMonitorWorkArea(
            Window window,
            Point cursorOnScreen,
            out Rect workArea)
        {
            NativePoint cursorPoint = new(
                (int)Math.Round(cursorOnScreen.X),
                (int)Math.Round(cursorOnScreen.Y));
            nint monitor = MonitorFromPoint(
                cursorPoint,
                MonitorDefaultToNearest);
            MonitorInfo monitorInfo = new()
            {
                Size = Marshal.SizeOf<MonitorInfo>()
            };

            if (monitor == 0 || !GetMonitorInfo(monitor, ref monitorInfo))
            {
                workArea = Rect.Empty;
                return false;
            }

            Point topLeftInWindow = window.PointFromScreen(
                new Point(monitorInfo.WorkArea.Left, monitorInfo.WorkArea.Top));
            Point bottomRightInWindow = window.PointFromScreen(
                new Point(monitorInfo.WorkArea.Right, monitorInfo.WorkArea.Bottom));
            Point topLeft = new(
                window.Left + topLeftInWindow.X,
                window.Top + topLeftInWindow.Y);
            Point bottomRight = new(
                window.Left + bottomRightInWindow.X,
                window.Top + bottomRightInWindow.Y);

            workArea = new Rect(topLeft, bottomRight);
            return true;
        }

        private bool IsInteractiveTitleBarElement(DependencyObject? source)
        {
            for (DependencyObject? current = source;
                 current is not null && current != DashboardHeader;
                 current = GetParent(current))
            {
                if (current is ButtonBase
                    or TextBoxBase
                    or PasswordBox
                    or Selector
                    or RangeBase
                    or Thumb
                    or MenuItem)
                {
                    return true;
                }
            }

            return false;
        }

        private static DependencyObject? GetParent(DependencyObject child) =>
            child is Visual
                ? VisualTreeHelper.GetParent(child)
                : LogicalTreeHelper.GetParent(child);

        private const uint MonitorDefaultToNearest = 2;

        [DllImport("user32.dll")]
        private static extern nint MonitorFromPoint(
            NativePoint point,
            uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(
            nint monitor,
            ref MonitorInfo monitorInfo);

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct NativePoint
        {
            public NativePoint(int x, int y)
            {
                X = x;
                Y = y;
            }

            public readonly int X;
            public readonly int Y;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect MonitorArea;
            public NativeRect WorkArea;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
