using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace PropertyManagement.Client.Views
{
    public partial class DeviceListView : UserControl
    {
        public DeviceListView()
        {
            InitializeComponent();
        }

        /// <summary>批量删除行勾选：逐行回写模型（参照纠纷列表，保证可单选）。</summary>
        private void BatchCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is ViewModels.DeviceRow row)
                row.IsChecked = cb.IsChecked == true;
        }
    }

    /// <summary>整数相等 → Visibility（详情浮层页签切换；ConverterParameter 为目标索引）。</summary>
    public class IntEqualConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int v; int p;
            int.TryParse(value == null ? string.Empty : value.ToString(), out v);
            int.TryParse(parameter == null ? string.Empty : parameter.ToString(), out p);
            return v == p ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
