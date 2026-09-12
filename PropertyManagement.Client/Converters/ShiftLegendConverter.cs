using System;
using System.Globalization;
using System.Windows.Data;
using PropertyManagement.Contract.Org;

namespace PropertyManagement.Client.Converters
{
    /// <summary>
    /// 排班图例文本：有时间窗时显示「早班 08:00-16:00」，无时间窗（如休）只显示名称。
    /// 参照原型 PG-ORG-02：图例芯片「早班 08:00-16:00 / 中班 16:00-24:00 / 晚班 00:00-08:00 / 休」。
    /// </summary>
    public class ShiftLegendConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var shift = value as ShiftDto;
            if (shift == null) { return string.Empty; }
            string name = shift.Name ?? string.Empty;
            string start = (shift.StartTime ?? string.Empty).Trim();
            string end = (shift.EndTime ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(start) && string.IsNullOrWhiteSpace(end))
            {
                return name;
            }
            return name + " " + start + "-" + end;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
