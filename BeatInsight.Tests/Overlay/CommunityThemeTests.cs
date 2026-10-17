using BeatInsight.Overlay;
using BeatInsight.Overlay.GameplayHud;
using BeatInsight.Theming;
using BeatInsight.ViewModels;
using BeatInsight.Views;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace BeatInsight.Tests.Overlay;

public sealed class CommunityThemeTests : IDisposable
{
    private readonly TemporaryDirectory temporary = new();
    private readonly CommunityThemeRegistry registry;

    public CommunityThemeTests()
    {
        registry = new CommunityThemeRegistry(Path.Combine(temporary.Path, "Themes"));
        CommunityThemeRuntimeCatalog.Replace([]);
    }

    public void Dispose()
    {
        CommunityThemeRuntimeCatalog.Replace([]);
        temporary.Dispose();
    }

    [Fact]
    public void ValidTheme_NormalizesSixDigitColors()
    {
        ThemeOperationResult<ThemeDefinition> result = registry.ValidateAndNormalize(Theme());

        Assert.True(result.Success);
        Assert.Equal("#FF112233", result.Value!.App!["AppBackground"]);
    }

    [Fact]
    public void AppOnlyTheme_IsAccepted()
    {
        ThemeOperationResult<ThemeDefinition> result = registry.ValidateAndNormalize(
            Theme() with { Overlay = null });

        Assert.True(result.Success);
        Assert.True(result.Value!.HasAppTheme);
        Assert.False(result.Value.HasOverlayTheme);
    }

    [Fact]
    public void OverlayOnlyTheme_IsAccepted()
    {
        ThemeOperationResult<ThemeDefinition> result = registry.ValidateAndNormalize(
            Theme() with { App = null });

        Assert.True(result.Success);
        Assert.False(result.Value!.HasAppTheme);
        Assert.True(result.Value.HasOverlayTheme);
    }

    [Fact]
    public void ThemeWithAppAndOverlay_IsAccepted()
    {
        ThemeOperationResult<ThemeDefinition> result = registry.ValidateAndNormalize(Theme());

        Assert.True(result.Success);
        Assert.True(result.Value!.HasAppTheme);
        Assert.True(result.Value.HasOverlayTheme);
    }

    [Theory]
    [InlineData(null, "Name", "1.0.0")]
    [InlineData("local.test", "", "1.0.0")]
    [InlineData("local.test", "Name", "")]
    public void MissingRequiredField_IsRejected(string? id, string name, string version)
    {
        ThemeDefinition theme = Theme() with { Id = id!, Name = name, Version = version };

        Assert.False(registry.ValidateAndNormalize(theme).Success);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("NaN")]
    [InlineData("#GG1122")]
    public void InvalidColor_IsRejected(string color)
    {
        ThemeDefinition theme = Theme() with
        {
            App = new Dictionary<string, string> { ["AppBackground"] = color }
        };

        Assert.False(registry.ValidateAndNormalize(theme).Success);
    }

    [Fact]
    public void UnknownFutureSchema_IsRejected()
    {
        Assert.False(registry.ValidateAndNormalize(Theme() with { SchemaVersion = 2 }).Success);
    }

    [Theory]
    [InlineData("../outside.png")]
    [InlineData("C:\\outside.png")]
    [InlineData("file:///outside.png")]
    [InlineData("assets/theme.xaml")]
    public void InvalidAssetPath_IsRejected(string path)
    {
        ThemeDefinition theme = Theme() with
        {
            Assets = new Dictionary<string, string> { ["logo"] = path }
        };

        Assert.False(registry.ValidateAndNormalize(theme).Success);
    }

    [Fact]
    public void MalformedJson_IsRejectedWithoutThrowing()
    {
        string folder = CreateSource("broken");
        File.WriteAllText(Path.Combine(folder, "theme.json"), "{ nope }");

        ThemeOperationResult<ThemeDefinition> result = registry.LoadAndValidate(
            Path.Combine(folder, "theme.json"));

        Assert.False(result.Success);
        Assert.Equal("Malformed theme JSON.", result.Error);
    }

    [Fact]
    public void SaveAndCatalogLoad_RoundTripUserTheme()
    {
        ThemeOperationResult<ThemeDefinition> saved = registry.Save(Theme());
        ThemeDefinition loaded = registry.LoadCatalog().Single(theme => theme.Id == "local.test-purple");

        Assert.True(saved.Success);
        Assert.False(loaded.BuiltIn);
        Assert.Equal("Test Purple", loaded.Name);
        Assert.NotNull(loaded.RootPath);
    }

    [Fact]
    public void FolderImport_InstallsThemeUnderThemesRoot()
    {
        string source = WritePackage(Theme());

        ThemeOperationResult<ThemeDefinition> result = registry.ImportFolder(source);

        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(registry.ThemesRoot, "local.test-purple", "theme.json")));
    }

    [Fact]
    public void ZipImport_InstallsValidatedTheme()
    {
        string source = WritePackage(Theme());
        string zip = Path.Combine(temporary.Path, "theme.zip");
        ZipFile.CreateFromDirectory(source, zip);

        ThemeOperationResult<ThemeDefinition> result = registry.ImportZip(zip);

        Assert.True(result.Success);
        Assert.Equal("local.test-purple", result.Value!.Id);
    }

    [Fact]
    public void ExportedZip_CanBeReimported()
    {
        ThemeDefinition installed = registry.Save(Theme()).Value!;
        string zip = Path.Combine(temporary.Path, "export.zip");
        Assert.True(registry.Export(installed, zip).Success);
        Assert.True(registry.Delete(installed).Success);

        ThemeOperationResult<ThemeDefinition> imported = registry.ImportZip(zip);

        Assert.True(imported.Success);
        Assert.Equal("local.test-purple", imported.Value!.Id);
    }

    [Fact]
    public void DuplicateId_IsRejectedWithoutReplace()
    {
        string source = WritePackage(Theme());
        Assert.True(registry.ImportFolder(source).Success);

        ThemeOperationResult<ThemeDefinition> duplicate = registry.ImportFolder(source);

        Assert.False(duplicate.Success);
        Assert.Equal("Theme ID is already installed.", duplicate.Error);
    }

    [Fact]
    public void Replace_UsesFullyValidatedSource()
    {
        string first = WritePackage(Theme());
        Assert.True(registry.ImportFolder(first).Success);
        string second = WritePackage(Theme() with { Name = "Replacement" });

        ThemeOperationResult<ThemeDefinition> replaced = registry.ImportFolder(second, replace: true);

        Assert.True(replaced.Success);
        Assert.Equal("Replacement", registry.LoadCatalog().Single(theme => theme.Id == "local.test-purple").Name);
    }

    [Fact]
    public void Import_DoesNotModifySource()
    {
        string source = WritePackage(Theme());
        string before = File.ReadAllText(Path.Combine(source, "theme.json"));

        Assert.True(registry.ImportFolder(source).Success);

        Assert.Equal(before, File.ReadAllText(Path.Combine(source, "theme.json")));
    }

    [Theory]
    [InlineData(AppThemeIds.Default)]
    [InlineData(AppThemeIds.Bia)]
    public void BuiltInIds_AreProtected(string id)
    {
        ThemeDefinition definition = Theme() with { Id = id };

        Assert.False(registry.ValidateAndNormalize(definition).Success);
        Assert.False(registry.Delete(definition with { BuiltIn = true }).Success);
    }

    [Fact]
    public void ZipTraversal_IsRejectedAndCreatesNoOutsideFile()
    {
        string zip = Path.Combine(temporary.Path, "traversal.zip");
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using StreamWriter writer = new(archive.CreateEntry("../outside.png").Open());
            writer.Write("bad");
        }

        ThemeOperationResult<ThemeDefinition> result = registry.ImportZip(zip);

        Assert.False(result.Success);
        Assert.False(File.Exists(Path.Combine(temporary.Path, "outside.png")));
    }

    [Fact]
    public void UnreferencedPackageFile_IsRejected()
    {
        string source = WritePackage(Theme());
        File.WriteAllText(Path.Combine(source, "payload.dll"), "not code");

        Assert.False(registry.ImportFolder(source).Success);
    }

    [Theory]
    [InlineData(AppThemeIds.Default)]
    [InlineData(AppThemeIds.Bia)]
    public void CreateFromBuiltIn_ProducesIndependentLocalTheme(string baseId)
    {
        ThemeDefinition created = registry.CreateFrom(baseId, "My Theme").Value!;

        Assert.StartsWith("local.my-theme", created.Id);
        Assert.False(created.BuiltIn);
        Assert.NotSame(AppThemeCatalog.Get(baseId).Colors, created.App);
        Assert.NotNull(created.Overlay);
    }

    [Fact]
    public void Duplicate_GetsStableIndependentIdAndDictionaries()
    {
        ThemeDefinition source = Theme();
        ThemeDefinition duplicate = registry.Duplicate(source).Value!;
        duplicate.App!["AppBackground"] = "#FFFFFFFF";

        Assert.NotEqual(source.Id, duplicate.Id);
        Assert.Equal("#112233", source.App!["AppBackground"]);
    }

    [Fact]
    public void CustomAppTheme_AppliesThroughCentralManager()
    {
        registry.Save(Theme());
        var resources = new ResourceDictionary();
        var manager = new AppThemeManager();

        string applied = manager.Apply(resources, "local.test-purple");

        Assert.Equal("local.test-purple", applied);
        Assert.Equal(Color.FromArgb(255, 17, 34, 51), ((SolidColorBrush)resources["AppBackgroundBrush"]).Color);
        Assert.Equal(Visibility.Collapsed, resources["AppThemeCharacterVisibility"]);
    }

    [Fact]
    public void CustomOverlayTheme_AppliesWithoutChangingLayoutOrEditSession()
    {
        registry.Save(Theme());
        var engine = new OverlayEngine(
            OverlayLayoutCatalog.Get(OverlayLayoutIds.Analysis),
            OverlayThemeCatalog.Get(OverlayThemeIds.Default));
        Assert.True(engine.BeginEdit());
        OverlayLayout layout = engine.CurrentLayout;
        OverlayLayout? working = engine.WorkingLayout;

        engine.ApplyTheme(OverlayThemeCatalog.Get("local.test-purple"));

        Assert.Equal("local.test-purple", engine.CurrentTheme.Id);
        Assert.Same(layout, engine.CurrentLayout);
        Assert.Same(working, engine.WorkingLayout);
        Assert.True(engine.IsOverlayEditMode);
    }

    [Fact]
    public void CustomThemeIds_RoundTripSettingsWhenCatalogIsLoaded()
    {
        registry.Save(Theme());
        string path = Path.Combine(temporary.Path, "settings.json");
        var store = new OverlaySettingsStore(path);
        OverlaySettings settings = new(
            OverlaySettings.CurrentVersion,
            OverlayLayoutIds.Minimal,
            GameplayHudDisplayOptions.Default,
            "local.test-purple",
            "local.test-purple");

        Assert.True(store.Save(settings));
        Assert.Equal(settings, store.Load());
    }

    [Fact]
    public void MissingSelectedTheme_FallsBackToDefaultWithoutChangingHud()
    {
        CommunityThemeRuntimeCatalog.Replace([]);
        string path = Path.Combine(temporary.Path, "settings.json");
        File.WriteAllText(path, """
            { "version":2, "presetId":"Minimal", "appThemeId":"local.missing",
              "overlayThemeId":"local.missing", "gameplayHud":{"showLiveStats":false} }
            """);

        OverlaySettings loaded = new OverlaySettingsStore(path).Load();

        Assert.Equal(AppThemeIds.Default, loaded.AppThemeId);
        Assert.Equal(OverlayThemeIds.Default, loaded.OverlayThemeId);
        Assert.False(loaded.GameplayHud.ShowLiveStats);
    }

    [Fact]
    public void Delete_RemovesOnlyUserThemeAndRuntimeFallsBack()
    {
        ThemeDefinition installed = registry.Save(Theme()).Value!;

        Assert.True(registry.Delete(installed).Success);

        Assert.False(Directory.Exists(Path.Combine(registry.ThemesRoot, installed.Id)));
        Assert.Equal(AppThemeIds.Default, AppThemeCatalog.Normalize(installed.Id));
    }

    [Fact]
    public void LowContrast_IsWarningOnly()
    {
        ThemeDefinition lowContrast = Theme() with
        {
            App = new Dictionary<string, string>
            {
                ["AppBackground"] = "#101010",
                ["AppTextPrimary"] = "#111111"
            }
        };

        Assert.True(registry.ValidateAndNormalize(lowContrast).Success);
        Assert.True(CommunityThemeRegistry.HasLowContrast(lowContrast));
    }

    [Fact]
    public void MissingAsset_ProducesWarningButThemeRemainsUsable()
    {
        string root = CreateSource("asset-warning");
        ThemeDefinition theme = Theme() with
        {
            Assets = new Dictionary<string, string> { ["logo"] = "assets/missing.png" }
        };

        ThemeOperationResult<ThemeDefinition> result = registry.ValidateAndNormalize(theme, root);

        Assert.True(result.Success);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void InvalidHexEntry_DoesNotProduceAppliedColor()
    {
        int changes = 0;
        var entry = new ThemeColorEntry("AppAccent", "#112233", () => changes++);

        entry.Hex = "not-a-color";

        Assert.False(entry.IsValid);
        Assert.Equal(Brushes.Transparent, entry.Swatch);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void ThemeEditor_SelectionLoadsColorsFromTheSelectedTheme()
    {
        Assert.True(registry.Save(Theme()).Success);
        Exception? failure = null;
        string? selectedAccent = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new CommunityThemesWindow(
                    registry,
                    _ => { },
                    () => { },
                    (_, _, _) => { },
                    _ => ThemeOperationResult<bool>.Ok(true));
                window.SelectedTheme = window.Themes.Single(theme => theme.Id == "local.test-purple");
                selectedAccent = window.Colors.Single(color => color.Token == "AppAccent").Hex;
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
        Assert.Equal("#FFAA44FF", selectedAccent);
    }

    [Fact]
    public void ThemeOptions_AreCapabilitySpecific()
    {
        var viewModel = new OverlaySettingsViewModel();
        viewModel.RefreshThemeOptions([
            BuiltIn(AppThemeIds.Default, "Default"),
            Theme() with { Id = "local.app", Overlay = null },
            Theme() with { Id = "local.overlay", App = null }
        ]);

        Assert.Contains(viewModel.AppThemes, theme => theme.Id == "local.app");
        Assert.DoesNotContain(viewModel.AppThemes, theme => theme.Id == "local.overlay");
        Assert.Contains(viewModel.OverlayThemes, theme => theme.Id == "local.overlay");
        Assert.DoesNotContain(viewModel.OverlayThemes, theme => theme.Id == "local.app");
    }

    [Fact]
    public void CustomThemeIdentity_DoesNotEnableBiaAsset()
    {
        registry.Save(Theme());
        AppTheme custom = AppThemeCatalog.Get("local.test-purple");

        Assert.False(custom.Identity.UsesCharacterAsset);
        Assert.DoesNotContain("Bia", custom.Identity.Label, StringComparison.OrdinalIgnoreCase);
    }

    private string WritePackage(ThemeDefinition definition)
    {
        string folder = CreateSource(Guid.NewGuid().ToString("N"));
        File.WriteAllText(
            Path.Combine(folder, "theme.json"),
            JsonSerializer.Serialize(definition, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));
        return folder;
    }

    private string CreateSource(string name)
    {
        string folder = Path.Combine(temporary.Path, "sources", name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static ThemeDefinition Theme() => new()
    {
        Id = "local.test-purple",
        Name = "Test Purple",
        Author = "BeatInsight Tests",
        Version = "1.0.0",
        App = new Dictionary<string, string>
        {
            ["AppBackground"] = "#112233",
            ["AppSurface"] = "#FF202040",
            ["AppAccent"] = "#FFAA44FF",
            ["AppTextPrimary"] = "#FFFFFFFF"
        },
        Overlay = new Dictionary<string, string>
        {
            ["OverlayBackground"] = "#CC101020",
            ["OverlayBorder"] = "#FFAA44FF",
            ["OverlayAccent"] = "#FFAA44FF",
            ["PerformanceAccent"] = "#FFDD88FF"
        }
    };

    private static ThemeDefinition BuiltIn(string id, string name) => new()
    {
        Id = id,
        Name = name,
        BuiltIn = true,
        App = new Dictionary<string, string> { ["AppBackground"] = "#FF000000" },
        Overlay = new Dictionary<string, string> { ["OverlayBackground"] = "#FF000000" }
    };

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "BeatInsightCommunityThemes",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
