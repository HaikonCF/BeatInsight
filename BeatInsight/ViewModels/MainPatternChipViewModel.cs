namespace BeatInsight.ViewModels;

public sealed class MainPatternChipViewModel : ViewModelBase
{
    private string name = string.Empty;
    private int count;
    private double ratio;
    private string occurrenceDisplay = string.Empty;

    public string Name
    {
        get => name;
        set => SetProperty(ref name, value);
    }

    public int Count
    {
        get => count;
        set => SetProperty(ref count, value);
    }

    public double Ratio
    {
        get => ratio;
        set => SetProperty(ref ratio, value);
    }

    public string OccurrenceDisplay
    {
        get => occurrenceDisplay;
        set => SetProperty(ref occurrenceDisplay, value);
    }

    public MainPatternChipViewModel() { }

    public MainPatternChipViewModel(string name, int count, double ratio, string occurrenceDisplay)
    {
        Name = name;
        Count = count;
        Ratio = ratio;
        OccurrenceDisplay = occurrenceDisplay;
    }
}
