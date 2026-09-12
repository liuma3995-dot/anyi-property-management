using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using PropertyManagement.Client.ViewModels;

namespace PropertyManagement.Client.Views
{
    public partial class AuditLogView : UserControl
    {
        public AuditLogView()
        {
            InitializeComponent();
        }

        /// <summary>行勾选写回（R13）：只读 DataGrid 中 CheckBox 的 IsChecked 绑定不会写回源，点击时显式同步。</summary>
        private void AuditCheck_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox box && box.DataContext is AuditLogRow row)
            {
                row.IsSelected = box.IsChecked == true;
            }
            var vm = DataContext as AuditLogViewModel;
            if (vm != null) { vm.RefreshSelectAllState(); }
        }
    }
}
