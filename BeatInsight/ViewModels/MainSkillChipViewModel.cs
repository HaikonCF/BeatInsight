namespace BeatInsight.ViewModels;

public sealed class MainSkillChipViewModel : ViewModelBase
{
    private string name = string.Empty;
    private double? score;
    private string? scoreDisplay;
    private string? reliability;

    public string Name
    {
        get => name;
        set => SetProperty(ref name, value);
    }

    public double? Score
    {
        get => score;
        set => SetProperty(ref score, value);
    }

    public string? ScoreDisplay
    {
        get => scoreDisplay;
        set => SetProperty(ref scoreDisplay, value);
    }

    public string? Reliability
    {
        get => reliability;
        set => SetProperty(ref reliability, value);
    }

    public MainSkillChipViewModel() { }

    public MainSkillChipViewModel(string name, double? score = null, string? scoreDisplay = null, string? reliability = null)
    {
        Name = name;
        Score = score;
        ScoreDisplay = scoreDisplay;
        Reliability = reliability;
    }
}
