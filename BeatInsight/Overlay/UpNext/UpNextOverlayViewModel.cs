using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace BeatInsight.Overlay.UpNext;

public sealed class UpNextOverlayViewModel : INotifyPropertyChanged
{
    private string? dominantStructure;
    private string? secondaryStructure;
    private string? countdownText;
    private string? summary;
    private Brush background = Brushes.Transparent;
    private Brush cardBackground = Brushes.Transparent;
    private Brush foreground = Brushes.White;
    private Brush secondaryForeground = Brushes.LightGray;
    private Brush accent = Brushes.Cyan;
    private Brush border = Brushes.Transparent;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string? DominantStructure { get => dominantStructure; private set => Set(ref dominantStructure, value); }
    public string? SecondaryStructure { get => secondaryStructure; private set => Set(ref secondaryStructure, value); }
    public string? CountdownText { get => countdownText; private set => Set(ref countdownText, value); }
    public string? Summary { get => summary; private set => Set(ref summary, value); }
    public Brush Background { get => background; private set => Set(ref background, value); }
    public Brush CardBackground { get => cardBackground; private set => Set(ref cardBackground, value); }
    public Brush Foreground { get => foreground; private set => Set(ref foreground, value); }
    public Brush SecondaryForeground { get => secondaryForeground; private set => Set(ref secondaryForeground, value); }
    public Brush Accent { get => accent; private set => Set(ref accent, value); }
    public Brush Border { get => border; private set => Set(ref border, value); }

    public void Apply(UpNextOverlayState state, OverlayTheme theme)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(theme);

        DominantStructure = state.DominantStructure;
        SecondaryStructure = state.SecondaryStructure;
        CountdownText = state.HasContent ? FormatCountdown(state.TimeUntilStart) : null;
        Summary = state.Summary;

        OverlayTheme fallback = OverlayThemeCatalog.Get(OverlayThemeIds.Default);
        Background = BrushFrom(Token(theme.Tokens.Backgrounds, fallback.Tokens.Backgrounds, "window"), Brushes.Transparent);
        CardBackground = BrushFrom(Token(theme.Tokens.Cards, fallback.Tokens.Cards, "background"), Brushes.Transparent);
        Foreground = BrushFrom(Token(theme.Tokens.Text, fallback.Tokens.Text, "primary"), Brushes.White);
        SecondaryForeground = BrushFrom(Token(theme.Tokens.Text, fallback.Tokens.Text, "secondary"), Brushes.LightGray);
        Accent = BrushFrom(
            Token(theme.Tokens.Colors, fallback.Tokens.Colors, "next.accent") ??
                Token(theme.Tokens.Colors, fallback.Tokens.Colors, "accent.primary"),
            Brushes.Cyan);
        Border = BrushFrom(Token(theme.Tokens.Borders, fallback.Tokens.Borders, "default"), Brushes.Transparent);
    }

    private static string FormatCountdown(TimeSpan timeUntilStart)
    {
        double seconds = Math.Max(0, timeUntilStart.TotalSeconds);
        return seconds >= 10
            ? $"Starts in {Math.Ceiling(seconds).ToString("0", CultureInfo.InvariantCulture)}s"
            : $"Starts in {seconds.ToString("0.0", CultureInfo.InvariantCulture)}s";
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
