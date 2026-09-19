using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BeatInsight.Converters;

public sealed class NullOrEmptyToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isNullOrEmpty = value switch
        {
            null => true,
            string s => string.IsNullOrWhiteSpace(s),
            System.Collections.ICollection c => c.Count == 0,
            _ => false
        };

        if (Invert)
        {
            return isNullOrEmpty ? Visibility.Visible : Visibility.Collapsed;
        }

        return isNullOrEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
