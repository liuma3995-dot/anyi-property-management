using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PropertyManagement.Client.ViewModels;

namespace PropertyManagement.Client.Views
{
    /// <summary>收费项目维护页（PG-FIN-01）。</summary>
    public partial class ChargeItemsView : UserControl
    {
        public ChargeItemsView()
        {
            InitializeComponent();
        }

        /// <summary>行勾选写回：只读 DataGrid 中 CheckBox 的 IsChecked 绑定不会写回源，需在点击时显式同步到行对象。</summary>
        private void RowCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is ChargeItemRow row)
            {
                row.IsChecked = cb.IsChecked == true;
            }
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is ChargeItemsViewModel vm)
            {
                _ = vm.SearchCommand.ExecuteAsync(null);
            }
        }

        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is ChargeItemsViewModel vm)
            {
                _ = vm.SearchCommand.ExecuteAsync(null);
            }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("导出功能随报表模块统一提供（M4-D4-6，Excel/PDF）。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
