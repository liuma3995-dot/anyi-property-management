using System;
using System.Globalization;
using System.Windows.Data;

namespace PropertyManagement.Client.Converters
{
    /// <summary>整型相等转换器：参数匹配时 IsChecked=true（用于页签 RadioButton ↔ 枚举值绑定）。</summary>
    public class IntEqualsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null && parameter != null && value.ToString() == parameter.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int result;
            return int.TryParse(parameter == null ? null : parameter.ToString(), out result) ? (object)result : 0;
        }
    }
}