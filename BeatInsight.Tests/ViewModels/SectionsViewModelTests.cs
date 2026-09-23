using BeatInsight.ViewModels;

namespace BeatInsight.Tests.ViewModels;

public sealed class SectionsViewModelTests
{
    [Fact]
    public void PreviousAndNextUseCollectionPositionInsteadOfSectionIndex()
    {
        var overview = new OverviewViewModel();
        var sections = new SectionsViewModel(overview);
        var first = new SectionTimelineItemViewModel { Index = 10 };
        var second = new SectionTimelineItemViewModel { Index = 30 };
        var third = new SectionTimelineItemViewModel { Index = 50 };
        overview.Sections.Add(first);
        overview.Sections.Add(second);
        overview.Sections.Add(third);

        sections.SelectSection(second);
        Assert.True(sections.CanGoPrevious);
        Assert.True(sections.CanGoNext);

        sections.SelectPrevious();
        Assert.Same(first, sections.SelectedSection);
        sections.SelectNext();
        sections.SelectNext();
        Assert.Same(third, sections.SelectedSection);
        Assert.False(sections.CanGoNext);
    }

    [Fact]
    public void LoadingAndReloadingSectionsSelectsFirstSectionWithoutUserClick()
    {
        var overview = new OverviewViewModel();
        var sections = new SectionsViewModel(overview);
        var first = new SectionTimelineItemViewModel { Index = 7 };
        var second = new SectionTimelineItemViewModel { Index = 19 };
        var changedProperties = new List<string?>();
        sections.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        overview.Sections.Add(first);
        changedProperties.Clear();
        overview.Sections.Add(second);

        Assert.Same(overview.Sections[0], sections.SelectedSection);
        Assert.True(sections.CanGoNext);
        Assert.Contains(nameof(SectionsViewModel.SelectedSection), changedProperties);
        Assert.Contains(nameof(SectionsViewModel.HasSelection), changedProperties);
        Assert.Contains(nameof(SectionsViewModel.CanGoPrevious), changedProperties);
        Assert.Contains(nameof(SectionsViewModel.CanGoNext), changedProperties);

        sections.SelectNext();
        Assert.Same(overview.Sections[1], sections.SelectedSection);

        overview.Sections.Clear();
        Assert.Null(sections.SelectedSection);
        Assert.False(sections.CanGoPrevious);
        Assert.False(sections.CanGoNext);

        overview.Sections.Add(new SectionTimelineItemViewModel { Index = 42 });
        overview.Sections.Add(new SectionTimelineItemViewModel { Index = 84 });

        Assert.Same(overview.Sections[0], sections.SelectedSection);
        Assert.True(sections.HasSelection);
        Assert.False(sections.CanGoPrevious);
        Assert.True(sections.CanGoNext);
    }
}
