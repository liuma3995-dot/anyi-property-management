using System.Windows;
using System.Windows.Controls;
using PropertyManagement.Client.ViewModels;

namespace PropertyManagement.Client.Views
{
    /// <summary>财务报表页（PG-FIN-07）。</summary>
    public partial class FinancialReportView : UserControl
    {
        public FinancialReportView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 行勾选写回（CHG-v1.3.1-05）：只读 DataGrid 中 CheckBox 的 IsChecked 绑定不会写回源，
        /// 与其它页面批量删除一致，在点击时显式同步到行对象。
        /// </summary>
        private void RowCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is ExportTraceRow row)
            {
                row.IsChecked = cb.IsChecked == true;
            }
        }
    }
}
