using BeatInsight.Converters;
using BeatInsight.Overlay;
using BeatInsight.Overlay.GameplayHud;
using BeatInsight.Theming;
using BeatInsight.ViewModels;
using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace BeatInsight.Tests.Overlay;

public sealed class ThemeEngineTests
{
    public static TheoryData<string, string> AppTokens => new()
    {
        { AppThemeIds.Default, "background" },
        { AppThemeIds.Default, "surface" },
        { AppThemeIds.Default, "card" },
        { AppThemeIds.Default, "border" },
        { AppThemeIds.Default, "text.primary" },
        { AppThemeIds.Default, "text.secondary" },
        { AppThemeIds.Default, "accent.primary" },
        { AppThemeIds.Bia, "background" },
        { AppThemeIds.Bia, "surface" },
        { AppThemeIds.Bia, "card" },
        { AppThemeIds.Bia, "border" },
        { AppThemeIds.Bia, "text.primary" },
        { AppThemeIds.Bia, "text.secondary" },
        { AppThemeIds.Bia, "accent.primary" }
    };

    public static TheoryData<string, string, string> OverlayTokens => new()
    {
        { OverlayThemeIds.Default, "colors", "accent.primary" },
        { OverlayThemeIds.Default, "colors", "accent.secondary" },
        { OverlayThemeIds.Default, "colors", "status.danger" },
        { OverlayThemeIds.Default, "colors", "status.warning" },
        { OverlayThemeIds.Default, "text", "primary" },
        { OverlayThemeIds.Default, "cards", "background" },
        { OverlayThemeIds.Default, "borders", "default" },
        { OverlayThemeIds.Bia, "colors", "accent.primary" },
        { OverlayThemeIds.Bia, "colors", "accent.secondary" },
        { OverlayThemeIds.Bia, "colors", "status.danger" },
        { OverlayThemeIds.Bia, "colors", "status.warning" },
        { OverlayThemeIds.Bia, "text", "primary" },
        { OverlayThemeIds.Bia, "cards", "background" },
        { OverlayThemeIds.Bia, "borders", "default" }
    };

    [Theory]
    [MemberData(nameof(AppTokens))]
    public void AppThemesContainRequiredTokens(string themeId, string token)
    {
        Assert.False(string.IsNullOrWhiteSpace(AppThemeCatalog.Get(themeId).Colors[token]));
    }

    [Theory]
    [MemberData(nameof(OverlayTokens))]
    public void OverlayThemesContainRequiredTokens(string themeId, string group, string token)
    {
        OverlayThemeTokens tokens = OverlayThemeCatalog.Get(themeId).Tokens;
        IReadOnlyDictionary<string, string> values = group switch
        {
            "colors" => tokens.Colors,
            "text" => tokens.Text,
            "cards" => tokens.Cards,
            "borders" => tokens.Borders,
            _ => throw new ArgumentOutOfRangeException(nameof(group))
        };
        Assert.False(string.IsNullOrWhiteSpace(values[token]));
    }

    [Fact]
    public void UnknownThemeIdsNormalizeIndependentlyToDefault()
    {
        Assert.Equal(AppThemeIds.Default, AppThemeCatalog.Normalize("future-app"));
        Assert.Equal(OverlayThemeIds.Default, OverlayThemeCatalog.Normalize("future-overlay"));
        Assert.Equal(AppThemeIds.Bia, AppThemeCatalog.Normalize(AppThemeIds.Bia));
        Assert.Equal(OverlayThemeIds.Bia, OverlayThemeCatalog.Normalize(OverlayThemeIds.Bia));
    }

    [Fact]
    public void AppThemeManagerSwitchesExistingResourcesBothWays()
    {
        var resources = new ResourceDictionary
        {
            ["AppBackgroundBrush"] = new SolidColorBrush(Colors.Black),
            ["AccentMagentaBrush"] = new SolidColorBrush(Colors.Black)
        };
        var manager = new AppThemeManager();

        Assert.Equal(AppThemeIds.Bia, manager.Apply(resources, AppThemeIds.Bia));
        Color biaBackground = ((SolidColorBrush)resources["AppBackgroundBrush"]).Color;
        Color biaAccent = ((SolidColorBrush)resources["AccentMagentaBrush"]).Color;

        Assert.Equal(AppThemeIds.Default, manager.Apply(resources, AppThemeIds.Default));
        Assert.NotEqual(biaBackground, ((SolidColorBrush)resources["AppBackgroundBrush"]).Color);
        Assert.NotEqual(biaAccent, ((SolidColorBrush)resources["AccentMagentaBrush"]).Color);
        Assert.Equal("BEATINSIGHT", resources["AppThemeIdentityLabel"]);
        Assert.Equal(Visibility.Collapsed, resources["AppThemeCharacterVisibility"]);
        Assert.Equal(Visibility.Visible, resources["AppThemeNeutralIdentityVisibility"]);
    }

    [Fact]
    public void DefaultThemesContainNoBiaIdentityOrAssets()
    {
        AppTheme appTheme = AppThemeCatalog.Get(AppThemeIds.Default);
        AppTheme biaAppTheme = AppThemeCatalog.Get(AppThemeIds.Bia);
        OverlayTheme overlayTheme = OverlayThemeCatalog.Get(OverlayThemeIds.Default);
        OverlayTheme biaOverlayTheme = OverlayThemeCatalog.Get(OverlayThemeIds.Bia);
        string[] defaultNamesAndValues = appTheme.Colors
            .SelectMany(pair => new[] { pair.Key, pair.Value })
            .Concat(overlayTheme.Tokens.Colors.SelectMany(pair => new[] { pair.Key, pair.Value }))
            .Concat(overlayTheme.Tokens.Text.SelectMany(pair => new[] { pair.Key, pair.Value }))
            .Concat(overlayTheme.Tokens.Cards.SelectMany(pair => new[] { pair.Key, pair.Value }))
            .Concat(overlayTheme.Tokens.Borders.SelectMany(pair => new[] { pair.Key, pair.Value }))
            .Concat(overlayTheme.Assets.SelectMany(pair => new[] { pair.Key, pair.Value }))
            .ToArray();
        string[] biaSpecificValues = biaAppTheme.Colors.Values
            .Concat(biaOverlayTheme.Tokens.Colors.Values)
            .Concat(biaOverlayTheme.Tokens.Text.Values)
            .Concat(biaOverlayTheme.Tokens.Cards.Values)
            .Concat(biaOverlayTheme.Tokens.Borders.Values)
            .Except(appTheme.Colors.Values, StringComparer.OrdinalIgnoreCase)
            .Except(overlayTheme.Tokens.Colors.Values, StringComparer.OrdinalIgnoreCase)
            .Except(overlayTheme.Tokens.Text.Values, StringComparer.OrdinalIgnoreCase)
            .Except(overlayTheme.Tokens.Cards.Values, StringComparer.OrdinalIgnoreCase)
            .Except(overlayTheme.Tokens.Borders.Values, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.False(appTheme.Identity.UsesCharacterAsset);
        Assert.DoesNotContain("bia", appTheme.Identity.Label, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(overlayTheme.Assets);
        Assert.DoesNotContain(defaultNamesAndValues, value =>
            value.Contains("bia", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("BiaChan", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(defaultNamesAndValues, value =>
            biaSpecificValues.Contains(value, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void AppDefaultDoesNotEnableBiaVisualAssets()
    {
        int assetLoadAttempts = 0;
        var assistant = new BiaAssistantViewModel(_ =>
        {
            assetLoadAttempts++;
            throw new FileNotFoundException("Bia assets are intentionally unavailable.");
        });
        var resources = new ResourceDictionary();
        var manager = new AppThemeManager();

        string themeId = manager.Apply(resources, AppThemeIds.Default);
        assistant.SetVisualAssetsEnabled(
            string.Equals(themeId, AppThemeIds.Bia, StringComparison.Ordinal));

        Assert.False(assistant.VisualAssetsEnabled);
        Assert.IsType<DrawingImage>(assistant.ImageSource);
        Assert.Equal(0, assetLoadAttempts);
    }

    [Fact]
    public void AppIdentitySwitchesDefaultToBiaAndBackWithoutStaleCharacterAsset()
    {
        var assistant = new BiaAssistantViewModel();
        var resources = new ResourceDictionary();
        var manager = new AppThemeManager();

        Apply(AppThemeIds.Default);
        Assert.Equal("BeatInsight", assistant.Name);
        Assert.IsType<DrawingImage>(assistant.ImageSource);

        Apply(AppThemeIds.Bia);
        Assert.Equal("Bia-chan", assistant.Name);
        Assert.IsType<System.Windows.Media.Imaging.BitmapImage>(assistant.ImageSource);

        Apply(AppThemeIds.Default);
        Assert.Equal("BeatInsight", assistant.Name);
        Assert.IsType<DrawingImage>(assistant.ImageSource);
        Assert.Equal(Visibility.Collapsed, resources["AppThemeCharacterVisibility"]);
        Assert.Equal(Visibility.Visible, resources["AppThemeNeutralIdentityVisibility"]);

        void Apply(string themeId)
        {
            string applied = manager.Apply(resources, themeId);
            assistant.SetVisualAssetsEnabled(
                string.Equals(applied, AppThemeIds.Bia, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ThemeSelectionsRoundTripWithoutChangingHudOptions()
    {
        WithStore(store =>
        {
            var expected = new OverlaySettings(
                OverlaySettings.CurrentVersion,
                OverlayLayoutIds.Analysis,
                new GameplayHudDisplayOptions(false, true, false, true, false, true),
                AppThemeIds.Bia,
                OverlayThemeIds.Default);

            Assert.True(store.Save(expected));
            Assert.Equal(expected, store.Load());
        });
    }

    [Fact]
    public void VersionOneSettingsMigrateWithDefaultThemes()
    {
        WithStore(store =>
        {
            File.WriteAllText(store.Path, """
                { "version": 1, "presetId": "Analysis", "gameplayHud": {
                  "showCurrentSection": false, "showUpNext": true } }
                """);

            OverlaySettings actual = store.Load();

            Assert.Equal(AppThemeIds.Default, actual.AppThemeId);
            Assert.Equal(OverlayThemeIds.Default, actual.OverlayThemeId);
            Assert.False(actual.GameplayHud.ShowCurrentSection);
            Assert.True(actual.GameplayHud.ShowUpNext);
        });
    }

    [Fact]
    public void FutureThemeValuesFallBackWithoutDestroyingOtherSettings()
    {
        WithStore(store =>
        {
            File.WriteAllText(store.Path, """
                { "version": 2, "presetId": "Timeline",
                  "appThemeId": "Future.App", "overlayThemeId": "BeatInsight.Bia",
                  "gameplayHud": { "showLiveStats": false, "showPerformance": true } }
                """);

            OverlaySettings actual = store.Load();

            Assert.Equal(OverlayLayoutIds.Timeline, actual.PresetId);
            Assert.Equal(AppThemeIds.Default, actual.AppThemeId);
            Assert.Equal(OverlayThemeIds.Bia, actual.OverlayThemeId);
            Assert.False(actual.GameplayHud.ShowLiveStats);
            Assert.True(actual.GameplayHud.ShowPerformance);
        });
    }

    [Fact]
    public void ViewModelChangesAppThemeWithoutChangingOverlayThemeOrLayoutSettings()
    {
        var viewModel = new OverlaySettingsViewModel();
        viewModel.Apply(new OverlaySettings(
            OverlaySettings.CurrentVersion,
            OverlayLayoutIds.Timeline,
            new GameplayHudDisplayOptions(false, true, true, false, false, true),
            AppThemeIds.Default,
            OverlayThemeIds.Bia));
        OverlaySettings? changed = null;
        viewModel.SettingsChanged += settings => changed = settings;

        viewModel.SelectedAppThemeId = AppThemeIds.Bia;

        Assert.NotNull(changed);
        Assert.Equal(AppThemeIds.Bia, changed.AppThemeId);
        Assert.Equal(OverlayThemeIds.Bia, changed.OverlayThemeId);
        Assert.Equal(OverlayLayoutIds.Timeline, changed.PresetId);
        Assert.Equal(new GameplayHudDisplayOptions(false, true, true, false, false, true), changed.GameplayHud);
    }

    [Fact]
    public void ViewModelChangesOverlayThemeWithoutChangingAppTheme()
    {
        var viewModel = new OverlaySettingsViewModel();
        viewModel.SelectedAppThemeId = AppThemeIds.Bia;
        OverlaySettings before = viewModel.Current;

        viewModel.SelectedOverlayThemeId = OverlayThemeIds.Bia;

        Assert.Equal(before.AppThemeId, viewModel.Current.AppThemeId);
        Assert.Equal(OverlayThemeIds.Bia, viewModel.Current.OverlayThemeId);
        Assert.Equal(before.GameplayHud, viewModel.Current.GameplayHud);
    }

    [Fact]
    public void OverlayThemeSwitchPreservesLayoutAndActiveEditSession()
    {
        OverlayLayout layout = OverlayLayoutCatalog.Get(OverlayLayoutIds.Analysis);
        var engine = new OverlayEngine(layout, OverlayThemeCatalog.Get(OverlayThemeIds.Default));
        Assert.True(engine.BeginEdit());
        OverlayLayout activeLayout = engine.CurrentLayout;
        OverlayLayout? entry = engine.EntryLayoutSnapshot;
        OverlayLayout? working = engine.WorkingLayout;

        engine.ApplyTheme(OverlayThemeCatalog.Get(OverlayThemeIds.Bia));

        Assert.Equal(OverlayThemeIds.Bia, engine.CurrentTheme.Id);
        Assert.True(engine.IsOverlayEditMode);
        Assert.Same(entry, engine.EntryLayoutSnapshot);
        Assert.Same(working, engine.WorkingLayout);
        Assert.Same(activeLayout, engine.CurrentLayout);
    }

    [Fact]
    public void DefaultAndBiaVisualTokensAreActuallyDistinct()
    {
        Assert.NotEqual(
            AppThemeCatalog.Get(AppThemeIds.Default).Colors["background"],
            AppThemeCatalog.Get(AppThemeIds.Bia).Colors["background"]);
        Assert.NotEqual(
            OverlayThemeCatalog.Get(OverlayThemeIds.Default).Tokens.Colors["accent.primary"],
            OverlayThemeCatalog.Get(OverlayThemeIds.Bia).Tokens.Colors["accent.primary"]);
    }

    [Theory]
    [InlineData("Jump")]
    [InlineData("Stream")]
    [InlineData("Tech")]
    [InlineData("ClassicMixed")]
    [InlineData("Break")]
    public void SemanticStructureColorsRemainResolvable(string structure)
    {
        object result = new StructureToBrushConverter().Convert(
            structure,
            typeof(Brush),
            null,
            CultureInfo.InvariantCulture);

        Assert.IsAssignableFrom<Brush>(result);
    }

    private static void WithStore(Action<OverlaySettingsStore> assertion)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"BeatInsight-Themes-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            assertion(new OverlaySettingsStore(Path.Combine(directory, "settings.json")));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
