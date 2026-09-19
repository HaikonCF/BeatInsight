namespace BeatInsight.ViewModels;

public sealed class SectionTimelineItemViewModel : ViewModelBase
{
    private int index;
    private double startTimeMs;
    private double endTimeMs;
    private string timeRangeDisplay = "00:00 - 00:00";
    private string structure = "ClassicMixed";
    private string structureColorKey = "StructureClassicMixed";
    private double localDifficulty;
    private string localDifficultyDisplay = "0.0";
    private string dominantSignal = string.Empty;
    private string tooltipText = string.Empty;

    public int Index
    {
        get => index;
        set => SetProperty(ref index, value);
    }

    public double StartTimeMs
    {
        get => startTimeMs;
        set => SetProperty(ref startTimeMs, value);
    }

    public double EndTimeMs
    {
        get => endTimeMs;
        set => SetProperty(ref endTimeMs, value);
    }

    public string TimeRangeDisplay
    {
        get => timeRangeDisplay;
        set => SetProperty(ref timeRangeDisplay, value);
    }

    public string Structure
    {
        get => structure;
        set => SetProperty(ref structure, value);
    }

    public string StructureColorKey
    {
        get => structureColorKey;
        set => SetProperty(ref structureColorKey, value);
    }

    public double LocalDifficulty
    {
        get => localDifficulty;
        set => SetProperty(ref localDifficulty, value);
    }

    public string LocalDifficultyDisplay
    {
        get => localDifficultyDisplay;
        set => SetProperty(ref localDifficultyDisplay, value);
    }

    public string DominantSignal
    {
        get => dominantSignal;
        set => SetProperty(ref dominantSignal, value);
    }

    public string TooltipText
    {
        get => tooltipText;
        set => SetProperty(ref tooltipText, value);
    }
}
