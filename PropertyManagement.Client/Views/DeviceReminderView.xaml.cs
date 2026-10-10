using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace PropertyManagement.Client.Views
{
    public partial class DeviceReminderView : UserControl
    {
        public DeviceReminderView()
        {
            InitializeComponent();
            // CHG-v1.4.1-14：仪表盘「到期提醒」待办 → 定位到指定行（选中 + 滚动到可视区）
            RowFocusAdapter.Bind(this, RemindersGrid);
        }
    }

    /// <summary>bool 取反 → Visibility（提醒行"—"占位：不可催办时显示）。IntEqualConverter 定义于 DeviceListView.xaml.cs（同命名空间共用）。</summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool && (bool)value) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
