using BeatInsight.ViewModels;

namespace BeatInsight.Tests.ViewModels;

public sealed class SettingsWindowViewModelTests
{
    [Fact]
    public void DefaultsToGeneralCategory()
    {
        var viewModel = new SettingsWindowViewModel(new MainWindowViewModel());

        Assert.Equal(SettingsCategory.General, viewModel.SelectedCategory);
        Assert.True(viewModel.IsGeneralSelected);
        Assert.False(viewModel.IsOverlaySelected);
        Assert.False(viewModel.IsAppearanceSelected);
        Assert.False(viewModel.IsUpdatesSelected);
        Assert.False(viewModel.IsOsuApiSelected);
    }

    [Theory]
    [InlineData(SettingsCategory.General)]
    [InlineData(SettingsCategory.Overlay)]
    [InlineData(SettingsCategory.Appearance)]
    [InlineData(SettingsCategory.Updates)]
    [InlineData(SettingsCategory.OsuApi)]
    public void CategorySwitchingSelectsExactlyOnePage(SettingsCategory category)
    {
        var viewModel = new SettingsWindowViewModel(new MainWindowViewModel())
        {
            SelectedCategory = category
        };

        bool[] selected =
        [
            viewModel.IsGeneralSelected,
            viewModel.IsOverlaySelected,
            viewModel.IsAppearanceSelected,
            viewModel.IsUpdatesSelected,
            viewModel.IsOsuApiSelected
        ];

        Assert.Single(selected, value => value);
        Assert.True(selected[(int)category]);
    }

    [Fact]
    public void ReusesExistingSettingsViewModels()
    {
        var main = new MainWindowViewModel();
        var settings = new SettingsWindowViewModel(main);

        Assert.Same(main.Library, settings.Library);
        Assert.Same(main.WindowLifecycle, settings.WindowLifecycle);
        Assert.Same(main.OverlaySettings, settings.OverlaySettings);
        Assert.Same(main.Updates, settings.Updates);
        Assert.Same(main.OsuApiSettings, settings.OsuApiSettings);
    }
}
