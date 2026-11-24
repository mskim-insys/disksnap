using System.Globalization;
using System.Windows.Data;

namespace DiskSnap.App;

public sealed class DepthToIndentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int depth ? new System.Windows.Thickness(depth * 16, 0, 0, 0) : new System.Windows.Thickness(0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
