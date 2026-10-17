using System.Collections.ObjectModel;
using BeatInsight.Models.Fusion;
using BeatInsight.Models.Identity;

namespace BeatInsight.ViewModels;

public sealed class PatternsViewModel : ViewModelBase
{
    public const int MaximumDominantPatternCount = 5;

    public ObservableCollection<PatternDisplayItemViewModel> DominantPatterns { get; } = [];
    public ObservableCollection<PatternDisplayItemViewModel> JumpPatterns { get; } = [];
    public ObservableCollection<PatternDisplayItemViewModel> StreamPatterns { get; } = [];
    public ObservableCollection<PatternDisplayItemViewModel> TechPatterns { get; } = [];

    public bool HasPatterns => JumpPatterns.Count + StreamPatterns.Count + TechPatterns.Count > 0;
    public bool HasJumpPatterns => JumpPatterns.Count > 0;
    public bool HasStreamPatterns => StreamPatterns.Count > 0;
    public bool HasTechPatterns => TechPatterns.Count > 0;

    public void Load(
        IReadOnlyList<GameplayIdentityV3Pattern>? patterns,
        GameplayStructureKind primaryStructure = GameplayStructureKind.ClassicMixed)
    {
        Clear();

        if (patterns is null)
            return;

        PatternDisplayItemViewModel[] detected = patterns
            .Where(pattern => pattern is not null)
            .Select(pattern => new PatternDisplayItemViewModel(pattern))
            .Where(pattern => pattern.IsDetected)
            .OrderByDescending(pattern => pattern.NumericSortValue)
            .ThenByDescending(pattern => pattern.Confidence ?? 0)
            .ThenBy(pattern => pattern.Name, StringComparer.Ordinal)
            .ToArray();

        foreach (PatternDisplayItemViewModel pattern in detected)
        {
            CollectionFor(pattern.Family).Add(pattern);
        }

        PatternDisplayItemViewModel[] overviewCandidates = SelectOverviewCandidates(
            detected.Where(pattern => pattern.HasCoverage).ToArray(),
            primaryStructure);
        foreach (PatternDisplayItemViewModel pattern in overviewCandidates
                     .Take(MaximumDominantPatternCount))
        {
            DominantPatterns.Add(pattern);
        }

        RaiseAvailabilityProperties();
    }

    private static PatternDisplayItemViewModel[] SelectOverviewCandidates(
        PatternDisplayItemViewModel[] detected,
        GameplayStructureKind primaryStructure)
    {
        if (primaryStructure == GameplayStructureKind.ClassicMixed)
            return detected;

        FusionPatternFamily primaryFamily = primaryStructure switch
        {
            GameplayStructureKind.Jump => FusionPatternFamily.Jump,
            GameplayStructureKind.Stream => FusionPatternFamily.Stream,
            GameplayStructureKind.Tech => FusionPatternFamily.Tech,
            _ => throw new ArgumentOutOfRangeException(nameof(primaryStructure), primaryStructure, null)
        };
        PatternDisplayItemViewModel[] primaryPatterns = detected
            .Where(pattern => pattern.Family == primaryFamily)
            .ToArray();
        return primaryPatterns.Length > 0 ? primaryPatterns : detected;
    }

    public void Clear()
    {
        DominantPatterns.Clear();
        JumpPatterns.Clear();
        StreamPatterns.Clear();
        TechPatterns.Clear();
        RaiseAvailabilityProperties();
    }

    private ObservableCollection<PatternDisplayItemViewModel> CollectionFor(FusionPatternFamily family) =>
        family switch
        {
            FusionPatternFamily.Jump => JumpPatterns,
            FusionPatternFamily.Stream => StreamPatterns,
            FusionPatternFamily.Tech => TechPatterns,
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, null)
        };

    private void RaiseAvailabilityProperties()
    {
        OnPropertyChanged(nameof(HasPatterns));
        OnPropertyChanged(nameof(HasJumpPatterns));
        OnPropertyChanged(nameof(HasStreamPatterns));
        OnPropertyChanged(nameof(HasTechPatterns));
    }
}
