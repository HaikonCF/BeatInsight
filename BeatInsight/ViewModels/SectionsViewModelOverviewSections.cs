using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace BeatInsight.ViewModels;

public sealed class SectionsViewModel : ViewModelBase
{
    private readonly OverviewViewModel overview;
    private SectionTimelineItemViewModel? selectedSection;

    public SectionsViewModel(OverviewViewModel overview)
    {
        this.overview = overview;

        overview.Sections.CollectionChanged += OnSectionsChanged;

        SyncSelection();
    }

    public ObservableCollection<SectionTimelineItemViewModel> Sections =>
        overview.Sections;

    public SectionTimelineItemViewModel? SelectedSection
    {
        get => selectedSection;
        set
        {
            if (SetProperty(ref selectedSection, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(CanGoPrevious));
                OnPropertyChanged(nameof(CanGoNext));
            }
        }
    }

    public bool HasSections => Sections.Count > 0;
    public bool HasSelection => SelectedSection is not null;

    public bool CanGoPrevious =>
        GetSelectedPosition() > 0;

    public bool CanGoNext =>
        GetSelectedPosition() is int position &&
        position >= 0 &&
        position < Sections.Count - 1;

    public void SelectPrevious()
    {
        int position = GetSelectedPosition();
        if (position <= 0)
            return;

        SelectedSection = Sections[position - 1];
    }

    public void SelectNext()
    {
        int position = GetSelectedPosition();
        if (position < 0 || position >= Sections.Count - 1)
            return;

        SelectedSection = Sections[position + 1];
    }

    public void SelectSection(SectionTimelineItemViewModel section)
    {
        if (section is not null && Sections.Contains(section))
            SelectedSection = section;
    }

    private void OnSectionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasSections));
        SyncSelection();
        RaiseSelectionPropertiesChanged();
    }

    private void SyncSelection()
    {
        if (Sections.Count == 0)
        {
            SelectedSection = null;
            return;
        }

        if (SelectedSection is null ||
            !Sections.Contains(SelectedSection))
        {
            SelectedSection = Sections[0];
        }
    }

    private int GetSelectedPosition() =>
        SelectedSection is null ? -1 : Sections.IndexOf(SelectedSection);

    private void RaiseSelectionPropertiesChanged()
    {
        OnPropertyChanged(nameof(SelectedSection));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
    }
}
