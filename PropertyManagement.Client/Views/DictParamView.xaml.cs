using System.Windows;
using System.Windows.Controls;
using PropertyManagement.Client.ViewModels;

namespace PropertyManagement.Client.Views
{
    public partial class DictParamView : UserControl
    {
        public DictParamView()
        {
            InitializeComponent();
        }

        /// <summary>行勾选写回：只读 DataGrid 中 CheckBox 的 IsChecked 绑定不会写回源，需在点击时显式同步（同欠费台账/账单工作台口径）。</summary>
        private void DictItemCheck_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox box && box.DataContext is DictItemRow row)
            {
                row.IsSelected = box.IsChecked == true;
            }
            var vm = DataContext as DictParamViewModel;
            if (vm != null) { vm.RefreshSelectAllState(); }
        }

        /// <summary>CHG-v1.1.2-26：计量变量行的勾选写回（只读 DataGrid 不自动回写源）。</summary>
        private void ChargeVariableCheck_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox box && box.DataContext is ChargeVariableRow row)
            {
                row.IsSelected = box.IsChecked == true;
            }
        }
    }
}
