using System.Collections.ObjectModel;
using BeatInsight.Models.Rating;

namespace BeatInsight.ViewModels;

public sealed class DifficultyAnalysisViewModel : ViewModelBase
{
    public const int MaximumTopSectionCount = 5;

    public ObservableCollection<RatingBreakdownItemViewModel> RatingBreakdown { get; } = [];
    public ObservableCollection<SectionTimelineItemViewModel> TopSections { get; } = [];

    public bool HasRatingBreakdown => RatingBreakdown.Count > 0;
    public bool HasTopSections => TopSections.Count > 0;

    public void LoadRating(BeatInsightRatingV2? rating)
    {
        RatingBreakdown.Clear();

        if (rating is not null)
        {
            RatingBreakdown.Add(new("Base difficulty", rating.BaseDifficulty));
            RatingBreakdown.Add(new("Sustained difficulty", rating.SustainedDifficulty));
            RatingBreakdown.Add(new("Peak difficulty", rating.PeakDifficulty));
            RatingBreakdown.Add(new("Skill pressure", rating.SkillPressureContribution));
            RatingBreakdown.Add(new("Pattern complexity", rating.PatternContribution));
            RatingBreakdown.Add(new("Endurance", rating.EnduranceContribution));
            RatingBreakdown.Add(new(
                "Specialization contribution",
                rating.SpecializationContribution,
                isRatingPointContribution: true));
        }

        OnPropertyChanged(nameof(HasRatingBreakdown));
    }

    public void LoadSections(IEnumerable<SectionTimelineItemViewModel> sections)
    {
        TopSections.Clear();

        foreach (SectionTimelineItemViewModel section in sections
                     .Where(section => double.IsFinite(section.LocalDifficulty))
                     .OrderByDescending(section => section.LocalDifficulty)
                     .ThenBy(section => section.Index)
                     .Take(MaximumTopSectionCount))
        {
            TopSections.Add(section);
        }

        OnPropertyChanged(nameof(HasTopSections));
    }

    public void Clear()
    {
        RatingBreakdown.Clear();
        TopSections.Clear();
        OnPropertyChanged(nameof(HasRatingBreakdown));
        OnPropertyChanged(nameof(HasTopSections));
    }
}
