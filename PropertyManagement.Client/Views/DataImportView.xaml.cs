using System.Windows;
using System.Windows.Controls;
using PropertyManagement.Client.ViewModels;

namespace PropertyManagement.Client.Views
{
    public partial class DataImportView : UserControl
    {
        public DataImportView()
        {
            InitializeComponent();
        }

        /// <summary>行勾选写回（v1.1.0-⑤）：只读 DataGrid 中 CheckBox 的 IsChecked 绑定不会写回源，点击时显式同步。</summary>
        private void BatchCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox box && box.DataContext is ImportLogRow row)
            {
                row.IsSelected = box.IsChecked == true;
            }
            var vm = DataContext as DataImportViewModel;
            if (vm != null) { vm.RefreshSelectAllState(); }
        }
    }
}
