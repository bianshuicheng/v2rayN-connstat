using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace v2rayN.Converters;

/// <summary>
/// Extracts the icon of an executable path (per-connection process icon),
/// cached per path so each process icon is only read once.
/// </summary>
public class ProcessIconConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, ImageSource?> _cache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path))
        {
            return null;
        }
        return _cache.GetOrAdd(path, p =>
        {
            try
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(p.Replace('/', '\\'));
                if (icon == null)
                {
                    return null;
                }
                var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            catch
            {
                return null;
            }
        });
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
