using System.IO;

namespace BeatInsight.Tests.Diagnostics;

public sealed class SettingsUiArchitectureTests
{
    private static readonly string RepositoryRoot = ResolveRepositoryRoot();

    [Fact]
    public void SettingsWindowHasLargeResizableLayoutAndAllProductionCategories()
    {
        string xaml = Read("BeatInsight", "Views", "SettingsWindow.xaml");

        Assert.Contains("Width=\"1100\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"760\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"900\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"620\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ResizeMode=\"CanResize\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"General\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Overlay\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Appearance\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Updates\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"osu! API\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsWindowReusesExistingBehaviorBindings()
    {
        string xaml = Read("BeatInsight", "Views", "SettingsWindow.xaml");

        Assert.Contains("Library.SongsFolderText", xaml, StringComparison.Ordinal);
        Assert.Contains("WindowLifecycle.StartWithWindows", xaml, StringComparison.Ordinal);
        Assert.Contains("OverlaySettings.SelectedPresetId", xaml, StringComparison.Ordinal);
        Assert.Contains("OverlaySettings.ShowLiveStats", xaml, StringComparison.Ordinal);
        Assert.Contains("OverlaySettings.ShowPerformance", xaml, StringComparison.Ordinal);
        Assert.Contains("OverlaySettings.SelectedAppThemeId", xaml, StringComparison.Ordinal);
        Assert.Contains("OverlaySettings.SelectedOverlayThemeId", xaml, StringComparison.Ordinal);
        Assert.Contains("Updates.CheckAutomatically", xaml, StringComparison.Ordinal);
        Assert.Contains("Updates.StatusText", xaml, StringComparison.Ordinal);
        Assert.Contains("OsuApiSettings.ClientId", xaml, StringComparison.Ordinal);
        Assert.Contains("<PasswordBox", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainDashboardUsesDedicatedSettingsPathWithoutLegacyPanel()
    {
        string dashboard = Read("BeatInsight", "Views", "DashboardView.xaml");
        string dashboardCode = Read("BeatInsight", "Views", "DashboardView.xaml.cs");
        string lifecycle = Read("BeatInsight", "MainWindow.Settings.cs");

        Assert.Contains("x:Name=\"SettingsButton\"", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("SettingsPanel", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("SettingsDismissLayer", dashboard, StringComparison.Ordinal);
        Assert.Contains("SettingsRequested?.Invoke", dashboardCode, StringComparison.Ordinal);
        Assert.Contains("private SettingsWindow? settingsWindow", lifecycle, StringComparison.Ordinal);
        Assert.Contains("settingsWindow.Activate()", lifecycle, StringComparison.Ordinal);
        Assert.Contains("CloseSettingsWindow", lifecycle, StringComparison.Ordinal);
    }

    [Fact]
    public void CommunityOverlaysLiveInOverlayPageWithThemeTokensAndManagedShutdown()
    {
        string xaml = Read("BeatInsight", "Views", "SettingsWindow.xaml");
        string mainWindow = Read("BeatInsight", "MainWindow.xaml.cs");
        string managed = Read("BeatInsight", "MainWindow.CommunityOverlaysManaged.cs");

        int overlayPage = xaml.IndexOf("Visibility=\"{Binding IsOverlaySelected", StringComparison.Ordinal);
        int appearancePage = xaml.IndexOf("Visibility=\"{Binding IsAppearanceSelected", StringComparison.Ordinal);
        int card = xaml.IndexOf("x:Name=\"CommunityOverlaysCard\"", StringComparison.Ordinal);
        Assert.InRange(card, overlayPage, appearancePage);
        Assert.DoesNotContain("Tag=\"CommunityOverlays\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Community Overlays\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CommunityOverlays.Overlays", xaml, StringComparison.Ordinal);
        Assert.Contains("DynamicResource AppDangerBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("new(\"community overlays stopped\", StopManagedCommunityOverlays)", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Un canal ouvert par une option de diagnostic reste ouvert.", managed, StringComparison.Ordinal);
    }

    [Fact]
    public void CentralStylesCoverEveryRequestedControlAndUseThemeTokens()
    {
        string styles = Read("BeatInsight", "Theming", "BeatInsightControls.xaml");

        Assert.Contains("BeatInsightButtonStyle", styles, StringComparison.Ordinal);
        Assert.Contains("BeatInsightPrimaryButtonStyle", styles, StringComparison.Ordinal);
        Assert.Contains("BeatInsightSecondaryButtonStyle", styles, StringComparison.Ordinal);
        Assert.Contains("BeatInsightDangerButtonStyle", styles, StringComparison.Ordinal);
        Assert.Contains("BeatInsightComboBoxStyle", styles, StringComparison.Ordinal);
        Assert.Contains("BeatInsightCheckBoxStyle", styles, StringComparison.Ordinal);
        Assert.Contains("BeatInsightTextBoxStyle", styles, StringComparison.Ordinal);
        Assert.Contains("BeatInsightPasswordBoxStyle", styles, StringComparison.Ordinal);
        Assert.Contains("BeatInsightScrollBarStyle", styles, StringComparison.Ordinal);
        Assert.Contains("SettingsSidebarItemStyle", styles, StringComparison.Ordinal);
        Assert.Contains("DynamicResource AppAccentBrush", styles, StringComparison.Ordinal);
        Assert.Contains("DynamicResource AppSurfaceBrush", styles, StringComparison.Ordinal);
        Assert.Contains("DynamicResource AppTextPrimaryBrush", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("#FF2E93", styles, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ThemeEditorDataGridStylesEveryGeneratedContainerWithDynamicThemeTokens()
    {
        string xaml = Read("BeatInsight", "Views", "CommunityThemesWindow.xaml");

        Assert.Contains("ThemeTokenDataGridStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("ThemeTokenDataGridRowStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("ThemeTokenDataGridCellStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("ThemeTokenDataGridHeaderStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("RowStyle=\"{StaticResource ThemeTokenDataGridRowStyle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CellStyle=\"{StaticResource ThemeTokenDataGridCellStyle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ColumnHeaderStyle=\"{StaticResource ThemeTokenDataGridHeaderStyle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DynamicResource AppSurfaceAltBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("DynamicResource AppSurfaceBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("DynamicResource AppTextPrimaryBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("DynamicResource AppTextSecondaryBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("DynamicResource AppBorderBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("DynamicResource AppBorderAccentBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("DynamicResource AppAccentSoftBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"IsMouseOver\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"IsSelected\"", xaml, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine(segments.Prepend(RepositoryRoot).ToArray()));

    private static string ResolveRepositoryRoot()
    {
        string? current = AppDomain.CurrentDomain.BaseDirectory;
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current, "BeatInsight.slnx")))
                return current;

            current = Directory.GetParent(current)?.FullName;
        }

        throw new InvalidOperationException("Could not locate BeatInsight.slnx.");
    }
}
