using BeatInsight.Theming;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace BeatInsight.Views;

public partial class CommunityThemesWindow : Window, INotifyPropertyChanged
{
    private readonly CommunityThemeRegistry registry;
    private readonly Action<ThemeDefinition> preview;
    private readonly Action restorePreview;
    private readonly Action<string, bool, bool> apply;
    private readonly Func<ThemeDefinition, ThemeOperationResult<bool>> delete;
    private ThemeDefinition? selectedTheme;
    private string editName = string.Empty;
    private string editAuthor = string.Empty;
    private bool appEnabled;
    private bool overlayEnabled;
    private string statusText = "Select or create a theme.";
    private bool closingNormally;

    public CommunityThemesWindow(
        CommunityThemeRegistry registry,
        Action<ThemeDefinition> preview,
        Action restorePreview,
        Action<string, bool, bool> apply,
        Func<ThemeDefinition, ThemeOperationResult<bool>> delete)
    {
        this.registry = registry;
        this.preview = preview;
        this.restorePreview = restorePreview;
        this.apply = apply;
        this.delete = delete;
        InitializeComponent();
        DataContext = this;
        Reload();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<ThemeDefinition> Themes { get; } = [];
    public ObservableCollection<ThemeColorEntry> Colors { get; } = [];

    public ThemeDefinition? SelectedTheme
    {
        get => selectedTheme;
        set
        {
            if (ReferenceEquals(selectedTheme, value))
                return;
            selectedTheme = value;
            OnPropertyChanged();
            LoadEditor(value);
        }
    }

    public string EditName { get => editName; set { editName = value; OnPropertyChanged(); PreviewWorking(); } }
    public string EditAuthor { get => editAuthor; set { editAuthor = value; OnPropertyChanged(); PreviewWorking(); } }
    public bool AppEnabled { get => appEnabled; set { appEnabled = value; OnPropertyChanged(); RebuildColors(); } }
    public bool OverlayEnabled { get => overlayEnabled; set { overlayEnabled = value; OnPropertyChanged(); RebuildColors(); } }
    public string StatusText { get => statusText; private set { statusText = value; OnPropertyChanged(); } }
    public string ThemeIdentityText => SelectedTheme is null ? string.Empty : $"ID: {SelectedTheme.Id}{(SelectedTheme.BuiltIn ? "  • built-in" : string.Empty)}";
    public Visibility LowContrastVisibility => WorkingDefinition() is { } theme && CommunityThemeRegistry.HasLowContrast(theme)
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Brush PreviewBackground => BrushFor("AppBackground", Brushes.Black);
    public Brush PreviewBorder => BrushFor("AppBorder", Brushes.Gray);
    public Brush PreviewPrimary => BrushFor("AppTextPrimary", Brushes.White);
    public Brush PreviewSecondary => BrushFor("AppTextSecondary", Brushes.LightGray);
    public Brush PreviewAccent => BrushFor("AppAccent", Brushes.DeepPink);
    public Brush PreviewDanger => BrushFor("AppDanger", Brushes.IndianRed);
    public Brush OverlayPreviewBackground => BrushFor("OverlayBackground", Brushes.Black);
    public Brush OverlayPreviewBorder => BrushFor("OverlayBorder", Brushes.Cyan);
    public Brush OverlayPreviewPrimary => BrushFor("OverlayTextPrimary", Brushes.White);
    public Brush OverlayPreviewAccent => BrushFor("PerformanceAccent", Brushes.MediumPurple);

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!closingNormally)
            restorePreview();
        base.OnClosing(e);
    }

    private void Reload(string? selectId = null)
    {
        string? desired = selectId ?? SelectedTheme?.Id;
        Themes.Clear();
        foreach (ThemeDefinition theme in registry.RefreshRuntimeCatalog())
            Themes.Add(theme);
        SelectedTheme = Themes.FirstOrDefault(theme => string.Equals(theme.Id, desired, StringComparison.Ordinal))
            ?? Themes.FirstOrDefault();
    }

    private void LoadEditor(ThemeDefinition? theme)
    {
        restorePreview();
        if (theme is null)
            return;
        editName = theme.Name;
        editAuthor = theme.Author ?? string.Empty;
        appEnabled = theme.HasAppTheme;
        overlayEnabled = theme.HasOverlayTheme;
        OnPropertyChanged(nameof(EditName));
        OnPropertyChanged(nameof(EditAuthor));
        OnPropertyChanged(nameof(AppEnabled));
        OnPropertyChanged(nameof(OverlayEnabled));
        OnPropertyChanged(nameof(ThemeIdentityText));
        Colors.Clear();
        RebuildColors();
        StatusText = theme.BuiltIn ? "Built-in themes are immutable. Duplicate to edit." : "Ready.";
    }

    private void RebuildColors()
    {
        if (SelectedTheme is null)
            return;
        Dictionary<string, string> current = Colors.ToDictionary(entry => entry.Token, entry => entry.Hex, StringComparer.Ordinal);
        Colors.Clear();
        if (AppEnabled)
        {
            foreach (string token in CommunityThemeContract.AppTokenMap.Keys)
                AddColor(token, current, SelectedTheme.App);
        }
        if (OverlayEnabled)
        {
            foreach (string token in CommunityThemeContract.OverlayTokens)
                AddColor(token, current, SelectedTheme.Overlay);
        }
        RefreshPreviewProperties();
        PreviewWorking();
    }

    private void AddColor(
        string token,
        IReadOnlyDictionary<string, string> current,
        IReadOnlyDictionary<string, string>? source)
    {
        string value = current.GetValueOrDefault(token)
            ?? source?.GetValueOrDefault(token)
            ?? "#FFFFFFFF";
        Colors.Add(new ThemeColorEntry(token, value, ColorChanged));
    }

    private void ColorChanged()
    {
        RefreshPreviewProperties();
        PreviewWorking();
    }

    private void PreviewWorking()
    {
        ThemeDefinition? working = WorkingDefinition();
        if (working is null || working.BuiltIn)
            return;
        if (Colors.Any(color => !color.IsValid))
        {
            StatusText = "Invalid hex value. Use #RRGGBB or #AARRGGBB.";
            return;
        }
        ThemeOperationResult<ThemeDefinition> validation = registry.ValidateAndNormalize(working);
        if (validation.Success && validation.Value is not null)
        {
            preview(validation.Value);
            StatusText = "Preview active.";
        }
        else if (!validation.Success)
        {
            StatusText = validation.Error ?? "Theme is invalid.";
        }
    }

    private ThemeDefinition? WorkingDefinition()
    {
        if (SelectedTheme is null)
            return null;
        Dictionary<string, string>? app = AppEnabled
            ? Colors.Where(entry => CommunityThemeContract.AppTokenMap.ContainsKey(entry.Token))
                .ToDictionary(entry => entry.Token, entry => entry.Hex, StringComparer.Ordinal)
            : null;
        Dictionary<string, string>? overlay = OverlayEnabled
            ? Colors.Where(entry => CommunityThemeContract.OverlayTokens.Contains(entry.Token))
                .ToDictionary(entry => entry.Token, entry => entry.Hex, StringComparer.Ordinal)
            : null;
        return SelectedTheme with
        {
            Name = EditName,
            Author = EditAuthor,
            App = app,
            Overlay = overlay,
            BuiltIn = SelectedTheme.BuiltIn
        };
    }

    private void NewDefault_Click(object sender, RoutedEventArgs e) => Create(AppThemeIds.Default);
    private void NewBia_Click(object sender, RoutedEventArgs e) => Create(AppThemeIds.Bia);

    private void Create(string baseId)
    {
        ThemeOperationResult<ThemeDefinition> result = registry.CreateFrom(baseId, "New Theme");
        if (result.Value is null)
            return;
        Themes.Add(result.Value);
        SelectedTheme = result.Value;
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTheme is null)
            return;
        ThemeOperationResult<ThemeDefinition> result = registry.Duplicate(SelectedTheme);
        if (result.Value is null)
            return;
        Themes.Add(result.Value);
        SelectedTheme = result.Value;
    }

    private void PickColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ThemeColorEntry entry })
            return;
        using var picker = new Forms.ColorDialog { FullOpen = true };
        if (entry.TryGetDrawingColor(out System.Drawing.Color color))
            picker.Color = color;
        if (picker.ShowDialog() == Forms.DialogResult.OK)
            entry.Hex = $"#{picker.Color.A:X2}{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ThemeDefinition? working = WorkingDefinition();
        if (working is null)
            return;
        if (working.BuiltIn)
        {
            StatusText = "Built-in themes cannot be edited. Duplicate it first.";
            return;
        }
        ThemeOperationResult<ThemeDefinition> result = registry.Save(working);
        StatusText = result.Success ? "Theme saved." : result.Error ?? "Save failed.";
        if (result.Success && result.Value is not null)
            Reload(result.Value.Id);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => LoadEditor(SelectedTheme);

    private void ApplyApp_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTheme is { BuiltIn: false, RootPath: null })
        {
            StatusText = "Save the theme before applying it.";
            return;
        }
        if (SelectedTheme?.HasAppTheme == true)
            apply(SelectedTheme.Id, true, false);
    }

    private void ApplyOverlay_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTheme is { BuiltIn: false, RootPath: null })
        {
            StatusText = "Save the theme before applying it.";
            return;
        }
        if (SelectedTheme?.HasOverlayTheme == true)
            apply(SelectedTheme.Id, false, true);
    }

    private void ImportZip_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "BeatInsight theme (*.zip)|*.zip", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true)
            return;
        Import(replace => registry.ImportZip(dialog.FileName, replace));
    }

    private void ImportFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select a BeatInsight theme folder" };
        if (dialog.ShowDialog(this) != true)
            return;
        Import(replace => registry.ImportFolder(dialog.FolderName, replace));
    }

    private void Import(Func<bool, ThemeOperationResult<ThemeDefinition>> import)
    {
        ThemeOperationResult<ThemeDefinition> result = import(false);
        if (!result.Success && result.Error == "Theme ID is already installed." &&
            MessageBox.Show(this, "This theme ID already exists. Replace it?", "Community Themes",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            result = import(true);
        }
        StatusText = result.Success ? "Theme imported." : result.Error ?? "Import failed.";
        if (result.Success && result.Value is not null)
            Reload(result.Value.Id);
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTheme is null || SelectedTheme.BuiltIn)
        {
            StatusText = "Duplicate a built-in theme before exporting.";
            return;
        }
        var dialog = new SaveFileDialog
        {
            Filter = "BeatInsight theme (*.zip)|*.zip",
            FileName = SelectedTheme.Id + ".zip",
            AddExtension = true,
            DefaultExt = ".zip"
        };
        if (dialog.ShowDialog(this) != true)
            return;
        ThemeOperationResult<string> result = registry.Export(SelectedTheme, dialog.FileName);
        StatusText = result.Success ? "Theme exported." : result.Error ?? "Export failed.";
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTheme is null)
            return;
        if (SelectedTheme.BuiltIn)
        {
            StatusText = "Built-in themes cannot be deleted.";
            return;
        }
        if (MessageBox.Show(this, $"Delete '{SelectedTheme.Name}'?", "Community Themes",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        ThemeOperationResult<bool> result = delete(SelectedTheme);
        StatusText = result.Success ? "Theme deleted." : result.Error ?? "Delete failed.";
        if (result.Success)
            Reload();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        restorePreview();
        closingNormally = true;
        Close();
    }

    private Brush BrushFor(string token, Brush fallback)
    {
        ThemeColorEntry? entry = Colors.FirstOrDefault(color => color.Token == token);
        return entry?.Swatch ?? fallback;
    }

    private void RefreshPreviewProperties()
    {
        foreach (string property in new[]
        {
            nameof(PreviewBackground), nameof(PreviewBorder), nameof(PreviewPrimary),
            nameof(PreviewSecondary), nameof(PreviewAccent), nameof(PreviewDanger),
            nameof(OverlayPreviewBackground), nameof(OverlayPreviewBorder),
            nameof(OverlayPreviewPrimary), nameof(OverlayPreviewAccent),
            nameof(LowContrastVisibility)
        })
            OnPropertyChanged(property);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ThemeColorEntry : INotifyPropertyChanged
{
    private readonly Action changed;
    private string hex;

    public ThemeColorEntry(string token, string hex, Action changed)
    {
        Token = token;
        this.hex = hex;
        this.changed = changed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Token { get; }
    public bool IsValid => CommunityThemeContract.TryNormalizeColor(hex, out _);

    public string Hex
    {
        get => hex;
        set
        {
            if (hex == value)
                return;
            hex = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Hex)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Swatch)));
            changed();
        }
    }

    public Brush Swatch
    {
        get
        {
            if (!CommunityThemeContract.TryNormalizeColor(hex, out string normalized))
                return Brushes.Transparent;
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(normalized));
        }
    }

    public bool TryGetDrawingColor(out System.Drawing.Color color)
    {
        color = System.Drawing.Color.Black;
        if (!CommunityThemeContract.TryNormalizeColor(hex, out string normalized))
            return false;
        color = System.Drawing.Color.FromArgb(
            Convert.ToByte(normalized.Substring(1, 2), 16),
            Convert.ToByte(normalized.Substring(3, 2), 16),
            Convert.ToByte(normalized.Substring(5, 2), 16),
            Convert.ToByte(normalized.Substring(7, 2), 16));
        return true;
    }
}
