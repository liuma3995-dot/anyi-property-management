using System.Windows;
using System.Windows.Controls;
using PropertyManagement.Client.ViewModels;

namespace PropertyManagement.Client.Views
{
    /// <summary>账单工作台页（PG-FIN-02）。</summary>
    public partial class BillWorkbenchView : UserControl
    {
        public BillWorkbenchView()
        {
            InitializeComponent();
        }

        /// <summary>行勾选写回：只读 DataGrid 中 CheckBox 的 IsChecked 绑定不会写回源，需在点击时显式同步到行对象。</summary>
        private void RowCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is BillBatchRow row)
            {
                row.IsChecked = cb.IsChecked == true;
            }
        }

        /// <summary>CHG-v1.1.0-10：缴费对象勾选写回（生成账单弹窗内只读 DataGrid）。</summary>
        private void ObjectCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is BillObjectRow row)
            {
                row.IsChecked = cb.IsChecked == true;
            }
        }
    }
}
