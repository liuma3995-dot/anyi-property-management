using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;
using PropertyManagement.Client.ViewModels;

namespace PropertyManagement.Client.Views
{
    public partial class DisputeListView : UserControl
    {
        public DisputeListView()
        {
            InitializeComponent();
        }

        /// <summary>双击行 → 跳转【处理与结案】（原型交互：点击行跳转）。</summary>
        private void CasesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var vm = DataContext as DisputeListViewModel;
            var row = CasesGrid.SelectedItem as DisputeRow;
            if (vm == null || row == null) return;
            if (vm.HandleCommand.CanExecute(row)) vm.HandleCommand.Execute(row);
        }

        /// <summary>批量删除行勾选：逐行回写模型，确保可单选（参考电话条目维护）。</summary>
        private void BatchCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox cb && cb.DataContext is DisputeRow row)
                row.IsChecked = cb.IsChecked == true;
        }
    }
}
