using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using BeatInsight.Integration.Game;

namespace BeatInsight.Overlay.DifficultyTimeline;

public sealed class DifficultyTimelineOverlayViewModel : INotifyPropertyChanged
{
    private DifficultyTimelineModel? model;
    private double markerRatio;
    private int currentSectionIndex = -1;
    private Brush background = Brushes.Transparent;
    private Brush cardBackground = Brushes.Transparent;
    private Brush secondaryForeground = Brushes.LightGray;
    private Brush accent = Brushes.Cyan;
    private Brush border = Brushes.Transparent;
    private OverlayTheme? appliedTheme;

    public event PropertyChangedEventHandler? PropertyChanged;

    public DifficultyTimelineModel? Model { get => model; private set => Set(ref model, value); }
    public double MarkerRatio { get => markerRatio; private set => Set(ref markerRatio, value); }
    public int CurrentSectionIndex { get => currentSectionIndex; private set => Set(ref currentSectionIndex, value); }
    public Brush Background { get => background; private set => Set(ref background, value); }
    public Brush CardBackground { get => cardBackground; private set => Set(ref cardBackground, value); }
    public Brush SecondaryForeground { get => secondaryForeground; private set => Set(ref secondaryForeground, value); }
    public Brush Accent { get => accent; private set => Set(ref accent, value); }
    public Brush Border { get => border; private set => Set(ref border, value); }

    public void Apply(DifficultyTimelineOverlayState state, OverlayTheme theme)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(theme);

        Model = state.HasContent ? state.Model : null;
        MarkerRatio = state.HasContent ? Math.Clamp(state.MarkerRatio, 0, 1) : 0;
        CurrentSectionIndex = state.HasContent ? state.CurrentSectionIndex ?? -1 : -1;

        if (!ReferenceEquals(appliedTheme, theme))
        {
            appliedTheme = theme;
            OverlayTheme fallback = OverlayThemeCatalog.Get(OverlayThemeIds.Default);
            Background = BrushFrom(Token(theme.Tokens.Backgrounds, fallback.Tokens.Backgrounds, "window"), Brushes.Transparent);
            CardBackground = BrushFrom(Token(theme.Tokens.Cards, fallback.Tokens.Cards, "background"), Brushes.Transparent);
            SecondaryForeground = BrushFrom(Token(theme.Tokens.Text, fallback.Tokens.Text, "secondary"), Brushes.LightGray);
            Accent = BrushFrom(Token(theme.Tokens.Colors, fallback.Tokens.Colors, "accent.primary"), Brushes.Cyan);
            Border = BrushFrom(Token(theme.Tokens.Borders, fallback.Tokens.Borders, "default"), Brushes.Transparent);
        }
    }

    private static string? Token(
        IReadOnlyDictionary<string, string> selected,
        IReadOnlyDictionary<string, string> fallback,
        string key) =>
        selected.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback.TryGetValue(key, out value) ? value : null;

    private static Brush BrushFrom(string? value, Brush fallback)
    {
        try
        {
            return value is not null && new BrushConverter().ConvertFromString(value) is Brush brush
                ? brush
                : fallback;
        }
        catch (Exception exception) when (
            exception is FormatException or NotSupportedException or ArgumentException)
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
