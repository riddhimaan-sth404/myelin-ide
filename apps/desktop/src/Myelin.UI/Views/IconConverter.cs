using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Myelin.UI.Views
{
    public class IconConverter : IValueConverter
    {
        public static readonly IconConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isDir && isDir)
            {
                if (Application.Current?.TryFindResource("IconFolder", out var res) == true && res is StreamGeometry folderGeom)
                {
                    return folderGeom;
                }
            }
            if (Application.Current?.TryFindResource("IconFile", out var fileRes) == true && fileRes is StreamGeometry fileGeom)
            {
                return fileGeom;
            }
            return null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
