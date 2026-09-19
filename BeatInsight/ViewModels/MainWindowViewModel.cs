namespace BeatInsight.ViewModels;

public enum NavigationTab
{
    Overview,
    Skills,
    Sections,
    Identity,
    Difficulty
}

public sealed class MainWindowViewModel : ViewModelBase
{
    private NavigationTab selectedTab = NavigationTab.Overview;
    private string statusConnectionText = "● En attente de Tosu...";
    private bool isConnected;
    private string statusMapText = "Aucune beatmap chargée";
    private string statusAnalysisText = "Prêt";
    private string appVersionText = "BeatInsight v0.1.0-v2";

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
                OnPropertyChanged(nameof(IsIdentityTabActive));
                OnPropertyChanged(nameof(IsDifficultyTabActive));
            }
        }
    }

    public bool IsOverviewTabActive => SelectedTab == NavigationTab.Overview;
    public bool IsSkillsTabActive => SelectedTab == NavigationTab.Skills;
    public bool IsSectionsTabActive => SelectedTab == NavigationTab.Sections;
    public bool IsIdentityTabActive => SelectedTab == NavigationTab.Identity;
    public bool IsDifficultyTabActive => SelectedTab == NavigationTab.Difficulty;

    public OverviewViewModel Overview { get; } = new();
    public LibraryPanelViewModel Library { get; } = new();

    public string StatusConnectionText
    {
        get => statusConnectionText;
        set => SetProperty(ref statusConnectionText, value);
    }

    public bool IsConnected
    {
        get => isConnected;
        set => SetProperty(ref isConnected, value);
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
