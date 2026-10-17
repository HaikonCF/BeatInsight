using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace BeatInsight.Overlay.MapOverview;

public sealed class MapOverviewOverlayViewModel : INotifyPropertyChanged
{
    private string? title;
    private string? difficulty;
    private string? artist;
    private string? mapper;
    private string? starRating;
    private string? beatInsightRating;
    private string? identity;
    private string? gameplayDetails;
    private Brush background = Brushes.Transparent;
    private Brush cardBackground = Brushes.Transparent;
    private Brush foreground = Brushes.White;
    private Brush secondaryForeground = Brushes.LightGray;
    private Brush accent = Brushes.Cyan;
    private Brush border = Brushes.Transparent;
    private Brush editBorder = Brushes.Cyan;
    private Brush editHandle = Brushes.Cyan;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string? Title { get => title; private set => Set(ref title, value); }
    public string? Difficulty { get => difficulty; private set => Set(ref difficulty, value); }
    public string? Artist { get => artist; private set => Set(ref artist, value); }
    public string? Mapper { get => mapper; private set => Set(ref mapper, value); }
    public string? StarRating { get => starRating; private set => Set(ref starRating, value); }
    public string? BeatInsightRating { get => beatInsightRating; private set => Set(ref beatInsightRating, value); }
    public string? Identity { get => identity; private set => Set(ref identity, value); }
    public string? GameplayDetails { get => gameplayDetails; private set => Set(ref gameplayDetails, value); }
    public Brush Background { get => background; private set => Set(ref background, value); }
    public Brush CardBackground { get => cardBackground; private set => Set(ref cardBackground, value); }
    public Brush Foreground { get => foreground; private set => Set(ref foreground, value); }
    public Brush SecondaryForeground { get => secondaryForeground; private set => Set(ref secondaryForeground, value); }
    public Brush Accent { get => accent; private set => Set(ref accent, value); }
    public Brush Border { get => border; private set => Set(ref border, value); }
    public Brush EditBorder { get => editBorder; private set => Set(ref editBorder, value); }
    public Brush EditHandle { get => editHandle; private set => Set(ref editHandle, value); }

    public void Apply(MapOverviewOverlayState state, OverlayTheme theme)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(theme);

        Title = state.Title;
        Difficulty = state.Difficulty;
        Artist = state.Artist;
        Mapper = state.Mapper;
        StarRating = Format(state.StarRating, "0.00", "★");
        BeatInsightRating = Format(state.BeatInsightRating, "0.00");
        Identity = state.GameplayIdentity;
        GameplayDetails = BuildGameplayDetails(state);

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

    private static string? BuildGameplayDetails(MapOverviewOverlayState state)
    {
        var values = new List<string>();
        if (state.Bpm is { } bpm)
            values.Add($"{bpm:0.##} BPM");
        if (state.Length is { } length)
            values.Add(length.ToString(@"m\:ss", CultureInfo.InvariantCulture));
        if (state.MaxCombo is { } maxCombo)
            values.Add($"{maxCombo:N0}x");
        if (state.ObjectCount is { } objectCount)
            values.Add($"{objectCount:N0} objects");
        if (state.ApproachRate is { } ar)
            values.Add($"AR {ar:0.##}");
        if (state.OverallDifficulty is { } od)
            values.Add($"OD {od:0.##}");
        if (state.CircleSize is { } cs)
            values.Add($"CS {cs:0.##}");
        if (state.HpDrain is { } hp)
            values.Add($"HP {hp:0.##}");
        return values.Count == 0 ? null : string.Join("  •  ", values);
    }

    private static string? Format(double? value, string format, string suffix = "") =>
        value is { } number
            ? number.ToString(format, CultureInfo.InvariantCulture) + suffix
            : null;

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
        catch (FormatException)
        {
            return fallback;
        }
        catch (NotSupportedException)
        {
            return fallback;
        }
        catch (ArgumentException)
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
