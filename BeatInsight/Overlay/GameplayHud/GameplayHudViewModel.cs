using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using BeatInsight.Overlay.CurrentSection;
using BeatInsight.Overlay.DifficultyTimeline;
using BeatInsight.Overlay.LivePatterns;
using BeatInsight.Overlay.UpNext;

namespace BeatInsight.Overlay.GameplayHud;

public sealed class GameplayHudViewModel : INotifyPropertyChanged
{
    private bool isCurrentSectionVisible;
    private bool isUpNextVisible;
    private bool isDifficultyTimelineVisible;
    private bool isLivePatternsVisible;
    private bool isLiveStatsVisible;
    private bool isPerformanceVisible;
    private Brush danger = Brushes.IndianRed;
    private Brush warning = Brushes.Gold;
    private Brush accentSecondary = Brushes.MediumPurple;
    private Brush liveStatsAccent = Brushes.MediumPurple;
    private Brush performanceAccent = Brushes.MediumPurple;

    public event PropertyChangedEventHandler? PropertyChanged;

    public CurrentSectionOverlayViewModel CurrentSection { get; } = new();
    public UpNextOverlayViewModel UpNext { get; } = new();
    public DifficultyTimelineOverlayViewModel DifficultyTimeline { get; } = new();
    public LivePatternsOverlayViewModel LivePatterns { get; } = new();
    public LiveGameplayStatsOverlayViewModel LiveStats { get; } = new();
    public LivePerformanceOverlayViewModel Performance { get; } = new();

    public bool IsCurrentSectionVisible
    {
        get => isCurrentSectionVisible;
        private set => Set(ref isCurrentSectionVisible, value);
    }

    public bool IsUpNextVisible
    {
        get => isUpNextVisible;
        private set => Set(ref isUpNextVisible, value);
    }

    public bool IsDifficultyTimelineVisible
    {
        get => isDifficultyTimelineVisible;
        private set => Set(ref isDifficultyTimelineVisible, value);
    }

    public bool IsLivePatternsVisible
    {
        get => isLivePatternsVisible;
        private set => Set(ref isLivePatternsVisible, value);
    }

    public bool IsLiveStatsVisible
    {
        get => isLiveStatsVisible;
        private set => Set(ref isLiveStatsVisible, value);
    }

    public bool IsPerformanceVisible
    {
        get => isPerformanceVisible;
        private set => Set(ref isPerformanceVisible, value);
    }

    public Brush Danger
    {
        get => danger;
        private set => Set(ref danger, value);
    }

    public Brush Warning
    {
        get => warning;
        private set => Set(ref warning, value);
    }

    public Brush AccentSecondary
    {
        get => accentSecondary;
        private set => Set(ref accentSecondary, value);
    }

    public Brush LiveStatsAccent
    {
        get => liveStatsAccent;
        private set => Set(ref liveStatsAccent, value);
    }

    public Brush PerformanceAccent
    {
        get => performanceAccent;
        private set => Set(ref performanceAccent, value);
    }

    public void Apply(GameplayHudOverlayState state, OverlayTheme theme)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(theme);

        OverlayTheme fallback = OverlayThemeCatalog.Get(OverlayThemeIds.Default);
        Danger = BrushFrom(Token(theme.Tokens.Colors, fallback.Tokens.Colors, "status.danger"), Brushes.IndianRed);
        Warning = BrushFrom(Token(theme.Tokens.Colors, fallback.Tokens.Colors, "status.warning"), Brushes.Gold);
        AccentSecondary = BrushFrom(Token(theme.Tokens.Colors, fallback.Tokens.Colors, "accent.secondary"), Brushes.MediumPurple);
        LiveStatsAccent = BrushFrom(
            Token(theme.Tokens.Colors, fallback.Tokens.Colors, "liveStats.accent") ??
                Token(theme.Tokens.Colors, fallback.Tokens.Colors, "accent.secondary"),
            AccentSecondary);
        PerformanceAccent = BrushFrom(
            Token(theme.Tokens.Colors, fallback.Tokens.Colors, "performance.accent") ??
                Token(theme.Tokens.Colors, fallback.Tokens.Colors, "accent.secondary"),
            AccentSecondary);

        CurrentSection.Apply(state.CurrentSection, theme);
        UpNext.Apply(state.UpNext, theme);
        DifficultyTimeline.Apply(state.DifficultyTimeline, theme);
        LivePatterns.Apply(state.LivePatterns, theme);
        LiveStats.Apply(state.LiveStats);
        Performance.Apply(state.Performance);

        IsCurrentSectionVisible = state.IsCurrentSectionVisible;
        IsUpNextVisible = state.IsUpNextVisible;
        IsDifficultyTimelineVisible = state.IsDifficultyTimelineVisible;
        IsLivePatternsVisible = state.IsLivePatternsVisible;
        IsLiveStatsVisible = state.IsLiveStatsVisible;
        IsPerformanceVisible = state.IsPerformanceVisible;
    }

    private static string? Token(
        IReadOnlyDictionary<string, string> selected,
        IReadOnlyDictionary<string, string> fallback,
        string key) => selected.TryGetValue(key, out string? value)
            ? value
            : fallback.TryGetValue(key, out string? defaultValue) ? defaultValue : null;

    private static Brush BrushFrom(string? value, Brush fallback)
    {
        try
        {
            return value is null ? fallback : (Brush)new BrushConverter().ConvertFromString(value)!;
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
