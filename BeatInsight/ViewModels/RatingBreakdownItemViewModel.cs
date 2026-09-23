using System.Globalization;

namespace BeatInsight.ViewModels;

public sealed class RatingBreakdownItemViewModel
{
    public RatingBreakdownItemViewModel(
        string name,
        double value,
        bool isRatingPointContribution = false)
    {
        Name = name;
        Value = value;
        ValueDisplay = isRatingPointContribution
            ? $"{value.ToString("F2", CultureInfo.InvariantCulture)} rating pts"
            : $"{value.ToString("F1", CultureInfo.InvariantCulture)} / 100";
    }

    public string Name { get; }
    public double Value { get; }
    public string ValueDisplay { get; }
}
