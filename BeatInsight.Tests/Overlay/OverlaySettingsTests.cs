using BeatInsight.Overlay;
using BeatInsight.Overlay.GameplayHud;
using BeatInsight.ViewModels;
using System.IO;

namespace BeatInsight.Tests.Overlay;

public sealed class OverlaySettingsTests
{
    [Fact]
    public void MissingFileUsesMinimalDefaults()
    {
        WithStore(store => AssertMinimal(store.Load()));
    }

    [Fact]
    public void ValidSettingsAreRestored()
    {
        WithStore(store =>
        {
            var expected = new OverlaySettings(
                OverlaySettings.CurrentVersion,
                OverlayLayoutIds.Analysis,
                new GameplayHudDisplayOptions(false, true, false, true));

            Assert.True(store.Save(expected));

            Assert.Equal(expected, store.Load());
        });
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"version\":1,\"presetId\":")]
    public void InvalidJsonUsesMinimalDefaults(string json)
    {
        WithStore(store =>
        {
            File.WriteAllText(store.Path, json);
            AssertMinimal(store.Load());
        });
    }

    [Fact]
    public void UnknownPresetUsesMinimalDefaults()
    {
        WithStore(store =>
        {
            File.WriteAllText(store.Path, """
                { "version": 1, "presetId": "Unknown", "gameplayHud": {
                  "showCurrentSection": false, "showUpNext": false,
                  "showDifficultyTimeline": true, "showLivePatterns": true } }
                """);

            AssertMinimal(store.Load());
        });
    }

    [Fact]
    public void MissingOptionsUseSelectedPresetDefaults()
    {
        WithStore(store =>
        {
            File.WriteAllText(store.Path, """
                { "version": 1, "presetId": "Timeline", "gameplayHud": {
                  "showCurrentSection": false } }
                """);

            OverlaySettings actual = store.Load();

            Assert.Equal(OverlayLayoutIds.Timeline, actual.PresetId);
            Assert.Equal(new GameplayHudDisplayOptions(false, true, true, false), actual.GameplayHud);
        });
    }

    [Fact]
    public void WriteThenReadRoundTrips()
    {
        WithStore(store =>
        {
            var expected = new OverlaySettings(
                OverlaySettings.CurrentVersion,
                OverlayLayoutIds.Timeline,
                new GameplayHudDisplayOptions(true, false, true, true, false));

            Assert.True(store.Save(expected));
            Assert.Equal(expected, store.Load());
        });
    }

    [Theory]
    [InlineData(OverlayLayoutIds.Minimal, true, true, false, false)]
    [InlineData(OverlayLayoutIds.Analysis, true, true, true, true)]
    [InlineData(OverlayLayoutIds.Timeline, true, true, true, false)]
    public void PresetDefaultsAreDeterministic(
        string preset,
        bool current,
        bool next,
        bool timeline,
        bool patterns)
    {
        OverlaySettings settings = OverlaySettings.ForPreset(preset);

        Assert.Equal(new GameplayHudDisplayOptions(current, next, timeline, patterns), settings.GameplayHud);
    }

    [Fact]
    public void ChoosingPresetAppliesDefaultsAndRaisesOneFinalState()
    {
        var viewModel = new OverlaySettingsViewModel();
        var changes = new List<OverlaySettings>();
        viewModel.SettingsChanged += changes.Add;

        viewModel.SelectedPresetId = OverlayLayoutIds.Analysis;

        OverlaySettings change = Assert.Single(changes);
        Assert.Equal(OverlayLayoutIds.Analysis, change.PresetId);
        Assert.Equal(new GameplayHudDisplayOptions(true, true, true, true), change.GameplayHud);
    }

    [Fact]
    public void CheckboxOverrideCanBePersisted()
    {
        WithStore(store =>
        {
            var viewModel = new OverlaySettingsViewModel();
            viewModel.SettingsChanged += settings => Assert.True(store.Save(settings));

            viewModel.ShowUpNext = false;

            Assert.False(store.Load().GameplayHud.ShowUpNext);
        });
    }

    [Fact]
    public void LiveStatsCheckboxCanBePersisted()
    {
        WithStore(store =>
        {
            var viewModel = new OverlaySettingsViewModel();
            viewModel.SettingsChanged += settings => Assert.True(store.Save(settings));

            viewModel.ShowLiveStats = false;

            Assert.False(store.Load().GameplayHud.ShowLiveStats);
        });
    }

    [Fact]
    public void PerformanceCheckboxCanBePersisted()
    {
        WithStore(store =>
        {
            var viewModel = new OverlaySettingsViewModel();
            viewModel.SettingsChanged += settings => Assert.True(store.Save(settings));

            viewModel.ShowPerformance = false;

            Assert.False(store.Load().GameplayHud.ShowPerformance);
        });
    }

    [Fact]
    public void ExposedPresetsUseOnlyUnifiedGameplayHudPlacements()
    {
        string[] legacyIds =
        [
            OverlayWidgetIds.CurrentSection,
            OverlayWidgetIds.UpNext,
            OverlayWidgetIds.DifficultyTimeline,
            OverlayWidgetIds.Patterns
        ];

        Assert.Equal(
            [OverlayLayoutIds.Minimal, OverlayLayoutIds.Analysis, OverlayLayoutIds.Timeline],
            OverlayLayoutCatalog.ExposedIds);
        foreach (string presetId in OverlayLayoutCatalog.ExposedIds)
        {
            OverlayLayout layout = OverlayLayoutCatalog.Get(presetId);
            Assert.Contains(layout.Widgets, item => item.WidgetId == OverlayWidgetIds.GameplayHud);
            Assert.DoesNotContain(layout.Widgets, item => legacyIds.Contains(item.WidgetId));
        }
    }

    private static void AssertMinimal(OverlaySettings settings)
    {
        Assert.Equal(OverlayLayoutIds.Minimal, settings.PresetId);
        Assert.Equal(new GameplayHudDisplayOptions(true, true, false, false), settings.GameplayHud);
    }

    private static void WithStore(Action<OverlaySettingsStore> assertion)
    {
        string directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"BeatInsight-OverlaySettings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            assertion(new OverlaySettingsStore(System.IO.Path.Combine(directory, "settings.json")));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
