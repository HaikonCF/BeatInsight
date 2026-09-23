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
    private double jumpScore;
    private double streamScore;
    private double techScore;
    private double burstScore;
    private bool isSpike;

    public double JumpScore
    {
        get => jumpScore;
        set => SetProperty(ref jumpScore, value);
    }

    public double StreamScore
    {
        get => streamScore;
        set => SetProperty(ref streamScore, value);
    }

    public double TechScore
    {
        get => techScore;
        set => SetProperty(ref techScore, value);
    }

    public double BurstScore
    {
        get => burstScore;
        set => SetProperty(ref burstScore, value);
    }

    public bool IsSpike
    {
        get => isSpike;
        set => SetProperty(ref isSpike, value);
    }

    public double DurationMs => Math.Max(0, EndTimeMs - StartTimeMs);


    public string DurationDisplay =>
        TimeSpan.FromMilliseconds(DurationMs).ToString(@"mm\:ss");

    public string SectionNumberDisplay => $"SECTION {Index + 1}";
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
