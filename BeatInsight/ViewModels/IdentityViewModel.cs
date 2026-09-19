using System.Collections.ObjectModel;

namespace BeatInsight.ViewModels;

public sealed class IdentityViewModel : ViewModelBase
{
    private string primaryStructure = "—";
    private string secondaryStructure = "—";
    private bool hasSecondary;
    private double globalConfidence;
    private string globalConfidenceDisplay = "0%";
    private string primaryStructureColorKey = "StructureClassicMixed";
    private string secondaryStructureColorKey = "StructureClassicMixed";

    public string PrimaryStructure
    {
        get => primaryStructure;
        set => SetProperty(ref primaryStructure, value);
    }

    public string SecondaryStructure
    {
        get => secondaryStructure;
        set => SetProperty(ref secondaryStructure, value);
    }

    public bool HasSecondary
    {
        get => hasSecondary;
        set => SetProperty(ref hasSecondary, value);
    }

    public double GlobalConfidence
    {
        get => globalConfidence;
        set => SetProperty(ref globalConfidence, value);
    }

    public string GlobalConfidenceDisplay
    {
        get => globalConfidenceDisplay;
        set => SetProperty(ref globalConfidenceDisplay, value);
    }

    public string PrimaryStructureColorKey
    {
        get => primaryStructureColorKey;
        set => SetProperty(ref primaryStructureColorKey, value);
    }

    public string SecondaryStructureColorKey
    {
        get => secondaryStructureColorKey;
        set => SetProperty(ref secondaryStructureColorKey, value);
    }

    public ObservableCollection<MainSkillChipViewModel> MainSkills { get; } = [];
    public ObservableCollection<MainPatternChipViewModel> MainPatterns { get; } = [];
}
