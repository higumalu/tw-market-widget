using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TwMarketWidget.Converters;

/// <summary>漲跌方向 → 顏色。台股習慣：紅漲、綠跌、平盤灰。</summary>
public sealed class DirectionToBrushConverter : IValueConverter
{
    public static readonly SolidColorBrush Up = Freeze(Color.FromRgb(0xF4, 0x51, 0x51));
    public static readonly SolidColorBrush Down = Freeze(Color.FromRgb(0x3F, 0xC1, 0x7A));
    public static readonly SolidColorBrush Flat = Freeze(Color.FromRgb(0xC8, 0xCD, 0xD6));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            int and > 0 => Up,
            int and < 0 => Down,
            _ => Flat,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

/// <summary>數值格式化；ConverterParameter 為格式字串，null 顯示 "—"。</summary>
public sealed class NumberConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return "—";
        }

        var format = parameter as string ?? "0.##";
        return value switch
        {
            decimal d => d.ToString(format, culture),
            long l => l.ToString(format, culture),
            double db => db.ToString(format, culture),
            _ => value.ToString() ?? "—",
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// 帶正負號的數值，例如 +12.5 / -3.2。ConverterParameter 是格式字串，
/// 可以用 "0.00|%" 這種寫法在後面接一個單位。
/// </summary>
public sealed class SignedNumberConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (parameter as string ?? "0.00").Split('|', 2);
        var suffix = parts.Length > 1 ? parts[1] : "";

        if (value is not decimal d)
        {
            return "—";
        }

        var text = Math.Abs(d).ToString(parts[0], culture) + suffix;
        return d switch
        {
            > 0 => $"+{text}",
            < 0 => $"-{text}",
            _ => text,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
