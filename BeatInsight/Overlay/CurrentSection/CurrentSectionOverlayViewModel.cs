using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace BeatInsight.Overlay.CurrentSection;

public sealed class CurrentSectionOverlayViewModel : INotifyPropertyChanged
{
    private string? dominantStructure;
    private string? secondaryStructure;
    private string? progressText;
    private string? remainingText;
    private string? summary;
    private double progressValue;
    private Brush background = Brushes.Transparent;
    private Brush cardBackground = Brushes.Transparent;
    private Brush foreground = Brushes.White;
    private Brush secondaryForeground = Brushes.LightGray;
    private Brush accent = Brushes.Cyan;
    private Brush border = Brushes.Transparent;
    private Brush editBorder = Brushes.Cyan;
    private Brush editHandle = Brushes.Cyan;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string? DominantStructure
    {
        get => dominantStructure;
        private set => Set(ref dominantStructure, value);
    }

    public string? SecondaryStructure
    {
        get => secondaryStructure;
        private set => Set(ref secondaryStructure, value);
    }

    public string? ProgressText
    {
        get => progressText;
        private set => Set(ref progressText, value);
    }

    public string? RemainingText
    {
        get => remainingText;
        private set => Set(ref remainingText, value);
    }

    public string? Summary
    {
        get => summary;
        private set => Set(ref summary, value);
    }

    public double ProgressValue
    {
        get => progressValue;
        private set => Set(ref progressValue, value);
    }

    public Brush Background { get => background; private set => Set(ref background, value); }
    public Brush CardBackground { get => cardBackground; private set => Set(ref cardBackground, value); }
    public Brush Foreground { get => foreground; private set => Set(ref foreground, value); }
    public Brush SecondaryForeground { get => secondaryForeground; private set => Set(ref secondaryForeground, value); }
    public Brush Accent { get => accent; private set => Set(ref accent, value); }
    public Brush Border { get => border; private set => Set(ref border, value); }
    public Brush EditBorder { get => editBorder; private set => Set(ref editBorder, value); }
    public Brush EditHandle { get => editHandle; private set => Set(ref editHandle, value); }

    public void Apply(CurrentSectionOverlayState state, OverlayTheme theme)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(theme);

        DominantStructure = state.DominantStructure;
        SecondaryStructure = state.SecondaryStructure;
        ProgressValue = state.HasContent
            ? Math.Clamp(state.LocalProgress * 100, 0, 100)
            : 0;
        ProgressText = state.HasContent
            ? ProgressValue.ToString("0", CultureInfo.InvariantCulture) + "%"
            : null;
        RemainingText = state.HasContent
            ? FormatRemaining(state.TimeRemaining)
            : null;
        Summary = state.Summary;

        OverlayTheme fallback = OverlayThemeCatalog.Get(OverlayThemeIds.Default);
        Background = BrushFrom(
            Token(theme.Tokens.Backgrounds, fallback.Tokens.Backgrounds, "window"),
            Brushes.Transparent);
        CardBackground = BrushFrom(
            Token(theme.Tokens.Cards, fallback.Tokens.Cards, "background"),
            Brushes.Transparent);
        Foreground = BrushFrom(
            Token(theme.Tokens.Text, fallback.Tokens.Text, "primary"),
            Brushes.White);
        SecondaryForeground = BrushFrom(
            Token(theme.Tokens.Text, fallback.Tokens.Text, "secondary"),
            Brushes.LightGray);
        Accent = BrushFrom(
            Token(theme.Tokens.Colors, fallback.Tokens.Colors, "current.accent") ??
                Token(theme.Tokens.Colors, fallback.Tokens.Colors, "accent.primary"),
            Brushes.Cyan);
        Border = BrushFrom(
            Token(theme.Tokens.Borders, fallback.Tokens.Borders, "default"),
            Brushes.Transparent);
        EditBorder = BrushFrom(
            Token(theme.Tokens.Borders, fallback.Tokens.Borders, "edit") ??
                Token(theme.Tokens.Borders, fallback.Tokens.Borders, "default"),
            Accent);
        EditHandle = BrushFrom(
            Token(theme.Tokens.Colors, fallback.Tokens.Colors, "edit.handle") ??
                Token(theme.Tokens.Colors, fallback.Tokens.Colors, "accent.primary"),
            Accent);
    }

    private static string FormatRemaining(TimeSpan remaining) =>
        remaining.TotalSeconds >= 60
            ? $"{(int)remaining.TotalMinutes}:{remaining.Seconds:00} remaining"
            : remaining.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s remaining";

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
