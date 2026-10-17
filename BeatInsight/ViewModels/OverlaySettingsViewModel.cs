using BeatInsight.Overlay;
using BeatInsight.Overlay.GameplayHud;
using BeatInsight.Theming;

namespace BeatInsight.ViewModels;

public sealed record OverlayPresetOption(string Id, string DisplayName);
public sealed record ThemeOption(string Id, string DisplayName);

public sealed class OverlaySettingsViewModel : ViewModelBase
{
    private string selectedPresetId = OverlayLayoutIds.Minimal;
    private string selectedAppThemeId = AppThemeIds.Default;
    private string selectedOverlayThemeId = OverlayThemeIds.Default;
    private bool showCurrentSection = true;
    private bool showUpNext = true;
    private bool showDifficultyTimeline;
    private bool showLivePatterns;
    private bool showLiveStats = true;
    private bool showPerformance = true;
    private bool isOverlayEditMode;
    private bool isEditAvailable = true;
    private bool applying;

    public event Action<OverlaySettings>? SettingsChanged;
    public event EventHandler? BeginEditRequested;
    public event EventHandler? SaveEditRequested;
    public event EventHandler? CancelEditRequested;
    public event EventHandler? ResetEditRequested;

    public IReadOnlyList<OverlayPresetOption> Presets { get; } =
    [
        new(OverlayLayoutIds.Minimal, "Minimal"),
        new(OverlayLayoutIds.Analysis, "Analysis"),
        new(OverlayLayoutIds.Timeline, "Timeline")
    ];

    public IReadOnlyList<ThemeOption> AppThemes { get; private set; } =
    [
        new(AppThemeIds.Default, "Default"),
        new(AppThemeIds.Bia, "Bia")
    ];

    public IReadOnlyList<ThemeOption> OverlayThemes { get; private set; } =
    [
        new(OverlayThemeIds.Default, "Default"),
        new(OverlayThemeIds.Bia, "Bia")
    ];

    public IReadOnlyList<ThemeOption> Themes => AppThemes;

    public string SelectedPresetId
    {
        get => selectedPresetId;
        set
        {
            string normalized = OverlaySettings.NormalizePresetId(value);
            if (string.Equals(selectedPresetId, normalized, StringComparison.Ordinal))
                return;

            selectedPresetId = normalized;
            OnPropertyChanged();
            ApplyOptions(OverlaySettings.ForPreset(normalized).GameplayHud);
            RaiseSettingsChanged();
        }
    }

    public string SelectedAppThemeId
    {
        get => selectedAppThemeId;
        set
        {
            string normalized = OverlaySettings.NormalizeAppThemeId(value);
            if (string.Equals(selectedAppThemeId, normalized, StringComparison.Ordinal))
                return;

            selectedAppThemeId = normalized;
            OnPropertyChanged();
            RaiseSettingsChanged();
        }
    }

    public string SelectedOverlayThemeId
    {
        get => selectedOverlayThemeId;
        set
        {
            string normalized = OverlaySettings.NormalizeOverlayThemeId(value);
            if (string.Equals(selectedOverlayThemeId, normalized, StringComparison.Ordinal))
                return;

            selectedOverlayThemeId = normalized;
            OnPropertyChanged();
            RaiseSettingsChanged();
        }
    }

    public bool ShowCurrentSection
    {
        get => showCurrentSection;
        set => SetOption(ref showCurrentSection, value);
    }

    public bool ShowUpNext
    {
        get => showUpNext;
        set => SetOption(ref showUpNext, value);
    }

    public bool ShowDifficultyTimeline
    {
        get => showDifficultyTimeline;
        set => SetOption(ref showDifficultyTimeline, value);
    }

    public bool ShowLivePatterns
    {
        get => showLivePatterns;
        set => SetOption(ref showLivePatterns, value);
    }

    public bool ShowLiveStats
    {
        get => showLiveStats;
        set => SetOption(ref showLiveStats, value);
    }

    public bool ShowPerformance
    {
        get => showPerformance;
        set => SetOption(ref showPerformance, value);
    }

    public bool IsOverlayEditMode
    {
        get => isOverlayEditMode;
        private set
        {
            if (!SetProperty(ref isOverlayEditMode, value))
                return;
            OnPropertyChanged(nameof(IsNormalSettingsEnabled));
        }
    }

    public bool IsEditAvailable
    {
        get => isEditAvailable;
        private set
        {
            if (!SetProperty(ref isEditAvailable, value))
                return;
            OnPropertyChanged(nameof(CanBeginEdit));
        }
    }

    public bool CanBeginEdit => IsEditAvailable && !IsOverlayEditMode;
    public bool IsNormalSettingsEnabled => !IsOverlayEditMode;

    public OverlaySettings Current => new(
        OverlaySettings.CurrentVersion,
        selectedPresetId,
        new GameplayHudDisplayOptions(
            showCurrentSection,
            showUpNext,
            showDifficultyTimeline,
            showLivePatterns,
            showLiveStats,
            showPerformance),
        selectedAppThemeId,
        selectedOverlayThemeId);

    public void Apply(OverlaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        applying = true;
        try
        {
            selectedPresetId = OverlaySettings.NormalizePresetId(settings.PresetId);
            OnPropertyChanged(nameof(SelectedPresetId));
            selectedAppThemeId = OverlaySettings.NormalizeAppThemeId(settings.AppThemeId);
            selectedOverlayThemeId = OverlaySettings.NormalizeOverlayThemeId(settings.OverlayThemeId);
            OnPropertyChanged(nameof(SelectedAppThemeId));
            OnPropertyChanged(nameof(SelectedOverlayThemeId));
            ApplyOptions(settings.GameplayHud);
        }
        finally
        {
            applying = false;
        }
    }

    public void RequestBeginEdit()
    {
        if (CanBeginEdit)
            BeginEditRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestSaveEdit()
    {
        if (IsOverlayEditMode)
            SaveEditRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestCancelEdit()
    {
        if (IsOverlayEditMode)
            CancelEditRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestResetEdit()
    {
        if (IsOverlayEditMode)
            ResetEditRequested?.Invoke(this, EventArgs.Empty);
    }

    public void SetEditState(bool isEditing) => IsOverlayEditMode = isEditing;

    public void SetEditAvailability(bool isAvailable) => IsEditAvailable = isAvailable;

    public void RefreshThemeOptions(IEnumerable<ThemeDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ThemeDefinition[] themes = definitions.ToArray();
        AppThemes = themes
            .Where(theme => theme.HasAppTheme)
            .Select(theme => new ThemeOption(theme.Id, theme.Name))
            .ToArray();
        OverlayThemes = themes
            .Where(theme => theme.HasOverlayTheme)
            .Select(theme => new ThemeOption(theme.Id, theme.Name))
            .ToArray();
        OnPropertyChanged(nameof(AppThemes));
        OnPropertyChanged(nameof(OverlayThemes));
        OnPropertyChanged(nameof(Themes));
    }

    private void ApplyOptions(GameplayHudDisplayOptions options)
    {
        SetProperty(ref showCurrentSection, options.ShowCurrentSection, nameof(ShowCurrentSection));
        SetProperty(ref showUpNext, options.ShowUpNext, nameof(ShowUpNext));
        SetProperty(ref showDifficultyTimeline, options.ShowDifficultyTimeline, nameof(ShowDifficultyTimeline));
        SetProperty(ref showLivePatterns, options.ShowLivePatterns, nameof(ShowLivePatterns));
        SetProperty(ref showLiveStats, options.ShowLiveStats, nameof(ShowLiveStats));
        SetProperty(ref showPerformance, options.ShowPerformance, nameof(ShowPerformance));
    }

    private void SetOption(ref bool field, bool value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName))
            return;

        RaiseSettingsChanged();
    }

    private void RaiseSettingsChanged()
    {
        if (!applying)
            SettingsChanged?.Invoke(Current);
    }
}
