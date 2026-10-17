using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using BeatInsight.Integration.Game;
using BeatInsight.Models.Fusion;
using BeatInsight.Models.Sections;

namespace BeatInsight.Overlay.LivePatterns;

public sealed record LivePatternRowViewModel(
    string DisplayName,
    string Category,
    FusionPatternFamily Family,
    string CoverageText);

public sealed class LivePatternsOverlayViewModel : INotifyPropertyChanged
{
    private IReadOnlyList<LivePatternRowViewModel> patterns = Array.Empty<LivePatternRowViewModel>();
    private bool hasDominantPatterns;
    private string? neutralText;
    private GameplaySectionV2Analysis? appliedSection;
    private Brush background = Brushes.Transparent;
    private Brush cardBackground = Brushes.Transparent;
    private Brush foreground = Brushes.White;
    private Brush secondaryForeground = Brushes.LightGray;
    private Brush accent = Brushes.Cyan;
    private Brush border = Brushes.Transparent;
    private OverlayTheme? appliedTheme;

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<LivePatternRowViewModel> Patterns { get => patterns; private set => Set(ref patterns, value); }
    public bool HasDominantPatterns { get => hasDominantPatterns; private set => Set(ref hasDominantPatterns, value); }
    public string? NeutralText { get => neutralText; private set => Set(ref neutralText, value); }
    public Brush Background { get => background; private set => Set(ref background, value); }
    public Brush CardBackground { get => cardBackground; private set => Set(ref cardBackground, value); }
    public Brush Foreground { get => foreground; private set => Set(ref foreground, value); }
    public Brush SecondaryForeground { get => secondaryForeground; private set => Set(ref secondaryForeground, value); }
    public Brush Accent { get => accent; private set => Set(ref accent, value); }
    public Brush Border { get => border; private set => Set(ref border, value); }

    public void Apply(LivePatternsOverlayState state, OverlayTheme theme)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(theme);

        GameplaySectionV2Analysis? section = state.HasContent
            ? state.Snapshot?.Section
            : null;
        if (!ReferenceEquals(appliedSection, section))
        {
            appliedSection = section;
            Patterns = state.Snapshot?.Patterns
                .Select(item => new LivePatternRowViewModel(
                    item.DisplayName,
                    item.Family.ToString().ToUpperInvariant(),
                    item.Family,
                    FormatCoverage(item.Coverage)))
                .ToArray()
                ?? Array.Empty<LivePatternRowViewModel>();
        }

        HasDominantPatterns = state.HasDominantPatterns;
        NeutralText = state.HasContent && !state.HasDominantPatterns
            ? "No dominant pattern"
            : null;

        if (!ReferenceEquals(appliedTheme, theme))
        {
            appliedTheme = theme;
            OverlayTheme fallback = OverlayThemeCatalog.Get(OverlayThemeIds.Default);
            Background = BrushFrom(Token(theme.Tokens.Backgrounds, fallback.Tokens.Backgrounds, "window"), Brushes.Transparent);
            CardBackground = BrushFrom(Token(theme.Tokens.Cards, fallback.Tokens.Cards, "background"), Brushes.Transparent);
            Foreground = BrushFrom(Token(theme.Tokens.Text, fallback.Tokens.Text, "primary"), Brushes.White);
            SecondaryForeground = BrushFrom(Token(theme.Tokens.Text, fallback.Tokens.Text, "secondary"), Brushes.LightGray);
            Accent = BrushFrom(Token(theme.Tokens.Colors, fallback.Tokens.Colors, "accent.primary"), Brushes.Cyan);
            Border = BrushFrom(Token(theme.Tokens.Borders, fallback.Tokens.Borders, "default"), Brushes.Transparent);
        }
    }

    private static string FormatCoverage(double coverage) =>
        (Math.Clamp(coverage, 0, 1) * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";

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
