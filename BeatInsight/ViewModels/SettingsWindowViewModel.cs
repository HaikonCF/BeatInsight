namespace BeatInsight.ViewModels;

public enum SettingsCategory
{
    General,
    Overlay,
    Appearance,
    Updates,
    OsuApi
}

public sealed class SettingsWindowViewModel : ViewModelBase
{
    private SettingsCategory selectedCategory = SettingsCategory.General;

    public SettingsWindowViewModel(MainWindowViewModel main)
    {
        ArgumentNullException.ThrowIfNull(main);
        Main = main;
    }

    public MainWindowViewModel Main { get; }

    public LibraryPanelViewModel Library => Main.Library;
    public WindowLifecycleSettingsViewModel WindowLifecycle => Main.WindowLifecycle;
    public OverlaySettingsViewModel OverlaySettings => Main.OverlaySettings;
    public CommunityOverlaysViewModel CommunityOverlays => Main.CommunityOverlays;
    public UpdateViewModel Updates => Main.Updates;
    public OsuApiSettingsViewModel OsuApiSettings => Main.OsuApiSettings;
    public string AppVersionText => Main.AppVersionText;

    public SettingsCategory SelectedCategory
    {
        get => selectedCategory;
        set
        {
            if (!SetProperty(ref selectedCategory, value))
                return;

            OnPropertyChanged(nameof(IsGeneralSelected));
            OnPropertyChanged(nameof(IsOverlaySelected));
            OnPropertyChanged(nameof(IsAppearanceSelected));
            OnPropertyChanged(nameof(IsUpdatesSelected));
            OnPropertyChanged(nameof(IsOsuApiSelected));
        }
    }

    public bool IsGeneralSelected => SelectedCategory == SettingsCategory.General;
    public bool IsOverlaySelected => SelectedCategory == SettingsCategory.Overlay;
    public bool IsAppearanceSelected => SelectedCategory == SettingsCategory.Appearance;
    public bool IsUpdatesSelected => SelectedCategory == SettingsCategory.Updates;
    public bool IsOsuApiSelected => SelectedCategory == SettingsCategory.OsuApi;
}
