using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace NightInjection.UI.Converters;

public sealed class PathToImageSourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var uri = Uri.TryCreate(path, UriKind.Absolute, out var absolute)
                ? absolute
                : new Uri($"ms-appx:///{path.Replace('\\', '/')}");
            return new BitmapImage(uri);
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
