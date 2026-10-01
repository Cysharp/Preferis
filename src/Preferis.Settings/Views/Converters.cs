using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Preferis.Core.Configuration;

namespace Preferis.Settings.Views;

/// <summary>PNG bytes (browser icons, profile pictures) to a bitmap. View models stay free of Avalonia types.</summary>
public sealed class PngToBitmapConverter : IValueConverter
{
    // The same byte array is shown in several places (list, editor, combo); decode it once.
    private static readonly ConditionalWeakTable<byte[], Bitmap> Cache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] { Length: > 0 } png)
        {
            return null;
        }
        return Cache.GetValue(png, bytes =>
        {
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        });
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Errors in red, warnings in amber; both readable on light and dark backgrounds.</summary>
public sealed class IssueBrushConverter : IValueConverter
{
    private static readonly IBrush Error = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));
    private static readonly IBrush Warning = new SolidColorBrush(Color.FromRgb(0xD9, 0x8E, 0x04));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is IssueSeverity.Error ? Error : Warning;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
