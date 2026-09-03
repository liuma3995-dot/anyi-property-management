using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PropertyManagement.Client.ViewModels;

namespace PropertyManagement.Client.Views
{
    /// <summary>收支明细流水页（PG-FIN-08）。</summary>
    public partial class LedgerView : UserControl
    {
        public LedgerView()
        {
            InitializeComponent();
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("流水导出请使用「财务报表」页的导出功能（Excel/PDF）。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>行双击 → 查看关联单据详情（T4F-7-1）。</summary>
        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var grid = sender as DataGrid;
            if (grid == null || grid.SelectedItem == null) { return; }
            var vm = DataContext as LedgerViewModel;
            if (vm == null) { return; }
            vm.OpenDetail(grid.SelectedItem as LedgerRow);
        }
    }
}