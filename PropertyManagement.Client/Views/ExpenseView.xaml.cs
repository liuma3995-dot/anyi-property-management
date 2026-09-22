using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PropertyManagement.Client.ViewModels;

namespace PropertyManagement.Client.Views
{
    /// <summary>支出登记页（PG-FIN-05）。</summary>
    public partial class ExpenseView : UserControl
    {
        public ExpenseView()
        {
            InitializeComponent();
        }


        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is ExpenseViewModel vm)
            {
                _ = vm.SearchCommand.ExecuteAsync(null);
            }
        }

        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is ExpenseViewModel vm)
            {
                _ = vm.SearchCommand.ExecuteAsync(null);
            }
        }

        /// <summary>行勾选写回（v1.1.0-⑤）：只读 DataGrid 中 CheckBox 的 IsChecked 绑定不会写回源，点击时显式同步。</summary>
        private void ExpenseCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox box && box.DataContext is ExpenseRow row)
            {
                row.IsSelected = box.IsChecked == true;
            }
            if (DataContext is ExpenseViewModel vm) { vm.RefreshSelectAllState(); }
        }
    }
}
