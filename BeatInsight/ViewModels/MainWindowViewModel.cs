namespace BeatInsight.ViewModels;

using BeatInsight.Integration.Game;
using BeatInsight.Overlay;

public enum NavigationTab
{
    Overview,
    Skills,
    Sections,
    Patterns,
    Identity,
    Difficulty
}

public sealed class MainWindowViewModel : ViewModelBase
{
    private NavigationTab selectedTab = NavigationTab.Overview;
    private string clientStatusText = "No osu! client detected";
    private string clientStatusCompactText = "● No client";
    private ClientStatusKind clientStatusKind = ClientStatusKind.Disconnected;
    private string emptyStateTitle = "No osu! client detected";
    private string emptyStateDescription = "Start a supported osu! client to begin.";
    private OverlayDisplayMode overlayDisplayMode = OverlayDisplayMode.ClientAbsent;
    private bool overlaySupported;
    private bool overlayVisible;
    private bool osuIsForeground;
    private string overlayStatusText = string.Empty;
    private string statusMapText = "Aucune beatmap chargée";
    private string statusAnalysisText = "Prêt";
    private string appVersionText = BeatInsight.Diagnostics.ReleaseInfo.DisplayName;

    public NavigationTab SelectedTab
    {
        get => selectedTab;
        set
        {
            if (SetProperty(ref selectedTab, value))
            {
                OnPropertyChanged(nameof(IsOverviewTabActive));
                OnPropertyChanged(nameof(IsSkillsTabActive));
                OnPropertyChanged(nameof(IsSectionsTabActive));
                OnPropertyChanged(nameof(IsPatternsTabActive));
                OnPropertyChanged(nameof(IsIdentityTabActive));
                OnPropertyChanged(nameof(IsDifficultyTabActive));
            }
        }
    }

    public bool IsOverviewTabActive => SelectedTab == NavigationTab.Overview;
    public bool IsSkillsTabActive => SelectedTab == NavigationTab.Skills;
    public bool IsSectionsTabActive => SelectedTab == NavigationTab.Sections;
    public bool IsPatternsTabActive => SelectedTab == NavigationTab.Patterns;
    public bool IsIdentityTabActive => SelectedTab == NavigationTab.Identity;
    public bool IsDifficultyTabActive => SelectedTab == NavigationTab.Difficulty;

    public OverviewViewModel Overview { get; }
    public SectionsViewModel Sections { get; }
    public LibraryPanelViewModel Library { get; }
    public OsuApiSettingsViewModel OsuApiSettings { get; }
    public OverlaySettingsViewModel OverlaySettings { get; }
    public WindowLifecycleSettingsViewModel WindowLifecycle { get; }
    public UpdateViewModel Updates { get; }
    public CommunityOverlaysViewModel CommunityOverlays { get; }

    public MainWindowViewModel()
    {
        Overview = new OverviewViewModel();
        Sections = new SectionsViewModel(Overview);
        Library = new LibraryPanelViewModel();
        OsuApiSettings = new OsuApiSettingsViewModel();
        OverlaySettings = new OverlaySettingsViewModel();
        WindowLifecycle = new WindowLifecycleSettingsViewModel();
        Updates = new UpdateViewModel();
        CommunityOverlays = new CommunityOverlaysViewModel();
    }

    public string ClientStatusText
    {
        get => clientStatusText;
        private set => SetProperty(ref clientStatusText, value);
    }

    public string GameStateStatusText => string.IsNullOrWhiteSpace(OverlayStatusText)
        ? ClientStatusText
        : $"{ClientStatusText} — {OverlayStatusText}";

    public OverlayDisplayMode OverlayDisplayMode
    {
        get => overlayDisplayMode;
        private set => SetProperty(ref overlayDisplayMode, value);
    }

    public bool OverlaySupported
    {
        get => overlaySupported;
        private set => SetProperty(ref overlaySupported, value);
    }

    public bool OverlayVisible
    {
        get => overlayVisible;
        private set => SetProperty(ref overlayVisible, value);
    }

    public bool OsuIsForeground
    {
        get => osuIsForeground;
        private set => SetProperty(ref osuIsForeground, value);
    }

    public string OverlayStatusText
    {
        get => overlayStatusText;
        private set => SetProperty(ref overlayStatusText, value);
    }

    public bool HasOverlayStatus => !string.IsNullOrWhiteSpace(OverlayStatusText);

    public string ClientStatusCompactText
    {
        get => clientStatusCompactText;
        private set => SetProperty(ref clientStatusCompactText, value);
    }

    public ClientStatusKind ClientStatusKind
    {
        get => clientStatusKind;
        private set => SetProperty(ref clientStatusKind, value);
    }

    public string EmptyStateTitle
    {
        get => emptyStateTitle;
        private set => SetProperty(ref emptyStateTitle, value);
    }

    public string EmptyStateDescription
    {
        get => emptyStateDescription;
        private set => SetProperty(ref emptyStateDescription, value);
    }

    public void ApplyGameState(GameStateSnapshot snapshot)
    {
        ClientStatusPresentation presentation =
            ClientStatusPresentation.From(snapshot);
        ClientStatusText = presentation.Text;
        ClientStatusCompactText = presentation.CompactText;
        ClientStatusKind = presentation.Kind;
        EmptyStateTitle = presentation.EmptyStateTitle;
        EmptyStateDescription = presentation.EmptyStateDescription;
        OnPropertyChanged(nameof(GameStateStatusText));
    }

    public void ApplyOverlayDisplayCompatibility(
        OverlayDisplayCompatibility compatibility)
    {
        ArgumentNullException.ThrowIfNull(compatibility);
        OverlayDisplayMode = compatibility.DisplayMode;
        OverlaySupported = compatibility.OverlaySupported;
        OverlayVisible = compatibility.OverlayVisible && OsuIsForeground;
        OverlayStatusText = compatibility.StatusText;
        OnPropertyChanged(nameof(HasOverlayStatus));
        OnPropertyChanged(nameof(GameStateStatusText));
    }

    public void ApplyOverlayForegroundState(
        bool isOsuForeground,
        bool overlayVisible)
    {
        OsuIsForeground = isOsuForeground;
        OverlayVisible = overlayVisible;
    }

    public string StatusMapText
    {
        get => statusMapText;
        set => SetProperty(ref statusMapText, value);
    }

    public string StatusAnalysisText
    {
        get => statusAnalysisText;
        set => SetProperty(ref statusAnalysisText, value);
    }

    public string AppVersionText
    {
        get => appVersionText;
        set => SetProperty(ref appVersionText, value);
    }
}
