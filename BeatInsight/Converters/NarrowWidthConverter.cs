using System.Globalization;
using System.Windows.Data;

namespace BeatInsight.Converters;

public sealed class NarrowWidthConverter : IValueConverter
{
    public double Threshold { get; set; } = 1200;

    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        value is double width && double.IsFinite(width) && width <= Threshold;

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}
