using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace BeatInsight.Converters;

public sealed class StructureToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush StreamBrush = new(Color.FromRgb(0x38, 0xBD, 0xF8));
    private static readonly SolidColorBrush JumpBrush = new(Color.FromRgb(0xF4, 0x3F, 0x5E));
    private static readonly SolidColorBrush TechBrush = new(Color.FromRgb(0xA8, 0x55, 0xF7));
    private static readonly SolidColorBrush ClassicMixedBrush = new(Color.FromRgb(0x10, 0xB9, 0x81));
    private static readonly SolidColorBrush AccentMagentaBrush = new(Color.FromRgb(0xFF, 0x2E, 0x93));
    private static readonly SolidColorBrush DefaultBrush = new(Color.FromRgb(0x64, 0x74, 0x8B));

    static StructureToBrushConverter()
    {
        StreamBrush.Freeze();
        JumpBrush.Freeze();
        TechBrush.Freeze();
        ClassicMixedBrush.Freeze();
        AccentMagentaBrush.Freeze();
        DefaultBrush.Freeze();
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string? structure = value?.ToString()?.ToUpperInvariant();
        return structure switch
        {
            "STREAM" or "STRUCTURESTREAM" => StreamBrush,
            "JUMP" or "STRUCTUREJUMP" => JumpBrush,
            "TECH" or "STRUCTURETECH" => TechBrush,
            "CLASSICMIXED" or "CLASSIC_MIXED" or "CLASSIC" or "MIXED" or "STRUCTURECLASSICMIXED" => ClassicMixedBrush,
            "ACCENTMAGENTA" => AccentMagentaBrush,
            _ => DefaultBrush
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
