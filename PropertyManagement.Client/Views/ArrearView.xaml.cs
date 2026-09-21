using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PropertyManagement.Client.ViewModels;

namespace PropertyManagement.Client.Views
{
    /// <summary>欠费台账页（PG-FIN-06）。</summary>
    public partial class ArrearView : UserControl
    {
        public ArrearView()
        {
            InitializeComponent();
        }

        /// <summary>行勾选写回：只读 DataGrid 中 CheckBox 的 IsChecked 绑定不会写回源，需在点击时显式同步到行对象。</summary>
        private void RowCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is ArrearRow row)
            {
                row.IsChecked = cb.IsChecked == true;
            }
        }

        private void KeywordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is ArrearViewModel vm)
            {
                _ = vm.SearchCommand.ExecuteAsync(null);
            }
        }

        private void CollectButton_Click(object sender, RoutedEventArgs e)
        {
                        MessageBox.Show("请在「收款登记」页办理收款。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BatchRemindButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("请逐条点击【催缴】选择渠道并登记（当前单机版支持单条催缴留痕）。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
                        MessageBox.Show("导出功能随报表模块统一提供（Excel/PDF）。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
