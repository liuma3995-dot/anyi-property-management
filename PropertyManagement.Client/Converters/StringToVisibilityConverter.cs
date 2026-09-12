using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PropertyManagement.Client.Converters
{
    /// <summary>
    /// 非空字符串 → Visible（空/空白 → Collapsed）。
    /// 用于状态/错误/提示文案：无内容时不占位，保证表单按钮与规则卡间距稳定。
    /// </summary>
    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string text = value as string;
            return string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
