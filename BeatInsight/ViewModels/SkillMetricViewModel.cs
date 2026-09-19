namespace BeatInsight.ViewModels;

public sealed class SkillMetricViewModel : ViewModelBase
{
    private string name = string.Empty;
    private double metricValue;
    private string displayValue = "0%";
    private string? description;
    private bool isAvailable = true;
    private string accentKey = "AccentMagenta";

    public string Name
    {
        get => name;
        set => SetProperty(ref name, value);
    }

    public double Value
    {
        get => metricValue;
        set => SetProperty(ref metricValue, Math.Clamp(value, 0.0, 100.0));
    }

    public string DisplayValue
    {
        get => displayValue;
        set => SetProperty(ref displayValue, value);
    }

    public string? Description
    {
        get => description;
        set => SetProperty(ref description, value);
    }

    public bool IsAvailable
    {
        get => isAvailable;
        set => SetProperty(ref isAvailable, value);
    }

    public string AccentKey
    {
        get => accentKey;
        set => SetProperty(ref accentKey, value);
    }

    public SkillMetricViewModel() { }

    public SkillMetricViewModel(string name, double value, string displayValue, string? description = null, bool isAvailable = true, string accentKey = "AccentMagenta")
    {
        Name = name;
        Value = value;
        DisplayValue = displayValue;
        Description = description;
        IsAvailable = isAvailable;
        AccentKey = accentKey;
    }
}
