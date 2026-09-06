using System.Windows;
using System.Windows.Controls;
using PropertyManagement.Client.ViewModels;

namespace PropertyManagement.Client.Views
{
    public partial class ParkingView : UserControl
    {
        public ParkingView()
        {
            InitializeComponent();
        }

        /// <summary>行勾选写回：只读 DataGrid 中 CheckBox 的 IsChecked 绑定不会写回源，需在点击时显式同步到行对象。</summary>
        private void RowCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is ParkingRow row)
            {
                row.IsChecked = cb.IsChecked == true;
            }
        }

        private void FormType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 人防禁售提示在 VM 校验层处理；此事件仅为切换触发
        }
    }
}
