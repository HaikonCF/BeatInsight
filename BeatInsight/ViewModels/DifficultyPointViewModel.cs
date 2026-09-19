namespace BeatInsight.ViewModels;

public sealed class DifficultyPointViewModel : ViewModelBase
{
    private int index;
    private double timeMs;
    private string timeDisplay = "00:00";
    private double localDifficulty;
    private bool isSpike;
    private string? spikeDescription;

    public int Index
    {
        get => index;
        set => SetProperty(ref index, value);
    }

    public double TimeMs
    {
        get => timeMs;
        set => SetProperty(ref timeMs, value);
    }

    public string TimeDisplay
    {
        get => timeDisplay;
        set => SetProperty(ref timeDisplay, value);
    }

    public double LocalDifficulty
    {
        get => localDifficulty;
        set => SetProperty(ref localDifficulty, value);
    }

    public bool IsSpike
    {
        get => isSpike;
        set => SetProperty(ref isSpike, value);
    }

    public string? SpikeDescription
    {
        get => spikeDescription;
        set => SetProperty(ref spikeDescription, value);
    }

    public DifficultyPointViewModel() { }

    public DifficultyPointViewModel(int index, double timeMs, string timeDisplay, double localDifficulty, bool isSpike = false, string? spikeDescription = null)
    {
        Index = index;
        TimeMs = timeMs;
        TimeDisplay = timeDisplay;
        LocalDifficulty = localDifficulty;
        IsSpike = isSpike;
        SpikeDescription = spikeDescription;
    }
}
