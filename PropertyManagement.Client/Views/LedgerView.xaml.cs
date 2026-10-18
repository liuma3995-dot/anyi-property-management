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

        /// <summary>
        /// CHG-v2.0.0-04（负责人 2026-10-11 反馈「搜索框需支持回车快捷查询」）：
        /// 与欠费台账同一写法 —— 回车直接执行「查询」，省去鼠标点击。
        /// </summary>
        private void KeywordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is LedgerViewModel vm)
            {
                _ = vm.SearchCommand.ExecuteAsync(null);
            }
        }

        /// <summary>行双击 → 与「查看」共用同一份流水来源明细。</summary>
        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var grid = sender as DataGrid;
            if (grid == null || grid.SelectedItem == null) { return; }
            var vm = DataContext as LedgerViewModel;
            if (vm == null) { return; }
            _ = vm.OpenDetailAsync(grid.SelectedItem as LedgerRow);
        }
    }
}
