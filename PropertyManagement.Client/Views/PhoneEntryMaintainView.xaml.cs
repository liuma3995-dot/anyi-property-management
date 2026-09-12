using System.Windows.Controls;
using System.Windows;
using PropertyManagement.Client.ViewModels;

namespace PropertyManagement.Client.Views
{
    public partial class PhoneEntryMaintainView : UserControl
    {
        public PhoneEntryMaintainView()
        {
            InitializeComponent();
        }

        /// <summary>勾选框点击 → 直接写入行 IsSelected（绕过只读 DataGrid 的双向绑定限制）。</summary>
        private void BatchCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is PhoneEntryRow row)
                row.IsSelected = cb.IsChecked == true;
        }
    }
}
