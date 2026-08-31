using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>把资源键（如 Icon.WalletCards）转换为 Geometry，用于导航图标绑定。</summary>
    public class IconKeyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var key = value as string;
            if (string.IsNullOrEmpty(key) || Application.Current == null)
            {
                return null;
            }

            return Application.Current.TryFindResource(key) as Geometry;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
